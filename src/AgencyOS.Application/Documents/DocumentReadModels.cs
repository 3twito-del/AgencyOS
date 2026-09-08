using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Documents;

/// <summary>One document, as a list renders it.</summary>
/// <param name="CurrentVersion">
/// The newest version, derived from the version rows rather than read from a
/// pointer that could disagree with them.
/// </param>
/// <param name="HoldsContent">
/// Whether AgencyOS actually holds bytes. Published rather than assumed, on the M8
/// precedent: a reader who assumes the file is there goes looking for it during an
/// argument (ADR-0024).
/// </param>
public sealed record DocumentSummaryModel(
    DocumentId Id,
    string Title,
    DocumentKind Kind,
    DocumentStatus Status,
    DocumentSensitivity Sensitivity,
    string? Reference,
    DocumentVersionSummaryModel? CurrentVersion,
    int VersionCount,
    int LinkCount,
    bool HoldsContent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CreatedByDisplayName,
    int Version);

/// <param name="ContentHash">
/// The SHA-256 AgencyOS computed while storing the bytes. Shown so an operator can
/// verify a file they downloaded is the file the system holds.
/// </param>
/// <param name="RecordedAt">
/// When AgencyOS was told about this version. Not when the document was authored,
/// which the system does not know and does not claim.
/// </param>
public sealed record DocumentVersionSummaryModel(
    DocumentVersionId Id,
    int Sequence,
    string DisplayFileName,
    string MediaType,
    long ByteLength,
    string ContentHash,
    DocumentVersionSource Source,
    string? SourceExternalReference,
    DateTimeOffset RecordedAt,
    string? CreatedByDisplayName,
    string? Notes,
    TextExtractionState ExtractionState,
    string? ExtractionDetail,
    BlobScanState ScanState);

/// <param name="TargetLabel">
/// A short name for the linked record. Names only: never a figure, a clause or a
/// classification, because it is rendered beside documents whose permissions differ
/// from the target's (ADR-0025).
/// </param>
public sealed record DocumentLinkModel(
    Guid Id,
    DocumentLinkTarget Target,
    Guid TargetId,
    string TargetLabel,
    string? Note,
    DateTimeOffset LinkedAt,
    string? LinkedByDisplayName);

