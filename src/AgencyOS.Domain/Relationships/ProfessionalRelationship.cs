using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Relationships;

/// <summary>Opaque, immutable identifier for a <see cref="ProfessionalRelationship"/>.</summary>
public readonly record struct RelationshipId(Guid Value)
{
    public static RelationshipId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// A professional connection between two parties.
/// </summary>
/// <remarks>
/// <para>
/// Endpoints are stored as an exclusive arc - one real foreign key per side,
/// with a check constraint ensuring exactly one is set - so the database can
/// verify that an endpoint exists. The four columns are exposed because EF maps
/// them; <see cref="From"/> and <see cref="To"/> are how the domain talks about
/// them. See ADR-0011.
/// </para>
/// <para>
/// Ending a relationship is a command that stamps <see cref="EndedAt"/>. The row
/// is never deleted, so "who did she work for in 2024" stays answerable.
/// </para>
/// </remarks>
public sealed class ProfessionalRelationship
{
    private ProfessionalRelationship()
    {
    }

    public RelationshipId Id { get; private set; }

    /// <summary>The tenant that owns this record.</summary>
    public OrganizationId OrganizationId { get; private set; }

    public PersonId? FromPersonId { get; private set; }

    public CompanyId? FromCompanyId { get; private set; }

    public PersonId? ToPersonId { get; private set; }

    public CompanyId? ToCompanyId { get; private set; }

    public RelationshipType Type { get; private set; }

    public RelationshipDirection Direction { get; private set; }

    public RelationshipStatus Status { get; private set; }

    /// <summary>Subjective strength or confidence, 1 to 5, when recorded.</summary>
    public int? Strength { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public UserId? EndedBy { get; private set; }

    /// <summary>
    /// Optimistic concurrency token, incremented on every mutation.
    /// </summary>
    /// <remarks>
    /// An explicit column rather than PostgreSQL's <c>xmin</c>: a concurrency
    /// token is part of the client contract and must outlive the storage engine.
    /// See <c>docs/adr/ADR-0014-concurrency-and-idempotency.md</c>.
    /// </remarks>
    public int Version { get; private set; }

    /// <summary>
    /// Fails unless the caller observed the current version.
    /// </summary>
    /// <remarks>
    /// This is what turns a blind overwrite into a detected conflict. A client
    /// that has been offline sends the version it last saw; if the record moved
    /// on, the write is refused rather than silently applied.
    /// </remarks>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(GetType().Name, Id.ToString(), expectedVersion, Version);
        }
    }

    /// <summary>The party the relationship runs from.</summary>
    public RelationshipEndpoint From => FromPersonId.HasValue
        ? RelationshipEndpoint.ForPerson(FromPersonId.Value)
        : RelationshipEndpoint.ForCompany(FromCompanyId!.Value);

    /// <summary>The party the relationship runs to.</summary>
    public RelationshipEndpoint To => ToPersonId.HasValue
        ? RelationshipEndpoint.ForPerson(ToPersonId.Value)
        : RelationshipEndpoint.ForCompany(ToCompanyId!.Value);

    public static ProfessionalRelationship Create(
        OrganizationId organizationId,
        RelationshipEndpoint from,
        RelationshipEndpoint to,
        RelationshipType type,
        UserId createdBy,
        DateTimeOffset now,
        RelationshipDirection? direction = null,
        int? strength = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        string? notes = null)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Unknown relationship type '{type}'.");
        }

        if (!RelationshipTypeRules.AllowsPairing(type, from.Kind, to.Kind))
        {
            throw new DomainException(
                $"A {type} relationship must be {RelationshipTypeRules.DescribeAllowedPairings(type)}, "
                    + $"not {from.Kind.ToString().ToLowerInvariant()} to {to.Kind.ToString().ToLowerInvariant()}.");
        }

        if (from == to && !RelationshipTypeRules.PermitsSelfReference(type))
        {
            throw new DomainException($"A {type} relationship cannot connect a party to itself.");
        }

        if (strength is not null and (< 1 or > 5))
        {
            throw new DomainException("Strength is recorded on a scale of 1 to 5.");
        }

        if (startedAt is { } start && endedAt is { } end && end < start)
        {
            throw new DomainException("A relationship cannot end before it started.");
        }

        ProfessionalRelationship relationship = new()
        {
            Id = RelationshipId.New(),
            OrganizationId = organizationId,
            Type = type,
            Direction = direction ?? RelationshipTypeRules.DefaultDirection(type),
            Status = endedAt.HasValue ? RelationshipStatus.Ended : RelationshipStatus.Active,
            Strength = strength,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

        relationship.AssignFrom(from);
        relationship.AssignTo(to);

        return relationship;
    }

    /// <summary>Ends the relationship, preserving it as history.</summary>
    public void End(UserId endedBy, DateTimeOffset endedAt, DateTimeOffset now)
    {
        if (Status == RelationshipStatus.Ended)
        {
            throw new DomainException("Relationship has already ended.");
        }

        if (StartedAt is { } start && endedAt < start)
        {
            throw new DomainException("A relationship cannot end before it started.");
        }

        Status = RelationshipStatus.Ended;
        EndedAt = endedAt;
        EndedBy = endedBy;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Records or revises the subjective strength of the relationship.</summary>
    public void SetStrength(int? strength, DateTimeOffset now)
    {
        if (strength is not null and (< 1 or > 5))
        {
            throw new DomainException("Strength is recorded on a scale of 1 to 5.");
        }

        Strength = strength;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Determines whether this relationship touches the given party.</summary>
    public bool Involves(RelationshipEndpoint party) => From == party || To == party;

    private void AssignFrom(RelationshipEndpoint endpoint)
    {
        FromPersonId = endpoint.AsPerson;
        FromCompanyId = endpoint.AsCompany;
    }

    private void AssignTo(RelationshipEndpoint endpoint)
    {
        ToPersonId = endpoint.AsPerson;
        ToCompanyId = endpoint.AsCompany;
    }
}
