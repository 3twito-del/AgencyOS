using System.Security.Cryptography;
using System.Text;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Integration.Storage;

/// <summary>
/// The contract every blob store must satisfy, whatever it stores bytes in.
/// </summary>
/// <remarks>
/// <para>
/// <c>IBlobStore</c> was written to be implementable by object storage as well as a
/// filesystem — streaming, content-addressed, organization-scoped, with no folders,
/// listing, renaming or signed URLs, because "a wider interface would be one an
/// Azure Blob or S3 implementation had to fake" (ADR-0024). That claim was sound
/// and, until M14, untested: one implementation existed, so nothing distinguished
/// the interface's rules from that implementation's behaviour.
/// </para>
/// <para>
/// This suite is the rules. Each implementation runs all of it, so a second backend
/// can be written against something checkable rather than against a reading of the
/// first one's source (§42, ADR-0037).
/// </para>
/// <para>
/// <strong>What this does not prove.</strong> An in-memory store shares no code with
/// a filesystem, which is enough to show the interface is not filesystem-shaped. It
/// is not a network, so it says nothing about latency, partial failure, retries or
/// multipart upload. A real object-store adapter would have to satisfy these rules
/// and then answer questions this suite cannot ask.
/// </para>
/// </remarks>
public abstract class BlobStoreConformance
{
    private static readonly OrganizationId Tenant = new(Guid.CreateVersion7());
    private static readonly OrganizationId Other = new(Guid.CreateVersion7());

    /// <summary>Creates the store under test.</summary>
    protected abstract IBlobStore CreateStore();

    /// <summary>
    /// The store computes the digest, and it is the digest of what was written.
    /// </summary>
    /// <remarks>
    /// The identity rule the whole document model rests on. A hash the caller
    /// supplied is a claim about identity the system cannot support, so the store
    /// computes it as the bytes pass through and reports what it saw.
    /// </remarks>
    [Fact]
    public async Task ThePutDigestIsTheDigestOfTheBytes()
    {
        IBlobStore store = CreateStore();
        byte[] content = Encoding.UTF8.GetBytes("A contract, filed.");

        BlobWriteResult result = await store.PutAsync(Tenant, new MemoryStream(content));

        Assert.Equal(Expected(content), result.ContentHash);
        Assert.Equal(content.Length, result.ByteLength);
        Assert.False(result.Deduplicated);
    }

    /// <summary>The same bytes twice are stored once.</summary>
    [Fact]
    public async Task IdenticalBytesDeduplicateWithinATenant()
    {
        IBlobStore store = CreateStore();
        byte[] content = Encoding.UTF8.GetBytes("The same attachment, twice.");

        BlobWriteResult first = await store.PutAsync(Tenant, new MemoryStream(content));
        BlobWriteResult second = await store.PutAsync(Tenant, new MemoryStream(content));

        Assert.Equal(first.StorageKey, second.StorageKey);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.False(first.Deduplicated);
        Assert.True(second.Deduplicated);
    }

    /// <summary>
    /// Deduplication never crosses a tenant.
    /// </summary>
    /// <remarks>
    /// The rule with teeth. A store shared across tenants would let one organization
    /// discover another's files by uploading a candidate and watching for a
    /// deduplication hit — a covert channel with a very high signal, and one that
    /// looks like an optimization in the code that introduces it.
    /// </remarks>
    [Fact]
    public async Task DeduplicationDoesNotCrossATenant()
    {
        IBlobStore store = CreateStore();
        byte[] content = Encoding.UTF8.GetBytes("Something both agencies happen to hold.");

        BlobWriteResult mine = await store.PutAsync(Tenant, new MemoryStream(content));
        BlobWriteResult theirs = await store.PutAsync(Other, new MemoryStream(content));

        Assert.False(theirs.Deduplicated);
        Assert.NotEqual(mine.StorageKey, theirs.StorageKey);

        // And one tenant's key names nothing in the other's store.
        Assert.False(await store.ExistsAsync(Other, mine.StorageKey));
    }

