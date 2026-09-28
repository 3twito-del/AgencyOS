using AgencyOS.Client;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.PeopleSlice;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// BF-01: a People search that matched nobody said the directory was empty.
/// </summary>
/// <remarks>
/// The C10 blind operator searched "Joel" on a tenant holding fifty people and was
/// told "No people yet - Create the first person to begin." The list view model knew
/// only that the rows it had just received were none, so a filtered answer and an
/// empty directory produced the same statement.
/// </remarks>
public sealed class PeopleSearchAbsenceTests
{
    [Fact]
    public async Task ASearchThatMatchesNobodyDoesNotSayTheDirectoryIsEmpty()
    {
        PeopleListViewModel viewModel = new(Populated()) { SearchText = "Joel" };

        await viewModel.LoadAsync();

        Assert.Empty(viewModel.People);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
        Assert.True(viewModel.HasNoMatches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnUnfilteredAnswerWithNobodyIsAnEmptyDirectory(string search)
    {
        PeopleListViewModel viewModel = new(new FakeAgencyOsApi()) { SearchText = search };

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasNoMatches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Solenne")]
    public async Task APopulatedAnswerStatesNoAbsence(string search)
    {
        PeopleListViewModel viewModel = new(Populated()) { SearchText = search };

        await viewModel.LoadAsync();

        Assert.NotEmpty(viewModel.People);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasNoMatches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Joel")]
    public async Task AFailedLoadStatesNoAbsence(string search)
    {
        FakeAgencyOsApi api = Populated();
        api.NextFailure = new AgencyOsApiException(
            System.Net.HttpStatusCode.Forbidden, "Permission denied", "Permission 'people.read' is required.");

        PeopleListViewModel viewModel = new(api) { SearchText = search };

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasNoMatches);
    }

    [Fact]
    public void NothingLoadedYetIsNotAnEmptyDirectory()
    {
        PeopleListViewModel viewModel = new(Populated());

        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.HasNoMatches);
    }

    /// <summary>While a reload is in flight, the last answer's absence is not restated.</summary>
    [Fact]
    public async Task WhileLoadingNoAbsenceIsStated()
    {
        PeopleListViewModel viewModel = new(new FakeAgencyOsApi());

        await viewModel.LoadAsync();

        Assert.True(viewModel.IsEmpty);

        List<(bool Empty, bool NoMatches)> duringLoad = [];

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModelBase.IsLoading) && viewModel.IsLoading)
            {
                duringLoad.Add((viewModel.IsEmpty, viewModel.HasNoMatches));
            }
        };

        viewModel.SearchText = "Joel";

        await viewModel.LoadAsync();

        Assert.Equal(new List<(bool Empty, bool NoMatches)> { (false, false) }, duringLoad);
        Assert.True(viewModel.HasNoMatches);
    }

    /// <summary>The statement answers the search that was run, not text typed since.</summary>
    [Fact]
    public async Task TheNoMatchStatementBelongsToTheSearchThatWasRun()
    {
        PeopleListViewModel viewModel = new(Populated()) { SearchText = "Joel" };

        await viewModel.LoadAsync();

        viewModel.SearchText = string.Empty;

        Assert.True(viewModel.HasNoMatches);
        Assert.False(viewModel.IsEmpty);
    }

    private static FakeAgencyOsApi Populated()
    {
        FakeAgencyOsApi api = new();

        api.People.Add(Person("Solenne Achterberg"));
        api.People.Add(Person("Gemma Okonjo"));

        return api;
    }

    private static PersonSummaryResponse Person(string name) =>
        new(Guid.NewGuid(), name, null, null, null, "Active", null, null, DateTimeOffset.UtcNow, 1);
}

/// <summary>
/// BF-02: the reconciliation that explains "3 term(s) differ" could not be opened.
/// </summary>
/// <remarks>
/// Three terms were agreed and the latest draft records none. The canonical summary
/// counts three MissingFromContract differences, which is true. The client offered
/// Reconcile only when a version carried visible term rows, so the one comparison
/// that shows which three terms were not carried was unreachable.
/// </remarks>
public sealed class ReconciliationReachTests
{
    [Fact]
    public async Task ReportedDifferencesAgainstADraftWithNoTermsCanBeReconciled()
    {
        FakeAgencyOsApi api = new();
        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = C10Contract(id);

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.Equal(3, view.Contract!.Contract.UnresolvedDifferenceCount);
        Assert.Equal("3 term(s) differ between the agreed terms and the latest draft.", view.ReconciliationStanding);
        Assert.False(view.HasTerms);
        Assert.NotNull(view.LatestVersion);
        Assert.True(view.CanReconcile);
    }

