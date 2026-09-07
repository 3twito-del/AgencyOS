namespace AgencyOS.Deals.Rules

open System

/// <summary>
/// The shape a negotiated term's value takes.
/// </summary>
/// <remarks>
/// <para>
/// A discriminated union rather than a bag of nullable columns with a kind
/// discriminator, because this is where the illegal states actually live: a money
/// term with no currency, a percentage carrying text, a date term holding a
/// number. None of those can be constructed here.
/// </para>
/// <para>
/// Every numeric case carries <c>decimal</c>. Binary floating point is forbidden
/// for economics (CLAUDE.md section 5), and a rules kernel that used it would
/// hand rounding errors to every layer above.
/// </para>
/// </remarks>
type TermValue =
    /// An amount and the currency it is denominated in. Never a naked number.
    | MoneyValue of amount: decimal * currency: string
    /// A percentage, such as backend points.
    | PercentageValue of percentage: decimal
    /// A whole number, such as an episode count.
    | IntegerValue of whole: int64
    /// A non-percentage fractional number.
    | DecimalValue of number: decimal
    /// Free text, for a term whose value is genuinely prose.
    | TextValue of text: string
    /// A yes or no.
    | BooleanValue of flag: bool
    /// A business date, with no time of day anybody agreed to.
    | DateValue of date: DateOnly
    /// A length of time, in a stated unit.
    | DurationValue of length: int64 * durationUnit: int
    /// A number of something, in an optional stated unit.
    | CountValue of quantity: int64 * countUnit: int option

/// <summary>Why a term value was refused.</summary>
type TermError =
    /// The kind discriminator is not one this build knows.
    | UnknownValueKind of kind: int
    /// The value carried for the stated kind was missing.
    | MissingValue of kind: int * field: string
    /// A value was supplied that the stated kind has no use for.
    | ExtraneousValue of kind: int * field: string
    /// Money arrived without a currency, or with one that is not shaped like ISO 4217.
    | InvalidCurrency of currency: string
    /// A number fell outside what the kind can mean.
    | OutOfRange of kind: int * value: decimal * low: decimal * high: decimal
    /// A unit was required and absent, or supplied and meaningless.
    | InvalidUnit of kind: int
    /// Text was blank or longer than the column can hold.
    | InvalidText of reason: string

/// <summary>
/// One term as it crosses the language boundary.
/// </summary>
/// <remarks>
/// Flat and nullable on purpose. This is the only shape C# has to construct, and
/// it mirrors the persisted row so the translation at the boundary is mechanical
/// rather than clever. Parsing it into <see cref="TermValue"/> is where the
/// illegal combinations are rejected (ADR-0021).
/// </remarks>
[<CLIMutable>]
type TermInput =
    { /// The controlled term code. Meaningful to the catalog, opaque here.
      Code: int
      /// Which <see cref="TermValue"/> case this row claims to be.
      Kind: int
      /// Money amount.
      Amount: Nullable<decimal>
      /// ISO 4217 alphabetic code, for money.
      Currency: string
      /// Percentage or decimal value.
      Number: Nullable<decimal>
      /// Integer, duration length or count quantity.
      Whole: Nullable<int64>
      /// Text value.
      Text: string
      /// Boolean value.
      Flag: Nullable<bool>
      /// Date value.
      Date: Nullable<DateOnly>
      /// Duration or count unit.
      Unit: Nullable<int>
      /// Display order within the offer.
      Sequence: int }

