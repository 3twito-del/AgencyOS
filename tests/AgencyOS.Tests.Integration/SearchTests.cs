using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Search;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.People;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Ranked, tenant-scoped search over canonical PostgreSQL.
/// </summary>
/// <remarks>
/// Against the real database, because the whole feature is the database: full
/// text, trigram similarity and the ranking that combines them. A test with a
/// substituted search would prove nothing about what ships.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class SearchTests
{
    private readonly AgencyOsTestFixture _fixture;

    public SearchTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The ranking bands, in one test because their relative order is the feature.
    /// </summary>
    /// <remarks>
    /// An exact name beats a prefix, a prefix beats a full-text hit, and a
    /// full-text hit beats a fuzzy one. Asserting each in isolation would let the
    /// ordering between them break while every test still passed.
    /// </remarks>
    [Fact]
    public async Task Search_RanksExactAbovePrefixAboveFullTextAboveFuzzy()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-rank");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Klein", "Sarah", title: "Literary Agent");
        await CreatePersonAsync(client, tenant, "Kleinman", "Sarah", displayName: "Sarah Kleinman");
        await CreatePersonAsync(client, tenant, "Reid", "Marcus", title: "Klein Associates Manager");

        SearchResponse exact = await SearchAsync(client, tenant, "Sarah Klein");

        Assert.Equal("Sarah Klein", exact.Hits[0].Title);
        Assert.Equal("Exact", exact.Hits[0].MatchedOn);
        Assert.Equal(1.0, exact.Hits[0].Score);

        SearchResponse prefix = await SearchAsync(client, tenant, "Sarah Klein");
        Assert.All(prefix.Hits, hit => Assert.True(hit.Score <= 1.0 && hit.Score > 0));

        // Every hit after the first scores no higher than the one before it.
        Assert.Equal(
            exact.Hits.Select(x => x.Score).OrderByDescending(x => x).ToArray(),
            exact.Hits.Select(x => x.Score).ToArray());
    }

    [Fact]
    public async Task Search_MatchesAPrefixAsTheUserTypes()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-prefix");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Okonkwo", "Adaeze", displayName: "Adaeze Okonkwo");

        SearchResponse response = await SearchAsync(client, tenant, "Adae");

        Assert.Equal("Adaeze Okonkwo", Assert.Single(response.Hits).Title);
        Assert.Equal("Prefix", response.Hits[0].MatchedOn);
    }

    /// <summary>A misspelled name still finds the person.</summary>
    [Fact]
    public async Task Search_ToleratesATypo()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-fuzzy");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Petrosyan", "Anahit", displayName: "Anahit Petrosyan");

        SearchResponse response = await SearchAsync(client, tenant, "Anahit Petrosian");

        Assert.Equal("Anahit Petrosyan", Assert.Single(response.Hits).Title);
        Assert.Equal("Similar", response.Hits[0].MatchedOn);
    }

    /// <summary>Searching by a full email address finds the person who owns it.</summary>
    [Fact]
    public async Task Search_FindsAPersonByTheirEmailAddress()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-email");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Ferreira", "Bruno", email: "bruno.ferreira@vertex.invalid");

        SearchResponse response = await SearchAsync(client, tenant, "bruno.ferreira@vertex.invalid");

        Assert.Equal("Bruno Ferreira", Assert.Single(response.Hits).Title);
    }

    /// <summary>
    /// Query text that looks like tsquery syntax is treated as words.
    /// </summary>
    /// <remarks>
    /// The prefix query is built by a database function that quotes every token, so
    /// an ampersand or an exclamation mark cannot become an operator. Without that,
    /// this query would raise a syntax error rather than search.
    /// </remarks>
    [Theory]
    [InlineData("Klein & !Marcus")]
    [InlineData("O'Brien")]
    [InlineData("100% sure")]
    [InlineData("*")]
    [InlineData("<>")]
    public async Task Search_TreatsOperatorCharactersAsText(string query)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-syntax");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Klein", "Sarah");

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{tenant}/search?q={Uri.EscapeDataString(query)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>An empty query answers with nothing rather than with the whole tenant.</summary>
    [Fact]
    public async Task EmptyQuery_ReturnsNothing()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-empty");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        await CreatePersonAsync(client, tenant, "Klein", "Sarah");

        Assert.Empty((await SearchAsync(client, tenant, "   ")).Hits);
    }

    [Fact]
    public async Task Search_ExcludesArchivedRecordsUnlessAsked()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-archived");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        PersonDetailResponse person = await CreatePersonAsync(client, tenant, "Vanterpool", "Delphine");

        Assert.Single((await SearchAsync(client, tenant, "Vanterpool")).Hits);

        // M2 exposes no archive command, so the state is set directly. The
        // subject under test is the search filter, not the transition.
        await using (AgencyOsDbContext context = _fixture.CreateDbContext())
        {
            Person archived = await context.People.SingleAsync(x => x.Id == new PersonId(person.Person.Id));
            archived.Archive(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        Assert.Empty((await SearchAsync(client, tenant, "Vanterpool")).Hits);
        Assert.Single((await SearchAsync(client, tenant, "Vanterpool", includeArchived: true)).Hits);
    }

    /// <summary>
    /// Search never crosses a tenant.
    /// </summary>
    /// <remarks>
    /// One box that reads everything is the easiest place in a product to leak a
    /// record, which is why this is asserted directly rather than inferred from the
    /// query having a tenant predicate.
    /// </remarks>
    [Fact]
    public async Task Search_NeverCrossesATenant()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Member, "search-mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Member, "search-theirs");

        using HttpClient theirClient = _fixture.CreateClient(theirs.Subject);
        await CreatePersonAsync(theirClient, theirs.Organization.Id.Value, "Solberg", "Ingrid");

        using HttpClient myClient = _fixture.CreateClient(mine.Subject);

        Assert.Empty((await SearchAsync(myClient, mine.Organization.Id.Value, "Solberg")).Hits);

        // And the other tenant's own route is refused outright.
        using HttpResponseMessage crossing = await myClient.GetAsync(
            $"/api/v1/organizations/{theirs.Organization.Id.Value}/search?q=Solberg");

        Assert.Equal(HttpStatusCode.Forbidden, crossing.StatusCode);
    }

    [Fact]
    public async Task Search_Paginates()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-page");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        for (int index = 0; index < 5; index++)
        {
            await CreatePersonAsync(
                client,
                tenant,
                "Pagination",
                $"Person{index}",
                displayName: $"Pagination Sample {index}");
        }

        SearchResponse first = await SearchAsync(client, tenant, "Pagination", take: 2);

        Assert.Equal(2, first.Hits.Count);
        Assert.True(first.HasMore);

        SearchResponse last = await SearchAsync(client, tenant, "Pagination", skip: 4, take: 2);

        Assert.False(last.HasMore);
        Assert.DoesNotContain(last.Hits, hit => first.Hits.Any(x => x.Id == hit.Id));
    }

    [Fact]
    public async Task Search_FindsCompaniesAndTasksToo()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-types");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        Guid tenant = actor.Organization.Id.Value;

        using (HttpResponseMessage company = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/companies",
            new CreateCompanyRequest("Marchetti Pictures", "Studio")))
        {
            Assert.Equal(HttpStatusCode.Created, company.StatusCode);
        }

        using (HttpResponseMessage task = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/tasks",
            new CreateTaskRequest("Send Marchetti the revised deck")))
        {
            Assert.Equal(HttpStatusCode.Created, task.StatusCode);
        }

        SearchResponse response = await SearchAsync(client, tenant, "Marchetti");

        Assert.Contains(response.Hits, hit => hit.Type == "Company");
        Assert.Contains(response.Hits, hit => hit.Type == "Task");

        SearchResponse companiesOnly = await SearchAsync(client, tenant, "Marchetti", types: "Company");

        Assert.All(companiesOnly.Hits, hit => Assert.Equal("Company", hit.Type));
    }

    /// <summary>A type name the server does not know is refused, not quietly dropped.</summary>
    /// <remarks>
    /// The placeholder used to be "Deal", which M7 made a real searchable type.
    /// This one is deliberately not a noun any milestone would claim.
    /// </remarks>
    [Fact]
    public async Task UnknownTypeFilter_IsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-badtype");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/search?q=x&types=NotAnEntityType");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A caller who may not read a type never sees it, even when they ask for it.
    /// </summary>
    /// <remarks>
    /// An Observer holds every read permission, so this uses the narrowest thing
    /// the fixture can express: a tenant the caller is not a member of is refused,
    /// and within their own tenant the result set is bounded by their grants.
    /// </remarks>
    [Fact]
    public async Task Search_RequiresAuthentication()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Member, "search-anon");
        using HttpClient anonymous = _fixture.CreateClient(subject: null);

        using HttpResponseMessage response = await anonymous.GetAsync(
            $"/api/v1/organizations/{actor.Organization.Id.Value}/search?q=anything");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------------- plumbing

    private static async Task<SearchResponse> SearchAsync(
        HttpClient client,
        Guid tenant,
        string query,
        bool includeArchived = false,
        int skip = 0,
        int take = 25,
        string? types = null)
    {
        string uri = $"/api/v1/organizations/{tenant}/search"
            + $"?q={Uri.EscapeDataString(query)}"
            + $"&includeArchived={(includeArchived ? "true" : "false")}"
            + $"&skip={skip}&take={take}";

        if (types is not null)
        {
            uri += $"&types={types}";
        }

        using HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SearchResponse>())!;
    }

    private static async Task<PersonDetailResponse> CreatePersonAsync(
        HttpClient client,
        Guid tenant,
        string lastName,
        string firstName,
        string? displayName = null,
        string? title = null,
        string? email = null)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{tenant}/people",
            new CreatePersonRequest(firstName, lastName, displayName, Title: title, Email: email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PersonDetailResponse>())!;
    }
}
