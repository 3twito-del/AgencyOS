using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;

namespace AgencyOS.Application.Communications;

/// <summary>What one pass over one dispatch did.</summary>
public sealed record OutboundStepResult(OutboundDispatchState State, string? Detail = null);

/// <summary>
/// Drives one outbound message from intent to evidence.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place in AgencyOS that causes something irreversible outside
/// it. Everything else in M10 records that something happened; this makes something
/// happen, to a real person, and it cannot be taken back (ADR-0028).
/// </para>
/// <para>
/// The protocol is four steps, and the order of the writes is the whole design:
/// </para>
/// <list type="number">
/// <item>
/// <strong>Create a provider draft.</strong> Repeatable and invisible to anybody
/// outside. Its identifier is persisted, which is what makes step four possible.
/// </item>
/// <item>
/// <strong>Record that a send is about to be requested</strong>, and commit that
/// before calling the provider. A crash after this leaves a row saying a send may
/// already have happened, which is exactly what a recovering worker needs to know.
/// </item>
/// <item>
/// <strong>Ask the provider to send.</strong> Three outcomes, kept apart: accepted,
/// refused, or no answer. The third is not a failure.
/// </item>
/// <item>
/// <strong>Reconcile</strong> when there was no answer, by searching the provider's
/// sent items for the correlation value carried on the message. Found, proven
/// absent, or still unknown — and the third is reported as itself.
/// </item>
/// </list>
/// <para>
/// The protocol is modelled in <c>specs/OutboundSend.tla</c> and checked with TLC.
/// The properties it proves are the ones a person would care about: one intent is
/// never knowingly sent twice, a confirmed send is never un-confirmed, and an
/// unknown outcome is never resolved by assuming the worst.
/// </para>
/// </remarks>
public sealed class OutboundSendProcessor
{
    /// <summary>How long to wait before looking again after an unknown outcome.</summary>
    /// <remarks>
    /// Longer than a retry delay on purpose. The provider's sent items may take a
    /// moment to reflect a message it has just accepted, and asking immediately
    /// invites a "proven absent" verdict about a message that is on its way.
    /// </remarks>
    private static readonly TimeSpan ReconcileDelay = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>How many times a retryable failure is retried before it is permanent.</summary>
    private const int MaximumAttempts = 5;

