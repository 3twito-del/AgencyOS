using System.Net;
using System.Net.Http.Json;
using AgencyOS.Api.Authentication;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Provisioning;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Provisioning;
using AgencyOS.Domain.Releases;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// First-run initialization: the one path that creates authority from nothing.
/// </summary>
/// <remarks>
/// Each test gets its own migrated, empty database. "Uninitialized" is a state
/// the shared fixture cannot return to - initialization is a singleton row and
/// the audit records it writes cannot be deleted - so it has to be created fresh.
/// </remarks>
[Collection(AgencyOsCollection.Name)]
public sealed class BootstrapTests : IAsyncLifetime
{
    private const string BootstrapToken = "bootstrap-token-for-tests-0123456789abcdef";
    private const string OwnerSubject = "founder@agencyos.invalid";

    private readonly AgencyOsTestFixture _shared;
    private TemporaryDatabase? _database;
    private AgencyOsApiFactory? _withBootstrap;
    private AgencyOsApiFactory? _withoutBootstrap;

    public BootstrapTests(AgencyOsTestFixture shared) => _shared = shared;

    public async Task InitializeAsync()
    {
        _database = await TemporaryDatabase.CreateAsync(_shared.ConnectionString, "agencyos_boot");
        await _database.MigrateAsync();

        _withBootstrap = new AgencyOsApiFactory(_database.ConnectionString, BootstrapToken);
        _withoutBootstrap = new AgencyOsApiFactory(_database.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_withBootstrap is not null)
        {
            await _withBootstrap.DisposeAsync();
        }

        if (_withoutBootstrap is not null)
        {
            await _withoutBootstrap.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private TemporaryDatabase Database => _database!;

    private AgencyOsApiFactory WithBootstrap => _withBootstrap!;

    private AgencyOsApiFactory WithoutBootstrap => _withoutBootstrap!;

    // ------------------------------------------------------------------ (a)

    [Fact]
    public async Task SystemStatus_ReportsUninitializedBeforeBootstrap()
    {
        using HttpClient client = WithBootstrap.CreateClient();

        SystemStatusResponse? status =
            await client.GetFromJsonAsync<SystemStatusResponse>("/api/v1/system/status");

        Assert.NotNull(status);
        Assert.False(status.Initialized);
    }

    /// <summary>Required scenario (a): bootstrap works on an empty system.</summary>
    [Fact]
    public async Task Bootstrap_InitializesAnEmptySystem()
    {
        using HttpResponseMessage response = await BootstrapAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        BootstrapResponse created = (await response.Content.ReadFromJsonAsync<BootstrapResponse>())!;

        Assert.NotEqual(Guid.Empty, created.OrganizationId);
        Assert.NotEqual(Guid.Empty, created.OwnerUserId);
        Assert.NotEqual(Guid.Empty, created.MembershipId);

        await using AgencyOsDbContext context = Database.CreateDbContext();

        // The owner exists, holds Owner in the new organization, and the
        // initialization singleton records both.
        User owner = await context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(OwnerSubject, owner.ExternalSubject);

        Membership membership = await context.Memberships.AsNoTracking().SingleAsync();
        Assert.Equal(AgencyOS.Domain.Authorization.AgencyRole.Owner, membership.Role);
        Assert.Equal(MembershipStatus.Active, membership.Status);

        SystemInitialization initialization = await context.SystemInitializations.AsNoTracking().SingleAsync();
        Assert.Equal(SystemInitialization.SingletonId, initialization.Id);
        Assert.Equal(created.OrganizationId, initialization.InitialOrganizationId.Value);
        Assert.Equal(created.OwnerUserId, initialization.InitialOwnerUserId.Value);
    }

    /// <summary>Bootstrap is a consequential act and is recorded as one.</summary>
    [Fact]
    public async Task Bootstrap_IsAudited()
    {
        using HttpResponseMessage response = await BootstrapAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using AgencyOsDbContext context = Database.CreateDbContext();

        AuditEvent bootstrapped = await context.AuditEvents
            .AsNoTracking()
            .SingleAsync(x => x.Action == AuditAction.SystemBootstrapped);

        // No authenticated actor exists during first run, and the record says so
        // rather than inventing one.
        Assert.Null(bootstrapped.ActorUserId);
        Assert.Equal("First-run initialization.", bootstrapped.Reason);
        Assert.Contains(OwnerSubject, bootstrapped.SemanticDelta!, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(bootstrapped.CorrelationId));

        // The resulting authority is explainable from the trail alone.
        Assert.True(await context.AuditEvents.AsNoTracking().AnyAsync(x => x.Action == AuditAction.UserRegistered));
        Assert.True(await context.AuditEvents.AsNoTracking().AnyAsync(x => x.Action == AuditAction.OrganizationCreated));
        Assert.True(await context.AuditEvents.AsNoTracking().AnyAsync(x => x.Action == AuditAction.MembershipGranted));
    }

    [Fact]
    public async Task SystemStatus_ReportsInitializedAfterBootstrap()
    {
        using HttpResponseMessage response = await BootstrapAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using HttpClient client = WithBootstrap.CreateClient();

        SystemStatusResponse status =
            (await client.GetFromJsonAsync<SystemStatusResponse>("/api/v1/system/status"))!;

        Assert.True(status.Initialized);
    }

    // ------------------------------------------------------------------ (b)

    /// <summary>Required scenario (b): bootstrap cannot be repeated.</summary>
    [Fact]
    public async Task Bootstrap_CannotBeRepeatedAfterInitialization()
    {
        using (HttpResponseMessage first = await BootstrapAsync())
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using HttpResponseMessage second = await BootstrapAsync(organizationName: "Second Attempt");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // And nothing was created by the refused attempt.
        await using AgencyOsDbContext context = Database.CreateDbContext();
        Assert.Equal(1, await context.Organizations.AsNoTracking().CountAsync());
        Assert.Equal(1, await context.SystemInitializations.AsNoTracking().CountAsync());
    }

    // ------------------------------------------------------------------ (c)

    /// <summary>
    /// Required scenario (c): normal organization creation still requires
    /// authorization after bootstrap.
    /// </summary>
    /// <remarks>
    /// Bootstrap creates one owner. It does not make the system permissive: a user
    /// who exists but holds no membership is refused, and the owner succeeds only
    /// because of the membership bootstrap granted them.
    /// </remarks>
    [Fact]
    public async Task Bootstrap_DoesNotWeakenNormalAuthorization()
    {
        using (HttpResponseMessage first = await BootstrapAsync())
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        await SeedReleasePolicyAsync();

        // A user with no membership is refused, even though they authenticate.
        string strangerSubject = $"stranger-{Guid.NewGuid():N}";
        await SeedUserAsync(strangerSubject);

        using (HttpClient stranger = CreateApiClient(strangerSubject))
        using (HttpResponseMessage refused = await stranger.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Stranger Org", null, "Agency")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        // An anonymous caller is refused too.
        using (HttpClient anonymous = CreateApiClient(subject: null))
        using (HttpResponseMessage unauthenticated = await anonymous.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Anonymous Org", null, "Agency")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        }

        // The bootstrapped owner succeeds - through the ordinary permission path.
        using HttpClient owner = CreateApiClient(OwnerSubject);
        using HttpResponseMessage permitted = await owner.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Second Organization", null, "Studio"));

        Assert.Equal(HttpStatusCode.Created, permitted.StatusCode);
    }

    // ------------------------------------------------------- token gating

    [Fact]
    public async Task Bootstrap_RequiresTheToken()
    {
        using HttpClient client = WithBootstrap.CreateClient();

        using HttpResponseMessage missing = await client.PostAsJsonAsync(
            "/api/v1/system/bootstrap",
            SampleRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        client.DefaultRequestHeaders.Add(BootstrapHeaders.Token, "not-the-configured-token-but-long-enough");

        using HttpResponseMessage wrong = await client.PostAsJsonAsync(
            "/api/v1/system/bootstrap",
            SampleRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        await using AgencyOsDbContext context = Database.CreateDbContext();
        Assert.False(await context.SystemInitializations.AsNoTracking().AnyAsync());
    }

    /// <summary>
    /// With no token configured the route is never mapped, so a deployment that has
    /// not deliberately enabled first-run initialization has no such endpoint.
    /// </summary>
    [Fact]
    public async Task Bootstrap_RouteDoesNotExistWhenNoTokenIsConfigured()
    {
        using HttpClient client = WithoutBootstrap.CreateClient();
        client.DefaultRequestHeaders.Add(BootstrapHeaders.Token, BootstrapToken);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/system/bootstrap",
            SampleRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------- helpers

    private static BootstrapRequest SampleRequest(string organizationName = "Founding Agency") => new(
        organizationName,
        "Founding Agency Ltd",
        "Agency",
        OwnerSubject,
        "Founding Owner",
        OwnerSubject);

    private async Task<HttpResponseMessage> BootstrapAsync(string organizationName = "Founding Agency")
    {
        using HttpClient client = WithBootstrap.CreateClient();
        client.DefaultRequestHeaders.Add(BootstrapHeaders.Token, BootstrapToken);

        return await client.PostAsJsonAsync("/api/v1/system/bootstrap", SampleRequest(organizationName));
    }

    private HttpClient CreateApiClient(string? subject)
    {
        HttpClient client = WithBootstrap.CreateClient();

        if (subject is not null)
        {
            client.DefaultRequestHeaders.Add(AgencyOsAuthentication.SubjectHeader, subject);
        }

        client.DefaultRequestHeaders.Add(ClientHeaders.Platform, AgencyOsTestFixture.Platform);
        client.DefaultRequestHeaders.Add(ClientHeaders.Channel, AgencyOsTestFixture.Channel);
        client.DefaultRequestHeaders.Add(ClientHeaders.ClientVersion, AgencyOsTestFixture.LatestVersion);
        client.DefaultRequestHeaders.Add(
            ClientHeaders.ApiContractVersion,
            ApiContract.Current.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return client;
    }

    private async Task SeedUserAsync(string subject)
    {
        await using AgencyOsDbContext context = Database.CreateDbContext();

        context.Users.Add(User.Register(subject, "Stranger", $"{subject}@agencyos.invalid", DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    private async Task SeedReleasePolicyAsync()
    {
        await using AgencyOsDbContext context = Database.CreateDbContext();

        context.ReleasePolicies.Add(ReleasePolicy.Create(
            platform: AgencyOsTestFixture.Platform,
            ring: ReleaseRing.Alpha,
            latestVersion: AgencyOsTestFixture.LatestVersion,
            minimumSupportedVersion: AgencyOsTestFixture.MinimumSupportedVersion,
            apiContractMinimum: ApiContract.Current,
            apiContractMaximum: ApiContract.Current,
            now: DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();
    }
}
