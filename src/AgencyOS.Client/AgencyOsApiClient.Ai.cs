using AgencyOS.Contracts.Ai;

namespace AgencyOS.Client;

/// <summary>
/// The M12 AI runtime surface, as the Windows client sees it.
/// </summary>
/// <remarks>
/// <para>
/// Every method here goes to the server and waits. Nothing is queued and nothing
/// is cached: <c>docs/13_OFFLINE_CLASSIFICATION.md</c> classifies the whole
/// milestone ONLINE_ONLY. A run reads the caller's authorized data through
/// server-side tools and may propose a canonical write, and neither of those has
/// any meaning on a laptop with no server to authorize it (§51).
/// </para>
/// <para>
/// <strong>There is deliberately no method that runs a tool.</strong> The client
/// cannot name a tool, cannot supply arguments and cannot ask for an action: it
/// starts a run, reads what came back, decides an approval and executes the
/// request that approval already binds to. Anything else would be a way to reach
/// a canonical command without the proposal that justified it (§32).
/// </para>
/// <para>
/// <see cref="IAgencyOsApi.ExecuteApprovedAiToolAsync"/> takes an idempotency key
/// for the same reason every other mutation does. A lost response on a canonical
/// write is exactly the case where a retry must not create a second task (§48).
/// </para>
/// </remarks>
public partial interface IAgencyOsApi
{
    // ---- runs ----

    /// <summary>The caller's own runs. There is no parameter that widens it.</summary>
    Task<IReadOnlyList<AgentRunResponse>> ListAiRunsAsync(
        string? kind = null,
        string? status = null,
        DateOnly? startedAfter = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    Task<AgentRunDetailResponse> GetAiRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a run and waits for it.
    /// </summary>
    /// <remarks>
    /// Long by the standards of this client — a run may take several model turns —
    /// and bounded by the server rather than here. A caller that gives up early
    /// leaves a run that finishes anyway and stays readable, which is why the
    /// identifier comes back rather than the result.
    /// </remarks>
    Task<AgentRunIdResponse> StartAiRunAsync(
        StartAgentRunRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task CancelAiRunAsync(
        Guid runId,
        CancelAgentRunRequest request,
        CancellationToken cancellationToken = default);

    // ---- approvals ----

    Task<IReadOnlyList<AiApprovalResponse>> ListPendingAiApprovalsAsync(
        CancellationToken cancellationToken = default);

    Task<AiApprovalResponse> GetAiApprovalAsync(
        Guid approvalId,
        CancellationToken cancellationToken = default);

    /// <summary>Records a decision. Approving does not execute.</summary>
    Task DecideAiApprovalAsync(
        Guid approvalId,
        DecideApprovalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs what an approval authorized.
    /// </summary>
    /// <remarks>
    /// Carries no arguments, because what runs is what was proposed and approved.
    /// A refusal comes back as a successful response saying it was refused: the
    /// approval permitted an attempt, and the domain is entitled to decline it.
    /// </remarks>
    Task<ExecuteApprovedToolResponse> ExecuteApprovedAiToolAsync(
        Guid toolRequestId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    // ---- catalog and policy ----

    Task<IReadOnlyList<string>> ListAiAgentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>What one agent may reach, as the runtime would offer it.</summary>
    Task<IReadOnlyList<AiToolDescriptorResponse>> ListAiToolsAsync(
        string kind,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiModelDescriptorResponse>> ListAiModelsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiProviderPolicyResponse>> ListAiProviderPoliciesAsync(
        CancellationToken cancellationToken = default);

    Task SetAiProviderPolicyAsync(
        string providerKey,
        UpdateAiProviderPolicyRequest request,
        CancellationToken cancellationToken = default);
}

public sealed partial class AgencyOsApiClient
{
    private string AiRoot => $"{TenantRoot}/ai";

    // ----------------------------------------------------------------- runs

    public Task<IReadOnlyList<AgentRunResponse>> ListAiRunsAsync(
        string? kind = null,
        string? status = null,
        DateOnly? startedAfter = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        QueryBuilder query = new();
        query.Add("kind", kind);
        query.Add("status", status);
        query.Add("startedAfter", startedAfter);
        query.Add("limit", limit);

        return GetListAsync<AgentRunResponse>(
            query.Apply($"{AiRoot}/runs"), cancellationToken);
    }

    public Task<AgentRunDetailResponse> GetAiRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AgentRunDetailResponse>($"{AiRoot}/runs/{runId}", cancellationToken);

    public Task<AgentRunIdResponse> StartAiRunAsync(
        StartAgentRunRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<StartAgentRunRequest, AgentRunIdResponse>(
            HttpMethod.Post,
            $"{AiRoot}/runs",
            request,
            idempotencyKey,
            cancellationToken);

    public Task CancelAiRunAsync(
        Guid runId,
        CancelAgentRunRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{AiRoot}/runs/{runId}/cancel",
            request,
            idempotencyKey: null,
            cancellationToken);

    // ------------------------------------------------------------ approvals

    public Task<IReadOnlyList<AiApprovalResponse>> ListPendingAiApprovalsAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<AiApprovalResponse>($"{AiRoot}/approvals", cancellationToken);

    public Task<AiApprovalResponse> GetAiApprovalAsync(
        Guid approvalId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AiApprovalResponse>($"{AiRoot}/approvals/{approvalId}", cancellationToken);

    public Task DecideAiApprovalAsync(
        Guid approvalId,
        DecideApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Post,
            $"{AiRoot}/approvals/{approvalId}/decision",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<ExecuteApprovedToolResponse> ExecuteApprovedAiToolAsync(
        Guid toolRequestId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<object?, ExecuteApprovedToolResponse>(
            HttpMethod.Post,
            $"{AiRoot}/tool-requests/{toolRequestId}/execute",
            body: null,
            idempotencyKey,
            cancellationToken);

    // -------------------------------------------------------------- catalog

    public Task<IReadOnlyList<string>> ListAiAgentsAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<string>($"{AiRoot}/agents", cancellationToken);

    public Task<IReadOnlyList<AiToolDescriptorResponse>> ListAiToolsAsync(
        string kind,
        CancellationToken cancellationToken = default) =>
        GetListAsync<AiToolDescriptorResponse>(
            $"{AiRoot}/agents/{Uri.EscapeDataString(kind)}/tools", cancellationToken);

    public Task<IReadOnlyList<AiModelDescriptorResponse>> ListAiModelsAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<AiModelDescriptorResponse>($"{AiRoot}/models", cancellationToken);

    // --------------------------------------------------------------- policy

    public Task<IReadOnlyList<AiProviderPolicyResponse>> ListAiProviderPoliciesAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<AiProviderPolicyResponse>($"{AiRoot}/policies", cancellationToken);

    public Task SetAiProviderPolicyAsync(
        string providerKey,
        UpdateAiProviderPolicyRequest request,
        CancellationToken cancellationToken = default) =>
        SendNoContentAsync(
            HttpMethod.Put,
            $"{AiRoot}/policies/{Uri.EscapeDataString(providerKey)}",
            request,
            idempotencyKey: null,
            cancellationToken);
}
