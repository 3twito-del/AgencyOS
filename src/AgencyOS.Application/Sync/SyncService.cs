using AgencyOS.Application.Authorization;
using AgencyOS.Application.Directory;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Sync;

namespace AgencyOS.Application.Sync;

/// <param name="Sequence">Position in the tenant's feed.</param>
/// <param name="EntityType">Type name of the changed record.</param>
/// <param name="EntityId">Identifier of the changed record.</param>
/// <param name="Kind">Whether the record should be upserted or dropped.</param>
/// <param name="OccurredAt">When the change committed.</param>
public sealed record ChangeEntryModel(
    long Sequence,
    string EntityType,
    Guid EntityId,
    ChangeKind Kind,
    DateTimeOffset OccurredAt);

/// <param name="Cursor">Position to resume from.</param>
/// <param name="HasMore">Whether further changes are waiting.</param>
/// <param name="Changes">Entries in this page, in sequence order.</param>
/// <param name="People">Current state of people named in this page.</param>
/// <param name="Companies">Current state of companies named in this page.</param>
/// <param name="Tasks">Current state of tasks named in this page.</param>
public sealed record SyncPageModel(
    long Cursor,
    bool HasMore,
    IReadOnlyList<ChangeEntryModel> Changes,
    IReadOnlyList<PersonSummaryModel> People,
    IReadOnlyList<CompanySummaryModel> Companies,
    IReadOnlyList<TaskModel> Tasks);

/// <summary>
/// Reads a page of a tenant's change feed together with the current state of
/// everything it names.
/// </summary>
/// <remarks>
/// The page and the hydrated state are read in one transaction. If they were read
/// separately, a record could change between the two reads and the client would
/// store content newer than the cursor it records - and then never be told to
/// re-read it.
/// </remarks>
public interface ISyncQueries
{
    Task<SyncPageModel> ReadChangesAsync(
        OrganizationId organizationId,
        long afterSequence,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the tenant's current head position without reading any changes.</summary>
    Task<long> GetHeadAsync(OrganizationId organizationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Serves the change feed that keeps a client's local cache current.
/// </summary>
/// <remarks>
/// <para>
/// There is one mechanism, not two: a first sync is a read of the feed from
/// position zero, and every later sync is a read from wherever the client got to.
/// The migration that introduces the feed backfills one entry per existing
/// record, so "everything" and "what changed" are the same query. A snapshot
/// endpoint alongside the feed would need its own consistency argument against
/// the feed's, and two arguments are how a sync engine gets subtly wrong.
/// </para>
/// <para>
/// The cost is that a cache rebuilt from zero replays a tenant's whole change
/// history rather than its current state. At one agency's volume that is a
/// bounded, rare operation. Feed compaction - collapsing superseded entries per
/// entity - is the answer if rebuild time is ever measured to be a problem, and
/// is deliberately not built before then.
/// </para>
/// <para>
/// A page carries every record type the cache holds, so reading the feed requires
/// read permission on all of them. Filtering the feed per permission would let a
/// client's cache silently diverge from what it believes it has.
/// </para>
/// </remarks>
public sealed class SyncService
{
    /// <summary>Largest page the feed will return.</summary>
    private const int MaximumTake = 500;

    private const int DefaultTake = 200;

    private readonly ISyncQueries _queries;
    private readonly TenantGuard _guard;

    public SyncService(ISyncQueries queries, TenantGuard guard)
    {
        _queries = queries;
        _guard = guard;
    }

    /// <summary>Reads the next page of changes after <paramref name="cursor"/>.</summary>
    /// <param name="organizationId">Tenant whose feed to read.</param>
    /// <param name="cursor">Last position the client durably applied. Zero for a first sync.</param>
    /// <param name="take">Page size.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<SyncPageModel> ReadChangesAsync(
        OrganizationId organizationId,
        long cursor = 0,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .ReadChangesAsync(
                organizationId,
                Math.Max(cursor, 0),
                Math.Clamp(take ?? DefaultTake, 1, MaximumTake),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the tenant's head position, for a client checking whether it is behind.</summary>
    public async Task<long> GetHeadAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries.GetHeadAsync(organizationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requires read access to every type the cache holds.
    /// </summary>
    /// <remarks>
    /// Deliberately all-or-nothing. A client granted people but not tasks would
    /// otherwise build a cache that looks complete and is not, and would show a
    /// stale task list offline with no way to know it was never allowed to have
    /// one.
    /// </remarks>
    private async Task AuthorizeAsync(OrganizationId organizationId, CancellationToken cancellationToken)
    {
        await _guard.AuthorizeAsync(Permission.PeopleRead, organizationId, cancellationToken).ConfigureAwait(false);
        await _guard.AuthorizeAsync(Permission.CompaniesRead, organizationId, cancellationToken).ConfigureAwait(false);
        await _guard.AuthorizeAsync(Permission.TasksRead, organizationId, cancellationToken).ConfigureAwait(false);
    }
}