/// <summary>Term value parsing and validation.</summary>
module TermValue =

    /// The persisted discriminator for each case.
    [<Literal>]
    let MoneyKind = 1

    [<Literal>]
    let PercentageKind = 2

    [<Literal>]
    let IntegerKind = 3

    [<Literal>]
    let DecimalKind = 4

    [<Literal>]
    let TextKind = 5

    [<Literal>]
    let BooleanKind = 6

    [<Literal>]
    let DateKind = 7

    [<Literal>]
    let DurationKind = 8

    [<Literal>]
    let CountKind = 9

    /// Every kind this build knows.
    let allKinds =
        [ MoneyKind
          PercentageKind
          IntegerKind
          DecimalKind
          TextKind
          BooleanKind
          DateKind
          DurationKind
          CountKind ]

    /// The persisted discriminator for a parsed value.
    let kindOf value =
        match value with
        | MoneyValue _ -> MoneyKind
        | PercentageValue _ -> PercentageKind
        | IntegerValue _ -> IntegerKind
        | DecimalValue _ -> DecimalKind
        | TextValue _ -> TextKind
        | BooleanValue _ -> BooleanKind
        | DateValue _ -> DateKind
        | DurationValue _ -> DurationKind
        | CountValue _ -> CountKind

    /// <summary>
    /// The widest percentage the kind itself will admit.
    /// </summary>
    /// <remarks>
    /// Generous rather than tight, because an escalation expressed as "110% of the
    /// prior season fee" is an ordinary term. The catalog narrows it per code -
    /// backend points are bounded to 100 there - so the kind check catches
    /// transposed digits without refusing real negotiations.
    /// </remarks>
    [<Literal>]
    let PercentageCeiling = 1000m

    /// Text terms are bounded to what the column holds.
    [<Literal>]
    let MaxTextLength = 2000

    /// Whether a string is shaped like an ISO 4217 alphabetic code.
    /// Membership of the actual code list is checked against reference data on the
    /// C# side; this only rejects things that could never be a code at all.
    let isCurrencyShaped (currency: string) =
        not (String.IsNullOrWhiteSpace currency)
        && currency.Length = 3
        && currency |> Seq.forall (fun c -> c >= 'A' && c <= 'Z')

    let private require condition error = if condition then Ok() else Error error

    /// Values that must be absent for the stated kind, named so the error says which.
    let private absent (input: TermInput) kind =
        let checks =
            [ "Amount", input.Amount.HasValue && kind <> MoneyKind
              "Currency", not (isNull input.Currency) && kind <> MoneyKind
              "Number", input.Number.HasValue && kind <> PercentageKind && kind <> DecimalKind
              "Whole",
              input.Whole.HasValue
              && kind <> IntegerKind
              && kind <> DurationKind
              && kind <> CountKind
              "Text", not (isNull input.Text) && kind <> TextKind
              "Flag", input.Flag.HasValue && kind <> BooleanKind
              "Date", input.Date.HasValue && kind <> DateKind
              "Unit", input.Unit.HasValue && kind <> DurationKind && kind <> CountKind ]

        match checks |> List.tryFind snd with
        | Some(field, _) -> Error(ExtraneousValue(kind, field))
        | None -> Ok()

    let private parseMoney (input: TermInput) =
        if not input.Amount.HasValue then
            Error(MissingValue(MoneyKind, "Amount"))
        elif not (isCurrencyShaped input.Currency) then
            Error(InvalidCurrency(if isNull input.Currency then "" else input.Currency))
        elif input.Amount.Value < 0m then
            Error(OutOfRange(MoneyKind, input.Amount.Value, 0m, Decimal.MaxValue))
        else
            Ok(MoneyValue(input.Amount.Value, input.Currency))

    let private parsePercentage (input: TermInput) =
        if not input.Number.HasValue then
            Error(MissingValue(PercentageKind, "Number"))
        elif input.Number.Value < 0m || input.Number.Value > PercentageCeiling then
            Error(OutOfRange(PercentageKind, input.Number.Value, 0m, PercentageCeiling))
        else
            Ok(PercentageValue input.Number.Value)

    let private parseText (input: TermInput) =
        if isNull input.Text || String.IsNullOrWhiteSpace input.Text then
            Error(InvalidText "A text term must not be blank.")
        elif input.Text.Length > MaxTextLength then
            Error(InvalidText $"A text term must be at most {MaxTextLength} characters.")
        else
            Ok(TextValue(input.Text.Trim()))

    let private parseDuration (input: TermInput) =
        if not input.Whole.HasValue then
            Error(MissingValue(DurationKind, "Whole"))
        elif not input.Unit.HasValue then
            Error(InvalidUnit DurationKind)
        elif input.Whole.Value <= 0L then
            Error(OutOfRange(DurationKind, decimal input.Whole.Value, 1m, decimal Int64.MaxValue))
        else
            Ok(DurationValue(input.Whole.Value, input.Unit.Value))

    let private parseCount (input: TermInput) =
        if not input.Whole.HasValue then
            Error(MissingValue(CountKind, "Whole"))
        elif input.Whole.Value < 0L then
            Error(OutOfRange(CountKind, decimal input.Whole.Value, 0m, decimal Int64.MaxValue))
        else
            Ok(CountValue(input.Whole.Value, if input.Unit.HasValue then Some input.Unit.Value else None))

    /// <summary>
    /// Turns a flat row into a value, or says exactly why it is not one.
    /// </summary>
    /// <remarks>
    /// Rejects both directions: a kind missing the value it needs, and a kind
    /// carrying a value it has no use for. The second matters as much as the
    /// first - a money term that also carries text is a row somebody edited
    /// carelessly, and letting it through means the text is silently lost the
    /// next time anything reads it.
    /// </remarks>
    let parse (input: TermInput) =
        match absent input input.Kind with
        | Error e -> Error e
        | Ok() ->
            match input.Kind with
            | MoneyKind -> parseMoney input
            | PercentageKind -> parsePercentage input
            | IntegerKind ->
                if input.Whole.HasValue then
                    Ok(IntegerValue input.Whole.Value)
                else
                    Error(MissingValue(IntegerKind, "Whole"))
            | DecimalKind ->
                if input.Number.HasValue then
                    Ok(DecimalValue input.Number.Value)
                else
                    Error(MissingValue(DecimalKind, "Number"))
            | TextKind -> parseText input
            | BooleanKind ->
                if input.Flag.HasValue then
                    Ok(BooleanValue input.Flag.Value)
                else
                    Error(MissingValue(BooleanKind, "Flag"))
            | DateKind ->
                if input.Date.HasValue then
                    Ok(DateValue input.Date.Value)
                else
                    Error(MissingValue(DateKind, "Date"))
            | DurationKind -> parseDuration input
            | CountKind -> parseCount input
            | other -> Error(UnknownValueKind other)

    /// Whether the row describes a value this build can make sense of.
    let isValid input =
        match parse input with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>
    /// The number two values of this kind can be compared on, when there is one.
    /// </summary>
    /// <remarks>
    /// Money returns its amount only when both sides share a currency, which the
    /// caller checks: M7 holds no exchange rates, and converting with an invented
    /// one would produce a comparison the agency might act on.
    /// </remarks>
    let comparableNumber value =
        match value with
        | MoneyValue(amount, _) -> Some amount
        | PercentageValue percentage -> Some percentage
        | DecimalValue number -> Some number
        | IntegerValue whole -> Some(decimal whole)
        | DurationValue(length, _) -> Some(decimal length)
        | CountValue(quantity, _) -> Some(decimal quantity)
        | TextValue _ | BooleanValue _ | DateValue _ -> None

    /// The currency a value is denominated in, when it is money.
    let currencyOf value =
        match value with
        | MoneyValue(_, currency) -> Some currency
        | PercentageValue _
        | IntegerValue _
        | DecimalValue _
        | TextValue _
        | BooleanValue _
        | DateValue _
        | DurationValue _
        | CountValue _ -> None

    /// <summary>
    /// Whether two values may be compared numerically at all.
    /// </summary>
    /// <remarks>
    /// Different kinds are never comparable, and neither are two amounts in
    /// different currencies. Saying so is the honest answer; producing a direction
    /// anyway would be arithmetic on a rate nobody supplied.
    /// </remarks>
    let areComparable left right =
        kindOf left = kindOf right
        && currencyOf left = currencyOf right
        && (comparableNumber left).IsSome
