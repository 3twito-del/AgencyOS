using System.Net;
using System.Text.Json;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That every documented tenant list actually answers.
/// </summary>
/// <remarks>
/// <para>
/// Audit 001's second finding was an endpoint that threw on every request, on
/// every tenant, since the milestone that shipped it. It survived 751 integration
/// tests because the suite mentioned the route exactly once — as a string in
/// <c>OpenApiContractTests</c>, asserting the published document describes it
/// (<c>AOS-R001-002</c>, <c>AOS-R001-014</c>).
/// </para>
/// <para>
/// Being in the contract is not coverage. It is equally true of a route that has
/// never returned a response. This test closes that gap for the whole surface at
/// once rather than one route at a time: it reads the document the server itself
/// publishes, takes every tenant-scoped collection GET, and calls it.
/// </para>
/// <para>
/// It asserts only that nothing answers 5xx. A 4xx is a decision the endpoint made
/// — a missing grant, a filter it requires — and this test has no business having
/// an opinion about those; the suites that own each surface do. A 5xx is the
/// endpoint failing to be an endpoint, which is what went unnoticed for a
/// milestone.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class DocumentedRoutesAnswerTests
{
    /// <summary>
    /// How many such routes the surface is expected to have, at least.
    /// </summary>
    /// <remarks>
    /// A floor, not a count. The point is that a selector which silently stopped
    /// matching would otherwise make this test pass by calling nothing — the false
    /// green this suite has been bitten by before.
    /// </remarks>
    private const int ExpectedAtLeast = 50;

    private readonly AgencyOsTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DocumentedRoutesAnswerTests(AgencyOsTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task EveryDocumentedTenantListAnswersWithoutFailing()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "documented-routes");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        // Populated rather than empty. An empty tenant exercises the early returns,
        // which is exactly what hid AOS-R001-001 — the query that broke only ran
        // when somebody had an employer.
        await _fixture.SeedPeopleAsync(actor, 6, "Documented");

        IReadOnlyList<string> routes = await TenantCollectionRoutesAsync(client);

        Assert.True(
            routes.Count >= ExpectedAtLeast,
            $"Only {routes.Count} documented tenant collection routes were found, and at least "
                + $"{ExpectedAtLeast} were expected. The selector has stopped matching, so this "
                + "test is calling almost nothing and would pass whatever the surface did.");

        string tenant = actor.Organization.Id.Value.ToString();
        List<string> broken = [];

        foreach (string route in routes)
        {
            string url = route.Replace("{organizationId}", tenant, StringComparison.Ordinal);

            using HttpResponseMessage response = await client.GetAsync(url);

            if ((int)response.StatusCode >= 500)
            {
                broken.Add($"{route} -> {(int)response.StatusCode} "
                    + await response.Content.ReadAsStringAsync());
            }
        }

        _output.WriteLine($"Called {routes.Count} documented tenant collection routes.");

        Assert.True(
            broken.Count == 0,
            "These documented routes failed rather than answered:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, broken));
    }

    /// <summary>
    /// Tenant-scoped collection GETs that need no input beyond the tenant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two exclusions, both read from the contract rather than hard-coded.
    /// </para>
    /// <para>
    /// A second path parameter means the route needs a real identifier, and
    /// inventing one would test the not-found path rather than the query behind it.
    /// Those belong to the suites that create the records.
    /// </para>
    /// <para>
    /// A required query parameter means the same thing: <c>/communication-providers</c>
    /// requires a redirect URI and <c>/participant-suggestions</c> requires an
    /// address, and calling them without one measures the framework's model binding
    /// rather than the endpoint. Both were caught by the first run of this test
    /// doing exactly that.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<string>> TenantCollectionRoutesAsync(HttpClient client)
    {
        const string Prefix = "/api/v1/organizations/{organizationId}/";

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        List<string> routes = [];

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Value.TryGetProperty("get", out JsonElement get)
                || !path.Name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (path.Name[Prefix.Length..].Contains('{', StringComparison.Ordinal))
            {
                continue;
            }

            if (RequiresInput(get))
            {
                continue;
            }

            routes.Add(path.Name);
        }

        routes.Sort(StringComparer.Ordinal);

        return routes;
    }

    /// <summary>Whether the operation demands anything the caller must supply.</summary>
    private static bool RequiresInput(JsonElement operation)
    {
        if (!operation.TryGetProperty("parameters", out JsonElement parameters))
        {
            return false;
        }

        foreach (JsonElement parameter in parameters.EnumerateArray())
        {
            if (!parameter.TryGetProperty("required", out JsonElement required)
                || !required.GetBoolean())
            {
                continue;
            }

            string? name = parameter.TryGetProperty("name", out JsonElement named)
                ? named.GetString()
                : null;

            if (!string.Equals(name, "organizationId", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
