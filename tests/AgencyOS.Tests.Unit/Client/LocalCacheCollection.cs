using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// Serializes every test that opens a local cache.
/// </summary>
/// <remarks>
/// <para>
/// These tests share one piece of process-wide state they cannot avoid:
/// <c>SqliteConnection.ClearAllPools()</c>. Closing an encrypted SQLite file so it
/// can be reopened, deleted, or inspected raw requires draining the pool, and the
/// pool is global. Run two such classes in parallel and one clears connections the
/// other is still holding, which surfaces as
/// <c>ObjectDisposedException: SQLitePCL.sqlite3</c> from whichever test happened
/// to be mid-query.
/// </para>
/// <para>
/// xUnit parallelizes across collections, so naming one collection is the whole
/// fix: these classes then run one at a time while the rest of the suite still
/// runs in parallel around them. The alternative - disabling pooling - would test
/// a configuration the application does not use.
/// </para>
/// <para>
/// The hazard predates M4 and was simply not dense enough to fire; adding the
/// migration tests made it reproducible.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalCacheCollection
{
    public const string Name = "local-cache";
}