    /// <summary>The comparison names each agreed term the draft did not carry, and its agreed value.</summary>
    [Fact]
    public async Task TheAuthorisedComparisonNamesTheThreeTermsNotCarried()
    {
        FakeAgencyOsApi api = new() { Reconciliation = ThreeMissing() };

        ReconciliationViewModel view = new(api);

        await view.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(3, view.Lines.Count);
        Assert.All(view.Lines, x => Assert.Equal("MissingFromContract", x.Result));
        Assert.All(view.Lines, x => Assert.Null(x.Contracted));
        Assert.Equal(
            ["140,000.00 GBP", "First position", "2027-02-01"],
            view.Lines.Select(x => x.Negotiated!.DisplayValue));
        Assert.Equal("3 not carried into the draft.", view.Summary);
        Assert.False(view.IsFaithful);

        // Said by role: the agreed value as agreed, the result, and nothing claimed
        // for a draft side the draft does not have.
        string fee = RowLabel.For(view.Lines[0], "Reconciliation");

        Assert.Equal("GuaranteedCompensation, Agreed: 140,000.00 GBP, Result: MissingFromContract", fee);
    }

    [Fact]
    public async Task WithoutADraftThereIsNothingToReconcile()
    {
        FakeAgencyOsApi api = new();
        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = C10Contract(id) with { Versions = [] };

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.Null(view.LatestVersion);
        Assert.False(view.CanReconcile);
    }

    /// <summary>With no terms visible and no reported difference, the gate is as it was.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public async Task NoVisibleTermsAndNoReportedDifferenceOffersNothing(int? differences)
    {
        FakeAgencyOsApi api = new();
        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = C10Contract(id, differences);

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.False(view.CanReconcile);
    }

    /// <summary>Visible terms still offer the comparison, whatever the count says.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task VisibleTermsStillOfferTheComparison(int differences)
    {
        FakeAgencyOsApi api = new();
        Guid id = Guid.NewGuid();

        api.ContractDetails[id] = ContractDetailViewModelTests.Detail(
            id,
            contract: FakeAgencyOsApi.Contract("Option agreement", "UnderReview", differences: differences, id: id),
            terms: [FakeAgencyOsApi.ContractTerm("GuaranteedCompensation", "150,000.00 GBP", economic: true)]);

        ContractDetailViewModel view = new(api);

        await view.LoadAsync(id);

        Assert.True(view.HasTerms);
        Assert.True(view.CanReconcile);
    }

    [Fact]
    public async Task AnOrdinaryComparisonStillRendersWhatChanged()
    {
        FakeAgencyOsApi api = new()
        {
            Reconciliation = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                [
                    new("GuaranteedCompensation", "GuaranteedCompensation", "Changed", "Decreased",
                        FakeAgencyOsApi.ContractTerm("GuaranteedCompensation", "150,000.00 GBP", economic: true),
                        FakeAgencyOsApi.ContractTerm("GuaranteedCompensation", "140,000.00 GBP", economic: true)),
                    new("GoverningLaw", "GoverningLaw", "Matched", "Level",
                        FakeAgencyOsApi.ContractTerm("GoverningLaw", "England"),
                        FakeAgencyOsApi.ContractTerm("GoverningLaw", "England")),
                ],
                1,
                false),
        };

        ReconciliationViewModel view = new(api);

        await view.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        ReconciliationLineResponse line = Assert.Single(view.Lines);

        Assert.Equal("140,000.00 GBP", line.Contracted!.DisplayValue);
        Assert.Equal("1 changed.", view.Summary);
    }

    /// <summary>A refused comparison is a refusal: no rows, no summary, no match claimed.</summary>
    [Fact]
    public async Task ARefusedReconciliationIsARefusalAndDisclosesNothing()
    {
        FakeAgencyOsApi api = new() { Reconciliation = ThreeMissing(), NextFailure = Refusal() };

        ReconciliationViewModel view = new(api);

        await view.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(view.HasError);
        Assert.Equal("Permission 'deals.economics.read' is required.", view.ErrorMessage);
        Assert.Null(view.Reconciliation);
        Assert.Empty(view.Lines);
        Assert.False(view.IsEmpty);
        Assert.False(view.IsFaithful);
        Assert.Equal(0, view.DifferenceCount);
        Assert.Equal(string.Empty, view.Summary);
    }

    [Fact]
    public async Task ARefusedReconciliationLeavesNoEarlierComparisonOnScreen()
    {
        FakeAgencyOsApi api = new() { Reconciliation = ThreeMissing() };

        ReconciliationViewModel view = new(api);

        await view.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        api.NextFailure = Refusal();

        await view.LoadAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(view.HasError);
        Assert.Null(view.Reconciliation);
        Assert.Empty(view.Lines);
        Assert.Equal(string.Empty, view.Summary);
    }

    internal static ContractDetailResponse C10Contract(Guid id, int? differences = 3) =>
        ContractDetailViewModelTests.Detail(
            id,
            contract: FakeAgencyOsApi.Contract("Harbour Lights - Maren", "Executed", differences: differences, id: id),
            terms: []);

    internal static ReconciliationResponse ThreeMissing() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            [
                Missing("GuaranteedCompensation", "140,000.00 GBP"),
                Missing("CreditPosition", "First position"),
                Missing("StartDate", "2027-02-01"),
            ],
            3,
            false);

    internal static AgencyOsApiException Refusal() =>
        new(
            System.Net.HttpStatusCode.Forbidden,
            "Permission denied",
            "Permission 'deals.economics.read' is required.");

    private static ReconciliationLineResponse Missing(string code, string agreed) =>
        new(
            code,
            code,
            "MissingFromContract",
            "NotComparable",
            FakeAgencyOsApi.ContractTerm(code, agreed, economic: code == "GuaranteedCompensation"),
            null);
}

