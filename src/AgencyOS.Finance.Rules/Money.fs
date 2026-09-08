namespace AgencyOS.Finance.Rules

open System

/// <summary>
/// Why a monetary operation was refused.
/// </summary>
/// <remarks>
/// Every case names a specific wrong thing rather than a generic failure, because
/// the caller shows these to a person who has to fix something. "Two currencies"
/// is actionable; "invalid amount" is not.
/// </remarks>
type MoneyError =
    /// An arithmetic operation was asked to mix two currencies.
    | CurrencyMismatch of left: string * right: string
    /// A currency code was blank or not shaped like ISO 4217.
    | MalformedCurrency of currency: string
    /// An amount was negative where the operation requires a positive one.
    | NegativeAmount of amount: decimal
    /// An amount was zero where the operation requires more than nothing.
    | ZeroAmount
    /// A subtraction would have produced less than nothing.
    | Underflow of minuend: decimal * subtrahend: decimal
    /// An amount carried more precision than its currency has places for.
    | ExcessivePrecision of amount: decimal * places: int

/// <summary>
/// An amount and the currency it is denominated in.
/// </summary>
/// <remarks>
/// <para>
/// The currency is part of the value, not a field beside it, so there is no
/// arithmetic path in this kernel that can add dollars to euros. That is the
/// single most valuable thing F# buys the finance layer: the mistake is not
/// caught, it is unrepresentable.
/// </para>
/// <para>
/// <c>decimal</c> throughout. Binary floating point is forbidden for money
/// (CLAUDE.md section 5), and a finance kernel that used it would hand rounding
/// error to the ledger, which is the one place it can never be absorbed.
/// </para>
/// <para>
/// Amounts here are non-negative. Every signed quantity in M9 is expressed as a
/// positive amount plus an explicit direction - a journal side, a variance
/// direction - because a negative amount in a column is a sign convention
/// somebody has to remember, and sign conventions are how ledgers go wrong.
/// </para>
/// </remarks>
type Amount =
    { /// The figure, held to whatever precision the caller supplied.
      Value: decimal
      /// The ISO 4217 alphabetic code, upper case.
      Currency: string }

/// <summary>
/// How a computed figure is reduced to a storable one.
/// </summary>
/// <remarks>
/// One policy, stated once, applied everywhere. Individual handlers choosing their
/// own rounding is how a system accumulates cent drift that nobody can attribute
/// to a decision (ADR-0023).
/// </remarks>
module Rounding =

    /// <summary>
    /// The precision intermediate arithmetic is carried at.
    /// </summary>
    /// <remarks>
    /// Six places, which is more than any currency's minor units and enough that a
    /// rate applied to a large basis does not lose anything before the single
    /// rounding step. It is a working precision, never a storage precision.
    /// </remarks>
    [<Literal>]
    let IntermediatePlaces = 6

    /// <summary>
    /// The rounding mode, everywhere, without exception.
    /// </summary>
    /// <remarks>
    /// Banker's rounding, matching the M7 <c>Money</c> type so a figure that
    /// crossed from a contract term into a commission calculation and back does not
    /// change on the way. Away-from-zero would bias every half-cent in the agency's
    /// favour, which is a small lie repeated a great many times.
    /// </remarks>
    let Mode = MidpointRounding.ToEven

    /// <summary>How many decimal places a currency carries.</summary>
    /// <remarks>
    /// The zero-decimal and three-decimal currencies are the reason this is a
    /// function rather than the number two. Rounding JPY to two places invents
    /// money; rounding KWD to two loses it.
    /// </remarks>
    let placesFor (currency: string) =
        match currency with
        | "JPY" | "KRW" | "ISK" | "CLP" | "VND" -> 0
        | "KWD" | "BHD" | "OMR" | "JOD" | "TND" -> 3
        | _ -> 2

    /// <summary>Rounds a figure to a currency's own minor units.</summary>
    let toMinorUnits (currency: string) (value: decimal) =
        Decimal.Round(value, placesFor currency, Mode)

    /// <summary>Rounds a figure to the working precision.</summary>
    let toIntermediate (value: decimal) =
        Decimal.Round(value, IntermediatePlaces, Mode)

    /// <summary>Whether a figure is already expressible in a currency's minor units.</summary>
    let isStorable (currency: string) (value: decimal) =
        toMinorUnits currency value = value

