namespace AgencyOS.Contracts.Documents;

// ------------------------------------------------------------------- requests

/// <param name="Target">
/// Person, Company, TalentProfile, Material, Project, Package, Opportunity,
/// Submission, Deal, Offer, Contract, ContractVersion, Invoice or Payment.
/// </param>
public sealed record DocumentLinkRequest(string Target, Guid TargetId, string? Note = null);

/// <summary>
/// Describes a document being uploaded.
/// </summary>
/// <remarks>
/// The bytes travel as multipart form data alongside this; they are never
/// base64 in a JSON body, because a two-hundred-megabyte file encoded into a
/// string is a request nothing can stream (ADR-0024).
/// </remarks>
/// <param name="Sensitivity">
/// Internal, Confidential, Privileged, Financial or Restricted. Always stated,
/// never inferred from the filename, the folder or the kind: privilege is a legal
/// conclusion a lawyer draws (ADR-0025).
/// </param>
public sealed record RecordDocumentRequest(
    string Title,
    string Kind,
    string Sensitivity,
    string? Reference = null,
    string? Description = null,
    string? Notes = null,
    IReadOnlyList<DocumentLinkRequest>? Links = null);

/// <summary>Records a new version of a document. Never replaces one.</summary>
public sealed record AddDocumentVersionRequest(int ExpectedVersion, string? Notes = null);

public sealed record UpdateDocumentRequest(
    string Title,
    string Kind,
    string Sensitivity,
    int ExpectedVersion,
    string? Reference = null,
    string? Description = null);

public sealed record LinkDocumentRequest(string Target, Guid TargetId, string? Note = null);

/// <summary>Takes a document out of ordinary use. Destroys nothing.</summary>
public sealed record ArchiveDocumentRequest(string Reason, int ExpectedVersion);

public sealed record RestoreDocumentRequest(int ExpectedVersion);

/// <summary>Completes a mailbox connection from an authorization code.</summary>
/// <remarks>
/// The code is exchanged server-side. Neither it nor the tokens it produces are
/// ever returned to a client (ADR-0027).
/// </remarks>
public sealed record ConnectMailboxRequest(
    string Provider,
    string AuthorizationCode,
    string RedirectUri,
    string Visibility = "Private");

public sealed record DisconnectMailboxRequest(int ExpectedVersion);

public sealed record ChangeMailboxVisibilityRequest(string Visibility, int ExpectedVersion);

public sealed record LinkMessageRequest(string Target, Guid TargetId, string? Note = null);

/// <summary>
/// Records that an address belongs to a person or company AgencyOS knows.
/// </summary>
/// <remarks>
/// Exactly one of the two, or neither to withdraw an identification. The raw
/// address and display name stay exactly as the mail said either way (ADR-0026).
/// </remarks>
public sealed record ResolveParticipantRequest(Guid? PersonId = null, Guid? CompanyId = null);

/// <summary>Pulls an attachment's bytes into the canonical document store.</summary>
public sealed record IngestAttachmentRequest(
    string Kind,
    string Sensitivity,
    string? Title = null,
    IReadOnlyList<DocumentLinkRequest>? Links = null);

/// <param name="Role">To, Cc or Bcc.</param>
public sealed record RecipientRequest(string Role, string Address, string? DisplayName = null);

/// <summary>
/// Composes an outbound message.
/// </summary>
/// <remarks>
/// The sending mailbox is named by identifier and checked against its owner
/// server-side. There is no From field: a caller cannot send as somebody else by
/// asking (ADR-0028).
/// </remarks>
public sealed record ComposeMessageRequest(
    Guid AccountId,
    string Subject,
    string BodyText,
    IReadOnlyList<RecipientRequest> Recipients,
    IReadOnlyList<Guid>? AttachmentVersionIds = null,
    Guid? InReplyToMessageId = null);

/// <summary>Hands the message to the worker. The last point at which nothing has left.</summary>
public sealed record QueueMessageRequest(int ExpectedVersion);

public sealed record CancelMessageRequest(int ExpectedVersion);

// ------------------------------------------------------------------ responses

/// <param name="ContentHash">
/// The SHA-256 AgencyOS computed while storing the bytes, so an operator can check
/// a downloaded file is the file the system holds.
/// </param>
/// <param name="RecordedAt">
/// When AgencyOS was told about this version. Not when the document was authored,
/// which the system does not know and does not claim (ADR-0024).
/// </param>
/// <param name="ScanState">
/// Unscanned, Clean, Suspect or Failed. <c>Unscanned</c> until a real scanner says
/// otherwise: AgencyOS implements no malware detection and will not call a file
/// clean on the strength of having stored it.
/// </param>
public sealed record DocumentVersionResponse(
    Guid Id,
    int Sequence,
    string DisplayFileName,
    string MediaType,
    long ByteLength,
    string ContentHash,
    string Source,
    string? SourceExternalReference,
    DateTimeOffset RecordedAt,
    string? CreatedByDisplayName,
    string? Notes,
    string ExtractionState,
    string? ExtractionDetail,
    string ScanState);

