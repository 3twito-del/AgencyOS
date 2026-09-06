using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// End-to-end tests over the M0 API host, exercised through the real HTTP pipeline.
/// </summary>
public sealed class ApiHostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiHostTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_ReportsHealthy()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponse? payload = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload.Status);
    }

    [Fact]
    public async Task Version_ReportsBuildIdentity()
    {
        using HttpClient client = _factory.CreateClient();

        VersionResponse? payload = await client.GetFromJsonAsync<VersionResponse>("/version");

        Assert.NotNull(payload);
        Assert.Equal(ApiContract.Current, payload.ApiContractVersion);
        Assert.Equal(BuildInfo.Version, payload.Version);
        Assert.False(string.IsNullOrWhiteSpace(payload.Channel));
        Assert.False(string.IsNullOrWhiteSpace(payload.BuildId));
        Assert.False(string.IsNullOrWhiteSpace(payload.GitCommit));
    }

    /// <summary>
    /// M0 exposes liveness and identity only. A business endpoint appearing before
    /// M1 would mean an unauthenticated, unaudited surface (docs/07_SECURITY_AND_AUDIT.md).
    /// </summary>
    [Fact]
    public async Task UnknownRoute_IsNotFound()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/people");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
