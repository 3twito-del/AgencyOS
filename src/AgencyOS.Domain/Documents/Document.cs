using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Documents;

/// <summary>Opaque, immutable identifier for a <see cref="Document"/>.</summary>
public readonly record struct DocumentId(Guid Value)
{
    public static DocumentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for a <see cref="DocumentVersion"/>.</summary>
public readonly record struct DocumentVersionId(Guid Value)
{
    public static DocumentVersionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What a document is, in business terms.
/// </summary>
/// <remarks>
/// Business meaning, not file format. A contract may arrive as a PDF or a DOCX and
/// is a contract either way; a PDF may be a contract, a headshot or an invoice.
/// <c>MediaType</c> on the version carries the format (ADR-0024).
/// </remarks>
public enum DocumentKind
{
    Contract = 1,
    ContractDraft = 2,
    SideLetter = 3,
    Amendment = 4,
    Script = 5,
    Treatment = 6,
    PitchDeck = 7,
    Headshot = 8,
    Resume = 9,
    Bio = 10,
    Reel = 11,
    Invoice = 12,
    Remittance = 13,
    Statement = 14,
    CorrespondenceAttachment = 15,
    DealMemo = 16,
    Other = 99,
}

/// <summary>Whether a document is in active use.</summary>
/// <remarks>
/// Archiving hides a document from ordinary lists. It destroys nothing: the
/// versions stay, the bytes stay, and the links stay, because a document somebody
/// archived is still evidence of what the agency held (ADR-0024).
/// </remarks>
public enum DocumentStatus
{
    Active = 1,
    Archived = 2,
}

/// <summary>
/// How sensitive a document is, as a person classified it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Never inferred.</strong> Not from the filename, not from the folder, not
/// from the document kind, and not from what it is linked to. Privilege in
/// particular is a legal conclusion a lawyer draws, and a system that guessed it
/// would be wrong in both directions: labelling ordinary correspondence privileged,
/// and leaving genuinely privileged advice unmarked (ADR-0022, ADR-0024).
/// </para>
/// <para>
/// Sensitivity gates the document itself. Access to a linked deal, contract or
/// invoice confers nothing.
/// </para>
/// </remarks>
public enum DocumentSensitivity
{
    /// <summary>Ordinary agency material. Visible to anybody who may read documents.</summary>
    Internal = 1,

    /// <summary>Commercially sensitive, but not legally privileged.</summary>
    Confidential = 2,

    /// <summary>Legal advice or work product. Behind its own grant.</summary>
    Privileged = 3,

    /// <summary>Financial records. Gated by the finance grants as well.</summary>
    Financial = 4,

    /// <summary>Restricted to an explicitly narrow readership.</summary>
    Restricted = 5,
}

/// <summary>Where a version's bytes came from.</summary>
public enum DocumentVersionSource
{
    /// <summary>A person uploaded the file.</summary>
    Upload = 1,

    /// <summary>Ingested from a message attachment.</summary>
    EmailAttachment = 2,

    /// <summary>Imported from another system, with its own reference.</summary>
    Import = 3,

    /// <summary>Produced by AgencyOS itself.</summary>
    Generated = 4,
}

/// <summary>What happened when text extraction was attempted.</summary>
/// <remarks>
/// Extraction is a derived projection and never the canonical content. A failure
/// costs nothing: the document is still a document and the bytes are untouched
/// (ADR-0024).
/// </remarks>
public enum TextExtractionState
{
    /// <summary>Nothing has been attempted.</summary>
    Pending = 1,

    /// <summary>Text was extracted.</summary>
    Extracted = 2,

    /// <summary>This build has no extractor for the format.</summary>
    Unsupported = 3,

    /// <summary>An extractor ran and could not finish.</summary>
    Failed = 4,
}

/// <summary>
/// The logical identity of a document.
/// </summary>
/// <remarks>
/// <para>
/// A document is the thing people talk about — "the Northgate writer agreement" —
/// and it outlives every particular file. Its metadata is mutable under optimistic
/// concurrency; its content is not, because content lives on
/// <see cref="DocumentVersion"/> rows that are never rewritten (ADR-0024).
/// </para>
/// <para>
/// The current version is <strong>derived</strong> from the versions rather than
/// stored as a pointer. A stored pointer is a second fact that can disagree with
/// the rows beneath it, which is the failure M9 spent a milestone avoiding.
/// </para>
/// </remarks>
public sealed class Document
{
    private readonly List<DocumentVersion> _versions = [];
    private readonly List<DocumentLink> _links = [];

    private Document()
    {
    }

    public DocumentId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public DocumentKind Kind { get; private set; }

    public DocumentStatus Status { get; private set; }

    public DocumentSensitivity Sensitivity { get; private set; }

    /// <summary>The agency's own handle for it, when it has one.</summary>
    public string? Reference { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Why it was archived, when it was.</summary>
    public string? ArchiveReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    /// <summary>Every version ever recorded, oldest first. Never rewritten.</summary>
    public IReadOnlyList<DocumentVersion> Versions => _versions;

    /// <summary>What this document is about.</summary>
    public IReadOnlyList<DocumentLink> Links => _links;

    /// <summary>The newest version, derived rather than stored.</summary>
    public DocumentVersion? CurrentVersion =>
        _versions.Count == 0 ? null : _versions.MaxBy(x => x.Sequence);

    /// <summary>Whether AgencyOS actually holds bytes for this document.</summary>
    /// <remarks>
    /// False for a document recorded before any file arrived. The M8 contract
    /// version published the same fact for the same reason: a reader who assumes a
    /// file is there will go looking for it during an argument.
    /// </remarks>
    public bool HoldsContent => _versions.Count > 0;

    public static Document Record(
        OrganizationId organizationId,
        string title,
        DocumentKind kind,
        DocumentSensitivity sensitivity,
        UserId createdBy,
        DateTimeOffset now,
        string? reference = null,
        string? description = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"'{kind}' is not a document kind.");
        }

        if (!Enum.IsDefined(sensitivity))
        {
            throw new DomainException(
                $"'{sensitivity}' is not a sensitivity. It must be chosen, never guessed.");
        }

        return new Document
        {
            Id = DocumentId.New(),
            OrganizationId = organizationId,
            Title = Ensure.NotBlankMax(title, nameof(title), 300),
            Kind = kind,
            Status = DocumentStatus.Active,
            Sensitivity = sensitivity,
            Reference = Ensure.OptionalMax(reference, nameof(reference), 100),
            Description = Ensure.OptionalMax(description, nameof(description), 2000),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };
    }

    /// <summary>Changes what the document says about itself. Never its content.</summary>
    public void Update(
        string title,
        DocumentKind kind,
        DocumentSensitivity sensitivity,
        int expectedVersion,
        DateTimeOffset now,
        string? reference = null,
        string? description = null)
    {
        RequireVersion(expectedVersion);
        RequireActive();

        Title = Ensure.NotBlankMax(title, nameof(title), 300);
        Kind = kind;
        Sensitivity = sensitivity;
        Reference = Ensure.OptionalMax(reference, nameof(reference), 100);
        Description = Ensure.OptionalMax(description, nameof(description), 2000);

        Touch(now);
    }

    /// <summary>
    /// Adds a version.
    /// </summary>
    /// <remarks>
    /// Always a new sequence number. There is no path that rewrites an existing
    /// version's bytes, hash or storage key, because somebody read that version and
    /// may have relied on it (ADR-0024).
    /// </remarks>
    public DocumentVersion AddVersion(
        BlobObjectId blobObjectId,
        string contentHash,
        long byteLength,
        string displayFileName,
        string mediaType,
        DocumentVersionSource source,
        UserId createdBy,
        DateTimeOffset now,
        string? sourceExternalReference = null,
        string? notes = null)
    {
        RequireActive();

        DocumentVersion version = DocumentVersion.Record(
            OrganizationId,
            Id,
            _versions.Count == 0 ? 1 : _versions.Max(x => x.Sequence) + 1,
            blobObjectId,
            contentHash,
            byteLength,
            displayFileName,
            mediaType,
            source,
            createdBy,
            now,
            sourceExternalReference,
            notes);

        _versions.Add(version);
        Touch(now);

        return version;
    }

    /// <summary>Connects the document to something it is about.</summary>
    public DocumentLink Link(
        DocumentLinkTarget target,
        Guid targetId,
        UserId linkedBy,
        DateTimeOffset now,
        string? note = null)
    {
        if (_links.Any(x => x.Target == target && x.TargetId == targetId))
        {
            throw new DomainException("This document is already linked to that record.");
        }

        DocumentLink link = DocumentLink.Create(
            OrganizationId, Id, target, targetId, linkedBy, now, note);

        _links.Add(link);
        Touch(now);

        return link;
    }

    public DocumentLink Unlink(Guid linkId, DateTimeOffset now)
    {
        DocumentLink link = _links.SingleOrDefault(x => x.Id == linkId)
            ?? throw new DomainException("That link is not on this document.");

        _links.Remove(link);
        Touch(now);

        return link;
    }

    /// <summary>
    /// Takes the document out of ordinary use.
    /// </summary>
    /// <remarks>
    /// Hides it. Destroys nothing: versions, bytes and links survive, because a
    /// document somebody archived is still evidence of what the agency held
    /// (ADR-0024).
    /// </remarks>
    public void Archive(string reason, int expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);

        if (Status == DocumentStatus.Archived)
        {
            throw new DomainException("This document is already archived.");
        }

        Status = DocumentStatus.Archived;
        ArchiveReason = Ensure.NotBlankMax(reason, nameof(reason), 500);

        Touch(now);
    }

    public void Restore(int expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);

        if (Status == DocumentStatus.Active)
        {
            throw new DomainException("This document is not archived.");
        }

        Status = DocumentStatus.Active;
        ArchiveReason = null;

        Touch(now);
    }

    private void RequireActive()
    {
        if (Status == DocumentStatus.Archived)
        {
            throw new DomainException(
                "This document is archived. Restore it before changing it.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(Document), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>
/// One immutable snapshot of a document's content.
/// </summary>
/// <remarks>
/// <para>
/// A version is a fact about what the agency held at a moment. Its bytes, its
/// digest and its storage key never change; updating a document produces version
/// N+1 rather than rewriting version N, because somebody read version N and formed
/// a view on it (ADR-0024).
/// </para>
/// <para>
/// This is a fifth kind of immutability in AgencyOS and it is deliberately its own
/// thing: audit immutability answers a security question, M7 offer immutability
/// freezes what was proposed, M8 contract-term immutability freezes what a draft
/// said, M9 journal immutability freezes what the books said, and this freezes what
/// a file was.
/// </para>
/// </remarks>
public sealed class DocumentVersion
{
    private DocumentVersion()
    {
    }

    public DocumentVersionId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public DocumentId DocumentId { get; private set; }

    /// <summary>1, 2, 3... in the order versions were recorded.</summary>
    public int Sequence { get; private set; }

    public BlobObjectId BlobObjectId { get; private set; }

    /// <summary>
    /// The digest of the bytes, copied onto the version.
    /// </summary>
    /// <remarks>
    /// Denormalised on purpose. A version is a historical claim about specific
    /// bytes, and it should be able to state that claim without depending on
    /// another row still being there to answer for it.
    /// </remarks>
    public string ContentHash { get; private set; } = string.Empty;

    public long ByteLength { get; private set; }

    /// <summary>What the file was called where it came from. Never used as a path.</summary>
    public string DisplayFileName { get; private set; } = string.Empty;

    public string MediaType { get; private set; } = string.Empty;

    public DocumentVersionSource Source { get; private set; }

    /// <summary>The sending system's own identifier, when there was one.</summary>
    public string? SourceExternalReference { get; private set; }

    /// <summary>
    /// When AgencyOS was told about this version.
    /// </summary>
    /// <remarks>
    /// Not when the document was authored. AgencyOS does not know that, does not
    /// read it out of file metadata, and does not claim it.
    /// </remarks>
    public DateTimeOffset RecordedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public string? Notes { get; private set; }

    public TextExtractionState ExtractionState { get; private set; }

    /// <summary>
    /// Text pulled out of the bytes, when a deterministic extractor could.
    /// </summary>
    /// <remarks>
    /// A derived projection, never the canonical content. It is not indexed for
    /// general search in M10, and it is readable only through the same
    /// authorization that guards the document itself (ADR-0024).
    /// </remarks>
    public string? ExtractedText { get; private set; }

    /// <summary>Why extraction did not produce text, when it did not.</summary>
    public string? ExtractionDetail { get; private set; }

    internal static DocumentVersion Record(
        OrganizationId organizationId,
        DocumentId documentId,
        int sequence,
        BlobObjectId blobObjectId,
        string contentHash,
        long byteLength,
        string displayFileName,
        string mediaType,
        DocumentVersionSource source,
        UserId createdBy,
        DateTimeOffset now,
        string? sourceExternalReference,
        string? notes) =>
        new()
        {
            Id = DocumentVersionId.New(),
            OrganizationId = organizationId,
            DocumentId = documentId,
            Sequence = sequence,
            BlobObjectId = blobObjectId,
            ContentHash = Ensure.NotBlankMax(contentHash, nameof(contentHash), 64),
            ByteLength = byteLength,
            DisplayFileName = Ensure.NotBlankMax(displayFileName, nameof(displayFileName), 300),
            MediaType = Ensure.NotBlankMax(mediaType, nameof(mediaType), 150),
            Source = source,
            SourceExternalReference =
                Ensure.OptionalMax(sourceExternalReference, nameof(sourceExternalReference), 500),
            RecordedAt = now,
            CreatedBy = createdBy,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
            ExtractionState = TextExtractionState.Pending,
        };

    /// <summary>Records what extraction produced, including having produced nothing.</summary>
    public void RecordExtraction(
        TextExtractionState state,
        string? text = null,
        string? detail = null)
    {
        if (state == TextExtractionState.Pending)
        {
            throw new DomainException(
                "Pending is the absence of an extraction result, not a result.");
        }

        ExtractionState = state;
        ExtractedText = state == TextExtractionState.Extracted ? text : null;
        ExtractionDetail = Ensure.OptionalMax(detail, nameof(detail), 500);
    }
}
