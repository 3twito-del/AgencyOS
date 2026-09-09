using AgencyOS.Api.Authentication;
using AgencyOS.Contracts;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Releases;
using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Infrastructure.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgencyOS.Tests.Integration.Infrastructure;

/// <summary>
/// Shared PostgreSQL database and API host for the integration suite.
/// </summary>
/// <remarks>
/// One migrated database per test run. Tests keep themselves independent by
/// using distinct subjects and organization names rather than by resetting state,
/// which the audit trail could not support anyway: it is append-only, and
/// TRUNCATE on it is refused by a trigger.
/// </remarks>
public sealed class AgencyOsTestFixture : IAsyncLifetime
{
    /// <summary>Platform used by the suite's simulated Windows client.</summary>
    public const string Platform = "windows-x64";

    /// <summary>Ring the suite's simulated client reports.</summary>
    public const string Channel = "alpha";

    /// <summary>Newest published version in the seeded release policy.</summary>
    public const string LatestVersion = "0.3.0";

    /// <summary>Lowest supported version in the seeded release policy.</summary>
    public const string MinimumSupportedVersion = "0.2.0";

    /// <summary>A version explicitly withdrawn in the seeded release policy.</summary>
    public const string RevokedVersion = "0.2.5";

    private readonly PostgresTestDatabase _database = new();
    private AgencyOsApiFactory? _factory;

    /// <summary>Connection string for the migrated test database.</summary>
    public string ConnectionString => _database.ConnectionString;

    /// <summary>Describes where the database came from, for diagnostics.</summary>
    public string DatabaseProvider => _database.Provider;

    /// <summary>The API host under test.</summary>
    public AgencyOsApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("Fixture is not initialized.");

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync().ConfigureAwait(false);

        await using (AgencyOsDbContext context = CreateDbContext())
        {
            // The migration path is what production will run. Tests exercise it
            // rather than EnsureCreated, so the migration itself is under test.
            await context.Database.MigrateAsync().ConfigureAwait(false);
        }

        _factory = new AgencyOsApiFactory(ConnectionString);

