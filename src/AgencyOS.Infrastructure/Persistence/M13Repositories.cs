using AgencyOS.Application.Ai;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>Reads and writes context leases.</summary>
/// <remarks>
/// Every read is tenant-qualified in the query rather than filtered afterwards.
/// A lease is the authorization artifact for a disclosure that has already
/// happened, and narrowing it late is how one tenant's row reaches another's
/// handler (ADR-0011, ADR-0035).
/// </remarks>
public sealed class AiContextLeaseRepository : IAiContextLeaseRepository
{
    private readonly AgencyOsDbContext _context;

    public AiContextLeaseRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(AiContextLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        _context.AiContextLeases.Add(lease);
    }

    /// <inheritdoc />
    public Task<AiContextLease?> FindAsync(
        OrganizationId organizationId,
        AiContextLeaseId id,
        CancellationToken cancellationToken = default) =>
        _context.AiContextLeases
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiContextLease>> ListOutstandingAsync(
        OrganizationId organizationId,
        AgentRunId runId,
        CancellationToken cancellationToken = default) =>
        await _context.AiContextLeases
            .Where(x => x.OrganizationId == organizationId
                && x.AgentRunId == runId
                && x.State == AiContextLeaseState.Issued)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
