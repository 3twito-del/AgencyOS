namespace AgencyOS.Finance.Rules

open System
open System.Runtime.CompilerServices

/// <summary>
/// The one place C# talks to the finance kernel.
/// </summary>
/// <remarks>
/// <para>
/// The same boundary discipline the M7 deal kernel established: everything
/// crossing here is a primitive, a plain array or a <c>[&lt;CLIMutable&gt;]</c>
/// record. The discriminated unions above - <c>Amount</c> with its currency inside
/// it, the error types, the result values - stay in this assembly, because they are
/// what makes the arithmetic safe and they are awkward to hold from C# (ADR-0023).
/// </para>
/// <para>
/// Money crosses as a <c>decimal</c> and a <c>string</c>, or as a
/// <c>MoneyInput</c>. It never crosses as a bare number: the currency travelling
/// separately from the amount is exactly the mistake this kernel exists to prevent,
/// and letting it happen at the boundary would give the rest of the system every
/// opportunity to make it.
/// </para>
/// <para>
/// Failure is reported as a message, not as an exception and not as an F#
/// <c>Result</c>. A null message means the operation was legal; a non-null one is a
/// sentence a person can act on.
/// </para>
/// </remarks>
[<AbstractClass; Sealed; Extension>]
type FinanceRules private () =

    // ------------------------------------------------------------------ money

    /// <summary>The decimal places a currency carries.</summary>
    static member MinorUnits(currency: string) = Rounding.placesFor currency

    /// <summary>Rounds a figure to a currency's own minor units, once.</summary>
    static member RoundToMinorUnits(value: decimal, currency: string) =
        Rounding.toMinorUnits currency value

    /// <summary>The working precision intermediate arithmetic is carried at.</summary>
    static member IntermediatePrecision = Rounding.IntermediatePlaces

    /// <summary>Whether a figure can be stored in a currency without losing anything.</summary>
    static member IsStorable(value: decimal, currency: string) = Rounding.isStorable currency value

    /// <summary>Whether an amount is well formed: known shape, not negative.</summary>
    static member IsMoneyValid(value: decimal, currency: string) =
        match Amount.create value currency with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>Why an amount was refused, or null when it was not.</summary>
    static member DescribeMoneyProblem(value: decimal, currency: string) =
        match Amount.create value currency with
        | Ok _ -> null
        | Error error -> Messages.describeMoney error

    /// <summary>
    /// Adds two amounts, returning the total.
    /// </summary>
    /// <remarks>
    /// Throws nothing and returns nothing useful on a currency mismatch; callers
    /// that need the reason ask <c>DescribeAdditionProblem</c> first. The C# domain
    /// checks currency compatibility before it ever gets here, so this is the second
    /// line rather than the first.
    /// </remarks>
    static member TryAdd(left: decimal, right: decimal, leftCurrency: string, rightCurrency: string) =
        match Amount.create left leftCurrency, Amount.create right rightCurrency with
        | Ok left, Ok right ->
            match Amount.add left right with
            | Ok total -> Nullable total.Value
            | Error _ -> Nullable()
        | _ -> Nullable()

    /// <summary>Subtracts, refusing to go below zero.</summary>
    static member TrySubtract
        (
            left: decimal,
            right: decimal,
            leftCurrency: string,
            rightCurrency: string
        ) =
        match Amount.create left leftCurrency, Amount.create right rightCurrency with
        | Ok left, Ok right ->
            match Amount.subtract left right with
            | Ok total -> Nullable total.Value
            | Error _ -> Nullable()
        | _ -> Nullable()

    /// <summary>Whether two currencies may be combined without an explicit conversion.</summary>
    /// <remarks>
    /// Only when they are the same. AgencyOS holds no exchange rates, so there is no
    /// other answer it is entitled to give.
    /// </remarks>
    static member AreCurrenciesCompatible(left: string, right: string) =
        not (String.IsNullOrWhiteSpace left) && left = right

    /// <summary>Applies a percentage rate, rounding exactly once.</summary>
    static member ApplyRate(rate: decimal, basis: decimal, currency: string) =
        match Amount.create basis currency with
        | Error _ -> Nullable()
        | Ok basis ->
            match Amount.applyRate rate basis with
            | Ok result -> Nullable result.Value
            | Error _ -> Nullable()

    /// <summary>Sums same-currency amounts, or returns nothing when they are mixed.</summary>
    static member TrySum(values: MoneyInput[], currency: string) =
        let parsed = values |> Array.map MoneyInput.parse

        if parsed |> Array.exists (fun result -> Result.isError result) then
            Nullable()
        else
            let amounts =
                parsed
                |> Array.choose (function
                    | Ok amount -> Some amount
                    | Error _ -> None)

            match Amount.sum currency amounts with
            | Ok total -> Nullable total.Value
            | Error _ -> Nullable()

    // ------------------------------------------------------------- allocation

    /// <summary>What a receivable still owes.</summary>
    static member Outstanding(receivable: ReceivableInput) = Allocation.outstanding receivable

    /// <summary>Where a receivable stands, as the persisted status value.</summary>
    static member ReceivableState(receivable: ReceivableInput) =
        int (Allocation.stateOf receivable)

    /// <summary>Whether a receivable can still take money.</summary>
    static member AcceptsAllocation(receivable: ReceivableInput) =
        Allocation.acceptsAllocation receivable

    /// <summary>Whether a receivable is past its date with something still owed.</summary>
    static member IsOverdue(receivable: ReceivableInput, today: DateOnly) =
        Allocation.isOverdue today receivable

    /// <summary>How much of a payment has not been applied to anything.</summary>
    static member Unapplied(paymentAmount: decimal, lines: AllocationInput[]) =
        Allocation.unapplied paymentAmount lines

    /// <summary>Whether one more allocation is legal.</summary>
    static member IsAllocationValid
        (
            paymentAmount: decimal,
            paymentCurrency: string,
            existing: AllocationInput[],
            receivable: ReceivableInput,
            requested: decimal
        ) =
        match Allocation.validate paymentAmount paymentCurrency existing receivable requested with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>Why an allocation was refused, or null when it was not.</summary>
    static member DescribeAllocationProblem
        (
            paymentAmount: decimal,
            paymentCurrency: string,
            existing: AllocationInput[],
            receivable: ReceivableInput,
            requested: decimal
        ) =
        match Allocation.validate paymentAmount paymentCurrency existing receivable requested with
        | Ok _ -> null
        | Error error -> Messages.describeAllocation error

    /// <summary>Splits a figure across weights, losing nothing to rounding.</summary>
    static member Apportion(currency: string, total: decimal, weights: decimal[]) =
        match Allocation.apportion currency total weights with
        | Ok shares -> shares
        | Error _ -> Array.empty

    // ----------------------------------------------------------------- ledger

    /// <summary>Every journal state this build knows, as persisted values.</summary>
    static member KnownJournalStates() =
        [| int JournalState.JournalDraft; int JournalState.Posted; int JournalState.Reversed |]

    /// <summary>Every journal trigger this build knows, as persisted values.</summary>
    static member KnownJournalTriggers() =
        [| int JournalTrigger.Post; int JournalTrigger.Reverse; int JournalTrigger.DiscardDraft |]

    /// <summary>Every account category this build knows, as persisted values.</summary>
    static member KnownAccountCategories() =
        [| int AccountCategory.Asset
           int AccountCategory.Liability
           int AccountCategory.Equity
           int AccountCategory.Revenue
           int AccountCategory.Expense
           int AccountCategory.Clearing |]

    /// <summary>The state a journal entry reaches, or 0 when the trigger is illegal.</summary>
    static member NextJournalState(state: int, trigger: int) =
        let state =
            match state with
            | 1 -> Some JournalState.JournalDraft
            | 2 -> Some JournalState.Posted
            | 3 -> Some JournalState.Reversed
            | _ -> None

        let trigger =
            match trigger with
            | 1 -> Some JournalTrigger.Post
            | 2 -> Some JournalTrigger.Reverse
            | 3 -> Some JournalTrigger.DiscardDraft
            | _ -> None

        match state, trigger with
        | Some state, Some trigger ->
            match Journal.next state trigger with
            | Some next -> int next
            | None -> 0
        | _ -> 0

    /// <summary>Whether a journal trigger is legal from a state.</summary>
    static member JournalPermits(state: int, trigger: int) =
        FinanceRules.NextJournalState(state, trigger) <> 0

    /// <summary>Whether a journal entry can still be edited.</summary>
    static member IsJournalEditable(state: int) = state = int JournalState.JournalDraft

    /// <summary>Whether a journal entry counts towards account balances.</summary>
    static member AffectsBalance(state: int) =
        state = int JournalState.Posted || state = int JournalState.Reversed

    /// <summary>What the debits of a set of lines come to.</summary>
    static member Debits(lines: JournalLineInput[]) = Journal.debits lines

    /// <summary>What the credits come to.</summary>
    static member Credits(lines: JournalLineInput[]) = Journal.credits lines

    /// <summary>Whether a set of lines is fit to post.</summary>
    static member IsBalanced(lines: JournalLineInput[]) = Journal.isBalanced lines

    /// <summary>Why a set of lines cannot be posted, or null when it can.</summary>
    static member DescribePostingProblem(lines: JournalLineInput[]) =
        match Journal.validate lines with
        | Ok _ -> null
        | Error error -> Messages.describePosting error

    /// <summary>The lines that undo a posted entry.</summary>
    static member ReverseLines(lines: JournalLineInput[]) = Journal.reverseLines lines

    /// <summary>How a line moves an account's balance, signed by the account's nature.</summary>
    static member SignedAmount(category: int, side: int, amount: decimal) =
        let category =
            match category with
            | 1 -> AccountCategory.Asset
            | 2 -> AccountCategory.Liability
            | 3 -> AccountCategory.Equity
            | 4 -> AccountCategory.Revenue
            | 5 -> AccountCategory.Expense
            | _ -> AccountCategory.Clearing

        match Journal.sideOf side with
        | Some side -> Journal.signedFor category side amount
        | None -> 0m

    // ------------------------------------------------------------- commission

    /// <summary>Every commission basis this build knows, as persisted values.</summary>
    static member KnownCommissionBases() =
        [| int CommissionBasis.GrossCompensation
           int CommissionBasis.SpecificTerm
           int CommissionBasis.FixedAmount |]

    /// <summary>The highest rate this build will accept.</summary>
    static member MaximumRate = Commission.MaximumCommissionRate

    /// <summary>Whether a rate is one a commission could plausibly carry.</summary>
    static member IsRateValid(rate: decimal) = Commission.isRateValid rate

    /// <summary>Whether a rule is internally complete.</summary>
    static member IsRuleValid(rule: CommissionRuleInput) =
        match Commission.validate rule with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>Why a rule was refused, or null when it was not.</summary>
    static member DescribeRuleProblem(rule: CommissionRuleInput) =
        match Commission.validate rule with
        | Ok _ -> null
        | Error error -> Messages.describeCommission error

    /// <summary>Whether a rule was in force on a date.</summary>
    static member RuleGovernsOn(rule: CommissionRuleInput, on: DateOnly) =
        Commission.governsOn on rule

    /// <summary>
    /// The identifier of the rule that governed a transaction, or empty when none did.
    /// </summary>
    /// <remarks>
    /// Empty for both "no rule" and "more than one rule", which are different
    /// problems with the same consequence: the system cannot say what the agency was
    /// entitled to. <c>DescribeGoverningProblem</c> distinguishes them.
    /// </remarks>
    static member GoverningRule(rules: CommissionRuleInput[], on: DateOnly, contractId: Nullable<Guid>) =
        match Commission.governing on contractId rules with
        | Ok rule -> rule.Id
        | Error _ -> Guid.Empty

    /// <summary>Why no single rule governs, or null when one does.</summary>
    static member DescribeGoverningProblem
        (
            rules: CommissionRuleInput[],
            on: DateOnly,
            contractId: Nullable<Guid>
        ) =
        match Commission.governing on contractId rules with
        | Ok _ -> null
        | Error error -> Messages.describeCommission error

    /// <summary>What the agency is entitled to against a basis, or null when it cannot be worked out.</summary>
    static member Entitlement(rule: CommissionRuleInput, basis: decimal, currency: string) =
        let basis =
            match Amount.create basis currency with
            | Ok amount -> Some amount
            | Error _ -> None

        match Commission.entitlement rule basis with
        | Ok amount -> Nullable amount.Value
        | Error _ -> Nullable()

    /// <summary>The currency an entitlement is denominated in, or null when it cannot be worked out.</summary>
    static member EntitlementCurrency(rule: CommissionRuleInput, basis: decimal, currency: string) =
        let basis =
            match Amount.create basis currency with
            | Ok amount -> Some amount
            | Error _ -> None

        match Commission.entitlement rule basis with
        | Ok amount -> amount.Currency
        | Error _ -> null

    /// <summary>Why an entitlement could not be worked out, or null when it could.</summary>
    static member DescribeEntitlementProblem(rule: CommissionRuleInput, basis: decimal, currency: string) =
        let basis =
            match Amount.create basis currency with
            | Ok amount -> Some amount
            | Error _ -> None

        match Commission.entitlement rule basis with
        | Ok _ -> null
        | Error error -> Messages.describeCommission error

    /// <summary>
    /// What the agency has earned in cash against what has actually been collected.
    /// </summary>
    static member Collected
        (
            rule: CommissionRuleInput,
            entitled: decimal,
            collectedBasis: decimal,
            currency: string
        ) =
        match Amount.create entitled currency, Amount.create collectedBasis currency with
        | Ok entitled, Ok collected ->
            match Commission.collected rule entitled collected with
            | Ok amount -> Nullable amount.Value
            | Error _ -> Nullable()
        | _ -> Nullable()

    /// <summary>What is still to come on a commission.</summary>
    static member OutstandingCommission
        (
            entitled: decimal,
            collectedSoFar: decimal,
            adjustments: decimal,
            currency: string
        ) =
        match
            Amount.create entitled currency,
            Amount.create collectedSoFar currency,
            Amount.create adjustments currency
        with
        | Ok entitled, Ok collected, Ok adjusted ->
            match Commission.outstanding entitled collected adjusted with
            | Ok amount -> Nullable amount.Value
            | Error _ -> Nullable()
        | _ -> Nullable()

    // --------------------------------------------------------- reconciliation

    /// <summary>What has been accounted for, by any means.</summary>
    static member Accounted(input: ReconciliationInput) = Reconciliation.accounted input

    /// <summary>The size of the gap between expected and accounted for.</summary>
    static member Variance(input: ReconciliationInput) =
        let magnitude, _ = Reconciliation.variance input
        magnitude

    /// <summary>Which way the gap runs: 1 short, -1 over, 0 exact.</summary>
    static member VarianceDirection(input: ReconciliationInput) =
        let _, direction = Reconciliation.variance input
        direction

    /// <summary>Where a reconciliation stands, as the persisted value.</summary>
    static member ReconciliationOutcome(input: ReconciliationInput) =
        int (Reconciliation.outcome input)

    /// <summary>Whether everything is explained.</summary>
    static member IsReconciled(input: ReconciliationInput) = Reconciliation.isReconciled input

    /// <summary>A sentence stating the arithmetic, for a person to read.</summary>
    static member DescribeReconciliation(input: ReconciliationInput) = Reconciliation.describe input