/// <summary>
/// Currency-safe arithmetic.
/// </summary>
/// <remarks>
/// Every operation returns a <c>Result</c>. There is no operator overload that
/// throws, and no silent coercion: a caller that wants to add two amounts must
/// deal with the possibility that they are not the same kind of money.
/// </remarks>
module Amount =

    /// <summary>Whether a string is shaped like an ISO 4217 alphabetic code.</summary>
    /// <remarks>
    /// Shape only. Which codes AgencyOS actually accepts is the C# currency table's
    /// business, and duplicating it here would be a second list to keep in step.
    /// </remarks>
    let private wellFormed (currency: string) =
        not (String.IsNullOrWhiteSpace currency)
        && currency.Length = 3
        && currency |> Seq.forall Char.IsAsciiLetterUpper

    /// <summary>Creates an amount, refusing a malformed currency or a negative figure.</summary>
    let create (value: decimal) (currency: string) =
        if not (wellFormed currency) then Error(MalformedCurrency currency)
        elif value < 0m then Error(NegativeAmount value)
        else Ok { Value = value; Currency = currency }

    /// <summary>Creates an amount that must be more than nothing.</summary>
    /// <remarks>
    /// Used where zero is meaningless rather than merely unusual: an allocation of
    /// nothing, a journal line of nothing, a payment of nothing. Each of those is a
    /// row that claims something happened when nothing did.
    /// </remarks>
    let createPositive (value: decimal) (currency: string) =
        if value = 0m then Error ZeroAmount else create value currency

    /// <summary>Nothing, in a stated currency.</summary>
    let zero (currency: string) = { Value = 0m; Currency = currency }

    /// <summary>Whether two amounts are the same kind of money.</summary>
    let sameCurrency (left: Amount) (right: Amount) = left.Currency = right.Currency

    /// <summary>Adds two amounts of the same currency.</summary>
    let add (left: Amount) (right: Amount) =
        if not (sameCurrency left right) then
            Error(CurrencyMismatch(left.Currency, right.Currency))
        else
            Ok { left with Value = left.Value + right.Value }

    /// <summary>
    /// Subtracts one amount from another, refusing a result below zero.
    /// </summary>
    /// <remarks>
    /// The refusal is the point. A receivable balance that could go negative is a
    /// receivable that has been over-allocated, and returning a negative number
    /// would hide the error inside a plausible-looking total.
    /// </remarks>
    let subtract (left: Amount) (right: Amount) =
        if not (sameCurrency left right) then
            Error(CurrencyMismatch(left.Currency, right.Currency))
        elif right.Value > left.Value then
            Error(Underflow(left.Value, right.Value))
        else
            Ok { left with Value = left.Value - right.Value }

    /// <summary>Subtracts without refusing, reporting which way the difference runs.</summary>
    /// <remarks>
    /// For reconciliation, where an overpayment is a real and ordinary outcome
    /// rather than an error. The direction is returned rather than folded into a
    /// sign, so no caller has to remember a convention.
    /// </remarks>
    let difference (expected: Amount) (actual: Amount) =
        if not (sameCurrency expected actual) then
            Error(CurrencyMismatch(expected.Currency, actual.Currency))
        else
            let delta = expected.Value - actual.Value
            Ok(abs delta, sign delta)

    /// <summary>Sums amounts, refusing a mixed-currency sequence.</summary>
    /// <remarks>
    /// An empty sequence has no currency and therefore no answer, so the caller
    /// supplies one. Returning a zero in some default currency would be the system
    /// choosing dollars on the caller's behalf.
    /// </remarks>
    let sum (currency: string) (amounts: Amount seq) =
        amounts
        |> Seq.fold
            (fun state amount ->
                match state with
                | Error _ -> state
                | Ok total -> add total amount)
            (Ok(zero currency))

    /// <summary>Applies a rate, carrying the working precision and rounding once.</summary>
    /// <remarks>
    /// The whole of the rounding policy for commission lives here. The product is
    /// computed at full precision, held at the working precision, and reduced to
    /// the currency's minor units exactly once. A caller that multiplied and
    /// rounded twice would drift, which is why no caller multiplies.
    /// </remarks>
    let applyRate (rate: decimal) (basis: Amount) =
        if rate < 0m then
            Error(NegativeAmount rate)
        else
            let raw = basis.Value * rate / 100m
            let rounded = Rounding.toMinorUnits basis.Currency (Rounding.toIntermediate raw)
            Ok { basis with Value = rounded }

    /// <summary>Whether an amount is expressible in its own currency's minor units.</summary>
    let isStorable (amount: Amount) = Rounding.isStorable amount.Currency amount.Value

    /// <summary>Reduces an amount to its currency's minor units.</summary>
    let normalise (amount: Amount) =
        { amount with Value = Rounding.toMinorUnits amount.Currency amount.Value }

    /// <summary>Whether an amount is nothing.</summary>
    let isZero (amount: Amount) = amount.Value = 0m

    /// <summary>Compares two amounts of the same currency.</summary>
    let compare (left: Amount) (right: Amount) =
        if not (sameCurrency left right) then
            Error(CurrencyMismatch(left.Currency, right.Currency))
        else
            Ok(compare left.Value right.Value)

/// <summary>
/// One money value as it crosses the language boundary.
/// </summary>
/// <remarks>
/// Flat and mutable so C# can construct it without F#-shaped ceremony, exactly as
/// the M7 kernel's term input is. The discriminated unions above never leave this
/// assembly; what crosses is a decimal and a string (ADR-0023).
/// </remarks>
[<CLIMutable>]
type MoneyInput =
    { /// The figure.
      Amount: decimal
      /// The ISO 4217 alphabetic code.
      Currency: string }

/// <summary>Reading and writing the boundary shape.</summary>
module MoneyInput =

    /// <summary>Parses a boundary value into a currency-safe amount.</summary>
    let parse (input: MoneyInput) = Amount.create input.Amount input.Currency

    /// <summary>Parses a boundary value that must be more than nothing.</summary>
    let parsePositive (input: MoneyInput) = Amount.createPositive input.Amount input.Currency

    /// <summary>Writes an amount back out to the boundary shape.</summary>
    let write (amount: Amount) : MoneyInput =
        { Amount = amount.Value; Currency = amount.Currency }
