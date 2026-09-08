using System.Collections.Concurrent;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Communications;
using AgencyOS.Domain.Communications;

namespace AgencyOS.Infrastructure.Communications;

/// <summary>
/// How the fake provider should behave on the next call.
/// </summary>
/// <remarks>
/// The reason this provider exists. CI has no Microsoft tenant, so the failures
/// that actually matter — a send whose acknowledgement never arrives, a provider
/// that accepted a message and cannot say so, a cursor the provider has forgotten —
/// cannot be produced against a real mailbox on demand. Here they are a field
/// (ADR-0026, ADR-0028).
/// </remarks>
public enum FakeProviderBehaviour
{
    /// <summary>Everything works.</summary>
    Normal = 0,

    /// <summary>The send is accepted, and the acknowledgement is lost.</summary>
    /// <remarks>
    /// The case the whole protocol is built around: the message <em>has gone</em>,
    /// and AgencyOS cannot prove it. A system that retried here would send it twice.
    /// </remarks>
    LoseAcknowledgementAfterSending = 1,

    /// <summary>The provider never answers, and nothing was sent.</summary>
    /// <remarks>
    /// Indistinguishable from the case above at the moment it happens, which is
    /// exactly why the outcome is unknown rather than failed.
    /// </remarks>
    TimeOutWithoutSending = 2,

    /// <summary>The provider states a refusal, before committing.</summary>
    RejectSend = 3,

    /// <summary>The provider refuses the credential.</summary>
    RefuseCredential = 4,

    /// <summary>The provider forgets the delta cursor and asks for a full resync.</summary>
    ExpireCursor = 5,

    /// <summary>Reconciliation cannot answer either way.</summary>
    ReconcileInconclusive = 6,
}

/// <summary>The mailbox a fake connection stands for.</summary>
public sealed class FakeMailbox
{
    public string Address { get; init; } = "agent@example.test";

    public string DisplayName { get; init; } = "Test Mailbox";

    /// <summary>Messages the provider will hand back, in order.</summary>
    public List<ProviderMessage> Inbox { get; } = [];

    /// <summary>Drafts created but not yet sent, by provider draft id.</summary>
    public ConcurrentDictionary<string, ProviderDraft> Drafts { get; } = [];

    /// <summary>
    /// Messages the provider has actually sent, by the correlation value.
    /// </summary>
    /// <remarks>
    /// The provider's own record of what went out. Reconciliation reads it, which
    /// is what makes "did this actually send" answerable at all (ADR-0028).
    /// </remarks>
    public ConcurrentDictionary<string, ProviderMessage> Sent { get; } = [];

    /// <summary>How the next call behaves. Reset to normal after it is consumed.</summary>
    public FakeProviderBehaviour Next { get; set; } = FakeProviderBehaviour.Normal;

    /// <summary>How many times a send was actually committed, for duplicate checks.</summary>
    public int SendCommitCount;

    public FakeProviderBehaviour Take()
    {
        FakeProviderBehaviour behaviour = Next;
        Next = FakeProviderBehaviour.Normal;

        return behaviour;
    }
}

/// <summary>
/// A deterministic in-process mail provider.
/// </summary>
/// <remarks>
/// <para>
/// Not a stub that returns success. It implements the same protocol the Graph
/// adapter does, including the parts that only matter when things go wrong: a
/// draft that must exist before a send, a correlation value carried onto the sent
/// message, and a sent-items record that reconciliation can query afterwards.
/// </para>
/// <para>
/// That is what makes it worth trusting in CI. The properties AgencyOS depends on —
/// one intent is never sent twice, an unknown outcome converges to evidence — are
/// exercised here against a provider that can lose an acknowledgement on demand,
/// which no real mailbox will do when asked (ADR-0028).
/// </para>
/// <para>
/// It is <strong>not</strong> evidence about Microsoft Graph. Real-provider
/// validation is reported separately and is not claimed by these tests passing.
/// </para>
/// </remarks>
public sealed class FakeCommunicationProvider : ICommunicationProvider
{
    private readonly ConcurrentDictionary<string, FakeMailbox> _mailboxes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public CommunicationProviderKind Kind => CommunicationProviderKind.Fake;

    /// <summary>The mailbox a refresh token stands for, created on first use.</summary>
    public FakeMailbox Mailbox(string refreshToken) =>
        _mailboxes.GetOrAdd(refreshToken, _ => new FakeMailbox());

    /// <inheritdoc />
    /// <remarks>
    /// There is no consent page. The fake exists so tests can exercise the
    /// protocol without a Microsoft tenant, and pretending it had a browser step
    /// would put a dead link in front of an operator.
    /// </remarks>
    public ProviderAuthorization? DescribeAuthorization(string redirectUri, string state) => null;