        await SeedReleasePolicyAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync().ConfigureAwait(false);
        }

        await _database.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Creates a context against the test database, outside the API host.</summary>
    public AgencyOsDbContext CreateDbContext()
    {
        DbContextOptions<AgencyOsDbContext> options =
            new DbContextOptionsBuilder<AgencyOsDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

        return new AgencyOsDbContext(options, new SystemClock());
    }

    /// <summary>
    /// Creates a context that enforces the append-only interceptor, as the API does.
    /// </summary>
    public AgencyOsDbContext CreateGuardedDbContext()
    {
        DbContextOptions<AgencyOsDbContext> options =
            new DbContextOptionsBuilder<AgencyOsDbContext>()
                .UseNpgsql(ConnectionString)
                .AddInterceptors(new AuditAppendOnlyInterceptor())
                .Options;

        return new AgencyOsDbContext(options, new SystemClock());
    }

    /// <summary>Registers a user directly, as a first-run bootstrap would.</summary>
    public async Task<User> SeedUserAsync(string subject, string displayName)
    {
        await using AgencyOsDbContext context = CreateDbContext();

        User user = User.Register(subject, displayName, $"{subject}@agencyos.invalid", DateTimeOffset.UtcNow);
        context.Users.Add(user);
        await context.SaveChangesAsync().ConfigureAwait(false);

        return user;
    }

    /// <summary>Creates an organization directly, bypassing the command path.</summary>
    public async Task<Organization> SeedOrganizationAsync(string name, UserId createdBy)
    {
        await using AgencyOsDbContext context = CreateDbContext();

        Organization organization = Organization.Create(
            name,
            legalName: null,
            OrganizationType.Agency,
            createdBy,
            DateTimeOffset.UtcNow);

        context.Organizations.Add(organization);
        await context.SaveChangesAsync().ConfigureAwait(false);

        return organization;
    }

    /// <summary>Grants a membership directly, bypassing the command path.</summary>
    public async Task<Membership> SeedMembershipAsync(
        OrganizationId organizationId,
        UserId userId,
        AgencyRole role,
        UserId grantedBy)
    {
        await using AgencyOsDbContext context = CreateDbContext();

        Membership membership = Membership.Grant(organizationId, userId, role, grantedBy, DateTimeOffset.UtcNow);

        context.Memberships.Add(membership);
        await context.SaveChangesAsync().ConfigureAwait(false);

        return membership;
    }

    /// <summary>
    /// Changes what somebody may do, by revoking what they held and granting the
    /// rest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SeedMembershipAsync"/> <em>adds</em> a membership, and permissions
    /// are the union across every active one. Calling it a second time to "demote"
    /// somebody therefore grants a second membership beside the first and takes
    /// nothing away — the person keeps everything they had.
    /// </para>
    /// <para>
    /// Four M13 tests were written that way and passed for months against a
    /// database nobody could run. They asserted that a sensitive AI result is
    /// withheld after a grant is revoked, having revoked nothing. This helper
    /// exists so a test that says "revoke" revokes.
    /// </para>
    /// </remarks>
    public async Task ChangeRoleAsync(
        OrganizationId organizationId,
        UserId userId,
        AgencyRole role,
        UserId changedBy)
    {
        await using AgencyOsDbContext context = CreateDbContext();

        List<Membership> held = await context.Memberships
            .Where(x => x.OrganizationId == organizationId
                && x.UserId == userId
                && x.RevokedAt == null)
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (Membership membership in held)
        {
            membership.Revoke(changedBy, DateTimeOffset.UtcNow);
        }

        context.Memberships.Add(
            Membership.Grant(organizationId, userId, role, changedBy, DateTimeOffset.UtcNow));

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Seeds a user, an organization and a membership binding them, and returns the
    /// subject that authenticates as that user.
    /// </summary>
    /// <remarks>
    /// Every M2 test needs an actor holding a role inside a tenant, and each needs a
    /// distinct one so tests stay independent on a shared database.
    /// </remarks>
    public async Task<SeededActor> SeedActorAsync(AgencyRole role, string? label = null)
    {
        string subject = $"{label ?? "actor"}-{Guid.NewGuid():N}";

        User user = await SeedUserAsync(subject, $"{label ?? "Actor"} {role}").ConfigureAwait(false);
        Organization organization = await SeedOrganizationAsync($"Tenant {Guid.NewGuid():N}", user.Id)
            .ConfigureAwait(false);

        await SeedMembershipAsync(organization.Id, user.Id, role, user.Id).ConfigureAwait(false);

        return new SeededActor(subject, user, organization);
    }

    private async Task SeedReleasePolicyAsync()
    {
        await using AgencyOsDbContext context = CreateDbContext();

        ReleasePolicy policy = ReleasePolicy.Create(
            platform: Platform,
            ring: ReleaseRing.Alpha,
            latestVersion: LatestVersion,
            minimumSupportedVersion: MinimumSupportedVersion,
            // Mirrors what the server actually declares: contract 2 added the M2
            // slice additively, so contract 1 is still served.
            apiContractMinimum: ApiContract.MinimumSupported,
            apiContractMaximum: ApiContract.Current,
            now: DateTimeOffset.UtcNow,
            behindPolicy: UpdatePolicy.Recommended,
            revokedVersions: [RevokedVersion]);

        context.ReleasePolicies.Add(policy);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an HTTP client presenting a subject and a client identity.
    /// </summary>
    /// <param name="subject">Development identity subject, or <see langword="null"/> for anonymous.</param>
    /// <param name="clientVersion">Client version to report; defaults to the current release.</param>
    /// <param name="apiContractVersion">Contract version to report; defaults to the current contract.</param>
    /// <param name="includeClientHeaders">Whether to present a client identity at all.</param>
    public HttpClient CreateClient(
        string? subject = null,
        string? clientVersion = null,
        int? apiContractVersion = null,
        bool includeClientHeaders = true)
    {
        HttpClient client = Factory.CreateClient();

        if (subject is not null)
        {
            client.DefaultRequestHeaders.Add(AgencyOsAuthentication.SubjectHeader, subject);
        }

        if (includeClientHeaders)
        {
            client.DefaultRequestHeaders.Add(ClientHeaders.Platform, Platform);
            client.DefaultRequestHeaders.Add(ClientHeaders.Channel, Channel);
            client.DefaultRequestHeaders.Add(ClientHeaders.ClientVersion, clientVersion ?? LatestVersion);
            client.DefaultRequestHeaders.Add(
                ClientHeaders.ApiContractVersion,
                (apiContractVersion ?? ApiContract.Current).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return client;
    }
}

/// <summary>A seeded user holding a role inside a seeded tenant.</summary>
/// <param name="Subject">Development identity subject for this user.</param>
/// <param name="User">The user record.</param>
/// <param name="Organization">The tenant they hold a membership in.</param>
public sealed record SeededActor(string Subject, User User, Organization Organization);

/// <summary>Hosts the API against a given database.</summary>
public sealed class AgencyOsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string? _bootstrapToken;

    /// <param name="connectionString">Database the host should use.</param>
    /// <param name="bootstrapToken">
    /// Bootstrap token to configure, or <see langword="null"/> to leave first-run
    /// initialization disabled so the route is never mapped.
    /// </param>
    public AgencyOsApiFactory(string connectionString, string? bootstrapToken = null)
    {
        _connectionString = connectionString;
        _bootstrapToken = bootstrapToken;

        BlobRoot = Path.Combine(
            Path.GetTempPath(),
            "agencyos-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(BlobRoot);
    }

    /// <summary>Where this host stores blob content, so a test can inspect it.</summary>
    /// <remarks>
    /// Inspected deliberately in the hostile-filename tests: the assertion that
    /// matters is that nothing was written outside this directory, and that can
    /// only be checked by looking at the directory (ADR-0024).
    /// </remarks>
    public string BlobRoot { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:AgencyOS", _connectionString);
        builder.UseEnvironment("Development");

        // The M10 background worker is off under test. It is exercised by driving
        // the same processor it drives, one step at a time, which is the only way
        // an assertion can sit between "the provider was called" and "the row was
        // written" - the interval the whole send protocol is about (ADR-0028).
        builder.UseSetting("AgencyOS:Worker:Enabled", "false");

        // Stored bytes go somewhere disposable, not beside the test binaries.
        builder.UseSetting("AgencyOS:BlobStore:RootPath", BlobRoot);

        if (_bootstrapToken is not null)
        {
            builder.UseSetting("AgencyOS:Bootstrap:Token", _bootstrapToken);
        }

        // The scale harness needs to know how many round trips a request made.
        // Added as extra configuration rather than by re-registering the context,
        // so the harness measures the options the application actually built.
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<Scale.QueryCounter>();
            services.AddSingleton<
                Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AgencyOsDbContext>>(
                provider => new Scale.CountingOptionsConfiguration(
                    provider.GetRequiredService<Scale.QueryCounter>()));
        });
    }
}

/// <summary>Binds the integration suite to one shared database and host.</summary>
[CollectionDefinition(Name)]
public sealed class AgencyOsCollection : ICollectionFixture<AgencyOsTestFixture>
{
    public const string Name = "AgencyOS integration";
}
