using AgencyOS.Domain.Communications;

namespace AgencyOS.Application.Abstractions;

/// <summary>One participant on a message, as a provider reports them.</summary>
public sealed record ProviderParticipant(
    ParticipantRole Role,
    string Address,
    string? DisplayName);

/// <summary>One attachment, as a provider reports it. Metadata only; no bytes.</summary>
public sealed record ProviderAttachment(
    string ExternalAttachmentId,
    string FileName,
    string MediaType,
    long ByteLength,
    bool IsInline);

/// <summary>
/// One message, as a provider reports it.
/// </summary>
/// <param name="BodyHtml">
/// The provider's HTML, entirely untrusted. It is sanitized before storage and
/// never rendered as it arrived (ADR-0026).
/// </param>
/// <param name="IsDeleted">
/// Whether this delta entry says the message is gone at the source rather than
/// describing its content.
/// </param>
public sealed record ProviderMessage(
    string ExternalMessageId,
    MessageDirection Direction,
    string? ExternalThreadId,
    string? InternetMessageId,
    string? Subject,
    string? BodyText,
    string? BodyHtml,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    string? Folder,
    IReadOnlyList<ProviderParticipant> Participants,
    IReadOnlyList<ProviderAttachment> Attachments,
    bool IsDeleted = false);

/// <summary>
/// One page of provider changes.
/// </summary>
/// <param name="NextCursor">
/// Where to resume. Null means the provider has no more to say for now.
/// </param>
/// <param name="CursorExpired">
/// The provider rejected the cursor and wants a full resynchronization. Not an
/// error: it is the provider saying "start again", and the only correct response is
/// to forget the cursor and do so (ADR-0026).
/// </param>
public sealed record ProviderSyncPage(
    IReadOnlyList<ProviderMessage> Messages,
    string? NextCursor,
    bool HasMore,
    bool CursorExpired = false);

/// <summary>A provider draft that exists but has not been sent.</summary>
public sealed record ProviderDraft(string ProviderDraftId, string? InternetMessageId);

/// <summary>What the provider said about a send.</summary>
public enum ProviderSendOutcome
{
    /// <summary>The provider confirmed it. The message has gone.</summary>
    Accepted = 1,

    /// <summary>The provider refused, before committing to anything.</summary>
    /// <remarks>
    /// A stated refusal: a rejected recipient, a validation failure, a revoked
    /// credential. Safe to treat as "did not send", because the provider said so.
    /// </remarks>
    Rejected = 2,

    /// <summary>
    /// No answer arrived. The message may or may not have gone.
    /// </summary>
    /// <remarks>
    /// A timeout, a dropped connection, a gateway error. The provider may have
    /// accepted and failed to tell us. This never becomes "failed" by assumption
    /// (ADR-0028).
    /// </remarks>
    Unknown = 3,
}

/// <summary>The result of asking a provider to send.</summary>
public sealed record ProviderSendResult(
    ProviderSendOutcome Outcome,
    string? ProviderMessageId = null,
    string? InternetMessageId = null,
    string? Error = null,
    bool IsPermanent = false);

/// <summary>What a search of the provider's sent items established.</summary>
/// <param name="Verdict">
/// Found, proven absent, or no answer. Only the first two are evidence.
/// </param>
public sealed record ProviderReconciliation(
    DispatchReconciliationVerdict Verdict,
    string? ProviderMessageId = null,
    string? InternetMessageId = null,
    string? Detail = null);

/// <summary>
/// Where to send a person to authorize a mailbox, and what will be asked of them.
/// </summary>
/// <remarks>
/// The URL carries the application identifier and the scopes, both of which are
/// public by construction in OAuth. It carries no secret: the client secret never
/// leaves the server, and the code the user brings back is exchanged there
/// (ADR-0027).
/// </remarks>
public sealed record ProviderAuthorization(string AuthorizationUrl, string Scopes);

