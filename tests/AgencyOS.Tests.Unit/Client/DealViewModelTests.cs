using System.Net;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Deals;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>The deal list: filters, derived counts and states.</summary>
public sealed class DealListViewModelTests
{
    [Fact]
    public async Task TheList_LoadsAndReportsWhatItFound()
    {
        FakeAgencyOsApi api = new();

        api.Deals.Add(FakeAgencyOsApi.Deal("The Undertow - Northgate"));
        api.Deals.Add(FakeAgencyOsApi.Deal("Salt Road - writing", "Writing"));

        DealListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Deals.Count);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
    }

    /// <summary>The list opens on what is being negotiated, not on everything closed.</summary>
    [Fact]
    public async Task TheList_DefaultsToActiveNegotiations()
    {
        FakeAgencyOsApi api = new();

        DealListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal("Negotiating", api.LastDealFilter.Status);
    }

    [Fact]
    public async Task EveryFilter_ReachesTheServerRatherThanBeingAppliedLocally()
    {
        FakeAgencyOsApi api = new();

        DealListViewModel viewModel = new(api)
        {
            Status = "TermsAgreed",
            Kind = "Writing",
            AwaitingResponse = true,
            TermsAgreedOnly = true,
            Search = "  undertow  ",
        };

        await viewModel.LoadAsync();

        Assert.Equal(("TermsAgreed", "Writing", true, true, "undertow"), api.LastDealFilter);
    }

    [Fact]
    public async Task DerivedCounts_ComeFromWhatTheServerReturned()
    {
        FakeAgencyOsApi api = new();

        api.Deals.Add(FakeAgencyOsApi.Deal(
            "Awaiting", hasOpenOffer: true, openOfferExpiresAt: DateTimeOffset.UtcNow.AddDays(3)));
        api.Deals.Add(FakeAgencyOsApi.Deal(
            "Also awaiting", hasOpenOffer: true, openOfferExpiresAt: DateTimeOffset.UtcNow.AddDays(60)));
        api.Deals.Add(FakeAgencyOsApi.Deal("Settled", status: "TermsAgreed"));

        DealListViewModel viewModel = new(api) { Status = null };

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Awaiting);
        Assert.Equal(1, viewModel.TermsAgreed);

        // Only the one lapsing within a week.
        Assert.Equal(1, viewModel.ExpiringSoon);
    }

    [Fact]
    public async Task AnEmptyDeskIsAStateRatherThanAnError()
    {
        FakeAgencyOsApi api = new();

        DealListViewModel viewModel = new(api);

        Assert.False(viewModel.IsEmpty);

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task ARefusal_SurfacesTheServersExplanation()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                HttpStatusCode.Forbidden, "Permission denied", "Permission 'deals.read' is required."),
        };

        DealListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal("Permission 'deals.read' is required.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task AnUnreachableServer_IsNotAnEmptyDesk()
    {
        FakeAgencyOsApi api = new() { NextFailure = new HttpRequestException("no route to host") };

        DealListViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
    }
}

/// <summary>One negotiation's working surface.</summary>
public sealed class DealDetailViewModelTests
{
    [Fact]
    public async Task TheDetail_LoadsTheThreadAndItsHistory()
    {
        FakeAgencyOsApi api = new();

        Guid id = await SeedAsync(api);

        api.DealHistory.Add(new DealHistoryEntryResponse(
            DateTimeOffset.UtcNow, "OfferOpened", "Inbound offer recorded", null, null, "Dana Ruiz"));

        DealDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(id);

        Assert.NotNull(viewModel.Deal);
        Assert.Single(viewModel.Offers);
        Assert.Single(viewModel.History);
        Assert.False(viewModel.IsEmpty);
    }

