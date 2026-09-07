using System.Globalization;
using System.Security.Cryptography;

namespace AgencyOS.Client.Cache;

/// <summary>
/// Where a cache lives and what unlocks it.
/// </summary>
/// <remarks>
/// <para>
/// One cache per channel, tenant and user. Isolation is by path, not by a column,
/// so a FORGE build cannot read an ALPHA cache even by accident, and two users on
/// one workstation never share a file. <c>docs/05_RELEASE_RINGS.md</c> forbids
/// mixing ring data; this is the client's half of that rule.
/// </para>
/// <para>
/// The key is generated on first use, protected with DPAPI for the current user
/// and stored beside the database. It is never derived from anything in the
/// repository, never committed, and never travels. Losing it costs a rebuild from
/// the change feed, which is a supported operation rather than data loss - the
/// cache is not canonical.
/// </para>
/// </remarks>
/// <param name="Channel">Release ring this build belongs to.</param>
/// <param name="OrganizationId">Tenant whose records are cached.</param>
/// <param name="UserSubject">Identity of the signed-in user.</param>
public sealed record LocalCacheIdentity(string Channel, Guid OrganizationId, string UserSubject)
{
    /// <summary>Builds the per-identity directory name.</summary>
    /// <remarks>
    /// The user's subject is hashed rather than used directly: a subject may be an
    /// email address, and an email address in a file path is both a path-safety
    /// problem and a small privacy leak to anything that lists the directory.
    /// </remarks>
    public string DirectoryName
    {
        get
        {
            byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(UserSubject));

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Sanitize(Channel)}-{OrganizationId:N}-{Convert.ToHexStringLower(hash)[..16]}");
        }
    }

    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        int written = 0;

        foreach (char character in value)
        {
            buffer[written++] = char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-';
        }

        return written == 0 ? "unknown" : new string(buffer[..written]);
    }
}

/// <summary>
/// Supplies the encryption key for a local cache.
/// </summary>
/// <remarks>
/// An interface so the cache can be tested with an explicit key on any platform,
/// while the shipped Windows client uses DPAPI. A test that needed the real
/// protector would be a test that only runs on one machine.
/// </remarks>
public interface ILocalCacheKeyProvider
{
    /// <summary>Gets the key for this cache, creating and protecting one if needed.</summary>
    string GetOrCreateKey(string cacheDirectory);

    /// <summary>Discards the stored key, so the next open creates a new database.</summary>
    void Discard(string cacheDirectory);
}
