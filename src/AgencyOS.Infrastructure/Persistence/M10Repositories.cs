using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>Reads and writes documents.</summary>
public sealed class DocumentRepository : IDocumentRepository
{
    private readonly AgencyOsDbContext _context;

    public DocumentRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _context.Documents.Add(document);
    }

    /// <inheritdoc />
    public Task<Document?> FindAsync(
        OrganizationId organizationId,
        DocumentId id,
        CancellationToken cancellationToken = default) =>
        _context.Documents
            .Include(x => x.Versions)
            .Include(x => x.Links)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<DocumentVersion?> FindVersionAsync(
        OrganizationId organizationId,
        DocumentVersionId id,
        CancellationToken cancellationToken = default) =>
        _context.DocumentVersions
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Document?> FindByVersionAsync(
        OrganizationId organizationId,
        DocumentVersionId versionId,
        CancellationToken cancellationToken = default)
    {
        DocumentVersion? version = await _context.DocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == versionId, cancellationToken)
            .ConfigureAwait(false);

        return version is null
            ? null
            : await FindAsync(organizationId, version.DocumentId, cancellationToken)
                .ConfigureAwait(false);
    }
}

/// <summary>Reads and writes stored-byte records and the ingestion ledger.</summary>
public sealed class BlobRepository : IBlobRepository
{
    private readonly AgencyOsDbContext _context;