    /// <summary>
    /// Strategy is absent rather than refused when the caller may not read it, and
    /// the screen shows nothing at all. A "hidden" placeholder would leak that
    /// there is something to hide.
    /// </summary>
    [Fact]
    public async Task StrategyIsShownOnlyWhenTheServerReturnedIt()
    {
        FakeAgencyOsApi api = new();

        Guid id = await SeedAsync(api);

        DealDetailViewModel redacted = new(api);
        await redacted.LoadAsync(id);

        Assert.False(redacted.HasStrategy);
        Assert.Null(redacted.Deal!.StrategyNotes);

        api.DealStrategy = "Hold at 700 and trade backend for guarantee.";

        DealDetailViewModel permitted = new(api);
        await permitted.LoadAsync(id);

        Assert.True(permitted.HasStrategy);
    }

    /// <summary>
    /// Economics is inferred from the terms actually returned, so a screen can
    /// avoid offering a comparison the server would refuse.
    /// </summary>
    [Fact]
    public async Task EconomicsIsInferredFromTheTermsThatCameBack()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();
        api.Deals.Add(FakeAgencyOsApi.Deal("The Undertow - Northgate", id: id));

        // What a caller without deals.economics.read sees: the structural terms
        // survive and the money is simply not there.
        api.Offers[id] =
        [
            FakeAgencyOsApi.Offer(
                Guid.NewGuid(),
                id,
                "Inbound",
                "Open",
                1,
                terms: [FakeAgencyOsApi.StructuralTerm("CreditBilling", "First position")]),
        ];

        DealDetailViewModel redacted = new(api);
        await redacted.LoadAsync(id);

        Assert.False(redacted.HasEconomics);

        api.Offers[id] =
        [
            FakeAgencyOsApi.Offer(
                Guid.NewGuid(),
                id,
                "Inbound",
                "Open",
                1,
                terms: [FakeAgencyOsApi.MoneyTerm("GuaranteedCompensation", 500_000m)]),
        ];

        DealDetailViewModel permitted = new(api);
        await permitted.LoadAsync(id);

        Assert.True(permitted.HasEconomics);
    }

    /// <summary>The thread is ordered most recent first, and drafts are not in it.</summary>
    [Fact]
    public async Task ThePreviousOfferIsTheOneBeforeTheCurrentOne()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();
        api.Deals.Add(FakeAgencyOsApi.Deal("The Undertow - Northgate", id: id, hasOpenOffer: true));

        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid third = Guid.NewGuid();

        api.Offers[id] =
        [
            FakeAgencyOsApi.Offer(first, id, "Inbound", "Superseded", 1),
            FakeAgencyOsApi.Offer(second, id, "Outbound", "Superseded", 2),
            FakeAgencyOsApi.Offer(third, id, "Inbound", "Open", 3),
        ];

        DealDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(id);

        Assert.Equal(third, viewModel.Offers[0].Id);
        Assert.Equal(third, viewModel.OpenOffer!.Id);
        Assert.Equal(second, viewModel.PreviousOffer!.Id);
    }

    /// <summary>
    /// Terms agreed is stated as a commercial fact and never as a signed contract.
    /// </summary>
    [Fact]
    public async Task Standing_SaysTermsAgreedAndNeverSigned()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();
        api.Deals.Add(FakeAgencyOsApi.Deal(
            "The Undertow - Northgate", status: "TermsAgreed", id: id));

        DealDetailViewModel viewModel = new(api);

        await viewModel.LoadAsync(id);

        Assert.Contains("terms agreed", viewModel.Standing, StringComparison.Ordinal);
        Assert.Contains("no contract recorded", viewModel.Standing, StringComparison.Ordinal);

        foreach (string forbidden in new[] { "signed", "executed", "closed won", "paid" })
        {
            Assert.DoesNotContain(forbidden, viewModel.Standing, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ASettledDeal_NoLongerAcceptsOffers()
    {
        FakeAgencyOsApi api = new();

        Guid id = Guid.NewGuid();
        api.Deals.Add(FakeAgencyOsApi.Deal("Settled", status: "TermsAgreed", id: id));

        DealDetailViewModel viewModel = new(api);
        await viewModel.LoadAsync(id);

        Assert.False(viewModel.AcceptsOffers);

        api.Deals[0] = api.Deals[0] with { Status = "Negotiating" };

        await viewModel.LoadAsync(id);

        Assert.True(viewModel.AcceptsOffers);
    }

    [Fact]
    public void BeforeLoading_TheDetailIsNeitherEmptyNorFailed()
    {
        DealDetailViewModel viewModel = new(new FakeAgencyOsApi());

        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasError);
        Assert.Equal(string.Empty, viewModel.Standing);
    }

    [Fact]
    public async Task ARefusedDetail_KeepsThePreviousScreen()
    {
        FakeAgencyOsApi api = new();

        Guid id = await SeedAsync(api);

        DealDetailViewModel viewModel = new(api);

        api.NextFailure = new AgencyOsApiException(
            HttpStatusCode.Forbidden, "Permission denied", "Permission 'deals.read' is required.");

        await viewModel.LoadAsync(id);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
        Assert.Empty(viewModel.Offers);
    }

    /// <summary>A negotiation with one standing inbound offer.</summary>
    internal static async Task<Guid> SeedAsync(FakeAgencyOsApi api)
    {
        Guid id = Guid.NewGuid();

        api.Deals.Add(FakeAgencyOsApi.Deal("The Undertow - Northgate", id: id));

        await api.RecordOfferAsync(
            id,
            new RecordOfferRequest(
                "Inbound",
                [new OfferTermRequest(
                    "GuaranteedCompensation",
                    new TermValueRequest("Money", Amount: 500_000m, Currency: "USD"))],
                1,
                Summary: "Opening number"),
            Guid.NewGuid().ToString("N"));

        return id;
    }
}

