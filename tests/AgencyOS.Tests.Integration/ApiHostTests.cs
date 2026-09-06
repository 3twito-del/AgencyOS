using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Tests over the API host itself, exercised through the real HTTP pipeline.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class ApiHostTests
{
    private readonly AgencyOsTestFixture _fixture;

    public ApiHostTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Health_ReportsHealthy()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponse? payload = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload.Status);
    }

    [Fact]
    public async Task Version_ReportsBuildIdentity()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        VersionResponse? payload = await client.GetFromJsonAsync<VersionResponse>("/version");

        Assert.NotNull(payload);
        Assert.Equal(ApiContract.Current, payload.ApiContractVersion);
        Assert.Equal(BuildInfo.Version, payload.Version);
        Assert.False(string.IsNullOrWhiteSpace(payload.Channel));
        Assert.False(string.IsNullOrWhiteSpace(payload.BuildId));
        Assert.False(string.IsNullOrWhiteSpace(payload.GitCommit));
    }

    [Fact]
    public async Task UnknownRoute_IsNotFound()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The API host must be running on a ring that forbids real data, because the
    /// development authentication scheme trusts a header. Program.cs refuses to
    /// start otherwise; this asserts the suite is in fact exercising that posture.
    /// </summary>
    [Fact]
    public async Task Host_RunsOnARingThatForbidsRealData()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        VersionResponse payload = (await client.GetFromJsonAsync<VersionResponse>("/version"))!;

        Assert.Contains(
            payload.Channel,
            new[] { "forge", "lab", "nightly" },
            StringComparer.Ordinal);
    }
}
