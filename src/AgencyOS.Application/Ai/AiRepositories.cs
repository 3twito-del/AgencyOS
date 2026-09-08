using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai;

/// <summary>Reads and writes agent runs with their step history.</summary>
public interface IAgentRunRepository
{
    void Add(AgentRun run);

    Task<AgentRun?> FindAsync(
        OrganizationId organizationId,
        AgentRunId id,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes the tools a model asked for.</summary>
public interface IAiToolRequestRepository
{
    void Add(AiToolRequest request);

    Task<AiToolRequest?> FindAsync(
        OrganizationId organizationId,
        AiToolRequestId id,
        CancellationToken cancellationToken = default);

    /// <summary>Everything still undecided on a run, for abandoning it.</summary>
    Task<IReadOnlyList<AiToolRequest>> ListPendingAsync(
        OrganizationId organizationId,
        AgentRunId runId,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes approvals.</summary>
public interface IAiApprovalRepository
{
    void Add(AiApproval approval);

    Task<AiApproval?> FindAsync(
        OrganizationId organizationId,
        AiApprovalId id,
        CancellationToken cancellationToken = default);

    /// <summary>The approval covering one tool request, when there is one.</summary>
    Task<AiApproval?> FindForRequestAsync(
        OrganizationId organizationId,
        AiToolRequestId requestId,
        CancellationToken cancellationToken = default);

    /// <summary>What is waiting for this person to decide.</summary>
    Task<IReadOnlyList<AiApproval>> ListPendingForUserAsync(
        OrganizationId organizationId,
        UserId userId,
        CancellationToken cancellationToken = default);
}
