using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Organizations;
using Microsoft.Extensions.Logging;

namespace AgencyOS.Infrastructure.Storage;

/// <summary>Where the content-addressed store keeps its files.</summary>
public sealed class BlobStoreOptions
{
    /// <summary>The directory the store owns entirely.</summary>
    public string RootPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "agencyos-blobs");
}

/// <summary>
/// A content-addressed store on the server's own filesystem.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a local filesystem, and it is not distributed object storage.</strong>
/// It has no replication, no cross-region durability and no story for two servers
/// that do not share a volume. It is behind <see cref="IBlobStore"/> precisely so
/// that saying this stays cheap and replacing it stays possible: an Azure Blob or
/// S3 implementation satisfies the same six methods (ADR-0024).
/// </para>
/// <para>
/// Layout is <c>{root}/{organization}/{aa}/{bb}/{sha256}</c>. Every segment is
/// derived from a digest AgencyOS computed, so no part of a path comes from a
/// caller and traversal is not something to defend against — there is nothing to
/// traverse with. The organization prefix means deduplication cannot cross a
/// tenant boundary, which would otherwise be a way to probe for another tenant's
/// files.
/// </para>
/// <para>
/// Writes are atomic. Bytes go to a staging file, are flushed to disk, and are then
/// moved into place by a rename, which the filesystem performs as one operation. A
/// reader therefore sees a complete file or no file, never half of one.
/// </para>
/// </remarks>
public sealed class FileSystemBlobStore : IBlobStore
{
    private const int BufferSize = 81_920;

    private readonly string _root;
    private readonly ILogger<FileSystemBlobStore> _logger;

    public FileSystemBlobStore(BlobStoreOptions options, ILogger<FileSystemBlobStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _root = Path.GetFullPath(options.RootPath);
        _logger = logger;

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(StagingRoot);
    }

    private string StagingRoot => Path.Combine(_root, ".staging");