/// <summary>Comparison: what changed, never whether it is good.</summary>
public sealed class OfferComparisonViewModelTests
{
    [Fact]
    public async Task AComparison_ShowsWhatMoved()
    {
        FakeAgencyOsApi api = new();

        (Guid deal, Guid previous, Guid current) = Seed(api);

        OfferComparisonViewModel viewModel = new(api);

        await viewModel.LoadAsync(deal, previous, current);

        TermDifferenceResponse difference = Assert.Single(viewModel.Differences);

        Assert.Equal("Changed", difference.Change);
        Assert.Equal("Increased", difference.Direction);
        Assert.Equal(1, viewModel.ChangedCount);
    }

    /// <summary>Unchanged terms are hidden by default and shown on request.</summary>
    [Fact]
    public async Task UnchangedTermsAreHiddenUntilAskedFor()
    {
        FakeAgencyOsApi api = new();

        Guid deal = Guid.NewGuid();
        Guid previous = Guid.NewGuid();
        Guid current = Guid.NewGuid();

        api.Deals.Add(FakeAgencyOsApi.Deal("The Undertow - Northgate", id: deal));

        api.Offers[deal] =
        [
            FakeAgencyOsApi.Offer(
                previous, deal, "Inbound", "Superseded", 1,
                terms: [FakeAgencyOsApi.MoneyTerm("Fee", 100_000m)]),
            FakeAgencyOsApi.Offer(
                current, deal, "Outbound", "Open", 2,
                terms: [FakeAgencyOsApi.MoneyTerm("Fee", 100_000m)]),
        ];

        OfferComparisonViewModel viewModel = new(api);

        await viewModel.LoadAsync(deal, previous, current);

        Assert.Empty(viewModel.Differences);
        Assert.True(viewModel.IsEmpty);
        Assert.Equal(0, viewModel.ChangedCount);

        viewModel.MaterialOnly = false;

        Assert.Single(viewModel.Differences);
    }

