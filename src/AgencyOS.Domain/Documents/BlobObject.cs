using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Documents;

/// <summary>Opaque, immutable identifier for a <see cref="BlobObject"/>.</summary>
public readonly record struct BlobObjectId(Guid Value)
{
    public static BlobObjectId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for a <see cref="BlobIngestion"/>.</summary>
public readonly record struct BlobIngestionId(Guid Value)
{
    public static BlobIngestionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Where an ingestion attempt got to.
/// </summary>
/// <remarks>
/// The staging ledger exists so orphan cleanup reads PostgreSQL rather than
/// walking the whole content store. Every state below is a fact recorded before
/// the act it describes, so a crash always leaves a row that says what to check
/// (ADR-0024).
/// </remarks>
public enum BlobIngestionState
{
    /// <summary>Intent recorded. Bytes may or may not have started arriving.</summary>
    Staging = 1,

    /// <summary>
    /// Bytes are durably in the content store, but no <see cref="BlobObject"/> row
    /// references them yet.
    /// </summary>
    /// <remarks>
    /// The one genuinely ambiguous state, and the reason the ledger exists: the
    /// file is real and nothing points at it. A sweeper resolves it.
    /// </remarks>
    Stored = 2,

    /// <summary>A blob object and a document version now reference the bytes.</summary>
    Finalized = 3,

    /// <summary>The attempt failed or was swept. Any staged bytes are gone.</summary>
    Abandoned = 4,
}

/// <summary>
/// An ingestion attempt, recorded before any bytes are written.
/// </summary>
/// <remarks>
/// <para>
/// Filesystem and PostgreSQL are two systems and a write across them is not one
/// transaction. AgencyOS does not pretend otherwise. The ordering is chosen so the
/// only reachable inconsistency is harmless: <strong>bytes first, row second</strong>.
/// </para>
/// <para>
/// A crash can therefore leave durable bytes that nothing references. It cannot
/// leave a database row pointing at bytes that were never written, which is the
/// failure that would actually hurt — a document that exists in every list and
/// cannot be opened (ADR-0024).
/// </para>
/// </remarks>
public sealed class BlobIngestion
{
    private BlobIngestion()
    {
    }

    public BlobIngestionId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public BlobIngestionState State { get; private set; }

    /// <summary>Where the bytes live once promoted, so a sweeper can find them.</summary>
    public string? StorageKey { get; private set; }

    /// <summary>The digest of the bytes actually written.</summary>
    public string? ContentHash { get; private set; }

    public long? ByteLength { get; private set; }

    /// <summary>What the caller said the file was called. Never used as a path.</summary>
    public string? DisplayFileName { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId StartedBy { get; private set; }

    /// <summary>Why the attempt ended, when it ended badly.</summary>
    public string? FailureReason { get; private set; }

    public static BlobIngestion Start(
        OrganizationId organizationId,
        UserId startedBy,
        DateTimeOffset now,
        string? displayFileName = null) =>
        new()
        {
            Id = BlobIngestionId.New(),
            OrganizationId = organizationId,
            State = BlobIngestionState.Staging,
            DisplayFileName = Ensure.OptionalMax(displayFileName, nameof(displayFileName), 300),
            StartedAt = now,
            UpdatedAt = now,
            StartedBy = startedBy,
        };

    /// <summary>Records that the bytes are durably in the content store.</summary>
    public void NoteStored(string storageKey, string contentHash, long byteLength, DateTimeOffset now)
    {
        State = BlobIngestionState.Stored;
        StorageKey = Ensure.NotBlankMax(storageKey, nameof(storageKey), 400);
        ContentHash = Ensure.NotBlankMax(contentHash, nameof(contentHash), 64);
        ByteLength = byteLength;
        UpdatedAt = now;
    }

    /// <summary>Records that a document version now references the bytes.</summary>
    public void NoteFinalized(DateTimeOffset now)
    {
        State = BlobIngestionState.Finalized;
        UpdatedAt = now;
    }

    public void NoteAbandoned(string reason, DateTimeOffset now)
    {
        State = BlobIngestionState.Abandoned;
        FailureReason = Ensure.NotBlankMax(reason, nameof(reason), 500);
        UpdatedAt = now;
    }
}

/// <summary>
/// Immutable stored bytes, addressed by their own digest.
/// </summary>
/// <remarks>
/// <para>
/// A blob is bytes and nothing else. It carries no title, no business meaning and
/// no authorization of its own: those belong to the <see cref="Document"/> and the
/// <see cref="DocumentVersion"/> that reference it. Two versions may share a blob
/// and still be readable by different people, because identical bytes are not the
/// same document (ADR-0024).
/// </para>
/// <para>
/// <strong>Deduplication is per organization.</strong> A content-addressed store
/// shared across tenants would let one tenant discover another's files by
/// uploading a candidate and observing whether the store already had it. The
/// storage key is therefore scoped to the organization, and identical bytes in two
/// tenants are two blobs.
/// </para>
/// </remarks>
public sealed class BlobObject
{
    private BlobObject()
    {
    }

    public BlobObjectId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>Lowercase hex SHA-256 of the bytes as they were written.</summary>
    /// <remarks>
    /// Computed by AgencyOS while streaming, never taken from a client. A hash the
    /// server did not compute is a claim about identity it cannot support.
    /// </remarks>
    public string ContentHash { get; private set; } = string.Empty;

    public long ByteLength { get; private set; }

    /// <summary>The declared media type, when one is known and plausible.</summary>
    public string? MediaType { get; private set; }

    /// <summary>
    /// Where the store put the bytes.
    /// </summary>
    /// <remarks>
    /// Derived from the organization and the digest, so it contains nothing a
    /// caller supplied and cannot escape the storage root. Never returned by the
    /// API.
    /// </remarks>
    public string StorageKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Whether anything has scanned these bytes, and what it found.
    /// </summary>
    /// <remarks>
    /// <c>Unscanned</c> until a real scanner says otherwise. AgencyOS implements no
    /// malware detection and will not label a file clean on the strength of having
    /// stored it (ADR-0024).
    /// </remarks>
    public BlobScanState ScanState { get; private set; }

    public DateTimeOffset? ScannedAt { get; private set; }

    /// <summary>What the scanner was, when one ran.</summary>
    public string? ScannerName { get; private set; }

    public string? ScanDetail { get; private set; }

    public static BlobObject Record(
        OrganizationId organizationId,
        string contentHash,
        long byteLength,
        string storageKey,
        DateTimeOffset now,
        string? mediaType = null)
    {
        if (byteLength < 0)
        {
            throw new DomainException("A blob cannot have a negative length.");
        }

        return new BlobObject
        {
            Id = BlobObjectId.New(),
            OrganizationId = organizationId,
            ContentHash = RequireDigest(contentHash),
            ByteLength = byteLength,
            StorageKey = Ensure.NotBlankMax(storageKey, nameof(storageKey), 400),
            MediaType = Ensure.OptionalMax(mediaType, nameof(mediaType), 150),
            CreatedAt = now,
            ScanState = BlobScanState.Unscanned,
        };
    }

    /// <summary>
    /// Records what a scanner found.
    /// </summary>
    /// <remarks>
    /// Only a real scanner result may reach <see cref="BlobScanState.Clean"/>, and
    /// the scanner names itself on the record so a later reader can tell what the
    /// verdict was worth.
    /// </remarks>
    public void RecordScan(
        BlobScanState state,
        string scannerName,
        DateTimeOffset now,
        string? detail = null)
    {
        if (state == BlobScanState.Unscanned)
        {
            throw new DomainException(
                "A scan result cannot be 'unscanned'. That is the absence of a result.");
        }

        ScanState = state;
        ScannerName = Ensure.NotBlankMax(scannerName, nameof(scannerName), 100);
        ScannedAt = now;
        ScanDetail = Ensure.OptionalMax(detail, nameof(detail), 500);
    }

    /// <summary>Checks a digest is the shape SHA-256 actually produces.</summary>
    private static string RequireDigest(string? value)
    {
        string digest = Ensure.NotBlankMax(value, nameof(value), 64).ToLowerInvariant();

        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
        {
            throw new DomainException(
                "A content hash must be 64 lowercase hexadecimal characters of SHA-256.");
        }

        return digest;
    }
}

/// <summary>
/// What is known about whether stored bytes are safe.
/// </summary>
/// <remarks>
/// Four honest states. AgencyOS builds no antivirus; this is the seam a real
/// scanner plugs into, and until one does every blob stays <c>Unscanned</c>
/// (ADR-0024).
/// </remarks>
public enum BlobScanState
{
    /// <summary>Nothing has looked at these bytes. The default, and not a claim of safety.</summary>
    Unscanned = 1,

    /// <summary>A real scanner examined the bytes and found nothing.</summary>
    Clean = 2,

    /// <summary>A real scanner flagged the bytes.</summary>
    Suspect = 3,

    /// <summary>A scan was attempted and could not complete.</summary>
    Failed = 4,
}
