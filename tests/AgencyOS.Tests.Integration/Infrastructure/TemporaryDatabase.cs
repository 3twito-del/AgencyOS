using AgencyOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgencyOS.Tests.Integration.Infrastructure;

/// <summary>
/// A throwaway database created on the same server the suite is already using.
/// </summary>
/// <remarks>
/// Needed wherever a test must observe a database in a state the shared fixture
/// cannot be in. First-run bootstrap is the clearest case: "uninitialized" is a
/// state the shared database leaves permanently after the first test that
/// initializes it, and it cannot be restored, because initialization is a
/// singleton row and the audit trail it writes cannot be deleted.
/// </remarks>
public sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;
    private readonly string _databaseName;

    private TemporaryDatabase(string serverConnectionString, string databaseName, string connectionString)
    {
        _serverConnectionString = serverConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    /// <summary>Connection string for the throwaway database.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates an empty database alongside <paramref name="templateConnectionString"/>.</summary>
    /// <param name="templateConnectionString">Any connection string on the target server.</param>
    /// <param name="prefix">Name prefix, to make stray databases identifiable.</param>
    public static async Task<TemporaryDatabase> CreateAsync(
        string templateConnectionString,
        string prefix = "agencyos_tmp")
    {
        string name = $"{prefix}_{Guid.NewGuid():N}";

        NpgsqlConnectionStringBuilder admin = new(templateConnectionString) { Database = "postgres" };

        await using (NpgsqlConnection connection = new(admin.ConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        NpgsqlConnectionStringBuilder target = new(templateConnectionString) { Database = name };

        return new TemporaryDatabase(admin.ConnectionString, name, target.ConnectionString);
    }

    /// <summary>Applies every migration, exactly as production would.</summary>
    public async Task MigrateAsync()
    {
        await using AgencyOsDbContext context = CreateDbContext();
        await context.Database.MigrateAsync().ConfigureAwait(false);
    }

    public AgencyOsDbContext CreateDbContext() =>
        new(
            new DbContextOptionsBuilder<AgencyOsDbContext>().UseNpgsql(ConnectionString).Options,
            new AgencyOS.Infrastructure.Time.SystemClock());

    /// <summary>Runs a scalar query against the throwaway database.</summary>
    public async Task<object?> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        try
        {
            await using NpgsqlConnection connection = new(_serverConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            // A leftover test database is untidy, not incorrect. Teardown must
            // never fail a run.
        }
    }
}
