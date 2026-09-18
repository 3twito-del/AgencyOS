using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Deals;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// What the Deals workspace asks for before anybody touches a filter.
/// </summary>
/// <remarks>
/// <para>
/// The operational-alpha evaluation opened the workspace live and found one row
/// where the API held three deals: the status filter defaulted to
/// <c>Negotiating</c>, so a negotiation that reached <c>TermsAgreed</c> — agreed,
/// waiting to be papered — was invisible in the workspace named after it.
/// </para>
/// <para>
/// The default now asks the server for live work. These tests are about the
/// request the view model makes, because that is where the defect was: filtering
/// after the fetch would be wrong whatever the list then showed, since the server
/// limits the page before the client sees it.
/// </para>
/// </remarks>
public sealed class DealWorkspaceDefaultTests
{
    /// <summary>By default the workspace asks for live work and no status.</summary>
    [Fact]
    public async Task TheDefaultViewAsksForLiveWork()
    {
        FakeAgencyOsApi api = new();
        DealListViewModel list = new(api);

        await list.LoadAsync();

        Assert.True(list.OpenOnly);
        Assert.Null(api.LastDealFilter.Status);
        Assert.True(api.LastDealFilter.OpenOnly);
    }

    /// <summary>An agreed negotiation survives the default view.</summary>
    /// <remarks>The defect, stated as the behaviour it broke.</remarks>
    [Fact]
    public async Task ANegotiationThatReachesTermsAgreedStaysVisible()
    {
        FakeAgencyOsApi api = new();

        api.Deals.Add(Deal("Under discussion", "Negotiating"));
        api.Deals.Add(Deal("Agreed, awaiting paper", "TermsAgreed"));
        api.Deals.Add(Deal("Walked away", "Cancelled"));

        DealListViewModel list = new(api);

        await list.LoadAsync();

        Assert.Contains(list.Deals, x => x.Name == "Under discussion");
        Assert.Contains(list.Deals, x => x.Name == "Agreed, awaiting paper");
        Assert.DoesNotContain(list.Deals, x => x.Name == "Walked away");
    }

    /// <summary>
    /// Choosing a status asks for that status, and not for live work as well.
    /// </summary>
    /// <remarks>
    /// An explicit choice is an explicit choice: combining it with the live set
    /// would answer a question nobody asked, and would make "Cancelled" return
    /// nothing for a reason the operator could not see.
    /// </remarks>
    [Fact]
    public async Task AnExplicitStatusIsSentAloneAndNegotiatingStillWorks()
    {
        FakeAgencyOsApi api = new();
        DealListViewModel list = new(api) { Status = "Negotiating" };

        await list.LoadAsync();

        Assert.Equal("Negotiating", api.LastDealFilter.Status);
        Assert.False(api.LastDealFilter.OpenOnly);
    }

    /// <summary>Clearing the status returns to the default live view.</summary>
    [Fact]
    public async Task ClearingTheStatusReturnsToLiveWork()
    {
        FakeAgencyOsApi api = new();
        DealListViewModel list = new(api) { Status = "Cancelled" };

        await list.LoadAsync();

        Assert.False(api.LastDealFilter.OpenOnly);

        list.Status = null;

        await list.LoadAsync();

        Assert.True(api.LastDealFilter.OpenOnly);
        Assert.Null(api.LastDealFilter.Status);
    }

    /// <summary>Asking for everything asks for neither a status nor live work.</summary>
    [Fact]
    public async Task AskingForEverythingSendsNoNarrowing()
    {
        FakeAgencyOsApi api = new();
        DealListViewModel list = new(api) { OpenOnly = false };

        await list.LoadAsync();

        Assert.Null(api.LastDealFilter.Status);
        Assert.False(api.LastDealFilter.OpenOnly);
    }

    private static DealSummaryResponse Deal(string name, string status) =>
        new(
            Id: Guid.NewGuid(),
            Name: name,
            Reference: null,
            Kind: "ProjectSale",
            Status: status,
            OpportunityId: Guid.NewGuid(),
            OpportunityName: "The Undertow to market",
            OpportunityTargetId: Guid.NewGuid(),
            CounterpartyDisplayName: "Northgate Pictures",
            CounterpartyCompanyId: Guid.NewGuid(),
            CounterpartyPersonId: null,
            SubjectDisplayName: "The Undertow",
            OwnerUserId: Guid.NewGuid(),
            OwnerDisplayName: "Review Owner",
            OpenedOn: DateOnly.FromDateTime(DateTime.UtcNow.Date),
            ClosedOn: null,
            OfferCount: 0,
            LatestOfferId: null,
            LatestOfferDirection: null,
            LatestOfferAt: null,
            HasOpenOffer: false,
            OpenOfferExpiresAt: null,
            AcceptedOfferId: null,
            OpenTaskCount: 0,
            NextTaskDueAt: null,
            UpdatedAt: DateTimeOffset.UtcNow,
            Version: 1);
}