    private readonly ICommunicationProviderRegistry _providers;
    private readonly ICommunicationAccountRepository _accounts;
    private readonly ICommunicationMessageRepository _messages;
    private readonly IDocumentRepository _documents;
    private readonly IBlobRepository _blobs;
    private readonly IBlobStore _blobStore;
    private readonly ICommunicationEventRepository _events;
    private readonly ISecretProtector _secrets;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public OutboundSendProcessor(
        ICommunicationProviderRegistry providers,
        ICommunicationAccountRepository accounts,
        ICommunicationMessageRepository messages,
        IDocumentRepository documents,
        IBlobRepository blobs,
        IBlobStore blobStore,
        ICommunicationEventRepository events,
        ISecretProtector secrets,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _providers = providers;
        _accounts = accounts;
        _messages = messages;
        _documents = documents;
        _blobs = blobs;
        _blobStore = blobStore;
        _events = events;
        _secrets = secrets;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Advances one dispatch by exactly one step.
    /// </summary>
    /// <remarks>
    /// One step per call, saved before returning. A loop that ran the whole protocol
    /// in one pass would hold a lease across two external calls and would have to
    /// decide what to do when the second failed with the first already committed;
    /// stepping means every intermediate state is durable and recoverable
    /// (ADR-0028).
    /// </remarks>
    public async Task<OutboundStepResult> StepAsync(
        OutboundDispatch dispatch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        CommunicationAccount? account = await _accounts
            .FindAsync(dispatch.OrganizationId, dispatch.AccountId, cancellationToken)
            .ConfigureAwait(false);

        if (account is null || account.ProtectedRefreshToken is not { } protectedToken)
        {
            return await FailPermanentAsync(
                dispatch, "The sending mailbox is no longer connected.", cancellationToken)
                .ConfigureAwait(false);
        }

        string refreshToken;

        try
        {
            refreshToken = _secrets.Unprotect(protectedToken);
        }
        catch (SecretProtectionException)
        {
            return await FailRetryableAsync(
                dispatch,
                "The stored credential could not be read. Reconnect the mailbox.",
                cancellationToken)
                .ConfigureAwait(false);
        }

        ICommunicationProvider provider = _providers.Resolve(account.Provider);

        return dispatch.State switch
        {
            OutboundDispatchState.Queued or OutboundDispatchState.FailedRetryable =>
                await CreateDraftAsync(dispatch, provider, refreshToken, cancellationToken)
                    .ConfigureAwait(false),

            OutboundDispatchState.ProviderDraftCreated =>
                await RequestSendAsync(dispatch, provider, refreshToken, cancellationToken)
                    .ConfigureAwait(false),

            // Reached only after a crash between recording the intent and learning
            // the outcome. The send may have happened, so the answer is never to
            // send again: it is to go and look.
            OutboundDispatchState.SendRequested =>
                await ReconcileAsync(
                    dispatch,
                    provider,
                    refreshToken,
                    "The worker restarted while a send was in flight.",
                    cancellationToken)
                    .ConfigureAwait(false),

            OutboundDispatchState.UnknownOutcome =>
                await ReconcileAsync(
                    dispatch, provider, refreshToken, null, cancellationToken)
                    .ConfigureAwait(false),

            _ => new OutboundStepResult(dispatch.State, "Nothing to do."),
        };
    }

    /// <summary>
    /// Creates the provider draft.
    /// </summary>
    /// <remarks>
    /// Safe to repeat: a draft nobody sent has no consequence anybody sees. That is
    /// precisely why the protocol creates one before committing to anything, and why
    /// a retry lands here rather than at the send.
    /// </remarks>
    private async Task<OutboundStepResult> CreateDraftAsync(
        OutboundDispatch dispatch,
        ICommunicationProvider provider,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (dispatch.AttemptCount >= MaximumAttempts)
        {
            return await FailPermanentAsync(
                dispatch,
                $"Gave up after {dispatch.AttemptCount} attempts. Last error: {dispatch.LastError}",
                cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            OutboundDraftRequest request = await BuildRequestAsync(dispatch, cancellationToken)
                .ConfigureAwait(false);

            ProviderDraft draft = await provider
                .CreateDraftAsync(refreshToken, request, dispatch.ClientReference, cancellationToken)
                .ConfigureAwait(false);

            dispatch.NoteProviderDraft(draft.ProviderDraftId, _clock.UtcNow);

            _events.Add(CommunicationEvent.Record(
                dispatch.OrganizationId,
                CommunicationEventKind.ProviderDraftCreated,
                "Draft created at the provider",
                _clock.UtcNow,
                accountId: dispatch.AccountId,
                dispatchId: dispatch.Id));

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new OutboundStepResult(dispatch.State);
        }
        catch (ProviderAuthorizationException)
        {
            return await FailPermanentAsync(
                dispatch, "The provider refused the credential.", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Nothing external happened: a draft either exists or it does not, and
            // either way nobody received anything. Retrying is safe.
            return await FailRetryableAsync(
                dispatch, $"Could not create the draft ({failure.GetType().Name}).", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks the provider to send, having first recorded that it is about to.
    /// </summary>
    /// <remarks>
    /// The ordering here is the single most important line of the milestone. The
    /// state is written and <strong>committed before the call</strong>. If the
    /// process dies at any point after that — mid-call, mid-response, mid-commit —
    /// the row still says a send may have happened, and recovery reconciles rather
    /// than resends (ADR-0028).
    /// </remarks>
    private async Task<OutboundStepResult> RequestSendAsync(
        OutboundDispatch dispatch,
        ICommunicationProvider provider,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (dispatch.ProviderDraftId is not { } draftId)
        {
            return await FailRetryableAsync(
                dispatch, "No provider draft to send.", cancellationToken).ConfigureAwait(false);
        }

        dispatch.NoteSendRequested(_clock.UtcNow);

        _audit.Record(
            AuditAction.OutboundSendRequested,
            entityType: nameof(OutboundDispatch),
            entityId: dispatch.Id.ToString(),
            organizationId: dispatch.OrganizationId,
            permission: Permission.CommunicationsSend,

            // Identifiers, counts and shape. Never the subject, never the body,
            // never a recipient address: audit is read by people who hold no
            // communications permission at all (ADR-0026).
            semanticDelta: new
            {
                AccountId = dispatch.AccountId.ToString(),
                RecipientCount = dispatch.Recipients.Count,
                AttachmentCount = dispatch.Attachments.Count,
                dispatch.AttemptCount,
            });

        // Committed before the provider is called. This is not an optimization to
        // be folded into the save below it.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ProviderSendResult result;

        try
        {
            result = await provider
                .SendDraftAsync(refreshToken, draftId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // An exception on the send call proves nothing. The provider may have
            // accepted and failed to answer, so this is unknown rather than failed.
            return await UnknownAsync(
                dispatch,
                $"No answer from the provider ({failure.GetType().Name}).",
                cancellationToken)
                .ConfigureAwait(false);
        }

        switch (result.Outcome)
        {
            case ProviderSendOutcome.Accepted:
                return await ConfirmAsync(dispatch, result, cancellationToken).ConfigureAwait(false);

            case ProviderSendOutcome.Rejected when result.IsPermanent:
                return await FailPermanentAsync(
                    dispatch, result.Error ?? "The provider refused the message.", cancellationToken)
                    .ConfigureAwait(false);

            case ProviderSendOutcome.Rejected:
                // A stated refusal, before any commit. The provider said it did not
                // send, so returning to the draft state is safe.
                dispatch.NoteRetryableFailure(
                    result.Error ?? "The provider refused the message.",
                    _clock.UtcNow,
                    _clock.UtcNow.Add(RetryDelay));

                await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return new OutboundStepResult(dispatch.State, result.Error);

            default:
                return await UnknownAsync(
                    dispatch, result.Error ?? "The provider did not answer.", cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks the provider what actually happened.
    /// </summary>
    /// <remarks>
    /// The recovery path, and the reason an unknown outcome is survivable rather
    /// than terminal. Three answers and three different consequences: found means
    /// sent, proven absent means it is safe to send again, and no answer means the
    /// state stays exactly as it is until somebody or something can do better
    /// (ADR-0028).
    /// </remarks>
    private async Task<OutboundStepResult> ReconcileAsync(
        OutboundDispatch dispatch,
        ICommunicationProvider provider,
        string refreshToken,
        string? enterUnknownFirstBecause,
        CancellationToken cancellationToken)
    {
        if (enterUnknownFirstBecause is not null)
        {
            dispatch.NoteUnknownOutcome(
                enterUnknownFirstBecause, _clock.UtcNow, _clock.UtcNow.Add(ReconcileDelay));

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        ProviderReconciliation reconciliation;

        try
        {
            reconciliation = await provider
                .ReconcileAsync(
                    refreshToken, dispatch.ClientReference, dispatch.ProviderDraftId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Still cannot tell. The state does not move, and it is not quietly
            // turned into a failure because asking twice did not work either.
            dispatch.ApplyReconciliation(
                DispatchReconciliationVerdict.Inconclusive,
                _clock.UtcNow,
                _clock.UtcNow.Add(ReconcileDelay));

            await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            return new OutboundStepResult(
                dispatch.State, $"Could not reconcile ({failure.GetType().Name}).");
        }

        dispatch.ApplyReconciliation(
            reconciliation.Verdict,
            _clock.UtcNow,
            reconciliation.Verdict == DispatchReconciliationVerdict.Inconclusive
                ? _clock.UtcNow.Add(ReconcileDelay)
                : _clock.UtcNow,
            reconciliation.ProviderMessageId,
            reconciliation.InternetMessageId);

        _events.Add(CommunicationEvent.Record(
            dispatch.OrganizationId,
            CommunicationEventKind.Reconciled,
            reconciliation.Verdict switch
            {
                DispatchReconciliationVerdict.FoundSent =>
                    "The provider confirms the message was sent",
                DispatchReconciliationVerdict.ProvenAbsent =>
                    "The provider confirms the message was not sent",
                _ => "The provider could not say whether the message was sent",
            },
            _clock.UtcNow,
            accountId: dispatch.AccountId,
            dispatchId: dispatch.Id,
            detail: reconciliation.Detail));

        _audit.Record(
            AuditAction.OutboundReconciled,
            entityType: nameof(OutboundDispatch),
            entityId: dispatch.Id.ToString(),
            organizationId: dispatch.OrganizationId,
            permission: Permission.CommunicationsSend,
            semanticDelta: new
            {
                Verdict = reconciliation.Verdict.ToString(),
                Resulting = dispatch.State.ToString(),
            });

        if (dispatch.State == OutboundDispatchState.Sent)
        {
            await RecordSentMessageAsync(dispatch, cancellationToken).ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OutboundStepResult(dispatch.State, reconciliation.Detail);
    }

    private async Task<OutboundStepResult> ConfirmAsync(
        OutboundDispatch dispatch,
        ProviderSendResult result,
        CancellationToken cancellationToken)
    {
        dispatch.NoteSent(result.ProviderMessageId, result.InternetMessageId, _clock.UtcNow);

        await RecordSentMessageAsync(dispatch, cancellationToken).ConfigureAwait(false);

        _events.Add(CommunicationEvent.Record(
            dispatch.OrganizationId,
            CommunicationEventKind.SendConfirmed,
            "The provider confirmed the send",
            _clock.UtcNow,
            accountId: dispatch.AccountId,
            dispatchId: dispatch.Id));

        _audit.Record(
            AuditAction.OutboundSendConfirmed,
            entityType: nameof(OutboundDispatch),
            entityId: dispatch.Id.ToString(),
            organizationId: dispatch.OrganizationId,
            permission: Permission.CommunicationsSend,
            semanticDelta: new
            {
                AccountId = dispatch.AccountId.ToString(),
                RecipientCount = dispatch.Recipients.Count,
                HasProviderMessageId = result.ProviderMessageId is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OutboundStepResult(dispatch.State);
    }

    /// <summary>
    /// Writes the canonical message row for a confirmed send.
    /// </summary>
    /// <remarks>
    /// So a sent message appears in the same list as everything else rather than
    /// only in the dispatch. The next mailbox synchronization will see the
    /// provider's own copy; the external identifier keys them together, so it
    /// updates this row instead of adding a second one (ADR-0026).
    /// </remarks>
    private async Task RecordSentMessageAsync(
        OutboundDispatch dispatch,
        CancellationToken cancellationToken)
    {
        if (dispatch.SentMessageId is not null || dispatch.ProviderMessageId is not { } messageId)
        {
            return;
        }

        CommunicationMessage? existing = await _messages
            .FindByExternalIdAsync(
                dispatch.OrganizationId, dispatch.AccountId, messageId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            dispatch.NoteSentMessage(existing.Id, _clock.UtcNow);
            return;
        }

        CommunicationMessage message = CommunicationMessage.Record(
            dispatch.OrganizationId,
            dispatch.AccountId,
            messageId,
            MessageDirection.Outbound,
            _clock.UtcNow,
            internetMessageId: dispatch.InternetMessageId,
            subject: dispatch.Subject,
            bodyText: dispatch.BodyText,
            sentAt: dispatch.SentAt,
            folder: "SentItems",
            hasAttachments: dispatch.Attachments.Count > 0);

        foreach (OutboundRecipient recipient in dispatch.Recipients)
        {
            message.AddParticipant(recipient.Role, recipient.Address, recipient.DisplayName);
        }

        _messages.Add(message);

        dispatch.NoteSentMessage(message.Id, _clock.UtcNow);
    }

    private async Task<OutboundStepResult> UnknownAsync(
        OutboundDispatch dispatch,
        string reason,
        CancellationToken cancellationToken)
    {
        dispatch.NoteUnknownOutcome(reason, _clock.UtcNow, _clock.UtcNow.Add(ReconcileDelay));

        _events.Add(CommunicationEvent.Record(
            dispatch.OrganizationId,
            CommunicationEventKind.OutcomeUnknown,
            "The provider did not confirm the send. Whether it went is not yet known.",
            _clock.UtcNow,
            accountId: dispatch.AccountId,
            dispatchId: dispatch.Id,
            detail: reason));

        _audit.Record(
            AuditAction.OutboundOutcomeUnknown,
            entityType: nameof(OutboundDispatch),
            entityId: dispatch.Id.ToString(),
            organizationId: dispatch.OrganizationId,
            permission: Permission.CommunicationsSend,
            semanticDelta: new { dispatch.AttemptCount },
            reason: reason);

        await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        return new OutboundStepResult(dispatch.State, reason);
    }

    private async Task<OutboundStepResult> FailRetryableAsync(
        OutboundDispatch dispatch,
        string reason,
        CancellationToken cancellationToken)
    {
        dispatch.NoteRetryableFailure(reason, _clock.UtcNow, _clock.UtcNow.Add(RetryDelay));

        await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        return new OutboundStepResult(dispatch.State, reason);
    }

    private async Task<OutboundStepResult> FailPermanentAsync(
        OutboundDispatch dispatch,
        string reason,
        CancellationToken cancellationToken)
    {
        dispatch.NotePermanentFailure(reason, _clock.UtcNow);

        _events.Add(CommunicationEvent.Record(
            dispatch.OrganizationId,
            CommunicationEventKind.SendFailed,
            "The message could not be sent",
            _clock.UtcNow,
            accountId: dispatch.AccountId,
            dispatchId: dispatch.Id,
            detail: reason));

        _audit.Record(
            AuditAction.OutboundSendFailed,
            entityType: nameof(OutboundDispatch),
            entityId: dispatch.Id.ToString(),
            organizationId: dispatch.OrganizationId,
            permission: Permission.CommunicationsSend,
            reason: reason);

        await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        return new OutboundStepResult(dispatch.State, reason);
    }

    /// <summary>
    /// Assembles what the provider needs, resolving attachments to stored bytes.
    /// </summary>
    /// <remarks>
    /// Attachments are canonical document versions, opened lazily so a message with
    /// four large files does not put all four in memory at once. The bytes the
    /// recipient gets are exactly the bytes AgencyOS can still produce and prove the
    /// digest of.
    /// </remarks>
    private async Task<OutboundDraftRequest> BuildRequestAsync(
        OutboundDispatch dispatch,
        CancellationToken cancellationToken)
    {
        List<OutboundDraftAttachment> attachments = [];

        foreach (OutboundAttachment attachment in dispatch.Attachments)
        {
            DocumentVersion? version = await _documents
                .FindVersionAsync(
                    dispatch.OrganizationId, attachment.DocumentVersionId, cancellationToken)
                .ConfigureAwait(false);

            if (version is null)
            {
                throw new ProviderTransientException("An attached document version is missing.");
            }

            BlobObject? blob = await _blobs
                .FindAsync(dispatch.OrganizationId, version.BlobObjectId, cancellationToken)
                .ConfigureAwait(false);

            if (blob is null)
            {
                throw new ProviderTransientException("An attached document has no stored bytes.");
            }

            string storageKey = blob.StorageKey;
            Domain.Organizations.OrganizationId organizationId = dispatch.OrganizationId;

            attachments.Add(new OutboundDraftAttachment(
                attachment.FileName,
                attachment.MediaType,
                attachment.ByteLength,
                token => _blobStore.OpenReadAsync(organizationId, storageKey, token)));
        }

        return new OutboundDraftRequest(
            dispatch.Subject,
            dispatch.BodyText,
            [
                .. dispatch.Recipients.Select(
                    x => new ProviderParticipant(x.Role, x.Address, x.DisplayName)),
            ],
            attachments);
    }
}
