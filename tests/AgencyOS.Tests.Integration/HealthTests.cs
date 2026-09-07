using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Liveness and readiness must answer different questions.
/// </summary>
/// <remarks>
/// The consequential test here is
/// <see cref="Liveness_StaysHealthyWhenPostgresIsUnreachable"/>. If a database
/// outage failed liveness, an orchestrator would restart every API instance for
/// the duration of the outage - which does not bring the database back, and
/// destroys the instances that would have recovered by themselves the moment it
/// returned.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class HealthTests
{
    /// <summary>
    /// A closed port, with a short timeout so the probe fails quickly rather than
    /// hanging the test.
    /// </summary>
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=agencyos_unreachable;Username=postgres;Password=none;Timeout=2;Command Timeout=2";

    private readonly AgencyOsTestFixture _fixture;

    public HealthTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Liveness_IsHealthyWhenPostgresIsReachable()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponse payload = (await response.Content.ReadFromJsonAsync<HealthResponse>())!;
        Assert.Equal("Healthy", payload.Status);
    }

    [Fact]
    public async Task Readiness_IsHealthyWhenPostgresIsReachable()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponse payload = (await response.Content.ReadFromJsonAsync<HealthResponse>())!;
        Assert.Equal("Healthy", payload.Status);
    }

    /// <summary>
    /// The point of separating the two endpoints: the process is fine, so liveness
    /// says so, even though the database is gone.
    /// </summary>
    [Fact]
    public async Task Liveness_StaysHealthyWhenPostgresIsUnreachable()
    {
        await using AgencyOsApiFactory factory = new(UnreachableConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponse payload = (await response.Content.ReadFromJsonAsync<HealthResponse>())!;
        Assert.Equal("Healthy", payload.Status);
    }

    /// <summary>Readiness must fail when canonical PostgreSQL cannot be reached.</summary>
    [Fact]
    public async Task Readiness_FailsWhenPostgresIsUnreachable()
    {
        await using AgencyOsApiFactory factory = new(UnreachableConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        HealthResponse payload = (await response.Content.ReadFromJsonAsync<HealthResponse>())!;
        Assert.Equal("Unhealthy", payload.Status);
    }

    /// <summary>
    /// A readiness probe reports; it does not throw. A 500 here would be read as an
    /// application defect rather than as an unavailable dependency.
    /// </summary>
    [Fact]
    public async Task Readiness_ReportsRatherThanThrowing()
    {
        await using AgencyOsApiFactory factory = new(UnreachableConnectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health/ready");

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
