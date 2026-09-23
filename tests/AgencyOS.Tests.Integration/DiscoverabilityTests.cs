using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// An operator who knows a person's name can find the pursuit about them.
/// </summary>
/// <remarks>
/// <para>
/// F-06. An opportunity exists to place a human, and it was the one commercial
/// surface that could not be found by that human's name. The deal it becomes and
/// the contract that follows both could, through joins <c>DealQueries</c> and
/// <c>ContractQueries</c> already write for exactly this purpose. An operator who
/// knew only the client's name could find the deal but not the pursuit that
/// produced it, and nothing recorded a decision to make it so.
/// </para>
/// <para>
/// <strong>Every test here pairs a positive with a negative.</strong> A search
/// that matched everything would satisfy the positives alone, so each fixture
/// seeds a person who is on the pursuit and a person who is not, and asserts the
/// second finds nothing. Discovering a record through a name it has no
/// relationship to is worse than not discovering it.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class DiscoverabilityTests
{
    private readonly AgencyOsTestFixture _fixture;

    public DiscoverabilityTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    private const string Subject = "Ravensworth Adeyemi-Lindqvist";
    private const string Contact = "Evander Quillon-Mbeki";
    private const string Approached = "Thessaly Vane";
    private const string Stranger = "Bartholomew Pemberton-Featherstonehaugh";
    private const string Studio = "Corvid Ash Pictures";

    /// <summary>
    /// Each canonical relationship an opportunity has to a human or a company.
    /// </summary>
    /// <remarks>
    /// The clue an operator would actually hold: the client they represent, the
    /// company they are pitching, the individual they deal with there, or a person
    /// approached directly. All four are relationships the pursuit records.
    /// </remarks>
    [Theory]
    [InlineData(Subject)]
    [InlineData(Studio)]
    [InlineData(Contact)]
    [InlineData(Approached)]
    public async Task APursuitIsFoundByEachIdentityItConcerns(string clue)
    {
        Fixture f = await SetUpAsync($"f06-{clue[..6]}");

        OpportunitySummaryResponse[] found = await SearchAsync(f, clue);

        Assert.Equal(f.OpportunityId, Assert.Single(found).Id);
    }

    /// <summary>
    /// Somebody with no relationship to the pursuit does not find it.
    /// </summary>
    /// <remarks>
    /// The assertion that makes the ones above mean something. This person exists
    /// in the same tenant and is on nothing.
    /// </remarks>
    [Fact]
    public async Task APursuitIsNotFoundByAnIdentityItDoesNotConcern()
    {
        Fixture f = await SetUpAsync("f06-stranger");

        Assert.Empty(await SearchAsync(f, Stranger));
    }

    /// <summary>
    /// Searching what the pursuit is called still works.
    /// </summary>
    /// <remarks>
    /// The behaviour that existed before the relationships were added, pinned so
    /// widening the search cannot quietly narrow it.
    /// </remarks>
    [Theory]
    [InlineData("Autumn slate")]
    [InlineData("autumn SLATE")]
    public async Task APursuitIsStillFoundByItsOwnName(string clue)
    {
        Fixture f = await SetUpAsync($"f06-own-{clue.Length}");

        Assert.Equal(f.OpportunityId, Assert.Single(await SearchAsync(f, clue)).Id);
    }

    /// <summary>
    /// Matching stays case-insensitive and substring-based, as it already was.
    /// </summary>
    /// <remarks>
    /// The existing surfaces use <c>ILike</c> with a pattern wrapped in wildcards.
    /// A name fragment is what an operator types, and requiring an exact name here
    /// would make this surface behave unlike every other one.
    /// </remarks>
    [Theory]
    [InlineData("ravensworth")]
    [InlineData("QUILLON")]
    [InlineData("Ash Pict")]
    public async Task AFragmentOfANameIsEnough(string clue)
    {
        Fixture f = await SetUpAsync($"f06-frag-{clue.Length}");

        Assert.Equal(f.OpportunityId, Assert.Single(await SearchAsync(f, clue)).Id);
    }

    /// <summary>
    /// An empty search returns what it always did.
    /// </summary>
    [Fact]
    public async Task AnEmptySearchStillListsEverything()
    {
        Fixture f = await SetUpAsync("f06-empty");

        Assert.Single(await SearchAsync(f, string.Empty));
    }

    /// <summary>
    /// A name is not matched out of prose the operator cannot see.
    /// </summary>
    /// <remarks>
    /// Strategy notes stay excluded: a caller without the grant must not be able
    /// to confirm what a note says by searching for a phrase in it. Widening the
    /// search to relationships must not widen it to text.
    /// </remarks>
    [Fact]
    public async Task AStrategyNoteIsStillNotSearchable()
    {
        Fixture f = await SetUpAsync("f06-notes");

        Assert.Empty(await SearchAsync(f, "Northgate last"));
    }

    // ------------------------------------------------------------- plumbing

    private sealed record Fixture(HttpClient Client, string Root, Guid OpportunityId);

    private static async Task<OpportunitySummaryResponse[]> SearchAsync(
        Fixture f, string search) =>
        await GetAsync<OpportunitySummaryResponse[]>(
            f.Client, $"{f.Root}/opportunities?search={Uri.EscapeDataString(search)}");

    private async Task<Fixture> SetUpAsync(string label)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, label);
        HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = $"/api/v1/organizations/{actor.Organization.Id.Value}";

        Guid subject = await PersonAsync(client, root, Subject);
        Guid contact = await PersonAsync(client, root, Contact);
        Guid approached = await PersonAsync(client, root, Approached);

        // Present in the tenant, on nothing. The negative case depends on them.
        await PersonAsync(client, root, Stranger);

        TalentDetailResponse talent = await CreatedAsync<TalentDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/talent",
                new CreateTalentProfileRequest(subject, "Established", Disciplines: ["Writer"])));

        CompanyDetailResponse studio = await CreatedAsync<CompanyDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/companies", new CreateCompanyRequest(Studio, Type: "Studio")));

        OpportunityDetailResponse opportunity = await CreatedAsync<OpportunityDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/opportunities",
                new CreateOpportunityRequest(
                    "Autumn slate - lead role",
                    "TalentEngagement",
                    actor.User.Id.Value,
                    StrategyNotes: "Northgate last; they owe us and will wait.",
                    Subjects:
                    [
                        new OpportunitySubjectRequest(
                            "TalentProfile", talent.Talent.Id, "Primary"),
                    ])));

        Guid id = opportunity.Opportunity.Id;
        int version = opportunity.Opportunity.Version;

        // A company target with the individual dealt with there.
        await CreatedIdAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{id}/targets",
            new AddOpportunityTargetRequest(
                version, CompanyId: studio.Company.Id, ContactPersonId: contact)));

        version = (await GetAsync<OpportunityDetailResponse>(
            client, $"{root}/opportunities/{id}")).Opportunity.Version;

        // And a person approached directly, which carries no contact.
        await CreatedIdAsync(client.PostAsJsonAsync(
            $"{root}/opportunities/{id}/targets",
            new AddOpportunityTargetRequest(version, PersonId: approached)));

        return new Fixture(client, root, id);
    }

    private static async Task<Guid> PersonAsync(HttpClient client, string root, string name)
    {
        string[] parts = name.Split(' ', 2);

        PersonDetailResponse person = await CreatedAsync<PersonDetailResponse>(
            client.PostAsJsonAsync(
                $"{root}/people",
                new CreatePersonRequest(parts[0], parts.Length > 1 ? parts[1] : null)));

        return person.Person.Id;
    }

    private static async Task<T> CreatedAsync<T>(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Guid> CreatedIdAsync(Task<HttpResponseMessage> pending)
    {
        using HttpResponseMessage response = await pending;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string uri)
    {
        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed record IdResponse(Guid Id);
}
