using AgencyOS.Client.Cache;
using AgencyOS.Contracts.PeopleSlice;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

// NOT-SOURCE-READ: Reads the cache database the store under test has just written,
// to check that its contents are encrypted rather than readable.

/// <summary>
/// The encrypted local cache: isolation, schema versioning, offline reads and the
/// durable write queue.
/// </summary>
/// <remarks>
/// These run against real SQLCipher files in a temporary directory. An in-memory
/// substitute would test the code and not the thing that matters - that the file
/// on disk is encrypted and that a wrong key is refused.
/// </remarks>
[Collection(LocalCacheCollection.Name)]
public sealed class LocalCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agencyos-cache-tests", Guid.NewGuid().ToString("N"));

    private static readonly LocalCacheIdentity Identity =
        new("LAB", Guid.Parse("11111111-1111-1111-1111-111111111111"), "user@example.invalid");

    private static readonly ExplicitCacheKeyProvider Key =
        new("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temporary directory is not worth failing a test over.
            }
        }
    }

    [Fact]
    public void FreshCache_IsCreatedAtTheCurrentSchemaVersion()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        Assert.Equal(0, cache.ReadCursor());
        Assert.Null(cache.ReadLastSyncedAt());
        Assert.Equal((0, 0, 0), cache.ReadCounts());
    }

    /// <summary>
    /// Isolation is by path: one cache per channel, tenant and user.
    /// </summary>
    /// <remarks>
    /// <c>docs/05_RELEASE_RINGS.md</c> forbids mixing ring data. Two identities
    /// resolving to one directory would make that a convention rather than a fact.
    /// </remarks>
    [Fact]
    public void CacheDirectory_DiffersByChannelTenantAndUser()
    {
        LocalCacheIdentity forge = Identity with { Channel = "FORGE" };
        LocalCacheIdentity otherTenant = Identity with { OrganizationId = Guid.NewGuid() };
        LocalCacheIdentity otherUser = Identity with { UserSubject = "someone.else@example.invalid" };

        string[] names =
        [
            Identity.DirectoryName,
            forge.DirectoryName,
            otherTenant.DirectoryName,
            otherUser.DirectoryName,
        ];

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The user's subject never appears in a path, even hashed beyond recognition.</summary>
    [Fact]
    public void CacheDirectory_DoesNotContainTheUsersSubject()
    {
        Assert.DoesNotContain("user@example.invalid", Identity.DirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReopeningWithTheSameKey_ReadsWhatWasWritten()
    {
        Guid personId = Guid.NewGuid();

        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            cache.ApplyPage(7, [Person(personId, "Sarah Klein")], [], [], [], DateTimeOffset.UtcNow);
        }

        SqliteConnection.ClearAllPools();

        using LocalCache reopened = LocalCache.Open(_root, Identity, Key);

        Assert.Equal(7, reopened.ReadCursor());
        Assert.Equal("Sarah Klein", Assert.Single(reopened.ReadPeople()).DisplayName);
    }

    /// <summary>
    /// A wrong key is refused rather than producing a corrupt read.
    /// </summary>
    /// <remarks>
    /// SQLCipher reports a failed decrypt as "file is not a database", because the
    /// bytes it produces are not a header. The cache turns that into something a
    /// user can act on.
    /// </remarks>
    [Fact]
    public void WrongKey_IsRefused()
    {
        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            cache.ApplyPage(1, [Person(Guid.NewGuid(), "Sarah Klein")], [], [], [], DateTimeOffset.UtcNow);
        }

        SqliteConnection.ClearAllPools();

        ExplicitCacheKeyProvider wrong = new("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

        LocalCacheUnusableException failure = Assert.Throws<LocalCacheUnusableException>(
            () => LocalCache.Open(_root, Identity, wrong));

        Assert.Contains("Resetting the cache is safe", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The file on disk carries no readable contact data.</summary>
    [Fact]
    public void DatabaseFile_HasNoPlaintextOnDisk()
    {
        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            cache.ApplyPage(
                1,
                [Person(Guid.NewGuid(), "Persephone Wintergarden")],
                [],
                [],
                [],
                DateTimeOffset.UtcNow);
        }

        SqliteConnection.ClearAllPools();

        string path = Path.Combine(_root, Identity.DirectoryName, LocalCache.DatabaseFileName);
        byte[] bytes = File.ReadAllBytes(path);
        string raw = System.Text.Encoding.ASCII.GetString(bytes);

        Assert.DoesNotContain("Persephone", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("SQLite format 3", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cache written by a newer build is refused, not guessed at.
    /// </summary>
    /// <remarks>
    /// Reading an unfamiliar shape risks writing a subtly wrong one back. A rebuild
    /// costs one synchronization; a corrupted local state costs trust.
    /// </remarks>
    [Fact]
    public void NewerSchema_IsRefusedRatherThanGuessedAt()
    {
        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            // Opened and closed so the schema exists to tamper with.
        }

        SqliteConnection.ClearAllPools();

        string path = Path.Combine(_root, Identity.DirectoryName, LocalCache.DatabaseFileName);

        SqliteConnectionStringBuilder builder = new() { DataSource = path };

        using (SqliteConnection connection = new(builder.ConnectionString))
        {
            connection.Open();

            using (SqliteCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText =
                    $"PRAGMA key = \"x'{Key.GetOrCreateKey(Path.Combine(_root, Identity.DirectoryName))}'\";";
                pragma.ExecuteNonQuery();
            }

            LocalCacheSchema.SetMeta(connection, "schema_version", "99");
        }

        SqliteConnection.ClearAllPools();

        LocalCacheUnusableException failure = Assert.Throws<LocalCacheUnusableException>(
            () => LocalCache.Open(_root, Identity, Key));

        Assert.Contains("newer version", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reset_RemovesTheCacheEntirely()
    {
        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            cache.ApplyPage(3, [Person(Guid.NewGuid(), "Sarah Klein")], [], [], [], DateTimeOffset.UtcNow);
        }

        LocalCache.Reset(_root, Identity, Key);

        Assert.False(Directory.Exists(Path.Combine(_root, Identity.DirectoryName)));

        using LocalCache rebuilt = LocalCache.Open(_root, Identity, Key);

        Assert.Equal(0, rebuilt.ReadCursor());
        Assert.Empty(rebuilt.ReadPeople());
    }

    /// <summary>
    /// A page's records and its cursor land together.
    /// </summary>
    /// <remarks>
    /// If the cursor could advance without the records, the client would believe it
    /// had applied changes it had not, and would never be offered them again.
    /// </remarks>
    [Fact]
    public void ApplyPage_AdvancesTheCursorWithTheRecords()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        Guid personId = Guid.NewGuid();

        cache.ApplyPage(5, [Person(personId, "Sarah Klein")], [], [], [], DateTimeOffset.UtcNow);

        Assert.Equal(5, cache.ReadCursor());
        Assert.Single(cache.ReadPeople());

        cache.ApplyPage(9, [Person(personId, "Sarah Klein-Moreau")], [], [], [], DateTimeOffset.UtcNow);

        Assert.Equal(9, cache.ReadCursor());
        Assert.Equal("Sarah Klein-Moreau", Assert.Single(cache.ReadPeople()).DisplayName);
    }

    [Fact]
    public void RemovedEntries_DropTheCachedRecord()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        Guid personId = Guid.NewGuid();

        cache.ApplyPage(1, [Person(personId, "Sarah Klein")], [], [], [], DateTimeOffset.UtcNow);
        cache.ApplyPage(2, [], [], [], [("Person", personId)], DateTimeOffset.UtcNow);

        Assert.Empty(cache.ReadPeople());
    }

    /// <summary>A feed entry for a type this build does not cache is ignored, not fatal.</summary>
    [Fact]
    public void RemovedEntry_ForAnUnknownType_IsIgnored()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        cache.ApplyPage(1, [], [], [], [("SomethingFromTheFuture", Guid.NewGuid())], DateTimeOffset.UtcNow);

        Assert.Equal(1, cache.ReadCursor());
    }

    [Fact]
    public void OfflineSearch_MatchesNameEmailAndTitle()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        cache.ApplyPage(
            1,
            [
                Person(Guid.NewGuid(), "Sarah Klein", title: "Literary Agent", email: "sarah@example.invalid"),
                Person(Guid.NewGuid(), "Marcus Reid", title: "Manager", email: "marcus@example.invalid"),
            ],
            [],
            [],
            [],
            DateTimeOffset.UtcNow);

        Assert.Single(cache.ReadPeople("Klein"));
        Assert.Single(cache.ReadPeople("marcus@"));
        Assert.Single(cache.ReadPeople("Literary"));
        Assert.Empty(cache.ReadPeople("Nobody"));
    }

    /// <summary>A wildcard typed into the search box is text, not a pattern.</summary>
    [Fact]
    public void OfflineSearch_TreatsWildcardsAsLiteralText()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        cache.ApplyPage(1, [Person(Guid.NewGuid(), "Sarah Klein")], [], [], [], DateTimeOffset.UtcNow);

        Assert.Empty(cache.ReadPeople("%"));
        Assert.Empty(cache.ReadPeople("_arah"));
    }

    // ----------------------------------------------------------- write queue

    [Fact]
    public void Enqueue_RecordsACommandWithAStableKey()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        QueuedCommand queued = cache.Enqueue(
            QueuedOperation.CreatePerson,
            "Add Sarah Klein",
            new CreatePersonRequest("Sarah", "Klein"),
            DateTimeOffset.UtcNow);

        QueuedCommand read = Assert.Single(cache.ReadOutstanding());

        Assert.Equal(queued.Id, read.Id);
        Assert.Equal(queued.IdempotencyKey, read.IdempotencyKey);
        Assert.Equal(QueuedState.LocalPending, read.State);
        Assert.Equal(0, read.Attempts);
    }

    /// <summary>
    /// The queue survives a restart, key and all.
    /// </summary>
    /// <remarks>
    /// This is the property the whole design rests on: a key regenerated after a
    /// crash would make the retry look like a second command to the server, which
    /// is the duplicate the queue exists to prevent.
    /// </remarks>
    [Fact]
    public void QueuedCommands_SurviveARestartWithTheirKeys()
    {
        string key;

        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            key = cache.Enqueue(
                QueuedOperation.CreatePerson,
                "Add Sarah Klein",
                new CreatePersonRequest("Sarah", "Klein"),
                DateTimeOffset.UtcNow).IdempotencyKey;
        }

        SqliteConnection.ClearAllPools();

        using LocalCache reopened = LocalCache.Open(_root, Identity, Key);

        Assert.Equal(key, Assert.Single(reopened.ReadOutstanding()).IdempotencyKey);
    }

    [Fact]
    public void OutstandingAndAttentionSets_AreDisjoint()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        QueuedCommand pending = cache.Enqueue(
            QueuedOperation.CreatePerson, "one", new CreatePersonRequest("A"), DateTimeOffset.UtcNow);

        QueuedCommand conflicted = cache.Enqueue(
            QueuedOperation.UpdatePerson,
            "two",
            new UpdatePersonRequest("B", 3),
            DateTimeOffset.UtcNow,
            targetId: Guid.NewGuid(),
            expectedVersion: 3);

        cache.RecordAttempt(conflicted.Id, QueuedState.Conflict, DateTimeOffset.UtcNow, "moved on", serverVersion: 5);

        Assert.Equal(pending.Id, Assert.Single(cache.ReadOutstanding()).Id);
        Assert.Equal(conflicted.Id, Assert.Single(cache.ReadNeedingAttention()).Id);
        Assert.Equal((1, 1), cache.ReadQueueCounts());
    }

    /// <summary>Order is preserved, because two edits to one record are not commutative.</summary>
    [Fact]
    public void OutstandingCommands_ComeBackOldestFirst()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        DateTimeOffset start = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

        cache.Enqueue(QueuedOperation.CreatePerson, "first", new CreatePersonRequest("A"), start);
        cache.Enqueue(QueuedOperation.CreatePerson, "second", new CreatePersonRequest("B"), start.AddMinutes(1));
        cache.Enqueue(QueuedOperation.CreatePerson, "third", new CreatePersonRequest("C"), start.AddMinutes(2));

        Assert.Equal(
            ["first", "second", "third"],
            cache.ReadOutstanding().Select(x => x.Description).ToArray());
    }

    private static PersonSummaryResponse Person(
        Guid id,
        string name,
        string? title = null,
        string? email = null,
        int version = 1) =>
        new(id, name, title, email, null, "Active", null, null, DateTimeOffset.UtcNow, version);
}
