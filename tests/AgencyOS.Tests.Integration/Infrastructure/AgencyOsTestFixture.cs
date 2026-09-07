using AgencyOS.Api.Authentication;
using AgencyOS.Contracts;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Releases;
using AgencyOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

        return new AgencyOsDbContext(options);
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

        return new AgencyOsDbContext(options);
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

    private async Task SeedReleasePolicyAsync()
    {
        await using AgencyOsDbContext context = CreateDbContext();

        ReleasePolicy policy = ReleasePolicy.Create(
            platform: Platform,
            ring: ReleaseRing.Alpha,
            latestVersion: LatestVersion,
            minimumSupportedVersion: MinimumSupportedVersion,
            apiContractMinimum: ApiContract.Current,
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
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:AgencyOS", _connectionString);
        builder.UseEnvironment("Development");

        if (_bootstrapToken is not null)
        {
            builder.UseSetting("AgencyOS:Bootstrap:Token", _bootstrapToken);
        }
    }
}

/// <summary>Binds the integration suite to one shared database and host.</summary>
[CollectionDefinition(Name)]
public sealed class AgencyOsCollection : ICollectionFixture<AgencyOsTestFixture>
{
    public const string Name = "AgencyOS integration";
}
