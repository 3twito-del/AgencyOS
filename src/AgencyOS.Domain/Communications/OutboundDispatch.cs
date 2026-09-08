using AgencyOS.Domain.Common;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Communications;

/// <summary>Opaque, immutable identifier for an <see cref="OutboundDispatch"/>.</summary>
public readonly record struct OutboundDispatchId(Guid Value)
{
    public static OutboundDispatchId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Where an outbound message has got to.
/// </summary>
/// <remarks>
/// <para>
/// The states exist to keep one distinction that everything else in M10 depends
/// on: <strong>not knowing whether a message was sent is not the same as knowing it
/// was not</strong>. A provider that accepted a send and then failed to answer has
/// left AgencyOS unable to prove either outcome, and the only safe thing to do with
/// that is to say so (ADR-0028).
/// </para>
/// <para>
/// The unsafe design — the one this vocabulary exists to prevent — treats a lost
/// acknowledgement as a failure and retries. That sends a client the same
/// commercial email twice, and nobody finds out from the system.
/// </para>
/// </remarks>
public enum OutboundDispatchState
{
    /// <summary>Composed and saved. Nothing has been asked of the provider.</summary>
    Draft = 1,

    /// <summary>Submitted. A worker will pick it up.</summary>
    Queued = 2,

    /// <summary>
    /// A draft exists at the provider and AgencyOS knows its identifier.
    /// </summary>
    /// <remarks>
    /// Nothing has been sent. The identifier is what makes recovery possible: it is
    /// how AgencyOS later asks "did this actually go out" and gets an answer.
    /// </remarks>
    ProviderDraftCreated = 3,

    /// <summary>
    /// AgencyOS has asked the provider to send, and does not yet know the result.
    /// </summary>
    /// <remarks>
    /// Recorded <strong>before</strong> the call, never after. A crash between the
    /// call and the record would otherwise leave a row saying nothing had been sent
    /// while the provider was already sending it.
    /// </remarks>
    SendRequested = 4,

    /// <summary>The provider confirmed the send. Terminal.</summary>
    Sent = 5,

    /// <summary>Failed before the provider could have committed. Safe to retry.</summary>
    FailedRetryable = 6,

    /// <summary>Failed for a reason retrying cannot fix.</summary>
    FailedPermanent = 7,

    /// <summary>
    /// The provider may or may not have sent it, and AgencyOS cannot yet prove which.
    /// </summary>
    /// <remarks>
    /// Never retried, never automatically called a failure, and never hidden behind
    /// a friendlier word in the interface. A person is told, because a person is the
    /// only one who can go and look.
    /// </remarks>
    UnknownOutcome = 8,

    /// <summary>Withdrawn before anything left. Terminal.</summary>
    Cancelled = 9,
}

/// <summary>What reconciliation established about a message of unknown outcome.</summary>
public enum DispatchReconciliationVerdict
{
    /// <summary>The message was found at the provider. It was sent.</summary>
    FoundSent = 1,

    /// <summary>
    /// The provider says the message is not there, and the draft still is.
    /// </summary>
    /// <remarks>
    /// The only evidence that makes a retry safe: the message did not go, and the
    /// draft AgencyOS would send is still the same draft.
    /// </remarks>
    ProvenAbsent = 2,

    /// <summary>The provider could not answer. The outcome stays unknown.</summary>
    Inconclusive = 3,
}

/// <summary>
/// One canonical intent to send an external message.
/// </summary>
/// <remarks>
/// <para>
/// The row is the truth about the operation, and it lives in PostgreSQL rather than
/// in a worker's memory, so a restart loses nothing. Every transition below is
/// written before or after a specific external call, in an order chosen so that no
/// crash can lose the knowledge that a send may have happened (ADR-0028).
/// </para>
/// <para>
/// The protocol is modelled in <c>specs/OutboundSend.tla</c> and checked by TLC.
/// The properties that matter are that one intent never knowingly sends twice, that
/// <see cref="OutboundDispatchState.Sent"/> is never left, and that
/// <see cref="OutboundDispatchState.UnknownOutcome"/> is never resolved by
/// assumption.
/// </para>
/// </remarks>
public sealed class OutboundDispatch
{
    private readonly List<OutboundRecipient> _recipients = [];
    private readonly List<OutboundAttachment> _attachments = [];

    private OutboundDispatch()
    {
    }

    public OutboundDispatchId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The mailbox it will be sent from.</summary>
    public CommunicationAccountId AccountId { get; private set; }

    public OutboundDispatchState State { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string BodyText { get; private set; } = string.Empty;

    /// <summary>The message being replied to, when this is a reply.</summary>
    public CommunicationMessageId? InReplyToMessageId { get; private set; }

    /// <summary>
    /// A correlation value AgencyOS generates once and reuses on every attempt.
    /// </summary>
    /// <remarks>
    /// The provider is asked to carry it, so a message found in the sent items
    /// during reconciliation can be matched back to this intent with certainty
    /// rather than by comparing subjects and timestamps (ADR-0028).
    /// </remarks>
    public string ClientReference { get; private set; } = string.Empty;

    /// <summary>The provider's draft identifier, once a draft exists.</summary>
    public string? ProviderDraftId { get; private set; }

    /// <summary>The provider's message identifier, once a send is confirmed.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>The RFC 5322 Message-ID, when the provider reports one.</summary>
    public string? InternetMessageId { get; private set; }

    /// <summary>The canonical message row created once the send is confirmed.</summary>
    public CommunicationMessageId? SentMessageId { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>What reconciliation last established, when it has run.</summary>
    public DispatchReconciliationVerdict? LastVerdict { get; private set; }

    public DateTimeOffset? LastReconciledAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the provider confirmed the send.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    // ---- worker lease -------------------------------------------------------

    public string? LeaseOwner { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    /// <summary>The earliest the worker should look at this again.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public int Version { get; private set; }

    public IReadOnlyList<OutboundRecipient> Recipients => _recipients;

    public IReadOnlyList<OutboundAttachment> Attachments => _attachments;

    /// <summary>Whether a worker should be doing anything about this row.</summary>
    public bool IsWorkable =>
        State is OutboundDispatchState.Queued
            or OutboundDispatchState.ProviderDraftCreated
            or OutboundDispatchState.SendRequested
            or OutboundDispatchState.FailedRetryable;

    /// <summary>Whether the operation has finished, one way or another.</summary>
    public bool IsSettled =>
        State is OutboundDispatchState.Sent
            or OutboundDispatchState.FailedPermanent
            or OutboundDispatchState.Cancelled;

    /// <summary>Whether a person needs to go and look.</summary>
    public bool NeedsAttention =>
        State is OutboundDispatchState.UnknownOutcome or OutboundDispatchState.FailedPermanent;

    public static OutboundDispatch Compose(
        OrganizationId organizationId,
        CommunicationAccountId accountId,
        string subject,
        string bodyText,
        string clientReference,
        UserId createdBy,
        DateTimeOffset now,
        CommunicationMessageId? inReplyToMessageId = null) =>
        new()
        {
            Id = OutboundDispatchId.New(),
            OrganizationId = organizationId,
            AccountId = accountId,
            State = OutboundDispatchState.Draft,
            Subject = Ensure.NotBlankMax(subject, nameof(subject), 500),
            BodyText = Ensure.NotBlankMax(bodyText, nameof(bodyText), 500_000),
            ClientReference = Ensure.NotBlankMax(clientReference, nameof(clientReference), 100),
            InReplyToMessageId = inReplyToMessageId,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
            Version = 1,
        };

    public OutboundRecipient AddRecipient(
        ParticipantRole role,
        string address,
        string? displayName = null)
    {
        RequireDraft();

        if (role is not (ParticipantRole.To or ParticipantRole.Cc or ParticipantRole.Bcc))
        {
            throw new DomainException("A recipient is a To, Cc or Bcc address.");
        }

        OutboundRecipient recipient = OutboundRecipient.Create(
            OrganizationId, Id, role, address, displayName);

        _recipients.Add(recipient);
        return recipient;
    }

    /// <summary>Attaches a document version AgencyOS already holds.</summary>
    /// <remarks>
    /// Only a canonical version, never an arbitrary file path. The bytes the
    /// recipient receives are then exactly the bytes AgencyOS can still produce and
    /// prove the digest of.
    /// </remarks>
    public OutboundAttachment AddAttachment(
        DocumentId documentId,
        DocumentVersionId documentVersionId,
        string fileName,
        string mediaType,
        long byteLength)
    {
        RequireDraft();

        if (_attachments.Any(x => x.DocumentVersionId == documentVersionId))
        {
            throw new DomainException("That version is already attached.");
        }

        OutboundAttachment attachment = OutboundAttachment.Create(
            OrganizationId, Id, documentId, documentVersionId, fileName, mediaType, byteLength);

        _attachments.Add(attachment);
        return attachment;
    }

    /// <summary>Hands the message to the worker.</summary>
    public void Queue(int expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireDraft();

        if (_recipients.Count == 0)
        {
            throw new DomainException("A message with no recipients cannot be sent.");
        }

        State = OutboundDispatchState.Queued;
        NextAttemptAt = now;

        Touch(now);
    }

    /// <summary>
    /// Records that a draft now exists at the provider.
    /// </summary>
    /// <remarks>
    /// Written after the draft call returns and before any send. Creating a draft
    /// has no external consequence anybody sees, so this step is safe to repeat;
    /// what must never repeat is the send.
    /// </remarks>
    public void NoteProviderDraft(string providerDraftId, DateTimeOffset now)
    {
        if (State is not (OutboundDispatchState.Queued or OutboundDispatchState.FailedRetryable))
        {
            throw new DomainException(
                $"A provider draft cannot be recorded from {State}.");
        }

        ProviderDraftId = Ensure.NotBlankMax(providerDraftId, nameof(providerDraftId), 300);
        State = OutboundDispatchState.ProviderDraftCreated;
        LastError = null;

        Touch(now);
    }

    /// <summary>
    /// Records that AgencyOS is about to ask the provider to send.
    /// </summary>
    /// <remarks>
    /// <strong>Called and committed before the provider call, never after.</strong>
    /// This single ordering is what makes the protocol safe: after any crash, a row
    /// in this state tells the recovering worker that a send may already have
    /// happened, and it reconciles instead of sending (ADR-0028).
    /// </remarks>
    public void NoteSendRequested(DateTimeOffset now)
    {
        if (State is not OutboundDispatchState.ProviderDraftCreated)
        {
            throw new DomainException($"A send cannot be requested from {State}.");
        }

        State = OutboundDispatchState.SendRequested;
        AttemptCount++;

        Touch(now);
    }

    /// <summary>Records the provider's confirmation. Terminal.</summary>
    public void NoteSent(
        string? providerMessageId,
        string? internetMessageId,
        DateTimeOffset now)
    {
        if (State is OutboundDispatchState.Sent)
        {
            return;
        }

        if (State is not (OutboundDispatchState.SendRequested or OutboundDispatchState.UnknownOutcome))
        {
            throw new DomainException($"A send cannot be confirmed from {State}.");
        }

        State = OutboundDispatchState.Sent;
        ProviderMessageId = Ensure.OptionalMax(providerMessageId, nameof(providerMessageId), 300);
        InternetMessageId = Ensure.OptionalMax(internetMessageId, nameof(internetMessageId), 500);
        SentAt = now;
        LastError = null;
        NextAttemptAt = null;

        Touch(now);
    }

    /// <summary>Connects the confirmed send to the canonical message row.</summary>
    public void NoteSentMessage(CommunicationMessageId messageId, DateTimeOffset now)
    {
        if (State is not OutboundDispatchState.Sent)
        {
            throw new DomainException("Only a sent dispatch has a sent message.");
        }

        SentMessageId = messageId;
        Touch(now);
    }

    /// <summary>
    /// Records a failure that definitely happened before the provider committed.
    /// </summary>
    /// <remarks>
    /// Only for refusals the provider stated: a rejected recipient, a validation
    /// error, an expired credential. A timeout is not one of these, because a
    /// timeout is exactly the case where the provider may have gone ahead.
    /// </remarks>
    public void NoteRetryableFailure(string error, DateTimeOffset now, DateTimeOffset retryAt)
    {
        if (State is OutboundDispatchState.Sent)
        {
            throw new DomainException("A sent message cannot fail afterwards.");
        }

        State = OutboundDispatchState.FailedRetryable;
        LastError = Ensure.NotBlankMax(error, nameof(error), 1000);
        NextAttemptAt = retryAt;

        Touch(now);
    }

    public void NotePermanentFailure(string error, DateTimeOffset now)
    {
        if (State is OutboundDispatchState.Sent)
        {
            throw new DomainException("A sent message cannot fail afterwards.");
        }

        State = OutboundDispatchState.FailedPermanent;
        LastError = Ensure.NotBlankMax(error, nameof(error), 1000);
        NextAttemptAt = null;

        Touch(now);
    }

    /// <summary>
    /// Records that AgencyOS cannot prove whether the message went.
    /// </summary>
    /// <remarks>
    /// Reachable only from <see cref="OutboundDispatchState.SendRequested"/>, which
    /// is the only state in which the question can arise.
    /// </remarks>
    public void NoteUnknownOutcome(string reason, DateTimeOffset now, DateTimeOffset reconcileAt)
    {
        if (State is not OutboundDispatchState.SendRequested)
        {
            throw new DomainException(
                $"An outcome can only become unknown from {OutboundDispatchState.SendRequested}.");
        }

        State = OutboundDispatchState.UnknownOutcome;
        LastError = Ensure.NotBlankMax(reason, nameof(reason), 1000);
        NextAttemptAt = reconcileAt;

        Touch(now);
    }

    /// <summary>
    /// Applies what reconciliation found.
    /// </summary>
    /// <remarks>
    /// The only route out of <see cref="OutboundDispatchState.UnknownOutcome"/>, and
    /// it moves only on evidence. Proven absence returns the dispatch to its draft
    /// state, where a retry is safe because the message demonstrably did not go.
    /// An inconclusive answer changes nothing except when to look again — the state
    /// stays unknown for as long as it is unknown (ADR-0028).
    /// </remarks>
    public void ApplyReconciliation(
        DispatchReconciliationVerdict verdict,
        DateTimeOffset now,
        DateTimeOffset? nextAttemptAt = null,
        string? providerMessageId = null,
        string? internetMessageId = null)
    {
        if (State is not OutboundDispatchState.UnknownOutcome)
        {
            throw new DomainException("Only an unknown outcome is reconciled.");
        }

        LastVerdict = verdict;
        LastReconciledAt = now;

        switch (verdict)
        {
            case DispatchReconciliationVerdict.FoundSent:
                NoteSent(providerMessageId, internetMessageId, now);
                return;

            case DispatchReconciliationVerdict.ProvenAbsent:
                State = OutboundDispatchState.ProviderDraftCreated;
                LastError = null;
                NextAttemptAt = nextAttemptAt ?? now;
                break;

            default:
                NextAttemptAt = nextAttemptAt;
                break;
        }

        Touch(now);
    }

    /// <summary>Withdraws a message that has not reached the provider.</summary>
    public void Cancel(int expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);

        if (State is not (OutboundDispatchState.Draft or OutboundDispatchState.Queued))
        {
            throw new DomainException(
                $"A message that has reached {State} cannot be cancelled. "
                    + "AgencyOS cannot unsend anything.");
        }

        State = OutboundDispatchState.Cancelled;
        NextAttemptAt = null;

        Touch(now);
    }

    /// <summary>Claims this dispatch for one worker.</summary>
    public bool TryLease(string owner, TimeSpan duration, DateTimeOffset now)
    {
        if (LeaseExpiresAt is { } expires && expires > now && LeaseOwner != owner)
        {
            return false;
        }

        LeaseOwner = Ensure.NotBlankMax(owner, nameof(owner), 100);
        LeaseExpiresAt = now.Add(duration);

        Touch(now);
        return true;
    }

    public void ReleaseLease(DateTimeOffset now)
    {
        LeaseOwner = null;
        LeaseExpiresAt = null;

        Touch(now);
    }

    private void RequireDraft()
    {
        if (State != OutboundDispatchState.Draft)
        {
            throw new DomainException("Only a draft message can be edited.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(OutboundDispatch), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>Somebody a message is addressed to.</summary>
public sealed class OutboundRecipient
{
    private OutboundRecipient()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OutboundDispatchId DispatchId { get; private set; }

    public ParticipantRole Role { get; private set; }

    public string Address { get; private set; } = string.Empty;

    public string? DisplayName { get; private set; }

    internal static OutboundRecipient Create(
        OrganizationId organizationId,
        OutboundDispatchId dispatchId,
        ParticipantRole role,
        string address,
        string? displayName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            DispatchId = dispatchId,
            Role = role,
            Address = Ensure.NotBlankMax(address, nameof(address), 320).ToLowerInvariant(),
            DisplayName = Ensure.OptionalMax(displayName, nameof(displayName), 200),
        };
}

/// <summary>A canonical document version travelling with an outbound message.</summary>
public sealed class OutboundAttachment
{
    private OutboundAttachment()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public OutboundDispatchId DispatchId { get; private set; }

    public DocumentId DocumentId { get; private set; }

    public DocumentVersionId DocumentVersionId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string MediaType { get; private set; } = string.Empty;

    public long ByteLength { get; private set; }

    internal static OutboundAttachment Create(
        OrganizationId organizationId,
        OutboundDispatchId dispatchId,
        DocumentId documentId,
        DocumentVersionId documentVersionId,
        string fileName,
        string mediaType,
        long byteLength) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            DispatchId = dispatchId,
            DocumentId = documentId,
            DocumentVersionId = documentVersionId,
            FileName = Ensure.NotBlankMax(fileName, nameof(fileName), 300),
            MediaType = Ensure.NotBlankMax(mediaType, nameof(mediaType), 150),
            ByteLength = byteLength,
        };
}

/// <summary>What a communication event records.</summary>
public enum CommunicationEventKind
{
    AccountConnected = 1,
    AccountDisconnected = 2,
    AccountVisibilityChanged = 3,
    MailboxSynchronized = 4,
    SyncFailed = 5,
    MessageLinked = 6,
    MessageUnlinked = 7,
    ParticipantResolved = 8,
    AttachmentIngested = 9,
    DispatchComposed = 10,
    DispatchQueued = 11,
    ProviderDraftCreated = 12,
    SendRequested = 13,
    SendConfirmed = 14,
    SendFailed = 15,
    OutcomeUnknown = 16,
    Reconciled = 17,
    DispatchCancelled = 18,
}

/// <summary>
/// One entry in the curated communication history.
/// </summary>
/// <remarks>
/// One business act, one entry, in the words a person would use. The audit trail
/// answers a different question in a different vocabulary and is never shown here
/// instead (ADR-0012).
/// </remarks>
public sealed class CommunicationEvent
{
    private CommunicationEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public CommunicationAccountId? AccountId { get; private set; }

    public CommunicationMessageId? MessageId { get; private set; }

    public OutboundDispatchId? DispatchId { get; private set; }

    public CommunicationEventKind Kind { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    /// <summary>
    /// Extra context.
    /// </summary>
    /// <remarks>
    /// Never a message body, never a token, never an attachment's contents. This is
    /// history a person reads, not a copy of the mail.
    /// </remarks>
    public string? Detail { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Who did it, when a person did. Null for worker activity.</summary>
    public UserId? ActorUserId { get; private set; }

    public static CommunicationEvent Record(
        OrganizationId organizationId,
        CommunicationEventKind kind,
        string summary,
        DateTimeOffset now,
        UserId? actorUserId = null,
        CommunicationAccountId? accountId = null,
        CommunicationMessageId? messageId = null,
        OutboundDispatchId? dispatchId = null,
        string? detail = null) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            AccountId = accountId,
            MessageId = messageId,
            DispatchId = dispatchId,
            Kind = kind,
            Summary = Ensure.NotBlankMax(summary, nameof(summary), 500),
            Detail = Ensure.OptionalMax(detail, nameof(detail), 2000),
            OccurredAt = now,
            ActorUserId = actorUserId,
        };
}
