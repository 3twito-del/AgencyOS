using System.Net;
using System.Text.Json;
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
public sealed class OpenApiContractTests
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
    /// The document must describe the M1 surface, including version identity and
    /// the release handshake.
    /// </summary>
    [Theory]
    [InlineData("/version")]
    [InlineData("/api/v1/release/handshake")]
    [InlineData("/api/v1/system/status")]
    [InlineData("/api/v1/organizations")]
    [InlineData("/api/v1/organizations/{id}")]
    [InlineData("/api/v1/organizations/{id}/memberships")]
    [InlineData("/api/v1/audit")]
    public async Task Contract_DescribesTheM1Surface(string path)
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
}
