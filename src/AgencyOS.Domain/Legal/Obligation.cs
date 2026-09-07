using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Legal;

/// <summary>Opaque, immutable identifier for an <see cref="Obligation"/>.</summary>
public readonly record struct ObligationId(Guid Value)
{
    public static ObligationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>What kind of thing a party is required to do.</summary>
public enum ObligationKind
{
    /// <summary>Deliver material by a date.</summary>
    Delivery = 1,

    /// <summary>Render services.</summary>
    Services = 2,

    /// <summary>Pay compensation. The legal requirement; M9 owns whether it was paid.</summary>
    Payment = 3,

    /// <summary>Give notice.</summary>
    Notice = 4,

    /// <summary>Obtain or give approval.</summary>
    Approval = 5,

    /// <summary>Furnish insurance or a certificate.</summary>
    Insurance = 6,

    /// <summary>Accord credit.</summary>
    Credit = 7,

    /// <summary>Refrain from something.</summary>
    Restraint = 8,

    /// <summary>Exercise an option by a date.</summary>
    OptionExercise = 9,

    /// <summary>Make availability or scheduling decisions.</summary>
    Availability = 10,

    Other = 99,
}

/// <summary>
/// Where an obligation stands.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no Due or Overdue value. Past due is a fact about a date
/// and today, derived from the row whenever anybody asks; a stored flag would be
/// wrong from the moment the clock moved past it (ADR-0022).
/// </para>
/// <para>
/// <see cref="Breached"/> is different in kind. A deadline passing is arithmetic;
/// a breach is a legal conclusion somebody reaches, often after notice, cure
/// periods and advice. It is recorded, never inferred.
/// </para>
/// </remarks>
public enum ObligationStatus
{
    /// <summary>Outstanding. Whether it is past due is derived, not stored.</summary>
    Pending = 1,

    Satisfied = 2,
    Waived = 3,

    /// <summary>Somebody determined it was breached. Never inferred from a date.</summary>
    Breached = 4,

    Cancelled = 5,
}

/// <summary>What causes an obligation to change status.</summary>
public enum ObligationTransition
{
    Satisfy = 1,
    Waive = 2,
    RecordBreach = 3,
    Cancel = 4,

    /// <summary>A breach determination reversed, or a waiver withdrawn by agreement.</summary>
    Reinstate = 5,
}

/// <summary>What kind of thing an obligation event records.</summary>
public enum ObligationEventKind
{
    Recorded = 1,
    StatusChanged = 2,
}

/// <summary>A recorded change to an obligation.</summary>
public sealed class ObligationEvent
{
    private ObligationEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ObligationId ObligationId { get; private set; }

    public ObligationEventKind Kind { get; private set; }

    public ObligationStatus? FromStatus { get; private set; }

    public ObligationStatus ToStatus { get; private set; }

    public ObligationTransition? Transition { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static ObligationEvent Record(
        OrganizationId organizationId,
        ObligationId obligationId,
        ObligationEventKind kind,
        ObligationStatus? fromStatus,
        ObligationStatus toStatus,
        ObligationTransition? transition,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason)
    {
        return new ObligationEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ObligationId = obligationId,
            Kind = kind,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Transition = transition,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// Something a party is required to do under a contract.
/// </summary>
/// <remarks>
/// <para>
/// An obligation is a legal requirement; a task is an operational action somebody
/// takes to manage it. They are linked, never merged: turning every obligation
/// into a task would fill the task list with things nobody has to do this week,
/// and turning every task into an obligation would put "chase counsel" in the
/// legal record (ADR-0022).
/// </para>
/// <para>
/// A payment obligation records that money is contractually due. Whether it was
/// invoiced, paid or reconciled is M9's subject and nothing here answers it.
/// </para>
/// </remarks>
public sealed class Obligation
{
    private readonly List<ObligationEvent> _events = [];

    private Obligation()
    {
    }

    public ObligationId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractId ContractId { get; private set; }

    /// <summary>The drafting version this obligation was read from.</summary>
    public ContractVersionId ContractVersionId { get; private set; }

    public string? ClauseReference { get; private set; }

    /// <summary>The party who must perform. A party on this contract.</summary>
    public Guid ObligorPartyId { get; private set; }

    /// <summary>The party owed. A party on this contract.</summary>
    public Guid ObligeePartyId { get; private set; }

    public ObligationKind Kind { get; private set; }

    /// <summary>What has to happen, as the clause requires it.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>When it is due, as the contract expresses it.</summary>
    public DeadlineRule Due { get; private set; } = null!;

    /// <summary>
    /// The due date once it is knowable.
    /// </summary>
    /// <remarks>
    /// A cache of the rule, recomputed when the anchor becomes known. The rule is
    /// what the contract says; this is what a work queue can sort on.
    /// </remarks>
    public DateOnly? ResolvedDueOn { get; private set; }

    public ObligationStatus Status { get; private set; }

    /// <summary>When it was satisfied or waived.</summary>
    public DateOnly? ResolvedOn { get; private set; }

    /// <summary>The option this obligation concerns, when it concerns one.</summary>
    public ContractOptionId? RelatedOptionId { get; private set; }

    /// <summary>The grant this obligation concerns, when it concerns one.</summary>
    public RightsGrantId? RelatedRightsGrantId { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>How sensitive this obligation is. Assigned, never inferred.</summary>
    public PrivilegeClass Privilege { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<ObligationEvent> Events => _events;

    /// <summary>Gets a value indicating whether the obligation is still outstanding.</summary>
    public bool IsOutstanding => DealRules.IsObligationOutstanding((int)Status);

    /// <summary>Gets a value indicating whether the obligation requires privileged access.</summary>
    public bool IsPrivileged =>
        Privilege is PrivilegeClass.LegalStrategy or PrivilegeClass.AttorneyClientPrivileged;

    /// <summary>
    /// Whether the due date has passed with the obligation still outstanding.
    /// </summary>
    /// <remarks>
    /// Derived, never stored, and deliberately not the same as breached. Past due
    /// is arithmetic anybody can check; breach is a determination somebody makes.
    /// An obligation whose due date is unresolved is never past due, because
    /// nothing knows when it was due.
    /// </remarks>
    public bool IsPastDueOn(DateOnly on) =>
        DealRules.ObligationCanBeOverdue((int)Status)
        && ResolvedDueOn is { } due
        && DealRules.IsDeadlinePast(on, due);

    /// <summary>The statuses reachable from a status.</summary>
    public static IReadOnlySet<ObligationStatus> ReachableFrom(ObligationStatus status)
    {
        HashSet<ObligationStatus> reachable = [];

        foreach (ObligationTransition transition in Enum.GetValues<ObligationTransition>())
        {
            int next = DealRules.NextObligationState((int)status, (int)transition);

            if (next != 0)
            {
                reachable.Add((ObligationStatus)next);
            }
        }

        return reachable;
    }

    /// <summary>Whether a transition is legal from a status.</summary>
    public static bool Permits(ObligationStatus status, ObligationTransition transition) =>
        DealRules.ObligationPermits((int)status, (int)transition);

    public static Obligation Record(
        OrganizationId organizationId,
        ContractId contractId,
        ContractVersionId versionId,
        Guid obligorPartyId,
        Guid obligeePartyId,
        ObligationKind kind,
        string description,
        DeadlineRule due,
        UserId recordedBy,
        DateTimeOffset now,
        DateOnly? anchorDate = null,
        string? clauseReference = null,
        ContractOptionId? relatedOptionId = null,
        RightsGrantId? relatedRightsGrantId = null,
        string? notes = null,
        PrivilegeClass privilege = PrivilegeClass.Ordinary)
    {
        ArgumentNullException.ThrowIfNull(due);

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown obligation kind '{kind}'.");
        }

        if (!Enum.IsDefined(privilege))
        {
            throw new DomainException($"Unknown privilege class '{privilege}'.");
        }

        if (obligorPartyId == obligeePartyId)
        {
            throw new DomainException("A party cannot owe an obligation to itself.");
        }

        Obligation obligation = new()
        {
            Id = ObligationId.New(),
            OrganizationId = organizationId,
            ContractId = contractId,
            ContractVersionId = versionId,
            ClauseReference = Ensure.OptionalMax(clauseReference, nameof(clauseReference), 100),
            ObligorPartyId = obligorPartyId,
            ObligeePartyId = obligeePartyId,
            Kind = kind,
            Description = Ensure.NotBlankMax(description, nameof(description), 2000),
            Due = due.Validated(),
            Status = ObligationStatus.Pending,
            RelatedOptionId = relatedOptionId,
            RelatedRightsGrantId = relatedRightsGrantId,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            Privilege = privilege,
            RecordedAt = now,
            RecordedBy = recordedBy,
            UpdatedAt = now,
            Version = 1,
        };

        obligation.ResolvedDueOn = due.Resolve(anchorDate);

        obligation._events.Add(ObligationEvent.Record(
            organizationId,
            obligation.Id,
            ObligationEventKind.Recorded,
            null,
            ObligationStatus.Pending,
            transition: null,
            now,
            recordedBy,
            reason: null));

        return obligation;
    }

    /// <summary>Records that the obligation was performed.</summary>
    public void Satisfy(
        DateOnly satisfiedOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason = null) =>
        Advance(ObligationTransition.Satisfy, satisfiedOn, now, actor, expectedVersion, reason);

    /// <summary>Records that the obligee gave it up.</summary>
    public void Waive(
        DateOnly waivedOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason = null) =>
        Advance(ObligationTransition.Waive, waivedOn, now, actor, expectedVersion, reason);

    /// <summary>
    /// Records a determination that the obligation was breached.
    /// </summary>
    /// <remarks>
    /// Requires a reason. A breach is a conclusion with consequences, and one
    /// recorded without saying why is one nobody can review later.
    /// </remarks>
    public void RecordBreach(DateTimeOffset now, UserId actor, int expectedVersion, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Recording a breach needs the determination behind it. Past due is a date; "
                + "breach is a judgement, and the record should say whose and why.");
        }

        Advance(ObligationTransition.RecordBreach, resolvedOn: null, now, actor, expectedVersion, reason);
    }

    /// <summary>Reverses a breach determination or a waiver.</summary>
    public void Reinstate(DateTimeOffset now, UserId actor, int expectedVersion, string? reason = null)
    {
        Advance(ObligationTransition.Reinstate, resolvedOn: null, now, actor, expectedVersion, reason);

        // Back to outstanding, so the date it was resolved no longer applies.
        ResolvedOn = null;
    }

    /// <summary>Removes an obligation recorded in error, or removed by amendment.</summary>
    public void Cancel(DateTimeOffset now, UserId actor, int expectedVersion, string? reason = null) =>
        Advance(ObligationTransition.Cancel, resolvedOn: null, now, actor, expectedVersion, reason);

    /// <summary>Recomputes the due date once the event it hangs off has one.</summary>
    public void ResolveDueDate(DateOnly? anchorDate, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        ResolvedDueOn = Due.Resolve(anchorDate);

        Touch(now);
    }

    private void Advance(
        ObligationTransition transition,
        DateOnly? resolvedOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason)
    {
        RequireVersion(expectedVersion);

        int next = DealRules.NextObligationState((int)Status, (int)transition);

        if (next == 0)
        {
            throw new DomainException(
                $"A {Status.ToString().ToLowerInvariant()} obligation cannot record '{transition}'.");
        }

        ObligationStatus from = Status;

        Status = (ObligationStatus)next;

        if (DealRules.ObligationHasResolutionDate((int)Status))
        {
            ResolvedOn = resolvedOn
                ?? throw new DomainException(
                    $"Recording an obligation as {Status} needs the date it happened.");
        }

        _events.Add(ObligationEvent.Record(
            OrganizationId,
            Id,
            ObligationEventKind.StatusChanged,
            from,
            Status,
            transition,
            now,
            actor,
            reason));

        Touch(now);
    }

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Obligation), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
