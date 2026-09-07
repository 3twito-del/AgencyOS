using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Opportunities;

/// <summary>Opaque, immutable identifier for an <see cref="Opportunity"/>.</summary>
public readonly record struct OpportunityId(Guid Value)
{
    public static OpportunityId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What kind of outcome a pursuit is trying to create.
/// </summary>
/// <remarks>
/// Deliberately not a junk drawer. Each value names an outcome the agency
/// recognises and can be held to, and each constrains which subjects the
/// opportunity must carry - a talent engagement without a talent profile is not an
/// under-filled record, it is a different thing wearing the wrong label
/// (ADR-0020).
/// </remarks>
public enum OpportunityKind
{
    /// <summary>Placing a client into a role or engagement.</summary>
    TalentEngagement = 1,

    /// <summary>Taking a project out to buyers.</summary>
    ProjectMarket = 2,

    /// <summary>Taking an assembled package out to buyers.</summary>
    PackageMarket = 3,

    /// <summary>Filling a position on a project the agency is working.</summary>
    Staffing = 4,

    /// <summary>Pursuing a company or person for a working relationship.</summary>
    Partnership = 5,

    Other = 99,
}

/// <summary>
/// Where a pursuit stands as a thing the agency is running.
/// </summary>
/// <remarks>
/// Deliberately not a pipeline. One opportunity routinely has a studio engaged,
/// another passed and a third not yet approached; a single stage across the whole
/// pursuit would have to lie about at least two of them. Market progression lives
/// on the target (ADR-0020).
/// </remarks>
public enum OpportunityStatus
{
    /// <summary>Being set up. Not yet approached anybody.</summary>
    Draft = 1,

    /// <summary>Being worked.</summary>
    Active = 2,

    /// <summary>Deliberately parked. Reversible.</summary>
    Paused = 3,

    /// <summary>Run its course, with a recorded outcome.</summary>
    Closed = 4,

    /// <summary>Called off. Terminal.</summary>
    Cancelled = 5,
}

/// <summary>
/// What happened to a pursuit, recorded when it closes.
/// </summary>
/// <remarks>
/// About the pursuit, never about a deal. There is no Won or Lost here: whether
/// money changed hands is an M7 question, and a Won on an M6 record would be a
/// claim this milestone has no way to substantiate.
/// </remarks>
public enum OpportunityOutcome
{
    /// <summary>The outcome the pursuit existed to create actually happened.</summary>
    Placed = 1,

    /// <summary>Every target passed.</summary>
    NoInterest = 2,

    /// <summary>The agency stopped pursuing it.</summary>
    Withdrawn = 3,

    /// <summary>Overtaken by another pursuit covering the same ground.</summary>
    Superseded = 4,

    /// <summary>Closed without ever being taken out.</summary>
    NotPursued = 5,
}

/// <summary>How urgent the agency considers a pursuit.</summary>
/// <remarks>
/// Set by a person. Nothing computes it, and nothing infers it from activity - a
/// number the system invented would be read as a judgement the agency made
/// (<c>CLAUDE.md</c> and the M4 precedent on scores).
/// </remarks>
public enum OpportunityPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
}

/// <summary>What kind of change an opportunity event records.</summary>
public enum OpportunityChangeKind
{
    Created = 1,
    StatusChanged = 2,
}

/// <summary>A recorded change to an opportunity's status.</summary>
/// <remarks>
/// Append-only. The row carries only the current status, so without these a
/// pursuit that was paused for three months and resumed would leave no trace of
/// the gap (ADR-0012 keeps this separate from the audit trail, which answers a
/// security question rather than a domain one).
/// </remarks>
public sealed class OpportunityEvent
{
    private OpportunityEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OpportunityId OpportunityId { get; private set; }

    public OpportunityChangeKind Kind { get; private set; }

    public OpportunityStatus? FromStatus { get; private set; }

