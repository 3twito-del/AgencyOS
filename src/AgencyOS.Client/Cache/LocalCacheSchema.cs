using Microsoft.Data.Sqlite;

namespace AgencyOS.Client.Cache;

/// <summary>
/// The local cache's schema and its version.
/// </summary>
/// <remarks>
/// <para>
/// The version is explicit and stored in the database. A cache written by a newer
/// build is refused rather than read: guessing at an unfamiliar shape is how a
/// downgrade turns into corrupted local state, and the cache can always be
/// rebuilt from the change feed.
/// </para>
/// <para>
/// A cache written by an older build is migrated when a migration exists and
/// discarded when one does not. Discarding is not data loss - nothing here is
/// canonical - but the write queue is the exception: it holds commands the server
/// has not seen. A migration that would drop a non-empty queue is refused
/// (<see cref="LocalCache"/>).
/// </para>
/// </remarks>
public static class LocalCacheSchema
{
    /// <summary>Schema version this build writes and understands.</summary>
    public const int Version = 1;

    /// <summary>Creates the schema at <see cref="Version"/>.</summary>
    public static void Create(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Execute(connection, """
            CREATE TABLE cache_meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            -- The tenant's change-feed position this cache has durably applied.
            -- Advanced only after a page is written, so an interrupted sync
            -- resumes rather than skips.
            CREATE TABLE sync_state (
                id     INTEGER PRIMARY KEY CHECK (id = 1),
                cursor INTEGER NOT NULL DEFAULT 0
            );

            INSERT INTO sync_state (id, cursor) VALUES (1, 0);

            CREATE TABLE cached_people (
                id                   TEXT PRIMARY KEY,
                display_name         TEXT NOT NULL,
                title                TEXT NULL,
                email                TEXT NULL,
                phone                TEXT NULL,
                status               TEXT NOT NULL,
                primary_company_id   TEXT NULL,
                primary_company_name TEXT NULL,
                updated_at           TEXT NOT NULL,
                version              INTEGER NOT NULL
            );

            CREATE TABLE cached_companies (
                id         TEXT PRIMARY KEY,
                name       TEXT NOT NULL,
                legal_name TEXT NULL,
                type       TEXT NOT NULL,
                status     TEXT NOT NULL,
                website    TEXT NULL,
                updated_at TEXT NOT NULL,
                version    INTEGER NOT NULL
            );

            CREATE TABLE cached_tasks (
                id                    TEXT PRIMARY KEY,
                title                 TEXT NOT NULL,
                state                 TEXT NOT NULL,
                priority              TEXT NOT NULL,
                due_at                TEXT NULL,
                subject_kind          TEXT NULL,
                subject_id            TEXT NULL,
                subject_name          TEXT NULL,
                source_interaction_id TEXT NULL,
                created_at            TEXT NOT NULL,
                completed_at          TEXT NULL,
                version               INTEGER NOT NULL
            );

            CREATE INDEX ix_cached_people_display_name ON cached_people (display_name);
            CREATE INDEX ix_cached_companies_name ON cached_companies (name);
            CREATE INDEX ix_cached_tasks_state_due ON cached_tasks (state, due_at);

            -- The write queue. Durable by construction: a command is committed
            -- here before it is ever sent, so a crash between intent and
            -- submission loses nothing.
            --
            -- idempotency_key is generated once, at enqueue, and never changes.
            -- That is what lets the server recognize a retry after a lost
            -- response as the same command rather than a second one.
            CREATE TABLE write_queue (
                id               TEXT PRIMARY KEY,
                idempotency_key  TEXT NOT NULL UNIQUE,
                operation        TEXT NOT NULL,
                target_id        TEXT NULL,
                expected_version INTEGER NULL,
                payload          TEXT NOT NULL,
                state            TEXT NOT NULL,
                attempts         INTEGER NOT NULL DEFAULT 0,
                enqueued_at      TEXT NOT NULL,
                last_attempt_at  TEXT NULL,
                last_error       TEXT NULL,
                server_version   INTEGER NULL,
                description      TEXT NOT NULL
            );

            CREATE INDEX ix_write_queue_state ON write_queue (state, enqueued_at);
            """);

        SetMeta(connection, "schema_version", Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Reads the schema version, or null when the database is not a cache.</summary>
    public static int? ReadVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using SqliteCommand exists = connection.CreateCommand();
        exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'cache_meta';";

        if (exists.ExecuteScalar() is null)
        {
            return null;
        }

        string? raw = ReadMeta(connection, "schema_version");

        return int.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out int version)
            ? version
            : null;
    }

    public static string? ReadMeta(SqliteConnection connection, string key)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM cache_meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string;
    }

    public static void SetMeta(SqliteConnection connection, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cache_meta (key, value) VALUES ($key, $value)
            ON CONFLICT (key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