/// <param name="TargetLabel">
/// A short name for the linked record. Names only: never a figure, a clause or a
/// classification, because it is rendered beside documents whose permissions differ
/// from the target's (ADR-0025).
/// </param>
public sealed record DocumentLinkResponse(
    Guid Id,
    string Target,
    Guid TargetId,
    string TargetLabel,
    string? Note,
    DateTimeOffset LinkedAt,
    string? LinkedByDisplayName);

public sealed record DocumentEventResponse(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <param name="HoldsContent">
/// Whether AgencyOS actually holds bytes. Published rather than assumed, on the M8
/// precedent: a reader who assumes the file is there goes looking for it during an
/// argument (ADR-0024).
/// </param>
public sealed record DocumentSummaryResponse(
    Guid Id,
    string Title,
    string Kind,
    string Status,
    string Sensitivity,
    string? Reference,
    DocumentVersionResponse? CurrentVersion,
    int VersionCount,
    int LinkCount,
    bool HoldsContent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CreatedByDisplayName,
    int Version);

/// <param name="ExtractedText">
/// Present only where a deterministic extractor produced text. A derived
/// projection, never the canonical content, and not indexed for general search in
/// M10 (ADR-0025).
/// </param>
public sealed record DocumentDetailResponse(
    DocumentSummaryResponse Document,
    string? Description,
    string? ArchiveReason,
    IReadOnlyList<DocumentVersionResponse> Versions,
    IReadOnlyList<DocumentLinkResponse> Links,
    IReadOnlyList<DocumentEventResponse> History,
    string? ExtractedText);

/// <param name="Deduplicated">
/// Whether the organization already held these exact bytes. A storage fact and
/// never an authorization one: identical bytes are not the same document
/// (ADR-0024).
/// </param>
public sealed record RecordDocumentResponse(
    Guid DocumentId,
    Guid VersionId,
    string ContentHash,
    long ByteLength,
    bool Deduplicated);

public sealed record LinkDocumentResponse(Guid LinkId);

/// <summary>One connected mailbox.</summary>
/// <remarks>
/// Carries no token, no fragment of one, and no field that could hold one. The
/// shape of this record is part of the guarantee (ADR-0027).
/// </remarks>
public sealed record CommunicationAccountResponse(
    Guid Id,
    string Provider,
    string MailboxAddress,
    string? DisplayName,
    Guid OwnerUserId,
    string? OwnerDisplayName,
    string State,
    string Visibility,
    string GrantedScopes,
    DateTimeOffset? LastSyncedAt,
    string? LastSyncError,
    bool HasStoredCredential,
    DateTimeOffset? CredentialExpiresAt,
    int MessageCount,
    DateTimeOffset CreatedAt,
    int Version);

public sealed record ParticipantResponse(
    Guid Id,
    string Role,
    string Address,
    string? DisplayName,
    Guid? PersonId,
    string? PersonDisplayName,
    Guid? CompanyId,
    string? CompanyDisplayName,
    string? ResolvedByDisplayName);

/// <param name="HoldsContent">
/// Whether the bytes have actually been ingested, as opposed to the attachment
/// merely being known to exist (ADR-0024).
/// </param>
public sealed record MessageAttachmentResponse(
    Guid Id,
    string FileName,
    string MediaType,
    long ByteLength,
    bool IsInline,
    bool HoldsContent,
    Guid? DocumentVersionId,
    Guid? DocumentId,
    DateTimeOffset? IngestedAt);

public sealed record MessageLinkResponse(
    Guid Id,
    string Target,
    Guid TargetId,
    string TargetLabel,
    string? Note,
    DateTimeOffset LinkedAt,
    string? LinkedByDisplayName);

/// <param name="SynchronizedAt">
/// When AgencyOS first saw it. A third date beside sent and received, never merged
/// with either.
/// </param>
public sealed record MessageSummaryResponse(
    Guid Id,
    Guid AccountId,
    string MailboxAddress,
    string Direction,
    string? Subject,
    string? FromAddress,
    string? FromDisplayName,
    IReadOnlyList<string> ToAddresses,
    DateTimeOffset OccurredAt,
    DateTimeOffset SynchronizedAt,
    bool HasAttachments,
    int AttachmentCount,
    int LinkCount,
    bool IsDeletedAtProvider,
    string? Folder);

/// <param name="SanitizedHtml">
/// Sanitized on the way in, once. Script, styles, frames, forms and external
/// images are gone, so a stored message cannot run anything and cannot tell its
/// sender that somebody looked at it three years later (ADR-0026).
/// </param>
public sealed record MessageDetailResponse(
    MessageSummaryResponse Message,
    string? BodyText,
    string? SanitizedHtml,
    string? InternetMessageId,
    string? ExternalMessageId,
    IReadOnlyList<ParticipantResponse> Participants,
    IReadOnlyList<MessageAttachmentResponse> Attachments,
    IReadOnlyList<MessageLinkResponse> Links,
    IReadOnlyList<MessageSummaryResponse> Thread);

public sealed record OutboundAttachmentResponse(
    Guid DocumentId,
    Guid DocumentVersionId,
    string FileName,
    string MediaType,
    long ByteLength);

/// <summary>One outbound send operation and the evidence behind it.</summary>
/// <param name="State">
/// Draft, Queued, ProviderDraftCreated, SendRequested, Sent, FailedRetryable,
/// FailedPermanent, UnknownOutcome or Cancelled.
/// </param>
/// <param name="NeedsAttention">
/// True while the outcome is unknown or the send failed for good. The one thing a
/// person has to look at, rather than a state the interface smooths over
/// (ADR-0028).
/// </param>
/// <param name="LastVerdict">
/// FoundSent, ProvenAbsent or Inconclusive, once reconciliation has run.
/// Inconclusive is reported as itself and never rounded to a failure.
/// </param>
public sealed record OutboundDispatchResponse(
    Guid Id,
    Guid AccountId,
    string MailboxAddress,
    string State,
    string Subject,
    IReadOnlyList<ParticipantResponse> Recipients,
    IReadOnlyList<OutboundAttachmentResponse> Attachments,
    int AttemptCount,
    string? LastError,
    string? LastVerdict,
    DateTimeOffset? LastReconciledAt,
    bool HasProviderDraft,
    bool HasProviderEvidence,
    Guid? SentMessageId,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CreatedByDisplayName,
    bool NeedsAttention,
    int Version);

public sealed record CommunicationEventResponse(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <summary>What the communications desk has to look at.</summary>
/// <remarks>
/// Counts and real rows. No inbox clone, no triage and no ranking: M10 records
/// communications and does not interpret them (ADR-0026).
/// </remarks>
public sealed record CommunicationCommandCenterResponse(
    IReadOnlyList<OutboundDispatchResponse> UnknownOutcomes,
    IReadOnlyList<OutboundDispatchResponse> FailedSends,
    IReadOnlyList<CommunicationAccountResponse> AccountsNeedingAttention,
    int UnknownOutcomeCount,
    int FailedSendCount,
    int DisconnectedAccountCount);

/// <param name="IsUnambiguous">
/// Whether this was the only match. False means the interface offers a choice
/// rather than a default: a wrong identification quietly attributes somebody's
/// correspondence to the wrong person (ADR-0026).
/// </param>
public sealed record ParticipantSuggestionResponse(
    Guid? PersonId,
    Guid? CompanyId,
    string DisplayName,
    string MatchedAddress,
    bool IsUnambiguous);

/// <summary>
/// A mail provider this server can actually connect to, and where to authorize it.
/// </summary>
/// <remarks>
/// <para>
/// Published so the client does not have to construct an authorization URL, which
/// would mean shipping the application registration in the Windows build. The URL
/// carries the application identifier and the scopes; both are public in OAuth by
/// design, and neither is a secret (ADR-0027).
/// </para>
/// <para>
/// <c>AuthorizationUrl</c> is null when the provider has no browser step or the
/// server has not been provisioned. A null is reported as itself rather than as an
/// empty string, so the interface can say "an administrator has not set this up"
/// instead of offering a link that goes nowhere.
/// </para>
/// </remarks>
public sealed record CommunicationProviderResponse(
    string Provider,
    string DisplayName,
    bool IsConfigured,
    string? Scopes,
    string? AuthorizationUrl);

public sealed record ConnectMailboxResponse(Guid AccountId);

public sealed record ComposeMessageResponse(Guid DispatchId);

public sealed record LinkMessageResponse(Guid LinkId);

public sealed record IngestAttachmentResponse(
    Guid DocumentId,
    Guid VersionId,
    string ContentHash,
    long ByteLength);
