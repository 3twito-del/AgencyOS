using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;

namespace AgencyOS.Domain.Talent;

/// <summary>Opaque, immutable identifier for a <see cref="Credit"/>.</summary>
public readonly record struct CreditId(Guid Value)
{
    public static CreditId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// The kind of contribution a credit records.
/// </summary>
/// <remarks>
/// Deliberately not acting-shaped. A writing or directing credit has to fit as
/// naturally as a performance, so the type says what the contribution was and the
/// free-text role says the specifics - a character name for a performance, a job
/// title for everything else.
/// </remarks>
public enum CreditType
{
    Performance = 1,
    Writing = 2,
    Directing = 3,
    Producing = 4,
    Showrunning = 5,
    Composing = 6,
    Authorship = 7,
    Other = 99,
}

/// <summary>Where a credited work has got to.</summary>
public enum CreditStatus
{
    Announced = 1,
    InProduction = 2,
    Completed = 3,
    Released = 4,
}

/// <summary>
/// A piece of work somebody is credited on.
/// </summary>
/// <remarks>
/// <para>
/// M4 has no canonical Project entity - that is M5 - so a credit records the work
/// by title, as reported. <see cref="ProjectId"/> is a deliberate seam: it is
/// nullable, carries no foreign key yet, and exists so that M5 can link credits to
/// canonical projects by filling it in. The title stays as recorded either way,
/// because what somebody's credit said is itself a fact worth keeping.
/// </para>
/// <para>
/// Inventing placeholder Project rows now would be worse: they would look
/// canonical, other things would start referencing them, and M5 would have to
/// unpick real relationships rather than fill in a column.
/// </para>
/// </remarks>
public sealed class Credit
{
    private Credit()
    {
    }

    public CreditId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The person credited.</summary>
    public PersonId PersonId { get; private set; }

    /// <summary>Title of the work, as reported.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// The specific role: a character name for a performance, a job title otherwise.
    /// </summary>
    public string? Role { get; private set; }

    public CreditType Type { get; private set; }

    public CreditStatus Status { get; private set; }

    /// <summary>Year the work is dated to, where known.</summary>
    public int? Year { get; private set; }

    /// <summary>Studio, network or production company, when it is one the agency has a record of.</summary>
    public CompanyId? CompanyId { get; private set; }

    /// <summary>Where this information came from, so a disputed credit can be traced.</summary>
    public string? Source { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>
    /// The canonical project this credit belongs to, when one has been identified.
    /// </summary>
    /// <remarks>
    /// The seam M4 left open, closed by M5 with a real foreign key. Still nullable,
    /// and deliberately so: most historical credits are for work the agency had
    /// nothing to do with and will never have a project record. Linking is always
    /// an explicit act - a credit is never matched to a project because the titles
    /// look alike (ADR-0019).
    /// </remarks>
    public ProjectId? ProjectId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public static Credit Create(
        OrganizationId organizationId,
        PersonId personId,
        string title,
        CreditType type,
        UserId createdBy,
        DateTimeOffset now,
        string? role = null,
        CreditStatus status = CreditStatus.Released,
        int? year = null,
        CompanyId? companyId = null,
        string? source = null,
        string? notes = null)
    {
        return new Credit
        {
            Id = CreditId.New(),
            OrganizationId = organizationId,
            PersonId = personId,
            Title = Ensure.NotBlankMax(title, nameof(title), 512),
            Role = Ensure.OptionalMax(role, nameof(role), 256),
            Type = type,
            Status = status,
            Year = ValidYear(year),
            CompanyId = companyId,
            Source = Ensure.OptionalMax(source, nameof(source), 512),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    public void Update(
        string title,
        CreditType type,
        CreditStatus status,
        DateTimeOffset now,
        string? role = null,
        int? year = null,
        CompanyId? companyId = null,
        string? source = null,
        string? notes = null)
    {
        Title = Ensure.NotBlankMax(title, nameof(title), 512);
        Role = Ensure.OptionalMax(role, nameof(role), 256);
        Type = type;
        Status = status;
        Year = ValidYear(year);
        CompanyId = companyId;
        Source = Ensure.OptionalMax(source, nameof(source), 512);
        Notes = Ensure.OptionalMax(notes, nameof(notes), 2000);
        UpdatedAt = now;
        Version++;
    }

    /// <summary>
    /// Links this credit to a canonical project, or clears the link.
    /// </summary>
    /// <remarks>
    /// The recorded <see cref="Title"/> is left alone either way. What a credit
    /// said is a fact about the credit, and it stays true whether or not the agency
    /// has since built a project record for the same work.
    /// </remarks>
    public void LinkToProject(ProjectId? projectId, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (ProjectId == projectId)
        {
            return;
        }

        ProjectId = projectId;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Fails unless the caller observed the current version.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(nameof(Credit), Id.ToString(), expectedVersion, Version);
        }
    }

    /// <summary>
    /// Rejects a year that cannot be a credit.
    /// </summary>
    /// <remarks>
    /// Bounded rather than unbounded because a mistyped year is common and silently
    /// storing 202 or 20255 makes every chronological view wrong. The upper bound
    /// allows announced future work.
    /// </remarks>
    private static int? ValidYear(int? year)
    {
        if (year is not { } value)
        {
            return null;
        }

        if (value is < 1850 or > 2200)
        {
            throw new DomainException($"A credit year of {value} is not plausible.");
        }

        return value;
    }
}
