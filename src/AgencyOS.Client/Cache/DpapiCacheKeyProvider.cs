using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace AgencyOS.Client.Cache;

/// <summary>
/// Protects the cache key with Windows DPAPI, scoped to the current user.
/// </summary>
/// <remarks>
/// <para>
/// The key is 32 random bytes, generated once and written next to the database as
/// a DPAPI blob under <see cref="DataProtectionScope.CurrentUser"/>. Another
/// account on the same machine cannot unprotect it, which is what makes the
/// per-user cache directory a real boundary rather than a naming convention.
/// </para>
/// <para>
/// Nothing here is derived from the repository, the build, or the tenant
/// identifier: a key that can be recomputed from public inputs is not a key. If
/// the blob is lost or unreadable - a restored profile, a different machine - the
/// cache is rebuilt from the change feed, which is a supported operation because
/// the cache is not canonical.
/// </para>
/// <para>
/// Verified against SQLCipher before adoption: the database file has no readable
/// plaintext and a wrong key is refused with SQLite error 26.
/// <c>docs/adr/ADR-0015-local-cache-encryption.md</c> records the evidence.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DpapiCacheKeyProvider : ILocalCacheKeyProvider
{
    /// <summary>Name of the protected key blob, beside the database it unlocks.</summary>
    public const string KeyFileName = "cache.key";

    /// <summary>
    /// Additional entropy mixed into the protection.
    /// </summary>
    /// <remarks>
    /// Not a secret and not pretending to be one. It scopes the blob to this
    /// application so that another program running as the same user cannot
    /// unprotect it by pointing DPAPI at the file.
    /// </remarks>
    private static readonly byte[] Entropy =
        System.Text.Encoding.UTF8.GetBytes("AgencyOS.LocalCache.v1");

    public string GetOrCreateKey(string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        Directory.CreateDirectory(cacheDirectory);

        string path = Path.Combine(cacheDirectory, KeyFileName);

        if (File.Exists(path))
        {
            try
            {
                byte[] unprotected = ProtectedData.Unprotect(
                    File.ReadAllBytes(path),
                    Entropy,
                    DataProtectionScope.CurrentUser);

                return Convert.ToHexStringLower(unprotected);
            }
            catch (CryptographicException)
            {
                // The blob exists but this user cannot unprotect it: a restored
                // profile, a copied directory, or a different account. Starting
                // over is correct and safe - the cache holds no truth - and it
                // beats refusing to launch.
                File.Delete(path);
            }
        }

        byte[] key = RandomNumberGenerator.GetBytes(32);

        File.WriteAllBytes(path, ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));

        return Convert.ToHexStringLower(key);
    }

    public void Discard(string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        string path = Path.Combine(cacheDirectory, KeyFileName);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Supplies a caller-provided key.
/// </summary>
/// <remarks>
/// For tests and for any host that manages key material itself. Deliberately not
/// the default: a client that takes its key from configuration is a client whose
/// key ends up in a configuration file.
/// </remarks>
public sealed class ExplicitCacheKeyProvider : ILocalCacheKeyProvider
{
    private readonly string _key;

    public ExplicitCacheKeyProvider(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _key = key;
    }

    public string GetOrCreateKey(string cacheDirectory) => _key;

    public void Discard(string cacheDirectory)
    {
        // Nothing to discard: the key is held by the caller.
    }
}
