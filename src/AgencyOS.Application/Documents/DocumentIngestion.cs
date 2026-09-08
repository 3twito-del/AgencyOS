using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Documents;

/// <summary>Bytes that have been durably stored but that nothing yet references.</summary>
/// <param name="IngestionId">The ledger row that records the attempt.</param>
/// <param name="BlobObjectId">
/// The blob record, whether created now or reused from identical bytes already
/// held by this organization.
/// </param>
public sealed record StagedContent(
    BlobIngestionId IngestionId,
    BlobObjectId BlobObjectId,
    string ContentHash,
    long ByteLength,
    string MediaType,
    string FileName,
    bool Deduplicated);

/// <summary>
/// Gets bytes safely from a caller into the content store.
/// </summary>
/// <remarks>
/// <para>
/// A filesystem and PostgreSQL are two systems, and a write that touches both is
/// not one transaction. AgencyOS says so rather than pretending, and picks the
/// ordering that makes the surviving failure the harmless one (ADR-0024).
/// </para>
/// <para>
/// <strong>The ordering is: ledger, then bytes, then rows.</strong>
/// </para>
/// <list type="number">
/// <item>
/// An ingestion row is committed first, saying an attempt has begun. It costs one
/// insert and it is what lets a sweeper find the wreckage later without walking
/// the whole content store.
/// </item>
/// <item>
/// The bytes are streamed, hashed and published into the content store by an atomic
/// rename. After this the file is durable and complete or it does not exist; there
/// is no half-written state a reader can reach.
/// </item>
/// <item>
/// The blob, document and version rows are written in one database transaction. If
/// that transaction fails, the bytes remain and nothing points at them.
/// </item>
/// </list>
/// <para>
/// So the reachable failure is an orphaned blob: durable, unreferenced, invisible,
/// and collectable. The failure this ordering rules out is the one that would
/// actually hurt — a document row in every list that cannot be opened, because the
/// bytes it names were never written.
/// </para>
/// </remarks>
public sealed class DocumentIngestion
{
    private readonly IBlobStore _blobs;
    private readonly IBlobRepository _repository;
    private readonly IDocumentTextExtractor _extractor;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public DocumentIngestion(
        IBlobStore blobs,
        IBlobRepository repository,
        IDocumentTextExtractor extractor,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _blobs = blobs;
        _repository = repository;
        _extractor = extractor;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Streams bytes into the store and records that they are there.
    /// </summary>
    /// <remarks>
    /// Returns without having created a document. The caller then builds the
    /// document and version rows and calls <see cref="FinalizeAsync"/> in the same
    /// transaction, so the two halves of the operation are visibly separate rather
    /// than hidden inside one method that appears atomic.
    /// </remarks>
    public async Task<StagedContent> StageAsync(
        OrganizationId organizationId,
        Stream content,
        string fileName,
        string? declaredMediaType,
        UserId actor,
        long sizeLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        string safeName = UploadPolicy.SafeFileName(fileName);
        string mediaType = UploadPolicy.ResolveMediaType(declaredMediaType);

        if (content.CanSeek)
        {
            UploadPolicy.RequireAcceptableLength(content.Length, sizeLimit);
        }

        // Committed before a single byte is written, so a crash mid-upload leaves a
        // row saying an attempt began rather than leaving nothing at all.
        BlobIngestion ingestion = BlobIngestion.Start(organizationId, actor, _clock.UtcNow, safeName);

        _repository.AddIngestion(ingestion);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        BlobWriteResult written;

        try
        {
            written = await _blobs.PutAsync(organizationId, content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            ingestion.NoteAbandoned(Describe(failure), _clock.UtcNow);

            await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // A stream that could not be measured up front is measured now, after the
        // fact, because the alternative is trusting a Content-Length header.
        if (written.ByteLength > sizeLimit)
        {
            ingestion.NoteAbandoned("Larger than the upload limit.", _clock.UtcNow);

            await _blobs.DeleteAsync(organizationId, written.StorageKey, CancellationToken.None)
                .ConfigureAwait(false);

            await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            throw new DomainException(
                $"That file is {written.ByteLength:N0} bytes, and the limit is {sizeLimit:N0}.");
        }

        ingestion.NoteStored(written.StorageKey, written.ContentHash, written.ByteLength, _clock.UtcNow);

        // Identical bytes reuse the record. Two versions may share a blob and still
        // be readable by different people, because identical bytes are not the same
        // document (ADR-0024).
        BlobObject? existing = await _repository
            .FindByHashAsync(organizationId, written.ContentHash, cancellationToken)
            .ConfigureAwait(false);

        BlobObject blob = existing ?? BlobObject.Record(
            organizationId,
            written.ContentHash,
            written.ByteLength,
            written.StorageKey,
            _clock.UtcNow,
            mediaType);

        if (existing is null)
        {
            _repository.Add(blob);
        }

        return new StagedContent(
            ingestion.Id,
            blob.Id,
            written.ContentHash,
            written.ByteLength,
            mediaType,
            safeName,
            written.Deduplicated || existing is not null);
    }

    /// <summary>Records that a version now references the staged bytes.</summary>
    /// <remarks>
    /// Called inside the caller's transaction, so the ledger reaches
    /// <c>Finalized</c> exactly when the version becomes real.
    /// </remarks>
    public async Task FinalizeAsync(
        OrganizationId organizationId,
        BlobIngestionId ingestionId,
        CancellationToken cancellationToken = default)
    {
        BlobIngestion? ingestion = await _repository
            .FindIngestionAsync(organizationId, ingestionId, cancellationToken)
            .ConfigureAwait(false);

        ingestion?.NoteFinalized(_clock.UtcNow);
    }

    /// <summary>Abandons an attempt whose document never came into being.</summary>
    public async Task AbandonAsync(
        OrganizationId organizationId,
        BlobIngestionId ingestionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        BlobIngestion? ingestion = await _repository
            .FindIngestionAsync(organizationId, ingestionId, cancellationToken)
            .ConfigureAwait(false);

        ingestion?.NoteAbandoned(reason, _clock.UtcNow);
    }

    /// <summary>
    /// Extracts text from stored bytes, and records having failed to.
    /// </summary>
    /// <remarks>
    /// Never throws into the caller. A document whose text could not be read is
    /// still a document, and the failure is a state on the version rather than a
    /// reason to lose the upload (ADR-0024).
    /// </remarks>
    public async Task ExtractTextAsync(
        OrganizationId organizationId,
        DocumentVersion version,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (!_extractor.CanExtract(version.MediaType))
        {
            version.RecordExtraction(
                TextExtractionState.Unsupported,
                detail: $"This build extracts no text from {version.MediaType}.");

            return;
        }

        try
        {
            await using Stream content = await _blobs
                .OpenReadAsync(organizationId, storageKey, cancellationToken)
                .ConfigureAwait(false);

            DocumentTextExtraction extraction = await _extractor
                .ExtractAsync(content, version.MediaType, cancellationToken)
                .ConfigureAwait(false);

            version.RecordExtraction(extraction.State, extraction.Text, extraction.Detail);
        }
        catch (Exception failure)
        {
            version.RecordExtraction(TextExtractionState.Failed, detail: Describe(failure));
        }
    }

    /// <summary>
    /// Deletes bytes from attempts that never became a document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sweeper. It reads the ingestion ledger, not the content store, and it
    /// only deletes bytes that <em>no</em> blob record in the organization claims —
    /// which matters because deduplication means a second, successful ingestion may
    /// have adopted exactly these bytes since (ADR-0024).
    /// </para>
    /// <para>
    /// The grace period exists so a request still in flight is never swept out from
    /// under itself.
    /// </para>
    /// </remarks>
    public async Task<int> SweepAsync(
        TimeSpan gracePeriod,
        int limit,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset cutoff = _clock.UtcNow - gracePeriod;

        IReadOnlyList<BlobIngestion> stale = await _repository
            .ListStaleIngestionsAsync(cutoff, limit, cancellationToken)
            .ConfigureAwait(false);

        int swept = 0;

        foreach (BlobIngestion ingestion in stale)
        {
            if (ingestion.StorageKey is { } key && ingestion.ContentHash is { } hash)
            {
                bool referenced = await _repository
                    .AnyReferencesAsync(ingestion.OrganizationId, hash, cancellationToken)
                    .ConfigureAwait(false);

                if (referenced)
                {
                    // Another ingestion adopted these bytes. The attempt is over,
                    // but the file belongs to somebody now.
                    ingestion.NoteAbandoned(
                        "Superseded: the bytes are referenced by another document.", _clock.UtcNow);

                    swept++;
                    continue;
                }

                await _blobs.DeleteAsync(ingestion.OrganizationId, key, cancellationToken)
                    .ConfigureAwait(false);
            }

            ingestion.NoteAbandoned("Swept: no document version referenced it.", _clock.UtcNow);
            swept++;
        }

        if (swept > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return swept;
    }

    /// <summary>Describes a failure without leaking a path or a stack.</summary>
    private static string Describe(Exception failure) =>
        failure switch
        {
            DomainException domain => domain.Message,
            BlobNotFoundException => "The stored object is missing.",
            BlobIntegrityException => "The stored bytes do not match their recorded digest.",
            _ => failure.GetType().Name,
        };
}
