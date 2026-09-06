using AgencyOS.Infrastructure.Persistence;
using AgencyOS.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AgencyOS.Tests.Integration;

/// <summary>
/// Verifies that the schema can be built from nothing, and that it arrives with
/// its integrity guarantees attached.
/// </summary>
[Collection(AgencyOsCollection.Name)]
public sealed class MigrationTests
{
    private readonly AgencyOsTestFixture _fixture;

    public MigrationTests(AgencyOsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Migrations_ApplyToACleanDatabase()
    {
        await using CleanDatabase clean = await CleanDatabase.CreateAsync(_fixture.ConnectionString);

        await using AgencyOsDbContext context = clean.CreateDbContext();
        await context.Database.MigrateAsync();

        IEnumerable<string> applied = await context.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);

        IEnumerable<string> pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Migrations_CreateEveryExpectedTable()
    {
        await using CleanDatabase clean = await CleanDatabase.CreateAsync(_fixture.ConnectionString);

        await using AgencyOsDbContext context = clean.CreateDbContext();
        await context.Database.MigrateAsync();

        foreach (string table in new[] { "users", "organizations", "memberships", "audit_events", "release_policies" })
        {
            // Cast to text: Npgsql cannot read the regclass type as an object.
            object? result = await clean.ScalarAsync($"SELECT to_regclass('public.{table}')::text");
            Assert.True(result is not null and not DBNull, $"Table '{table}' was not created.");
        }
    }

    /// <summary>
    /// The audit table must never exist without its guard. Both arrive in the same
    /// migration precisely so there is no window in which one is present and the
    /// other is not.
    /// </summary>
    [Fact]
    public async Task Migrations_InstallTheAuditAppendOnlyTriggers()
    {
        await using CleanDatabase clean = await CleanDatabase.CreateAsync(_fixture.ConnectionString);

        await using AgencyOsDbContext context = clean.CreateDbContext();
        await context.Database.MigrateAsync();

        object? rowTrigger = await clean.ScalarAsync(
            "SELECT tgname FROM pg_trigger WHERE tgrelid = 'audit_events'::regclass AND tgname = 'audit_events_append_only'");
        Assert.Equal("audit_events_append_only", rowTrigger);

        object? truncateTrigger = await clean.ScalarAsync(
            "SELECT tgname FROM pg_trigger WHERE tgrelid = 'audit_events'::regclass AND tgname = 'audit_events_no_truncate'");
        Assert.Equal("audit_events_no_truncate", truncateTrigger);
    }

    /// <summary>Re-running migrations must be a no-op, not an error.</summary>
    [Fact]
    public async Task Migrations_AreIdempotent()
    {
        await using CleanDatabase clean = await CleanDatabase.CreateAsync(_fixture.ConnectionString);

        await using (AgencyOsDbContext first = clean.CreateDbContext())
        {
            await first.Database.MigrateAsync();
        }

        await using AgencyOsDbContext second = clean.CreateDbContext();
        await second.Database.MigrateAsync();

        Assert.Empty(await second.Database.GetPendingMigrationsAsync());
    }

    /// <summary>A throwaway database on the same server as the suite's fixture.</summary>
    private sealed class CleanDatabase : IAsyncDisposable
    {
        private readonly string _serverConnectionString;
        private readonly string _databaseName;

        private CleanDatabase(string serverConnectionString, string databaseName, string connectionString)
        {
            _serverConnectionString = serverConnectionString;
            _databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<CleanDatabase> CreateAsync(string templateConnectionString)
        {
            string name = $"agencyos_migr_{Guid.NewGuid():N}";

            NpgsqlConnectionStringBuilder admin = new(templateConnectionString) { Database = "postgres" };

            await using (NpgsqlConnection connection = new(admin.ConnectionString))
            {
                await connection.OpenAsync();
                await using NpgsqlCommand command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{name}\"";
                await command.ExecuteNonQueryAsync();
            }

            NpgsqlConnectionStringBuilder target = new(templateConnectionString) { Database = name };
            return new CleanDatabase(admin.ConnectionString, name, target.ConnectionString);
        }

        public AgencyOsDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AgencyOsDbContext>().UseNpgsql(ConnectionString).Options);

        public async Task<object?> ScalarAsync(string sql)
        {
            await using NpgsqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();

            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();

            try
            {
                await using NpgsqlConnection connection = new(_serverConnectionString);
                await connection.OpenAsync();

                await using NpgsqlCommand command = connection.CreateCommand();
                command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
                await command.ExecuteNonQueryAsync();
            }
            catch (NpgsqlException)
            {
                // Teardown must never fail a run.
            }
        }
    }
}
