using System.Runtime.Versioning;
using AgencyOS.Client.Cache;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// The key provider the shipped client actually uses.
/// </summary>
/// <remarks>
/// <para>
/// Every other cache test supplies an explicit key, which exercises the cache but
/// not the thing that unlocks it in production. A DPAPI path that threw would mean
/// no cache at all on a real workstation, and no other test would notice.
/// </para>
/// <para>
/// DPAPI is Windows-only, and the CI job that runs this project is
/// <c>Build and unit tests (Windows)</c>, so these assertions do run on every
/// build. The guard only affects a developer running unit tests on Linux, where
/// the API does not exist to be tested.
/// </para>
/// </remarks>
[Collection(LocalCacheCollection.Name)]
public sealed class DpapiCacheKeyProviderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "agencyos-dpapi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directory))
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Not worth failing a test over.
            }
        }
    }

    /// <summary>The generated key is a 256-bit hex string, which is what raw-key mode needs.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void GeneratedKey_Is256BitsOfHex()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string key = new DpapiCacheKeyProvider().GetOrCreateKey(_directory);

        Assert.Equal(64, key.Length);
        Assert.All(key, character => Assert.True(Uri.IsHexDigit(character), $"'{character}' is not hex."));
    }

    /// <summary>The same directory yields the same key, or a cache would be unreadable after a restart.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheSameDirectory_YieldsTheSameKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DpapiCacheKeyProvider provider = new();

        Assert.Equal(provider.GetOrCreateKey(_directory), provider.GetOrCreateKey(_directory));
    }

    /// <summary>Two caches get two keys: the key is per cache, not per machine.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void DifferentDirectories_GetDifferentKeys()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DpapiCacheKeyProvider provider = new();

        string first = provider.GetOrCreateKey(Path.Combine(_directory, "one"));
        string second = provider.GetOrCreateKey(Path.Combine(_directory, "two"));

        Assert.NotEqual(first, second);
    }

    /// <summary>The key on disk is a protected blob, not the key itself.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheStoredBlob_IsNotTheKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string key = new DpapiCacheKeyProvider().GetOrCreateKey(_directory);

        byte[] stored = File.ReadAllBytes(Path.Combine(_directory, DpapiCacheKeyProvider.KeyFileName));

        Assert.DoesNotContain(key, Convert.ToHexStringLower(stored), StringComparison.OrdinalIgnoreCase);

        // A DPAPI blob carries overhead, so it is never merely the 32 raw bytes.
        Assert.True(stored.Length > 32, $"The stored blob is {stored.Length} bytes; it looks unprotected.");
    }

    /// <summary>
    /// An unreadable blob is replaced rather than fatal.
    /// </summary>
    /// <remarks>
    /// A restored profile or a copied directory produces a blob this user cannot
    /// unprotect. Starting over is correct and safe - the cache holds no truth - and
    /// it beats refusing to launch.
    /// </remarks>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void ACorruptBlob_IsReplacedRatherThanFatal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DpapiCacheKeyProvider provider = new();

        string original = provider.GetOrCreateKey(_directory);
        string path = Path.Combine(_directory, DpapiCacheKeyProvider.KeyFileName);

        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);

        string replacement = provider.GetOrCreateKey(_directory);

        Assert.Equal(64, replacement.Length);
        Assert.NotEqual(original, replacement);
    }

    /// <summary>Discarding the key means the next open creates a fresh database.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Discard_RemovesTheStoredKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DpapiCacheKeyProvider provider = new();

        provider.GetOrCreateKey(_directory);
        provider.Discard(_directory);

        Assert.False(File.Exists(Path.Combine(_directory, DpapiCacheKeyProvider.KeyFileName)));
    }

    /// <summary>
    /// The whole path works end to end: a DPAPI-keyed cache opens, writes and reopens.
    /// </summary>
    /// <remarks>
    /// The assertion that matters most, because it is the exact sequence a real
    /// workstation performs on every launch.
    /// </remarks>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void ADpapiKeyedCache_OpensWritesAndReopens()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        LocalCacheIdentity identity = new("LAB", Guid.NewGuid(), "agent@example.invalid");
        DpapiCacheKeyProvider provider = new();

        using (LocalCache cache = LocalCache.Open(_directory, identity, provider))
        {
            cache.ApplyPage(
                4,
                [new Contracts.PeopleSlice.PersonSummaryResponse(
                    Guid.NewGuid(), "Sarah Klein", null, null, null, "Active", null, null,
                    DateTimeOffset.UtcNow, 1)],
                [],
                [],
                [],
                DateTimeOffset.UtcNow);
        }

        SqliteConnection.ClearAllPools();

        using LocalCache reopened = LocalCache.Open(_directory, identity, provider);

        Assert.Equal(4, reopened.ReadCursor());
        Assert.Equal("Sarah Klein", Assert.Single(reopened.ReadPeople()).DisplayName);
    }
}