    public BlobRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(BlobObject blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        _context.BlobObjects.Add(blob);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The change tracker is consulted first, for the same reason as the lookup by
    /// hash: a blob added earlier in this very transaction is not visible to a
    /// query, and text extraction asks for the blob it has just staged. Without
    /// this the extractor found nothing on every first upload and every version
    /// stayed Pending for ever (ADR-0024).
    /// </remarks>
    public async Task<BlobObject?> FindAsync(
        OrganizationId organizationId,
        BlobObjectId id,
        CancellationToken cancellationToken = default)
    {
        BlobObject? pending = _context.ChangeTracker
            .Entries<BlobObject>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .FirstOrDefault(x => x.OrganizationId == organizationId && x.Id == id);

        return pending ?? await _context.BlobObjects
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BlobObject?> FindByHashAsync(
        OrganizationId organizationId,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        // The change tracker is consulted first, because a blob added earlier in
        // this same transaction is not yet visible to a query.
        BlobObject? pending = _context.ChangeTracker
            .Entries<BlobObject>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .FirstOrDefault(
                x => x.OrganizationId == organizationId && x.ContentHash == contentHash);

        return pending ?? await _context.BlobObjects
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.ContentHash == contentHash,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> AnyReferencesAsync(
        OrganizationId organizationId,
        string contentHash,
        CancellationToken cancellationToken = default) =>
        _context.BlobObjects
            .AnyAsync(
                x => x.OrganizationId == organizationId && x.ContentHash == contentHash,
                cancellationToken);

    /// <inheritdoc />
    public void AddIngestion(BlobIngestion ingestion)
    {
        ArgumentNullException.ThrowIfNull(ingestion);

        _context.BlobIngestions.Add(ingestion);
    }

    /// <inheritdoc />
    public Task<BlobIngestion?> FindIngestionAsync(
        OrganizationId organizationId,
        BlobIngestionId id,
        CancellationToken cancellationToken = default)
    {
        BlobIngestion? pending = _context.ChangeTracker
            .Entries<BlobIngestion>()
            .Select(x => x.Entity)
            .FirstOrDefault(x => x.OrganizationId == organizationId && x.Id == id);

        return pending is not null
            ? Task.FromResult<BlobIngestion?>(pending)
            : _context.BlobIngestions
                .FirstOrDefaultAsync(
                    x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlobIngestion>> ListStaleIngestionsAsync(
        DateTimeOffset olderThan,
        int limit,
        CancellationToken cancellationToken = default) =>
        await _context.BlobIngestions
            .Where(x =>
                (x.State == BlobIngestionState.Staging || x.State == BlobIngestionState.Stored)
                && x.UpdatedAt < olderThan)
            .OrderBy(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>Appends the curated document history.</summary>
public sealed class DocumentEventRepository : IDocumentEventRepository
{
    private readonly AgencyOsDbContext _context;

    public DocumentEventRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(DocumentEvent documentEvent)
    {
        ArgumentNullException.ThrowIfNull(documentEvent);

        _context.DocumentEvents.Add(documentEvent);
    }
}

/// <summary>Reads and writes connected mailboxes.</summary>
public sealed class CommunicationAccountRepository : ICommunicationAccountRepository
{
    private readonly AgencyOsDbContext _context;

    public CommunicationAccountRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(CommunicationAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        _context.CommunicationAccounts.Add(account);
    }

    /// <inheritdoc />
    public Task<CommunicationAccount?> FindAsync(
        OrganizationId organizationId,
        CommunicationAccountId id,
        CancellationToken cancellationToken = default) =>
        _context.CommunicationAccounts
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommunicationAccount>> ListAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        await _context.CommunicationAccounts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.MailboxAddress)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<CommunicationAccount?> ClaimForSyncAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Selecting and leasing in one statement, for the same reason the dispatch
        // claim does it: a row lock taken by a bare SELECT lasts only as long as
        // that statement's own implicit transaction, so two workers would read the
        // same mailbox and the second would discover the collision as a concurrency
        // failure rather than moving on to the next one (ADR-0029).
        DateTimeOffset expiresAt = now.Add(leaseDuration);

        List<Guid?> claimed = await _context.Database
            .SqlQuery<Guid?>(
                $"""
                 UPDATE communication_accounts
                    SET sync_lease_owner = {leaseOwner},
                        sync_lease_expires_at = {expiresAt},
                        version = version + 1
                  WHERE id = (
                        SELECT id
                        FROM communication_accounts
                        WHERE state = 1
                          AND (sync_lease_expires_at IS NULL
                               OR sync_lease_expires_at < {now})
                        ORDER BY COALESCE(last_synced_at, TIMESTAMPTZ '-infinity')
                        LIMIT 1
                        FOR UPDATE SKIP LOCKED)
                 RETURNING id AS "Value"
                 """)

            // Materialized rather than composed. An UPDATE ... RETURNING is not
            // something EF can wrap another SELECT around, and asking for the first
            // row would make it try.
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (claimed.FirstOrDefault() is not { } id)
        {
            return null;
        }

        // Read back after the lease, so the tracked entity carries the version the
        // database now holds.
        return await _context.CommunicationAccounts
            .FirstOrDefaultAsync(x => x.Id == new CommunicationAccountId(id), cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Reads and writes synchronized messages.</summary>
public sealed class CommunicationMessageRepository : ICommunicationMessageRepository
{
    private readonly AgencyOsDbContext _context;

    public CommunicationMessageRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(CommunicationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The typed arc columns are filled by LinkArcSynchronizer on save, so a
        // link created before the first save is handled the same way as one added
        // to a loaded message (ADR-0025).
        _context.CommunicationMessages.Add(message);
    }

    /// <inheritdoc />
    public Task<CommunicationMessage?> FindAsync(
        OrganizationId organizationId,
        CommunicationMessageId id,
        CancellationToken cancellationToken = default) =>
        _context.CommunicationMessages
            .Include(x => x.Participants)
            .Include(x => x.Attachments)
            .Include(x => x.Links)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<CommunicationMessage?> FindByExternalIdAsync(
        OrganizationId organizationId,
        CommunicationAccountId accountId,
        string externalMessageId,
        CancellationToken cancellationToken = default)
    {
        // The change tracker first, so two entries for the same message inside one
        // sync page produce one row rather than a unique-index violation.
        CommunicationMessage? pending = _context.ChangeTracker
            .Entries<CommunicationMessage>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .FirstOrDefault(x =>
                x.AccountId == accountId && x.ExternalMessageId == externalMessageId);

        return pending ?? await _context.CommunicationMessages
            .Include(x => x.Participants)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.AccountId == accountId
                    && x.ExternalMessageId == externalMessageId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<CommunicationAttachment?> FindAttachmentAsync(
        OrganizationId organizationId,
        CommunicationAttachmentId id,
        CancellationToken cancellationToken = default) =>
        _context.CommunicationAttachments
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);
}

/// <summary>Reads and writes provider threads.</summary>
public sealed class CommunicationThreadRepository : ICommunicationThreadRepository
{
    private readonly AgencyOsDbContext _context;

    public CommunicationThreadRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(CommunicationThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        _context.CommunicationThreads.Add(thread);
    }

    /// <inheritdoc />
    public async Task<CommunicationThread?> FindByExternalIdAsync(
        OrganizationId organizationId,
        CommunicationAccountId accountId,
        string externalThreadId,
        CancellationToken cancellationToken = default)
    {
        CommunicationThread? pending = _context.ChangeTracker
            .Entries<CommunicationThread>()
            .Select(x => x.Entity)
            .FirstOrDefault(x =>
                x.AccountId == accountId && x.ExternalThreadId == externalThreadId);

        return pending ?? await _context.CommunicationThreads
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.AccountId == accountId
                    && x.ExternalThreadId == externalThreadId,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Reads and writes outbound send operations.</summary>
public sealed class OutboundDispatchRepository : IOutboundDispatchRepository
{
    private readonly AgencyOsDbContext _context;

    public OutboundDispatchRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(OutboundDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        _context.OutboundDispatches.Add(dispatch);
    }

    /// <inheritdoc />
    public Task<OutboundDispatch?> FindAsync(
        OrganizationId organizationId,
        OutboundDispatchId id,
        CancellationToken cancellationToken = default) =>
        _context.OutboundDispatches
            .Include(x => x.Recipients)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Takes the next dispatch a worker may act on, and leases it in the same act.
    /// </para>
    /// <para>
    /// The states a worker may act on: queued, drafted, mid-send, retryable and
    /// unknown. A dispatch in SendRequested is claimed so recovery can reconcile
    /// it, which is the whole reason a crashed worker's row is not lost. An
    /// UnknownOutcome is claimed for the same reason and is the one that matters
    /// most: without it nothing would ever go and look, and a message whose fate is
    /// unknown would stay unknown for ever (ADR-0028).
    /// </para>
    /// <para>
    /// Reconciling is not retrying. It reads the provider's sent items and moves
    /// only on evidence; <c>next_attempt_at</c> spaces the attempts out so an
    /// unresolvable dispatch does not spin.
    /// </para>
    /// <para>
    /// Selecting and leasing are <strong>one statement</strong>. Split into a
    /// <c>SELECT ... FOR UPDATE SKIP LOCKED</c> followed by a separate write, the
    /// row lock lasts only as long as the select's own implicit transaction, so two
    /// workers read the same row, both believe they have it, and the second
    /// discovers otherwise as a concurrency conflict when it saves. Safe, but
    /// wrong: a losing worker should move to the next row, not fail (ADR-0029).
    /// </para>
    /// </remarks>
    public async Task<OutboundDispatch?> ClaimNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset expiresAt = now.Add(leaseDuration);

        List<Guid?> claimed = await _context.Database
            .SqlQuery<Guid?>(
                $"""
                 UPDATE outbound_dispatches
                    SET lease_owner = {leaseOwner},
                        lease_expires_at = {expiresAt},
                        updated_at = {now},
                        version = version + 1
                  WHERE id = (
                        SELECT id
                        FROM outbound_dispatches
                        WHERE state IN (2, 3, 4, 6, 8)
                          AND (lease_expires_at IS NULL OR lease_expires_at < {now})
                          AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
                        ORDER BY created_at
                        LIMIT 1
                        FOR UPDATE SKIP LOCKED)
                 RETURNING id AS "Value"
                 """)

            // Materialized rather than composed. An UPDATE ... RETURNING is not
            // something EF can wrap another SELECT around, and asking for the first
            // row would make it try.
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (claimed.FirstOrDefault() is not { } id)
        {
            return null;
        }

        // Read back after the lease, so the tracked entity carries the version the
        // database now holds and a later release does not collide with our own
        // write.
        return await _context.OutboundDispatches
            .Include(x => x.Recipients)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == new OutboundDispatchId(id), cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Appends the curated communication history.</summary>
public sealed class CommunicationEventRepository : ICommunicationEventRepository
{
    private readonly AgencyOsDbContext _context;

    public CommunicationEventRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(CommunicationEvent communicationEvent)
    {
        ArgumentNullException.ThrowIfNull(communicationEvent);

        _context.CommunicationEvents.Add(communicationEvent);
    }
}