    /// <summary>
    /// Comparison is refused rather than emptied without economics. A diff missing
    /// its money rows would say nothing changed when the number doubled.
    /// </summary>
    [Fact]
    public async Task WithoutEconomics_TheComparisonIsRefused()
    {
        FakeAgencyOsApi api = new()
        {
            NextFailure = new AgencyOsApiException(
                HttpStatusCode.Forbidden,
                "Permission denied",
                "Permission 'deals.economics.read' is required."),
        };

        OfferComparisonViewModel viewModel = new(api);

        await viewModel.LoadAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.True(viewModel.HasError);
        Assert.Contains("economics", viewModel.ErrorMessage, StringComparison.Ordinal);

        // Not an empty diff, which would read as "nothing changed".
        Assert.Empty(viewModel.Differences);
        Assert.False(viewModel.IsEmpty);
    }

    private static (Guid Deal, Guid Previous, Guid Current) Seed(FakeAgencyOsApi api)
    {
        Guid deal = Guid.NewGuid();
        Guid previous = Guid.NewGuid();
        Guid current = Guid.NewGuid();

        api.Deals.Add(FakeAgencyOsApi.Deal("The Undertow - Northgate", id: deal));

        api.Offers[deal] =
        [
            FakeAgencyOsApi.Offer(
                previous, deal, "Inbound", "Superseded", 1,
                terms: [FakeAgencyOsApi.MoneyTerm("GuaranteedCompensation", 500_000m)]),
            FakeAgencyOsApi.Offer(
                current, deal, "Outbound", "Open", 2,
                terms: [FakeAgencyOsApi.MoneyTerm("GuaranteedCompensation", 650_000m)]),
        ];

        return (deal, previous, current);
    }
}

/// <summary>The deal board and the term catalog.</summary>
public sealed class DealPipelineAndCatalogTests
{
    [Fact]
    public async Task ThePipeline_GroupsNegotiationsByStatus()
    {
        FakeAgencyOsApi api = new();

        api.DealPipeline.Add(new DealPipelineColumnResponse(
            "Negotiating", [FakeAgencyOsApi.Deal("One"), FakeAgencyOsApi.Deal("Two")]));
        api.DealPipeline.Add(new DealPipelineColumnResponse("TermsAgreed", []));

        DealPipelineViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Columns.Count);
        Assert.Equal(2, viewModel.DealCount);
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task ColumnsWithNoDeals_ReadAsEmpty()
    {
        FakeAgencyOsApi api = new();

        api.DealPipeline.Add(new DealPipelineColumnResponse("Draft", []));
        api.DealPipeline.Add(new DealPipelineColumnResponse("Negotiating", []));

        DealPipelineViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.Equal(0, viewModel.DealCount);
    }

    /// <summary>
    /// The term vocabulary comes from the server, so a term editor cannot drift
    /// from what the server validates against.
    /// </summary>
    [Fact]
    public async Task TheCatalog_ComesFromTheServer()
    {
        FakeAgencyOsApi api = new();

        api.DealTerms.Add(new DealTermDefinitionResponse(
            "GuaranteedCompensation", "Guaranteed compensation", "Money", true, [], null, null));
        api.DealTerms.Add(new DealTermDefinitionResponse(
            "StartDate", "Start date", "Date", false, [], null, null));

        DealTermCatalogViewModel viewModel = new(api);

        await viewModel.LoadAsync();

        Assert.Equal(2, viewModel.Terms.Count);
        Assert.Single(viewModel.Economic);
        Assert.Equal("Start date", viewModel.Find("StartDate")!.DisplayName);
        Assert.Null(viewModel.Find("NotATerm"));
    }
}

