using Npgsql;
using Testcontainers.PostgreSql;

namespace AgencyOS.Tests.Integration.Infrastructure;

/// <summary>
/// Provides a real PostgreSQL database for integration tests.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/11_TESTING_AND_FORMAL_METHODS.md</c> puts integration tests against a
/// real PostgreSQL instance at level 3 of the pyramid. There is no in-memory
/// substitute here by design: the append-only trigger, the <c>jsonb</c> and
/// <c>text[]</c> columns and the migration itself are PostgreSQL behavior, and a
/// fake would verify none of them.
/// </para>
/// <para>
/// Resolution order:
/// </para>
/// <list type="number">
/// <item><c>AGENCYOS_TEST_POSTGRES</c>, when set, naming a server the suite may
/// create databases on;</item>
/// <item>a Testcontainers PostgreSQL container, pinned to the ALPHA baseline
/// image.</item>
/// </list>
/// <para>
/// If neither is available the fixture throws. It deliberately does not skip:
/// a silently skipped audit-immutability test is indistinguishable from a
/// passing one, and that is the failure mode this suite exists to prevent.
/// </para>
/// </remarks>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    /// <summary>Environment variable naming an existing PostgreSQL server to test against.</summary>
    public const string ConnectionVariable = "AGENCYOS_TEST_POSTGRES";

    /// <summary>
    /// Container image used when no server is supplied. Pinned to the ALPHA
    /// baseline in <c>config/version-policy.yaml</c>.
    /// </summary>
    public const string ContainerImage = "postgres:18.6";

    private PostgreSqlContainer? _container;
    private string? _serverConnectionString;
    private string? _databaseName;

    /// <summary>Connection string for the isolated test database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Describes where the test database came from, for diagnostics.</summary>
    public string Provider { get; private set; } = "unresolved";

    public async Task InitializeAsync()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionVariable);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            _serverConnectionString = configured;
            Provider = $"{ConnectionVariable} environment variable";
        }
        else
        {
            try
            {
                _container = new PostgreSqlBuilder(ContainerImage).Build();

                await _container.StartAsync().ConfigureAwait(false);
                _serverConnectionString = _container.GetConnectionString();
                Provider = $"Testcontainers ({ContainerImage})";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "AgencyOS integration tests require a real PostgreSQL server. "
                        + $"Set {ConnectionVariable} to a connection string the suite may create databases on, "
                        + "or start Docker so a container can be used. "
                        + $"Container startup failed: {ex.Message}",
                    ex);
            }
        }

        // A dedicated database per run, so a test never sees another run's rows and
        // never touches anything that already existed on the target server.
        _databaseName = $"agencyos_test_{Guid.NewGuid():N}";

        NpgsqlConnectionStringBuilder adminBuilder = new(_serverConnectionString);
        string adminDatabase = string.IsNullOrWhiteSpace(adminBuilder.Database) ? "postgres" : adminBuilder.Database;
        adminBuilder.Database = adminDatabase;

        await using (NpgsqlConnection admin = new(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync().ConfigureAwait(false);

            await using NpgsqlCommand create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        NpgsqlConnectionStringBuilder testBuilder = new(_serverConnectionString)
        {
            Database = _databaseName,
        };

        ConnectionString = testBuilder.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (_serverConnectionString is null || _databaseName is null)
        {
            return;
        }

        // Drop the run's database from the supplied server, leaving it as found.
        NpgsqlConnectionStringBuilder adminBuilder = new(_serverConnectionString);
        adminBuilder.Database = string.IsNullOrWhiteSpace(adminBuilder.Database) ? "postgres" : adminBuilder.Database;

        NpgsqlConnection.ClearAllPools();

        try
        {
            await using NpgsqlConnection admin = new(adminBuilder.ConnectionString);
            await admin.OpenAsync().ConfigureAwait(false);

            await using NpgsqlCommand drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            // A leftover test database is untidy, not incorrect. Never fail a run
            // in teardown.
        }
    }
}
