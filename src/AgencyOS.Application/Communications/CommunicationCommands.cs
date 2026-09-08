using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Documents;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Communications;

// -------------------------------------------------------------------- commands

/// <summary>
/// Completes a mailbox connection from an authorization code.
/// </summary>
/// <remarks>
/// The code is exchanged server-side. Neither it nor the tokens it produces ever
/// reach the Windows client, and neither is returned by any endpoint (ADR-0027).
/// </remarks>
public sealed record ConnectMailboxCommand(
    OrganizationId OrganizationId,
    CommunicationProviderKind Provider,
    string AuthorizationCode,
    string RedirectUri,
    MailboxVisibility Visibility = MailboxVisibility.Private);

public sealed record DisconnectMailboxCommand(
    OrganizationId OrganizationId,
    CommunicationAccountId AccountId,
    int ExpectedVersion);

public sealed record ChangeMailboxVisibilityCommand(
    OrganizationId OrganizationId,
    CommunicationAccountId AccountId,
    MailboxVisibility Visibility,
    int ExpectedVersion);

public sealed record LinkMessageCommand(
    OrganizationId OrganizationId,
    CommunicationMessageId MessageId,
    DocumentLinkTarget Target,
    Guid TargetId,
    string? Note = null);

public sealed record UnlinkMessageCommand(
    OrganizationId OrganizationId,
    CommunicationMessageId MessageId,
    Guid LinkId);

/// <summary>Records that an address belongs to a person or company AgencyOS knows.</summary>
public sealed record ResolveParticipantCommand(
    OrganizationId OrganizationId,
    CommunicationMessageId MessageId,
    Guid ParticipantId,
    Guid? PersonId,
    Guid? CompanyId);

/// <summary>Pulls an attachment's bytes into the canonical document store.</summary>
public sealed record IngestAttachmentCommand(
    OrganizationId OrganizationId,
    CommunicationAttachmentId AttachmentId,
    DocumentKind Kind,
    DocumentSensitivity Sensitivity,
    string? Title = null,
    IReadOnlyList<DocumentLinkInput>? Links = null);

public sealed record ComposeMessageCommand(
    OrganizationId OrganizationId,
    CommunicationAccountId AccountId,
    string Subject,
    string BodyText,
    IReadOnlyList<OutboundRecipientInput> Recipients,
    IReadOnlyList<DocumentVersionId>? Attachments = null,
    CommunicationMessageId? InReplyToMessageId = null,
    IReadOnlyList<DocumentLinkInput>? Links = null);

public sealed record OutboundRecipientInput(
    ParticipantRole Role,
    string Address,
    string? DisplayName = null);

public sealed record QueueMessageCommand(
    OrganizationId OrganizationId,
    OutboundDispatchId DispatchId,
    int ExpectedVersion);

public sealed record CancelMessageCommand(
    OrganizationId OrganizationId,
    OutboundDispatchId DispatchId,
    int ExpectedVersion);

// -------------------------------------------------------------------- handlers

