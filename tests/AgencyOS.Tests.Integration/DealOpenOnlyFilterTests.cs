using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Deals;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Asking the negotiation list for work that is still live.
/// </summary>
/// <remarks>
/// <para>
/// <c>openOnly</c> means <see cref="Deal.LiveStatuses"/> — <c>Draft</c>,
/// <c>Negotiating</c>, <c>TermsAgreed</c> — and the set is read from the domain
/// rather than repeated, here or in the query.
/// </para>
/// <para>
/// The operational-alpha evaluation found the Deals workspace defaulting to
/// <c>Negotiating</c> alone, so a deal that reached <c>TermsAgreed</c> vanished from
/// it. The API had no way to say "still being worked", which is what this adds.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class DealOpenOnlyFilterTests
{
    private readonly AgencyOsTestFixture _fixture;

    public DealOpenOnlyFilterTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>Every live status is included, and it is the domain's set.</summary>
    [Theory]
    [InlineData("Draft")]
    [InlineData("Negotiating")]
    [InlineData("TermsAgreed")]
    public async Task OpenOnlyIncludesEveryLiveStatus(string status)
    {
        Assert.Contains(
            Deal.LiveStatuses,
            x => string.Equals(x.ToString(), status, StringComparison.Ordinal));

        Scene s = await SetUpAsync($"openonly-{status.ToLowerInvariant()}");

        Guid deal = await DealAtAsync(s, status);

        Assert.Contains(await ListAsync(s, "openOnly=true"), x => x.Id == deal);
    }

    /// <summary>Every terminal status is excluded.</summary>
    [Theory]
    [InlineData("NoDeal")]
    [InlineData("Cancelled")]
    public async Task OpenOnlyExcludesEveryTerminalStatus(string status)
    {
        Assert.DoesNotContain(
            Deal.LiveStatuses,
            x => string.Equals(x.ToString(), status, StringComparison.Ordinal));

        Scene s = await SetUpAsync($"openonly-not-{status.ToLowerInvariant()}");

        Guid deal = await DealAtAsync(s, status);

        Assert.DoesNotContain(await ListAsync(s, "openOnly=true"), x => x.Id == deal);

        // and it is still reachable when the operator asks for everything
        Assert.Contains(await ListAsync(s, string.Empty), x => x.Id == deal);
    }

    /// <summary>Omitting the parameter answers exactly as it did before.</summary>
    /// <remarks>
    /// The backward-compatibility guarantee: a contract-14 caller sends no
    /// <c>openOnly</c> and must still see closed negotiations.
    /// </remarks>
    [Fact]
    public async Task OmittingTheParameterPreservesPreviousBehaviour()
    {
        Scene s = await SetUpAsync("openonly-omitted");

        Guid live = await DealAtAsync(s, "Negotiating");
        Guid closed = await DealAtAsync(s, "Cancelled");

        IReadOnlyList<DealSummaryResponse> all = await ListAsync(s, string.Empty);

        Assert.Contains(all, x => x.Id == live);
        Assert.Contains(all, x => x.Id == closed);

        Assert.Equal(
            all.Count, (await ListAsync(s, "openOnly=false")).Count);
    }

    /// <summary>An explicit status still wins, including a terminal one.</summary>
    [Fact]
    public async Task AnExplicitStatusFilterIsPreserved()
    {
        Scene s = await SetUpAsync("openonly-explicit");

        Guid negotiating = await DealAtAsync(s, "Negotiating");
        Guid agreed = await DealAtAsync(s, "TermsAgreed");

        IReadOnlyList<DealSummaryResponse> only = await ListAsync(s, "status=Negotiating");

        Assert.Contains(only, x => x.Id == negotiating);
        Assert.DoesNotContain(only, x => x.Id == agreed);
    }

    /// <summary>Live work never reaches into another tenant.</summary>
    [Fact]
    public async Task OpenOnlyCannotReachIntoAnotherTenant()
    {
        Scene mine = await SetUpAsync("openonly-tenant-a");
        Scene theirs = await SetUpAsync("openonly-tenant-b");

        Guid ours = await DealAtAsync(mine, "Negotiating");
        Guid yours = await DealAtAsync(theirs, "Negotiating");

        IReadOnlyList<DealSummaryResponse> visible = await ListAsync(mine, "openOnly=true");

        Assert.Contains(visible, x => x.Id == ours);
        Assert.DoesNotContain(visible, x => x.Id == yours);
    }

    /// <summary>
    /// Closed negotiations cannot consume the page and hide live ones.
    /// </summary>
    /// <remarks>
    /// The reason this is a server filter rather than a client one. The list orders
    /// by <c>UpdatedAt</c> descending and takes a limit, and closing a deal updates
    /// it — so the closed ones here are the newest rows and would fill a page
    /// filtered afterwards. Asking for live work with a limit of two must return the
    /// two live negotiations, not two closed ones and nothing.
    /// </remarks>
    [Fact]
    public async Task ClosedNegotiationsDoNotDisplaceLiveOnesWithinTheLimit()
    {
        Scene s = await SetUpAsync("openonly-displacement");

        Guid liveA = await DealAtAsync(s, "Negotiating");
        Guid liveB = await DealAtAsync(s, "TermsAgreed");

        // Newer than both, because closing is a mutation.
        foreach (int _ in Enumerable.Range(0, 3))
        {
            await DealAtAsync(s, "Cancelled");
        }

        IReadOnlyList<DealSummaryResponse> page = await ListAsync(s, "openOnly=true&limit=2");

        Assert.Equal(2, page.Count);
        Assert.Contains(page, x => x.Id == liveA);
        Assert.Contains(page, x => x.Id == liveB);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Opens a negotiation and walks it to the status asked for.</summary>
    private static async Task<Guid> DealAtAsync(Scene s, string status)
    {
        Guid targetId = await AddTargetAsync(s);

        DealDetailResponse deal = await CreatedAsync<DealDetailResponse>(
            s.Client.PostAsJsonAsync(
                $"{s.Root}/deals",
                new CreateDealRequest(
                    s.OpportunityId, targetId, $"Negotiation {Guid.NewGuid():N}",
                    "ProjectSale", s.OwnerUserId)));

        Guid id = deal.Deal.Id;

        if (status == "Draft")
        {
            return id;
        }

        // An offer moves a draft into negotiation.
        await CreatedAsync<RecordOfferResponse>(s.Client.PostAsJsonAsync(
            $"{s.Root}/deals/{id}/offers",
            new RecordOfferRequest(
                "Inbound",
                [
                    new OfferTermRequest(
                        "Fee", new TermValueRequest("Money", Amount: 100_000m, Currency: "USD")),
                ],
                deal.Deal.Version)));

        if (status == "Negotiating")
        {
            return id;
        }

        if (status == "TermsAgreed")
        {
            DealDetailResponse current = await GetAsync<DealDetailResponse>(
                s.Client, $"{s.Root}/deals/{id}");

            OfferResponse offer = await GetAsync<OfferResponse>(
                s.Client, $"{s.Root}/offers/{current.OpenOffer!.Id}");

            using HttpResponseMessage answered = await s.Client.PostAsJsonAsync(
                $"{s.Root}/offers/{offer.Id}/answer",
                new AnswerOfferRequest("Accept", offer.Version));

            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

            return id;
        }

        DealDetailResponse toClose = await GetAsync<DealDetailResponse>(
            s.Client, $"{s.Root}/deals/{id}");

        using HttpResponseMessage closed = await s.Client.PostAsJsonAsync(
            $"{s.Root}/deals/{id}/close",
            new CloseDealRequest(status, toClose.Deal.Version, Reason: "Synthetic."));

        Assert.True(
            closed.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
            $"Closing to {status} answered {(int)closed.StatusCode}: "
                + await closed.Content.ReadAsStringAsync());

        return id;
    }

    private static async Task<Guid> AddTargetAsync(Scene s)
    {
        // A company is a target of an opportunity once, so each negotiation gets
        // its own counterparty rather than competing for one.
        CompanyDetailResponse buyer = await CreatedAsync<CompanyDetailResponse>(
            s.Client.PostAsJsonAsync(
                $"{s.Root}/companies",
                new CreateCompanyRequest($"Buyer {Guid.NewGuid():N}", Type: "Studio")));

        OpportunityDetailResponse current = await GetAsync<OpportunityDetailResponse>(
            s.Client, $"{s.Root}/opportunities/{s.OpportunityId}");

        Guid targetId = await CreatedIdAsync(s.Client.PostAsJsonAsync(
            $"{s.Root}/opportunities/{s.OpportunityId}/targets",
            new AddOpportunityTargetRequest(
                current.Opportunity.Version, CompanyId: buyer.Company.Id)));

        foreach (string stage in (string[])["Approved", "Contacted", "Engaged", "Interested"])
        {
            OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
                s.Client, $"{s.Root}/opportunity-targets/{targetId}");

            await NoContentAsync(s.Client.PostAsJsonAsync(
                $"{s.Root}/opportunity-targets/{targetId}/stage",
                new MoveOpportunityTargetRequest(stage, target.Version)));
        }

        return targetId;
    }

    private static async Task<IReadOnlyList<DealSummaryResponse>> ListAsync(
        Scene s, string query)
    {
        using HttpResponseMessage response = await s.Client.GetAsync(
            query.Length == 0 ? $"{s.Root}/deals" : $"{s.Root}/deals?{query}");

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"'{query}' answered {(int)response.StatusCode}.");

        return (await response.Content.ReadFromJsonAsync<List<DealSummaryResponse>>())!;
    }

    private async Task<Scene> SetUpAsync(string label)
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
                    "The Undertow to market", "ProjectMarket", actor.User.Id.Value,
                    Subjects:
                    [new OpportunitySubjectRequest("Project", project.Project.Id, "Primary")])));

        await NoContentAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunity.Opportunity.Id}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));

        return new Scene(
            client, root, opportunity.Opportunity.Id, studio.Company.Id, actor.User.Id.Value);
    }

    private sealed record Scene(
        HttpClient Client, string Root, Guid OpportunityId, Guid CompanyId, Guid OwnerUserId);

    private sealed record CreatedId(Guid Id);

    private static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
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
