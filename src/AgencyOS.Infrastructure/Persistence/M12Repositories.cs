using AgencyOS.Application.Ai;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>Reads and writes agent runs.</summary>
public sealed class AgentRunRepository : IAgentRunRepository
{
    private readonly AgencyOsDbContext _context;

    public AgentRunRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(AgentRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        _context.AgentRuns.Add(run);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Steps are included because appending one numbers it from the count, and
    /// numbering from a collection nobody loaded would restart the sequence and
    /// collide with the rows already there.
    /// </remarks>
    public Task<AgentRun?> FindAsync(
        OrganizationId organizationId,
        AgentRunId id,
        CancellationToken cancellationToken = default) =>
        _context.AgentRuns
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);
}

/// <summary>Reads and writes the tools a model asked for.</summary>
public sealed class AiToolRequestRepository : IAiToolRequestRepository
{
    private static readonly ToolRequestStatus[] Undecided =
    [
        ToolRequestStatus.Proposed,
        ToolRequestStatus.AwaitingApproval,
        ToolRequestStatus.Approved,
    ];

    private readonly AgencyOsDbContext _context;

    public AiToolRequestRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(AiToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _context.AiToolRequests.Add(request);
    }

    /// <inheritdoc />
    public Task<AiToolRequest?> FindAsync(
        OrganizationId organizationId,
        AiToolRequestId id,
        CancellationToken cancellationToken = default) =>
        _context.AiToolRequests
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Approved counts as undecided here. A request approved but not yet run still
    /// has to be abandoned when the run is cancelled, or it would remain executable
    /// against a run that no longer exists (§45).
    /// </remarks>
    public async Task<IReadOnlyList<AiToolRequest>> ListPendingAsync(
        OrganizationId organizationId,
        AgentRunId runId,
        CancellationToken cancellationToken = default) =>
        await _context.AiToolRequests
            .Where(x => x.OrganizationId == organizationId
                && x.AgentRunId == runId
                && Undecided.Contains(x.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>Reads and writes approvals.</summary>
public sealed class AiApprovalRepository : IAiApprovalRepository
{
    private readonly AgencyOsDbContext _context;

    public AiApprovalRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(AiApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);

        _context.AiApprovals.Add(approval);
    }

    /// <inheritdoc />
    public Task<AiApproval?> FindAsync(
        OrganizationId organizationId,
        AiApprovalId id,
        CancellationToken cancellationToken = default) =>
        _context.AiApprovals
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<AiApproval?> FindForRequestAsync(
        OrganizationId organizationId,
        AiToolRequestId requestId,
        CancellationToken cancellationToken = default) =>
        _context.AiApprovals
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.ToolRequestId == requestId,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiApproval>> ListPendingForUserAsync(
        OrganizationId organizationId,
        UserId userId,
        CancellationToken cancellationToken = default) =>
        await _context.AiApprovals
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.RequestedOf == userId
                && x.Decision == ApprovalDecision.Pending)
            .OrderBy(x => x.RequestedAt)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>Reads and writes an organization's provider policy.</summary>
public sealed class AiProviderPolicyRepository : IAiProviderPolicyRepository
{
    private readonly AgencyOsDbContext _context;

    public AiProviderPolicyRepository(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public void Add(AiProviderPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _context.AiProviderPolicies.Add(policy);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns null where no row exists, and the caller substitutes the closed
    /// default. Materializing a permissive row on first read is how a tenant ends
    /// up transmitting because somebody opened a screen (§43).
    /// </remarks>
    public Task<AiProviderPolicy?> FindAsync(
        OrganizationId organizationId,
        string providerKey,
        CancellationToken cancellationToken = default) =>
        _context.AiProviderPolicies
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.ProviderKey == providerKey,
                cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiProviderPolicy>> ListAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default) =>
        await _context.AiProviderPolicies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.ProviderKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
