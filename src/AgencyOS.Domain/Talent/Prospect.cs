using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Talent;

/// <summary>Opaque, immutable identifier for a <see cref="Prospect"/>.</summary>
public readonly record struct ProspectId(Guid Value)
{
    public static ProspectId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How far the agency has got in pursuing somebody.
/// </summary>
/// <remarks>
/// A pursuit process, kept separate from <c>RepresentationStatus</c>. The two
/// answer different questions - "are we chasing them" and "do we represent them" -
/// and a person can legitimately be in both, which is what happens when a former
/// client is courted again (ADR-0017).
/// </remarks>
public enum ProspectStage
{
    /// <summary>Somebody worth pursuing. Nothing has been said to them yet.</summary>
    Identified = 1,

    /// <summary>Contact has been made.</summary>
    Contacted = 2,

    /// <summary>Actively being pursued.</summary>
    Courting = 3,

    /// <summary>They said no. Terminal.</summary>
    Declined = 4,

    /// <summary>They went elsewhere, or the agency stopped pursuing. Terminal.</summary>
    Lost = 5,

    /// <summary>They became represented. Terminal, and the only successful ending.</summary>
    Converted = 6,
}

/// <summary>A recorded change of prospect stage.</summary>
/// <remarks>
/// Append-only, for the same reason representation events are: the prospect row
/// holds only the current stage, and without these the path somebody took to a
/// decline would be lost.
/// </remarks>
public sealed class ProspectEvent
{
    private ProspectEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ProspectId ProspectId { get; private set; }

    public ProspectStage? FromStage { get; private set; }

    public ProspectStage ToStage { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Reason { get; private set; }

    internal static ProspectEvent Record(
        OrganizationId organizationId,
        ProspectId prospectId,
        ProspectStage? fromStage,
        ProspectStage toStage,
        DateOnly occurredOn,
        DateTimeOffset recordedAt,
        UserId recordedBy,
        string? reason)
    {
        return new ProspectEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ProspectId = prospectId,
            FromStage = fromStage,
            ToStage = toStage,
            OccurredOn = occurredOn,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1000),
        };
    }
}

/// <summary>
/// Somebody the agency is considering representing.
/// </summary>
/// <remarks>
/// A pursuit, not a relationship. It has an internal owner, a stage and a
/// follow-up date, because those are what the agency actually works from; it
/// deliberately carries no representation terms, since none have been agreed.
/// </remarks>
public sealed class Prospect
{
    /// <summary>Stages from which a prospect can still move.</summary>
    public static IReadOnlySet<ProspectStage> OpenStages { get; } = new HashSet<ProspectStage>
    {
        ProspectStage.Identified,
        ProspectStage.Contacted,
        ProspectStage.Courting,
    };

    /// <summary>
    /// The complete transition table. Anything absent is illegal.
    /// </summary>
    /// <remarks>
    /// Pursuit only moves forward. A prospect who was contacted cannot go back to
    /// merely identified: that did not happen, and letting the stage move backwards
    /// would make the funnel unreadable.
    /// </remarks>
    public static IReadOnlyDictionary<ProspectStage, IReadOnlySet<ProspectStage>> AllowedTransitions { get; } =
        new Dictionary<ProspectStage, IReadOnlySet<ProspectStage>>
        {
            [ProspectStage.Identified] = Freeze(
                ProspectStage.Contacted,
                ProspectStage.Courting,
                ProspectStage.Declined,
                ProspectStage.Lost,
                ProspectStage.Converted),

            [ProspectStage.Contacted] = Freeze(
                ProspectStage.Courting,
                ProspectStage.Declined,
                ProspectStage.Lost,
                ProspectStage.Converted),

            [ProspectStage.Courting] = Freeze(
                ProspectStage.Declined,
                ProspectStage.Lost,
                ProspectStage.Converted),

            // Terminal. Pursuing them again is a new prospect, which is the truth:
            // it is a fresh attempt, not a continuation of the one that failed.
            [ProspectStage.Declined] = Freeze(),
            [ProspectStage.Lost] = Freeze(),
            [ProspectStage.Converted] = Freeze(),
        };

    private readonly List<ProspectEvent> _events = [];

    private Prospect()
    {
    }

    public ProspectId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PersonId PersonId { get; private set; }

    public ProspectStage Stage { get; private set; }

    /// <summary>The internal user pursuing them.</summary>
    public UserId OwnerUserId { get; private set; }

