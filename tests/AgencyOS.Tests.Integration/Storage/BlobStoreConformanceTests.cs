using System.Collections.Concurrent;
using System.Security.Cryptography;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgencyOS.Tests.Integration.Storage;

/// <summary>The shipped store, against the contract.</summary>
public sealed class FileSystemBlobStoreConformanceTests : BlobStoreConformance, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "agencyos-conformance", Guid.NewGuid().ToString("N"));

    protected override IBlobStore CreateStore() =>
        new FileSystemBlobStore(
            new BlobStoreOptions { RootPath = _root },
            NullLogger<FileSystemBlobStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

/// <summary>
/// A second store that shares no code with the first.
/// </summary>
/// <remarks>
/// <para>
/// The point of this class is that it is not a filesystem. If the interface had
/// quietly assumed one — a path, a rename, a directory, a length known in advance —
/// this implementation could not satisfy the same suite, and the claim that
/// <c>IBlobStore</c> could be backed by object storage would be untested belief.
/// </para>
/// <para>
/// It lives in the test project deliberately. Shipping an in-memory blob store
/// would put a configuration option in reach whose effect is that documents
/// disappear when the process restarts.
/// </para>
/// </remarks>
public sealed class InMemoryBlobStoreConformanceTests : BlobStoreConformance
{
    protected override IBlobStore CreateStore() => new InMemoryBlobStore();
}

/// <summary>A blob store with no filesystem anywhere in it.</summary>
internal sealed class InMemoryBlobStore : IBlobStore
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public async Task<BlobWriteResult> PutAsync(
        OrganizationId organizationId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        using MemoryStream buffer = new();

        // Copied rather than measured, because the contract admits a stream whose
        // length is unknown until it ends.
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        byte[] bytes = buffer.ToArray();
        string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string key = Key(organizationId, digest);

        bool existed = _objects.ContainsKey(key);

        _objects[key] = bytes;

        return new BlobWriteResult(key, digest, bytes.LongLength, existed);
    }

    public Task<Stream> OpenReadAsync(
        OrganizationId organizationId,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        if (!Owned(organizationId, storageKey)
            || !_objects.TryGetValue(storageKey, out byte[]? bytes))
        {
            throw new BlobNotFoundException(storageKey);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task<bool> ExistsAsync(
        OrganizationId organizationId,
        string storageKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Owned(organizationId, storageKey) && _objects.ContainsKey(storageKey));

    public Task DeleteAsync(
        OrganizationId organizationId,
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        if (Owned(organizationId, storageKey))
        {
            _objects.TryRemove(storageKey, out _);
        }

        return Task.CompletedTask;
    }

    public Task<bool> VerifyAsync(
        OrganizationId organizationId,
        string storageKey,
        string expectedContentHash,
        CancellationToken cancellationToken = default)
    {
        if (!Owned(organizationId, storageKey)
            || !_objects.TryGetValue(storageKey, out byte[]? bytes))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(string.Equals(
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            expectedContentHash,
            StringComparison.Ordinal));
    }

    /// <summary>The organization prefix is what keeps deduplication inside a tenant.</summary>
    private static string Key(OrganizationId organizationId, string digest) =>
        $"{organizationId.Value:N}/{digest}";

    private static bool Owned(OrganizationId organizationId, string storageKey) =>
        storageKey.StartsWith($"{organizationId.Value:N}/", StringComparison.Ordinal);
}
