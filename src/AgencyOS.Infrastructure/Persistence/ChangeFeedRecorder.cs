using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Sync;
using AgencyOS.Domain.Talent;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>A change observed in the tracker, before it has a feed position.</summary>
/// <param name="OrganizationId">Tenant whose feed the entry belongs to.</param>
/// <param name="EntityType">Type name, matching the audit vocabulary.</param>
/// <param name="EntityId">Identifier of the changed record.</param>
/// <param name="Kind">Whether a client should upsert or drop the record.</param>
internal sealed record PendingChange(
    OrganizationId OrganizationId,
    string EntityType,
    string EntityId,
    ChangeKind Kind);

/// <summary>
/// Turns tracked entity changes into change-feed entries with commit-ordered
/// positions.
/// </summary>
/// <remarks>
/// <para>
/// Derived from the change tracker rather than recorded by each handler. A
/// handler that forgets to record a change produces a client cache that is wrong
/// and does not know it, and "every handler remembers" is not a property anyone
/// can verify. Deriving it means the feed is correct by construction for every
/// command that already exists and every one that has not been written yet.
/// </para>
/// <para>
/// Positions come from the tenant's counter row, allocated with an
/// <c>UPDATE … RETURNING</c> inside the same transaction as the business change.
/// Holding that row lock until commit is what makes sequence order equal commit
/// order: a transaction that allocated a lower position must commit before a
/// higher one can be allocated at all, so a client that has read up to position
/// <c>N</c> cannot later discover a gap below it. A bare <c>BIGSERIAL</c> gives no
/// such guarantee. See <c>docs/adr/ADR-0013-synchronization-architecture.md</c>.
/// </para>
/// </remarks>
internal static class ChangeFeedRecorder
{
    /// <summary>
    /// The record types a client caches, and therefore the types the feed carries.
    /// </summary>
    /// <remarks>
    /// Relationships and interactions are deliberately absent in M3: they are read
    /// online from a person's detail and timeline, and adding them to the cache
    /// would mean answering what an offline timeline means when half its inputs
    /// are stale. The feed can gain a type without a protocol change.
    /// </remarks>
    public static IReadOnlyList<PendingChange> Collect(ChangeTracker tracker)
    {
        List<PendingChange> pending = [];

        foreach (EntityEntry<Person> entry in tracker.Entries<Person>())
        {
            Add(pending, entry.State, entry.Entity.OrganizationId, nameof(Person), entry.Entity.Id.ToString());
        }

        foreach (EntityEntry<Company> entry in tracker.Entries<Company>())
        {
            Add(pending, entry.State, entry.Entity.OrganizationId, nameof(Company), entry.Entity.Id.ToString());
        }

        foreach (EntityEntry<TaskItem> entry in tracker.Entries<TaskItem>())
        {
            Add(pending, entry.State, entry.Entity.OrganizationId, nameof(TaskItem), entry.Entity.Id.ToString());
        }

        // Talent entries are keyed by person, not by profile. The cached row is a
        // summary that denormalizes representation status, lead and scopes, so a
        // representation change has to invalidate it too - and the person is the
        // only identifier both records share.
        foreach (EntityEntry<TalentProfile> entry in tracker.Entries<TalentProfile>())
        {
            Add(
                pending,
                entry.State,
                entry.Entity.OrganizationId,
                nameof(TalentProfile),
                entry.Entity.PersonId.ToString());
        }

        foreach (EntityEntry<Representation> entry in tracker.Entries<Representation>())
        {
            Add(
                pending,
                entry.State,
                entry.Entity.OrganizationId,
                nameof(TalentProfile),
                entry.Entity.PersonId.ToString());
        }

        // One entry per person per commit. Changing a profile and its representation
        // together is one thing happening, and the client only needs telling once.
        return
        [
            .. pending
                .GroupBy(x => (x.OrganizationId, x.EntityType, x.EntityId))
                .Select(g => g.First()),
        ];
    }

    /// <summary>
    /// Allocates positions and adds the feed entries to the same unit of work.
    /// </summary>
    /// <remarks>
    /// Must be called inside the transaction that will commit the business change.
    /// The caller is responsible for that; this method assumes it and would
    /// silently produce a feed that can disagree with the records if called
    /// outside one.
    /// </remarks>
    public static async Task RecordAsync(
        AgencyOsDbContext context,
        IReadOnlyList<PendingChange> pending,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (IGrouping<OrganizationId, PendingChange> group in pending.GroupBy(x => x.OrganizationId))
        {
            PendingChange[] changes = [.. group];

            long last = await AllocateAsync(context, group.Key, changes.Length, cancellationToken)
                .ConfigureAwait(false);

            long next = last - changes.Length + 1;

            foreach (PendingChange change in changes)
            {
                context.ChangeLog.Add(ChangeLogEntry.Record(
                    change.OrganizationId,
                    next++,
                    change.EntityType,
                    change.EntityId,
                    change.Kind,
                    now));
            }
        }
    }

    private static void Add(
        List<PendingChange> pending,
        EntityState state,
        OrganizationId organizationId,
        string entityType,
        string entityId)
    {
        ChangeKind? kind = state switch
        {
            EntityState.Added or EntityState.Modified => ChangeKind.Upsert,

            // AgencyOS archives rather than deletes, so this arm does not fire
            // today. The mapping is total anyway: a partial mapping here would
            // mean a record leaving the tenant without any client being told.
            EntityState.Deleted => ChangeKind.Removed,

            _ => null,
        };

        if (kind is { } resolved)
        {
            pending.Add(new PendingChange(organizationId, entityType, entityId, resolved));
        }
    }

    /// <summary>
    /// Reserves <paramref name="count"/> consecutive positions for a tenant.
    /// </summary>
    /// <remarks>
    /// The upsert both creates the counter row on a tenant's first write and takes
    /// its row lock on every later one. Doing it in a single statement means there
    /// is no window in which two callers both find the row missing.
    /// </remarks>
    private static async Task<long> AllocateAsync(
        AgencyOsDbContext context,
        OrganizationId organizationId,
        int count,
        CancellationToken cancellationToken)
    {
        const string Sql = """
            INSERT INTO change_sequence (organization_id, last_value)
            VALUES (@organization_id, @count)
            ON CONFLICT (organization_id)
            DO UPDATE SET last_value = change_sequence.last_value + EXCLUDED.last_value
            RETURNING last_value;
            """;

        System.Data.Common.DbConnection connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using System.Data.Common.DbCommand command = connection.CreateCommand();

        command.CommandText = Sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();

        command.Parameters.Add(new NpgsqlParameter("organization_id", organizationId.Value));
        command.Parameters.Add(new NpgsqlParameter("count", (long)count));

        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
