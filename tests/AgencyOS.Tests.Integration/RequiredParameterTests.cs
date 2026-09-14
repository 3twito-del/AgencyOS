using System.Net;
using System.Net.Http.Json;
using System.Text;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Domain.Authorization;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// What the API answers when the caller leaves out something it requires.
/// </summary>
/// <remarks>
/// <para>
/// A required query parameter the caller omitted is a client error, and until
/// Repair Wave 001.5 every one of them answered <strong>500</strong>. Nine
/// required query parameters are declared in the contract and all of them did it,
/// as did every request body the framework could not parse.
/// </para>
/// <para>
/// The cause was one arm: <c>AgencyOsExceptionHandler</c> mapped
/// <c>BadHttpRequestException</c> only when its status was 413, so 400 fell
/// through to the untranslated path and became a server error with no detail.
/// The framework had already decided the right status; nothing was reading it.
/// </para>
/// <para>
/// These tests pin the public contract: <strong>missing or unparseable client
/// input is a 400 with an explanation, never a 500</strong> — and, just as
/// importantly, that authentication and authorization still run first, so the
/// refusal cannot tell an unauthorized caller anything.
/// </para>
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class RequiredParameterTests
{
    private readonly AgencyOsTestFixture _fixture;

    public RequiredParameterTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    /// <summary>A. The parameter is present and valid.</summary>
    /// <remarks>
    /// First, so a repair that answered 400 to everything could not pass by
    /// refusing the good request too.
    /// </remarks>
    [Fact]
    public async Task AProvidedParameterStillAnswers()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-ok");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"{Root(actor)}/communication-providers?redirectUri=https%3A%2F%2Fagency.invalid%2Fcallback");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>B. The parameter is missing.</summary>
    /// <remarks>
    /// Three routes and three parameter types — a string on a GET, a string on a
    /// different GET, an int on a DELETE — because the defect was in the shared
    /// handler rather than in any one endpoint, and one route would not show that.
    /// </remarks>
    [Theory]
    [InlineData("communication-providers", "redirectUri")]
    [InlineData("participant-suggestions", "address")]
    public async Task AMissingRequiredQueryParameterIsRefusedAsABadRequest(string route, string parameter)
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-missing");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.GetAsync($"{Root(actor)}/{route}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains(parameter, body, StringComparison.Ordinal);
    }

    /// <summary>B, for a required parameter that is not a string.</summary>
    [Fact]
    public async Task AMissingRequiredIntegerParameterIsRefusedAsABadRequest()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-int");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        Guid watchlist = await OpenWatchlistAsync(client, actor);

        using HttpResponseMessage response = await client.DeleteAsync(
            $"{Root(actor)}/intelligence/watchlists/{watchlist}/entries/{Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "expectedVersion",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    /// <summary>C. The parameter is present and will not parse.</summary>
    [Fact]
    public async Task AMalformedParameterIsRefusedAsABadRequest()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-malformed");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        Guid watchlist = await OpenWatchlistAsync(client, actor);

        using HttpResponseMessage response = await client.DeleteAsync(
            $"{Root(actor)}/intelligence/watchlists/{watchlist}/entries/{Guid.Empty}?expectedVersion=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>C. The parameter is present and empty.</summary>
    /// <remarks>
    /// A separate origin from the two above, and it needed its own repair. Binding
    /// succeeds — an empty string is a string — and the endpoint's own
    /// <c>ArgumentException.ThrowIfNullOrWhiteSpace</c> then threw, which reached
    /// the edge as an unrecognized exception and answered 500.
    ///
    /// Mapping <c>ArgumentException</c> to 400 globally would have been the wrong
    /// repair: that exception is how the rest of the codebase asserts internal
    /// preconditions, and relabelling it would hide real faults. The endpoint
    /// refuses explicitly instead.
    /// </remarks>
    [Fact]
    public async Task AnEmptyRequiredParameterIsRefusedAsABadRequest()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-empty");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"{Root(actor)}/communication-providers?redirectUri=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "redirectUri",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    /// <summary>A body the framework cannot read is the same class of failure.</summary>
    /// <remarks>
    /// Found during Audit 001 and repaired by the same arm. Kept because it is the
    /// other half of the class: the fix is about what the framework rejects before
    /// a handler runs, not about query strings specifically.
    /// </remarks>
    [Fact]
    public async Task AnUnparseableBodyIsRefusedAsABadRequest()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-body");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        using StringContent broken = new("{\"firstName\":", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync($"{Root(actor)}/people", broken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// D. An unauthenticated caller is refused before binding is reached.
    /// </summary>
    /// <remarks>
    /// The security property the repair had to preserve. Parameter binding runs
    /// inside the endpoint middleware, after authentication and authorization, so
    /// a 400 explaining which parameter is missing can only ever be seen by
    /// somebody already entitled to call the route. If this ever answered 400, the
    /// refusal would have become a way to probe the surface anonymously.
    /// </remarks>
    [Fact]
    public async Task AnUnauthenticatedCallerIsRefusedBeforeTheParameterIsChecked()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-anon");
        using HttpClient client = _fixture.CreateClient(subject: null);

        using HttpResponseMessage response = await client.GetAsync(
            $"{Root(actor)}/communication-providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// D. A caller without the grant is refused before binding is reached.
    /// </summary>
    /// <remarks>
    /// The route requires <c>communications.account.manage</c>. A member of the
    /// tenant who does not hold it must meet the authorization refusal, not the
    /// parameter one.
    /// </remarks>
    [Fact]
    public async Task ACallerWithoutTheGrantIsRefusedBeforeTheParameterIsChecked()
    {
        SeededActor owner = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-grantowner");
        SeededActor member = await _fixture.SeedActorAsync(AgencyRole.Observer, "param-nogrant");

        await _fixture.SeedMembershipAsync(
            owner.Organization.Id, member.User.Id, AgencyRole.Observer, owner.User.Id);

        using HttpClient client = _fixture.CreateClient(member.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"{Root(owner)}/communication-providers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// E. A well-formed cross-tenant request is still refused with 403.
    /// </summary>
    /// <remarks>
    /// The refusal the repair had to leave alone. A caller outside the tenant who
    /// sends a valid request meets the tenant refusal exactly as before.
    /// </remarks>
    [Fact]
    public async Task AWellFormedCrossTenantRequestIsStillRefused()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-tenant");
        SeededActor stranger = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-outsider");

        using HttpClient client = _fixture.CreateClient(stranger.Subject);

        using HttpResponseMessage response = await client.GetAsync(
            $"{Root(actor)}/participant-suggestions?address=someone%40elsewhere.invalid");

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"A stranger got {(int)response.StatusCode} rather than a tenant refusal.");
    }

    /// <summary>
    /// E. The 400 says nothing about whether the tenant exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter binding runs inside the endpoint middleware — after
    /// authentication, but <em>before</em> the tenant permission check. So an
    /// authenticated caller who omits a required parameter gets 400 rather than
    /// the 403 a well-formed request would have earned. That ordering predates this
    /// wave; before the repair the same request answered 500, which was equally
    /// distinguishable from 403 and less useful.
    /// </para>
    /// <para>
    /// What matters is that the refusal is identical for a tenant that exists and
    /// one that does not, which is what ADR-0038 requires of anything an outsider
    /// can probe. Binding cannot depend on the tenant because it happens before
    /// anything looks the tenant up — this asserts that rather than assuming it.
    /// The parameter's name is published in the OpenAPI contract, so naming it
    /// discloses nothing that was not already public.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AMissingParameterRefusalDoesNotRevealWhetherTheTenantExists()
    {
        SeededActor real = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-real");
        SeededActor stranger = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-prober");

        using HttpClient client = _fixture.CreateClient(stranger.Subject);

        using HttpResponseMessage existing = await client.GetAsync(
            $"{Root(real)}/participant-suggestions");

        using HttpResponseMessage invented = await client.GetAsync(
            $"/api/v1/organizations/{Guid.CreateVersion7()}/participant-suggestions");

        Assert.Equal(HttpStatusCode.BadRequest, existing.StatusCode);
        Assert.Equal(existing.StatusCode, invented.StatusCode);
        Assert.Equal(
            await existing.Content.ReadAsStringAsync(),
            await invented.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Nothing in the surface answers 500 to a missing required parameter.
    /// </summary>
    /// <remarks>
    /// The class-level assertion, read from the contract rather than from a list
    /// somebody maintains. Every operation declaring a required query parameter is
    /// called without it; none may answer 5xx. Nine such parameters exist today,
    /// and a tenth added tomorrow is covered the day it is documented.
    /// </remarks>
    [Fact]
    public async Task NoDocumentedRequiredQueryParameterAnswersWithAServerError()
    {
        SeededActor actor = await _fixture.SeedActorAsync(AgencyRole.Owner, "param-surface");
        using HttpClient client = _fixture.CreateClient(actor.Subject);

        IReadOnlyList<(string Verb, string Path)> operations =
            await OperationsWithRequiredQueryParametersAsync(client);

        Assert.True(
            operations.Count >= 5,
            $"Only {operations.Count} operations with a required query parameter were found. "
                + "The selector has stopped matching and this test is asserting almost nothing.");

        string tenant = actor.Organization.Id.Value.ToString();
        List<string> failures = [];

        foreach ((string verb, string path) in operations)
        {
            string url = path
                .Replace("{organizationId}", tenant, StringComparison.Ordinal)
                .Replace("{researchCaseId}", Guid.Empty.ToString(), StringComparison.Ordinal)
                .Replace("{signalId}", Guid.Empty.ToString(), StringComparison.Ordinal)
                .Replace("{thesisId}", Guid.Empty.ToString(), StringComparison.Ordinal)
                .Replace("{watchlistId}", Guid.Empty.ToString(), StringComparison.Ordinal)
                .Replace("{dealId}", Guid.Empty.ToString(), StringComparison.Ordinal)
                .Replace("{linkId}", Guid.Empty.ToString(), StringComparison.Ordinal)
                .Replace("{evidenceId}", Guid.Empty.ToString(), StringComparison.Ordinal)
                .Replace("{entryId}", Guid.Empty.ToString(), StringComparison.Ordinal)
                .Replace("{subjectRowId}", Guid.Empty.ToString(), StringComparison.Ordinal);

            if (url.Contains('{', StringComparison.Ordinal))
            {
                continue;
            }

            using HttpRequestMessage request = new(new HttpMethod(verb), url);
            using HttpResponseMessage response = await client.SendAsync(request);

            if ((int)response.StatusCode >= 500)
            {
                failures.Add($"{verb} {path} -> {(int)response.StatusCode}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "These answered a server error to a missing required parameter:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures));
    }

    // ------------------------------------------------------------- helpers

    private static string Root(SeededActor actor) =>
        $"/api/v1/organizations/{actor.Organization.Id.Value}";

    private static async Task<Guid> OpenWatchlistAsync(HttpClient client, SeededActor actor)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{Root(actor)}/intelligence/watchlists",
            new CreateWatchlistRequest("Parameter probe", "Internal", Purpose: "Why it is kept"));

        Assert.True(
            response.IsSuccessStatusCode,
            $"Opening a watchlist answered {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<IntelligenceIdResponse>())!.Id;
    }

    private static async Task<IReadOnlyList<(string Verb, string Path)>>
        OperationsWithRequiredQueryParametersAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        List<(string, string)> operations = [];

        foreach (System.Text.Json.JsonProperty path in
            document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (System.Text.Json.JsonProperty operation in path.Value.EnumerateObject())
            {
                if (operation.Name is not ("get" or "post" or "put" or "delete" or "patch"))
                {
                    continue;
                }

                if (!operation.Value.TryGetProperty("parameters", out System.Text.Json.JsonElement parameters))
                {
                    continue;
                }

                bool required = false;

                foreach (System.Text.Json.JsonElement parameter in parameters.EnumerateArray())
                {
                    if (parameter.TryGetProperty("in", out System.Text.Json.JsonElement where)
                        && where.GetString() == "query"
                        && parameter.TryGetProperty("required", out System.Text.Json.JsonElement flag)
                        && flag.GetBoolean())
                    {
                        required = true;
                        break;
                    }
                }

                if (required)
                {
                    operations.Add((operation.Name.ToUpperInvariant(), path.Name));
                }
            }
        }

        return operations;
    }
}