    public OpportunityStatus ToStatus { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static OpportunityEvent Record(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        OpportunityChangeKind kind,
        OpportunityStatus? fromStatus,
        OpportunityStatus toStatus,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason)
    {
        return new OpportunityEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            OpportunityId = opportunityId,
            Kind = kind,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// A pursuit of a concrete market outcome.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from a <c>Project</c>, which is the work, and from a <c>Package</c>,
/// which is what the agency assembled. An opportunity is what the agency is
/// currently trying to make happen with them, and it ends whether or not the work
/// does.
/// </para>
/// <para>
/// It stops well short of an offer. When a target reaches the point where money is
/// being discussed, the target moves to <c>Advanced</c> and M7 takes over; nothing
/// here models terms, and nothing here should pretend to (ADR-0020).
/// </para>
/// </remarks>
public sealed class Opportunity
{
    /// <summary>Statuses in which the pursuit is over.</summary>
    public static IReadOnlySet<OpportunityStatus> TerminalStatuses { get; } =
        new HashSet<OpportunityStatus>
        {
            OpportunityStatus.Closed,
            OpportunityStatus.Cancelled,
        };

    /// <summary>
    /// Statuses in which new market activity may be recorded.
    /// </summary>
    /// <remarks>
    /// A draft has not been taken out yet, and a closed pursuit is history. Both
    /// refuse submissions, pitches and target progression, because recording market
    /// activity against them would describe something that did not happen the way
    /// the record says.
    /// </remarks>
    public static IReadOnlySet<OpportunityStatus> MarketActiveStatuses { get; } =
        new HashSet<OpportunityStatus> { OpportunityStatus.Active };

    /// <summary>
    /// The complete status transition table. Anything absent is illegal.
    /// </summary>
    /// <remarks>
    /// Stated as data so the machine can be read at once and enumerated
    /// exhaustively by a test. A closed pursuit can be reopened to Active, because
    /// a buyer coming back weeks later is ordinary; a cancelled one cannot, because
    /// calling something off and then continuing it is a new pursuit.
    /// </remarks>
    public static IReadOnlyDictionary<OpportunityStatus, IReadOnlySet<OpportunityStatus>> AllowedTransitions
    { get; } = new Dictionary<OpportunityStatus, IReadOnlySet<OpportunityStatus>>
    {
        [OpportunityStatus.Draft] = Freeze(
            OpportunityStatus.Active,
            OpportunityStatus.Cancelled),

        [OpportunityStatus.Active] = Freeze(
            OpportunityStatus.Paused,
            OpportunityStatus.Closed,
            OpportunityStatus.Cancelled),

        [OpportunityStatus.Paused] = Freeze(
            OpportunityStatus.Active,
            OpportunityStatus.Closed,
            OpportunityStatus.Cancelled),

        [OpportunityStatus.Closed] = Freeze(OpportunityStatus.Active),

        // Terminal. Calling something off and then carrying on is a new pursuit,
        // and recording it as the same one would erase that it was ever stopped.
        [OpportunityStatus.Cancelled] = Freeze(),
    };

    private readonly List<OpportunityEvent> _events = [];
    private readonly List<OpportunitySubject> _subjects = [];

    private Opportunity()
    {
    }

    public OpportunityId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public OpportunityKind Kind { get; private set; }

    public OpportunityStatus Status { get; private set; }

    public OpportunityPriority Priority { get; private set; }

    /// <summary>The internal owner. A pursuit nobody owns is one nobody works.</summary>
    public UserId OwnerUserId { get; private set; }

    public DateOnly OpenedOn { get; private set; }

    /// <summary>When the pursuit ended, set with the terminal status.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>What happened, recorded on closure. Never a deal outcome.</summary>
    public OpportunityOutcome? Outcome { get; private set; }

    /// <summary>Factual description of what is being pursued.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Internal strategy. Requires <c>opportunities.strategy.read</c>.
    /// </summary>
    /// <remarks>
    /// The most sensitive text in the milestone: it routinely names who the agency
    /// expects to pass, what it will accept, and why one buyer is being kept until
    /// last. Redacted absent-not-refused, and deliberately excluded from the search
    /// vector so its terms cannot be confirmed by searching for them (ADR-0020).
    /// </remarks>
    public string? StrategyNotes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<OpportunityEvent> Events => _events;

    public IReadOnlyCollection<OpportunitySubject> Subjects => _subjects;

    /// <summary>Gets a value indicating whether the pursuit is over.</summary>
    public bool IsTerminal => TerminalStatuses.Contains(Status);

    /// <summary>Gets a value indicating whether market activity may be recorded now.</summary>
    public bool AcceptsMarketActivity => MarketActiveStatuses.Contains(Status);

    /// <summary>
    /// The subject the pursuit is fundamentally about, derived from its kind.
    /// </summary>
    /// <remarks>
    /// Derived rather than flagged. A stored "is primary" boolean would be a second
    /// source of truth that drifts the moment somebody adds a subject and forgets
    /// to move the flag, and the invariants below guarantee exactly one subject of
    /// the required kind exists.
    /// </remarks>
    public OpportunitySubject? PrimarySubject =>
        RequiredSubjectKind(Kind) is { } required
            ? _subjects.FirstOrDefault(x => x.Kind == required)
            : _subjects.FirstOrDefault();

    /// <summary>The subject kind an opportunity of this kind must carry exactly one of.</summary>
    /// <remarks>
    /// Null for kinds whose subject is genuinely open - a partnership can be about
    /// a project, a package or a client, and forcing a choice would make the record
    /// less true rather than more.
    /// </remarks>
    public static OpportunitySubjectKind? RequiredSubjectKind(OpportunityKind kind) => kind switch
    {
        OpportunityKind.TalentEngagement => OpportunitySubjectKind.TalentProfile,
        OpportunityKind.ProjectMarket => OpportunitySubjectKind.Project,
        OpportunityKind.PackageMarket => OpportunitySubjectKind.Package,
        OpportunityKind.Staffing => OpportunitySubjectKind.ProjectRole,
        _ => null,
    };

    public static Opportunity Create(
        OrganizationId organizationId,
        string name,
        OpportunityKind kind,
        UserId ownerUserId,
        DateOnly openedOn,
        UserId createdBy,
        DateTimeOffset now,
        OpportunityPriority priority = OpportunityPriority.Normal,
        string? description = null,
        string? strategyNotes = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown opportunity kind '{kind}'.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new DomainException($"Unknown opportunity priority '{priority}'.");
        }

        Opportunity opportunity = new()
        {
            Id = OpportunityId.New(),
            OrganizationId = organizationId,
            Name = Ensure.NotBlankMax(name, nameof(name), 300),
            Kind = kind,
            Status = OpportunityStatus.Draft,
            Priority = priority,
            OwnerUserId = ownerUserId,
            OpenedOn = openedOn,
            Description = Ensure.OptionalMax(description, nameof(description), 4000),
            StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 8000),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        opportunity._events.Add(OpportunityEvent.Record(
            organizationId,
            opportunity.Id,
            OpportunityChangeKind.Created,
            null,
            OpportunityStatus.Draft,
            now,
            createdBy,
            reason: null));

        return opportunity;
    }

    public void Update(
        string name,
        OpportunityPriority priority,
        UserId ownerUserId,
        DateTimeOffset now,
        int expectedVersion,
        string? description = null,
        string? strategyNotes = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(priority))
        {
            throw new DomainException($"Unknown opportunity priority '{priority}'.");
        }

        Name = Ensure.NotBlankMax(name, nameof(name), 300);
        Priority = priority;
        OwnerUserId = ownerUserId;
        Description = Ensure.OptionalMax(description, nameof(description), 4000);
        StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 8000);

        Touch(now);
    }

