using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Projects;

/// <summary>Opaque, immutable identifier for a <see cref="SourceProperty"/>.</summary>
public readonly record struct SourcePropertyId(Guid Value)
{
    public static SourcePropertyId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>What kind of underlying property a project derives from.</summary>
public enum SourcePropertyType
{
    OriginalScreenplay = 1,
    Book = 2,
    Article = 3,
    Podcast = 4,
    LifeRights = 5,
    SourceFilm = 6,
    Franchise = 7,
    StageWork = 8,
    OriginalConcept = 9,
    Other = 99,
}

/// <summary>
/// An underlying property a project is or could be based on, as described.
/// </summary>
/// <remarks>
/// <para>
/// The name is chosen carefully. This is a <em>source property</em>, not a rights
/// record: it describes what a project comes from and says nothing whatever about
/// who owns it, whether it is available, whether an option exists, or what
/// territories or windows apply. Chain of title, options and rights are M8, and a
/// model that implied AgencyOS had verified any of that would be worse than having
/// no model at all - somebody would rely on it (ADR-0019).
/// </para>
/// <para>
/// <see cref="AttributedCreator"/> is free text because the author of a novel is
/// usually not somebody the agency represents, and manufacturing a
/// <c>Person</c> row for every credited author would fill the directory with
/// people nobody has a relationship with. <see cref="CreatorPersonId"/> is there
/// for the case where the creator genuinely is in the directory.
/// </para>
/// </remarks>
public sealed class SourceProperty
{
    private SourceProperty()
    {
    }

    public SourcePropertyId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public SourcePropertyType Type { get; private set; }

    /// <summary>The creator as reported. Attribution, not a rights assertion.</summary>
    public string? AttributedCreator { get; private set; }

    /// <summary>The creator, when they are somebody the agency actually has a record for.</summary>
    public PersonId? CreatorPersonId { get; private set; }

    /// <summary>An ISBN, a URL, a publication - whatever identifies the property externally.</summary>
    public string? SourceReference { get; private set; }

    /// <summary>Where the agency's information about this property came from.</summary>
    public string? Provenance { get; private set; }

    /// <summary>Year of publication or release, when known.</summary>
    public int? Year { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public static SourceProperty Create(
        OrganizationId organizationId,
        string title,
        SourcePropertyType type,
        UserId createdBy,
        DateTimeOffset now,
        string? attributedCreator = null,
        PersonId? creatorPersonId = null,
        string? sourceReference = null,
        string? provenance = null,
        int? year = null,
        string? notes = null)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Unknown source property type '{type}'.");
        }

        return new SourceProperty
        {
            Id = SourcePropertyId.New(),
            OrganizationId = organizationId,
            Title = Ensure.NotBlankMax(title, nameof(title), 400),
            Type = type,
            AttributedCreator = Ensure.OptionalMax(attributedCreator, nameof(attributedCreator), 400),
            CreatorPersonId = creatorPersonId,
            SourceReference = Ensure.OptionalMax(sourceReference, nameof(sourceReference), 1000),
            Provenance = Ensure.OptionalMax(provenance, nameof(provenance), 400),
            Year = ValidYear(year),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    public void Update(
        string title,
        SourcePropertyType type,
        DateTimeOffset now,
        int expectedVersion,
        string? attributedCreator = null,
        PersonId? creatorPersonId = null,
        string? sourceReference = null,
        string? provenance = null,
        int? year = null,
        string? notes = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(type))
        {
            throw new DomainException($"Unknown source property type '{type}'.");
        }

        Title = Ensure.NotBlankMax(title, nameof(title), 400);
        Type = type;
        AttributedCreator = Ensure.OptionalMax(attributedCreator, nameof(attributedCreator), 400);
        CreatorPersonId = creatorPersonId;
        SourceReference = Ensure.OptionalMax(sourceReference, nameof(sourceReference), 1000);
        Provenance = Ensure.OptionalMax(provenance, nameof(provenance), 400);
        Year = ValidYear(year);
        Notes = Ensure.OptionalMax(notes, nameof(notes), 4000);

        UpdatedAt = now;
        Version++;
    }

    /// <summary>Refuses a mutation built on a version the caller no longer holds.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(SourceProperty),
                Id.Value.ToString(),
                expectedVersion,
                Version);
        }
    }

    private static int? ValidYear(int? year)
    {
        if (year is null)
        {
            return null;
        }

        if (year is < 1450 or > 2200)
        {
            throw new DomainException(
                $"A source property year of {year} is outside the range this system accepts "
                    + "(1450-2200).");
        }

        return year;
    }
}
