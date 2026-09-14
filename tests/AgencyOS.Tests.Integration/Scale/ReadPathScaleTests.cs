using System.Net.Http.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.People;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgencyOS.Tests.Integration.Scale;

/// <summary>
/// That reads cost the same whether a tenant is small or large.
/// </summary>
/// <remarks>
/// <para>
/// The M14 scale harness. It deliberately asserts no wall-clock figure: a hosted
/// runner's timings move for reasons that have nothing to do with this code, and a
/// flaky performance gate teaches people to ignore the gate (§53).
/// </para>
/// <para>
/// What it asserts instead is an algorithmic invariant that needs no baseline and
/// no magic number: <strong>the number of database round trips a read makes must
/// not depend on how many rows come back.</strong> That is the definition of an
/// N+1, and it is exactly the defect that stays invisible in development — where
/// every tenant has four people — and becomes the whole problem at scale.
/// </para>
/// <para>
/// Because the assertion compares two measurements of the same endpoint rather
/// than one measurement against a threshold, it cannot drift, cannot be tuned, and
/// does not need re-baselining when the schema changes.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class ReadPathScaleTests
{
    /// <summary>The documented ceiling on a people page.</summary>
    /// <remarks>
    /// <c>PeopleSliceQueryService.MaximumLimit</c>. Stated here so a change to the
    /// product's ceiling fails this test rather than silently redefining it.
    /// </remarks>
    private const int DocumentedPageCeiling = 200;

    private readonly AgencyOsTestFixture _fixture;

    public ReadPathScaleTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The instrument is connected.
    /// </summary>
    /// <remarks>
    /// Asserted first and separately. Every other test here compares query counts,
    /// and a counter that was never wired into the host would report zero for both
    /// sides of every comparison and pass — the most convincing false green
    /// available. This is the test that fails instead.
    /// </remarks>
    [Fact]
    public async Task TheHarnessIsActuallyMeasuring()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-instrument");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = Root(actor);

        QueryCounter counter = Counter();
        counter.Reset();

        using HttpResponseMessage response = await client.GetAsync($"{root}/people");

        response.EnsureSuccessStatusCode();

        Assert.True(
            counter.EverObserved,
            "The query counter was never invoked. The harness is measuring nothing, "
                + "and every comparison it makes would pass vacuously.");

        Assert.True(counter.Count > 0, "A request executed no database commands.");
    }

    /// <summary>
    /// Listing people costs the same for fifty as for five.
    /// </summary>
    /// <remarks>
    /// The N+1 invariant, stated as a comparison so it needs no baseline. If the
    /// projection ever starts loading a relationship, a company name or a task
    /// count per row, this fails immediately and says by how much.
    /// </remarks>
    [Fact]
    public async Task ListingPeopleDoesNotQueryPerRow()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-people-n1");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = Root(actor);

        await SeedPeopleAsync(actor, 5);

        // Warm: the first request through a host builds the model and opens a
        // connection, and neither is part of what this measures.
        await WarmAsync(client, $"{root}/people");

        int few = await MeasureAsync(client, $"{root}/people?limit={DocumentedPageCeiling}");

        await SeedPeopleAsync(actor, 45);

        int many = await MeasureAsync(client, $"{root}/people?limit={DocumentedPageCeiling}");

        Assert.True(
            few == many,
            $"Listing 5 people took {few} queries and listing 50 took {many}. "
                + "A read whose query count grows with its result set is an N+1.");
    }

    /// <summary>
    /// Searching costs the same whether it matches four rows or forty.
    /// </summary>
    /// <remarks>
    /// Search is the read most likely to acquire a per-hit lookup, because a result
    /// naturally wants a label from somewhere else. The corpus grows between the two
    /// measurements and the query count may not.
    /// </remarks>
    [Fact]
    public async Task SearchingDoesNotQueryPerHit()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-search-n1");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = Root(actor);

        await SeedPeopleAsync(actor, 4, "Scalewick");
        await WarmAsync(client, $"{root}/search?q=Scalewick");

        int few = await MeasureAsync(client, $"{root}/search?q=Scalewick");

        await SeedPeopleAsync(actor, 36, "Scalewick");

        int many = await MeasureAsync(client, $"{root}/search?q=Scalewick");

        Assert.True(
            few == many,
            $"Searching 4 matches took {few} queries and 40 took {many}.");
    }

    /// <summary>
    /// A caller cannot ask for the whole tenant.
    /// </summary>
    /// <remarks>
    /// The clamp is written in the query service, and this is the assertion that it
    /// is reachable through the HTTP surface rather than merely present in a
    /// constant. Seeded past the ceiling deliberately: with fewer rows than the
    /// limit, a broken clamp and a working one return the same thing (§25).
    /// </remarks>
    [Fact]
    public async Task ThePageCeilingHoldsAgainstAnAbsurdLimit()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-ceiling");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = Root(actor);

        await SeedPeopleAsync(actor, DocumentedPageCeiling + 50);

        PersonSummaryResponse[] page = (await client
            .GetFromJsonAsync<PersonSummaryResponse[]>($"{root}/people?limit=1000000"))!;

        Assert.Equal(DocumentedPageCeiling, page.Length);
    }

    /// <summary>
    /// The ceiling is a ceiling, not a fixed page size.
    /// </summary>
    /// <remarks>
    /// A clamp that ignored the caller entirely would pass the test above while
    /// making every list request as expensive as the largest one.
    /// </remarks>
    [Fact]
    public async Task ASmallerPageIsHonoured()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "m14-page");
        using HttpClient client = _fixture.CreateClient(actor.Subject);
        string root = Root(actor);

        await SeedPeopleAsync(actor, 30);

        PersonSummaryResponse[] page = (await client
            .GetFromJsonAsync<PersonSummaryResponse[]>($"{root}/people?limit=10"))!;

        Assert.Equal(10, page.Length);
    }

    // ------------------------------------------------------------- helpers

    private QueryCounter Counter() =>
        _fixture.Factory.Services.GetRequiredService<QueryCounter>();

    private static string Root(SeededActor actor) =>
        $"/api/v1/organizations/{actor.Organization.Id.Value}";

    private static async Task WarmAsync(HttpClient client, string route)
    {
        using HttpResponseMessage response = await client.GetAsync(route);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>Counts the database commands one request executed.</summary>
    private async Task<int> MeasureAsync(HttpClient client, string route)
    {
        QueryCounter counter = Counter();
        counter.Reset();

        using HttpResponseMessage response = await client.GetAsync(route);

        response.EnsureSuccessStatusCode();

        return counter.Count;
    }

    /// <summary>
    /// Adds people directly, because the harness is measuring reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Going through the API would make seeding two hundred rows the slowest part
    /// of the suite and would measure the write path, which is not what is under
    /// test here.
    /// </para>
    /// <para>
    /// Delegates to the shared fixture helper, which gives most of them an
    /// employer. Before REPAIR-001 this method created people with no
    /// <c>PrimaryCompanyId</c>, so the N+1 invariant below measured a list whose
    /// company lookup never ran — it compared two identical counts of a query that
    /// was not being executed, and would have passed however that lookup behaved
    /// (<c>AOS-R001-014</c>).
    /// </para>
    /// </remarks>
    private Task SeedPeopleAsync(SeededActor actor, int count, string surname = "Scale") =>
        _fixture.SeedPeopleAsync(actor, count, surname);
}