    /// <summary>
    /// Moves the pursuit to a new status.
    /// </summary>
    /// <remarks>
    /// Already being at the target returns without complaint, so a retried command
    /// lands where the first attempt did (ADR-0014).
    /// </remarks>
    public void ChangeStatus(
        OpportunityStatus target,
        DateOnly occurredOn,
        DateTimeOffset now,
        UserId changedBy,
        int expectedVersion,
        OpportunityOutcome? outcome = null,
        string? reason = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(target))
        {
            throw new DomainException($"Unknown opportunity status '{target}'.");
        }

        if (Status == target)
        {
            return;
        }

        if (!AllowedTransitions[Status].Contains(target))
        {
            throw new DomainException(
                $"An opportunity cannot move from {Status} to {target}. "
                    + $"From {Status} it can move to: {Describe(AllowedTransitions[Status])}.");
        }

        // Closing says what happened; cancelling says it was called off, which is
        // itself the answer. Requiring an outcome on a cancellation would only
        // teach people to pick one at random.
        if (target == OpportunityStatus.Closed && outcome is null)
        {
            throw new DomainException(
                "Closing an opportunity records what came of it. Choose an outcome.");
        }

        if (target != OpportunityStatus.Closed && outcome is not null)
        {
            throw new DomainException(
                $"An outcome only belongs to a closed opportunity, not to one moving to {target}.");
        }