/// <summary>Connects, shares and disconnects mailboxes.</summary>
public sealed class CommunicationAccountHandler
{
    private readonly ICommunicationProviderRegistry _providers;
    private readonly ICommunicationAccountRepository _accounts;
    private readonly ICommunicationEventRepository _events;
    private readonly CommunicationAuthorization _authorization;
    private readonly ISecretProtector _secrets;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CommunicationAccountHandler(
        ICommunicationProviderRegistry providers,
        ICommunicationAccountRepository accounts,
        ICommunicationEventRepository events,
        CommunicationAuthorization authorization,
        ISecretProtector secrets,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _providers = providers;
        _accounts = accounts;
        _events = events;
        _authorization = authorization;
        _secrets = secrets;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Exchanges an authorization code for a stored connection.
    /// </summary>
    /// <remarks>
    /// The mailbox is owned by whoever completed the flow, and by nobody else. That
    /// is the fact every later authorization decision rests on, so it is set here
    /// from the authenticated caller and never from the request (ADR-0026).
    /// </remarks>
    public async Task<CommunicationAccountId> HandleAsync(
        ConnectMailboxCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _authorization
            .AuthorizeManageAsync(command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!_providers.Supports(command.Provider))
        {
            throw new DomainException(
                $"No adapter is configured for {command.Provider} on this server.");
        }

        ICommunicationProvider provider = _providers.Resolve(command.Provider);

        ProviderConnection connection = await provider
            .CompleteConnectionAsync(command.AuthorizationCode, command.RedirectUri, cancellationToken)
            .ConfigureAwait(false);

        // Encrypted before it touches the database, and the plaintext is not held
        // beyond this statement (ADR-0027).
        CommunicationAccount account = CommunicationAccount.Connect(
            command.OrganizationId,
            actor,
            command.Provider,
            connection.MailboxAddress,
            connection.ExternalAccountId,
            connection.GrantedScopes,
            _secrets.Protect(connection.RefreshToken),
            command.Visibility,
            _clock.UtcNow,
            connection.DisplayName,
            connection.ExpiresAt);

        _accounts.Add(account);

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.AccountConnected,
            $"Connected {account.MailboxAddress}",
            _clock.UtcNow,
            actor,
            account.Id,
            detail: $"{command.Provider}, {command.Visibility.ToString().ToLowerInvariant()}"));

