using AgencyOS.Client.Cache;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// Bringing an M3-era cache forward to the M4 schema.
/// </summary>
/// <remarks>
/// <para>
/// M3 shipped schema version 1 and, having no second version to move to, left
/// <c>Prepare</c> throwing for anything older than current. That was fine while
/// nothing had changed and a latent defect the moment something did: the write
/// queue holds commands the server has never seen, so discarding the file to
/// avoid writing a migration would lose a user's work.
/// </para>
/// <para>
/// M4 is the first schema change, so these tests exist to prove the path works
/// before anybody's cache depends on it.
/// </para>
/// </remarks>
[Collection(LocalCacheCollection.Name)]
public sealed class CacheMigrationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "agencyos-cache-migration", Guid.NewGuid().ToString("N"));

    private static readonly LocalCacheIdentity Identity =
        new("LAB", Guid.Parse("33333333-3333-3333-3333-333333333333"), "agent@example.invalid");

    private static readonly ExplicitCacheKeyProvider Key =
        new("0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0");

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
                // Not worth failing a test over.
            }
        }
    }

    [Fact]
    public void AFreshCache_IsCreatedAtTheCurrentVersion()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        Assert.Equal(0, cache.ReadTalentCount());
        Assert.Equal(2, LocalCacheSchema.Version);
    }

    /// <summary>
    /// The property that matters: migrating never loses an unsent command.
    /// </summary>
    /// <remarks>
    /// Everything else in the cache came from the server and can be refetched.
    /// Queued commands cannot: they exist nowhere else until they are submitted.
    /// </remarks>
    [Fact]
    public void UpgradingFromVersionOne_KeepsEveryQueuedCommand()
    {
        Guid personId = Guid.NewGuid();
        string key;
        Guid queuedId;

        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            cache.ApplyPage(
                12,
                [Person(personId, "Ada Reyes")],
                [],
                [],
                [],
                DateTimeOffset.UtcNow);

            QueuedCommand queued = cache.Enqueue(
                QueuedOperation.UpdatePerson,
                "Rename Ada Reyes",
                new UpdatePersonRequest("Ada", 1),
                DateTimeOffset.UtcNow,
                targetId: personId,
                expectedVersion: 1);

            key = queued.IdempotencyKey;
            queuedId = queued.Id;
        }

        RewindToVersionOne();

        using LocalCache upgraded = LocalCache.Open(_root, Identity, Key);

        // The queued command survived, with the key that makes its retry safe.
        QueuedCommand recovered = Assert.Single(upgraded.ReadOutstanding());

        Assert.Equal(queuedId, recovered.Id);
        Assert.Equal(key, recovered.IdempotencyKey);

        // So did the cached records and the cursor.
        Assert.Equal(12, upgraded.ReadCursor());
        Assert.Equal("Ada Reyes", Assert.Single(upgraded.ReadPeople()).DisplayName);

        // And the new table exists, empty, ready to fill from the feed.
        Assert.Equal(0, upgraded.ReadTalentCount());
    }

    /// <summary>The upgrade is recorded, so it happens once rather than on every open.</summary>
    [Fact]
    public void UpgradingFromVersionOne_StampsTheNewVersion()
    {
        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            // Created at the current version.
        }

        RewindToVersionOne();

        using (LocalCache upgraded = LocalCache.Open(_root, Identity, Key))
        {
            Assert.Equal(0, upgraded.ReadTalentCount());
        }

        SqliteConnection.ClearAllPools();

        // Opening again must not try to create the table a second time.
        using LocalCache reopened = LocalCache.Open(_root, Identity, Key);

        Assert.Equal(0, reopened.ReadTalentCount());
    }

    /// <summary>
    /// The cursor is deliberately not rewound by the upgrade.
    /// </summary>
    /// <remarks>
    /// The talent table fills from the change feed like any other, on the next
    /// synchronization. Rewinding to refill it faster would replay the tenant's
    /// whole history for no benefit.
    /// </remarks>
    [Fact]
    public void UpgradingFromVersionOne_LeavesTheCursorAlone()
    {
        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            cache.ApplyPage(99, [], [], [], [], DateTimeOffset.UtcNow);
        }

        RewindToVersionOne();

        using LocalCache upgraded = LocalCache.Open(_root, Identity, Key);

        Assert.Equal(99, upgraded.ReadCursor());
    }

    /// <summary>A cache from the future is still refused rather than guessed at.</summary>
    [Fact]
    public void ACacheFromTheFuture_IsStillRefused()
    {
        using (LocalCache cache = LocalCache.Open(_root, Identity, Key))
        {
            // Created at the current version.
        }

        SetVersion(99);

        LocalCacheUnusableException failure = Assert.Throws<LocalCacheUnusableException>(
            () => LocalCache.Open(_root, Identity, Key));

        Assert.Contains("newer version", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------- talent caching

    [Fact]
    public void CachedTalent_IsReadableOffline()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        Guid personId = Guid.NewGuid();

        cache.ApplyPage(
            1,
            [],
            [],
            [],
            [],
            DateTimeOffset.UtcNow,
            [Talent(personId, "Ada Reyes", isClient: true, "Active")]);

        TalentSummaryResponse cached = Assert.Single(cache.ReadTalent());

        Assert.Equal("Ada Reyes", cached.DisplayName);
        Assert.True(cached.IsClient);
        Assert.Equal("Active", cached.RepresentationStatus);
        Assert.Equal(["Writer"], cached.Disciplines);
        Assert.Equal(["Television"], cached.Scopes);
        Assert.Equal(1, cache.ReadTalentCount());
    }

    [Fact]
    public void CachedTalent_CanBeNarrowedToClients()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        cache.ApplyPage(
            1,
            [],
            [],
            [],
            [],
            DateTimeOffset.UtcNow,
            [
                Talent(Guid.NewGuid(), "Ada Reyes", isClient: true, "Active"),
                Talent(Guid.NewGuid(), "Bo Ferreira", isClient: false, null),
            ]);

        Assert.Equal(2, cache.ReadTalent().Count);
        Assert.Equal("Ada Reyes", Assert.Single(cache.ReadTalent(clientsOnly: true)).DisplayName);
    }

    /// <summary>A talent row is keyed by person, so a later page replaces it.</summary>
    [Fact]
    public void ALaterPage_ReplacesTheCachedTalentRow()
    {
        using LocalCache cache = LocalCache.Open(_root, Identity, Key);

        Guid personId = Guid.NewGuid();

        cache.ApplyPage(
            1, [], [], [], [], DateTimeOffset.UtcNow,
            [Talent(personId, "Ada Reyes", isClient: false, "Pending")]);

        cache.ApplyPage(
            2, [], [], [], [], DateTimeOffset.UtcNow,
            [Talent(personId, "Ada Reyes", isClient: true, "Active")]);

        TalentSummaryResponse cached = Assert.Single(cache.ReadTalent());

        Assert.True(cached.IsClient);
        Assert.Equal("Active", cached.RepresentationStatus);
    }

    // ------------------------------------------------------------- plumbing

    /// <summary>
    /// Puts the file back the way an M3 build would have left it.
    /// </summary>
    /// <remarks>
    /// Drops the table version 2 added and restores the stored version, so the
    /// upgrade path is exercised for real rather than simulated.
    /// </remarks>
    private void RewindToVersionOne()
    {
        SqliteConnection.ClearAllPools();

        using SqliteConnection connection = OpenRaw();

        using (SqliteCommand drop = connection.CreateCommand())
        {
            drop.CommandText = "DROP TABLE IF EXISTS cached_talent;";
            drop.ExecuteNonQuery();
        }

        LocalCacheSchema.SetMeta(connection, "schema_version", "1");

        connection.Close();
        SqliteConnection.ClearAllPools();
    }

    private void SetVersion(int version)
    {
        SqliteConnection.ClearAllPools();

        using SqliteConnection connection = OpenRaw();

        LocalCacheSchema.SetMeta(
            connection,
            "schema_version",
            version.ToString(System.Globalization.CultureInfo.InvariantCulture));

        connection.Close();
        SqliteConnection.ClearAllPools();
    }

    private SqliteConnection OpenRaw()
    {
        SQLitePCL.Batteries_V2.Init();

        string directory = Path.Combine(_root, Identity.DirectoryName);

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = Path.Combine(directory, LocalCache.DatabaseFileName),
        };

        SqliteConnection connection = new(builder.ConnectionString);

        connection.Open();

        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA key = \"x'{Key.GetOrCreateKey(directory)}'\";";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static PersonSummaryResponse Person(Guid id, string name) =>
        new(id, name, null, null, null, "Active", null, null, DateTimeOffset.UtcNow, 1);

    private static TalentSummaryResponse Talent(Guid personId, string name, bool isClient, string? status) =>
        new(
            Guid.NewGuid(),
            personId,
            name,
            "Established",
            ["Writer"],
            status,
            isClient,
            isClient ? Guid.NewGuid() : null,
            isClient ? "Marcus Reid" : null,
            isClient ? ["Television"] : [],
            DateTimeOffset.UtcNow,
            1);
}