    /// <inheritdoc />
    public Task<ProviderConnection> CompleteConnectionAsync(
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        // The code becomes the refresh token, so a test can address the same mailbox
        // again without threading an identifier through every call.
        string refreshToken = $"fake-refresh-{authorizationCode}";
        FakeMailbox mailbox = Mailbox(refreshToken);

        return Task.FromResult(new ProviderConnection(
            ExternalAccountId: $"fake-account-{authorizationCode}",
            MailboxAddress: mailbox.Address,
            DisplayName: mailbox.DisplayName,
            GrantedScopes: "Mail.Read Mail.Send offline_access",
            RefreshToken: refreshToken,
            ExpiresAt: null));
    }

    /// <inheritdoc />
    public Task<ProviderSyncPage> SyncAsync(
        string refreshToken,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        FakeMailbox mailbox = Mailbox(refreshToken);

        switch (mailbox.Take())
        {
            case FakeProviderBehaviour.RefuseCredential:
                throw new ProviderAuthorizationException();

            case FakeProviderBehaviour.ExpireCursor:
                // The provider has forgotten the cursor. Not an error: it is asking
                // for a full resynchronization, and idempotent writes make that
                // harmless (ADR-0026).
                return Task.FromResult(new ProviderSyncPage([], null, false, CursorExpired: true));

            default:
                break;
        }

        int offset = cursor is null
            ? 0
            : int.TryParse(cursor, out int parsed) ? parsed : 0;

        ProviderMessage[] page = [.. mailbox.Inbox.Skip(offset).Take(pageSize)];
        int next = offset + page.Length;

        return Task.FromResult(new ProviderSyncPage(
            page,
            next.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HasMore: next < mailbox.Inbox.Count));
    }

    /// <inheritdoc />
    public async Task<ProviderDraft> CreateDraftAsync(
        string refreshToken,
        OutboundDraftRequest request,
        string clientReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        FakeMailbox mailbox = Mailbox(refreshToken);

        if (mailbox.Take() == FakeProviderBehaviour.RefuseCredential)
        {
            throw new ProviderAuthorizationException();
        }

        // Attachment content is opened here, as the real adapter does, so a test
        // that attaches a document actually exercises reading it back out of the
        // blob store.
        foreach (OutboundDraftAttachment attachment in request.Attachments)
        {
            await using Stream content = await attachment.OpenContent(cancellationToken)
                .ConfigureAwait(false);

            byte[] discard = new byte[4096];

            while (await content.ReadAsync(discard, cancellationToken).ConfigureAwait(false) > 0)
            {
                // Read to completion, so a broken or missing blob fails here rather
                // than silently sending an empty attachment.
            }
        }

        // Keyed by the correlation value, so asking twice for the same intent
        // produces the same draft rather than a second one.
        string draftId = $"draft-{clientReference}";

        ProviderDraft draft = new(draftId, $"<{clientReference}@agencyos.test>");

        mailbox.Drafts[draftId] = draft;

        return draft;
    }

    /// <inheritdoc />
    public Task<ProviderSendResult> SendDraftAsync(
        string refreshToken,
        string providerDraftId,
        CancellationToken cancellationToken = default)
    {
        FakeMailbox mailbox = Mailbox(refreshToken);

        if (!mailbox.Drafts.TryGetValue(providerDraftId, out ProviderDraft? draft))
        {
            return Task.FromResult(new ProviderSendResult(
                ProviderSendOutcome.Rejected, Error: "No such draft.", IsPermanent: true));
        }

        string clientReference = providerDraftId["draft-".Length..];

        switch (mailbox.Take())
        {
            case FakeProviderBehaviour.RejectSend:
                return Task.FromResult(new ProviderSendResult(
                    ProviderSendOutcome.Rejected, Error: "The provider refused the message."));

            case FakeProviderBehaviour.TimeOutWithoutSending:
                // Nothing was committed, and AgencyOS has no way to know that.
                return Task.FromResult(new ProviderSendResult(
                    ProviderSendOutcome.Unknown, Error: "No answer from the provider."));

            case FakeProviderBehaviour.LoseAcknowledgementAfterSending:
                // The message goes. The acknowledgement does not. This is the case
                // that makes retrying unsafe (ADR-0028).
                Commit(mailbox, draft, clientReference);

                return Task.FromResult(new ProviderSendResult(
                    ProviderSendOutcome.Unknown, Error: "The connection dropped after sending."));

            default:
                Commit(mailbox, draft, clientReference);

                return Task.FromResult(new ProviderSendResult(
                    ProviderSendOutcome.Accepted,
                    ProviderMessageId: $"sent-{clientReference}",
                    InternetMessageId: draft.InternetMessageId));
        }
    }