/// <summary>One entry in a document's curated history.</summary>
public sealed record DocumentEventModel(
    DateTimeOffset OccurredAt,
    DocumentEventKind Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <param name="ExtractedText">
/// Present only where extraction succeeded and the caller may read the document.
/// A derived projection, never the canonical content, and never indexed for
/// general search in M10 (ADR-0025).
/// </param>
public sealed record DocumentDetailModel(
    DocumentSummaryModel Document,
    string? Description,
    string? ArchiveReason,
    IReadOnlyList<DocumentVersionSummaryModel> Versions,
    IReadOnlyList<DocumentLinkModel> Links,
    IReadOnlyList<DocumentEventModel> History,
    string? ExtractedText);

/// <summary>The optional predicates a document list accepts.</summary>
/// <remarks>
/// Metadata only. <see cref="Search"/> covers title, filename and reference, never
/// extracted text: a snippet from a privileged contract is precisely the leak the
/// classification exists to prevent (ADR-0025).
/// </remarks>
public sealed record DocumentFilter(
    DocumentKind? Kind = null,
    DocumentStatus? Status = null,
    DocumentSensitivity? Sensitivity = null,
    DocumentVersionSource? Source = null,
    DocumentLinkTarget? LinkedTarget = null,
    Guid? LinkedTargetId = null,
    bool HasContent = false,
    DateOnly? CreatedAfter = null,
    DateOnly? CreatedBefore = null,
    string? Search = null);

/// <summary>What the caller may see, and what they asked for.</summary>
/// <remarks>
/// The readable set is applied inside the query, before counting, ranking or
/// paging. Filtering afterwards would leak the count, and a count of privileged
/// documents about a named person is itself a disclosure (ADR-0025).
/// </remarks>
public sealed record DocumentQueryScope(
    IReadOnlySet<DocumentSensitivity> ReadableSensitivities,
    DocumentFilter Filter);

/// <summary>Read-side projections for documents.</summary>
public interface IDocumentQueries
{
    Task<IReadOnlyList<DocumentSummaryModel>> ListAsync(
        OrganizationId organizationId,
        DocumentQueryScope scope,
        int limit,
        CancellationToken cancellationToken = default);

    Task<DocumentDetailModel?> GetAsync(
        OrganizationId organizationId,
        DocumentId id,
        CancellationToken cancellationToken = default);

    /// <summary>Documents attached to one business record.</summary>
    Task<IReadOnlyList<DocumentSummaryModel>> ListForTargetAsync(
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        IReadOnlySet<DocumentSensitivity> readableSensitivities,
        CancellationToken cancellationToken = default);
}

/// <summary>One connected mailbox, as the accounts list renders it.</summary>
/// <remarks>
/// Deliberately carries no token, no fragment of one, and no field that could hold
/// one. The shape of this record is part of the guarantee (ADR-0027).
/// </remarks>
public sealed record CommunicationAccountModel(
    CommunicationAccountId Id,
    CommunicationProviderKind Provider,
    string MailboxAddress,
    string? DisplayName,
    Guid OwnerUserId,
    string? OwnerDisplayName,
    CommunicationAccountState State,
    MailboxVisibility Visibility,
    string GrantedScopes,
    DateTimeOffset? LastSyncedAt,
    string? LastSyncError,
    bool HasStoredCredential,
    DateTimeOffset? CredentialExpiresAt,
    int MessageCount,
    DateTimeOffset CreatedAt,
    int Version);

public sealed record CommunicationParticipantModel(
    Guid Id,
    ParticipantRole Role,
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
public sealed record CommunicationAttachmentModel(
    CommunicationAttachmentId Id,
    string FileName,
    string MediaType,
    long ByteLength,
    bool IsInline,
    bool HoldsContent,
    DocumentVersionId? DocumentVersionId,
    DocumentId? DocumentId,
    DateTimeOffset? IngestedAt);

public sealed record CommunicationLinkModel(
    Guid Id,
    DocumentLinkTarget Target,
    Guid TargetId,
    string TargetLabel,
    string? Note,
    DateTimeOffset LinkedAt,
    string? LinkedByDisplayName);

/// <summary>One message, as a list renders it.</summary>
public sealed record CommunicationMessageSummaryModel(
    CommunicationMessageId Id,
    CommunicationAccountId AccountId,
    string MailboxAddress,
    MessageDirection Direction,
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
/// Sanitized on the way in, once. Script, styles, frames, forms and external images
/// are gone, so a stored message cannot run anything or tell its sender that
/// somebody looked at it three years later (ADR-0026).
/// </param>
public sealed record CommunicationMessageDetailModel(
    CommunicationMessageSummaryModel Message,
    string? BodyText,
    string? SanitizedHtml,
    string? InternetMessageId,
    string? ExternalMessageId,
    IReadOnlyList<CommunicationParticipantModel> Participants,
    IReadOnlyList<CommunicationAttachmentModel> Attachments,
    IReadOnlyList<CommunicationLinkModel> Links,
    IReadOnlyList<CommunicationMessageSummaryModel> Thread);

/// <summary>One outbound send operation and the evidence behind it.</summary>
/// <param name="NeedsAttention">
/// True while the outcome is unknown or the send failed for good. The one thing a
/// person must look at, rather than a state the interface smooths over (ADR-0028).
/// </param>
public sealed record OutboundDispatchModel(
    OutboundDispatchId Id,
    CommunicationAccountId AccountId,
    string MailboxAddress,
    OutboundDispatchState State,
    string Subject,
    IReadOnlyList<CommunicationParticipantModel> Recipients,
    IReadOnlyList<CommunicationAttachmentSummaryModel> Attachments,
    int AttemptCount,
    string? LastError,
    DispatchReconciliationVerdict? LastVerdict,
    DateTimeOffset? LastReconciledAt,
    bool HasProviderDraft,
    bool HasProviderEvidence,
    CommunicationMessageId? SentMessageId,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CreatedByDisplayName,
    bool NeedsAttention,
    int Version);

public sealed record CommunicationAttachmentSummaryModel(
    DocumentId DocumentId,
    DocumentVersionId DocumentVersionId,
    string FileName,
    string MediaType,
    long ByteLength);

/// <summary>One entry in the curated communication history.</summary>
public sealed record CommunicationEventModel(
    DateTimeOffset OccurredAt,
    CommunicationEventKind Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <summary>The optional predicates a message list accepts.</summary>
public sealed record CommunicationFilter(
    CommunicationAccountId? AccountId = null,
    MessageDirection? Direction = null,
    DocumentLinkTarget? LinkedTarget = null,
    Guid? LinkedTargetId = null,
    bool UnlinkedOnly = false,
    bool HasAttachments = false,
    DateOnly? OccurredAfter = null,
    DateOnly? OccurredBefore = null,
    string? Search = null);

/// <summary>
/// Which mailboxes the caller may read, plus what they asked for.
/// </summary>
/// <param name="ReadableAccountIds">
/// Computed from ownership and grants, and applied inside the query. Filtering
/// afterwards would leak how many messages exist in a mailbox the caller cannot
/// open (ADR-0026).
/// </param>
public sealed record CommunicationQueryScope(
    IReadOnlySet<CommunicationAccountId> ReadableAccountIds,
    CommunicationFilter Filter);

/// <summary>What the communications desk has to look at.</summary>
/// <remarks>
/// Counts and real rows. No inbox clone, no triage, no ranking, and nothing that
/// interprets what a message means (ADR-0026).
/// </remarks>
public sealed record CommunicationCommandCenterModel(
    IReadOnlyList<OutboundDispatchModel> UnknownOutcomes,
    IReadOnlyList<OutboundDispatchModel> FailedSends,
    IReadOnlyList<CommunicationAccountModel> AccountsNeedingAttention,
    int UnknownOutcomeCount,
    int FailedSendCount,
    int DisconnectedAccountCount);

/// <summary>Read-side projections for communications.</summary>
public interface ICommunicationQueries
{
    Task<IReadOnlyList<CommunicationAccountModel>> ListAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommunicationMessageSummaryModel>> ListMessagesAsync(
        OrganizationId organizationId,
        CommunicationQueryScope scope,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CommunicationMessageDetailModel?> GetMessageAsync(
        OrganizationId organizationId,
        CommunicationMessageId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboundDispatchModel>> ListDispatchesAsync(
        OrganizationId organizationId,
        IReadOnlySet<CommunicationAccountId> readableAccountIds,
        OutboundDispatchState? state,
        int limit,
        CancellationToken cancellationToken = default);

    Task<OutboundDispatchModel?> GetDispatchAsync(
        OrganizationId organizationId,
        OutboundDispatchId id,
        CancellationToken cancellationToken = default);

    /// <summary>Messages linked to one business record.</summary>
    Task<IReadOnlyList<CommunicationMessageSummaryModel>> ListForTargetAsync(
        OrganizationId organizationId,
        DocumentLinkTarget target,
        Guid targetId,
        IReadOnlySet<CommunicationAccountId> readableAccountIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommunicationEventModel>> GetHistoryAsync(
        OrganizationId organizationId,
        CommunicationAccountId? accountId,
        OutboundDispatchId? dispatchId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CommunicationCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        IReadOnlySet<CommunicationAccountId> readableAccountIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// People and companies whose recorded address matches exactly.
    /// </summary>
    /// <remarks>
    /// Suggestions, never assertions, and never narrowed to one when two match.
    /// A wrong identification quietly attributes somebody's correspondence to the
    /// wrong person (ADR-0026).
    /// </remarks>
    Task<IReadOnlyList<ParticipantSuggestionModel>> SuggestParticipantsAsync(
        OrganizationId organizationId,
        string address,
        CancellationToken cancellationToken = default);
}

/// <param name="IsUnambiguous">
/// Whether this was the only match. False means the interface offers a choice
/// rather than a default.
/// </param>
public sealed record ParticipantSuggestionModel(
    Guid? PersonId,
    Guid? CompanyId,
    string DisplayName,
    string MatchedAddress,
    bool IsUnambiguous);
