namespace AgencyOS.Finance.Rules

open System.Globalization

/// <summary>
/// Turning a refusal into a sentence somebody can act on.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the rules themselves so the rules stay total functions over
/// values and the wording stays changeable without touching them. Every message
/// names what was wrong and, where there is one, what to do instead.
/// </para>
/// <para>
/// None of these apologise, and none of them say "invalid". A person who has just
/// been stopped from posting a journal entry needs to know which side is short and
/// by how much.
/// </para>
/// </remarks>
module Messages =

    let private n (value: decimal) = value.ToString("N2", CultureInfo.InvariantCulture)

    /// <summary>Why a monetary value was refused.</summary>
    let describeMoney (error: MoneyError) =
        match error with
        | CurrencyMismatch(left, right) ->
            $"These amounts are in different currencies ({left} and {right}). "
            + "AgencyOS holds no exchange rates, so they cannot be combined without an explicit conversion."
        | MalformedCurrency currency ->
            $"'{currency}' is not shaped like a currency code. Three upper-case letters are expected."
        | NegativeAmount amount ->
            $"{n amount} is negative. Amounts are held as positive figures; "
            + "direction is carried by the side or the adjustment, not by the sign."
        | ZeroAmount -> "An amount of nothing records an event that did not happen."
        | Underflow(minuend, subtrahend) ->
            $"Subtracting {n subtrahend} from {n minuend} would leave less than nothing."
        | ExcessivePrecision(amount, places) ->
            $"{amount.ToString(CultureInfo.InvariantCulture)} carries more precision than this "
            + $"currency's {places} decimal place(s)."

    /// <summary>Why an allocation was refused.</summary>
    let describeAllocation (error: AllocationError) =
        match error with
        | AllocationCurrencyMismatch(allocation, target) ->
            $"The payment is in {allocation} and the receivable is in {target}. "
            + "A payment can only be applied to a receivable in the same currency."
        | AllocationNotPositive -> "An allocation of nothing applies nothing."
        | ExceedsPaymentFunds(requested, available) ->
            $"That would apply {n requested}, and only {n available} of this payment is unapplied."
        | ExceedsReceivableBalance(requested, outstanding) ->
            $"That would apply {n requested} against an outstanding balance of {n outstanding}. "
            + "The excess would have to be recorded as an overpayment rather than absorbed here."
        | TargetNotAllocatable reason -> $"This allocation is not possible: {reason}."

    /// <summary>Why a journal entry could not be posted.</summary>
    let describePosting (error: PostingError) =
        match error with
        | Unbalanced(debits, credits, currency) ->
            let difference = abs (debits - credits)

            $"Debits come to {n debits} {currency} and credits to {n credits} {currency}, "
            + $"a difference of {n difference}. A posted entry must balance."
        | NoLines -> "An entry with no lines asserts that something happened and records nothing about it."
        | ZeroLine _ -> "A line of nothing moves nothing. Remove it, or give it an amount."
        | MixedCurrency(first, second) ->
            $"This entry mixes {first} and {second}. "
            + "Debits and credits balance within one currency, so an entry carries one."
        | SingleSided side ->
            let missing = if side = EntrySide.Debit then "credits" else "debits"
            $"This entry has no {missing}. Double entry needs both sides."
        | NotPostable state ->
            match state with
            | JournalState.Posted -> "This entry has already been posted."
            | JournalState.Reversed -> "This entry has been reversed and cannot be posted again."
            | _ -> "This entry is not in a state that can be posted."
        | NotReversible state ->
            match state with
            | JournalState.JournalDraft ->
                "A draft has not been posted, so there is nothing to reverse. Discard it instead."
            | JournalState.Reversed -> "This entry has already been reversed."
            | _ -> "Only a posted entry can be reversed."

    /// <summary>Why a commission could not be worked out.</summary>
    let describeCommission (error: CommissionError) =
        match error with
        | RateOutOfRange rate ->
            $"A commission rate of {rate.ToString(CultureInfo.InvariantCulture)} per cent is outside "
            + $"what this build accepts (above zero, up to {Commission.MaximumCommissionRate}). "
            + "Rates are entered as percentages, so ten per cent is 10."
        | RuleIncomplete basis ->
            match basis with
            | CommissionBasis.FixedAmount ->
                "A fixed-amount rule needs an amount and a currency."
            | CommissionBasis.SpecificTerm ->
                "A rule against a specific term needs both a rate and the term it applies to."
            | _ -> "A rate rule needs a rate."
        | BasisCurrencyMismatch(rule, basis) ->
            $"The rule is denominated in {rule} and the basis in {basis}. "
            + "AgencyOS holds no exchange rates, so the commission cannot be worked out."
        | NoGoverningRule on ->
            // Precomputed: an interpolation hole cannot carry a quoted format
            // string inside an ordinary interpolated string.
            let day = on.ToString("O", CultureInfo.InvariantCulture)

            $"No commission rule was in force on {day}. "
            + "AgencyOS has no default rate and will not assume one."
        | AmbiguousRule(on, count) ->
            let day = on.ToString("O", CultureInfo.InvariantCulture)

            $"{count} commission rules were in force on {day}. "
            + "Which one governs is not something AgencyOS can decide; end one of them."
        | BasisUnknown ->
            "The amount this rate applies to is not known, so the commission cannot be worked out. "
            + "A contingent or backend obligation has no calculable basis until its amount is recorded."