    /// <inheritdoc />
    public async Task<BlobWriteResult> PutAsync(
        OrganizationId organizationId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        string stagingPath = Path.Combine(StagingRoot, Guid.CreateVersion7().ToString("N"));

        string digest;
        long length;

        try
        {
            await using (FileStream staging = new(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous))
            {
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                byte[] buffer = new byte[BufferSize];
                length = 0;

                while (true)
                {
                    int read = await content.ReadAsync(buffer, cancellationToken)
                        .ConfigureAwait(false);

                    if (read == 0)
                    {
                        break;
                    }

                    hash.AppendData(buffer, 0, read);

                    await staging.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);

                    length += read;
                }

                // Flushed to the device before the rename, so a crash between the
                // two cannot publish a file the disk has not actually written.
                await staging.FlushAsync(cancellationToken).ConfigureAwait(false);
                staging.Flush(flushToDisk: true);

                digest = Convert.ToHexStringLower(hash.GetCurrentHash());
            }

            string key = KeyFor(organizationId, digest);
            string finalPath = ResolvePath(organizationId, key);

            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            if (File.Exists(finalPath))
            {
                // The organization already holds these exact bytes. The staged copy
                // is redundant: content is addressed by its digest, so the file
                // already there is byte-for-byte the file just written.
                return new BlobWriteResult(key, digest, length, Deduplicated: true);
            }

            try
            {
                // One filesystem operation. A reader sees the complete file or
                // nothing; there is no window in which a half-written file is
                // readable at its final path.
                File.Move(stagingPath, finalPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                // Lost a race with an identical concurrent upload. The winner wrote
                // the same bytes - the digest says so - which makes this a
                // deduplication rather than a conflict.
                return new BlobWriteResult(key, digest, length, Deduplicated: true);
            }

            return new BlobWriteResult(key, digest, length, Deduplicated: false);
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                TryDelete(stagingPath);
            }
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(
        OrganizationId organizationId,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(organizationId, storageKey);

        if (!File.Exists(path))
        {
            throw new BlobNotFoundException(storageKey);
        }

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answers, never raises. A key belonging to another organization, or one this
    /// store did not make, is simply not here — which is the true answer and the
    /// only one a caller asking "does this exist" can use. Throwing would also make
    /// a foreign key distinguishable from an absent one, which is a difference
    /// nobody outside the tenant should be able to observe (ADR-0037).
    /// </remarks>
    public Task<bool> ExistsAsync(
        OrganizationId organizationId,
        string storageKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            TryResolvePath(organizationId, storageKey, out string? path) && File.Exists(path));

    /// <inheritdoc />
    public Task DeleteAsync(
        OrganizationId organizationId,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        // The orphan sweeper may meet a key it cannot resolve, and a sweep that
        // threw on one row would stop before the rest. Nothing to delete is the
        // outcome deletion was asked for.
        if (!TryResolvePath(organizationId, storageKey, out string? path))
        {
            return Task.CompletedTask;
        }

        if (File.Exists(path))
        {
            File.Delete(path);

            _logger.LogInformation(
                "Deleted an unreferenced stored object for organization {OrganizationId}.",
                organizationId.Value);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> VerifyAsync(
        OrganizationId organizationId,
        string storageKey,
        string expectedContentHash,
        CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(organizationId, storageKey);

        if (!File.Exists(path))
        {
            throw new BlobNotFoundException(storageKey);
        }

        await using FileStream file = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);

        byte[] computed = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);

        return string.Equals(
            Convert.ToHexStringLower(computed),
            expectedContentHash,
            StringComparison.Ordinal);
    }

    /// <summary>Builds the key for a digest within one organization.</summary>
    /// <remarks>
    /// Two levels of fan-out from the digest's own first bytes, so no directory
    /// accumulates a million entries. The organization is the outermost segment, so
    /// a tenant's content is contiguous on disk and dedup cannot reach across.
    /// </remarks>
    private static string KeyFor(OrganizationId organizationId, string digest) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{organizationId.Value:N}/{digest[..2]}/{digest[2..4]}/{digest}");

    /// <summary>
    /// Turns a key into a path, refusing anything that is not a key this store made.
    /// </summary>
    /// <remarks>
    /// Keys are derived from digests and never from user input, so this should be
    /// unreachable. It is here because "should be unreachable" is exactly the
    /// assumption a future caller will break, and the consequence would be reading
    /// or deleting an arbitrary file on the server.
    /// </remarks>
    private string ResolvePath(OrganizationId organizationId, string storageKey) =>
        TryResolvePath(organizationId, storageKey, out string? path)
            ? path
            : throw new BlobNotFoundException(storageKey);

    /// <summary>
    /// Turns a key into a path, or reports that it is not one of this store's.
    /// </summary>
    /// <remarks>
    /// The same checks, without deciding what the caller should do about a failure.
    /// Reading a key that names nothing is an error worth raising; asking whether
    /// one exists, or deleting one, is not.
    /// </remarks>
    private bool TryResolvePath(
        OrganizationId organizationId,
        string storageKey,
        [NotNullWhen(true)] out string? path)
    {
        path = null;

        string expectedPrefix = organizationId.Value.ToString("N", CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(storageKey)
            || !storageKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (string segment in storageKey.Split('/'))
        {
            bool safe = segment.Length is > 0 and <= 64 && segment.All(char.IsAsciiHexDigit);

            if (!safe)
            {
                return false;
            }
        }

        string candidate = Path.GetFullPath(Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar)));

        // Belt and braces: even a key that passed the checks above must land inside
        // the root the store owns.
        if (!candidate.StartsWith(_root, StringComparison.Ordinal))
        {
            return false;
        }

        path = candidate;

        return true;
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException failure)
        {
            // A staged file that could not be removed is collected by the sweeper.
            // Failing the upload over it would turn a tidiness problem into a lost
            // document.
            _logger.LogWarning(failure, "Could not remove a staged upload.");
        }
    }
}
