using System.Globalization;
using System.Text.Json;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using Microsoft.Data.Sqlite;

namespace AgencyOS.Client.Cache;

/// <summary>Raised when a cache cannot be opened and must not be guessed at.</summary>
public sealed class LocalCacheUnusableException : Exception
{
    public LocalCacheUnusableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The encrypted local cache: read-through copies and a durable write queue.
/// </summary>
/// <remarks>
/// <para>
/// Never canonical. Everything here came from the server and can be rebuilt from
/// the change feed; the client reads it when the network is unavailable and
/// prefers the server when it is. Nothing decides a business outcome from this
/// file. <c>CLAUDE.md</c> section 2 makes that a rule rather than a habit.
/// </para>
/// <para>
/// Cache operations are not audited. The audit trail records consequential
/// business facts, and "this workstation stored a copy of a person" is not one;
/// recording it would dilute the trail that matters
/// (<c>docs/07_SECURITY_AND_AUDIT.md</c>). The commands the queue eventually
/// submits are audited by the server when they run, which is where they actually
/// happen.
/// </para>
/// <para>
/// One file per channel, tenant and user, encrypted with SQLCipher under a
/// DPAPI-protected key (ADR-0015).
/// </para>
/// </remarks>
public sealed class LocalCache : AgencyOS.Client.Sync.IWriteQueue, IDisposable
{
    /// <summary>Name of the database file inside a cache directory.</summary>
    public const string DatabaseFileName = "cache.db";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _connection;

    private LocalCache(SqliteConnection connection, string directory)
    {
        _connection = connection;
        Directory = directory;
    }

    /// <summary>The directory holding this cache and its protected key.</summary>
    public string Directory { get; }