/// <summary>A mailbox connection, as a provider describes it after authorization.</summary>
public sealed record ProviderConnection(
    string ExternalAccountId,
    string MailboxAddress,
    string? DisplayName,
    string GrantedScopes,
    string RefreshToken,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// What AgencyOS needs from an external mail system.
/// </summary>
/// <remarks>
/// <para>
/// Six operations, chosen so the protocol AgencyOS depends on is stated here rather
/// than spread through a provider SDK. Nothing in this interface is
/// Microsoft-shaped: no Graph identifiers, no Graph paging, no Graph error model
/// (ADR-0026).
/// </para>
/// <para>
/// The three send operations exist as three because the protocol needs them
/// separate. Creating a draft is repeatable and has no external consequence;
/// sending is neither; and finding a message afterwards is the only way to learn
/// what happened when the second one did not answer (ADR-0028).
/// </para>
/// </remarks>
public interface ICommunicationProvider
{
    /// <summary>Which provider this is.</summary>
    CommunicationProviderKind Kind { get; }

    /// <summary>
    /// Describes where a person authorizes this provider, when it has such a place.
    /// </summary>
    /// <remarks>
    /// Null when the adapter is unconfigured, and null for adapters with no browser
    /// step at all. The Windows client cannot construct this itself: it would need
    /// the application registration, which is server configuration and stays there
    /// (ADR-0027).
    /// </remarks>
    ProviderAuthorization? DescribeAuthorization(string redirectUri, string state);

    /// <summary>
    /// Exchanges an authorization code for a usable connection.
    /// </summary>
    /// <remarks>
    /// The returned refresh token is plaintext and is encrypted by the caller
    /// before it touches the database. It is never returned to a client, never
    /// logged and never placed in telemetry (ADR-0027).
    /// </remarks>
    Task<ProviderConnection> CompleteConnectionAsync(
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for what has changed since a cursor.
    /// </summary>
    /// <param name="cursor">
    /// The provider's own cursor, or null to start from the beginning. Never a
    /// timestamp: a timestamp comparison misses back-dated messages and re-reads
    /// unchanged ones (ADR-0026).
    /// </param>
    Task<ProviderSyncPage> SyncAsync(
        string refreshToken,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a draft at the provider and returns its identifier.
    /// </summary>
    /// <param name="clientReference">
    /// Carried on the message so a later search can match it back to the intent
    /// with certainty, rather than by comparing subject lines.
    /// </param>
    /// <remarks>
    /// Repeatable. A draft nobody sent has no consequence anybody sees, which is
    /// exactly why the protocol creates one before committing to anything.
    /// </remarks>
    Task<ProviderDraft> CreateDraftAsync(
        string refreshToken,
        OutboundDraftRequest request,
        string clientReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the provider to send a draft.
    /// </summary>
    /// <remarks>
    /// The one irreversible operation in the milestone. Its result is
    /// three-valued on purpose: accepted, refused, or no answer at all.
    /// </remarks>
    Task<ProviderSendResult> SendDraftAsync(
        string refreshToken,
        string providerDraftId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks for a message that may or may not have been sent.
    /// </summary>
    /// <remarks>
    /// The recovery path, and the reason an unknown outcome is survivable. It
    /// answers found, proven absent, or "cannot tell" - and the third answer is
    /// reported as itself rather than rounded to one of the others (ADR-0028).
    /// </remarks>
    Task<ProviderReconciliation> ReconcileAsync(
        string refreshToken,
        string clientReference,
        string? providerDraftId,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches an attachment's bytes, for ingestion into the document store.</summary>
    Task<Stream> OpenAttachmentAsync(
        string refreshToken,
        string externalMessageId,
        string externalAttachmentId,
        CancellationToken cancellationToken = default);
}

/// <summary>One attachment on an outbound message: bytes AgencyOS already holds.</summary>
public sealed record OutboundDraftAttachment(
    string FileName,
    string MediaType,
    long ByteLength,
    Func<CancellationToken, Task<Stream>> OpenContent);

/// <summary>Everything a provider needs to build a draft.</summary>
public sealed record OutboundDraftRequest(
    string Subject,
    string BodyText,
    IReadOnlyList<ProviderParticipant> Recipients,
    IReadOnlyList<OutboundDraftAttachment> Attachments,
    string? InReplyToInternetMessageId = null);

/// <summary>Chooses the provider for an account.</summary>
/// <remarks>
/// Resolved per account rather than injected as one implementation, because a
/// tenant can hold a real Microsoft mailbox and a test mailbox at the same time and
/// each must reach its own adapter.
/// </remarks>
public interface ICommunicationProviderRegistry
{
    ICommunicationProvider Resolve(CommunicationProviderKind kind);

    bool Supports(CommunicationProviderKind kind);
}

/// <summary>
/// Pulls readable text out of stored bytes.
/// </summary>
/// <remarks>
/// <para>
/// A seam with a deliberately small implementation in M10. Plain text formats are
/// extracted natively; PDF and DOCX report <c>Unsupported</c> and say so, rather
/// than being handled by a parser this build has not fuzzed
/// (<c>docs/11_TESTING_AND_FORMAL_METHODS.md</c> makes document parsers a mandatory
/// fuzzing target, and adding two before the fuzzing exists would be the wrong
/// order).
/// </para>
/// <para>
/// No OCR, no Python, no model. Extracted text is a derived projection and never
/// the canonical content of a document (ADR-0024).
/// </para>
/// </remarks>
public interface IDocumentTextExtractor
{
    bool CanExtract(string mediaType);

    Task<DocumentTextExtraction> ExtractAsync(
        Stream content,
        string mediaType,
        CancellationToken cancellationToken = default);
}

/// <summary>What extraction produced, including having produced nothing.</summary>
public sealed record DocumentTextExtraction(
    Domain.Documents.TextExtractionState State,
    string? Text = null,
    string? Detail = null);
