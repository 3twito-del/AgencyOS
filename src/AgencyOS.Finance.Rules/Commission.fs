namespace AgencyOS.Finance.Rules

open System

/// <summary>
/// What a commission rate is applied to.
/// </summary>
/// <remarks>
/// Three concrete bases and nothing speculative. There is deliberately no
/// universal entertainment-industry commission formula here, because there is no
/// universal entertainment-industry commission formula: rates and bases differ by
/// engagement, by guild, by jurisdiction and by what the parties actually agreed.
/// The system records the rule that applied rather than assuming one (ADR-0023).
/// </remarks>
type CommissionBasis =
    /// Every compensation term the contract records as payable to the client.
    | GrossCompensation = 1
    /// One named term only, such as the episodic rate but not the signing payment.
    | SpecificTerm = 2
    /// A stated sum, not a rate against anything.
    | FixedAmount = 3

/// <summary>Why a commission calculation was refused.</summary>
type CommissionError =
    /// The rate is outside anything a commission can plausibly be.
    | RateOutOfRange of rate: decimal
    /// A percentage rule with no rate, or a fixed rule with no amount.
    | RuleIncomplete of basis: CommissionBasis
    /// The rule and the basis are denominated differently.
    | BasisCurrencyMismatch of rule: string * basis: string
    /// No rule was in force when the transaction occurred.
    | NoGoverningRule of on: DateOnly
    /// More than one rule was in force, so which governs is not a question the system can answer.
    | AmbiguousRule of on: DateOnly * count: int
    /// The basis for a rate rule could not be established.
    | BasisUnknown

/// <summary>
/// One commission rule as it crosses the language boundary.
/// </summary>
/// <remarks>
/// Effective-dated, because a commission rate is a term of a representation
/// relationship and relationships are renegotiated. Which rule governs a
/// transaction is decided by when the transaction happened, not by which rule is
/// current when somebody happens to run the calculation.
/// </remarks>
[<CLIMutable>]
type CommissionRuleInput =
    { Id: Guid
      /// GrossCompensation, SpecificTerm or FixedAmount, as the enum's integer value.
      Basis: int
      /// The percentage, for a rate rule. Null for a fixed rule.
      RatePercent: Nullable<decimal>
      /// The sum, for a fixed rule. Null for a rate rule.
      FixedAmount: Nullable<decimal>
      /// The currency a fixed rule is denominated in. Null for a rate rule.
      Currency: string
      /// The term a SpecificTerm rule applies to. Null otherwise.
      TermCode: Nullable<int>
      /// When the rule started governing.
      EffectiveFrom: DateOnly
      /// When it stopped, or null while it still governs.
      EffectiveTo: Nullable<DateOnly>
      /// The contract it is confined to, or null when it governs the representation broadly.
      ContractId: Nullable<Guid> }

