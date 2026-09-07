using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.People;

/// <summary>
/// A human the agency holds records about.
/// </summary>
/// <remarks>
/// <para>
/// One identity model for every person, whatever they do. There is no separate
/// Actor, Writer or Agent entity: what someone does professionally is described
/// by <see cref="Title"/> and by the relationships they hold, not by their type.
/// Representation arrives in M4 as a relationship, not as a different kind of
/// human.
/// </para>
/// <para>
/// <see cref="OrganizationId"/> is the owning tenant (ADR-0010), not an employer.
/// The employer, if known, is <see cref="PrimaryCompanyId"/>.
/// </para>
/// </remarks>
public sealed class Person
{
    private Person()
    {
    }

    public PersonId Id { get; private set; }

    /// <summary>The tenant that owns this record.</summary>
    public OrganizationId OrganizationId { get; private set; }

    /// <summary>Name as it should be shown. Derived on creation when not supplied.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string? MiddleName { get; private set; }

    public string? LastName { get; private set; }

    /// <summary>What they actually go by, when it differs from their first name.</summary>
    public string? PreferredName { get; private set; }

    public PersonStatus Status { get; private set; }

    /// <summary>Company they are principally associated with, when known.</summary>
    public CompanyId? PrimaryCompanyId { get; private set; }

    /// <summary>Free-text professional role, for example "Literary Agent".</summary>
    public string? Title { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>Unstructured judgment, kept distinct from structured fact.</summary>
    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

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

    public static Person Create(
        OrganizationId organizationId,
        string firstName,
        string? lastName,
        UserId createdBy,
        DateTimeOffset now,
        string? displayName = null,
        string? middleName = null,
        string? preferredName = null,
        CompanyId? primaryCompanyId = null,
        string? title = null,
        string? email = null,
        string? phone = null,
        string? notes = null)
    {
        string first = Ensure.NotBlankMax(firstName, nameof(firstName), 128);
        string? last = Ensure.OptionalMax(lastName, nameof(lastName), 128);

        return new Person
        {
            Id = PersonId.New(),
            OrganizationId = organizationId,
            FirstName = first,
            MiddleName = Ensure.OptionalMax(middleName, nameof(middleName), 128),
            LastName = last,
            PreferredName = Ensure.OptionalMax(preferredName, nameof(preferredName), 128),
            DisplayName = ResolveDisplayName(displayName, first, last, preferredName),
            Status = PersonStatus.Active,
            PrimaryCompanyId = primaryCompanyId,
            Title = Ensure.OptionalMax(title, nameof(title), 256),
            Email = Ensure.OptionalMax(email, nameof(email), 320),
            Phone = Ensure.OptionalMax(phone, nameof(phone), 64),
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    /// <summary>Updates the descriptive fields of a person.</summary>
    /// <remarks>
    /// Deliberately not a general property setter. Status changes are separate
    /// commands, and the owning tenant is never editable.
    /// </remarks>
    public void Update(
        string firstName,
        string? lastName,
        DateTimeOffset now,
        string? displayName = null,
        string? middleName = null,
        string? preferredName = null,
        CompanyId? primaryCompanyId = null,
        string? title = null,
        string? email = null,
        string? phone = null,
        string? notes = null)
    {
        if (Status != PersonStatus.Active)
        {
            throw new DomainException("An archived person cannot be edited. Restore them first.");
        }

        string first = Ensure.NotBlankMax(firstName, nameof(firstName), 128);
        string? last = Ensure.OptionalMax(lastName, nameof(lastName), 128);

        FirstName = first;
        LastName = last;
        MiddleName = Ensure.OptionalMax(middleName, nameof(middleName), 128);
        PreferredName = Ensure.OptionalMax(preferredName, nameof(preferredName), 128);
        DisplayName = ResolveDisplayName(displayName, first, last, preferredName);
        PrimaryCompanyId = primaryCompanyId;
        Title = Ensure.OptionalMax(title, nameof(title), 256);
        Email = Ensure.OptionalMax(email, nameof(email), 320);
        Phone = Ensure.OptionalMax(phone, nameof(phone), 64);
        Notes = notes;
        UpdatedAt = now;
        Version++;
    }

    public void Archive(DateTimeOffset now)
    {
        if (Status == PersonStatus.Archived)
        {
            throw new DomainException("Person is already archived.");
        }

        Status = PersonStatus.Archived;
        UpdatedAt = now;
        Version++;
    }

    public void Restore(DateTimeOffset now)
    {
        if (Status == PersonStatus.Active)
        {
            throw new DomainException("Person is already active.");
        }

        Status = PersonStatus.Active;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Builds a display name when the caller did not supply one.
    /// </summary>
    /// <remarks>
    /// Preferred name wins over first name, because it is what the person is
    /// actually called. Names that do not split into given and family parts are
    /// common, so a single-token name is a valid result rather than an error.
    /// </remarks>
    private static string ResolveDisplayName(
        string? supplied,
        string firstName,
        string? lastName,
        string? preferredName)
    {
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            return Ensure.NotBlankMax(supplied, nameof(supplied), 256);
        }

        string given = string.IsNullOrWhiteSpace(preferredName) ? firstName : preferredName.Trim();
        string composed = string.IsNullOrWhiteSpace(lastName) ? given : $"{given} {lastName.Trim()}";

        return Ensure.NotBlankMax(composed, nameof(composed), 256);
    }
}