/// <summary>
/// The deal commands the palette offers, and what recording actually does.
/// </summary>
public sealed class DealClientTests
{
    /// <summary>
    /// Every deal command the workspace handles is reachable from the keyboard. A
    /// command the palette names but nothing implements is worse than none.
    /// </summary>
    [Theory]
    [InlineData("deal.create")]
    [InlineData("deal.open")]
    [InlineData("offer.record.inbound")]
    [InlineData("offer.record.counter")]
    [InlineData("offer.compare")]
    [InlineData("offer.accept")]
    [InlineData("offer.answer")]
    [InlineData("go.negotiations")]
    [InlineData("go.terms.agreed")]
    [InlineData("go.deals.awaiting")]
    [InlineData("go.deals")]
    public void ThePalette_OffersTheDealCommands(string commandId)
    {
        CommandPaletteViewModel palette = new();

        Assert.Contains(palette.AllCommands, c => c.Id == commandId);
    }

    /// <summary>
    /// The category is searchable. The query also matches "Go to Deals" by title,
    /// which is the palette working as intended rather than a leak between
    /// categories.
    /// </summary>
    [Fact]
    public void ThePalette_FindsDealCommandsByCategory()
    {
        CommandPaletteViewModel palette = new() { Query = "Deals" };

        Assert.NotEmpty(palette.Results);
        Assert.Contains(palette.Results, c => c.Category == "Deals");

        Assert.All(
            palette.Results,
            c => Assert.True(
                c.Category == "Deals"
                    || c.Title.Contains("Deals", StringComparison.OrdinalIgnoreCase)
                    || c.Id.Contains("deal", StringComparison.OrdinalIgnoreCase),
                $"'{c.Id}' matched the query 'Deals' without mentioning it."));
    }

    /// <summary>
    /// The palette never offers to do something AgencyOS cannot do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guarantee is about the verb, not the noun. AgencyOS transmits nothing,
    /// signs nothing and executes nothing: every command that touches an outward
    /// act is named for writing down what somebody says happened, so a user reading
    /// the palette never expects the system to perform the act itself (ADR-0021,
    /// ADR-0022).
    /// </para>
    /// <para>
    /// M8 makes signatures and notices real subjects, so the assertion moved from
    /// "the word sign never appears" to "no command title begins with a verb that
    /// promises the act". "Record signature" is honest; "Sign contract" would not
    /// be.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePalette_NeverOffersToSendAnything()
    {
        CommandPaletteViewModel palette = new();

        string[] forbidden =
        [
            "Send ", "Sign ", "Execute ", "Transmit ", "Deliver ", "Email ", "Upload ",
        ];

        foreach (PaletteCommand command in palette.AllCommands)
        {
            Assert.DoesNotContain("Send offer", command.Title, StringComparison.OrdinalIgnoreCase);

            foreach (string verb in forbidden)
            {
                Assert.False(
                    command.Title.StartsWith(verb, StringComparison.OrdinalIgnoreCase),
                    $"'{command.Title}' promises an act AgencyOS does not perform.");
            }
        }
    }