    /// <summary>
    /// Opens or creates the cache for an identity.
    /// </summary>
    /// <exception cref="LocalCacheUnusableException">
    /// The file exists, is a cache, and was written by a newer build; or a
    /// migration would discard queued commands the server has never seen.
    /// </exception>
    public static LocalCache Open(string rootDirectory, LocalCacheIdentity identity, ILocalCacheKeyProvider keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(keys);

        string directory = Path.Combine(rootDirectory, identity.DirectoryName);
        System.IO.Directory.CreateDirectory(directory);

        string key = keys.GetOrCreateKey(directory);

        SqliteConnection connection = OpenEncrypted(Path.Combine(directory, DatabaseFileName), key);

        try
        {
            Prepare(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return new LocalCache(connection, directory);
    }

    /// <summary>
    /// Deletes a cache entirely, key included.
    /// </summary>
    /// <remarks>
    /// The user-facing "reset local cache". Safe because nothing here is
    /// canonical; the next sync rebuilds it from position zero. Queued commands
    /// go with it, which is why the UI states plainly how many are outstanding
    /// before it offers this.
    /// </remarks>
    public static void Reset(string rootDirectory, LocalCacheIdentity identity, ILocalCacheKeyProvider keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(keys);

        // Pooled connections keep a handle on the file, and Windows refuses to
        // delete a file that something still holds open.
        SqliteConnection.ClearAllPools();

        string directory = Path.Combine(rootDirectory, identity.DirectoryName);

        if (!System.IO.Directory.Exists(directory))
        {
            return;
        }

        keys.Discard(directory);
        System.IO.Directory.Delete(directory, recursive: true);
    }

    // ---------------------------------------------------------------- state

    /// <summary>Gets the change-feed position this cache has durably applied.</summary>
    public long ReadCursor()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT cursor FROM sync_state WHERE id = 1;";

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Gets when the cache last completed a sync, if it ever has.</summary>
    public DateTimeOffset? ReadLastSyncedAt()
    {
        string? raw = LocalCacheSchema.ReadMeta(_connection, "last_synced_at");

        return raw is null
            ? null
            : DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    /// <summary>
    /// Applies a page of changes and advances the cursor, atomically.
    /// </summary>
    /// <remarks>
    /// One transaction for the records and the cursor together. Advancing the
    /// cursor separately would let a crash between the two leave a cache that
    /// believes it has applied a page it has not, and the change would never be
    /// offered again.
    /// </remarks>
    public void ApplyPage(
        long cursor,
        IReadOnlyList<PersonSummaryResponse> people,
        IReadOnlyList<CompanySummaryResponse> companies,
        IReadOnlyList<TaskResponse> tasks,
        IReadOnlyList<(string EntityType, Guid EntityId)> removed,
        DateTimeOffset syncedAt,
        IReadOnlyList<TalentSummaryResponse>? talent = null)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(removed);

        using SqliteTransaction transaction = _connection.BeginTransaction();

        foreach (PersonSummaryResponse person in people)
        {
            UpsertPerson(transaction, person);
        }

        foreach (CompanySummaryResponse company in companies)
        {
            UpsertCompany(transaction, company);
        }

        foreach (TaskResponse task in tasks)
        {
            UpsertTask(transaction, task);
        }

        foreach (TalentSummaryResponse entry in talent ?? [])
        {
            UpsertTalent(transaction, entry);
        }

        foreach ((string entityType, Guid entityId) in removed)
        {
            Remove(transaction, entityType, entityId);
        }

        using (SqliteCommand advance = _connection.CreateCommand())
        {
            advance.Transaction = transaction;
            advance.CommandText = "UPDATE sync_state SET cursor = $cursor WHERE id = 1;";
            advance.Parameters.AddWithValue("$cursor", cursor);
            advance.ExecuteNonQuery();
        }

        using (SqliteCommand stamp = _connection.CreateCommand())
        {
            stamp.Transaction = transaction;
            stamp.CommandText = """
                INSERT INTO cache_meta (key, value) VALUES ('last_synced_at', $at)
                ON CONFLICT (key) DO UPDATE SET value = excluded.value;
                """;
            stamp.Parameters.AddWithValue("$at", syncedAt.ToString("O", CultureInfo.InvariantCulture));
            stamp.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // ---------------------------------------------------------- offline reads

    public IReadOnlyList<PersonSummaryResponse> ReadPeople(string? search = null, int limit = 200)
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = search is null
            ? "SELECT * FROM cached_people ORDER BY display_name LIMIT $limit;"
            : """
              SELECT * FROM cached_people
              WHERE display_name LIKE $pattern ESCAPE '\'
                 OR COALESCE(email, '') LIKE $pattern ESCAPE '\'
                 OR COALESCE(title, '') LIKE $pattern ESCAPE '\'
              ORDER BY display_name LIMIT $limit;
              """;

        command.Parameters.AddWithValue("$limit", limit);

        if (search is not null)
        {
            command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(search)}%");
        }

        List<PersonSummaryResponse> people = [];

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            people.Add(new PersonSummaryResponse(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                reader.GetString(reader.GetOrdinal("display_name")),
                GetNullableString(reader, "title"),
                GetNullableString(reader, "email"),
                GetNullableString(reader, "phone"),
                reader.GetString(reader.GetOrdinal("status")),
                GetNullableGuid(reader, "primary_company_id"),
                GetNullableString(reader, "primary_company_name"),
                ReadInstant(reader, "updated_at"),
                reader.GetInt32(reader.GetOrdinal("version"))));
        }

        return people;
    }

    public IReadOnlyList<CompanySummaryResponse> ReadCompanies(int limit = 200)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM cached_companies ORDER BY name LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);

        List<CompanySummaryResponse> companies = [];

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            companies.Add(new CompanySummaryResponse(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                reader.GetString(reader.GetOrdinal("name")),
                GetNullableString(reader, "legal_name"),
                reader.GetString(reader.GetOrdinal("type")),
                reader.GetString(reader.GetOrdinal("status")),
                GetNullableString(reader, "website"),
                ReadInstant(reader, "updated_at"),
                reader.GetInt32(reader.GetOrdinal("version"))));
        }

