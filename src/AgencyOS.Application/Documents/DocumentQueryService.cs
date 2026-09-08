using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Communications;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Documents;

/// <summary>Bytes on their way to a caller, with what a response needs to describe them.</summary>
/// <param name="Inline">
/// Whether the browser may render it rather than save it. False for everything not
/// on a short allow-list, because inline rendering of an uploaded file is how a
/// document store becomes a way to run script on the application's origin
/// (ADR-0025).
/// </param>
public sealed record DocumentDownload(
    Stream Content,
    string FileName,
    string MediaType,
    long ByteLength,
    string ContentHash,
    bool Inline);

/// <summary>
/// Authorizes every read of the document model.
/// </summary>
/// <remarks>
/// Two rules, applied everywhere. Sensitivity is evaluated on the document rather
/// than inherited from whatever it is linked to, and the readable set is applied
/// inside the query so a caller never learns the count of documents they may not
/// open (ADR-0025).
/// </remarks>
public sealed class DocumentQueryService
{
    private const int MaximumLimit = 200;
    private const int DefaultLimit = 50;

    private readonly IDocumentQueries _queries;
    private readonly IDocumentRepository _documents;
    private readonly IBlobRepository _blobs;
    private readonly IBlobStore _store;
    private readonly IDocumentEventRepository _events;
    private readonly DocumentAuthorization _authorization;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public DocumentQueryService(
        IDocumentQueries queries,
        IDocumentRepository documents,
        IBlobRepository blobs,
        IBlobStore store,
        IDocumentEventRepository events,
        DocumentAuthorization authorization,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _queries = queries;
        _documents = documents;
        _blobs = blobs;
        _store = store;
        _events = events;
        _authorization = authorization;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<DocumentSummaryModel>> ListAsync(
        OrganizationId organizationId,
        DocumentFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<DocumentSensitivity> readable = await _authorization
            .ReadableSensitivitiesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListAsync(
                organizationId,
                new DocumentQueryScope(readable, filter ?? new DocumentFilter()),
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// One document in full.
    /// </summary>
    /// <remarks>
    /// The classification is loaded first and authorized before anything else is
    /// returned, so a caller without the grant is refused rather than handed a
    /// hollowed-out record that reads like a document with no versions.
    /// </remarks>
    public async Task<DocumentDetailModel?> GetAsync(
        OrganizationId organizationId,
        DocumentId id,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Document? document = await _documents
            .FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return null;
        }

        await _authorization
            .AuthorizeReadAsync(organizationId, document.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.GetAsync(organizationId, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DocumentSummaryModel>> ListForTargetAsync(
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<DocumentSensitivity> readable = await _authorization
            .ReadableSensitivitiesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListForTargetAsync(organizationId, target, targetId, readable, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a version's bytes for an authorized caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only route to stored content. There is no public URL, no signed link and
    /// no path anywhere in an API response: every byte leaves through here, having
    /// been authorized against the document's own classification (ADR-0025).
    /// </para>
    /// <para>
    /// The digest is verified before the stream is handed over. It costs a read of
    /// the file and it means a corrupted or swapped blob is a refusal rather than a
    /// wrong document somebody acts on.
    /// </para>
    /// </remarks>
    public async Task<DocumentDownload?> OpenAsync(
        OrganizationId organizationId,
        DocumentVersionId versionId,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Document? document = await _documents
            .FindByVersionAsync(organizationId, versionId, cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return null;
        }

        UserId actor = await _authorization
            .AuthorizeReadAsync(organizationId, document.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DocumentVersion version = document.Versions.Single(x => x.Id == versionId);

        BlobObject? blob = await _blobs
            .FindAsync(organizationId, version.BlobObjectId, cancellationToken)
            .ConfigureAwait(false);

        if (blob is null)
        {
            throw new BlobNotFoundException(version.Id.ToString());
        }

        bool intact = await _store
            .VerifyAsync(organizationId, blob.StorageKey, version.ContentHash, cancellationToken)
            .ConfigureAwait(false);

        if (!intact)
        {
            throw new BlobIntegrityException(
                blob.StorageKey, version.ContentHash, "a different digest");
        }

        Stream content = await _store
            .OpenReadAsync(organizationId, blob.StorageKey, cancellationToken)
            .ConfigureAwait(false);

        // A download of a privileged contract is a disclosure, and disclosures are
        // audited. Previewing a list is not, because auditing every read would bury
        // the acts that matter (ADR-0025).
        _events.Add(DocumentEvent.Record(
            organizationId,
            document.Id,
            DocumentEventKind.Downloaded,
            $"Version {version.Sequence} downloaded",
            actor,
            _clock.UtcNow,
            version.Id));

        _audit.Record(
            AuditAction.DocumentDownloaded,
            entityType: nameof(DocumentVersion),
            entityId: version.Id.ToString(),
            organizationId: organizationId,
            permission: Domain.Authorization.Permission.DocumentsRead,
            semanticDelta: new
            {
                DocumentId = document.Id.ToString(),
                Sensitivity = document.Sensitivity.ToString(),
                version.ByteLength,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new DocumentDownload(
            content,
            version.DisplayFileName,
            version.MediaType,
            version.ByteLength,
            version.ContentHash,
            UploadPolicy.MayRenderInline(version.MediaType));
    }

    private static int Clamp(int? limit) =>
        limit is not { } value ? DefaultLimit : Math.Clamp(value, 1, MaximumLimit);
}

/// <summary>
/// Authorizes every read of the communication model.
/// </summary>
/// <remarks>
/// Mailbox ownership is resolved once, into a set of readable accounts, and that
/// set is applied inside every query. A caller therefore never learns that a
/// mailbox they cannot open contains four hundred messages about a named client
/// (ADR-0026).
/// </remarks>
public sealed class CommunicationQueryService
{
    private const int MaximumLimit = 200;
    private const int DefaultLimit = 50;
    private const int HistoryLimit = 200;

    private readonly ICommunicationQueries _queries;
    private readonly ICommunicationAccountRepository _accounts;
    private readonly CommunicationAuthorization _authorization;

    public CommunicationQueryService(
        ICommunicationQueries queries,
        ICommunicationAccountRepository accounts,
        CommunicationAuthorization authorization)
    {
        _queries = queries;
        _accounts = accounts;
        _authorization = authorization;
    }

    /// <summary>
    /// Every mailbox the caller may read.
    /// </summary>
    /// <remarks>
    /// A caller with no shared-read grant sees their own connections and the
    /// agency's shared ones. That is a shorter list rather than a refusal, because
    /// a list of mailboxes is not an arithmetic: an absent mailbox is not a wrong
    /// answer about the mailboxes that are there.
    /// </remarks>
    public async Task<IReadOnlyList<CommunicationAccountModel>> ListAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await _authorization
            .AuthorizeReadAsync(organizationId, cancellationToken).ConfigureAwait(false);

        bool sharedRead = await _authorization
            .CanReadOtherMailboxesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<CommunicationAccountModel> accounts = await _queries
            .ListAccountsAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return
        [
            .. accounts.Where(x =>
                sharedRead
                || x.OwnerUserId == actor.Value
                || x.Visibility == MailboxVisibility.Shared),
        ];
    }

    public async Task<IReadOnlyList<CommunicationMessageSummaryModel>> ListMessagesAsync(
        OrganizationId organizationId,
        CommunicationFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<CommunicationAccountId> readable = await ReadableAccountsAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .ListMessagesAsync(
                organizationId,
                new CommunicationQueryScope(readable, filter ?? new CommunicationFilter()),
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CommunicationMessageDetailModel?> GetMessageAsync(
        OrganizationId organizationId,
        CommunicationMessageId id,
        CancellationToken cancellationToken = default)
    {
        CommunicationMessageDetailModel? message = await _queries
            .GetMessageAsync(organizationId, id, cancellationToken).ConfigureAwait(false);

        if (message is null)
        {
            return null;
        }

        CommunicationAccount? account = await _accounts
            .FindAsync(organizationId, message.Message.AccountId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return null;
        }

        // Reading one message is authorized against its own mailbox. Being linked
        // to a deal the caller can see is not a reason to read somebody else's mail
        // (ADR-0026).
        //
        // Absent rather than refused, which is the opposite of the choice documents
        // make and deliberately so. A refusal tells the caller that this message
        // exists in a colleague's mailbox, and for correspondence the existence is
        // itself the disclosure: it says who is talking to whom. A document refusal
        // tells somebody to ask for a grant; there is no grant to ask for here
        // short of reading another person's mail (ADR-0025, ADR-0026).
        if (!await _authorization
                .CanReadAsync(account, cancellationToken)
                .ConfigureAwait(false))
        {
            return null;
        }

        return message;
    }

    public async Task<IReadOnlyList<CommunicationMessageSummaryModel>> ListForTargetAsync(
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<CommunicationAccountId> readable = await ReadableAccountsAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .ListForTargetAsync(organizationId, target, targetId, readable, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboundDispatchModel>> ListDispatchesAsync(
        OrganizationId organizationId,
        OutboundDispatchState? state = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<CommunicationAccountId> readable = await ReadableAccountsAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .ListDispatchesAsync(organizationId, readable, state, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OutboundDispatchModel?> GetDispatchAsync(
        OrganizationId organizationId,
        OutboundDispatchId id,
        CancellationToken cancellationToken = default)
    {
        OutboundDispatchModel? dispatch = await _queries
            .GetDispatchAsync(organizationId, id, cancellationToken).ConfigureAwait(false);

        if (dispatch is null)
        {
            return null;
        }

        CommunicationAccount? account = await _accounts
            .FindAsync(organizationId, dispatch.AccountId, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            return null;
        }

        await _authorization.AuthorizeReadAsync(account, cancellationToken).ConfigureAwait(false);

        return dispatch;
    }

    public async Task<IReadOnlyList<CommunicationEventModel>> GetHistoryAsync(
        OrganizationId organizationId,
        CommunicationAccountId? accountId = null,
        OutboundDispatchId? dispatchId = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId is { } id)
        {
            CommunicationAccount? account = await _accounts
                .FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false);

            if (account is not null)
            {
                await _authorization.AuthorizeReadAsync(account, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _queries
            .GetHistoryAsync(organizationId, accountId, dispatchId, HistoryLimit, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// What the communications desk has to look at.
    /// </summary>
    /// <remarks>
    /// Unknown outcomes first, because they are the only state in the milestone that
    /// nothing can resolve on its own. A person has to go and look (ADR-0028).
    /// </remarks>
    public async Task<CommunicationCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<CommunicationAccountId> readable = await ReadableAccountsAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .GetCommandCenterAsync(organizationId, readable, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ParticipantSuggestionModel>> SuggestParticipantsAsync(
        OrganizationId organizationId,
        string address,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .SuggestParticipantsAsync(organizationId, address, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The mailboxes this caller may read, resolved once.
    /// </summary>
    /// <remarks>
    /// Computed here and handed to the query so the restriction is part of the SQL.
    /// A post-filter would produce correct rows and an incorrect count, and the
    /// count is a disclosure of its own.
    /// </remarks>
    private async Task<IReadOnlySet<CommunicationAccountId>> ReadableAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        UserId actor = await _authorization
            .AuthorizeReadAsync(organizationId, cancellationToken).ConfigureAwait(false);

        bool sharedRead = await _authorization
            .CanReadOtherMailboxesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<CommunicationAccount> accounts = await _accounts
            .ListAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return accounts
            .Where(x =>
                sharedRead
                || x.OwnerUserId == actor
                || x.Visibility == MailboxVisibility.Shared)
            .Select(x => x.Id)
            .ToHashSet();
    }

    private static int Clamp(int? limit) =>
        limit is not { } value ? DefaultLimit : Math.Clamp(value, 1, MaximumLimit);
}
