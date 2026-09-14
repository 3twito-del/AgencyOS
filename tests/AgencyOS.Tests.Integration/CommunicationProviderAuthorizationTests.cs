using System.Net;
using System.Text.Json;
using AgencyOS.Contracts.Documents;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// That the provider route is scoped to the organization in its own path.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /organizations/{organizationId}/communication-providers</c> answered
/// <strong>200 to a caller who was not a member of that organization</strong>,
/// and to an organization identifier that named nothing at all. Every sibling
/// under the same group answered 403.
/// </para>
/// <para>
/// The cause was not a missing permission. <c>RequireAuthorization</c> was
/// present, and <c>PermissionAuthorizationHandler</c> is deliberately coarse: it
/// asks whether the caller holds the permission through <em>any</em> active
/// membership, passing <c>scope: null</c>, and its own remarks say the
/// authoritative organization-scoped check belongs in the handler. Every sibling
/// makes that second call. This one could not — its lambda never bound
/// <c>organizationId</c>, so there was no organization to check against.
/// </para>
/// <para>
/// The provider list is server configuration and is global by design: the
/// registry is a singleton built from deployment settings, and nothing about the
/// response varies by tenant. The route is organization-scoped because the
/// <em>action</em> is — you ask which providers you could connect for this
/// organization, gated on <c>communications.account.manage</c>. The repair
/// restores the authorization boundary without reinterpreting the resource as
/// tenant-owned.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class CommunicationProviderAuthorizationTests
{
    private const string RedirectUri = "https%3A%2F%2Fagency.invalid%2Fcallback";

    private readonly AgencyOsTestFixture _fixture;

    public CommunicationProviderAuthorizationTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>A. A member holding the grant still gets the providers.</summary>
    /// <remarks>
    /// First, because a repair that refused everybody would satisfy every other
    /// test in this file.
    /// </remarks>
    [Fact]
    public async Task AMemberWithTheGrantStillReadsTheProviders()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-member");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.GetAsync(Providers(actor));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CommunicationProviderResponse[] providers = JsonSerializer.Deserialize<CommunicationProviderResponse[]>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.NotEmpty(providers);
    }

    /// <summary>
    /// B and C. A member of a different organization is refused.
    /// </summary>
    /// <remarks>
    /// The defect itself. The caller is authenticated, holds Owner inside their own
    /// tenant — so the coarse any-membership gate passes — and is not a member of
    /// the organization named in the route.
    /// </remarks>
    [Fact]
    public async Task AMemberOfAnotherOrganizationIsRefused()
    {
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-owner");
        SeededActor stranger = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-stranger");

        using HttpClient client = _fixture.CreateClient(stranger.Subject);

        using HttpResponseMessage response = await client.GetAsync(Providers(theirs));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// D. An organization that does not exist is refused identically.
    /// </summary>
    /// <remarks>
    /// ADR-0038's rule for this surface, and the established behaviour of every
    /// sibling: the refusal is about the caller rather than about a record, so it
    /// is 403, and it must be the same whether the organization is real or
    /// invented. Comparing the two bodies is what proves the route cannot be used
    /// to discover which organizations exist.
    /// </remarks>
    [Fact]
    public async Task AnInventedOrganizationIsRefusedIdentically()
    {
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-real");
        SeededActor stranger = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-prober");

        using HttpClient client = _fixture.CreateClient(stranger.Subject);

        using HttpResponseMessage real = await client.GetAsync(Providers(theirs));

        using HttpResponseMessage invented = await client.GetAsync(
            $"/api/v1/organizations/{Guid.CreateVersion7()}"
                + $"/communication-providers?redirectUri={RedirectUri}");

        Assert.Equal(HttpStatusCode.Forbidden, real.StatusCode);
        Assert.Equal(real.StatusCode, invented.StatusCode);
        Assert.Equal(
            await real.Content.ReadAsStringAsync(),
            await invented.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// E. A member of the right organization without the grant is refused.
    /// </summary>
    /// <remarks>
    /// The other half of the boundary. Membership is not the permission: an
    /// observer belongs to the tenant and does not hold
    /// <c>communications.account.manage</c>, and the organization-scoped check has
    /// to refuse them too.
    /// </remarks>
    [Fact]
    public async Task AMemberWithoutTheGrantIsRefused()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-grantowner");
        SeededActor observer = await _fixture.SeedActorAsync(AgencyRole.Observer, "prov-observer");

        await _fixture.SeedMembershipAsync(
            owner.Organization.Id, observer.User.Id, AgencyRole.Observer, owner.User.Id);

        using HttpClient client = _fixture.CreateClient(observer.Subject);

        using HttpResponseMessage response = await client.GetAsync(Providers(owner));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unauthenticated caller is still refused first.</summary>
    [Fact]
    public async Task AnUnauthenticatedCallerIsRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-anon");
        using HttpClient client = _fixture.CreateClient(subject: null);

        using HttpResponseMessage response = await client.GetAsync(Providers(actor));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// F. The response carries no secret-bearing field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted over the raw JSON rather than the typed contract, because the
    /// question is what crosses the wire. The authorization URL is built with
    /// <c>client_id</c>, <c>redirect_uri</c>, <c>scope</c> and <c>state</c> — all
    /// public OAuth parameters. The client secret is used only in the server-side
    /// token exchange and appears in no contract type (ADR-0027).
    /// </para>
    /// <para>
    /// Kept as a standing assertion rather than a one-off review: this route
    /// returns configuration, and configuration is where a secret gets added by
    /// accident.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheResponseCarriesNoSecret()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-secrets");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.GetAsync(Providers(actor));

        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync();

        string[] forbidden =
        [
            "client_secret", "clientSecret", "refreshToken", "refresh_token",
            "accessToken", "access_token", "apiKey", "api_key", "password",
            "connectionString", "privateKey", "C:\\\\", "/var/", "bearer ",
        ];

        foreach (string needle in forbidden)
        {
            Assert.DoesNotContain(needle, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// G. Substituting another organization in the route reads nothing.
    /// </summary>
    /// <remarks>
    /// Stated as the attack rather than as a status code: a caller who legitimately
    /// holds the grant in their own tenant swaps the identifier in the path for
    /// somebody else's. Before the repair this returned the provider configuration.
    /// </remarks>
    [Fact]
    public async Task SubstitutingAnotherOrganizationInTheRouteReadsNothing()
    {
        SeededActor mine = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-mine");
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-theirs");

        using HttpClient client = _fixture.CreateClient(mine.Subject);

        using HttpResponseMessage own = await client.GetAsync(Providers(mine));
        using HttpResponseMessage swapped = await client.GetAsync(Providers(theirs));

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, swapped.StatusCode);

        Assert.DoesNotContain(
            "authorizationUrl",
            await swapped.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The class-level guard: no Communications route answers a non-member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Executable rather than structural. A static check that every endpoint binds
    /// a parameter called <c>organizationId</c> would encode a naming convention
    /// and prove nothing about authorization — an endpoint can bind it and ignore
    /// it. This calls each route as a proven non-member and asserts none of them
    /// answers with data.
    /// </para>
    /// <para>
    /// Routes are read from the document the server publishes, so a Communications
    /// route added later is covered the day it is documented. Required query
    /// parameters are supplied, because a 400 from binding would mask the
    /// authorization question this test is asking.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoCommunicationsRouteAnswersANonMember()
    {
        SeededActor theirs = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-class-owner");
        SeededActor stranger = await _fixture.SeedActorAsync(AgencyRole.Owner, "prov-class-stranger");

        using HttpClient client = _fixture.CreateClient(stranger.Subject);
        using HttpClient insider = _fixture.CreateClient(theirs.Subject);

        IReadOnlyList<string> routes = await CommunicationsRoutesAsync(client);

        Assert.True(
            routes.Count >= 5,
            $"Only {routes.Count} Communications routes were found; the selector has stopped "
                + "matching and this test is asserting almost nothing.");

        string tenant = theirs.Organization.Id.Value.ToString();
        List<string> leaked = [];
        List<string> unreachable = [];

        foreach (string route in routes)
        {
            string url = route.Replace("{organizationId}", tenant, StringComparison.Ordinal);

            using HttpResponseMessage outsider = await client.GetAsync(url);

            if (outsider.IsSuccessStatusCode)
            {
                leaked.Add($"{route} -> {(int)outsider.StatusCode} for a non-member");
            }

            // The member must still be served, or a route could pass the test above
            // by being broken for everybody.
            using HttpResponseMessage member = await insider.GetAsync(url);

            if (!member.IsSuccessStatusCode)
            {
                unreachable.Add($"{route} -> {(int)member.StatusCode} for a member");
            }
        }

        Assert.True(
            leaked.Count == 0,
            "These Communications routes answered a caller who is not a member:"
                + Environment.NewLine + string.Join(Environment.NewLine, leaked));

        Assert.True(
            unreachable.Count == 0,
            "These Communications routes refused a member who should be served:"
                + Environment.NewLine + string.Join(Environment.NewLine, unreachable));
    }

    // ------------------------------------------------------------- helpers

    private static string Providers(SeededActor actor) =>
        $"/api/v1/organizations/{actor.Organization.Id.Value}"
            + $"/communication-providers?redirectUri={RedirectUri}";

    /// <summary>
    /// Communications GETs that need no identifier, with their required inputs filled.
    /// </summary>
    private static async Task<IReadOnlyList<string>> CommunicationsRoutesAsync(HttpClient client)
    {
        const string Prefix = "/api/v1/organizations/{organizationId}/";

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        List<string> routes = [];

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith(Prefix, StringComparison.Ordinal)
                || !path.Value.TryGetProperty("get", out JsonElement get))
            {
                continue;
            }

            string rest = path.Name[Prefix.Length..];

            if (rest.Contains('{', StringComparison.Ordinal))
            {
                continue;
            }

            bool communications =
                rest.Contains("communication", StringComparison.Ordinal)
                || rest.Contains("message", StringComparison.Ordinal)
                || rest.Contains("outbound", StringComparison.Ordinal)
                || rest.Contains("participant", StringComparison.Ordinal);

            if (!communications)
            {
                continue;
            }

            routes.Add(path.Name + QueryFor(get));
        }

        routes.Sort(StringComparer.Ordinal);

        return routes;
    }

    /// <summary>Fills a route's required query parameters so binding cannot mask the refusal.</summary>
    private static string QueryFor(JsonElement operation)
    {
        if (!operation.TryGetProperty("parameters", out JsonElement parameters))
        {
            return string.Empty;
        }

        List<string> pairs = [];

        foreach (JsonElement parameter in parameters.EnumerateArray())
        {
            if (!parameter.TryGetProperty("required", out JsonElement required)
                || !required.GetBoolean()
                || !parameter.TryGetProperty("in", out JsonElement where)
                || where.GetString() != "query")
            {
                continue;
            }

            string name = parameter.GetProperty("name").GetString()!;

            pairs.Add(name switch
            {
                "redirectUri" => $"redirectUri={RedirectUri}",
                "address" => "address=someone%40elsewhere.invalid",
                _ => $"{name}=1",
            });
        }

        return pairs.Count == 0 ? string.Empty : "?" + string.Join('&', pairs);
    }
}
