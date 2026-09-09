using AgencyOS.Contracts.Ai;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// The M12 half of the fake API.
/// </summary>
/// <remarks>
/// Records what the client asked for and returns what a test set up. It decides
/// nothing: the questions these tests answer are whether the view model separates
/// deciding from executing, and whether it ever presents model output as a
/// finding — neither of which a stub should be allowed to answer for it.
/// </remarks>
internal sealed partial class FakeAgencyOsApi
{
    public List<AgentRunResponse> AgentRuns { get; } = [];

    public AgentRunDetailResponse? AgentRunDetail { get; set; }

    public List<AiApprovalResponse> PendingApprovals { get; } = [];

    public List<AiProviderPolicyResponse> AiPolicies { get; } = [];

    public List<AiModelDescriptorResponse> AiModels { get; } = [];

    public List<AiToolDescriptorResponse> AiTools { get; } = [];

    public ExecuteApprovedToolResponse ExecutionResult { get; set; } =
        new(true, "Created.", null);

    public StartAgentRunRequest? LastStartRequest { get; private set; }

    public DecideApprovalRequest? LastDecision { get; private set; }

    public UpdateAiProviderPolicyRequest? LastPolicyUpdate { get; private set; }

    public CancelAgentRunRequest? LastRunCancellation { get; private set; }

    /// <summary>Tool requests the client asked to execute, in order.</summary>
    /// <remarks>
    /// Kept as a list rather than a last-write field so a test can assert that a
    /// rejection executed <em>nothing</em>, which a single field could not
    /// distinguish from a stale value.
    /// </remarks>
    public List<Guid> ExecutedToolRequests { get; } = [];

    public Task<IReadOnlyList<AgentRunResponse>> ListAiRunsAsync(
        string? kind = null,
        string? status = null,
        DateOnly? startedAfter = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentRunResponse>>(
        [
            .. AgentRuns
                .Where(x => kind is null || x.Kind == kind)
                .Where(x => status is null || x.Status == status),
        ]);

    public Task<AgentRunDetailResponse> GetAiRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AgentRunDetail
            ?? throw new InvalidOperationException("No run detail was set up."));

    public Task<AgentRunIdResponse> StartAiRunAsync(
        StartAgentRunRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        LastStartRequest = request;

        return Task.FromResult(new AgentRunIdResponse(
            AgentRunDetail?.Run.Id ?? Guid.CreateVersion7()));
    }

    public Task CancelAiRunAsync(
        Guid runId,
        CancelAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRunCancellation = request;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AiApprovalResponse>> ListPendingAiApprovalsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AiApprovalResponse>>([.. PendingApprovals]);

    public Task<AiApprovalResponse> GetAiApprovalAsync(
        Guid approvalId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PendingApprovals.First(x => x.Id == approvalId));

    public Task DecideAiApprovalAsync(
        Guid approvalId,
        DecideApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        LastDecision = request;
        PendingApprovals.RemoveAll(x => x.Id == approvalId);
        return Task.CompletedTask;
    }

    public Task<ExecuteApprovedToolResponse> ExecuteApprovedAiToolAsync(
        Guid toolRequestId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ExecutedToolRequests.Add(toolRequestId);
        return Task.FromResult(ExecutionResult);
    }

    public Task<IReadOnlyList<string>> ListAiAgentsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(["ResearchCopilot"]);

    public Task<IReadOnlyList<AiToolDescriptorResponse>> ListAiToolsAsync(
        string kind,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AiToolDescriptorResponse>>([.. AiTools]);

    public Task<IReadOnlyList<AiModelDescriptorResponse>> ListAiModelsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AiModelDescriptorResponse>>([.. AiModels]);

    public Task<IReadOnlyList<AiProviderPolicyResponse>> ListAiProviderPoliciesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AiProviderPolicyResponse>>([.. AiPolicies]);

    public Task SetAiProviderPolicyAsync(
        string providerKey,
        UpdateAiProviderPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        LastPolicyUpdate = request;
        return Task.CompletedTask;
    }
}

/// <summary>
/// The M13 half of the fake API.
/// </summary>
/// <remarks>
/// The device-local protocol is exercised properly in AgencyOS.Tests.Windows,
/// where the runner and a deterministic model live. These members exist so the
/// M12 view-model tests keep compiling against one client interface rather than
/// two, and they refuse rather than pretend: a view model that reached the lease
/// protocol from here would be doing something no screen should.
/// </remarks>
internal sealed partial class FakeAgencyOsApi
{
    public Task<ContextLeaseResponse> IssueAiContextLeaseAsync(
        Guid runId,
        IssueContextLeaseRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "The device-local protocol is tested in AgencyOS.Tests.Windows.");

    public Task<SubmitLocalResultResponse> SubmitAiLocalResultAsync(
        Guid runId,
        SubmitLocalResultRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "The device-local protocol is tested in AgencyOS.Tests.Windows.");

    public Task<IReadOnlyList<AiExecutionTargetResponse>> ListAiExecutionTargetsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AiExecutionTargetResponse>>([.. ExecutionTargets]);

    /// <summary>What a test says this server can execute.</summary>
    public List<AiExecutionTargetResponse> ExecutionTargets { get; } = [];
}
