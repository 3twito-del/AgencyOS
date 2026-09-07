using System.Globalization;
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
/// First-run initialization: the one path that creates authority from nothing,
/// and the only one that must leave the instance immediately usable.
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

    /// <summary>The version the simulated bootstrapping client reports.</summary>
    private const string BootstrapClientVersion = AgencyOsTestFixture.LatestVersion;

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

    /// <summary>
    /// Initialization publishes a release policy in the same operation, and it is
    /// the narrowest one that works: one platform, one ring, one version.
    /// </summary>
    [Fact]
    public async Task Bootstrap_PublishesAConservativeReleasePolicy()
    {
        using HttpResponseMessage response = await BootstrapAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        BootstrapResponse created = (await response.Content.ReadFromJsonAsync<BootstrapResponse>())!;

        Assert.Equal(AgencyOsTestFixture.Platform, created.ReleasePolicyPlatform);
        Assert.Equal(AgencyOsTestFixture.Channel, created.ReleasePolicyRing);
        Assert.Equal(BootstrapClientVersion, created.ReleasePolicyVersion);

        await using AgencyOsDbContext context = Database.CreateDbContext();

        ReleasePolicy policy = await context.ReleasePolicies.AsNoTracking().SingleAsync();

        Assert.Equal(AgencyOsTestFixture.Platform, policy.Platform);
        Assert.Equal(ReleaseRing.Alpha, policy.Ring);

        // Latest equals minimum: nothing older than the bootstrapping build is
        // admitted, and there is no wildcard range to widen by accident.
        Assert.Equal(BootstrapClientVersion, policy.LatestVersion);
        Assert.Equal(BootstrapClientVersion, policy.MinimumSupportedVersion);

        // The version is pinned to the bootstrapping build; the contract range is
        // the server's own, which spans every contract it still serves.
        Assert.Equal(ApiContract.MinimumSupported, policy.ApiContractMinimum);
        Assert.Equal(ApiContract.Current, policy.ApiContractMaximum);

        // Conservative posture: nothing revoked yet, no kill switch, no deadline.
        Assert.False(policy.KillSwitch);
        Assert.Empty(policy.RevokedVersions);
        Assert.Null(policy.MandatoryAfterUtc);
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

    /// <summary>The policy that makes the system usable is itself audited.</summary>
    [Fact]
    public async Task Bootstrap_AuditsThePublishedReleasePolicy()
    {
        using HttpResponseMessage response = await BootstrapAsync();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using AgencyOsDbContext context = Database.CreateDbContext();

        AuditEvent published = await context.AuditEvents
            .AsNoTracking()
            .SingleAsync(x => x.Action == AuditAction.ReleasePolicyPublished);

        Assert.Equal($"{AgencyOsTestFixture.Platform}/{AgencyOsTestFixture.Channel}", published.EntityId);
        Assert.Contains(BootstrapClientVersion, published.SemanticDelta!, StringComparison.Ordinal);
        Assert.Contains("bootstrapping build", published.Reason!, StringComparison.Ordinal);
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
    }

    /// <summary>
    /// A repeat attempt with a valid token must not mutate anything. The token
    /// remaining configured is an operational mistake, not an opening.
    /// </summary>
    [Fact]
    public async Task Bootstrap_RepeatedWithAValidTokenMutatesNothing()
    {
        using (HttpResponseMessage first = await BootstrapAsync())
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        Counts before = await CountAsync();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpResponseMessage repeat = await BootstrapAsync($"Repeat {attempt}");
            Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        }

        Counts after = await CountAsync();

        Assert.Equal(before, after);
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
        using HttpClient anonymous = CreateApiClient(subject: null);
        using HttpResponseMessage unauthenticated = await anonymous.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Anonymous Org", null, "Agency"));

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    // ------------------------------------------- immediately usable system

    /// <summary>
    /// The point of publishing a policy during initialization: an authorized,
    /// compatible client can perform an ordinary protected mutation straight after
    /// bootstrap, with no manual database seeding of any kind.
    /// </summary>
    [Fact]
    public async Task AfterBootstrap_AnAuthorizedCompatibleClientCanMutateImmediately()
    {
        using (HttpResponseMessage first = await BootstrapAsync())
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        // Nothing between bootstrap and this call. No seeding, no policy publish.
        using HttpClient owner = CreateApiClient(OwnerSubject);

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Second Organization", null, "Studio"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        OrganizationResponse organization =
            (await created.Content.ReadFromJsonAsync<OrganizationResponse>())!;

        Assert.Equal("Second Organization", organization.Name);
    }

    /// <summary>The owner can also grant a membership immediately, not merely read.</summary>
    [Fact]
    public async Task AfterBootstrap_TheOwnerCanGrantMembershipImmediately()
    {
        using HttpResponseMessage first = await BootstrapAsync();
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        BootstrapResponse created = (await first.Content.ReadFromJsonAsync<BootstrapResponse>())!;

        string recruitSubject = $"recruit-{Guid.NewGuid():N}";
        User recruit = await SeedUserAsync(recruitSubject);

        using HttpClient owner = CreateApiClient(OwnerSubject);

        using HttpResponseMessage granted = await owner.PostAsJsonAsync(
            $"/api/v1/organizations/{created.OrganizationId}/memberships",
            new GrantMembershipRequest(recruit.Id.Value, "Member"));

        Assert.Equal(HttpStatusCode.Created, granted.StatusCode);
    }

    /// <summary>
    /// The handshake answers correctly straight after bootstrap, so the client that
    /// initialized the system is told it is current.
    /// </summary>
    [Fact]
    public async Task AfterBootstrap_TheHandshakeReportsTheBootstrappingClientCurrent()
    {
        using HttpResponseMessage first = await BootstrapAsync();
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using HttpClient client = WithBootstrap.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/release/handshake",
            new AgencyOS.Contracts.Releases.HandshakeRequest(
                AgencyOsTestFixture.Platform,
                AgencyOsTestFixture.Channel,
                BootstrapClientVersion,
                ApiContract.Current));

        AgencyOS.Contracts.Releases.HandshakeResponse answer =
            (await response.Content.ReadFromJsonAsync<AgencyOS.Contracts.Releases.HandshakeResponse>())!;

        Assert.Equal("NONE", answer.Policy);
        Assert.False(answer.BlocksProtectedMutations);
    }

    // ------------------------------------ enforcement survives bootstrap

    /// <summary>
    /// Server-side compatibility enforcement is preserved: a build older than the
    /// one that bootstrapped the system is refused.
    /// </summary>
    [Fact]
    public async Task AfterBootstrap_AnIncompatibleClientIsStillRejected()
    {
        using (HttpResponseMessage first = await BootstrapAsync())
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using HttpClient stale = CreateApiClient(OwnerSubject, clientVersion: "0.2.0");

        using HttpResponseMessage response = await stale.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("From Stale Build", null, "Agency"));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task AfterBootstrap_AClientOnAnUnsupportedContractIsStillRejected()
    {
        using (HttpResponseMessage first = await BootstrapAsync())
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using HttpClient wrongContract = CreateApiClient(OwnerSubject, apiContractVersion: ApiContract.Current + 1);

        using HttpResponseMessage response = await wrongContract.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("From Future Contract", null, "Agency"));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task AfterBootstrap_AClientWithoutIdentityHeadersIsStillRejected()
    {
        using (HttpResponseMessage first = await BootstrapAsync())
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using HttpClient anonymousClient = WithBootstrap.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add(AgencyOsAuthentication.SubjectHeader, OwnerSubject);

        using HttpResponseMessage response = await anonymousClient.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("From Ungoverned Client", null, "Agency"));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    /// <summary>
    /// REVOKED semantics survive initialization: revoking the bootstrapping build
    /// refuses it for mutations, server-side.
    /// </summary>
    [Fact]
    public async Task AfterBootstrap_ARevokedClientIsStillRejected()
    {
        using (HttpResponseMessage first = await BootstrapAsync())
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        await RevokeAsync(BootstrapClientVersion);

        using HttpClient revoked = CreateApiClient(OwnerSubject);

        using HttpResponseMessage response = await revoked.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("From Revoked Build", null, "Agency"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("REVOKED", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------- atomicity of failure

    /// <summary>
    /// The initial policy is derived from the calling client, so initialization
    /// without a client identity is refused - and leaves nothing behind.
    /// </summary>
    [Fact]
    public async Task Bootstrap_RequiresClientIdentityAndLeavesNothingBehindWithoutIt()
    {
        using HttpClient client = WithBootstrap.CreateClient();
        client.DefaultRequestHeaders.Add(BootstrapHeaders.Token, BootstrapToken);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/system/bootstrap",
            SampleRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertNothingInitializedAsync();
    }

    /// <summary>
    /// Initializing for a client that speaks an unsupported contract would publish
    /// a policy that locks that client out immediately, so it is refused.
    /// </summary>
    [Fact]
    public async Task Bootstrap_RejectsAClientSpeakingAnUnsupportedContract()
    {
        using HttpClient client = BootstrapClient(apiContractVersion: ApiContract.Current + 1);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/system/bootstrap",
            SampleRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertNothingInitializedAsync();
    }

    /// <summary>
    /// A refused initialization leaves no partial state: no user, no organization,
    /// no membership, no policy, no audit record. Every component is committed by
    /// one save, so the outcome is all or nothing.
    /// </summary>
    [Fact]
    public async Task Bootstrap_LeavesNoPartialStateWhenItFails()
    {
        using HttpClient client = BootstrapClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/system/bootstrap",
            SampleRequest(organizationName: "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertNothingInitializedAsync();
    }

    // ------------------------------------------------------- token gating

    [Fact]
    public async Task Bootstrap_RequiresTheToken()
    {
        using HttpClient client = BootstrapClient(includeToken: false);

        using HttpResponseMessage missing = await client.PostAsJsonAsync(
            "/api/v1/system/bootstrap",
            SampleRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using HttpClient wrongToken = BootstrapClient(includeToken: false);
        wrongToken.DefaultRequestHeaders.Add(BootstrapHeaders.Token, "not-the-configured-token-but-long-enough");

        using HttpResponseMessage wrong = await wrongToken.PostAsJsonAsync(
            "/api/v1/system/bootstrap",
            SampleRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        await AssertNothingInitializedAsync();
    }

    /// <summary>
    /// With no token configured the route is never mapped, so a deployment that has
    /// not deliberately enabled first-run initialization has no such endpoint.
    /// </summary>
    [Fact]
    public async Task Bootstrap_RouteDoesNotExistWhenNoTokenIsConfigured()
    {
        HttpClient client = WithoutBootstrap.CreateClient();

        try
        {
            client.DefaultRequestHeaders.Add(BootstrapHeaders.Token, BootstrapToken);

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/v1/system/bootstrap",
                SampleRequest());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    // ------------------------------------------------------------- helpers

    private static BootstrapRequest SampleRequest(string organizationName = "Founding Agency") => new(
        organizationName,
        "Founding Agency Ltd",
        "Agency",
        OwnerSubject,
        "Founding Owner",
        OwnerSubject);

    /// <summary>
    /// A client presenting both the bootstrap token and a full client identity.
    /// The identity is what the initial release policy is derived from.
    /// </summary>
    private HttpClient BootstrapClient(
        bool includeToken = true,
        string? clientVersion = null,
        int? apiContractVersion = null)
    {
        HttpClient client = WithBootstrap.CreateClient();

        if (includeToken)
        {
            client.DefaultRequestHeaders.Add(BootstrapHeaders.Token, BootstrapToken);
        }

        client.DefaultRequestHeaders.Add(ClientHeaders.Platform, AgencyOsTestFixture.Platform);
        client.DefaultRequestHeaders.Add(ClientHeaders.Channel, AgencyOsTestFixture.Channel);
        client.DefaultRequestHeaders.Add(ClientHeaders.ClientVersion, clientVersion ?? BootstrapClientVersion);
        client.DefaultRequestHeaders.Add(
            ClientHeaders.ApiContractVersion,
            (apiContractVersion ?? ApiContract.Current).ToString(CultureInfo.InvariantCulture));

        return client;
    }

    private async Task<HttpResponseMessage> BootstrapAsync(string organizationName = "Founding Agency")
    {
        using HttpClient client = BootstrapClient();

        return await client.PostAsJsonAsync("/api/v1/system/bootstrap", SampleRequest(organizationName));
    }

    private HttpClient CreateApiClient(
        string? subject,
        string? clientVersion = null,
        int? apiContractVersion = null)
    {
        HttpClient client = WithBootstrap.CreateClient();

        if (subject is not null)
        {
            client.DefaultRequestHeaders.Add(AgencyOsAuthentication.SubjectHeader, subject);
        }

        client.DefaultRequestHeaders.Add(ClientHeaders.Platform, AgencyOsTestFixture.Platform);
        client.DefaultRequestHeaders.Add(ClientHeaders.Channel, AgencyOsTestFixture.Channel);
        client.DefaultRequestHeaders.Add(ClientHeaders.ClientVersion, clientVersion ?? BootstrapClientVersion);
        client.DefaultRequestHeaders.Add(
            ClientHeaders.ApiContractVersion,
            (apiContractVersion ?? ApiContract.Current).ToString(CultureInfo.InvariantCulture));

        return client;
    }

    private async Task<User> SeedUserAsync(string subject)
    {
        await using AgencyOsDbContext context = Database.CreateDbContext();

        User user = User.Register(subject, "Seeded", $"{subject}@agencyos.invalid", DateTimeOffset.UtcNow);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user;
    }

    /// <summary>Withdraws a build through the domain, as an authorized operator would.</summary>
    private async Task RevokeAsync(string version)
    {
        await using AgencyOsDbContext context = Database.CreateDbContext();

        ReleasePolicy policy = await context.ReleasePolicies.SingleAsync();
        policy.RevokeVersion(version, DateTimeOffset.UtcNow);

        await context.SaveChangesAsync();
    }

    private async Task AssertNothingInitializedAsync()
    {
        await using AgencyOsDbContext context = Database.CreateDbContext();

        Assert.False(await context.SystemInitializations.AsNoTracking().AnyAsync());
        Assert.False(await context.Organizations.AsNoTracking().AnyAsync());
        Assert.False(await context.Memberships.AsNoTracking().AnyAsync());
        Assert.False(await context.ReleasePolicies.AsNoTracking().AnyAsync());
        Assert.False(await context.AuditEvents.AsNoTracking().AnyAsync());

        // Users seeded directly by a test are not part of initialization, so only
        // the bootstrap owner's absence is asserted.
        Assert.False(await context.Users.AsNoTracking().AnyAsync(x => x.ExternalSubject == OwnerSubject));
    }

    private async Task<Counts> CountAsync()
    {
        await using AgencyOsDbContext context = Database.CreateDbContext();

        return new Counts(
            await context.Users.AsNoTracking().CountAsync(),
            await context.Organizations.AsNoTracking().CountAsync(),
            await context.Memberships.AsNoTracking().CountAsync(),
            await context.ReleasePolicies.AsNoTracking().CountAsync(),
            await context.SystemInitializations.AsNoTracking().CountAsync(),
            await context.AuditEvents.AsNoTracking().CountAsync());
    }

    private sealed record Counts(
        int Users,
        int Organizations,
        int Memberships,
        int ReleasePolicies,
        int Initializations,
        int AuditEvents);
}
