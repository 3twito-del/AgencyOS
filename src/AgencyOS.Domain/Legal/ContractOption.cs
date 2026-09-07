using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;

namespace AgencyOS.Domain.Legal;

/// <summary>Opaque, immutable identifier for a <see cref="ContractOption"/>.</summary>
public readonly record struct ContractOptionId(Guid Value)
{
    public static ContractOptionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>What kind of election the option is.</summary>
public enum OptionKind
{
    /// <summary>An option over the person's continued services.</summary>
    Employment = 1,

    /// <summary>An option to renew the agreement.</summary>
    Renewal = 2,

    /// <summary>An option over a sequel.</summary>
    Sequel = 3,

    /// <summary>An option to extend a period.</summary>
    Extension = 4,

    /// <summary>An option to purchase outright.</summary>
    Purchase = 5,

    /// <summary>An option over rights in the underlying property.</summary>
    Rights = 6,

    Other = 99,
}

/// <summary>
/// Where a contractual option stands.
/// </summary>
/// <remarks>
/// Every value but <see cref="Available"/> is terminal. An exercised option never
/// returns to available: if a later amendment restores one, that is a new legal
/// fact recorded as a new option, not a rewriting of this one (ADR-0022).
/// </remarks>
public enum OptionStatus
{
    Available = 1,
    Exercised = 2,
    Declined = 3,
    Expired = 4,
    Waived = 5,
    Cancelled = 6,
}

/// <summary>What causes an option to change status.</summary>
public enum OptionTransition
{
    Exercise = 1,
    Decline = 2,

    /// <summary>A stated deadline passed and somebody recorded that. Never automatic.</summary>
    RecordExpiry = 3,

    Waive = 4,
    Cancel = 5,
}

/// <summary>What kind of thing an option event records.</summary>
public enum OptionEventKind
{
    Recorded = 1,
    StatusChanged = 2,
}

/// <summary>A recorded change to an option.</summary>
/// <remarks>Append-only. The option row carries only where it is now.</remarks>
public sealed class OptionEvent
{
    private OptionEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractOptionId ContractOptionId { get; private set; }

    public OptionEventKind Kind { get; private set; }

    public OptionStatus? FromStatus { get; private set; }

    public OptionStatus ToStatus { get; private set; }

    public OptionTransition? Transition { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static OptionEvent Record(
        OrganizationId organizationId,
        ContractOptionId optionId,
        OptionEventKind kind,
        OptionStatus? fromStatus,
        OptionStatus toStatus,
        OptionTransition? transition,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason)
    {
        return new OptionEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ContractOptionId = optionId,
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
/// A contractual right to elect a future action.
/// </summary>
/// <remarks>
/// <para>
/// First-class rather than a date on something else, because an option has a
/// lifecycle, a holder, a window, a notice requirement and consequences, and none
/// of those fit in a column.
/// </para>
/// <para>
/// <strong>Nothing exercises or expires by itself.</strong> An option lapses
/// because somebody records that its deadline passed, exactly as an M7 offer does.
/// Neither is exercise ever inferred from a payment: money is M9's subject, and a
/// system that read an exercise out of a bank line would be guessing about a legal
/// election (ADR-0022).
/// </para>
/// </remarks>
public sealed class ContractOption
{
    private readonly List<OptionEvent> _events = [];

    private ContractOption()
    {
    }

    public ContractOptionId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractId ContractId { get; private set; }

    /// <summary>The drafting version this option was read from.</summary>
    public ContractVersionId ContractVersionId { get; private set; }

    public string? ClauseReference { get; private set; }

    public OptionKind Kind { get; private set; }

    /// <summary>The party who may elect. A party on this contract.</summary>
    public Guid HolderPartyId { get; private set; }

    /// <summary>What the option is over, in the contract's terms.</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>The project the option concerns, when it concerns one.</summary>
    public ProjectId? ProjectId { get; private set; }

    /// <summary>The property the option concerns, when it concerns one.</summary>
    public SourcePropertyId? SourcePropertyId { get; private set; }

    /// <summary>When the window opens, when the contract states one.</summary>
    public DateOnly? WindowOpensOn { get; private set; }

    /// <summary>When the election must be made, as the contract expresses it.</summary>
    public DeadlineRule Deadline { get; private set; } = null!;

    /// <summary>
    /// The deadline as a date, once it is knowable.
    /// </summary>
    /// <remarks>
    /// Stored because it is what work queues sort and filter on, and recomputed by
    /// the aggregate whenever the anchor becomes known. It is a cache of the rule,
    /// never a second source of truth: the rule is what the contract says.
    /// </remarks>
    public DateOnly? ResolvedDeadlineOn { get; private set; }

    /// <summary>How the election must be made, as the contract requires.</summary>
    public string? ExerciseMethod { get; private set; }

    /// <summary>The notice the contract requires for the election.</summary>
    public Guid? NoticeRequirementId { get; private set; }

    /// <summary>The term recording what the option period pays, when the contract states it.</summary>
    public ContractTermId? EconomicsTermId { get; private set; }

    public OptionStatus Status { get; private set; }

    /// <summary>When the option was resolved, for the states that carry a date.</summary>
    public DateOnly? ResolvedOn { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<OptionEvent> Events => _events;

    /// <summary>Gets a value indicating whether the option is resolved.</summary>
    public bool IsTerminal => DealRules.IsOptionTerminal((int)Status);

    /// <summary>
    /// Gets a value indicating whether the election could be made today.
    /// </summary>
    /// <remarks>
    /// Derived from the window and the status, never stored. An option whose window
    /// has not opened is not exercisable, and neither is one already resolved.
    /// </remarks>
    public bool IsExercisableOn(DateOnly on) =>
        Status == OptionStatus.Available
        && (WindowOpensOn is not { } opens || on >= opens)
        && (ResolvedDeadlineOn is not { } deadline || on <= deadline);

    /// <summary>Whether the stated deadline has passed, when it is known.</summary>
    /// <remarks>
    /// Past deadline is a fact about a date; it is not the same as expired, which
    /// is a status somebody recorded. An option can be past its deadline and still
    /// Available because nobody has dealt with it yet, and that is precisely what a
    /// work queue needs to surface.
    /// </remarks>
    public bool IsPastDeadlineOn(DateOnly on) =>
        Status == OptionStatus.Available
        && ResolvedDeadlineOn is { } deadline
        && DealRules.IsDeadlinePast(on, deadline);

    /// <summary>The statuses reachable from a status.</summary>
    public static IReadOnlySet<OptionStatus> ReachableFrom(OptionStatus status)
    {
        HashSet<OptionStatus> reachable = [];

        foreach (OptionTransition transition in Enum.GetValues<OptionTransition>())
        {
            int next = DealRules.NextOptionState((int)status, (int)transition);

            if (next != 0)
            {
                reachable.Add((OptionStatus)next);
            }
        }

        return reachable;
    }

    /// <summary>Whether a transition is legal from a status.</summary>
    public static bool Permits(OptionStatus status, OptionTransition transition) =>
        DealRules.OptionPermits((int)status, (int)transition);

    public static ContractOption Record(
        OrganizationId organizationId,
        ContractId contractId,
        ContractVersionId versionId,
        OptionKind kind,
        Guid holderPartyId,
        string subject,
        DeadlineRule deadline,
        UserId recordedBy,
        DateTimeOffset now,
        DateOnly? anchorDate = null,
        DateOnly? windowOpensOn = null,
        string? clauseReference = null,
        string? exerciseMethod = null,
        ProjectId? projectId = null,
        SourcePropertyId? sourcePropertyId = null,
        ContractTermId? economicsTermId = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(deadline);

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown option kind '{kind}'.");
        }

        ContractOption option = new()
        {
            Id = ContractOptionId.New(),
            OrganizationId = organizationId,
            ContractId = contractId,
            ContractVersionId = versionId,
            ClauseReference = Ensure.OptionalMax(clauseReference, nameof(clauseReference), 100),
            Kind = kind,
            HolderPartyId = holderPartyId,
            Subject = Ensure.NotBlankMax(subject, nameof(subject), 500),
            ProjectId = projectId,
            SourcePropertyId = sourcePropertyId,
            WindowOpensOn = windowOpensOn,
            Deadline = deadline.Validated(),
            ExerciseMethod = Ensure.OptionalMax(exerciseMethod, nameof(exerciseMethod), 500),
            EconomicsTermId = economicsTermId,
            Status = OptionStatus.Available,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            RecordedAt = now,
            RecordedBy = recordedBy,
            UpdatedAt = now,
            Version = 1,
        };

        option.ResolvedDeadlineOn = deadline.Resolve(anchorDate);

        if (option.ResolvedDeadlineOn is { } resolved
            && windowOpensOn is { } opens
            && resolved < opens)
        {
            throw new DomainException(
                "An option cannot have to be exercised before its window opens.");
        }

        option._events.Add(OptionEvent.Record(
            organizationId,
            option.Id,
            OptionEventKind.Recorded,
            null,
            OptionStatus.Available,
            transition: null,
            now,
            recordedBy,
            reason: null));

        return option;
    }

    /// <summary>Records the holder's election.</summary>
    public void Exercise(
        DateOnly exercisedOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason = null) =>
        Advance(OptionTransition.Exercise, exercisedOn, now, actor, expectedVersion, reason);

    /// <summary>Records that the holder declined.</summary>
    public void Decline(
        DateOnly declinedOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason = null) =>
        Advance(OptionTransition.Decline, declinedOn, now, actor, expectedVersion, reason);

    /// <summary>Records that the holder gave the option up.</summary>
    public void Waive(
        DateOnly waivedOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason = null) =>
        Advance(OptionTransition.Waive, waivedOn, now, actor, expectedVersion, reason);

    /// <summary>
    /// Records that the stated deadline passed without an election.
    /// </summary>
    /// <remarks>
    /// Requires a deadline that is actually knowable and actually behind us.
    /// Nothing lapses because a clock ticked: an option with an unresolved deadline
    /// cannot expire, because the system does not know when it would have.
    /// </remarks>
    public void RecordExpiry(DateTimeOffset now, UserId actor, int expectedVersion, string? reason = null)
    {
        if (ResolvedDeadlineOn is not { } deadline)
        {
            throw new DomainException(
                "This option's deadline is not a date this build can work out, so it cannot be "
                + "recorded as expired. "
                + (Deadline.WhyUnresolved(null) ?? string.Empty));
        }

        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        if (deadline >= today)
        {
            throw new DomainException("This option has not reached its deadline yet.");
        }

        Advance(OptionTransition.RecordExpiry, deadline, now, actor, expectedVersion, reason);
    }

    /// <summary>Removes an option recorded in error, or removed by amendment.</summary>
    public void Cancel(DateTimeOffset now, UserId actor, int expectedVersion, string? reason = null) =>
        Advance(OptionTransition.Cancel, resolvedOn: null, now, actor, expectedVersion, reason);

    /// <summary>
    /// Recomputes the deadline once the event it hangs off has a date.
    /// </summary>
    /// <remarks>
    /// "Sixty days before the option expires" and "thirty days after delivery" are
    /// dates only once expiry or delivery are known. This is how they become known,
    /// and the rule itself never changes.
    /// </remarks>
    public void ResolveDeadline(DateOnly? anchorDate, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        ResolvedDeadlineOn = Deadline.Resolve(anchorDate);

        Touch(now);
    }

    private void Advance(
        OptionTransition transition,
        DateOnly? resolvedOn,
        DateTimeOffset now,
        UserId actor,
        int expectedVersion,
        string? reason)
    {
        RequireVersion(expectedVersion);

        int next = DealRules.NextOptionState((int)Status, (int)transition);

        if (next == 0)
        {
            throw new DomainException(
                $"A {Status.ToString().ToLowerInvariant()} option cannot record '{transition}'. "
                + "An option resolves once; a later amendment that restores one is a new option.");
        }

        OptionStatus from = Status;

        Status = (OptionStatus)next;

        if (DealRules.OptionHasResolutionDate((int)Status))
        {
            ResolvedOn = resolvedOn
                ?? throw new DomainException($"Recording an option as {Status} needs the date it happened.");
        }

        _events.Add(OptionEvent.Record(
            OrganizationId,
            Id,
            OptionEventKind.StatusChanged,
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
                nameof(ContractOption), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
