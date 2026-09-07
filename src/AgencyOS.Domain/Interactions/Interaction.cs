using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;

namespace AgencyOS.Domain.Interactions;

/// <summary>Opaque, immutable identifier for an <see cref="Interaction"/>.</summary>
public readonly record struct InteractionId(Guid Value)
{
    public static InteractionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for an <see cref="InteractionParticipant"/>.</summary>
public readonly record struct InteractionParticipantId(Guid Value)
{
    public static InteractionParticipantId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>What kind of contact took place.</summary>
public enum InteractionType
{
    Meeting = 1,
    Call = 2,
    Email = 3,
    Message = 4,
    Event = 5,
    Note = 6,
    Other = 99,
}

/// <summary>How the record came to exist.</summary>
/// <remarks>
/// Provenance matters: a hand-entered recollection and an ingested mail thread
/// deserve different confidence, and <c>docs/01_PRODUCT_SPEC.md</c> keeps fact and
/// judgment distinct. Only manual entry exists in M2.
/// </remarks>
public enum InteractionSource
{
    ManualEntry = 1,
    Imported = 2,
}

/// <summary>
/// A party who took part in an interaction.
/// </summary>
/// <remarks>
/// A join entity rather than an array on the interaction, as
/// <c>docs/08_DATA_MODEL_FOUNDATION.md</c> requires, so participation is queryable
/// and each participant is a real foreign key.
/// </remarks>
public sealed class InteractionParticipant
{
    private InteractionParticipant()
    {
    }

    public InteractionParticipantId Id { get; private set; }

    public InteractionId InteractionId { get; private set; }

    /// <summary>The tenant that owns this record. Always the interaction's tenant.</summary>
    public OrganizationId OrganizationId { get; private set; }

    public PersonId? PersonId { get; private set; }

    public CompanyId? CompanyId { get; private set; }

    /// <summary>Free-text note about how they took part, for example "host".</summary>
    public string? Role { get; private set; }

    /// <summary>The party who took part.</summary>
    public RelationshipEndpoint Party => PersonId.HasValue
        ? RelationshipEndpoint.ForPerson(PersonId.Value)
        : RelationshipEndpoint.ForCompany(CompanyId!.Value);

    internal static InteractionParticipant Create(
        InteractionId interactionId,
        OrganizationId organizationId,
        RelationshipEndpoint party,
        string? role)
    {
        return new InteractionParticipant
        {
            Id = InteractionParticipantId.New(),
            InteractionId = interactionId,
            OrganizationId = organizationId,
            PersonId = party.AsPerson,
            CompanyId = party.AsCompany,
            Role = Ensure.OptionalMax(role, nameof(role), 128),
        };
    }
}

/// <summary>
/// A recorded contact with one or more parties.
/// </summary>
/// <remarks>
/// The unit of institutional memory: what happened, when, with whom, and what was
/// said. <see cref="Summary"/> is the structured fact; <see cref="DetailedNotes"/>
/// is judgment, and the two are kept apart deliberately.
/// </remarks>
public sealed class Interaction
{
    private readonly List<InteractionParticipant> _participants = [];

    private Interaction()
    {
    }

    public InteractionId Id { get; private set; }

    /// <summary>The tenant that owns this record.</summary>
    public OrganizationId OrganizationId { get; private set; }

    public InteractionType Type { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public string? DetailedNotes { get; private set; }

    public InteractionSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Everyone who took part. Never empty.</summary>
    public IReadOnlyList<InteractionParticipant> Participants => _participants;

    /// <summary>Records that an interaction happened.</summary>
    /// <param name="organizationId">Owning tenant.</param>
    /// <param name="type">Kind of contact.</param>
    /// <param name="occurredAt">When it happened, which is not when it was recorded.</param>
    /// <param name="summary">One-line factual summary.</param>
    /// <param name="participants">Parties involved, each with an optional role note.</param>
    /// <param name="createdBy">Acting user.</param>
    /// <param name="now">Recording instant.</param>
    /// <param name="detailedNotes">Longer, subjective account.</param>
    /// <param name="source">Provenance of the record.</param>
    public static Interaction Record(
        OrganizationId organizationId,
        InteractionType type,
        DateTimeOffset occurredAt,
        string summary,
        IReadOnlyList<(RelationshipEndpoint Party, string? Role)> participants,
        UserId createdBy,
        DateTimeOffset now,
        string? detailedNotes = null,
        InteractionSource source = InteractionSource.ManualEntry)
    {
        ArgumentNullException.ThrowIfNull(participants);

        // An interaction with nobody in it records nothing. It would also be
        // invisible on every timeline, since timelines are reached through parties.
        if (participants.Count == 0)
        {
            throw new DomainException("An interaction must involve at least one participant.");
        }

        HashSet<RelationshipEndpoint> seen = [];

        foreach ((RelationshipEndpoint party, _) in participants)
        {
            if (!seen.Add(party))
            {
                throw new DomainException($"Party {party} is listed as a participant more than once.");
            }
        }

        Interaction interaction = new()
        {
            Id = InteractionId.New(),
            OrganizationId = organizationId,
            Type = type,
            OccurredAt = occurredAt,
            Summary = Ensure.NotBlankMax(summary, nameof(summary), 512),
            DetailedNotes = detailedNotes,
            Source = source,
            CreatedAt = now,
            CreatedBy = createdBy,
        };

        foreach ((RelationshipEndpoint party, string? role) in participants)
        {
            interaction._participants.Add(
                InteractionParticipant.Create(interaction.Id, organizationId, party, role));
        }

        return interaction;
    }

    /// <summary>Determines whether the given party took part.</summary>
    public bool Involves(RelationshipEndpoint party) =>
        _participants.Exists(participant => participant.Party == party);
}
