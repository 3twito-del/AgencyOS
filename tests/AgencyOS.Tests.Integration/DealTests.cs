using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Search;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The M7 negotiation workflow, end to end against PostgreSQL.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class DealTests
{
    private readonly AgencyOsTestFixture _fixture;

    public DealTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The whole negotiation, in the order an agency performs it.
    /// </summary>
    /// <remarks>
    /// One test on purpose: the value of M7 is that these steps connect. Separate
    /// tests would each pass while the joins between them stayed broken.
    /// </remarks>
    [Fact]
    public async Task Workflow_FromTargetToTermsAgreed()
    {
        Fixture f = await SetUpAsync("m7-workflow");

        // A negotiation opens from a target that has reached commercial discussion.
        DealDetailResponse deal = await OpenDealAsync(f);

        Assert.Equal("Draft", deal.Deal.Status);
        Assert.Equal(f.OpportunityId, deal.Deal.OpportunityId);
        Assert.Equal(f.TargetId, deal.Deal.OpportunityTargetId);
        Assert.Equal("Northgate Pictures", deal.Deal.CounterpartyDisplayName);
        Assert.Equal("The Undertow", deal.Deal.SubjectDisplayName);

        // They open at 500,000 with second billing.
        RecordOfferResponse inbound = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound",
                [
                    Money("GuaranteedCompensation", 500_000m),
                    Text("CreditBilling", "Second position"),
                ],
                deal.Deal.Version,
                Summary: "Opening number"));

        deal = await GetAsync<DealDetailResponse>(f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        // Recording an offer is what moves the deal into negotiation. No status
        // command can do it.
        Assert.Equal("Negotiating", deal.Deal.Status);
        Assert.True(deal.Deal.HasOpenOffer);
        Assert.Equal(1, deal.Deal.OfferCount);
        Assert.Equal(inbound.OfferId, deal.OpenOffer!.Id);
        Assert.Null(deal.AcceptedOffer);

        // We counter at 650,000 and first billing. The counter is a new offer.
        RecordOfferResponse counter = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Outbound",
                [
                    Money("GuaranteedCompensation", 650_000m),
                    Text("CreditBilling", "First position"),
                    Percentage("BackendPercentage", 3m),
                ],
                deal.Deal.Version,
                RespondsToOfferId: inbound.OfferId,
                Summary: "Counter"));

        Assert.Equal(inbound.OfferId, counter.SupersededOfferId);

        // The earlier offer says exactly what it always said.
        OfferResponse original = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{inbound.OfferId}");

        Assert.Equal("Superseded", original.Status);
        Assert.Equal(
            500_000m,
            original.Terms.Single(x => x.Code == "GuaranteedCompensation").Amount);

        // The comparison says what moved, and in which direction.
        OfferComparisonResponse comparison = await GetAsync<OfferComparisonResponse>(
            f.Client,
            $"{f.Root}/deals/{deal.Deal.Id}/comparison"
                + $"?previousOfferId={inbound.OfferId}&currentOfferId={counter.OfferId}");

        TermDifferenceResponse compensation =
            comparison.Differences.Single(x => x.Code == "GuaranteedCompensation");

        Assert.Equal("Changed", compensation.Change);
        Assert.Equal("Increased", compensation.Direction);
        Assert.Equal(500_000m, compensation.Previous!.Amount);
        Assert.Equal(650_000m, compensation.Current!.Amount);

        Assert.Equal("Added", comparison.Differences.Single(x => x.Code == "BackendPercentage").Change);

        // They accept.
        OfferResponse standing = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{counter.OfferId}");

        AnswerOfferResponse accepted = await AnswerOfferAsync(
            f, counter.OfferId, new AnswerOfferRequest("Accept", standing.Version));

        Assert.Equal("TermsAgreed", accepted.DealStatus);

        deal = await GetAsync<DealDetailResponse>(f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        Assert.Equal("TermsAgreed", deal.Deal.Status);
        Assert.Equal(counter.OfferId, deal.Deal.AcceptedOfferId);
        Assert.Equal(counter.OfferId, deal.AcceptedOffer!.Id);
        Assert.False(deal.Deal.HasOpenOffer);

        // The timeline is composed from domain events, not audit rows.
        DealHistoryEntryResponse[] history = await GetAsync<DealHistoryEntryResponse[]>(
            f.Client, $"{f.Root}/deals/{deal.Deal.Id}/history");

        Assert.Contains(history, x => x.Summary == "Negotiation opened");
        Assert.Contains(history, x => x.Summary == "Commercial terms agreed");
        Assert.Contains(history, x => x.Summary.Contains("accepted", StringComparison.Ordinal));

        // And the M6 target is untouched: M7 owns the negotiation, M6 owns the
        // pipeline, and neither writes the other's state.
        OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
            f.Client, $"{f.Root}/opportunity-targets/{f.TargetId}");

        Assert.Equal("Interested", target.Stage);
    }

    // ------------------------------------------------------------- anchoring

    [Fact]
    public async Task ADealRequiresATargetThatBelongsToItsOpportunity()
    {
        Fixture f = await SetUpAsync("m7-anchor");

        // A second pursuit, which this target has nothing to do with.
        OpportunityDetailResponse other = await CreatedAsync<OpportunityDetailResponse>(
            f.Client.PostAsJsonAsync(
                $"{f.Root}/opportunities",
                new CreateOpportunityRequest(
                    "Salt Road to market", "ProjectMarket", f.Actor.User.Id.Value)));

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/deals",
            new CreateDealRequest(
                other.Opportunity.Id,
                f.TargetId,
                "Mismatched",
                "ProjectSale",
                f.Actor.User.Id.Value));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Opening a negotiation is an explicit act. A target nobody has spoken to
    /// cannot anchor one.
    /// </summary>
    [Theory]
    [InlineData("Identified", false)]
    [InlineData("Approved", false)]
    [InlineData("Contacted", false)]
    [InlineData("Interested", true)]
    public async Task ANegotiationOpensOnlyOnceATargetIsFarEnoughAlong(string stage, bool allowed)
    {
        // The target is walked only as far as the stage under test; walking it
        // further and back is not possible, and would not be the situation anyway.
        Fixture f = await SetUpAsync($"m7-stage-{stage.ToLowerInvariant()}", stage);

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/deals",
            new CreateDealRequest(
                f.OpportunityId, f.TargetId, "The Undertow - Northgate", "ProjectSale",
                f.Actor.User.Id.Value));

        Assert.Equal(
            allowed ? HttpStatusCode.Created : HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task ADeal_CannotReachIntoAnotherTenant()
    {
        Fixture mine = await SetUpAsync("m7-tenant-a");
        Fixture theirs = await SetUpAsync("m7-tenant-b");

        using HttpResponseMessage response = await mine.Client.PostAsJsonAsync(
            $"{mine.Root}/deals",
            new CreateDealRequest(
                theirs.OpportunityId,
                theirs.TargetId,
                "Somebody else's negotiation",
                "ProjectSale",
                mine.Actor.User.Id.Value));

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest,
            $"Expected a refusal, got {response.StatusCode}.");
    }

    /// <summary>
    /// One live negotiation per target per kind. A genuinely different transaction
    /// with the same buyer is a different kind and is allowed.
    /// </summary>
    [Fact]
    public async Task OneLiveDealPerTargetAndKind()
    {
        Fixture f = await SetUpAsync("m7-duplicate");

        await OpenDealAsync(f);

        using HttpResponseMessage duplicate = await f.Client.PostAsJsonAsync(
            $"{f.Root}/deals",
            new CreateDealRequest(
                f.OpportunityId, f.TargetId, "Again", "ProjectSale", f.Actor.User.Id.Value));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        // A different kind of transaction with the same buyer is a different deal.
        using HttpResponseMessage differentKind = await f.Client.PostAsJsonAsync(
            $"{f.Root}/deals",
            new CreateDealRequest(
                f.OpportunityId, f.TargetId, "Producing", "Producing", f.Actor.User.Id.Value));

        Assert.Equal(HttpStatusCode.Created, differentKind.StatusCode);
    }

    // ---------------------------------------------------------- immutability

    /// <summary>
    /// A recorded offer's commercial snapshot cannot be rewritten through any
    /// normal application path.
    /// </summary>
    [Fact]
    public async Task ARecordedOffersTermsCannotBeRewritten()
    {
        Fixture f = await SetUpAsync("m7-immutable");

        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse recorded = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound", [Money("GuaranteedCompensation", 500_000m)], deal.Deal.Version));

        OfferResponse offer = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{recorded.OfferId}");

        // Changing a term.
        using HttpResponseMessage term = await f.Client.PostAsJsonAsync(
            $"{f.Root}/offers/{recorded.OfferId}/terms",
            new ChangeOfferTermRequest(
                "GuaranteedCompensation",
                offer.Version,
                new TermValueRequest("Money", Amount: 1m, Currency: "USD")));

        Assert.Equal(HttpStatusCode.BadRequest, term.StatusCode);

        // Removing one.
        using HttpResponseMessage removal = await f.Client.PostAsJsonAsync(
            $"{f.Root}/offers/{recorded.OfferId}/terms",
            new ChangeOfferTermRequest("GuaranteedCompensation", offer.Version));

        Assert.Equal(HttpStatusCode.BadRequest, removal.StatusCode);

        // Editing the offer's own commercial record.
        using HttpResponseMessage edit = await f.Client.PutAsJsonAsync(
            $"{f.Root}/offers/{recorded.OfferId}",
            new UpdateDraftOfferRequest(offer.Version, Summary: "Rewritten"));

        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);

        // And it still says what it said.
        OfferResponse unchanged = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{recorded.OfferId}");

        Assert.Equal(500_000m, unchanged.Terms.Single().Amount);
    }

    /// <summary>A draft is a working document, and its terms are editable.</summary>
    [Fact]
    public async Task ADraftOffersTermsCanBeEditedUntilItIsRecorded()
    {
        Fixture f = await SetUpAsync("m7-draft");

        DealDetailResponse deal = await OpenDealAsync(f);

        Guid draftId = await CreatedIdAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/deals/{deal.Deal.Id}/draft-offers",
            new DraftOfferRequest("Outbound", deal.Deal.Version)));

        OfferResponse draft = await GetAsync<OfferResponse>(f.Client, $"{f.Root}/offers/{draftId}");

        Assert.Equal("Draft", draft.Status);

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/offers/{draftId}/terms",
            new ChangeOfferTermRequest(
                "Fee", draft.Version, new TermValueRequest("Money", Amount: 100_000m, Currency: "USD"))));

        draft = await GetAsync<OfferResponse>(f.Client, $"{f.Root}/offers/{draftId}");

        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/offers/{draftId}/terms",
            new ChangeOfferTermRequest(
                "Fee", draft.Version, new TermValueRequest("Money", Amount: 140_000m, Currency: "USD"))));

        draft = await GetAsync<OfferResponse>(f.Client, $"{f.Root}/offers/{draftId}");

        Assert.Equal(140_000m, draft.Terms.Single().Amount);

        // Recording it freezes it.
        using HttpResponseMessage opened = await f.Client.PostAsJsonAsync(
            $"{f.Root}/offers/{draftId}/record", new OpenOfferRequest(draft.Version));

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        OfferResponse frozen = await GetAsync<OfferResponse>(f.Client, $"{f.Root}/offers/{draftId}");

        using HttpResponseMessage refused = await f.Client.PostAsJsonAsync(
            $"{f.Root}/offers/{draftId}/terms",
            new ChangeOfferTermRequest(
                "Fee", frozen.Version, new TermValueRequest("Money", Amount: 1m, Currency: "USD")));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // ------------------------------------------------------------ acceptance

    /// <summary>Exactly one accepted offer per negotiation, ever.</summary>
    [Fact]
    public async Task OnlyOneOfferCanBeTheAgreement()
    {
        Fixture f = await SetUpAsync("m7-one-accepted");

        (DealDetailResponse deal, Guid first, Guid second) = await ThreadAsync(f);

        OfferResponse standing = await GetAsync<OfferResponse>(f.Client, $"{f.Root}/offers/{second}");

        await AnswerOfferAsync(f, second, new AnswerOfferRequest("Accept", standing.Version));

        // The superseded offer cannot also become the agreement.
        OfferResponse superseded = await GetAsync<OfferResponse>(f.Client, $"{f.Root}/offers/{first}");

        using HttpResponseMessage refused = await f.Client.PostAsJsonAsync(
            $"{f.Root}/offers/{first}/answer",
            new AnswerOfferRequest("Accept", superseded.Version));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        DealDetailResponse settled = await GetAsync<DealDetailResponse>(
            f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        Assert.Equal(second, settled.Deal.AcceptedOfferId);
        Assert.Single(settled.Offers, x => x.Status == "Accepted");
    }

    /// <summary>
    /// A settled deal refuses further offers until somebody explicitly reopens it.
    /// </summary>
    [Fact]
    public async Task AgreedTermsRefuseFurtherOffersUntilReopened()
    {
        Fixture f = await SetUpAsync("m7-reopen");

        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse recorded = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound", [Money("GuaranteedCompensation", 500_000m)], deal.Deal.Version));

        OfferResponse offer = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{recorded.OfferId}");

        await AnswerOfferAsync(f, recorded.OfferId, new AnswerOfferRequest("Accept", offer.Version));

        deal = await GetAsync<DealDetailResponse>(f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        using HttpResponseMessage refused = await f.Client.PostAsJsonAsync(
            $"{f.Root}/deals/{deal.Deal.Id}/offers",
            new RecordOfferRequest(
                "Outbound", [Money("GuaranteedCompensation", 700_000m)], deal.Deal.Version));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // Reopening supersedes the agreement without touching what was agreed.
        await NoContentAsync(f.Client.PostAsJsonAsync(
            $"{f.Root}/deals/{deal.Deal.Id}/reopen",
            new ReopenNegotiationRequest(deal.Deal.Version, "They came back on the backend.")));

        DealDetailResponse reopened = await GetAsync<DealDetailResponse>(
            f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        Assert.Equal("Negotiating", reopened.Deal.Status);
        Assert.Null(reopened.Deal.AcceptedOfferId);

        OfferResponse unwound = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{recorded.OfferId}");

        Assert.Equal("Superseded", unwound.Status);

        // What was agreed on the day survives exactly.
        Assert.Equal(500_000m, unwound.Terms.Single().Amount);

        // And a new offer is accepted again.
        using HttpResponseMessage allowed = await f.Client.PostAsJsonAsync(
            $"{f.Root}/deals/{deal.Deal.Id}/offers",
            new RecordOfferRequest(
                "Outbound", [Money("GuaranteedCompensation", 700_000m)], reopened.Deal.Version));

        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    /// <summary>
    /// Nothing expires because time passed. An expiry has to have been stated and
    /// to have arrived.
    /// </summary>
    [Fact]
    public async Task ExpiryIsRecordedRatherThanInferred()
    {
        Fixture f = await SetUpAsync("m7-expiry");

        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse noExpiry = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest("Inbound", [Money("Fee", 100_000m)], deal.Deal.Version));

        OfferResponse offer = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{noExpiry.OfferId}");

        using HttpResponseMessage refused = await f.Client.PostAsJsonAsync(
            $"{f.Root}/offers/{noExpiry.OfferId}/answer",
            new AnswerOfferRequest("Expire", offer.Version));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // ----------------------------------------------------------- concurrency

    /// <summary>
    /// Two agents recording competing counters. Exactly one lands, because a
    /// negotiation holds one offer on the table at a time.
    /// </summary>
    [Fact]
    public async Task ConcurrentCounters_LeaveOneOfferOnTheTable()
    {
        Fixture f = await SetUpAsync("m7-counter-race");

        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse inbound = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound", [Money("GuaranteedCompensation", 500_000m)], deal.Deal.Version));

        deal = await GetAsync<DealDetailResponse>(f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        List<Task<HttpResponseMessage>> attempts = [];
        List<HttpClient> clients = [];

        for (int index = 0; index < 8; index++)
        {
            HttpClient client = _fixture.CreateClient(f.Actor.Subject);
            clients.Add(client);

            attempts.Add(client.PostAsJsonAsync(
                $"{f.Root}/deals/{deal.Deal.Id}/offers",
                new RecordOfferRequest(
                    "Outbound",
                    [Money("GuaranteedCompensation", 600_000m + (index * 1_000m))],
                    deal.Deal.Version,
                    RespondsToOfferId: inbound.OfferId)));
        }

        HttpResponseMessage[] responses = await Task.WhenAll(attempts);

        int created = responses.Count(x => x.StatusCode == HttpStatusCode.Created);

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }

        foreach (HttpClient client in clients)
        {
            client.Dispose();
        }

        Assert.Equal(1, created);

        OfferResponse[] thread = await GetAsync<OfferResponse[]>(
            f.Client, $"{f.Root}/offers?dealId={deal.Deal.Id}");

        Assert.Single(thread, x => x.Status == "Open");
    }

    /// <summary>
    /// One agent accepts while another counters. One of them wins and the
    /// negotiation stays coherent either way.
    /// </summary>
    [Fact]
    public async Task AcceptingWhileCountering_LeavesACoherentNegotiation()
    {
        Fixture f = await SetUpAsync("m7-accept-race");

        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse inbound = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound", [Money("GuaranteedCompensation", 500_000m)], deal.Deal.Version));

        deal = await GetAsync<DealDetailResponse>(f.Client, $"{f.Root}/deals/{deal.Deal.Id}");
        OfferResponse offer = deal.OpenOffer!;

        using HttpClient accepter = _fixture.CreateClient(f.Actor.Subject);
        using HttpClient counterer = _fixture.CreateClient(f.Actor.Subject);

        Task<HttpResponseMessage> accepting = accepter.PostAsJsonAsync(
            $"{f.Root}/offers/{offer.Id}/answer", new AnswerOfferRequest("Accept", offer.Version));

        Task<HttpResponseMessage> countering = counterer.PostAsJsonAsync(
            $"{f.Root}/deals/{deal.Deal.Id}/offers",
            new RecordOfferRequest(
                "Outbound",
                [Money("GuaranteedCompensation", 650_000m)],
                deal.Deal.Version,
                RespondsToOfferId: inbound.OfferId));

        HttpResponseMessage[] responses = await Task.WhenAll(accepting, countering);

        bool accepted = responses[0].IsSuccessStatusCode;
        bool countered = responses[1].IsSuccessStatusCode;

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }

        // Both cannot happen: accepting an offer and countering it are mutually
        // exclusive readings of the same moment.
        Assert.False(accepted && countered, "An offer was both accepted and countered.");

        DealDetailResponse settled = await GetAsync<DealDetailResponse>(
            f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        // The invariant holds whichever won.
        Assert.Equal(
            settled.Deal.Status == "TermsAgreed",
            settled.Deal.AcceptedOfferId is not null);

        Assert.True(settled.Offers.Count(x => x.Status == "Accepted") <= 1);
        Assert.True(settled.Offers.Count(x => x.Status == "Open") <= 1);
    }

    /// <summary>Two people creating a deal from one target: exactly one wins.</summary>
    [Fact]
    public async Task ConcurrentDealCreation_ProducesOneNegotiation()
    {
        Fixture f = await SetUpAsync("m7-create-race");

        List<Task<HttpResponseMessage>> attempts = [];
        List<HttpClient> clients = [];

        for (int index = 0; index < 8; index++)
        {
            HttpClient client = _fixture.CreateClient(f.Actor.Subject);
            clients.Add(client);

            attempts.Add(client.PostAsJsonAsync(
                $"{f.Root}/deals",
                new CreateDealRequest(
                    f.OpportunityId,
                    f.TargetId,
                    "The Undertow - Northgate",
                    "ProjectSale",
                    f.Actor.User.Id.Value)));
        }

        HttpResponseMessage[] responses = await Task.WhenAll(attempts);

        int created = responses.Count(x => x.StatusCode == HttpStatusCode.Created);

        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }

        foreach (HttpClient client in clients)
        {
            client.Dispose();
        }

        Assert.Equal(1, created);

        DealSummaryResponse[] deals = await GetAsync<DealSummaryResponse[]>(
            f.Client, $"{f.Root}/deals?status=Draft");

        Assert.Single(deals);
    }

    [Fact]
    public async Task AStaleAnswerIsRefused()
    {
        Fixture f = await SetUpAsync("m7-stale");

        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse recorded = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest("Inbound", [Money("Fee", 100_000m)], deal.Deal.Version));

        OfferResponse offer = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{recorded.OfferId}");

        using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            $"{f.Root}/offers/{recorded.OfferId}/answer",
            new AnswerOfferRequest("Reject", offer.Version - 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ----------------------------------------------------------- idempotency

    /// <summary>
    /// A retry after a lost acknowledgement records the offer once.
    /// </summary>
    [Fact]
    public async Task AReplayedOffer_RecordsItOnce()
    {
        Fixture f = await SetUpAsync("m7-offer-replay");

        DealDetailResponse deal = await OpenDealAsync(f);

        string key = Guid.NewGuid().ToString("N");

        RecordOfferRequest request = new(
            "Inbound", [Money("GuaranteedCompensation", 500_000m)], deal.Deal.Version);

        RecordOfferResponse first = await RecordOfferAsync(f, deal.Deal.Id, request, key);
        RecordOfferResponse replay = await RecordOfferAsync(f, deal.Deal.Id, request, key);

        Assert.Equal(first.OfferId, replay.OfferId);

        OfferResponse[] thread = await GetAsync<OfferResponse[]>(
            f.Client, $"{f.Root}/offers?dealId={deal.Deal.Id}");

        Assert.Single(thread);
    }

    /// <summary>
    /// A retry of an acceptance cannot produce a second accepted offer, a second
    /// deal event or a duplicate audit row.
    /// </summary>
    [Fact]
    public async Task AReplayedAcceptance_AgreesTheTermsOnce()
    {
        Fixture f = await SetUpAsync("m7-accept-replay");

        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse recorded = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound", [Money("GuaranteedCompensation", 500_000m)], deal.Deal.Version));

        OfferResponse offer = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{recorded.OfferId}");

        string key = Guid.NewGuid().ToString("N");
        AnswerOfferRequest request = new("Accept", offer.Version);

        await AnswerOfferAsync(f, recorded.OfferId, request, key);
        await AnswerOfferAsync(f, recorded.OfferId, request, key);

        DealDetailResponse settled = await GetAsync<DealDetailResponse>(
            f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        Assert.Equal("TermsAgreed", settled.Deal.Status);
        Assert.Single(settled.Offers, x => x.Status == "Accepted");

        DealHistoryEntryResponse[] history = await GetAsync<DealHistoryEntryResponse[]>(
            f.Client, $"{f.Root}/deals/{deal.Deal.Id}/history");

        Assert.Single(history, x => x.Summary == "Commercial terms agreed");
        Assert.Single(history, x => x.Summary.Contains("accepted", StringComparison.Ordinal));
    }

    /// <summary>The same key with a different payload is a conflict, not a replay.</summary>
    [Fact]
    public async Task TheSameKeyWithDifferentTerms_IsAConflict()
    {
        Fixture f = await SetUpAsync("m7-key-conflict");

        DealDetailResponse deal = await OpenDealAsync(f);

        string key = Guid.NewGuid().ToString("N");

        await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound", [Money("GuaranteedCompensation", 500_000m)], deal.Deal.Version),
            key);

        using HttpRequestMessage message = new(
            HttpMethod.Post, $"{f.Root}/deals/{deal.Deal.Id}/offers")
        {
            Content = JsonContent.Create(new RecordOfferRequest(
                "Inbound", [Money("GuaranteedCompensation", 900_000m)], deal.Deal.Version)),
        };

        message.Headers.Add(ClientHeaders.IdempotencyKey, key);

        using HttpResponseMessage response = await f.Client.SendAsync(message);

        // 422 is the established idempotency-conflict code across the API
        // (AgencyOsExceptionHandler); a key reused with different arguments is a
        // caller mistake rather than a race.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ------------------------------------------------------------- redaction

    /// <summary>
    /// Economics is removed everywhere for a caller who may not read it, and
    /// structure survives so the pipeline stays usable.
    /// </summary>
    [Fact]
    public async Task Economics_AreRedactedAcrossEveryProjection()
    {
        Fixture f = await SetUpAsync("m7-economics");

        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse recorded = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound",
                [
                    Money("GuaranteedCompensation", 500_000m),
                    Percentage("BackendPercentage", 2.5m),
                    Text("CreditBilling", "Second position"),
                ],
                deal.Deal.Version));

        // The member who recorded it sees all three terms.
        OfferResponse mine = await GetAsync<OfferResponse>(
            f.Client, $"{f.Root}/offers/{recorded.OfferId}");

        Assert.Equal(3, mine.Terms.Count);

        string observerSubject = $"m7-observer-{Guid.NewGuid():N}";
        Domain.Identity.User observer = await _fixture.SeedUserAsync(observerSubject, "Observer");

        await _fixture.SeedMembershipAsync(
            f.Actor.Organization.Id, observer.Id, AgencyRole.Observer, f.Actor.User.Id);

        using HttpClient observerClient = _fixture.CreateClient(observerSubject);

        // Direct offer read: the money is gone, the structure remains.
        OfferResponse observed = await GetAsync<OfferResponse>(
            observerClient, $"{f.Root}/offers/{recorded.OfferId}");

        Assert.Single(observed.Terms);
        Assert.Equal("CreditBilling", observed.Terms.Single().Code);
        Assert.DoesNotContain(observed.Terms, x => x.IsEconomic);

        // Deal detail: the same rule, and the strategy is absent too.
        DealDetailResponse observedDeal = await GetAsync<DealDetailResponse>(
            observerClient, $"{f.Root}/deals/{deal.Deal.Id}");

        Assert.Null(observedDeal.StrategyNotes);
        Assert.All(observedDeal.Offers, offer => Assert.DoesNotContain(offer.Terms, x => x.IsEconomic));
        Assert.DoesNotContain(observedDeal.OpenOffer!.Terms, x => x.IsEconomic);

        // The offer list.
        OfferResponse[] listed = await GetAsync<OfferResponse[]>(
            observerClient, $"{f.Root}/offers?dealId={deal.Deal.Id}");

        Assert.All(listed, offer => Assert.DoesNotContain(offer.Terms, x => x.IsEconomic));

        // Comparison is refused rather than emptied: a diff missing its money rows
        // would report that nothing changed when the number moved.
        using HttpResponseMessage comparison = await observerClient.GetAsync(
            $"{f.Root}/deals/{deal.Deal.Id}/comparison"
                + $"?previousOfferId={recorded.OfferId}&currentOfferId={recorded.OfferId}");

        Assert.Equal(HttpStatusCode.Forbidden, comparison.StatusCode);
    }

    /// <summary>
    /// Strategy is absent, and cannot be confirmed by searching for a phrase in it.
    /// </summary>
    [Fact]
    public async Task Search_FindsDealsButNeverTheirStrategyOrTerms()
    {
        Fixture f = await SetUpAsync("m7-search");

        await OpenDealAsync(f, strategy: "Zephyrine will not go past six hundred.");

        SearchResponse byName = await GetAsync<SearchResponse>(
            f.Client, $"{f.Root}/search?q=Undertow&types=Deal");

        Assert.Contains(byName.Hits, x => x.Type == "Deal");

        // Nothing from the strategy is indexed.
        SearchResponse byStrategy = await GetAsync<SearchResponse>(
            f.Client, $"{f.Root}/search?q=Zephyrine&types=Deal");

        Assert.Empty(byStrategy.Hits);

        // Neither is a compensation figure.
        SearchResponse byAmount = await GetAsync<SearchResponse>(
            f.Client, $"{f.Root}/search?q=500000&types=Deal");

        Assert.Empty(byAmount.Hits);
    }

    // --------------------------------------------------------- surfaces

    /// <summary>
    /// A saved deals view runs and carries no economics, because a summary carries
    /// none to begin with.
    /// </summary>
    [Fact]
    public async Task SavedViews_RunOverDeals()
    {
        Fixture f = await SetUpAsync("m7-saved-view");

        await OpenDealAsync(f);

        Contracts.SavedViews.SavedViewResponse view =
            await CreatedAsync<Contracts.SavedViews.SavedViewResponse>(
                f.Client.PostAsJsonAsync(
                    $"{f.Root}/saved-views",
                    new Contracts.SavedViews.CreateSavedViewRequest(
                        "Live negotiations",
                        new Contracts.SavedViews.SavedViewDefinitionModel(
                            5,
                            "Deals",
                            new Contracts.SavedViews.SavedViewFiltersModel(DealStatus: "Draft")))));

        Contracts.SavedViews.SavedViewResultsResponse results =
            await GetAsync<Contracts.SavedViews.SavedViewResultsResponse>(
                f.Client, $"{f.Root}/saved-views/{view.Id}/results");

        Assert.Single(results.Deals);
        Assert.Equal("Deals", results.Target);
    }

    [Fact]
    public async Task TheCommandCentre_ReportsWhatIsOutstanding()
    {
        Fixture f = await SetUpAsync("m7-command-centre");

        DealDetailResponse deal = await OpenDealAsync(f);

        await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound",
                [Money("GuaranteedCompensation", 500_000m)],
                deal.Deal.Version,
                ExpiresAt: DateTimeOffset.UtcNow.AddDays(3)));

        DealCommandCenterResponse centre = await GetAsync<DealCommandCenterResponse>(
            f.Client, $"{f.Root}/deal-command-center");

        Assert.Equal(1, centre.NegotiatingCount);
        Assert.Single(centre.Negotiating);
        Assert.Single(centre.AwaitingResponse);
        Assert.Single(centre.ExpiringSoon);
        Assert.Single(centre.RecentlyReceived);
        Assert.Empty(centre.TermsAgreedAwaitingContract);
    }

    [Fact]
    public async Task ThePipeline_GroupsNegotiationsByStatus()
    {
        Fixture f = await SetUpAsync("m7-pipeline");

        await OpenDealAsync(f);

        DealPipelineColumnResponse[] columns = await GetAsync<DealPipelineColumnResponse[]>(
            f.Client, $"{f.Root}/deal-pipeline");

        Assert.Contains(columns, x => x.Status == "Draft" && x.Deals.Count == 1);
        Assert.Contains(columns, x => x.Status == "TermsAgreed" && x.Deals.Count == 0);
    }

    /// <summary>
    /// A follow-up asked for with an offer becomes a linked task, created in the
    /// same transaction.
    /// </summary>
    [Fact]
    public async Task AFollowUpAskedFor_BecomesALinkedTask()
    {
        Fixture f = await SetUpAsync("m7-followup");

        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse recorded = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound",
                [Money("Fee", 100_000m)],
                deal.Deal.Version,
                FollowUp: new DealFollowUpRequest("Prepare the counter")));

        Assert.NotNull(recorded.FollowUpTaskId);

        DealDetailResponse withTask = await GetAsync<DealDetailResponse>(
            f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        DealTaskResponse task = Assert.Single(withTask.OpenTasks);

        Assert.Equal("Prepare the counter", task.Title);
        Assert.Equal(recorded.OfferId, task.OfferId);
        Assert.Equal(1, withTask.Deal.OpenTaskCount);
    }

    /// <summary>The published term catalog is what the client builds an editor from.</summary>
    [Fact]
    public async Task TheTermCatalogIsPublished()
    {
        Fixture f = await SetUpAsync("m7-catalog");

        DealTermDefinitionResponse[] terms = await GetAsync<DealTermDefinitionResponse[]>(
            f.Client, "/api/v1/deal-terms");

        Assert.NotEmpty(terms);
        Assert.Contains(terms, x => x.Code == "GuaranteedCompensation" && x.IsEconomic);
        Assert.Contains(terms, x => x.Code == "StartDate" && !x.IsEconomic);

        // Every money or percentage term is economic, which is what the redaction
        // rule depends on.
        Assert.All(
            terms.Where(x => x.ValueKind is "Money" or "Percentage"),
            x => Assert.True(x.IsEconomic, $"{x.Code} carries a figure and is not economic."));
    }

    /// <summary>Money is refused without a currency, and an unknown one is refused too.</summary>
    [Fact]
    public async Task MoneyAlwaysCarriesAKnownCurrency()
    {
        Fixture f = await SetUpAsync("m7-currency");

        DealDetailResponse deal = await OpenDealAsync(f);

        foreach (TermValueRequest bad in new[]
        {
            new TermValueRequest("Money", Amount: 500_000m),
            new TermValueRequest("Money", Amount: 500_000m, Currency: "XYZ"),
            new TermValueRequest("Money", Currency: "USD"),
        })
        {
            using HttpResponseMessage response = await f.Client.PostAsJsonAsync(
                $"{f.Root}/deals/{deal.Deal.Id}/offers",
                new RecordOfferRequest(
                    "Inbound",
                    [new OfferTermRequest("GuaranteedCompensation", bad)],
                    deal.Deal.Version));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task DealsRequireAuthentication()
    {
        Fixture f = await SetUpAsync("m7-anonymous");

        using HttpClient anonymous = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync($"{f.Root}/deals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ----------------------------------------------------------------- setup

    private sealed record Fixture(
        SeededActor Actor,
        HttpClient Client,
        string Root,
        Guid OpportunityId,
        Guid TargetId,
        Guid StudioId);

    /// <summary>
    /// A tenant with a project, a studio, a live pursuit and one interested target.
    /// </summary>
    private async Task<Fixture> SetUpAsync(string label, string targetStage = "Interested")
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        ProjectDetailResponse project = await CreatedAsync<ProjectDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/projects", new CreateProjectRequest("The Undertow", "FeatureFilm")));

        CompanyDetailResponse studio = await CreatedAsync<CompanyDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/companies", new CreateCompanyRequest("Northgate Pictures", Type: "Studio")));

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/opportunities",
                new CreateOpportunityRequest(
                    "The Undertow to market",
                    "ProjectMarket",
                    actor.User.Id.Value,
                    Description: "Take the feature out to studios.",
                    Subjects: [new OpportunitySubjectRequest("Project", project.Project.Id, "Primary")])));

        await NoContentAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunity.Opportunity.Id}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));

        OpportunityDetailResponse active = await GetAsync<OpportunityDetailResponse>(
            client, $"{root}/opportunities/{opportunity.Opportunity.Id}");

        Guid targetId = await CreatedIdAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunity.Opportunity.Id}/targets",
            new AddOpportunityTargetRequest(active.Opportunity.Version, CompanyId: studio.Company.Id)));

        Fixture fixture = new(
            actor, client, root, opportunity.Opportunity.Id, targetId, studio.Company.Id);

        await WalkTargetToAsync(fixture, targetStage);

        return fixture;
    }

    /// <summary>Walks the seeded target up to a stage through the M6 commands.</summary>
    private static async Task WalkTargetToAsync(Fixture f, string stage)
    {
        string[] path = stage switch
        {
            "Identified" => [],
            "Approved" => ["Approved"],
            "Contacted" => ["Approved", "Contacted"],
            "Interested" => ["Approved", "Contacted", "Engaged", "Interested"],
            _ => ["Approved", "Contacted", "Engaged", "Interested", "Advanced"],
        };

        foreach (string step in path)
        {
            OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
                f.Client, $"{f.Root}/opportunity-targets/{f.TargetId}");

            if (target.Stage == step)
            {
                continue;
            }

            await NoContentAsync(f.Client.PostAsJsonAsync(
                $"{f.Root}/opportunity-targets/{f.TargetId}/stage",
                new MoveOpportunityTargetRequest(step, target.Version)));
        }
    }

    private static Task<DealDetailResponse> OpenDealAsync(Fixture f, string? strategy = null) =>
        CreatedAsync<DealDetailResponse>(f.Client.PostAsJsonAsync(
            $"{f.Root}/deals",
            new CreateDealRequest(
                f.OpportunityId,
                f.TargetId,
                "The Undertow - Northgate",
                "ProjectSale",
                f.Actor.User.Id.Value,
                Summary: "Outright sale of the feature.",
                StrategyNotes: strategy)));

    private static async Task<RecordOfferResponse> RecordOfferAsync(
        Fixture f,
        Guid dealId,
        RecordOfferRequest request,
        string? key = null)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, $"{f.Root}/deals/{dealId}/offers")
        {
            Content = JsonContent.Create(request),
        };

        if (key is not null)
        {
            message.Headers.Add(ClientHeaders.IdempotencyKey, key);
        }

        using HttpResponseMessage response = await f.Client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<RecordOfferResponse>())!;
    }

    private static async Task<AnswerOfferResponse> AnswerOfferAsync(
        Fixture f,
        Guid offerId,
        AnswerOfferRequest request,
        string? key = null)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, $"{f.Root}/offers/{offerId}/answer")
        {
            Content = JsonContent.Create(request),
        };

        if (key is not null)
        {
            message.Headers.Add(ClientHeaders.IdempotencyKey, key);
        }

        using HttpResponseMessage response = await f.Client.SendAsync(message);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<AnswerOfferResponse>())!;
    }

    /// <summary>A negotiation with an inbound offer and an outbound counter.</summary>
    private static async Task<(DealDetailResponse Deal, Guid First, Guid Second)> ThreadAsync(Fixture f)
    {
        DealDetailResponse deal = await OpenDealAsync(f);

        RecordOfferResponse first = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Inbound", [Money("GuaranteedCompensation", 500_000m)], deal.Deal.Version));

        deal = await GetAsync<DealDetailResponse>(f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        RecordOfferResponse second = await RecordOfferAsync(
            f,
            deal.Deal.Id,
            new RecordOfferRequest(
                "Outbound",
                [Money("GuaranteedCompensation", 650_000m)],
                deal.Deal.Version,
                RespondsToOfferId: first.OfferId));

        deal = await GetAsync<DealDetailResponse>(f.Client, $"{f.Root}/deals/{deal.Deal.Id}");

        return (deal, first.OfferId, second.OfferId);
    }

    private static OfferTermRequest Money(string code, decimal amount, string currency = "USD") =>
        new(code, new TermValueRequest("Money", Amount: amount, Currency: currency));

    private static OfferTermRequest Percentage(string code, decimal percentage) =>
        new(code, new TermValueRequest("Percentage", Number: percentage));

    private static OfferTermRequest Text(string code, string text) =>
        new(code, new TermValueRequest("Text", Text: text));

    private sealed record CreatedId(Guid Id);

    private static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        CreatedId? created = await response.Content.ReadFromJsonAsync<CreatedId>();

        return created!.Id;
    }

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task NoContentAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