        return companies;
    }

    public IReadOnlyList<TaskResponse> ReadTasks(bool openOnly = true, int limit = 200)
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = openOnly
            ? "SELECT * FROM cached_tasks WHERE state = 'Open' ORDER BY due_at IS NULL, due_at LIMIT $limit;"
            : "SELECT * FROM cached_tasks ORDER BY due_at IS NULL, due_at LIMIT $limit;";

        command.Parameters.AddWithValue("$limit", limit);

        List<TaskResponse> tasks = [];

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            string? subjectKind = GetNullableString(reader, "subject_kind");

            tasks.Add(new TaskResponse(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                reader.GetString(reader.GetOrdinal("title")),
                reader.GetString(reader.GetOrdinal("state")),
                reader.GetString(reader.GetOrdinal("priority")),
                ReadNullableInstant(reader, "due_at"),
                subjectKind is null
                    ? null
                    : new PartyReferenceResponse(
                        subjectKind,
                        GetNullableGuid(reader, "subject_id") ?? Guid.Empty,
                        GetNullableString(reader, "subject_name") ?? "(unknown)"),
                GetNullableGuid(reader, "source_interaction_id"),
                ReadInstant(reader, "created_at"),
                ReadNullableInstant(reader, "completed_at"),
                reader.GetInt32(reader.GetOrdinal("version"))));
        }

        return tasks;
    }

    /// <summary>Gets the number of cached records, for the diagnostics surface.</summary>
    public (int People, int Companies, int Tasks) ReadCounts()
    {
        return (Count("cached_people"), Count("cached_companies"), Count("cached_tasks"));
    }

    /// <summary>Gets how many talent summaries are cached for offline reading.</summary>
    public int ReadTalentCount() => Count("cached_talent");

    /// <summary>
    /// Reads cached talent, for the client list when the server is unreachable.
    /// </summary>
    /// <remarks>
    /// A read of a copy, never a source of truth. The client prefers the server
    /// whenever it can reach it, and the surface says which it used.
    /// </remarks>
    public IReadOnlyList<TalentSummaryResponse> ReadTalent(bool clientsOnly = false, int limit = 200)
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = clientsOnly
            ? "SELECT * FROM cached_talent WHERE is_client = 1 ORDER BY display_name LIMIT $limit;"
            : "SELECT * FROM cached_talent ORDER BY display_name LIMIT $limit;";

        command.Parameters.AddWithValue("$limit", limit);

        List<TalentSummaryResponse> talent = [];

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            talent.Add(new TalentSummaryResponse(
                Guid.Parse(reader.GetString(reader.GetOrdinal("talent_profile_id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("person_id"))),
                reader.GetString(reader.GetOrdinal("display_name")),
                reader.GetString(reader.GetOrdinal("career_stage")),
                Split(reader.GetString(reader.GetOrdinal("disciplines"))),
                GetNullableString(reader, "representation_status"),
                reader.GetInt32(reader.GetOrdinal("is_client")) == 1,
                GetNullableGuid(reader, "lead_user_id"),
                GetNullableString(reader, "lead_display_name"),
                Split(reader.GetString(reader.GetOrdinal("scopes"))),
                ReadInstant(reader, "updated_at"),
                reader.GetInt32(reader.GetOrdinal("version"))));
        }

        return talent;
    }

    // ----------------------------------------------------------- write queue

    /// <summary>
    /// Records a command locally before any attempt to send it.
    /// </summary>
    /// <remarks>
    /// The key is generated here, once. Generating it at submission time would
    /// mean a retry after a lost response carried a different key, and the server
    /// would correctly treat it as a second command - which is exactly the
    /// duplicate the queue exists to prevent.
    /// </remarks>
    public QueuedCommand Enqueue(
        QueuedOperation operation,
        string description,
        object payload,
        DateTimeOffset now,
        Guid? targetId = null,
        int? expectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        QueuedCommand command = new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture),
            operation,
            targetId,
            expectedVersion,
            JsonSerializer.Serialize(payload, payload.GetType(), Json),
            QueuedState.LocalPending,
            Attempts: 0,
            now,
            LastAttemptAt: null,
            LastError: null,
            ServerVersion: null,
            description);

        using SqliteCommand insert = _connection.CreateCommand();

        insert.CommandText = """
            INSERT INTO write_queue
                (id, idempotency_key, operation, target_id, expected_version, payload,
                 state, attempts, enqueued_at, description)
            VALUES ($id, $key, $operation, $target, $expected, $payload, $state, 0, $enqueued, $description);
            """;

        insert.Parameters.AddWithValue("$id", command.Id.ToString("D", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$key", command.IdempotencyKey);
        insert.Parameters.AddWithValue("$operation", operation.ToString());
        insert.Parameters.AddWithValue("$target", (object?)targetId?.ToString("D", CultureInfo.InvariantCulture) ?? DBNull.Value);
        insert.Parameters.AddWithValue("$expected", (object?)expectedVersion ?? DBNull.Value);
        insert.Parameters.AddWithValue("$payload", command.Payload);
        insert.Parameters.AddWithValue("$state", QueuedState.LocalPending.ToString());
        insert.Parameters.AddWithValue("$enqueued", now.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$description", description);

        insert.ExecuteNonQuery();

        return command;
    }

    /// <summary>Reads queued commands still wanting to be sent, oldest first.</summary>
    /// <remarks>
    /// Order matters: two edits to one record must reach the server in the order
    /// the user made them, or the earlier one wins.
    /// </remarks>
    public IReadOnlyList<QueuedCommand> ReadOutstanding(int limit = 100) =>
        ReadQueue(
            "WHERE state IN ('LocalPending', 'FailedRetryable') ORDER BY enqueued_at LIMIT $limit",
            limit);

    /// <summary>Reads commands that need a person to decide something.</summary>
    public IReadOnlyList<QueuedCommand> ReadNeedingAttention(int limit = 100) =>
        ReadQueue("WHERE state IN ('Conflict', 'FailedPermanent') ORDER BY enqueued_at LIMIT $limit", limit);

    public IReadOnlyList<QueuedCommand> ReadAll(int limit = 500) =>
        ReadQueue("ORDER BY enqueued_at LIMIT $limit", limit);

    /// <summary>Records the outcome of a submission attempt.</summary>
    public void RecordAttempt(
        Guid id,
        QueuedState state,
        DateTimeOffset attemptedAt,
        string? error = null,
        int? serverVersion = null,
        bool countAttempt = true)
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = $"""
            UPDATE write_queue
            SET state = $state,
                attempts = attempts + {(countAttempt ? 1 : 0)},
                last_attempt_at = $at,
                last_error = $error,
                server_version = $serverVersion
            WHERE id = $id;
            """;

        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$at", attemptedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$serverVersion", (object?)serverVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id.ToString("D", CultureInfo.InvariantCulture));

        command.ExecuteNonQuery();
    }

    /// <summary>Removes a settled command from the queue.</summary>
    /// <remarks>
    /// Used when a synced command has been superseded by the sync that follows
    /// it, and when the user abandons a conflicted one. A conflicted command is
    /// never removed on the client's own initiative: somebody decides.
    /// </remarks>
    public void Remove(Guid id)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM write_queue WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    /// <summary>Gets how many commands are outstanding or waiting on a decision.</summary>
    public (int Outstanding, int NeedsAttention) ReadQueueCounts()
    {
        using SqliteCommand command = _connection.CreateCommand();

        command.CommandText = """
            SELECT
                SUM(CASE WHEN state IN ('LocalPending', 'FailedRetryable') THEN 1 ELSE 0 END),
                SUM(CASE WHEN state IN ('Conflict', 'FailedPermanent') THEN 1 ELSE 0 END)
            FROM write_queue;
            """;

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return (0, 0);
        }

        return (
            reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt32(1));
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    // ------------------------------------------------------------- plumbing

    private static SqliteConnection OpenEncrypted(string path, string key)
    {
        SQLitePCL.Batteries_V2.Init();

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        SqliteConnection connection = new(builder.ConnectionString);

        try
        {
            connection.Open();

            // SQLCipher's raw-key form. The connection-string password would run
            // the key through PBKDF2, which buys nothing here: the key is already
            // 256 bits from a cryptographic RNG, so derivation adds cost and no
            // entropy. It must be the first statement on the connection.
            using (SqliteCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText = $"PRAGMA key = \"x'{key}'\";";
                pragma.ExecuteNonQuery();
            }

            // Reading the schema is what actually proves the key: SQLCipher does
            // not decrypt anything until the first real query, so opening alone
            // would succeed with a wrong key and fail later somewhere unhelpful.
            using (SqliteCommand probe = connection.CreateCommand())
            {
                probe.CommandText = "SELECT count(*) FROM sqlite_master;";
                probe.ExecuteScalar();
            }
        }
        catch (SqliteException exception)
        {
            connection.Dispose();

            // SQLite reports a wrong key as "file is not a database", because a
            // failed decrypt produces bytes that are not a header. Saying so
            // plainly beats surfacing that message to a user.
            throw new LocalCacheUnusableException(
                "The local cache could not be opened. Its key does not match, or the file is damaged. "
                    + "Resetting the cache is safe: it holds no data that is not on the server.",
                exception);
        }

        return connection;
    }

    /// <summary>
    /// Brings an opened database to the current schema version.
    /// </summary>
    /// <remarks>
    /// A newer cache is refused outright. Reading a shape this build does not
    /// understand risks writing a subtly wrong one back, and a downgrade is rare
    /// enough that a rebuild is the right price.
    /// </remarks>
    private static void Prepare(SqliteConnection connection)
    {
        int? version = LocalCacheSchema.ReadVersion(connection);

        if (version is null)
        {
            LocalCacheSchema.Create(connection);
            return;
        }

        if (version == LocalCacheSchema.Version)
        {
            return;
        }

        if (version > LocalCacheSchema.Version)
        {
            throw new LocalCacheUnusableException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The local cache is at schema version {version}; this build understands {LocalCacheSchema.Version}. It was written by a newer version of AgencyOS. Reset the cache to continue."));
        }

        if (version >= LocalCacheSchema.MinimumUpgradableVersion)
        {
            // Brought forward rather than discarded. The write queue holds commands
            // the server has never seen, so throwing the file away to avoid writing
            // a migration would lose a user's work.
            LocalCacheSchema.Upgrade(connection, version.Value);
            return;
        }

        throw new LocalCacheUnusableException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"The local cache is at schema version {version} and no upgrade to {LocalCacheSchema.Version} exists."));
    }

    private int Count(string table)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<QueuedCommand> ReadQueue(string clause, int limit)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = $"SELECT * FROM write_queue {clause};";
        command.Parameters.AddWithValue("$limit", limit);

        List<QueuedCommand> queued = [];

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            queued.Add(new QueuedCommand(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                reader.GetString(reader.GetOrdinal("idempotency_key")),
                Enum.Parse<QueuedOperation>(reader.GetString(reader.GetOrdinal("operation"))),
                GetNullableGuid(reader, "target_id"),
                GetNullableInt(reader, "expected_version"),
                reader.GetString(reader.GetOrdinal("payload")),
                Enum.Parse<QueuedState>(reader.GetString(reader.GetOrdinal("state"))),
                reader.GetInt32(reader.GetOrdinal("attempts")),
                ReadInstant(reader, "enqueued_at"),
                ReadNullableInstant(reader, "last_attempt_at"),
                GetNullableString(reader, "last_error"),
                GetNullableInt(reader, "server_version"),
                reader.GetString(reader.GetOrdinal("description"))));
        }

        return queued;
    }

    private void UpsertPerson(SqliteTransaction transaction, PersonSummaryResponse person)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO cached_people
                (id, display_name, title, email, phone, status, primary_company_id,
                 primary_company_name, updated_at, version)
            VALUES ($id, $displayName, $title, $email, $phone, $status, $companyId,
                    $companyName, $updatedAt, $version)
            ON CONFLICT (id) DO UPDATE SET
                display_name = excluded.display_name,
                title = excluded.title,
                email = excluded.email,
                phone = excluded.phone,
                status = excluded.status,
                primary_company_id = excluded.primary_company_id,
                primary_company_name = excluded.primary_company_name,
                updated_at = excluded.updated_at,
                version = excluded.version;
            """;

        command.Parameters.AddWithValue("$id", person.Id.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$displayName", person.DisplayName);
        command.Parameters.AddWithValue("$title", (object?)person.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$email", (object?)person.Email ?? DBNull.Value);
        command.Parameters.AddWithValue("$phone", (object?)person.Phone ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", person.Status);
        command.Parameters.AddWithValue(
            "$companyId",
            (object?)person.PrimaryCompanyId?.ToString("D", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$companyName", (object?)person.PrimaryCompanyName ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", person.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$version", person.Version);

        command.ExecuteNonQuery();
    }

    private void UpsertCompany(SqliteTransaction transaction, CompanySummaryResponse company)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO cached_companies
                (id, name, legal_name, type, status, website, updated_at, version)
            VALUES ($id, $name, $legalName, $type, $status, $website, $updatedAt, $version)
            ON CONFLICT (id) DO UPDATE SET
                name = excluded.name,
                legal_name = excluded.legal_name,
                type = excluded.type,
                status = excluded.status,
                website = excluded.website,
                updated_at = excluded.updated_at,
                version = excluded.version;
            """;

        command.Parameters.AddWithValue("$id", company.Id.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$name", company.Name);
        command.Parameters.AddWithValue("$legalName", (object?)company.LegalName ?? DBNull.Value);
        command.Parameters.AddWithValue("$type", company.Type);
        command.Parameters.AddWithValue("$status", company.Status);
        command.Parameters.AddWithValue("$website", (object?)company.Website ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", company.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$version", company.Version);

        command.ExecuteNonQuery();
    }

    private void UpsertTask(SqliteTransaction transaction, TaskResponse task)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO cached_tasks
                (id, title, state, priority, due_at, subject_kind, subject_id, subject_name,
                 source_interaction_id, created_at, completed_at, version)
            VALUES ($id, $title, $state, $priority, $dueAt, $subjectKind, $subjectId, $subjectName,
                    $sourceInteractionId, $createdAt, $completedAt, $version)
            ON CONFLICT (id) DO UPDATE SET
                title = excluded.title,
                state = excluded.state,
                priority = excluded.priority,
                due_at = excluded.due_at,
                subject_kind = excluded.subject_kind,
                subject_id = excluded.subject_id,
                subject_name = excluded.subject_name,
                source_interaction_id = excluded.source_interaction_id,
                created_at = excluded.created_at,
                completed_at = excluded.completed_at,
                version = excluded.version;
            """;

        command.Parameters.AddWithValue("$id", task.Id.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$title", task.Title);
        command.Parameters.AddWithValue("$state", task.State);
        command.Parameters.AddWithValue("$priority", task.Priority);
        command.Parameters.AddWithValue(
            "$dueAt",
            (object?)task.DueAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$subjectKind", (object?)task.Subject?.Kind ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$subjectId",
            (object?)task.Subject?.Id.ToString("D", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$subjectName", (object?)task.Subject?.Name ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$sourceInteractionId",
            (object?)task.SourceInteractionId?.ToString("D", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$completedAt",
            (object?)task.CompletedAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", task.Version);

        command.ExecuteNonQuery();
    }

    private void UpsertTalent(SqliteTransaction transaction, TalentSummaryResponse talent)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO cached_talent
                (person_id, talent_profile_id, display_name, career_stage, disciplines,
                 representation_status, is_client, lead_user_id, lead_display_name, scopes,
                 updated_at, version)
            VALUES ($personId, $profileId, $displayName, $careerStage, $disciplines,
                    $status, $isClient, $leadId, $leadName, $scopes, $updatedAt, $version)
            ON CONFLICT (person_id) DO UPDATE SET
                talent_profile_id = excluded.talent_profile_id,
                display_name = excluded.display_name,
                career_stage = excluded.career_stage,
                disciplines = excluded.disciplines,
                representation_status = excluded.representation_status,
                is_client = excluded.is_client,
                lead_user_id = excluded.lead_user_id,
                lead_display_name = excluded.lead_display_name,
                scopes = excluded.scopes,
                updated_at = excluded.updated_at,
                version = excluded.version;
            """;

        command.Parameters.AddWithValue("$personId", talent.PersonId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$profileId", talent.Id.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$displayName", talent.DisplayName);
        command.Parameters.AddWithValue("$careerStage", talent.CareerStage);
        command.Parameters.AddWithValue("$disciplines", string.Join('\u001f', talent.Disciplines));
        command.Parameters.AddWithValue("$status", (object?)talent.RepresentationStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$isClient", talent.IsClient ? 1 : 0);
        command.Parameters.AddWithValue(
            "$leadId",
            (object?)talent.LeadUserId?.ToString("D", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$leadName", (object?)talent.LeadDisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$scopes", string.Join('\u001f', talent.Scopes));
        command.Parameters.AddWithValue("$updatedAt", talent.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$version", talent.Version);

        command.ExecuteNonQuery();
    }

    /// <summary>Splits a stored list back into its parts.</summary>
    /// <remarks>
    /// Joined on a unit separator rather than a comma, because a discipline or
    /// scope could in principle contain one and a split on commas would invent
    /// entries that were never there.
    /// </remarks>
    private static IReadOnlyList<string> Split(string value) =>
        value.Length == 0 ? [] : value.Split('\u001f');

    private void Remove(SqliteTransaction transaction, string entityType, Guid entityId)
    {
        string? table = entityType switch
        {
            "Person" => "cached_people",
            "Company" => "cached_companies",
            "TaskItem" => "cached_tasks",

            // A talent feed entry is keyed by person, which is exactly this table's
            // primary key, so a removal needs no special case.
            "TalentProfile" => "cached_talent",

            // A type this build does not cache. Ignoring it is correct: the feed
            // is allowed to carry more than this client stores, and refusing
            // would stop a newer server from ever syncing an older client.
            _ => null,
        };

        if (table is null)
        {
            return;
        }

        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", entityId.ToString("D", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(SqliteDataReader reader, string column)
    {
        string? raw = GetNullableString(reader, column);
        return raw is null ? null : Guid.Parse(raw);
    }

    private static int? GetNullableInt(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTimeOffset ReadInstant(SqliteDataReader reader, string column) =>
        DateTimeOffset.Parse(
            reader.GetString(reader.GetOrdinal(column)),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? ReadNullableInstant(SqliteDataReader reader, string column)
    {
        string? raw = GetNullableString(reader, column);

        return raw is null
            ? null
            : DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
