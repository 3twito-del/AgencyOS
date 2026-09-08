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

/// <summary>
/// Composes, queues and cancels outbound messages.
/// </summary>
/// <remarks>
/// <para>
/// Everything here happens before anything leaves. The worker owns the part that
/// cannot be undone, and this owns the part a person can still change their mind
/// about (ADR-0028).
/// </para>
/// <para>
/// The distinction M6 to M9 established survives intact. Recording a submission is
/// still an assertion that somebody sent something; sending an email through a
/// connected mailbox is AgencyOS performing an external act with provider evidence.
/// M10 adds the second and renames none of the first (ADR-0026).
/// </para>
/// </remarks>
public sealed class OutboundDispatchHandler
{
    private readonly IOutboundDispatchRepository _dispatches;
    private readonly ICommunicationAccountRepository _accounts;
    private readonly ICommunicationMessageRepository _messages;
    private readonly IDocumentRepository _documents;
    private readonly ICommunicationEventRepository _events;
    private readonly CommunicationAuthorization _authorization;
    private readonly DocumentAuthorization _documentAuthorization;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public OutboundDispatchHandler(
        IOutboundDispatchRepository dispatches,
        ICommunicationAccountRepository accounts,
        ICommunicationMessageRepository messages,
        IDocumentRepository documents,
        ICommunicationEventRepository events,
        CommunicationAuthorization authorization,
        DocumentAuthorization documentAuthorization,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _dispatches = dispatches;
        _accounts = accounts;
        _messages = messages;
        _documents = documents;
        _events = events;
        _authorization = authorization;
        _documentAuthorization = documentAuthorization;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Records the intent to send.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One canonical intent per command. The M3 idempotency key already guarantees
    /// that a retried API call produces one row rather than two; this is where that
    /// row is created, and everything the provider is later asked to do hangs off
    /// it (ADR-0028).
    /// </para>
    /// <para>
    /// Attachments are canonical document versions, and the caller must be able to
    /// read each one. Otherwise attaching would be a way to exfiltrate a privileged
    /// contract by emailing it to yourself.
    /// </para>
    /// </remarks>
    public async Task<OutboundDispatchId> HandleAsync(
        ComposeMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        CommunicationAccount account = await RequireAccountAsync(
            command.OrganizationId, command.AccountId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeSendAsync(account, cancellationToken)
            .ConfigureAwait(false);

        if (command.Recipients.Count == 0)
        {
            throw new DomainException("A message needs at least one recipient.");
        }

        // The correlation value the provider carries, generated once and reused on
        // every attempt. It is what makes a message found in the sent items
        // afterwards attributable to this intent with certainty rather than by
        // comparing subject lines (ADR-0028).
        string clientReference = Guid.CreateVersion7().ToString("N");

        OutboundDispatch dispatch = OutboundDispatch.Compose(
            command.OrganizationId,
            account.Id,
            command.Subject,
            command.BodyText,
            clientReference,
            actor,
            _clock.UtcNow,
            command.InReplyToMessageId);

        foreach (OutboundRecipientInput recipient in command.Recipients)
        {
            dispatch.AddRecipient(recipient.Role, recipient.Address, recipient.DisplayName);
        }

        foreach (DocumentVersionId versionId in command.Attachments ?? [])
        {
            await AttachAsync(command.OrganizationId, dispatch, versionId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (command.InReplyToMessageId is { } replyTo)
        {
            _ = await _messages.FindAsync(command.OrganizationId, replyTo, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new EntityNotFoundException(
                    nameof(CommunicationMessage), replyTo.ToString());
        }

        _dispatches.Add(dispatch);

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.DispatchComposed,
            "Message composed",
            _clock.UtcNow,
            actor,
            account.Id,
            dispatchId: dispatch.Id,
            detail: $"{dispatch.Recipients.Count} recipient(s), "
                + $"{dispatch.Attachments.Count} attachment(s)"));

        _audit.Record(
            AuditAction.OutboundDispatchComposed,
            entityType: nameof(OutboundDispatch),
            entityId: dispatch.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CommunicationsSend,

            // Counts and identifiers. Never the subject, never the body, never a
            // recipient address (ADR-0026).
            semanticDelta: new
            {
                AccountId = account.Id.ToString(),
                RecipientCount = dispatch.Recipients.Count,
                AttachmentCount = dispatch.Attachments.Count,
                IsReply = command.InReplyToMessageId is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return dispatch.Id;
    }

    /// <summary>
    /// Hands the message to the worker.
    /// </summary>
    /// <remarks>
    /// The last point at which nothing has been asked of the provider. After this
    /// the worker owns the operation, and cancelling is no longer possible because
    /// AgencyOS cannot unsend anything (ADR-0028).
    /// </remarks>
    public async Task HandleAsync(
        QueueMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        (OutboundDispatch dispatch, CommunicationAccount account, UserId actor) =
            await RequireSendableAsync(
                command.OrganizationId, command.DispatchId, cancellationToken).ConfigureAwait(false);

        dispatch.Queue(command.ExpectedVersion, _clock.UtcNow);

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.DispatchQueued,
            "Message queued for sending",
            _clock.UtcNow,
            actor,
            account.Id,
            dispatchId: dispatch.Id));

        _audit.Record(
            AuditAction.OutboundDispatchQueued,
            entityType: nameof(OutboundDispatch),
            entityId: dispatch.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CommunicationsSend,
            semanticDelta: new
            {
                AccountId = account.Id.ToString(),
                RecipientCount = dispatch.Recipients.Count,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        CancelMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        (OutboundDispatch dispatch, CommunicationAccount account, UserId actor) =
            await RequireSendableAsync(
                command.OrganizationId, command.DispatchId, cancellationToken).ConfigureAwait(false);

        dispatch.Cancel(command.ExpectedVersion, _clock.UtcNow);

        _events.Add(CommunicationEvent.Record(
            command.OrganizationId,
            CommunicationEventKind.DispatchCancelled,
            "Message cancelled before it was sent",
            _clock.UtcNow,
            actor,
            account.Id,
            dispatchId: dispatch.Id));

        _audit.Record(
            AuditAction.OutboundDispatchCancelled,
            entityType: nameof(OutboundDispatch),
            entityId: dispatch.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.CommunicationsSend);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Attaches a document version, having proved the caller may read it.
    /// </summary>
    /// <remarks>
    /// The check is on the document's own classification, not on anything it is
    /// linked to. Without it, attaching would be a route around every document
    /// permission in the system: compose a message to yourself, attach the
    /// privileged contract, send (ADR-0025).
    /// </remarks>
    private async Task AttachAsync(
        OrganizationId organizationId,
        OutboundDispatch dispatch,
        DocumentVersionId versionId,
        CancellationToken cancellationToken)
    {
        Document document = await _documents
            .FindByVersionAsync(organizationId, versionId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(DocumentVersion), versionId.ToString());

        await _documentAuthorization
            .AuthorizeReadAsync(organizationId, document.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        DocumentVersion version = document.Versions.Single(x => x.Id == versionId);

        dispatch.AddAttachment(
            document.Id, version.Id, version.DisplayFileName, version.MediaType, version.ByteLength);
    }

    private async Task<(OutboundDispatch Dispatch, CommunicationAccount Account, UserId Actor)>
        RequireSendableAsync(
            OrganizationId organizationId,
            OutboundDispatchId id,
            CancellationToken cancellationToken)
    {
        OutboundDispatch dispatch = await _dispatches
            .FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(OutboundDispatch), id.ToString());

        CommunicationAccount account = await RequireAccountAsync(
            organizationId, dispatch.AccountId, cancellationToken).ConfigureAwait(false);

        UserId actor = await _authorization
            .AuthorizeSendAsync(account, cancellationToken).ConfigureAwait(false);

        return (dispatch, account, actor);
    }

    private async Task<CommunicationAccount> RequireAccountAsync(
        OrganizationId organizationId,
        CommunicationAccountId id,
        CancellationToken cancellationToken) =>
        await _accounts.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(CommunicationAccount), id.ToString());
}