/// <summary>
/// BF-03: Deal Activity explained a superseded offer with a raw offer identifier.
/// </summary>
public sealed class DealActivityDetailTests
{
    [Fact]
    public async Task ASupersededOfferIsNotExplainedByItsIdentifier()
    {
        (FakeAgencyOsApi api, Guid dealId, Guid[] offers) = Negotiation();

        api.DealHistory.Add(History(
            DealActivityLine.AnsweredByCounter, "Inbound offer superseded by a later offer", $"Answered by offer {offers[1]}.", offers[0]));

        DealDetailViewModel view = new(api);

        await view.LoadAsync(dealId);

        DealHistoryEntryResponse row = Assert.Single(view.History);

        Assert.Equal("Inbound offer superseded by a later offer", row.Summary);
        Assert.Equal("Answered by offer 2 (outbound).", row.Detail);
        Assert.DoesNotContain(offers[1].ToString(), row.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An answering offer the page cannot name is not named by its identifier either.</summary>
    [Fact]
    public async Task AnAnsweringOfferThePageDoesNotHoldIsNotShownRaw()
    {
        (FakeAgencyOsApi api, Guid dealId, Guid[] offers) = Negotiation();
        Guid unknown = Guid.NewGuid();

        api.DealHistory.Add(History(
            DealActivityLine.AnsweredByCounter, "Inbound offer superseded by a later offer", $"Answered by offer {unknown}.", offers[0]));

        DealDetailViewModel view = new(api);

        await view.LoadAsync(dealId);

        Assert.Null(Assert.Single(view.History).Detail);
    }

    /// <summary>
    /// A reason somebody wrote is kept, including one that reads like the recorded form.
    /// </summary>
    [Fact]
    public async Task HumanDetailIsKept()
    {
        (FakeAgencyOsApi api, Guid dealId, Guid[] offers) = Negotiation();

        api.DealHistory.Add(History("OfferOpened", "Outbound offer recorded", "Counter on fee and first-position billing.", offers[1], 3));
        api.DealHistory.Add(History("OfferRejected", "Inbound offer rejected", $"Answered by offer {offers[1]}.", offers[0], 2));
        api.DealHistory.Add(History(DealActivityLine.AnsweredByCounter, "Inbound offer superseded by a later offer", "Answered by offer letter from Halvard.", offers[0], 1));
        api.DealHistory.Add(History("StatusChanged", "Commercial terms agreed", "Accepted at 140,000 with first position.", null, 0));

        DealDetailViewModel view = new(api);

        await view.LoadAsync(dealId);

        Assert.Equal(api.DealHistory.Select(x => x.Detail), view.History.Select(x => x.Detail));
    }

    /// <summary>The server's order and the server's record are both left as they were.</summary>
    [Fact]
    public async Task OrderAndTheCanonicalRecordAreUnchanged()
    {
        (FakeAgencyOsApi api, Guid dealId, Guid[] offers) = Negotiation();
        string recorded = $"Answered by offer {offers[2]}.";

        api.DealHistory.Add(History("OfferAccepted", "Inbound offer accepted", null, offers[2], 9));
        api.DealHistory.Add(History(DealActivityLine.AnsweredByCounter, "Outbound offer superseded by a later offer", recorded, offers[1], 5));
        api.DealHistory.Add(History("OfferOpened", "Inbound offer recorded", null, offers[0], 1));

        DealDetailViewModel view = new(api);

        await view.LoadAsync(dealId);

        Assert.Equal(api.DealHistory.Select(x => (x.OccurredAt, x.Summary)), view.History.Select(x => (x.OccurredAt, x.Summary)));
        Assert.Equal("Answered by offer 3 (inbound).", view.History[1].Detail);
        Assert.Equal(recorded, api.DealHistory[1].Detail);
    }

    internal static (FakeAgencyOsApi Api, Guid DealId, Guid[] Offers) Negotiation()
    {
        FakeAgencyOsApi api = new();
        Guid dealId = Guid.NewGuid();
        Guid[] offers = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        api.Deals.Add(FakeAgencyOsApi.Deal("Harbour Lights - Maren", status: "TermsAgreed", id: dealId));
        api.Offers[dealId] =
        [
            FakeAgencyOsApi.Offer(offers[0], dealId, "Inbound", "Superseded", 1),
            FakeAgencyOsApi.Offer(offers[1], dealId, "Outbound", "Superseded", 2, respondsTo: offers[0]),
            FakeAgencyOsApi.Offer(offers[2], dealId, "Inbound", "Accepted", 3, respondsTo: offers[1]),
        ];

        return (api, dealId, offers);
    }

    internal static DealHistoryEntryResponse History(string kind, string summary, string? detail, Guid? offerId, int minute = 0) =>
        new(
            new DateTimeOffset(2026, 9, 2, 10, minute, 0, TimeSpan.Zero),
            kind,
            summary,
            detail,
            offerId,
            "Review Owner");
}
