using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using AgencyOS.Finance.Rules;

namespace AgencyOS.Domain.Finance;

/// <summary>Opaque, immutable identifier for a <see cref="CommissionRule"/>.</summary>
public readonly record struct CommissionRuleId(Guid Value)
{
    public static CommissionRuleId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for a <see cref="CommissionEntitlement"/>.</summary>
public readonly record struct CommissionEntitlementId(Guid Value)
{
    public static CommissionEntitlementId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What a commission rate is applied to.
/// </summary>
/// <remarks>
/// Values match <see cref="AgencyOS.Finance.Rules.CommissionBasis"/>. Three
/// concrete bases and nothing speculative: there is no universal
/// entertainment-industry commission formula, so the system records the rule that
/// applied rather than assuming one (ADR-0023).
/// </remarks>
public enum CommissionBasisKind
{
    /// <summary>Every compensation term the contract records as payable to the client.</summary>
    GrossCompensation = 1,

    /// <summary>One named term only.</summary>
    SpecificTerm = 2,

    /// <summary>A stated sum, not a rate against anything.</summary>
    FixedAmount = 3,
}

/// <summary>Where a commission entitlement stands.</summary>
/// <remarks>
/// Deliberately short. Whether it has been collected is arithmetic over the
/// payments that arrived, not a status somebody sets, so there is no
/// <c>Collected</c> member here (ADR-0023).
/// </remarks>
public enum CommissionEntitlementStatus
{
    /// <summary>Worked out from a rule against a basis.</summary>
    Calculated = 1,

    /// <summary>Superseded by a recalculation, or released.</summary>
    Superseded = 2,

    /// <summary>Withdrawn: the underlying obligation went away.</summary>
    Cancelled = 3,
}

/// <summary>Why a commission entitlement changed after it was calculated.</summary>
public enum CommissionAdjustmentKind
{
    /// <summary>The rate applied was wrong and has been corrected.</summary>
    RateCorrection = 1,

    /// <summary>The parties settled for less.</summary>
    Settlement = 2,

    /// <summary>The agency waived part of it.</summary>
    Waiver = 3,

    /// <summary>The underlying compensation changed.</summary>
    BasisCorrection = 4,

    /// <summary>Something the vocabulary does not name.</summary>
    Other = 99,
}

/// <summary>
/// The rule that decides what the agency is entitled to.
/// </summary>
/// <remarks>
/// <para>
/// Effective-dated, because a commission rate is a term of a representation
/// relationship and relationships are renegotiated. Which rule governs a
/// transaction is decided by <em>when the transaction happened</em>, never by which
/// rule happens to be current when somebody runs the calculation - otherwise
/// recalculating a 2027 commission in 2029 would silently restate what the agency
/// was owed two years ago (ADR-0023).
/// </para>
/// <para>
/// There is no default rate anywhere in AgencyOS. A transaction with no governing
/// rule produces no entitlement and says so.
/// </para>
/// </remarks>
public sealed class CommissionRule
{
    private CommissionRule()
    {
    }

    public CommissionRuleId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The representation it arises under. Required.</summary>
    public Guid RepresentationId { get; private set; }

    /// <summary>The client, denormalised so lineage is one hop rather than two.</summary>
    public Guid ClientPersonId { get; private set; }

    /// <summary>The contract it is confined to, or null when it governs broadly.</summary>
    public ContractId? ContractId { get; private set; }

    public CommissionBasisKind Basis { get; private set; }

    /// <summary>The percentage, for a rate rule.</summary>
    public decimal? RatePercent { get; private set; }

    /// <summary>The sum, for a fixed rule.</summary>
    public decimal? FixedAmountValue { get; private set; }

    /// <summary>What a fixed rule is denominated in.</summary>
    public string? CurrencyCodeValue { get; private set; }

    /// <summary>The term a specific-term rule applies to.</summary>
    public ContractTermCode? TermCode { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>Where the rule came from: an agency agreement, a guild schedule, a side letter.</summary>
    public string? Provenance { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (ADR-0014).</summary>
    public int Version { get; private set; }

    /// <summary>Whether it was in force on a date.</summary>
    public bool GovernsOn(DateOnly on) => FinanceRules.RuleGovernsOn(ToRulesInput(), on);

    /// <summary>Records a commission rule.</summary>
    public static CommissionRule Create(
        OrganizationId organizationId,
        Guid representationId,
        Guid clientPersonId,
        CommissionBasisKind basis,
        DateOnly effectiveFrom,
        UserId createdBy,
        DateTimeOffset now,
        decimal? ratePercent = null,
        Money? fixedAmount = null,
        ContractTermCode? termCode = null,
        ContractId? contractId = null,
        DateOnly? effectiveTo = null,
        string? provenance = null,
        string? notes = null)
    {
        if (!Enum.IsDefined(basis))
        {
            throw new DomainException($"Unknown commission basis '{basis}'.");
        }

        if (effectiveTo is { } ends && ends < effectiveFrom)
        {
            throw new DomainException("A commission rule cannot end before it starts.");
        }

        CommissionRule rule = new()
        {
            Id = CommissionRuleId.New(),
            OrganizationId = organizationId,
            RepresentationId = representationId,
            ClientPersonId = clientPersonId,
            ContractId = contractId,
            Basis = basis,
            RatePercent = ratePercent,
            FixedAmountValue = fixedAmount?.Amount,
            CurrencyCodeValue = fixedAmount?.Currency.Value,
            TermCode = termCode,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            Provenance = Ensure.OptionalMax(provenance, nameof(provenance), 500),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            CreatedAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now,
            Version = 1,
        };

        // The kernel owns what makes a rule coherent, so a rule built any other way
        // could not be more permissive than this one.
        if (FinanceRules.DescribeRuleProblem(rule.ToRulesInput()) is { } problem)
        {
            throw new DomainException(problem);
        }

        return rule;
    }

    /// <summary>
    /// Ends a rule from a date.
    /// </summary>
    /// <remarks>
    /// The only way a rule stops governing. Editing a rate in place would restate
    /// every commission ever calculated under it, which is precisely what
    /// effective dating exists to prevent.
    /// </remarks>
    public void EndOn(DateOnly endsOn, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (endsOn < EffectiveFrom)
        {
            throw new DomainException("A commission rule cannot end before it starts.");
        }

        if (EffectiveTo is not null)
        {
            throw new DomainException("This rule has already been ended.");
        }

        EffectiveTo = endsOn;

        Touch(now);
    }

    /// <summary>The flat shape the finance kernel reasons about.</summary>
    public CommissionRuleInput ToRulesInput() =>
        new()
        {
            Id = Id.Value,
            Basis = (int)Basis,
            RatePercent = RatePercent,
            FixedAmount = FixedAmountValue,
            Currency = CurrencyCodeValue,
            TermCode = TermCode is { } code ? (int)code : null,
            EffectiveFrom = EffectiveFrom,
            EffectiveTo = EffectiveTo,
            ContractId = ContractId?.Value,
        };

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(CommissionRule), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>
/// What the agency is entitled to against one monetary obligation.
/// </summary>
/// <remarks>
/// <para>
/// The rate and basis are <strong>snapshotted at calculation</strong> rather than
/// read through to the rule. A rule that is later corrected or ended must not
/// silently restate an entitlement somebody already acted on; if the rate was
/// wrong, that is an adjustment with its own row, its own reason and its own
/// ledger consequence (ADR-0023).
/// </para>
/// <para>
/// Entitlement is what the agency is owed. What it has actually <em>collected</em>
/// is arithmetic over the payments that arrived against the underlying receivable,
/// and is deliberately not a column here. An agency entitled to a hundred thousand
/// against a contract that has paid four hundred of a million has collected forty
/// thousand, and a row that stored one number as the other would be read as the
/// other.
/// </para>
/// </remarks>
public sealed class CommissionEntitlement
{
    private readonly List<CommissionAdjustment> _adjustments = [];

    private CommissionEntitlement()
    {
    }

    public CommissionEntitlementId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    // ---- lineage: every question the milestone requires an answer to ----

    /// <summary>Which obligation produced it.</summary>
    public MonetaryObligationId MonetaryObligationId { get; private set; }

    /// <summary>Which contract.</summary>
    public ContractId ContractId { get; private set; }

    /// <summary>Which client.</summary>
    public Guid ClientPersonId { get; private set; }

    /// <summary>Which representation it arises under.</summary>
    public Guid RepresentationId { get; private set; }

    /// <summary>Which rule was applied.</summary>
    public CommissionRuleId CommissionRuleId { get; private set; }

    /// <summary>The receivable the client money is collected through.</summary>
    public ReceivableId? ClientReceivableId { get; private set; }

    // ---- the snapshot ----

    /// <summary>The basis the rule used, as it stood at calculation.</summary>
    public CommissionBasisKind Basis { get; private set; }

    /// <summary>The rate applied, as it stood at calculation. Never re-read.</summary>
    public decimal? RatePercentSnapshot { get; private set; }

    /// <summary>What the rate was applied to.</summary>
    public decimal BasisAmountValue { get; private set; }

    /// <summary>What the agency is entitled to.</summary>
    public decimal EntitledAmountValue { get; private set; }

    public string CurrencyCodeValue { get; private set; } = string.Empty;

    /// <summary>The date whose rules governed. Not the date of the calculation.</summary>
    public DateOnly GoverningOn { get; private set; }

    public CommissionEntitlementStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CalculatedAt { get; private set; }

    public UserId CalculatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<CommissionAdjustment> Adjustments => _adjustments;

    /// <summary>What was calculated.</summary>
    public Money EntitledAmount => Money.Create(EntitledAmountValue, CurrencyCodeValue);

    /// <summary>What it was calculated against.</summary>
    public Money BasisAmount => Money.Create(BasisAmountValue, CurrencyCodeValue);

    /// <summary>The total of applied adjustments, which reduce what is owed.</summary>
    public Money AdjustedAmount =>
        Money.Create(
            _adjustments.Where(x => x.IsApplied).Sum(x => x.AmountValue), CurrencyCodeValue);

    /// <summary>
    /// What the agency has earned in cash, given how much has been collected.
    /// </summary>
    /// <remarks>
    /// The rate applied to what actually arrived, capped at the entitlement. Passed
    /// the collected basis rather than reading it, because the entitlement does not
    /// know about payments and should not.
    /// </remarks>
    public Money CollectedAgainst(Money collectedBasis)
    {
        decimal? collected = FinanceRules.Collected(
            ToRulesRule(), EntitledAmountValue, collectedBasis.Amount, CurrencyCodeValue);

        return Money.Create(collected ?? 0m, CurrencyCodeValue);
    }

    /// <summary>What is still to come, given what has been earned so far.</summary>
    public Money OutstandingAgainst(Money collectedSoFar)
    {
        decimal? outstanding = FinanceRules.OutstandingCommission(
            EntitledAmountValue,
            collectedSoFar.Amount,
            AdjustedAmount.Amount,
            CurrencyCodeValue);

        return Money.Create(outstanding ?? 0m, CurrencyCodeValue);
    }

    /// <summary>Records what a rule entitles the agency to against an obligation.</summary>
    public static CommissionEntitlement Calculate(
        OrganizationId organizationId,
        MonetaryObligationId obligationId,
        ContractId contractId,
        Guid clientPersonId,
        Guid representationId,
        CommissionRule rule,
        Money basisAmount,
        DateOnly governingOn,
        UserId calculatedBy,
        DateTimeOffset now,
        ReceivableId? clientReceivableId = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.OrganizationId != organizationId)
        {
            throw new DomainException("A commission rule cannot be applied across tenants.");
        }

        if (!rule.GovernsOn(governingOn))
        {
            throw new DomainException(
                "That rule was not in force on the date this obligation falls due. "
                + "Historical commission uses the rule that governed at the time.");
        }

        decimal? entitled = FinanceRules.Entitlement(
            rule.ToRulesInput(), basisAmount.Amount, basisAmount.Currency.Value);

        if (entitled is not { } amount)
        {
            throw new DomainException(
                FinanceRules.DescribeEntitlementProblem(
                    rule.ToRulesInput(), basisAmount.Amount, basisAmount.Currency.Value)
                ?? "The commission could not be worked out.");
        }

        string currency =
            FinanceRules.EntitlementCurrency(
                rule.ToRulesInput(), basisAmount.Amount, basisAmount.Currency.Value)
            ?? basisAmount.Currency.Value;

        return new CommissionEntitlement
        {
            Id = CommissionEntitlementId.New(),
            OrganizationId = organizationId,
            MonetaryObligationId = obligationId,
            ContractId = contractId,
            ClientPersonId = clientPersonId,
            RepresentationId = representationId,
            CommissionRuleId = rule.Id,
            ClientReceivableId = clientReceivableId,
            Basis = rule.Basis,

            // Snapshotted, never re-read. A later correction to the rule must not
            // restate what was already calculated.
            RatePercentSnapshot = rule.RatePercent,

            BasisAmountValue = basisAmount.Amount,
            EntitledAmountValue = amount,
            CurrencyCodeValue = currency,
            GoverningOn = governingOn,
            Status = CommissionEntitlementStatus.Calculated,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            CalculatedAt = now,
            CalculatedBy = calculatedBy,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Records a change to what the agency is owed.
    /// </summary>
    /// <remarks>
    /// An adjustment row, never an edit to the calculated figure. The original
    /// calculation plus the adjustments is the current economic position, and both
    /// halves stay readable: "we calculated a hundred thousand and then waived ten"
    /// is a different story from "we calculated ninety thousand" (ADR-0023).
    /// </remarks>
    public CommissionAdjustment Adjust(
        CommissionAdjustmentKind kind,
        Money amount,
        string reason,
        UserId actor,
        DateTimeOffset now,
        int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status != CommissionEntitlementStatus.Calculated)
        {
            throw new DomainException(
                $"This entitlement is {Status.ToString().ToLowerInvariant()} and cannot be adjusted.");
        }

        if (amount.Currency.Value != CurrencyCodeValue)
        {
            throw new DomainException(
                $"This entitlement is in {CurrencyCodeValue} and that adjustment is in "
                + $"{amount.Currency}.");
        }

        if (amount.Amount > EntitledAmountValue - AdjustedAmount.Amount)
        {
            throw new DomainException(
                "That adjustment is larger than what remains of the entitlement. "
                + "Reducing it below nothing would make the agency owe money it never earned.");
        }

        CommissionAdjustment adjustment = CommissionAdjustment.Record(
            OrganizationId, Id, kind, amount, reason, actor, now);

        _adjustments.Add(adjustment);

        Touch(now);

        return adjustment;
    }

    /// <summary>Marks the entitlement replaced by a recalculation.</summary>
    public void Supersede(DateTimeOffset now, int expectedVersion, string reason)
    {
        RequireVersion(expectedVersion);

        if (Status != CommissionEntitlementStatus.Calculated)
        {
            throw new DomainException(
                $"This entitlement is already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = CommissionEntitlementStatus.Superseded;
        Notes = Ensure.OptionalMax(
            string.IsNullOrWhiteSpace(Notes) ? reason : $"{Notes}\n{reason}", nameof(reason), 4000);

        Touch(now);
    }

    /// <summary>Withdraws an entitlement whose obligation went away.</summary>
    public void Cancel(DateTimeOffset now, int expectedVersion, string reason)
    {
        RequireVersion(expectedVersion);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Cancelling a commission entitlement needs a reason.");
        }

        Status = CommissionEntitlementStatus.Cancelled;
        Notes = Ensure.OptionalMax(
            string.IsNullOrWhiteSpace(Notes) ? reason : $"{Notes}\n{reason}", nameof(reason), 4000);

        Touch(now);
    }

    /// <summary>Links the client receivable this commission is collected through.</summary>
    public void LinkReceivable(ReceivableId receivable, DateTimeOffset now)
    {
        ClientReceivableId = receivable;

        Touch(now);
    }

    /// <summary>
    /// The snapshotted rule, as the kernel needs to see it.
    /// </summary>
    /// <remarks>
    /// Built from the snapshot rather than from the live rule, so collection
    /// arithmetic uses the rate that was actually applied at calculation.
    /// </remarks>
    private CommissionRuleInput ToRulesRule() =>
        new()
        {
            Id = CommissionRuleId.Value,
            Basis = (int)Basis,
            RatePercent = RatePercentSnapshot,
            FixedAmount = Basis == CommissionBasisKind.FixedAmount ? EntitledAmountValue : null,
            Currency = CurrencyCodeValue,
            TermCode = null,
            EffectiveFrom = GoverningOn,
            EffectiveTo = null,
            ContractId = ContractId.Value,
        };

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(CommissionEntitlement), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>
/// A change to what the agency is owed, recorded rather than applied.
/// </summary>
public sealed class CommissionAdjustment
{
    private CommissionAdjustment()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public CommissionEntitlementId CommissionEntitlementId { get; private set; }

    public CommissionAdjustmentKind Kind { get; private set; }

    /// <summary>How much the entitlement is reduced by.</summary>
    public decimal AmountValue { get; private set; }

    public string CurrencyCodeValue { get; private set; } = string.Empty;

    /// <summary>Why. Required.</summary>
    public string Reason { get; private set; } = string.Empty;

    public bool IsApplied { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public Money Amount => Money.Create(AmountValue, CurrencyCodeValue);

    internal static CommissionAdjustment Record(
        OrganizationId organizationId,
        CommissionEntitlementId entitlementId,
        CommissionAdjustmentKind kind,
        Money amount,
        string reason,
        UserId actor,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown commission adjustment kind '{kind}'.");
        }

        if (amount.Amount <= 0m)
        {
            throw new DomainException("An adjustment of nothing adjusts nothing.");
        }

        return new CommissionAdjustment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            CommissionEntitlementId = entitlementId,
            Kind = kind,
            AmountValue = amount.Amount,
            CurrencyCodeValue = amount.Currency.Value,
            Reason = Ensure.NotBlankMax(reason, nameof(reason), 1000),
            IsApplied = true,
            RecordedAt = now,
            RecordedBy = actor,
        };
    }
}
