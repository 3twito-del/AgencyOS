using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// The versioned, machine-readable API contract.
/// </summary>
/// <remarks>
/// <c>CLAUDE.md</c> principle 6 requires API contracts to be versioned and
/// explicit. A document that is valid but incomplete would satisfy a schema
/// validator and still fail the purpose, so these tests assert the surface it
/// describes, not merely that it parses.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed partial class OpenApiContractTests
{
    private readonly AgencyOsTestFixture _fixture;

    public OpenApiContractTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Contract_IsServedForVersionOne()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// ASP.NET Core 10 emits OpenAPI 3.1 natively, so the target version needs no
    /// custom serialization code. This asserts that remains true.
    /// </summary>
    [Fact]
    public async Task Contract_IsOpenApi31()
    {
        using JsonDocument document = await GetContractAsync();

        string version = document.RootElement.GetProperty("openapi").GetString()!;

        Assert.StartsWith("3.1", version, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contract_IdentifiesItself()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement info = document.RootElement.GetProperty("info");

        Assert.Equal("AgencyOS API", info.GetProperty("title").GetString());
        Assert.Equal("v1", info.GetProperty("version").GetString());
    }

    /// <summary>
    /// The document must describe every implemented route: version identity and the
    /// release handshake from M1, the people slice from M2, search, saved views
    /// and synchronization from M3, and talent, prospects, representation, credits
    /// and materials from M4.
    /// </summary>
    /// <remarks>
    /// A contract that silently stopped describing a route would still be valid
    /// OpenAPI, and a client generated from it would simply not know the route
    /// exists. Listing them is the only way that failure is visible.
    /// </remarks>
    [Theory]
    [InlineData("/version")]
    [InlineData("/api/v1/release/handshake")]
    [InlineData("/api/v1/system/status")]
    [InlineData("/api/v1/organizations")]
    [InlineData("/api/v1/organizations/{id}")]
    [InlineData("/api/v1/organizations/{id}/memberships")]
    [InlineData("/api/v1/audit")]
    [InlineData("/api/v1/organizations/{organizationId}/people")]
    [InlineData("/api/v1/organizations/{organizationId}/people/{personId}")]
    [InlineData("/api/v1/organizations/{organizationId}/people/{personId}/timeline")]
    [InlineData("/api/v1/organizations/{organizationId}/companies")]
    [InlineData("/api/v1/organizations/{organizationId}/companies/{companyId}")]
    [InlineData("/api/v1/organizations/{organizationId}/companies/{companyId}/timeline")]
    [InlineData("/api/v1/organizations/{organizationId}/relationships")]
    [InlineData("/api/v1/organizations/{organizationId}/relationships/{relationshipId}/end")]
    [InlineData("/api/v1/organizations/{organizationId}/interactions")]
    [InlineData("/api/v1/organizations/{organizationId}/tasks")]
    [InlineData("/api/v1/organizations/{organizationId}/tasks/{taskId}/complete")]
    [InlineData("/api/v1/organizations/{organizationId}/tasks/{taskId}/reopen")]
    [InlineData("/api/v1/organizations/{organizationId}/command-center")]
    [InlineData("/api/v1/organizations/{organizationId}/search")]
    [InlineData("/api/v1/organizations/{organizationId}/saved-views")]
    [InlineData("/api/v1/organizations/{organizationId}/saved-views/{savedViewId}")]
    [InlineData("/api/v1/organizations/{organizationId}/saved-views/{savedViewId}/results")]
    [InlineData("/api/v1/organizations/{organizationId}/sync/changes")]
    [InlineData("/api/v1/organizations/{organizationId}/sync/head")]
    [InlineData("/api/v1/organizations/{organizationId}/talent")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}/overview")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}/history")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}/credits")]
    [InlineData("/api/v1/organizations/{organizationId}/talent/{personId}/materials")]
    [InlineData("/api/v1/organizations/{organizationId}/talent-profiles/{talentProfileId}")]
    [InlineData("/api/v1/organizations/{organizationId}/talent-profiles/{talentProfileId}/disciplines")]
    [InlineData("/api/v1/organizations/{organizationId}/prospects")]
    [InlineData("/api/v1/organizations/{organizationId}/prospects/{prospectId}")]
    [InlineData("/api/v1/organizations/{organizationId}/prospects/{prospectId}/advance")]
    [InlineData("/api/v1/organizations/{organizationId}/prospects/{prospectId}/convert")]
    [InlineData("/api/v1/organizations/{organizationId}/representations")]
    [InlineData("/api/v1/organizations/{organizationId}/representations/{representationId}")]
    [InlineData("/api/v1/organizations/{organizationId}/representations/{representationId}/transition")]
    [InlineData("/api/v1/organizations/{organizationId}/representations/{representationId}/scopes")]
    [InlineData("/api/v1/organizations/{organizationId}/representations/{representationId}/team")]
    [InlineData("/api/v1/organizations/{organizationId}/credits")]
    [InlineData("/api/v1/organizations/{organizationId}/credits/{creditId}")]
    [InlineData("/api/v1/organizations/{organizationId}/materials")]
    [InlineData("/api/v1/organizations/{organizationId}/materials/{materialId}")]
    public async Task Contract_DescribesTheImplementedSurface(string path)
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement paths = document.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty(path, out _),
            $"The OpenAPI document does not describe '{path}'.");
    }

    /// <summary>
    /// The bootstrap route is absent here because this host has no bootstrap token
    /// configured, so it is genuinely not mapped. The published contract is
    /// generated with a throwaway token by
    /// <c>scripts/Invoke-AgencyOS.ps1 contract</c> so it documents the full surface.
    /// </summary>
    [Fact]
    public async Task Contract_OmitsBootstrapWhenItIsNotEnabled()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement paths = document.RootElement.GetProperty("paths");

        Assert.False(paths.TryGetProperty("/api/v1/system/bootstrap", out _));
    }

    /// <summary>
    /// No two paths may differ only in what their template parameters are called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OpenAPI 3.1 forbids it, and for a good reason: <c>/talent/{personId}</c> and
    /// <c>/talent/{talentProfileId}</c> are the same path as far as a router or a
    /// generated client is concerned, so a document containing both describes two
    /// resources that a caller has no way to tell apart.
    /// </para>
    /// <para>
    /// ASP.NET Core will happily route them, because it separates them by HTTP
    /// method. The contract cannot. M4 introduced exactly this pair before the
    /// talent-profile routes were moved to their own resource, and nothing in the
    /// build noticed, which is why this test exists rather than a note in a review.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Contract_HasNoPathsThatDifferOnlyByParameterName()
    {
        using JsonDocument document = await GetContractAsync();

        Dictionary<string, List<string>> byShape = [];

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            // Reduce every template parameter to a placeholder, so paths that a
            // caller could not distinguish collapse onto the same key.
            string shape = TemplateParameter().Replace(path.Name, "{}");

            if (!byShape.TryGetValue(shape, out List<string>? paths))
            {
                byShape[shape] = paths = [];
            }

            paths.Add(path.Name);
        }

        KeyValuePair<string, List<string>>[] ambiguous = [.. byShape.Where(x => x.Value.Count > 1)];

        Assert.True(
            ambiguous.Length == 0,
            "The contract contains paths that differ only by parameter name: "
                + string.Join("; ", ambiguous.Select(x => string.Join(" and ", x.Value))));
    }

    [Fact]
    public async Task Contract_DefinesResponseSchemas()
    {
        using JsonDocument document = await GetContractAsync();

        JsonElement schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        Assert.NotEmpty(schemas.EnumerateObject());
    }

    private async Task<JsonDocument> GetContractAsync()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        await using Stream stream = await client.GetStreamAsync("/openapi/v1.json");

        return await JsonDocument.ParseAsync(stream);
    }

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex TemplateParameter();
}
