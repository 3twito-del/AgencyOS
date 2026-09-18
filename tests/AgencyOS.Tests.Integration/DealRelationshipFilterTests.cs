using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Narrowing negotiations by the thing they are about, or the party they are with.
/// </summary>
/// <remarks>
/// <para>
/// The operational-alpha evaluation found all four of these answering <c>500</c> on
/// ALPHA 0.1.0 (<c>d8a8bc56</c>): each ran a correlated <c>Any()</c> comparing
/// converted strongly-typed identifiers and unwrapping a nullable one through
/// <c>.Value.Value</c>, which EF Core could not translate. The whole suite passed
/// anyway, because nothing asked these questions.
/// </para>
/// <para>
/// They are the questions the model exists to answer — *what is this client working
/// on*, *what are we doing with this company* — so they are covered as a set, each
/// with a no-match and a tenant case, rather than one happy path.
/// </para>
/// <para>
/// The scenario carries two negotiations on one opportunity, because the two filter
/// families read different places: the subject filters read the opportunity, so both
/// deals match, while the counterparty filters read the target, so exactly one does.
/// A scenario with a single deal would pass while the filters crossed over.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class DealRelationshipFilterTests
{
    private readonly AgencyOsTestFixture _fixture;

    public DealRelationshipFilterTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    private static readonly string[] AllFilters =
        ["talentProfileId", "projectId", "counterpartyCompanyId", "counterpartyPersonId"];

    public static TheoryData<string> Filters() => [.. AllFilters];

    /// <summary>Each filter finds the negotiation it describes.</summary>
    [Theory]
    [MemberData(nameof(Filters))]
    public async Task AFilterFindsTheNegotiation(string filter)
    {
        Scenario s = await SetUpAsync($"dealfilter-match-{filter}");

        IReadOnlyList<DealSummaryResponse> deals =
            await QueryAsync(s, $"{filter}={s.Value(filter)}");

        Assert.Contains(deals, x => x.Id == s.Expected(filter));
    }

    /// <summary>
    /// A counterparty filter returns only the negotiation with that counterparty.
    /// </summary>
    /// <remarks>
    /// The discriminating case. Both deals hang off one opportunity, so a
    /// counterparty filter that accidentally read the opportunity would return two.
    /// </remarks>
    [Theory]
    [InlineData("counterpartyCompanyId")]
    [InlineData("counterpartyPersonId")]
    public async Task ACounterpartyFilterReturnsOnlyThatCounterpartysNegotiation(string filter)
    {
        Scenario s = await SetUpAsync($"dealfilter-only-{filter}");

        DealSummaryResponse only = Assert.Single(await QueryAsync(s, $"{filter}={s.Value(filter)}"));

        Assert.Equal(s.Expected(filter), only.Id);
    }

    /// <summary>
    /// A subject filter returns every negotiation pursuing that subject.
    /// </summary>
    [Theory]
    [InlineData("talentProfileId")]
    [InlineData("projectId")]
    public async Task ASubjectFilterReturnsEveryNegotiationAboutIt(string filter)
    {
        Scenario s = await SetUpAsync($"dealfilter-subject-{filter}");

        IReadOnlyList<DealSummaryResponse> deals = await QueryAsync(s, $"{filter}={s.Value(filter)}");

        Assert.Equal(2, deals.Count);
        Assert.Contains(deals, x => x.Id == s.CompanyDealId);
        Assert.Contains(deals, x => x.Id == s.PersonDealId);
    }

    /// <summary>An identifier nothing is about answers with an empty list, not a refusal.</summary>
    [Theory]
    [MemberData(nameof(Filters))]
    public async Task AFilterThatMatchesNothingIsEmpty(string filter)
    {
        Scenario s = await SetUpAsync($"dealfilter-empty-{filter}");

        Assert.Empty(await QueryAsync(s, $"{filter}={Guid.NewGuid()}"));
    }

    /// <summary>A filter naming another tenant's record never reaches their negotiation.</summary>
    [Theory]
    [MemberData(nameof(Filters))]
    public async Task AFilterCannotReachIntoAnotherTenant(string filter)
    {
        Scenario mine = await SetUpAsync($"dealfilter-tenant-a-{filter}");
        Scenario theirs = await SetUpAsync($"dealfilter-tenant-b-{filter}");

        Assert.Empty(await QueryAsync(mine, $"{filter}={theirs.Value(filter)}"));
    }

    /// <summary>
    /// A relationship filter combines with the status and kind filters.
    /// </summary>
    /// <remarks>
    /// The representative combination: the two filter families are built by
    /// different code paths and the defect was in only one of them.
    /// </remarks>
    [Fact]
    public async Task ARelationshipFilterCombinesWithStatusAndKind()
    {
        Scenario s = await SetUpAsync("dealfilter-combined");

        DealSummaryResponse only = Assert.Single(await QueryAsync(
            s, $"projectId={s.ProjectId}&kind=ProjectSale"));

        Assert.Equal(s.CompanyDealId, only.Id);

        Assert.Empty(await QueryAsync(s, $"projectId={s.ProjectId}&status=TermsAgreed"));
    }

    /// <summary>Every relationship filter answers rather than failing.</summary>
    /// <remarks>
    /// The guard for the defect class itself. A <c>500</c> from any of these is the
    /// regression, whatever the cause.
    /// </remarks>
    [Fact]
    public async Task NoRelationshipFilterFails()
    {
        Scenario s = await SetUpAsync("dealfilter-answers");

        foreach (string filter in AllFilters)
        {
            using HttpResponseMessage response = await s.Client.GetAsync(
                $"{s.Root}/deals?{filter}={s.Value(filter)}");

            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"'{filter}' answered {(int)response.StatusCode}.");
        }
    }

    // ------------------------------------------------------------------ set-up

    private async Task<Scenario> SetUpAsync(string label)
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

        PersonDetailResponse buyer = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people", new CreatePersonRequest("Marguerite", "Okonjo-Lindqvist")));

        PersonDetailResponse represented = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people", new CreatePersonRequest("Tobias", "Ferreira-Nakamura")));

        TalentDetailResponse talent = await CreatedAsync<TalentDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/talent", new CreateTalentProfileRequest(represented.Person.Id)));

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/opportunities",
                new CreateOpportunityRequest(
                    "The Undertow to market",
                    "ProjectMarket",
                    actor.User.Id.Value,
                    Description: "Take the feature out to studios.",
                    Subjects:
                    [
                        new OpportunitySubjectRequest("Project", project.Project.Id, "Primary"),
                        new OpportunitySubjectRequest(
                            "TalentProfile", talent.Talent.Id, "Supporting"),
                    ])));

        Guid opportunityId = opportunity.Opportunity.Id;

        await NoContentAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunityId}/status",
            new ChangeOpportunityStatusRequest("Active", opportunity.Opportunity.Version)));

        // A target is a company or a person, never both, so the two counterparty
        // filters need a target each.
        Guid companyTarget = await AddTargetAsync(
            client, root, opportunityId, companyId: studio.Company.Id);

        Guid personTarget = await AddTargetAsync(
            client, root, opportunityId, personId: buyer.Person.Id);

        await WalkToInterestedAsync(client, root, companyTarget);
        await WalkToInterestedAsync(client, root, personTarget);

        Guid companyDeal = await OpenDealAsync(
            client, root, opportunityId, companyTarget, "ProjectSale",
            "The Undertow to Northgate", actor.User.Id.Value);

        Guid personDeal = await OpenDealAsync(
            client, root, opportunityId, personTarget, "Services",
            "The Undertow with Marguerite", actor.User.Id.Value);

        return new Scenario(
            client, root, companyDeal, personDeal, project.Project.Id,
            talent.Talent.Id, studio.Company.Id, buyer.Person.Id);
    }

    private static async Task<Guid> OpenDealAsync(
        HttpClient client, string root, Guid opportunityId, Guid targetId,
        string kind, string name, Guid ownerUserId)
    {
        DealDetailResponse deal = await CreatedAsync<DealDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/deals",
                new CreateDealRequest(opportunityId, targetId, name, kind, ownerUserId)));

        return deal.Deal.Id;
    }

    private static async Task<Guid> AddTargetAsync(
        HttpClient client, string root, Guid opportunityId,
        Guid? companyId = null, Guid? personId = null)
    {
        OpportunityDetailResponse current = await GetAsync<OpportunityDetailResponse>(
            client, $"{root}/opportunities/{opportunityId}");

        return await CreatedIdAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{opportunityId}/targets",
            new AddOpportunityTargetRequest(
                current.Opportunity.Version, CompanyId: companyId, PersonId: personId)));
    }

    private static async Task WalkToInterestedAsync(
        HttpClient client, string root, Guid targetId)
    {
        foreach (string stage in (string[])["Approved", "Contacted", "Engaged", "Interested"])
        {
            OpportunityTargetResponse target = await GetAsync<OpportunityTargetResponse>(
                client, $"{root}/opportunity-targets/{targetId}");

            await NoContentAsync(client.PostAsJsonAsync(
                $"{root}/opportunity-targets/{targetId}/stage",
                new MoveOpportunityTargetRequest(stage, target.Version)));
        }
    }

    private static async Task<IReadOnlyList<DealSummaryResponse>> QueryAsync(
        Scenario s, string query)
    {
        using HttpResponseMessage response = await s.Client.GetAsync($"{s.Root}/deals?{query}");

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"'{query}' answered {(int)response.StatusCode}.");

        return (await response.Content.ReadFromJsonAsync<List<DealSummaryResponse>>())!;
    }

    private sealed record Scenario(
        HttpClient Client,
        string Root,
        Guid CompanyDealId,
        Guid PersonDealId,
        Guid ProjectId,
        Guid TalentProfileId,
        Guid CompanyId,
        Guid PersonId)
    {
        /// <summary>The identifier this filter is given.</summary>
        public Guid Value(string filter) => filter switch
        {
            "talentProfileId" => TalentProfileId,
            "projectId" => ProjectId,
            "counterpartyCompanyId" => CompanyId,
            "counterpartyPersonId" => PersonId,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown filter."),
        };

        /// <summary>The negotiation this filter must return.</summary>
        public Guid Expected(string filter) => filter switch
        {
            "counterpartyPersonId" => PersonDealId,
            _ => CompanyDealId,
        };
    }

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