/// <summary>
/// Working out what the agency is entitled to.
/// </summary>
/// <remarks>
/// <para>
/// The three quantities this module keeps apart are entitlement, collection and
/// the difference between them. An agency entitled to a hundred thousand against a
/// contract that has paid four hundred of a million has collected forty thousand,
/// and a system that reported either number as the other would be wrong in a way
/// somebody would act on.
/// </para>
/// <para>
/// Nothing here reads a clock or a database. The governing rule is selected from
/// candidates the caller supplies, by the date the caller supplies, so the same
/// transaction produces the same answer next year (ADR-0023).
/// </para>
/// </remarks>
module Commission =

    /// <summary>
    /// The upper bound on a commission rate.
    /// </summary>
    /// <remarks>
    /// Fifty per cent, which is far above any real agency commission and far below
    /// a typing error. The bound exists to catch 1000 entered for 10.00, not to
    /// express a view about what a fair rate is (ADR-0023).
    /// </remarks>
    // A plain binding rather than a [<Literal>]: F# emits a decimal literal
    // through DecimalConstantAttribute, and doing that inside a module
    // produces a static initializer the runtime rejects at load time.
    let MaximumCommissionRate = 50m

    /// <summary>The basis an integer denotes, or nothing when it denotes neither.</summary>
    let basisOf (value: int) =
        match value with
        | 1 -> Some CommissionBasis.GrossCompensation
        | 2 -> Some CommissionBasis.SpecificTerm
        | 3 -> Some CommissionBasis.FixedAmount
        | _ -> None

    /// <summary>Whether a rate is one a commission could plausibly carry.</summary>
    let isRateValid (rate: decimal) = rate > 0m && rate <= MaximumCommissionRate

    /// <summary>Whether a rule is internally complete.</summary>
    let validate (rule: CommissionRuleInput) =
        match basisOf rule.Basis with
        | None -> Error(RuleIncomplete CommissionBasis.GrossCompensation)
        | Some CommissionBasis.FixedAmount ->
            if not rule.FixedAmount.HasValue || String.IsNullOrWhiteSpace rule.Currency then
                Error(RuleIncomplete CommissionBasis.FixedAmount)
            elif rule.FixedAmount.Value <= 0m then
                Error(RuleIncomplete CommissionBasis.FixedAmount)
            else
                Ok rule
        | Some basis ->
            if not rule.RatePercent.HasValue then Error(RuleIncomplete basis)
            elif not (isRateValid rule.RatePercent.Value) then
                Error(RateOutOfRange rule.RatePercent.Value)
            elif basis = CommissionBasis.SpecificTerm && not rule.TermCode.HasValue then
                Error(RuleIncomplete basis)
            else
                Ok rule

    /// <summary>Whether a rule was in force on a date.</summary>
    let governsOn (on: DateOnly) (rule: CommissionRuleInput) =
        rule.EffectiveFrom <= on
        && (not rule.EffectiveTo.HasValue || on <= rule.EffectiveTo.Value)

    /// <summary>
    /// The rule that governed a transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A contract-specific rule beats a representation-wide one, because the
    /// narrower statement is the more deliberate. Beyond that, two rules in force
    /// at once is an <em>ambiguity the system refuses to resolve</em>: picking the
    /// higher would favour the agency, picking the newer would favour whoever
    /// edited last, and picking either silently would hide a data problem that a
    /// person needs to fix.
    /// </para>
    /// <para>
    /// The absence of any rule is likewise refused rather than defaulted. There is
    /// no house rate.
    /// </para>
    /// </remarks>
    let governing (on: DateOnly) (contractId: Nullable<Guid>) (rules: CommissionRuleInput seq) =
        let inForce = rules |> Seq.filter (governsOn on) |> Array.ofSeq

        let specific =
            inForce
            |> Array.filter (fun rule ->
                rule.ContractId.HasValue
                && contractId.HasValue
                && rule.ContractId.Value = contractId.Value)

        let candidates =
            if specific.Length > 0 then
                specific
            else
                inForce |> Array.filter (fun rule -> not rule.ContractId.HasValue)

        match candidates.Length with
        | 0 -> Error(NoGoverningRule on)
        | 1 -> Ok candidates.[0]
        | count -> Error(AmbiguousRule(on, count))

    /// <summary>
    /// What the agency is entitled to against a basis.
    /// </summary>
    /// <remarks>
    /// A fixed rule ignores the basis entirely and returns its own sum, in its own
    /// currency. A rate rule applies the rate through <c>Amount.applyRate</c>, so
    /// the single rounding step is the same one every other calculation uses.
    /// </remarks>
    let entitlement (rule: CommissionRuleInput) (basis: Amount option) =
        match validate rule with
        | Error error -> Error error
        | Ok rule ->
            match basisOf rule.Basis with
            | Some CommissionBasis.FixedAmount ->
                match Amount.create rule.FixedAmount.Value rule.Currency with
                | Ok amount -> Ok amount
                | Error _ -> Error(RuleIncomplete CommissionBasis.FixedAmount)
            | Some _ ->
                match basis with
                | None -> Error BasisUnknown
                | Some basis ->
                    match Amount.applyRate rule.RatePercent.Value basis with
                    | Ok amount -> Ok amount
                    | Error _ -> Error(BasisCurrencyMismatch(rule.Currency, basis.Currency))
            | None -> Error(RuleIncomplete CommissionBasis.GrossCompensation)

    /// <summary>
    /// What the agency has actually earned in cash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rate applied to what was collected, not to what was promised. This is
    /// the number that becomes revenue when the ledger posts it, and it is
    /// deliberately a different calculation from <c>entitlement</c> rather than a
    /// proportion of it, so a rounding difference between the two cannot compound.
    /// </para>
    /// <para>
    /// It is capped at the entitlement. Collecting more than the whole obligation
    /// cannot earn more than the whole commission; the excess is an overpayment,
    /// which is a different fact with a different home.
    /// </para>
    /// </remarks>
    let collected (rule: CommissionRuleInput) (entitled: Amount) (collectedBasis: Amount) =
        match basisOf rule.Basis with
        | Some CommissionBasis.FixedAmount ->
            // A fixed fee is earned as cash arrives, up to the fee. The rule's own
            // currency is checked as well as the two amounts', because a fee stated
            // in euros is not earned by dollars arriving.
            if rule.Currency <> entitled.Currency then
                Error(BasisCurrencyMismatch(rule.Currency, entitled.Currency))
            elif collectedBasis.Currency <> entitled.Currency then
                Error(BasisCurrencyMismatch(entitled.Currency, collectedBasis.Currency))
            elif collectedBasis.Value >= entitled.Value then
                Ok entitled
            else
                Ok collectedBasis
        | Some _ ->
            if not rule.RatePercent.HasValue then
                Error(RuleIncomplete CommissionBasis.GrossCompensation)
            else
                match Amount.applyRate rule.RatePercent.Value collectedBasis with
                | Error _ -> Error(BasisCurrencyMismatch(entitled.Currency, collectedBasis.Currency))
                | Ok earned ->
                    if earned.Currency <> entitled.Currency then
                        Error(BasisCurrencyMismatch(entitled.Currency, earned.Currency))
                    elif earned.Value > entitled.Value then
                        Ok entitled
                    else
                        Ok earned
        | None -> Error(RuleIncomplete CommissionBasis.GrossCompensation)

    /// <summary>
    /// What is still to come.
    /// </summary>
    /// <remarks>
    /// Entitlement less what has been collected and less any adjustment. Floored at
    /// zero: an adjustment larger than the entitlement means the agency owes
    /// something back, which is a payable rather than a negative receivable.
    /// </remarks>
    let outstanding (entitled: Amount) (collectedSoFar: Amount) (adjustments: Amount) =
        match Amount.subtract entitled collectedSoFar with
        | Error _ -> Ok(Amount.zero entitled.Currency)
        | Ok remaining ->
            match Amount.subtract remaining adjustments with
            | Error _ -> Ok(Amount.zero entitled.Currency)
            | Ok result -> Ok result