        if (TerminalStatuses.Contains(target))
        {
            if (occurredOn < OpenedOn)
            {
                throw new DomainException(
                    $"An opportunity cannot end on {occurredOn:yyyy-MM-dd}, before it opened on "
                        + $"{OpenedOn:yyyy-MM-dd}.");
            }

            ClosedOn = occurredOn;
            Outcome = target == OpportunityStatus.Closed ? outcome : null;
        }
        else
        {
            // Reopening genuinely reopens: the end date and outcome no longer
            // describe anything true.
            ClosedOn = null;
            Outcome = null;
        }

        OpportunityStatus from = Status;

        Status = target;

        _events.Add(OpportunityEvent.Record(
            OrganizationId,
            Id,
            OpportunityChangeKind.StatusChanged,
            from,
            target,
            now,
            changedBy,
            reason));

        Touch(now);
    }

    // -------------------------------------------------------------- subjects

    /// <summary>
    /// Attaches something the pursuit is about.
    /// </summary>
    /// <remarks>
    /// Adding the same subject twice returns the existing row, because that is what
    /// a retry looks like. A partial unique index catches two requests racing past
    /// this check.
    /// </remarks>
    public OpportunitySubject AddSubject(
        OpportunitySubjectRef subject,
        OpportunitySubjectRole role,
        DateTimeOffset now,
        int expectedVersion,
        string? note = null)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        OpportunitySubject? existing = _subjects.FirstOrDefault(x => x.Matches(subject));

        if (existing is not null)
        {
            return existing;
        }

        OpportunitySubject added = OpportunitySubject.Create(
            OrganizationId, Id, subject, role, now, note);

        _subjects.Add(added);

        Touch(now);

        return added;
    }

    /// <summary>Removes a subject, or returns quietly if it is already gone.</summary>
    /// <remarks>
    /// Refused when it would leave the opportunity without the subject its kind
    /// requires - a talent engagement with no client is not a sparser record, it is
    /// an incoherent one.
    /// </remarks>
    public void RemoveSubject(Guid subjectId, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireOpen();

        OpportunitySubject? subject = _subjects.FirstOrDefault(x => x.Id == subjectId);

        if (subject is null)
        {
            return;
        }

        if (RequiredSubjectKind(Kind) is { } required
            && subject.Kind == required
            && _subjects.Count(x => x.Kind == required) == 1)
        {
            throw new DomainException(
                $"A {Kind} opportunity must keep one {required} subject. "
                    + "Add the replacement before removing this one.");
        }

        _subjects.Remove(subject);

        Touch(now);
    }

    /// <summary>
    /// Confirms the pursuit carries the subjects its kind requires.
    /// </summary>
    /// <remarks>
    /// Checked when the opportunity is first taken out rather than at creation: a
    /// draft is exactly where somebody assembles the pieces, and refusing to save
    /// an incomplete draft would make the status pointless.
    /// </remarks>
    public void RequireCoherentSubjects()
    {
        if (_subjects.Count == 0)
        {
            throw new DomainException(
                "An opportunity needs at least one subject before it is taken out: "
                    + "somebody has to be able to say what is being pursued.");
        }

        if (RequiredSubjectKind(Kind) is not { } required)
        {
            return;
        }

        int matching = _subjects.Count(x => x.Kind == required);

        if (matching != 1)
        {
            throw new DomainException(
                $"A {Kind} opportunity needs exactly one {required} subject; this one has "
                    + $"{matching}.");
        }
    }

    // --------------------------------------------------------------- plumbing

    /// <summary>Refuses a mutation built on a version the caller no longer holds.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Opportunity),
                Id.Value.ToString(),
                expectedVersion,
                Version);
        }
    }

    /// <summary>Refuses a change to a pursuit that is over.</summary>
    public void RequireOpen()
    {
        if (IsTerminal)
        {
            throw new DomainException(
                $"This opportunity is {Status} and is kept as it stood. "
                    + "Reopen it if the pursuit has genuinely restarted.");
        }
    }

    /// <summary>Refuses market activity against a pursuit that is not being worked.</summary>
    public void RequireMarketActive()
    {
        if (!AcceptsMarketActivity)
        {
            throw new DomainException(
                Status == OpportunityStatus.Draft
                    ? "This opportunity is still a draft. Activate it before recording market activity."
                    : $"This opportunity is {Status}, so market activity cannot be recorded against it.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static string Describe(IReadOnlySet<OpportunityStatus> statuses) =>
        statuses.Count == 0 ? "nothing, it is terminal" : string.Join(", ", statuses);

    private static IReadOnlySet<OpportunityStatus> Freeze(params OpportunityStatus[] statuses) =>
        new HashSet<OpportunityStatus>(statuses);
}
