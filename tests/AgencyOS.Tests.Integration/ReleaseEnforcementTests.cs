using System.Net;
using System.Net.Http.Json;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Releases;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Tests.Integration.Infrastructure;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Verifies that the forced-update protocol is enforced by the server.
/// </summary>
/// <remarks>
/// <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>: update enforcement is a
/// security and data-integrity mechanism, not merely UX, and a REVOKED client
/// "cannot bypass it by altering the UI". Every mutation test here uses a caller
/// who is fully authorized; the only thing being tested is the build it is
/// calling from.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class ReleaseEnforcementTests
{
    private readonly AgencyOsTestFixture _fixture;

    public ReleaseEnforcementTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    // ---------------------------------------------------------------- handshake

    /// <summary>The required verification scenario: a compatible client is accepted.</summary>
    [Fact]
    public async Task Handshake_AcceptsACurrentClient()
    {
        HandshakeResponse answer = await HandshakeAsync(AgencyOsTestFixture.LatestVersion, ApiContract.Current);

        Assert.Equal("NONE", answer.Policy);
        Assert.False(answer.BlocksProtectedMutations);
        Assert.Equal(AgencyOsTestFixture.LatestVersion, answer.LatestVersion);
        Assert.Equal(AgencyOsTestFixture.MinimumSupportedVersion, answer.MinimumSupportedVersion);
        Assert.Equal(ApiContract.Current, answer.ApiContract.Minimum);
    }

    [Fact]
    public async Task Handshake_RecommendsUpdatingASupportedButOlderClient()
    {
        HandshakeResponse answer = await HandshakeAsync(AgencyOsTestFixture.MinimumSupportedVersion, ApiContract.Current);

        Assert.Equal("RECOMMENDED", answer.Policy);
        Assert.False(answer.BlocksProtectedMutations);
    }

    /// <summary>The required verification scenario: an incompatible client is rejected by policy.</summary>
    [Fact]
    public async Task Handshake_RequiresAnUpdateBelowTheMinimumSupportedVersion()
    {
        HandshakeResponse answer = await HandshakeAsync("0.1.0", ApiContract.Current);

        Assert.Equal("MANDATORY", answer.Policy);
        Assert.True(answer.BlocksProtectedMutations);
    }

    [Fact]
    public async Task Handshake_RequiresAnUpdateForAnUnsupportedApiContract()
    {
        HandshakeResponse answer = await HandshakeAsync(AgencyOsTestFixture.LatestVersion, ApiContract.Current + 1);

        Assert.Equal("MANDATORY", answer.Policy);
        Assert.True(answer.BlocksProtectedMutations);
        Assert.Contains("contract", answer.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The required verification scenario: a REVOKED build is identified as such.</summary>
    [Fact]
    public async Task Handshake_ReportsARevokedBuild()
    {
        HandshakeResponse answer = await HandshakeAsync(AgencyOsTestFixture.RevokedVersion, ApiContract.Current);

        Assert.Equal("REVOKED", answer.Policy);
        Assert.True(answer.BlocksProtectedMutations);
    }

    /// <summary>
    /// An unknown platform cannot be governed, so it is refused rather than waved
    /// through. Failing closed is the point.
    /// </summary>
    [Fact]
    public async Task Handshake_FailsClosedForAnUnknownPlatform()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/release/handshake",
            new HandshakeRequest("commodore-64", AgencyOsTestFixture.Channel, "0.3.0", ApiContract.Current));

        HandshakeResponse answer = (await response.Content.ReadFromJsonAsync<HandshakeResponse>())!;

        Assert.Equal("MANDATORY", answer.Policy);
        Assert.True(answer.BlocksProtectedMutations);
    }

    // -------------------------------------------------------------- enforcement

    /// <summary>
    /// The required verification scenario: a REVOKED client is rejected server-side
    /// for protected mutations.
    /// </summary>
    [Fact]
    public async Task RevokedClient_CannotPerformAProtectedMutation()
    {
        string subject = await SeedAuthorizedActorAsync();

        using HttpClient client = _fixture.CreateClient(subject, clientVersion: AgencyOsTestFixture.RevokedVersion);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"From Revoked {Guid.NewGuid():N}", null, "Agency"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("REVOKED", body, StringComparison.Ordinal);
    }

    /// <summary>An incompatible client is refused with 426 Upgrade Required.</summary>
    [Fact]
    public async Task ClientBelowMinimumVersion_CannotPerformAProtectedMutation()
    {
        string subject = await SeedAuthorizedActorAsync();

        using HttpClient client = _fixture.CreateClient(subject, clientVersion: "0.1.0");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"From Old {Guid.NewGuid():N}", null, "Agency"));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task ClientWithUnsupportedContract_CannotPerformAProtectedMutation()
    {
        string subject = await SeedAuthorizedActorAsync();

        using HttpClient client = _fixture.CreateClient(subject, apiContractVersion: ApiContract.Current + 1);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"From Future {Guid.NewGuid():N}", null, "Agency"));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    /// <summary>A client that presents no identity cannot be governed, so it cannot mutate.</summary>
    [Fact]
    public async Task ClientWithoutIdentityHeaders_CannotPerformAProtectedMutation()
    {
        string subject = await SeedAuthorizedActorAsync();

        using HttpClient client = _fixture.CreateClient(subject, includeClientHeaders: false);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"From Nowhere {Guid.NewGuid():N}", null, "Agency"));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    /// <summary>
    /// Enforcement blocks writes, not reads. A revoked build must still be able to
    /// read enough to explain itself and update, which is the update-only and
    /// read-only posture described in docs/06_FORCED_UPDATE_PROTOCOL.md.
    /// </summary>
    [Fact]
    public async Task RevokedClient_MayStillRead()
    {
        string subject = await SeedAuthorizedActorAsync();

        using HttpClient authorized = _fixture.CreateClient(subject);

        using HttpResponseMessage created = await authorized.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest($"Readable {Guid.NewGuid():N}", null, "Agency"));

        OrganizationResponse organization = (await created.Content.ReadFromJsonAsync<OrganizationResponse>())!;

        using HttpClient revoked = _fixture.CreateClient(subject, clientVersion: AgencyOsTestFixture.RevokedVersion);

        using HttpResponseMessage response = await revoked.GetAsync($"/api/v1/organizations/{organization.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The handshake stays reachable from a revoked build, otherwise a client could
    /// never be told why it is being refused.
    /// </summary>
    [Fact]
    public async Task RevokedClient_CanStillPerformTheHandshake()
    {
        HandshakeResponse answer = await HandshakeAsync(AgencyOsTestFixture.RevokedVersion, ApiContract.Current);

        Assert.Equal("REVOKED", answer.Policy);
    }

    /// <summary>
    /// Enforcement does not depend on the caller being authenticated: an anonymous
    /// request from a revoked build is stopped by release policy first.
    /// </summary>
    [Fact]
    public async Task RevokedClient_IsRefusedBeforeAuthentication()
    {
        using HttpClient client = _fixture.CreateClient(
            subject: null,
            clientVersion: AgencyOsTestFixture.RevokedVersion);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Anonymous From Revoked", null, "Agency"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("REVOKED", body, StringComparison.Ordinal);
    }

    private async Task<HandshakeResponse> HandshakeAsync(string clientVersion, int apiContractVersion)
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/release/handshake",
            new HandshakeRequest(
                AgencyOsTestFixture.Platform,
                AgencyOsTestFixture.Channel,
                clientVersion,
                apiContractVersion,
                BuildId: "20260907.0001"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<HandshakeResponse>())!;
    }

    private async Task<string> SeedAuthorizedActorAsync()
    {
        string subject = $"release-{Guid.NewGuid():N}";

        User user = await _fixture.SeedUserAsync(subject, "Release Actor");
        Organization organization = await _fixture.SeedOrganizationAsync($"Release Org {Guid.NewGuid():N}", user.Id);

        await _fixture.SeedMembershipAsync(organization.Id, user.Id, AgencyRole.Owner, user.Id);

        return subject;
    }
}
