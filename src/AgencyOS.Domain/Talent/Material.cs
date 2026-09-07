using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Domain.Talent;

/// <summary>Opaque, immutable identifier for a <see cref="Material"/>.</summary>
public readonly record struct MaterialId(Guid Value)
{
    public static MaterialId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>The kind of artifact a material record describes.</summary>
public enum MaterialType
{
    Screenplay = 1,
    Pilot = 2,
    Treatment = 3,
    PitchDeck = 4,
    Reel = 5,
    Headshot = 6,
    Resume = 7,
    Bio = 8,
    Sample = 9,
    Lookbook = 10,
    Other = 99,
}

/// <summary>Whether a material is ready to be used.</summary>
public enum MaterialStatus
{
    Draft = 1,

    /// <summary>With the agency or the client for comment.</summary>
    InReview = 2,

    /// <summary>Good to send out.</summary>
    Ready = 3,

    /// <summary>Superseded. Kept, because knowing what was sent last year matters.</summary>
    Retired = 4,
}

/// <summary>
/// An artifact the agency represents on somebody's behalf.
/// </summary>
/// <remarks>
/// <para>
/// Metadata only. AgencyOS does not store the file in M4 - canonical document and
/// blob storage is M10 - and this record is careful not to pretend otherwise. A
/// material says a screenplay exists, what draft it is and whether it is ready to
/// send; it does not claim the system is holding it.
/// </para>
/// <para>
/// <see cref="ExternalUri"/> may point at somewhere the document actually lives,
/// and must be an absolute HTTP or HTTPS address. A device-local file path is
/// deliberately refused: it is meaningless to every other user, it breaks the
/// moment the file moves, and it leaks the author's account name into a shared
/// record. When M10 arrives, a document reference is added alongside this, and the
/// URI stays as the external link it always was.
/// </para>
/// </remarks>
public sealed class Material
{
    private Material()
    {
    }

    public MaterialId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The person this material belongs to.</summary>
    public PersonId PersonId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public MaterialType Type { get; private set; }

    public MaterialStatus Status { get; private set; }

    /// <summary>Which draft or cut this is, as the client labels it.</summary>
    public string? VersionLabel { get; private set; }

    /// <summary>Where the document lives, when it lives somewhere addressable.</summary>
    public string? ExternalUri { get; private set; }

    /// <summary>When the agency received it.</summary>
    public DateOnly? ReceivedOn { get; private set; }

    /// <summary>Who supplied it, so provenance is not guesswork later.</summary>
    public string? Source { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    public static Material Create(
        OrganizationId organizationId,
        PersonId personId,
        string title,
        MaterialType type,
        UserId createdBy,
        DateTimeOffset now,
        MaterialStatus status = MaterialStatus.Draft,
        string? versionLabel = null,
        string? externalUri = null,
        DateOnly? receivedOn = null,
        string? source = null,
        string? notes = null)
    {
        return new Material
        {
            Id = MaterialId.New(),
            OrganizationId = organizationId,
            PersonId = personId,
            Title = Ensure.NotBlankMax(title, nameof(title), 512),
            Type = type,
            Status = status,
            VersionLabel = Ensure.OptionalMax(versionLabel, nameof(versionLabel), 128),
            ExternalUri = ValidUri(externalUri),
            ReceivedOn = receivedOn,
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
        MaterialType type,
        MaterialStatus status,
        DateTimeOffset now,
        string? versionLabel = null,
        string? externalUri = null,
        DateOnly? receivedOn = null,
        string? source = null,
        string? notes = null)
    {
        Title = Ensure.NotBlankMax(title, nameof(title), 512);
        Type = type;
        Status = status;
        VersionLabel = Ensure.OptionalMax(versionLabel, nameof(versionLabel), 128);
        ExternalUri = ValidUri(externalUri);
        ReceivedOn = receivedOn;
        Source = Ensure.OptionalMax(source, nameof(source), 512);
        Notes = Ensure.OptionalMax(notes, nameof(notes), 2000);
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Fails unless the caller observed the current version.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(nameof(Material), Id.ToString(), expectedVersion, Version);
        }
    }

    /// <summary>
    /// Accepts an absolute HTTP or HTTPS address, and nothing else.
    /// </summary>
    /// <remarks>
    /// A <c>file:</c> URI or a Windows path is refused rather than stored. It would
    /// resolve only on the machine that supplied it, so recording it server-side
    /// would put something in a shared record that is true for exactly one person -
    /// and would carry their account name with it.
    /// </remarks>
    private static string? ValidUri(string? value)
    {
        string? trimmed = Ensure.OptionalMax(value, "externalUri", 2048);

        if (trimmed is null)
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException(
                "A material link must be an absolute http or https address. "
                    + "A local file path cannot be stored: it would only resolve on the machine it came from. "
                    + "Document storage arrives in a later milestone.");
        }

        return uri.ToString();
    }
}
