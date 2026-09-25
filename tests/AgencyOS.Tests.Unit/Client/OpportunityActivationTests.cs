using System.Net;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// A pursuit made in the Windows client can be activated there, and then worked.
/// </summary>
/// <remarks>
/// <para>
/// The fresh final-candidate RC at <c>2d2b46a</c> stopped before its operator handoff.
/// A pursuit is created as a Draft; only an Active pursuit accepts market activity -
/// a target move, then a negotiation; and nothing in the Windows client activated
/// one. The capability already existed: the status command in contract 17 and
/// <see cref="IAgencyOsApi.ChangeOpportunityStatusAsync"/>. What was missing was the
/// operator's route to it.
/// </para>
/// <para>
/// The fake refuses a target move on a Draft and a stale expected version, as the
/// server does, so these exercise the same prerequisites the live story meets.
/// </para>
/// </remarks>
public sealed class OpportunityActivationTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private static async Task<(FakeAgencyOsApi Api, Guid Id)> DraftAsync()
    {
        FakeAgencyOsApi api = new();

        OpportunityDetailResponse created = await api.CreateOpportunityAsync(
            new CreateOpportunityRequest("Maren Holloway - spring lead", "TalentEngagement", Owner));

        return (api, created.Opportunity.Id);
    }

    /// <summary>Creating a pursuit leaves it a Draft; nothing activates it on the way.</summary>
    [Fact]
    public async Task ACreatedPursuit_IsADraft_AndIsNotActivated()
    {
        (FakeAgencyOsApi api, Guid id) = await DraftAsync();

        Assert.Equal("Draft", Assert.Single(api.Opportunities, x => x.Id == id).Status);
        Assert.Empty(api.OpportunityStatusChanges);
    }

    /// <summary>
    /// The list's normal Active default hides a new Draft; widening the status - what
    /// the page's reveal does - shows it.
    /// </summary>
    [Fact]
    public async Task TheActiveDefault_HidesANewDraft_AndWideningShowsIt()
    {
        (FakeAgencyOsApi api, Guid id) = await DraftAsync();

        OpportunityListViewModel list = new(api);

        Assert.Equal("Active", list.Status);

        await list.LoadAsync();

        Assert.DoesNotContain(list.Opportunities, x => x.Id == id);

        list.Status = null;
        await list.LoadAsync();

        Assert.Equal("Draft", Assert.Single(list.Opportunities, x => x.Id == id).Status);
    }

    /// <summary>A loaded Draft offers activation.</summary>
    [Fact]
    public async Task ALoadedDraft_OffersActivation()
    {
        (FakeAgencyOsApi api, Guid id) = await DraftAsync();

        OpportunityDetailViewModel detail = new(api);

        Assert.False(detail.CanActivate);

        await detail.LoadAsync(id);

        Assert.True(detail.CanActivate);
    }

    /// <summary>An Active pursuit is not offered Draft activation.</summary>
    [Fact]
    public async Task AnActivePursuit_IsNotOfferedActivation()
    {
        FakeAgencyOsApi api = new();
        OpportunitySummaryResponse active = FakeAgencyOsApi.Opportunity("Autumn slate", "TalentEngagement", "Active");

        api.Opportunities.Add(active);

        OpportunityDetailViewModel detail = new(api);

        await detail.LoadAsync(active.Id);

        Assert.False(detail.CanActivate);
        Assert.False(await detail.ActivateAsync());
        Assert.Empty(api.OpportunityStatusChanges);
    }

    /// <summary>
    /// Activation sends exactly Active, with the version the operator saw and a key,
    /// through the existing method; then the same pursuit is reloaded as Active.
    /// </summary>
    [Fact]
    public async Task Activation_SendsActiveWithTheObservedVersion_AndReloadsTheSamePursuit()
    {
        (FakeAgencyOsApi api, Guid id) = await DraftAsync();

        await api.AddOpportunityTargetAsync(id, new AddOpportunityTargetRequest(1, CompanyId: Guid.NewGuid()));

        OpportunityDetailViewModel detail = new(api);

        await detail.LoadAsync(id);

        int observed = detail.Opportunity!.Opportunity.Version;

        Assert.True(await detail.ActivateAsync());

        (Guid sentTo, ChangeOpportunityStatusRequest request, string? key) = Assert.Single(api.OpportunityStatusChanges);

        Assert.Equal(id, sentTo);
        Assert.Equal("Active", request.Status);
        Assert.Equal(observed, request.ExpectedVersion);
        Assert.Null(request.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(key));

        // Reloaded from the server, not set locally: the same pursuit, now Active.
        Assert.Equal(id, detail.Opportunity!.Opportunity.Id);
        Assert.Equal("Active", detail.Opportunity.Opportunity.Status);
        Assert.Equal(observed + 1, detail.Opportunity.Opportunity.Version);
        Assert.StartsWith("Active - ", detail.Standing, StringComparison.Ordinal);
        Assert.Single(detail.Targets);
        Assert.False(detail.CanActivate);
        Assert.Single(api.Opportunities);
    }

    /// <summary>
    /// A version conflict is not success: it is thrown, nothing says Active, and there
    /// is no retry with a newer version.
    /// </summary>
    [Fact]
    public async Task AConflict_IsNotSuccess_AndIsNotRetried()
    {
        (FakeAgencyOsApi api, Guid id) = await DraftAsync();

        OpportunityDetailViewModel detail = new(api);

        await detail.LoadAsync(id);

        // Somebody else changed it since the operator loaded it.
        int index = api.Opportunities.FindIndex(x => x.Id == id);
        api.Opportunities[index] = api.Opportunities[index] with { Version = api.Opportunities[index].Version + 1 };

        AgencyOsApiException refused = await Assert.ThrowsAsync<AgencyOsApiException>(() => detail.ActivateAsync());

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Single(api.OpportunityStatusChanges);
        Assert.Equal("Draft", detail.Opportunity!.Opportunity.Status);
        Assert.True(detail.CanActivate);
        Assert.Equal("Draft", api.Opportunities[index].Status);
    }

    /// <summary>A refusal leaves the pursuit exactly as the server last described it.</summary>
    [Fact]
    public async Task ARefusal_LeavesTheDraftAsItWas()
    {
        (FakeAgencyOsApi api, Guid id) = await DraftAsync();

        OpportunityDetailViewModel detail = new(api);

        await detail.LoadAsync(id);

        api.Failures.Enqueue(new AgencyOsApiException(HttpStatusCode.Forbidden, "Permission denied"));

        await Assert.ThrowsAsync<AgencyOsApiException>(() => detail.ActivateAsync());

        Assert.Equal("Draft", detail.Opportunity!.Opportunity.Status);
        Assert.Equal("Draft", Assert.Single(api.Opportunities).Status);
    }

    /// <summary>
    /// The C7 blocker, end to end on one pursuit: a Draft's target cannot move; after
    /// the operator activates it, the same target moves to Interested - the stage a
    /// negotiation opens from.
    /// </summary>
    [Fact]
    public async Task ActivationUnblocksMarketActivityOnTheSamePursuit()
    {
        (FakeAgencyOsApi api, Guid id) = await DraftAsync();

        Guid target = await api.AddOpportunityTargetAsync(id, new AddOpportunityTargetRequest(1, CompanyId: Guid.NewGuid()));

        OpportunityDetailViewModel detail = new(api);

        await detail.LoadAsync(id);

        // Before: the domain's own refusal.
        AgencyOsApiException draft = await Assert.ThrowsAsync<AgencyOsApiException>(() =>
            api.MoveOpportunityTargetAsync(target, new MoveOpportunityTargetRequest("Approved", 1)));

        Assert.Equal(
            "This opportunity is still a draft. Activate it before recording market activity.",
            draft.Detail);

        // The operator's action. Its return value is not what is under test here:
        // whether the market now accepts the move is.
        await detail.ActivateAsync();

        foreach (string stage in (string[])["Approved", "Contacted", "Interested"])
        {
            OpportunityTargetResponse current = await api.GetOpportunityTargetAsync(target);

            await api.MoveOpportunityTargetAsync(target, new MoveOpportunityTargetRequest(stage, current.Version));
        }

        await detail.LoadAsync(id);

        Assert.Equal("Interested", Assert.Single(detail.Targets).Stage);
        Assert.Equal("Active", detail.Opportunity!.Opportunity.Status);
    }
}