    /// <summary>What went in comes back.</summary>
    [Fact]
    public async Task StoredBytesReadBackExactly()
    {
        IBlobStore store = CreateStore();
        byte[] content = RandomNumberGenerator.GetBytes(64 * 1024);

        BlobWriteResult written = await store.PutAsync(Tenant, new MemoryStream(content));

        await using Stream read = await store.OpenReadAsync(Tenant, written.StorageKey);
        using MemoryStream buffer = new();
        await read.CopyToAsync(buffer);

        Assert.Equal(content, buffer.ToArray());
    }

    /// <summary>An empty object is a legitimate object.</summary>
    /// <remarks>
    /// Zero bytes has a well-defined SHA-256, and a store that treated "empty" as
    /// "absent" would lose a real, if unusual, upload.
    /// </remarks>
    [Fact]
    public async Task EmptyContentIsStoredAndFound()
    {
        IBlobStore store = CreateStore();

        BlobWriteResult written = await store.PutAsync(Tenant, new MemoryStream([]));

        Assert.Equal(0, written.ByteLength);
        Assert.Equal(Expected([]), written.ContentHash);
        Assert.True(await store.ExistsAsync(Tenant, written.StorageKey));
    }

    /// <summary>A key that names nothing is an exception, not an empty stream.</summary>
    [Fact]
    public async Task ReadingAnUnknownKeyIsRefused()
    {
        IBlobStore store = CreateStore();

        await Assert.ThrowsAsync<BlobNotFoundException>(
            () => store.OpenReadAsync(Tenant, "nothing/is/here"));

        Assert.False(await store.ExistsAsync(Tenant, "nothing/is/here"));
    }

    /// <summary>Deletion removes, and deleting nothing is not an error.</summary>
    /// <remarks>
    /// The sweeper may run twice over the same orphan, and a second pass must not
    /// fail. Deleting bytes that are already gone has achieved what it was asked to.
    /// </remarks>
    [Fact]
    public async Task DeletionRemovesAndIsIdempotent()
    {
        IBlobStore store = CreateStore();
        byte[] content = Encoding.UTF8.GetBytes("An orphan.");

        BlobWriteResult written = await store.PutAsync(Tenant, new MemoryStream(content));

        await store.DeleteAsync(Tenant, written.StorageKey);

        Assert.False(await store.ExistsAsync(Tenant, written.StorageKey));

        await store.DeleteAsync(Tenant, written.StorageKey);
    }

    /// <summary>Verification agrees with the truth, in both directions.</summary>
    [Fact]
    public async Task VerificationDetectsWhatItShould()
    {
        IBlobStore store = CreateStore();
        byte[] content = Encoding.UTF8.GetBytes("Bytes that should still hash the same.");

        BlobWriteResult written = await store.PutAsync(Tenant, new MemoryStream(content));

        Assert.True(await store.VerifyAsync(Tenant, written.StorageKey, written.ContentHash));

        Assert.False(await store.VerifyAsync(
            Tenant, written.StorageKey, Expected(Encoding.UTF8.GetBytes("different"))));
    }

    /// <summary>
    /// A stream that cannot seek is still storable.
    /// </summary>
    /// <remarks>
    /// An upload arrives over a network. A store that quietly required a seekable
    /// stream would work in every test and fail on the first real request, so the
    /// rule is asserted with a stream that refuses to seek.
    /// </remarks>
    [Fact]
    public async Task AForwardOnlyStreamIsAccepted()
    {
        IBlobStore store = CreateStore();
        byte[] content = Encoding.UTF8.GetBytes("Arriving over a wire.");

        BlobWriteResult written = await store.PutAsync(
            Tenant, new ForwardOnlyStream(content));

        Assert.Equal(Expected(content), written.ContentHash);
        Assert.Equal(content.Length, written.ByteLength);
    }

    private static string Expected(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>A stream with no length and no seeking, like a request body.</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _inner;

        public ForwardOnlyStream(byte[] content) => _inner = new MemoryStream(content);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