    /// <inheritdoc />
    public Task<ProviderReconciliation> ReconcileAsync(
        string refreshToken,
        string clientReference,
        string? providerDraftId,
        CancellationToken cancellationToken = default)
    {
        FakeMailbox mailbox = Mailbox(refreshToken);

        if (mailbox.Take() == FakeProviderBehaviour.ReconcileInconclusive)
        {
            return Task.FromResult(new ProviderReconciliation(
                DispatchReconciliationVerdict.Inconclusive,
                Detail: "The provider could not answer."));
        }

        if (mailbox.Sent.TryGetValue(clientReference, out ProviderMessage? sent))
        {
            return Task.FromResult(new ProviderReconciliation(
                DispatchReconciliationVerdict.FoundSent,
                sent.ExternalMessageId,
                sent.InternetMessageId,
                "Found in the sent items."));
        }

        // Absence is only evidence when the draft is still there. A missing draft
        // and a missing sent message together prove nothing, and returning
        // "absent" would authorize a resend on no evidence at all (ADR-0028).
        bool draftIntact = providerDraftId is not null && mailbox.Drafts.ContainsKey(providerDraftId);

        return Task.FromResult(draftIntact
            ? new ProviderReconciliation(
                DispatchReconciliationVerdict.ProvenAbsent,
                Detail: "Not in the sent items, and the draft is still there.")
            : new ProviderReconciliation(
                DispatchReconciliationVerdict.Inconclusive,
                Detail: "Neither a sent message nor the draft could be found."));
    }

    /// <inheritdoc />
    public Task<Stream> OpenAttachmentAsync(
        string refreshToken,
        string externalMessageId,
        string externalAttachmentId,
        CancellationToken cancellationToken = default)
    {
        FakeMailbox mailbox = Mailbox(refreshToken);

        ProviderMessage? message = mailbox.Inbox
            .FirstOrDefault(x => x.ExternalMessageId == externalMessageId);

        ProviderAttachment? attachment = message?.Attachments
            .FirstOrDefault(x => x.ExternalAttachmentId == externalAttachmentId);

        if (attachment is null)
        {
            throw new ProviderTransientException("No such attachment.");
        }

        // Deterministic bytes, so a test can assert the digest of what was ingested.
        byte[] content = System.Text.Encoding.UTF8.GetBytes(
            $"fake-attachment:{externalMessageId}:{externalAttachmentId}");

        return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
    }

    /// <summary>Records that the provider actually sent a message.</summary>
    private static void Commit(FakeMailbox mailbox, ProviderDraft draft, string clientReference)
    {
        // The counter is what a test asserts against to prove one intent never
        // produced two external messages.
        Interlocked.Increment(ref mailbox.SendCommitCount);

        mailbox.Drafts.TryRemove(draft.ProviderDraftId, out _);

        mailbox.Sent[clientReference] = new ProviderMessage(
            ExternalMessageId: $"sent-{clientReference}",
            Direction: MessageDirection.Outbound,
            ExternalThreadId: null,
            InternetMessageId: draft.InternetMessageId,
            Subject: null,
            BodyText: null,
            BodyHtml: null,
            SentAt: DateTimeOffset.UtcNow,
            ReceivedAt: null,
            Folder: "SentItems",
            Participants: [],
            Attachments: []);
    }
}

/// <summary>Resolves the adapter for a provider.</summary>
/// <remarks>
/// Resolved per account rather than injected as one implementation, because a
/// tenant can hold a real Microsoft mailbox and a test mailbox at once and each
/// must reach its own adapter (ADR-0026).
/// </remarks>
public sealed class CommunicationProviderRegistry : ICommunicationProviderRegistry
{
    private readonly IReadOnlyDictionary<CommunicationProviderKind, ICommunicationProvider> _providers;

    public CommunicationProviderRegistry(IEnumerable<ICommunicationProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToDictionary(x => x.Kind);
    }

    /// <inheritdoc />
    public ICommunicationProvider Resolve(CommunicationProviderKind kind) =>
        _providers.TryGetValue(kind, out ICommunicationProvider? provider)
            ? provider
            : throw new Domain.Common.DomainException(
                $"No adapter for {kind} is configured on this server.");

    /// <inheritdoc />
    public bool Supports(CommunicationProviderKind kind) => _providers.ContainsKey(kind);
}
