using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Abstractions;

/// <summary>What a completed write to the blob store produced.</summary>
/// <param name="StorageKey">Where the bytes went. Never returned by the API.</param>
/// <param name="ContentHash">Lowercase hex SHA-256 of what was actually written.</param>
/// <param name="ByteLength">How many bytes were written.</param>
/// <param name="Deduplicated">
/// Whether the store already held these exact bytes for this organization. A true
/// value is a storage saving and nothing more: it never changes who may read a
/// document (ADR-0024).
/// </param>
public sealed record BlobWriteResult(
    string StorageKey,
    string ContentHash,
    long ByteLength,
    bool Deduplicated);

/// <summary>
/// Durable storage for immutable bytes.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrow. Everything AgencyOS needs from object storage is here:
/// write bytes and learn their digest, read them back, ask whether they exist, and
/// delete when policy allows. Nothing about folders, listing, renaming, metadata or
/// signed URLs, because a wider interface would be one an Azure Blob or S3
/// implementation had to fake (ADR-0024).
/// </para>
/// <para>
/// Writes stream. A method taking <c>byte[]</c> would put a two-gigabyte screener
/// in the managed heap to store it, and the only reason it would ever be written
/// that way is that it was easier to test.
/// </para>
/// <para>
/// The store is content-addressed and <strong>scoped to an organization</strong>. A
/// store shared across tenants would let one tenant discover another's files by
/// uploading a candidate and watching for a deduplication hit, which is a covert
/// channel with a very high signal.
/// </para>
/// </remarks>
public interface IBlobStore
{
    /// <summary>
    /// Writes bytes and reports the digest of what was actually written.
    /// </summary>
    /// <remarks>
    /// The digest is computed by the store as the bytes pass through, never taken
    /// from the caller. A hash somebody else computed is a claim about identity
    /// this system cannot support.
    /// </remarks>
    Task<BlobWriteResult> PutAsync(
        OrganizationId organizationId,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Opens stored bytes for reading.</summary>
    /// <exception cref="BlobNotFoundException">The key names nothing.</exception>
    Task<Stream> OpenReadAsync(
        OrganizationId organizationId,
        string storageKey,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        OrganizationId organizationId,
        string storageKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes bytes.
    /// </summary>
    /// <remarks>
    /// Called only by the orphan sweeper, and only for bytes nothing references.
    /// Archiving a document never reaches here: archiving hides a document and
    /// deletion destroys evidence, and M10 does not confuse the two (ADR-0024).
    /// </remarks>
    Task DeleteAsync(
        OrganizationId organizationId,
        string storageKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads back what is stored and checks it still hashes to what it claims.
    /// </summary>
    /// <remarks>
    /// Not called on the read path, where it would double every download. It exists
    /// so corruption is detectable at all rather than discovered by a person opening
    /// a broken file.
    /// </remarks>
    Task<bool> VerifyAsync(
        OrganizationId organizationId,
        string storageKey,
        string expectedContentHash,
        CancellationToken cancellationToken = default);
}

/// <summary>Raised when a storage key names nothing.</summary>
/// <remarks>
/// A distinct exception because the response differs from an ordinary missing
/// record: a document row whose bytes are gone is a system fault worth surfacing as
/// one, not a 404 that looks like a typo in a URL.
/// </remarks>
public sealed class BlobNotFoundException : Exception
{
    public BlobNotFoundException(string storageKey)
        : base($"No stored object at '{storageKey}'.") => StorageKey = storageKey;

    public string StorageKey { get; }
}

/// <summary>Raised when stored bytes no longer hash to what the record claims.</summary>
public sealed class BlobIntegrityException : Exception
{
    public BlobIntegrityException(string storageKey, string expected, string actual)
        : base($"The bytes at '{storageKey}' hash to {actual}, but the record says {expected}.")
    {
        StorageKey = storageKey;
        Expected = expected;
        Actual = actual;
    }

    public string StorageKey { get; }

    public string Expected { get; }

    public string Actual { get; }
}

/// <summary>
/// Encrypts secrets that must be stored but never read by a person.
/// </summary>
/// <remarks>
/// <para>
/// OAuth refresh tokens are the only thing that goes through this in M10, and they
/// are the reason it exists: a refresh token in a database column is a standing
/// grant to read somebody's mail, readable by anybody who can read a backup
/// (ADR-0027).
/// </para>
/// <para>
/// The ALPHA implementation uses ASP.NET Core Data Protection with keys on the
/// server's own disk. That protects against a leaked database and not against a
/// compromised server, which is a real limitation stated rather than glossed over.
/// Key custody moves to a managed store in M15.
/// </para>
/// </remarks>
public interface ISecretProtector
{
    /// <summary>Encrypts a secret for storage.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a stored secret, for the length of one provider call.</summary>
    /// <exception cref="SecretProtectionException">
    /// The ciphertext cannot be read: a rotated or lost key, or a tampered value.
    /// </exception>
    string Unprotect(string ciphertext);
}

/// <summary>Raised when a stored secret cannot be decrypted.</summary>
/// <remarks>
/// Recoverable in exactly one way: a person reconnects the mailbox. The message
/// deliberately carries no ciphertext, no key identifier and no fragment of either.
/// </remarks>
public sealed class SecretProtectionException : Exception
{
    public SecretProtectionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