        _audit.Record(
            AuditAction.CommunicationAccountConnected,
            entityType: nameof(CommunicationAccount),
            entityId: account.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CommunicationsAccountManage,

            // The mailbox address, the provider and the scopes granted. Never the
            // token, never a fragment of it, never the authorization code.
            semanticDelta: new
            {
                Provider = command.Provider.ToString(),
                account.MailboxAddress,
                Visibility = command.Visibility.ToString(),
                account.GrantedScopes,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return account.Id;
    }

    /// <summary>
    /// Ends a connection and destroys its credential.
    /// </summary>
    /// <remarks>
    /// Only the owner may disconnect their own mailbox. Synchronized messages stay:
    /// they are a record of correspondence the agency had, and disconnecting a
    /// mailbox is not a reason to lose it (ADR-0026).
    /// </remarks>
    public async Task HandleAsync(
        DisconnectMailboxCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CommunicationAccount account = await RequireAccountAsync(
            command.OrganizationId, command.AccountId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeManageAsync(command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        RequireOwner(account, actor);

        account.Disconnect(command.ExpectedVersion, _clock.UtcNow);

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.AccountDisconnected,
            $"Disconnected {account.MailboxAddress}",
            _clock.UtcNow,
            actor,
            account.Id,
            detail: "The stored credential was destroyed. Synchronized messages are kept."));

        _audit.Record(
            AuditAction.CommunicationAccountDisconnected,
            entityType: nameof(CommunicationAccount),
            entityId: account.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CommunicationsAccountManage,
            semanticDelta: new { account.MailboxAddress, CredentialDestroyed = true });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        ChangeMailboxVisibilityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CommunicationAccount account = await RequireAccountAsync(
            command.OrganizationId, command.AccountId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeManageAsync(command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        RequireOwner(account, actor);

        MailboxVisibility before = account.Visibility;

        account.ChangeVisibility(command.Visibility, command.ExpectedVersion, _clock.UtcNow);

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.AccountVisibilityChanged,
            command.Visibility == MailboxVisibility.Shared
                ? $"{account.MailboxAddress} is now readable by the agency"
                : $"{account.MailboxAddress} is now private",
            _clock.UtcNow,
            actor,
            account.Id));

        _audit.Record(
            AuditAction.CommunicationAccountVisibilityChanged,
            entityType: nameof(CommunicationAccount),
            entityId: account.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CommunicationsAccountManage,
            semanticDelta: new { From = before.ToString(), To = command.Visibility.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refuses an act on a mailbox the caller does not own.</summary>
    /// <remarks>
    /// Sharing a mailbox for reading does not hand over control of the connection.
    /// Disconnecting somebody else's mailbox, or making it readable by the agency,
    /// is theirs to do.
    /// </remarks>
    private static void RequireOwner(CommunicationAccount account, UserId actor)
    {
        if (account.OwnerUserId != actor)
        {
            throw new PermissionDeniedException(Permission.CommunicationsAccountManage);
        }
    }

    private async Task<CommunicationAccount> RequireAccountAsync(
        OrganizationId organizationId,
        CommunicationAccountId id,
        CancellationToken cancellationToken) =>
        await _accounts.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(CommunicationAccount), id.ToString());
}

/// <summary>Links messages, identifies participants and ingests attachments.</summary>
public sealed class CommunicationMessageHandler
{
    private readonly ICommunicationMessageRepository _messages;
    private readonly ICommunicationAccountRepository _accounts;
    private readonly ICommunicationProviderRegistry _providers;
    private readonly ICommunicationEventRepository _events;
    private readonly IDocumentRepository _documents;
    private readonly IBlobRepository _blobs;
    private readonly DocumentIngestion _ingestion;
    private readonly CommunicationAuthorization _authorization;
    private readonly DocumentAuthorization _documentAuthorization;
    private readonly DocumentLinkValidator _links;
    private readonly IDocumentEventRepository _documentEvents;
    private readonly ISecretProtector _secrets;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CommunicationMessageHandler(
        ICommunicationMessageRepository messages,
        ICommunicationAccountRepository accounts,
        ICommunicationProviderRegistry providers,
        ICommunicationEventRepository events,
        IDocumentRepository documents,
        IBlobRepository blobs,
        DocumentIngestion ingestion,
        CommunicationAuthorization authorization,
        DocumentAuthorization documentAuthorization,
        DocumentLinkValidator links,
        IDocumentEventRepository documentEvents,
        ISecretProtector secrets,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _messages = messages;
        _accounts = accounts;
        _providers = providers;
        _events = events;
        _documents = documents;
        _blobs = blobs;
        _ingestion = ingestion;
        _authorization = authorization;
        _documentAuthorization = documentAuthorization;
        _links = links;
        _documentEvents = documentEvents;
        _secrets = secrets;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Connects a message to a record it is evidence about.
    /// </summary>
    /// <remarks>
    /// Evidence and context. Linking an email to a deal does not move the deal, does
    /// not create a submission, and does not turn a paragraph of prose into an offer
    /// — M10 captures communications and M11 is where anything reasons about them
    /// (ADR-0026).
    /// </remarks>
    public async Task<Guid> HandleAsync(
        LinkMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        (CommunicationMessage message, UserId actor) = await RequireReadableAsync(
            command.OrganizationId, command.MessageId, cancellationToken).ConfigureAwait(false);

        await _links
            .RequireTargetAsync(
                command.OrganizationId, command.Target, command.TargetId, cancellationToken)
            .ConfigureAwait(false);

        CommunicationLink link = message.Link(
            command.Target, command.TargetId, actor, _clock.UtcNow, command.Note);

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.MessageLinked,
            $"Message linked to a {command.Target}",
            _clock.UtcNow,
            actor,
            message.AccountId,
            message.Id,
            detail: command.Note));

        _audit.Record(
            AuditAction.CommunicationMessageLinked,
            entityType: nameof(CommunicationLink),
            entityId: link.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CommunicationsRead,
            semanticDelta: new
            {
                MessageId = message.Id.ToString(),
                Target = command.Target.ToString(),
                TargetId = command.TargetId.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return link.Id;
    }

    public async Task HandleAsync(
        UnlinkMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        (CommunicationMessage message, UserId actor) = await RequireReadableAsync(
            command.OrganizationId, command.MessageId, cancellationToken).ConfigureAwait(false);

        CommunicationLink link = message.Unlink(command.LinkId);

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.MessageUnlinked,
            $"Message unlinked from a {link.Target}",
            _clock.UtcNow,
            actor,
            message.AccountId,
            message.Id));

        _audit.Record(
            AuditAction.CommunicationMessageUnlinked,
            entityType: nameof(CommunicationLink),
            entityId: link.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CommunicationsRead,
            semanticDelta: new { MessageId = message.Id.ToString(), Target = link.Target.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records that an address belongs to somebody AgencyOS knows.
    /// </summary>
    /// <remarks>
    /// Always explicit. An address is not an identity: people share mailboxes, one
    /// person has six addresses, and an address that belonged to an agent last year
    /// belongs to their replacement now. The raw address and display name stay
    /// exactly as the mail said, and this sits beside them (ADR-0026).
    /// </remarks>
    public async Task HandleAsync(
        ResolveParticipantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        (CommunicationMessage message, UserId actor) = await RequireReadableAsync(
            command.OrganizationId, command.MessageId, cancellationToken).ConfigureAwait(false);

        CommunicationParticipant participant =
            message.Participants.SingleOrDefault(x => x.Id == command.ParticipantId)
            ?? throw new EntityNotFoundException(
                nameof(CommunicationParticipant), command.ParticipantId.ToString());

        if (command.PersonId is null && command.CompanyId is null)
        {
            participant.ClearResolution();
        }
        else
        {
            DocumentLinkTarget target = command.PersonId is not null
                ? DocumentLinkTarget.Person
                : DocumentLinkTarget.Company;

            await _links
                .RequireTargetAsync(
                    command.OrganizationId,
                    target,
                    command.PersonId ?? command.CompanyId!.Value,
                    cancellationToken)
                .ConfigureAwait(false);

            participant.ResolveTo(command.PersonId, command.CompanyId, actor, _clock.UtcNow);
        }

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.ParticipantResolved,
            command.PersonId is null && command.CompanyId is null
                ? "An address identification was withdrawn"
                : "An address was identified",
            _clock.UtcNow,
            actor,
            message.AccountId,
            message.Id));

        _audit.Record(
            AuditAction.CommunicationParticipantResolved,
            entityType: nameof(CommunicationParticipant),
            entityId: participant.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CommunicationsRead,

            // Which record, never the address: an audit trail full of email
            // addresses is a contact list for anybody who can read audit.
            semanticDelta: new
            {
                MessageId = message.Id.ToString(),
                HasPerson = command.PersonId is not null,
                HasCompany = command.CompanyId is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pulls an attachment's bytes into the canonical document store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A deliberate act, never automatic. Downloading every attachment on sight
    /// would move gigabytes of unrequested video through the server, and would give
    /// the agency durable copies of files nobody asked it to keep (ADR-0024).
    /// </para>
    /// <para>
    /// Until this runs, the attachment row says the file exists and that AgencyOS
    /// does not hold it. Afterwards it names the document version that does.
    /// </para>
    /// </remarks>
    public async Task<RecordDocumentResult> HandleAsync(
        IngestAttachmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CommunicationAttachment attachment = await _messages
            .FindAttachmentAsync(command.OrganizationId, command.AttachmentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(CommunicationAttachment), command.AttachmentId.ToString());

        (CommunicationMessage message, UserId actor) = await RequireReadableAsync(
            command.OrganizationId, attachment.MessageId, cancellationToken).ConfigureAwait(false);

        // Writing a document is a separate grant from reading a mailbox, and both
        // are required: ingesting turns somebody's mail into an agency record.
        await _documentAuthorization
            .AuthorizeClassifyAsync(command.OrganizationId, command.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        if (attachment.HoldsContent)
        {
            throw new DomainException("That attachment has already been ingested.");
        }

        UploadPolicy.RequireAcceptableLength(
            attachment.ByteLength, UploadPolicy.MaximumAttachmentByteLength);

        CommunicationAccount account = await _accounts
            .FindAsync(command.OrganizationId, message.AccountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(CommunicationAccount), message.AccountId.ToString());

        if (account.ProtectedRefreshToken is not { } protectedToken)
        {
            throw new DomainException(
                "That mailbox is disconnected, so its attachments cannot be fetched.");
        }

        ICommunicationProvider provider = _providers.Resolve(account.Provider);

        await using Stream content = await provider
            .OpenAttachmentAsync(
                _secrets.Unprotect(protectedToken),
                message.ExternalMessageId,
                attachment.ExternalAttachmentId,
                cancellationToken)
            .ConfigureAwait(false);

        StagedContent staged = await _ingestion
            .StageAsync(
                command.OrganizationId,
                content,
                attachment.FileName,
                attachment.MediaType,
                actor,
                UploadPolicy.MaximumAttachmentByteLength,
                cancellationToken)
            .ConfigureAwait(false);

        Document document = Document.Record(
            command.OrganizationId,
            command.Title ?? attachment.FileName,
            command.Kind,
            command.Sensitivity,
            actor,
            _clock.UtcNow,
            description: $"Ingested from a message in {account.MailboxAddress}.");

        DocumentVersion version = document.AddVersion(
            staged.BlobObjectId,
            staged.ContentHash,
            staged.ByteLength,
            staged.FileName,
            staged.MediaType,
            DocumentVersionSource.EmailAttachment,
            actor,
            _clock.UtcNow,
            attachment.ExternalAttachmentId);

        _documents.Add(document);

        foreach (DocumentLinkInput link in command.Links ?? [])
        {
            await _links
                .RequireTargetAsync(
                    command.OrganizationId, link.Target, link.TargetId, cancellationToken)
                .ConfigureAwait(false);

            document.Link(link.Target, link.TargetId, actor, _clock.UtcNow, link.Note);
        }

        BlobObject? blob = await _blobs
            .FindAsync(command.OrganizationId, staged.BlobObjectId, cancellationToken)
            .ConfigureAwait(false);

        if (blob is not null)
        {
            await _ingestion
                .ExtractTextAsync(command.OrganizationId, version, blob.StorageKey, cancellationToken)
                .ConfigureAwait(false);
        }

        // The document and its version are committed before the attachment points
        // at them. The containment foreign key from an attachment to a document
        // version is a real constraint in PostgreSQL but is not part of the EF
        // model - it spans the tenant key, and it was added in SQL - so nothing
        // orders these two writes for us, and PostgreSQL refuses the attachment
        // update when it lands first (ADR-0024).
        //
        // The worst a failure between the two can leave behind is a document
        // nobody linked to the message: the same benign orphan the ingestion
        // ledger already tolerates, and the opposite ordering would leave an
        // attachment pointing at a version that does not exist.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        attachment.NoteIngested(version.Id, actor, _clock.UtcNow);

        _documentEvents.Add(DocumentEvent.Record(
            command.OrganizationId,
            document.Id,
            DocumentEventKind.Recorded,
            $"Ingested from a message: {staged.FileName}",
            actor,
            _clock.UtcNow,
            version.Id));

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.AttachmentIngested,
            $"Attachment ingested: {attachment.FileName}",
            _clock.UtcNow,
            actor,
            message.AccountId,
            message.Id,
            detail: $"{staged.ByteLength:N0} bytes"));

        _audit.Record(
            AuditAction.CommunicationAttachmentIngested,
            entityType: nameof(CommunicationAttachment),
            entityId: attachment.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.DocumentsWrite,
            semanticDelta: new
            {
                MessageId = message.Id.ToString(),
                DocumentId = document.Id.ToString(),
                staged.ByteLength,
                staged.MediaType,
                Sensitivity = command.Sensitivity.ToString(),
            });

        await _ingestion
            .FinalizeAsync(command.OrganizationId, staged.IngestionId, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordDocumentResult(
            document.Id, version.Id, staged.ContentHash, staged.ByteLength, staged.Deduplicated);
    }

    /// <summary>Loads a message and proves the caller may read its mailbox.</summary>
    private async Task<(CommunicationMessage Message, UserId Actor)> RequireReadableAsync(
        OrganizationId organizationId,
        CommunicationMessageId id,
        CancellationToken cancellationToken)
    {
        CommunicationMessage message = await _messages
            .FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(CommunicationMessage), id.ToString());

        CommunicationAccount account = await _accounts
            .FindAsync(organizationId, message.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(CommunicationAccount), message.AccountId.ToString());

        UserId actor = await _authorization
            .AuthorizeReadAsync(account, cancellationToken).ConfigureAwait(false);

        return (message, actor);
    }
}
