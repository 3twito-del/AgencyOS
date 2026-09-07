using AgencyOS.Application.Idempotency;
using AgencyOS.Application.SavedViews;
using AgencyOS.Domain.Idempotency;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.SavedViews;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgencyOS.Infrastructure.Persistence;

/// <remarks>
/// Every query filters on the tenant and the owner. A saved view is private, and
/// a lookup that omitted either key would be the way that stops being true.
/// </remarks>
internal sealed class SavedViewRepository : ISavedViewRepository
{
    private readonly AgencyOsDbContext _context;

    public SavedViewRepository(AgencyOsDbContext context) => _context = context;

    public Task<SavedView?> FindAsync(
        OrganizationId organizationId,
        UserId ownerUserId,
        SavedViewId id,
        CancellationToken cancellationToken = default)
    {
        return _context.SavedViews.FirstOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId && x.OwnerUserId == ownerUserId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SavedView>> ListAsync(
        OrganizationId organizationId,
        UserId ownerUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.SavedViews
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.OwnerUserId == ownerUserId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> NameExistsAsync(
        OrganizationId organizationId,
        UserId ownerUserId,
        string name,
        SavedViewId? excluding,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SavedView> query = _context.SavedViews
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.OwnerUserId == ownerUserId
                && x.Name == name);

        if (excluding is { } id)
        {
            query = query.Where(x => x.Id != id);
        }

        return query.AnyAsync(cancellationToken);
    }

    public void Add(SavedView view) => _context.SavedViews.Add(view);

    public void Remove(SavedView view) => _context.SavedViews.Remove(view);
}

/// <summary>
/// Idempotency keys, stored in canonical PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Reservation is a single <c>INSERT … ON CONFLICT DO NOTHING</c>. That is the
/// whole mechanism: two submissions of one key race on the primary key and
/// exactly one insert reports a row. Reading first and then inserting would leave
/// a window between the two in which both callers decide to execute, which is
/// precisely the duplicate this table exists to prevent.
/// </para>
/// <para>
/// The write runs on its own connection state rather than as part of the caller's
/// unit of work, and is committed immediately, because a reservation that rolls
/// back with a failed command would let a retry execute a second time against a
/// first attempt that may already have committed its effect elsewhere.
/// </para>
/// </remarks>
internal sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly AgencyOsDbContext _context;

    public IdempotencyStore(AgencyOsDbContext context) => _context = context;

    public async Task<IdempotencyRecord?> TryReserveAsync(
        OrganizationId organizationId,
        string key,
        string requestFingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        const string Sql = """
            INSERT INTO idempotency_keys
                (organization_id, key, request_fingerprint, created_at, expires_at)
            VALUES (@organization_id, @key, @fingerprint, @created_at, @expires_at)
            ON CONFLICT (organization_id, key) DO NOTHING;
            """;

        int inserted = await _context.Database
            .ExecuteSqlRawAsync(
                Sql,
                [
                    new NpgsqlParameter("organization_id", organizationId.Value),
                    new NpgsqlParameter("key", key),
                    new NpgsqlParameter("fingerprint", requestFingerprint),
                    new NpgsqlParameter("created_at", now),
                    new NpgsqlParameter("expires_at", now.Add(IdempotencyRecord.Retention)),
                ],
                cancellationToken)
            .ConfigureAwait(false);

        if (inserted == 1)
        {
            return null;
        }

        return await _context.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Key == key,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Idempotency key '{key}' was neither inserted nor found. "
                    + "The reservation table may have been modified concurrently.");
    }

    public async Task CompleteAsync(
        OrganizationId organizationId,
        string key,
        int statusCode,
        string? responseBody,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        const string Sql = """
            UPDATE idempotency_keys
            SET response_status_code = @status, response_body = @body, completed_at = @completed_at
            WHERE organization_id = @organization_id AND key = @key AND completed_at IS NULL;
            """;

        await _context.Database
            .ExecuteSqlRawAsync(
                Sql,
                [
                    new NpgsqlParameter("status", statusCode),
                    new NpgsqlParameter("body", (object?)responseBody ?? DBNull.Value),
                    new NpgsqlParameter("completed_at", now),
                    new NpgsqlParameter("organization_id", organizationId.Value),
                    new NpgsqlParameter("key", key),
                ],
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AbandonAsync(
        OrganizationId organizationId,
        string key,
        CancellationToken cancellationToken = default)
    {
        // Only an incomplete reservation is removable. Deleting a completed one
        // would turn a stored answer back into a second execution.
        const string Sql = """
            DELETE FROM idempotency_keys
            WHERE organization_id = @organization_id AND key = @key AND completed_at IS NULL;
            """;

        await _context.Database
            .ExecuteSqlRawAsync(
                Sql,
                [
                    new NpgsqlParameter("organization_id", organizationId.Value),
                    new NpgsqlParameter("key", key),
                ],
                cancellationToken)
            .ConfigureAwait(false);
    }
}
