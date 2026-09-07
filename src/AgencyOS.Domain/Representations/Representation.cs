using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Representations;

/// <summary>Opaque, immutable identifier for a <see cref="Representation"/>.</summary>
public readonly record struct RepresentationId(Guid Value)
{
    public static RepresentationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Where a representation relationship stands.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately separate from prospecting. Courting somebody is a pursuit the
/// agency runs; representing them is a relationship with its own validity period.
/// One enum spanning both would make "Courting" and "Suspended" siblings, and
/// would leave nowhere to record a former client the agency is now courting again
/// - which is an ordinary thing to happen and something the milestone requires
/// (ADR-0017).
/// </para>
/// <para>
/// <see cref="Terminated"/> and <see cref="Expired"/> are terminal. A terminal
/// representation is history and is never reopened; signing the person again
/// creates a new representation, which is what actually happened.
/// </para>
/// </remarks>
public enum RepresentationStatus
{
    /// <summary>Agreed in principle, not yet in effect.</summary>
    Pending = 1,

    /// <summary>In effect. This is what makes somebody a client.</summary>
    Active = 2,

    /// <summary>Temporarily not acting, by agreement. Reversible.</summary>
    Suspended = 3,

    /// <summary>Ended deliberately by either side.</summary>
    Terminated = 4,

    /// <summary>Ran to its end date without being renewed.</summary>
    Expired = 5,
}

/// <summary>Why a representation changed state, as recorded at the time.</summary>
/// <remarks>
/// Append-only. The representation row carries only the current status, so without
/// these a suspend-and-resume would leave no trace and "do not overwrite
/// historical state destructively" would be untrue. This is domain history for
/// people to read, distinct from the audit trail's security record (ADR-0012).
/// </remarks>
public sealed class RepresentationEvent
{
    private RepresentationEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public RepresentationId RepresentationId { get; private set; }

    /// <summary>Status before the transition, or null for the first record.</summary>
    public RepresentationStatus? FromStatus { get; private set; }

    public RepresentationStatus ToStatus { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static RepresentationEvent Record(
        OrganizationId organizationId,
        RepresentationId representationId,
        RepresentationStatus? fromStatus,
        RepresentationStatus toStatus,
        DateOnly occurredOn,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason)
    {
        return new RepresentationEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            RepresentationId = representationId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            OccurredOn = occurredOn,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// The agency's representation relationship with a person.
/// </summary>
/// <remarks>
/// <para>
/// The operational relationship, not the executed legal agreement. Contract
/// documents, clauses and deal terms are M8; modelling them here would mean
/// guessing at contract law from a milestone that has no contracts in it.
/// </para>
/// <para>
/// Dates are <see cref="DateOnly"/> rather than instants. "Representation started
/// on 3 March" is a business date, and storing it as a timestamp would invent a
/// time of day nobody agreed and make the answer depend on the reader's timezone.
/// </para>
/// <para>
/// The lead representative is not stored here. It is derived from the team member
/// holding <c>Lead</c> with no end date, so "the primary representative is a
/// member of the team" is true by construction rather than by a check that drifts
/// the first time somebody is removed from the team.
/// </para>
/// </remarks>
public sealed class Representation
{
    /// <summary>Statuses from which a representation can still change.</summary>
    /// <remarks>
    /// A person may hold at most one representation in a non-terminal status per
    /// tenant. That is enforced here, and again by a partial unique index, because
    /// a duplicate active representation is the failure a retried conversion would
    /// produce and it must be impossible rather than unlikely.
    /// </remarks>
    public static IReadOnlySet<RepresentationStatus> NonTerminalStatuses { get; } =
        new HashSet<RepresentationStatus>
        {
            RepresentationStatus.Pending,
            RepresentationStatus.Active,
            RepresentationStatus.Suspended,
        };

    /// <summary>
    /// The complete transition table. Anything absent is illegal.
    /// </summary>
    /// <remarks>
    /// Stated as data rather than scattered through methods so the whole state
    /// machine can be read at once - and enumerated exhaustively by a test, which
    /// for a machine this size is a proof rather than a sample.
    /// </remarks>
    public static IReadOnlyDictionary<RepresentationStatus, IReadOnlySet<RepresentationStatus>> AllowedTransitions
    { get; } = new Dictionary<RepresentationStatus, IReadOnlySet<RepresentationStatus>>
    {
        [RepresentationStatus.Pending] = Freeze(
            RepresentationStatus.Active,
            RepresentationStatus.Terminated),

        [RepresentationStatus.Active] = Freeze(
            RepresentationStatus.Suspended,
            RepresentationStatus.Terminated,
            RepresentationStatus.Expired),

        [RepresentationStatus.Suspended] = Freeze(
            RepresentationStatus.Active,
            RepresentationStatus.Terminated,
            RepresentationStatus.Expired),

        // Terminal. Signing again creates a new representation, because that is
        // what happened; reopening would erase that there was a gap.
        [RepresentationStatus.Terminated] = Freeze(),
        [RepresentationStatus.Expired] = Freeze(),
    };

    private readonly List<RepresentationEvent> _events = [];
    private readonly List<RepresentationScope> _scopes = [];
    private readonly List<RepresentationTeamMember> _team = [];

    private Representation()
    {
    }

    public RepresentationId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The person being represented.</summary>
    public PersonId PersonId { get; private set; }

    public RepresentationStatus Status { get; private set; }

    /// <summary>When the relationship takes effect.</summary>
    public DateOnly StartsOn { get; private set; }

    /// <summary>When it ends, or null while open-ended.</summary>
    public DateOnly? EndsOn { get; private set; }

    /// <summary>
    /// Whether the agency represents them exclusively within the scopes recorded.
    /// </summary>
    /// <remarks>
    /// Nullable because "we have not established this" is a real and common state,
    /// and defaulting it to false would assert something nobody checked.
    /// </remarks>
    public bool? IsExclusive { get; private set; }

    /// <summary>Territory the representation covers, as recorded. Null when not established.</summary>
    public string? Territory { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<RepresentationEvent> Events => _events;

    public IReadOnlyCollection<RepresentationScope> Scopes => _scopes;

    public IReadOnlyCollection<RepresentationTeamMember> Team => _team;

    /// <summary>Gets a value indicating whether this representation still has a future.</summary>
    public bool IsNonTerminal => NonTerminalStatuses.Contains(Status);

    /// <summary>Gets a value indicating whether the person is currently a client through this record.</summary>
    public bool MakesClient => Status == RepresentationStatus.Active;

    /// <summary>Scopes that still apply.</summary>
    public IEnumerable<RepresentationScope> CurrentScopes => _scopes.Where(x => x.IsOpen);

    /// <summary>Team members currently assigned.</summary>
    public IEnumerable<RepresentationTeamMember> CurrentTeam => _team.Where(x => x.IsOpen);

    /// <summary>The lead representative, derived from the team rather than stored.</summary>
    public RepresentationTeamMember? Lead =>
        _team.FirstOrDefault(x => x.IsOpen && x.Role == RepresentationTeamRole.Lead);

    public static Representation Create(
        OrganizationId organizationId,
        PersonId personId,
        DateOnly startsOn,
        UserId createdBy,
        DateTimeOffset now,
        DateOnly? endsOn = null,
        bool? isExclusive = null,
        string? territory = null,
        string? notes = null)
    {
        RequireValidPeriod(startsOn, endsOn);

        Representation representation = new()
        {
            Id = RepresentationId.New(),
            OrganizationId = organizationId,
            PersonId = personId,
            Status = RepresentationStatus.Pending,
            StartsOn = startsOn,
            EndsOn = endsOn,
            IsExclusive = isExclusive,
            Territory = Ensure.OptionalMax(territory, nameof(territory), 256),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        representation._events.Add(RepresentationEvent.Record(
            organizationId,
            representation.Id,
            fromStatus: null,
            RepresentationStatus.Pending,
            startsOn,
            now,
            createdBy,
            reason: null));

        return representation;
    }

    /// <summary>
    /// Moves the relationship to a new status, refusing anything the transition
    /// table does not allow.
    /// </summary>
    /// <remarks>
    /// One method rather than five, because the legality question is the same one
    /// every time and five copies of it would eventually disagree. The commands
    /// above it stay explicit; this is where they land.
    /// </remarks>
    public void TransitionTo(
        RepresentationStatus target,
        DateOnly occurredOn,
        UserId actor,
        DateTimeOffset now,
        string? reason = null)
    {
        if (Status == target)
        {
            // Already there. A retried command asked for a state that holds, and
            // refusing would turn a safe replay into an error.
            return;
        }

        if (!AllowedTransitions[Status].Contains(target))
        {
            throw new DomainException(
                $"A representation cannot move from {Status} to {target}.");
        }

        if (occurredOn < StartsOn)
        {
            throw new DomainException(
                $"A representation change cannot be dated {occurredOn:O}, before the representation began on {StartsOn:O}.");
        }

        // Ending the relationship fixes its end date if one was not already set,
        // so a terminated representation always says when it stopped.
        if (target is RepresentationStatus.Terminated or RepresentationStatus.Expired)
        {
            EndsOn = occurredOn;

            foreach (RepresentationScope scope in CurrentScopes.ToArray())
            {
                scope.End(occurredOn);
            }

            foreach (RepresentationTeamMember member in CurrentTeam.ToArray())
            {
                member.End(occurredOn);
            }
        }

        _events.Add(RepresentationEvent.Record(
            OrganizationId,
            Id,
            Status,
            target,
            occurredOn,
            now,
            actor,
            reason));

        Status = target;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Adds a scope the agency represents them for.</summary>
    public RepresentationScope AddScope(RepresentationScopeArea area, DateOnly startsOn, DateTimeOffset now)
    {
        RequireOpen();

        RepresentationScope? existing = _scopes.FirstOrDefault(x => x.IsOpen && x.Area == area);

        if (existing is not null)
        {
            // Already represented for this area. Idempotent by intent.
            return existing;
        }

        RepresentationScope scope = RepresentationScope.Open(OrganizationId, Id, area, startsOn);

        _scopes.Add(scope);

        UpdatedAt = now;
        Version++;

        return scope;
    }

    /// <summary>Ends a scope without erasing that it once applied.</summary>
    public void EndScope(RepresentationScopeArea area, DateOnly endsOn, DateTimeOffset now)
    {
        RepresentationScope open =
            _scopes.FirstOrDefault(x => x.IsOpen && x.Area == area)
            ?? throw new DomainException($"This representation does not currently cover '{area}'.");

        open.End(endsOn);

        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Assigns somebody internal to this representation.
    /// </summary>
    /// <remarks>
    /// Assigning a lead ends the previous lead's row rather than refusing, because
    /// changing who leads a relationship is an ordinary event and forcing the
    /// caller to remove-then-add would leave a window with no lead at all.
    /// </remarks>
    public RepresentationTeamMember AssignTeamMember(
        UserId userId,
        RepresentationTeamRole role,
        DateOnly startsOn,
        DateTimeOffset now)
    {
        RequireOpen();

        RepresentationTeamMember? existing = _team.FirstOrDefault(x => x.IsOpen && x.UserId == userId);

        if (existing is not null && existing.Role == role)
        {
            return existing;
        }

        if (role == RepresentationTeamRole.Lead && Lead is { } currentLead)
        {
            currentLead.End(startsOn);
        }

        // A member changing role ends their old row and starts a new one, so the
        // record shows what they were and when it changed.
        existing?.End(startsOn);

        RepresentationTeamMember member =
            RepresentationTeamMember.Open(OrganizationId, Id, userId, role, startsOn);

        _team.Add(member);

        UpdatedAt = now;
        Version++;

        return member;
    }

    /// <summary>Removes somebody from the team, keeping the record that they were on it.</summary>
    public void RemoveTeamMember(UserId userId, DateOnly endsOn, DateTimeOffset now)
    {
        RepresentationTeamMember open =
            _team.FirstOrDefault(x => x.IsOpen && x.UserId == userId)
            ?? throw new DomainException("That user is not currently on this representation's team.");

        open.End(endsOn);

        UpdatedAt = now;
        Version++;
    }

    /// <summary>Revises the descriptive terms of the relationship.</summary>
    public void Update(
        DateOnly startsOn,
        DateOnly? endsOn,
        bool? isExclusive,
        string? territory,
        string? notes,
        DateTimeOffset now)
    {
        RequireValidPeriod(startsOn, endsOn);

        StartsOn = startsOn;
        EndsOn = endsOn;
        IsExclusive = isExclusive;
        Territory = Ensure.OptionalMax(territory, nameof(territory), 256);
        Notes = Ensure.OptionalMax(notes, nameof(notes), 4000);
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Fails unless the caller observed the current version.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(nameof(Representation), Id.ToString(), expectedVersion, Version);
        }
    }

    private void RequireOpen()
    {
        if (!IsNonTerminal)
        {
            throw new DomainException(
                $"This representation is {Status} and can no longer be changed.");
        }
    }

    private static void RequireValidPeriod(DateOnly startsOn, DateOnly? endsOn)
    {
        if (endsOn is { } end && end < startsOn)
        {
            throw new DomainException(
                $"A representation cannot end on {end:O} when it starts on {startsOn:O}.");
        }
    }

    private static IReadOnlySet<RepresentationStatus> Freeze(params RepresentationStatus[] statuses) =>
        new HashSet<RepresentationStatus>(statuses);
}