    /// <summary>How the agency came across them.</summary>
    public string? Source { get; private set; }

    /// <summary>
    /// Internal strategy for pursuing them.
    /// </summary>
    /// <remarks>
    /// Sensitive in the same way a talent profile's positioning is, and gated by
    /// the same <c>talent.notes.read</c> permission (ADR-0017).
    /// </remarks>
    public string? StrategyNotes { get; private set; }

    public DateOnly IdentifiedOn { get; private set; }

    /// <summary>When somebody should next act on this, if a date has been set.</summary>
    public DateOnly? NextFollowUpOn { get; private set; }

    /// <summary>The representation this prospect became, once converted.</summary>
    public Representations.RepresentationId? ConvertedToRepresentationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<ProspectEvent> Events => _events;

    /// <summary>Gets a value indicating whether this pursuit is still live.</summary>
    public bool IsOpen => OpenStages.Contains(Stage);

    public static Prospect Create(
        OrganizationId organizationId,
        PersonId personId,
        UserId ownerUserId,
        DateOnly identifiedOn,
        UserId createdBy,
        DateTimeOffset now,
        string? source = null,
        string? strategyNotes = null,
        DateOnly? nextFollowUpOn = null)
    {
        Prospect prospect = new()
        {
            Id = ProspectId.New(),
            OrganizationId = organizationId,
            PersonId = personId,
            Stage = ProspectStage.Identified,
            OwnerUserId = ownerUserId,
            Source = Ensure.OptionalMax(source, nameof(source), 256),
            StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 4000),
            IdentifiedOn = identifiedOn,
            NextFollowUpOn = nextFollowUpOn,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        prospect._events.Add(ProspectEvent.Record(
            organizationId,
            prospect.Id,
            fromStage: null,
            ProspectStage.Identified,
            identifiedOn,
            now,
            createdBy,
            reason: null));

        return prospect;
    }

    /// <summary>Moves the pursuit to a new stage, refusing anything illegal.</summary>
    public void TransitionTo(
        ProspectStage target,
        DateOnly occurredOn,
        UserId actor,
        DateTimeOffset now,
        string? reason = null)
    {
        if (Stage == target)
        {
            // Already there; a retry asked for a state that holds.
            return;
        }

        if (!AllowedTransitions[Stage].Contains(target))
        {
            throw new DomainException($"A prospect cannot move from {Stage} to {target}.");
        }

        if (occurredOn < IdentifiedOn)
        {
            throw new DomainException(
                $"A prospect change cannot be dated {occurredOn:O}, before they were identified on {IdentifiedOn:O}.");
        }

        _events.Add(ProspectEvent.Record(
            OrganizationId,
            Id,
            Stage,
            target,
            occurredOn,
            now,
            actor,
            reason));

        Stage = target;

        if (target is ProspectStage.Declined or ProspectStage.Lost or ProspectStage.Converted)
        {
            // Nothing left to follow up: the pursuit is over either way.
            NextFollowUpOn = null;
        }

        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Records that this pursuit produced a representation.
    /// </summary>
    /// <remarks>
    /// Sets the link and the terminal stage together, so a converted prospect can
    /// never point at nothing and a linked prospect can never look still-open.
    /// </remarks>
    public void MarkConverted(
        Representations.RepresentationId representationId,
        DateOnly occurredOn,
        UserId actor,
        DateTimeOffset now)
    {
        TransitionTo(ProspectStage.Converted, occurredOn, actor, now, reason: "Converted to representation.");

        ConvertedToRepresentationId = representationId;
    }

    /// <summary>Revises who owns the pursuit and what is planned.</summary>
    public void Update(
        UserId ownerUserId,
        string? source,
        string? strategyNotes,
        DateOnly? nextFollowUpOn,
        DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DomainException($"This prospect is {Stage} and can no longer be changed.");
        }

        OwnerUserId = ownerUserId;
        Source = Ensure.OptionalMax(source, nameof(source), 256);
        StrategyNotes = Ensure.OptionalMax(strategyNotes, nameof(strategyNotes), 4000);
        NextFollowUpOn = nextFollowUpOn;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Fails unless the caller observed the current version.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(nameof(Prospect), Id.ToString(), expectedVersion, Version);
        }
    }

    private static IReadOnlySet<ProspectStage> Freeze(params ProspectStage[] stages) =>
        new HashSet<ProspectStage>(stages);
}
