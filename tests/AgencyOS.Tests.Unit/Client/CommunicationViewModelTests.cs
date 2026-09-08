using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Documents;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// What the communications surfaces say about a send.
/// </summary>
/// <remarks>
/// One rule runs through all of it: an unknown outcome is never shown, counted or
/// grouped as a failure. The two look similar and demand opposite responses - a
/// failure invites another attempt, and an unknown outcome may already have put
/// the message in somebody's inbox. Rounding one to the other is how a client
/// receives the same commercial email twice (ADR-0028).
/// </remarks>
public sealed class CommunicationViewModelTests
{
    /// <summary>Every state reads as what it is.</summary>
    [Theory]
    [InlineData("Draft", "Draft - nothing sent")]
    [InlineData("Queued", "Queued to send")]
    [InlineData("ProviderDraftCreated", "Prepared at provider")]
    [InlineData("SendRequested", "Send in progress")]
    [InlineData("Sent", "Sent - provider confirmed")]
    [InlineData("FailedRetryable", "Failed - will retry")]
    [InlineData("FailedPermanent", "Failed - stopped")]
    [InlineData("Cancelled", "Cancelled before sending")]
    public void EveryStateHasItsOwnWords(string state, string expected) =>
        Assert.Equal(expected, OutboundFormatting.State(state));

