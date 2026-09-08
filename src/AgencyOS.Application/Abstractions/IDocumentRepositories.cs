using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Abstractions;

/// <summary>Reads and writes documents and their versions.</summary>
public interface IDocumentRepository
{
    void Add(Document document);

    /// <summary>Loads a document with its versions and links.</summary>
    Task<Document?> FindAsync(
        OrganizationId organizationId,
        DocumentId id,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one version, for download and for attaching to a message.</summary>
    Task<DocumentVersion?> FindVersionAsync(
        OrganizationId organizationId,
        DocumentVersionId id,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the document a version belongs to, so its sensitivity can be checked.</summary>
    /// <remarks>
    /// Downloads are addressed by version, and the authorization that guards them
    /// lives on the document. Fetching the version alone would be a read of bytes
    /// with no classification attached to it (ADR-0025).
    /// </remarks>
    Task<Document?> FindByVersionAsync(
        OrganizationId organizationId,
        DocumentVersionId versionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes stored-byte records and the ingestion ledger.</summary>
public interface IBlobRepository
{
    void Add(BlobObject blob);

    Task<BlobObject?> FindAsync(
        OrganizationId organizationId,
        BlobObjectId id,
        CancellationToken cancellationToken = default);

    /// <summary>Finds an existing blob with the same digest in the same tenant.</summary>
    /// <remarks>
    /// Scoped to the organization on purpose. A cross-tenant lookup would let one
    /// tenant learn that another holds a particular file by uploading a copy and
    /// watching for the hit (ADR-0024).
    /// </remarks>
    Task<BlobObject?> FindByHashAsync(
        OrganizationId organizationId,
        string contentHash,
        CancellationToken cancellationToken = default);

    /// <summary>Whether any blob record still points at these bytes.</summary>
    Task<bool> AnyReferencesAsync(
        OrganizationId organizationId,
        string contentHash,
        CancellationToken cancellationToken = default);

    void AddIngestion(BlobIngestion ingestion);

    Task<BlobIngestion?> FindIngestionAsync(
        OrganizationId organizationId,
        BlobIngestionId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds ingestion attempts old enough to be abandoned.
    /// </summary>
    /// <remarks>
    /// The sweeper reads this ledger rather than walking the content store. An
    /// enumeration of every stored file would grow without bound and would have to
    /// distinguish an orphan from a blob written two seconds ago by a request still
    /// in flight (ADR-0024).
    /// </remarks>
    Task<IReadOnlyList<BlobIngestion>> ListStaleIngestionsAsync(
        DateTimeOffset olderThan,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>Appends the curated document history.</summary>
public interface IDocumentEventRepository
{
    void Add(DocumentEvent documentEvent);
}

/// <summary>Reads and writes connected mailboxes.</summary>
public interface ICommunicationAccountRepository
{
    void Add(CommunicationAccount account);

    Task<CommunicationAccount?> FindAsync(
        OrganizationId organizationId,
        CommunicationAccountId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommunicationAccount>> ListAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims one connected mailbox that is due a synchronization.
    /// </summary>
    /// <remarks>
    /// Claimed with a row lock that skips rows another worker already holds, so two
    /// workers reaching for the same mailbox produce one winner and one worker that
    /// moves on, rather than two syncs writing the same messages (ADR-0029).
    /// </remarks>
    Task<CommunicationAccount?> ClaimForSyncAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes synchronized messages.</summary>
public interface ICommunicationMessageRepository
{
    void Add(CommunicationMessage message);

    Task<CommunicationMessage?> FindAsync(
        OrganizationId organizationId,
        CommunicationMessageId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a message already synchronized from this mailbox.
    /// </summary>
    /// <remarks>
    /// The check that makes delta synchronization idempotent. A provider will hand
    /// back the same message after a cursor reset, after a folder move, and
    /// sometimes for no reason at all; without this, one email becomes six rows
    /// (ADR-0026).
    /// </remarks>
    Task<CommunicationMessage?> FindByExternalIdAsync(
        OrganizationId organizationId,
        CommunicationAccountId accountId,
        string externalMessageId,
        CancellationToken cancellationToken = default);

    Task<CommunicationAttachment?> FindAttachmentAsync(
        OrganizationId organizationId,
        CommunicationAttachmentId id,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes provider threads.</summary>
public interface ICommunicationThreadRepository
{
    void Add(CommunicationThread thread);

    Task<CommunicationThread?> FindByExternalIdAsync(
        OrganizationId organizationId,
        CommunicationAccountId accountId,
        string externalThreadId,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes outbound send operations.</summary>
public interface IOutboundDispatchRepository
{
    void Add(OutboundDispatch dispatch);

    Task<OutboundDispatch?> FindAsync(
        OrganizationId organizationId,
        OutboundDispatchId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims one dispatch that needs work.
    /// </summary>
    /// <remarks>
    /// The same skip-locked lease as mailbox synchronization, and here it matters
    /// more: two workers processing one dispatch is the direct route to sending a
    /// client the same email twice (ADR-0028, ADR-0029).
    /// </remarks>
    Task<OutboundDispatch?> ClaimNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>Appends the curated communication history.</summary>
public interface ICommunicationEventRepository
{
    void Add(CommunicationEvent communicationEvent);
}