    /// <summary>
    /// Every consequential legal command is named for recording, not for doing.
    /// </summary>
    /// <remarks>
    /// The M8 half of the guarantee above, stated positively. A signature, a notice
    /// and an option outcome are all assertions somebody makes about the world, and
    /// the palette says so before anybody clicks (ADR-0022).
    /// </remarks>
    [Fact]
    public void TheLegalCommands_AreNamedForRecording()
    {
        CommandPaletteViewModel palette = new();

        string[] mustRecord =
        [
            "contract.signature.record",
            "contract.version.record",
            "notice.record",

            // rights.grant.record, option.record and obligation.record were
            // advertised and never dispatched: the contracts workspace shows
            // rights, options and obligations but has no surface that records one.
            // M13 removed the claim rather than the requirement (ADR-0032).
            "option.resolve",
            "obligation.resolve",
        ];

        foreach (string id in mustRecord)
        {
            PaletteCommand command = Assert.Single(palette.AllCommands, x => x.Id == id);

            Assert.StartsWith("Record", command.Title, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Recording a counter supersedes the standing offer rather than editing it.
    /// </summary>
    [Fact]
    public async Task RecordingACounter_SupersedesTheStandingOffer()
    {
        FakeAgencyOsApi api = new();

        Guid deal = await DealDetailViewModelTests.SeedAsync(api);

        Guid first = api.OffersOf(deal).Single().Id;

        RecordOfferResponse counter = await api.RecordOfferAsync(
            deal,
            new RecordOfferRequest(
                "Outbound",
                [new OfferTermRequest(
                    "GuaranteedCompensation",
                    new TermValueRequest("Money", Amount: 650_000m, Currency: "USD"))],
                2,
                RespondsToOfferId: first),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(first, counter.SupersededOfferId);

        List<OfferResponse> thread = api.OffersOf(deal);

        Assert.Equal(2, thread.Count);
        Assert.Equal("Superseded", thread.Single(x => x.Id == first).Status);
        Assert.Equal("Open", thread.Single(x => x.Id == counter.OfferId).Status);

        // What the first offer said is untouched.
        Assert.Equal(
            500_000m,
            thread.Single(x => x.Id == first).Terms.Single().Amount);
    }

    [Fact]
    public async Task AcceptingAnOffer_AgreesTheTerms()
    {
        FakeAgencyOsApi api = new();

        Guid deal = await DealDetailViewModelTests.SeedAsync(api);
        Guid offer = api.OffersOf(deal).Single().Id;

        AnswerOfferResponse answered = await api.AnswerOfferAsync(
            offer, new AnswerOfferRequest("Accept", 1), Guid.NewGuid().ToString("N"));

        Assert.Equal("TermsAgreed", answered.DealStatus);
        Assert.Equal(offer, api.Deals.Single(x => x.Id == deal).AcceptedOfferId);
    }

    /// <summary>
    /// Every deal write carries an idempotency key. M7 writes are online-only, so
    /// the key is the client's whole half of the at-most-one-effect guarantee: a
    /// keyless retry after a lost acknowledgement is a second recorded offer.
    /// </summary>
    [Fact]
    public async Task EveryDealWrite_CarriesAnIdempotencyKey()
    {
        FakeAgencyOsApi api = new();

        Guid deal = await DealDetailViewModelTests.SeedAsync(api);
        Guid offer = api.OffersOf(deal).Single().Id;

        await api.AnswerOfferAsync(
            offer, new AnswerOfferRequest("Reject", 1), Guid.NewGuid().ToString("N"));

        await api.CloseDealAsync(
            deal,
            new CloseDealRequest("NoDeal", 1, Reason: "They passed."),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(3, api.IdempotencyKeys.Count);
        Assert.All(api.IdempotencyKeys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
        Assert.Equal(3, api.IdempotencyKeys.Distinct().Count());
        Assert.All(api.Effects.Values, count => Assert.Equal(1, count));
    }

    /// <summary>
    /// Reopening unwinds the agreement without touching what was agreed.
    /// </summary>
    [Fact]
    public async Task ReopeningClearsTheAgreementAndReturnsToNegotiation()
    {
        FakeAgencyOsApi api = new();

        Guid deal = await DealDetailViewModelTests.SeedAsync(api);
        Guid offer = api.OffersOf(deal).Single().Id;

        await api.AnswerOfferAsync(
            offer, new AnswerOfferRequest("Accept", 1), Guid.NewGuid().ToString("N"));

        await api.ReopenNegotiationAsync(
            deal,
            new ReopenNegotiationRequest(2, "They came back on the backend."),
            Guid.NewGuid().ToString("N"));

        DealSummaryResponse reopened = api.Deals.Single(x => x.Id == deal);

        Assert.Equal("Negotiating", reopened.Status);
        Assert.Null(reopened.AcceptedOfferId);

        // The offer's own terms are exactly what they were.
        Assert.Equal(500_000m, api.OffersOf(deal).Single(x => x.Id == offer).Terms.Single().Amount);
    }
}