    /// <summary>
    /// An unknown outcome never reads as a failure, and says what to do instead.
    /// </summary>
    [Fact]
    public void AnUnknownOutcome_IsNeverCalledAFailure()
    {
        string words = OutboundFormatting.State("UnknownOutcome");

        Assert.Contains("unknown", words, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fail", words, StringComparison.OrdinalIgnoreCase);

        string explanation = OutboundFormatting.UnknownOutcomeExplanation;

        // It says the message may already have gone, and that AgencyOS will not
        // resend it on a guess.
        Assert.Contains("may or may not", explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("will not resend", explanation, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sent means the provider confirmed it, and never that it was delivered.
    /// </summary>
    [Fact]
    public void Sent_ClaimsConfirmationAndNotDelivery()
    {
        string words = OutboundFormatting.State("Sent");

        Assert.Contains("confirmed", words, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deliver", words, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("received", words, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("read", words, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Only an unknown outcome or a permanent failure needs a person.</summary>
    [Theory]
    [InlineData("UnknownOutcome", true)]
    [InlineData("FailedPermanent", true)]
    [InlineData("FailedRetryable", false)]
    [InlineData("Queued", false)]
    [InlineData("Sent", false)]
    [InlineData("Cancelled", false)]
    public void OnlySomeStatesNeedAPerson(string state, bool needed) =>
        Assert.Equal(needed, OutboundFormatting.NeedsAttention(state));

    /// <summary>Cancelling is offered only while it is honest.</summary>
    /// <remarks>
    /// After the provider has the message AgencyOS cannot take it back, and a
    /// button that implied otherwise would be the interface lying about what the
    /// system can do (ADR-0028).
    /// </remarks>
    [Theory]
    [InlineData("Draft", true)]
    [InlineData("Queued", true)]
    [InlineData("ProviderDraftCreated", false)]
    [InlineData("SendRequested", false)]
    [InlineData("Sent", false)]
    [InlineData("UnknownOutcome", false)]
    [InlineData("FailedPermanent", false)]
    public void CancellingIsOfferedOnlyBeforeTheProvider(string state, bool offered) =>
        Assert.Equal(offered, OutboundFormatting.CanCancel(state));

    /// <summary>An inconclusive reconciliation is reported as itself.</summary>
    [Theory]
    [InlineData("FoundSent", "Found in sent items - the message went")]
    [InlineData("ProvenAbsent", "Not sent, and the draft is still there")]
    [InlineData("Inconclusive", "Could not tell")]
    [InlineData(null, "Not reconciled")]
    public void AVerdictSaysExactlyWhatWasEstablished(string? verdict, string expected) =>
        Assert.Equal(expected, OutboundFormatting.Verdict(verdict));

    /// <summary>The outbound list counts unknown outcomes apart from failures.</summary>
    [Fact]
    public async Task TheOutboundList_CountsUnknownOutcomesApartFromFailures()
    {
        FakeAgencyOsApi api = new();

        api.OutboundMessages.Add(FakeAgencyOsApi.Dispatch("Sent"));
        api.OutboundMessages.Add(FakeAgencyOsApi.Dispatch("Sent"));
        api.OutboundMessages.Add(FakeAgencyOsApi.Dispatch("UnknownOutcome"));
        api.OutboundMessages.Add(FakeAgencyOsApi.Dispatch("FailedPermanent"));
        api.OutboundMessages.Add(FakeAgencyOsApi.Dispatch("FailedRetryable"));

        OutboundListViewModel view = new(api);

        await view.LoadAsync();

        Assert.Equal(2, view.Sent);
        Assert.Equal(1, view.UnknownOutcomes);
        Assert.Equal(1, view.Failed);
    }

    /// <summary>What needs a person sorts to the top.</summary>
    /// <remarks>
    /// A desk that has to scroll to find the message nobody knows the fate of will
    /// not find it.
    /// </remarks>
    [Fact]
    public async Task WhatNeedsAPersonSortsToTheTop()
    {
        FakeAgencyOsApi api = new();

        api.OutboundMessages.Add(FakeAgencyOsApi.Dispatch("Sent", "Ordinary"));
        api.OutboundMessages.Add(FakeAgencyOsApi.Dispatch("UnknownOutcome", "Needs looking at"));
        api.OutboundMessages.Add(FakeAgencyOsApi.Dispatch("Queued", "Waiting"));

        OutboundListViewModel view = new(api);

        await view.LoadAsync();

        Assert.Equal("Needs looking at", view.Dispatches[0].Subject);
    }

    /// <summary>Composing produces a draft and sends nothing.</summary>
    [Fact]
    public async Task Composing_ProducesADraftAndSendsNothing()
    {
        FakeAgencyOsApi api = new();

        api.CommunicationAccounts.Add(Account());

        ComposeMessageViewModel view = new(api);

        await view.LoadAccountsAsync();

        Assert.Single(view.Accounts);

        view.AccountId = view.Accounts[0].Id;
        view.Subject = "Terms for The Undertow";
        view.Body = "As discussed.";

        Assert.False(view.CanCompose);

        view.AddRecipient("To", "producer@studio.test");

        Assert.True(view.CanCompose);

        await view.ComposeAsync();

        Assert.NotNull(view.DispatchId);
        Assert.Equal("Draft", view.State);
        Assert.Contains("nothing sent", view.StateText, StringComparison.OrdinalIgnoreCase);

        // Nothing was queued, and nothing left.
        Assert.Empty(api.QueuedDispatches);

        Assert.True(view.CanQueue);
        Assert.True(view.CanCancel);
    }

    /// <summary>Queueing is a separate act, and it says what it does.</summary>
    [Fact]
    public async Task Queueing_IsASeparateActWithAWarningThatIsTrue()
    {
        FakeAgencyOsApi api = new();

        api.CommunicationAccounts.Add(Account());

        ComposeMessageViewModel view = new(api);

        await view.LoadAccountsAsync();

        view.AccountId = view.Accounts[0].Id;
        view.Subject = "Terms";
        view.AddRecipient("To", "producer@studio.test");

        await view.ComposeAsync();
        await view.QueueAsync();

        Assert.Equal("Queued", view.State);
        Assert.Single(api.QueuedDispatches);

        // Queued is past the point of composing, so composing again is not offered.
        Assert.False(view.CanQueue);

        Assert.Contains(
            "cannot recall",
            ComposeMessageViewModel.QueueWarning,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Only connected mailboxes are offered to send from.</summary>
    /// <remarks>
    /// A disconnected mailbox cannot send, and offering it would produce a refusal
    /// at the end of composing a message rather than at the start.
    /// </remarks>
    [Fact]
    public async Task OnlyConnectedMailboxes_AreOfferedToSendFrom()
    {
        FakeAgencyOsApi api = new();

        api.CommunicationAccounts.Add(Account("agent@agency.example"));
        api.CommunicationAccounts.Add(Account("old@agency.example", state: "Disconnected"));

        ComposeMessageViewModel view = new(api);

        await view.LoadAccountsAsync();

        CommunicationAccountResponse offered = Assert.Single(view.Accounts);

        Assert.Equal("agent@agency.example", offered.MailboxAddress);
    }

    /// <summary>The mailbox list counts what needs attention and what is private.</summary>
    [Fact]
    public async Task TheMailboxList_CountsWhatNeedsAttention()
    {
        FakeAgencyOsApi api = new();

        api.CommunicationAccounts.Add(Account("one@agency.example"));
        api.CommunicationAccounts.Add(Account("two@agency.example", state: "Disconnected"));
        api.CommunicationAccounts.Add(Account("three@agency.example", visibility: "Shared"));

        MailboxListViewModel view = new(api);

        await view.LoadAsync();

        Assert.Equal(3, view.Accounts.Count);
        Assert.Equal(1, view.NeedingAttention);
        Assert.Equal(2, view.Private);
    }

    /// <summary>The message list says what its search covers.</summary>
    [Fact]
    public void TheMessageSearchScope_IsStated() =>
        Assert.Contains(
            "Message bodies are not searched",
            MessageListViewModel.SearchScopeNotice,
            StringComparison.Ordinal);

    /// <summary>The message filters reach the server.</summary>
    [Fact]
    public async Task TheMessageFilters_AreSentToTheServer()
    {
        FakeAgencyOsApi api = new();

        Guid accountId = Guid.NewGuid();

        MessageListViewModel view = new(api)
        {
            AccountId = accountId,
            Direction = "Inbound",
            UnlinkedOnly = true,
            WithAttachmentsOnly = true,
            Search = "  offer  ",
        };

        await view.LoadAsync();

        Assert.Equal(accountId, api.LastMessageFilter.AccountId);
        Assert.Equal("Inbound", api.LastMessageFilter.Direction);
        Assert.True(api.LastMessageFilter.UnlinkedOnly);
        Assert.True(api.LastMessageFilter.HasAttachments);
        Assert.Equal("offer", api.LastMessageFilter.Search);
    }

    /// <summary>The message list counts what is still unfiled.</summary>
    [Fact]
    public async Task TheMessageList_CountsWhatIsUnfiled()
    {
        FakeAgencyOsApi api = new();

        api.Messages.Add(FakeAgencyOsApi.Message("Filed", linkCount: 1));
        api.Messages.Add(FakeAgencyOsApi.Message("Unfiled"));
        api.Messages.Add(FakeAgencyOsApi.Message("With a file", attachmentCount: 2));

        MessageListViewModel view = new(api);

        await view.LoadAsync();

        Assert.Equal(2, view.Unlinked);
        Assert.Equal(1, view.WithAttachments);
    }

    /// <summary>The desk says whether anything needs a person.</summary>
    [Fact]
    public async Task TheDesk_SaysWhetherAnythingNeedsAPerson()
    {
        FakeAgencyOsApi api = new();

        MailboxListViewModel _ = new(api);

        CommunicationCommandCenterViewModel quiet = new(api);

        await quiet.LoadAsync();

        Assert.False(quiet.NeedsAttention);
        Assert.True(quiet.IsEmpty);

        api.CommunicationCommandCenter = new CommunicationCommandCenterResponse(
            [FakeAgencyOsApi.Dispatch("UnknownOutcome")],
            [],
            [],
            1,
            0,
            0);

        CommunicationCommandCenterViewModel busy = new(api);

        await busy.LoadAsync();

        Assert.True(busy.NeedsAttention);
        Assert.Equal(1, busy.UnknownOutcomeCount);
        Assert.Equal(0, busy.FailedSendCount);
        Assert.False(busy.IsEmpty);
    }

    /// <summary>The message detail says how the message was treated on the way in.</summary>
    [Fact]
    public void TheSanitizationNotice_SaysWhatWasRemoved() =>
        Assert.Contains(
            "Remote images",
            MessageDetailViewModel.SanitizationNotice,
            StringComparison.OrdinalIgnoreCase);

    private static CommunicationAccountResponse Account(
        string address = "agent@agency.example",
        string state = "Connected",
        string visibility = "Private") =>
        new(
            Guid.NewGuid(),
            "MicrosoftGraph",
            address,
            "Agent",
            Guid.NewGuid(),
            "Operator",
            state,
            visibility,
            "Mail.Read Mail.Send offline_access",
            DateTimeOffset.UtcNow,
            null,
            state == "Connected",
            null,
            0,
            DateTimeOffset.UtcNow,
            1);
}
