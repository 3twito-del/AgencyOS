using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai;

public sealed record AgentRunSummaryModel(
    AgentRunId Id,
    AgentKind Kind,
    AgentRunStatus Status,
    string Task,
    AgentSubjectKind SubjectKind,
    Guid? SubjectId,
    string ProviderKey,
    string ModelKey,
    string? StartedByDisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    AgentFailureKind Failure,
    int ModelInvocationCount,
    int ToolCallCount,
    int Version);

/// <param name="Steps">
/// What happened, in order. This is what lets a reader tell what the model said
/// from what AgencyOS did, which is the point of keeping it (§64).
/// </param>
public sealed record AgentRunDetailModel(
    AgentRunSummaryModel Run,
    string? Result,
    string? FailureDetail,
    string PromptTemplateId,
    int PromptTemplateVersion,
    IReadOnlyList<AgentStepModel> Steps,
    IReadOnlyList<AiToolRequestModel> ToolRequests);

public sealed record AgentStepModel(
    int Sequence,
    AgentStepKind Kind,
    string Summary,
    string? Detail,
    Guid? ReferenceId,
    DateTimeOffset OccurredAt);

/// <param name="Summary">
/// What AgencyOS says this would do, written from the validated arguments. Never
/// the model's own account of its request (§65).
/// </param>
public sealed record AiToolRequestModel(
    AiToolRequestId Id,
    string ToolName,
    int ToolVersion,
    ToolEffect Effect,
    ToolRequestStatus Status,
    string Summary,
    string Arguments,
    DateTimeOffset RequestedAt,
    string? Result,
    string? Refusal,
    int Version);

public sealed record AiApprovalModel(
    AiApprovalId Id,
    AgentRunId AgentRunId,
    AiToolRequestId ToolRequestId,
    AgentKind AgentKind,
    string ToolName,
    string Summary,
    string Arguments,
    ApprovalDecision Decision,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    bool HasExpired,
    string? DecidedByDisplayName,
    DateTimeOffset? DecidedAt,
    int Version);

public sealed record AiProviderPolicyModel(
    string ProviderKey,
    bool IsProviderConfigured,
    bool IsEnabled,
    ModelDataSensitivity MaximumSensitivity,
    bool AllowsCanonicalWriteProposals,
    DateTimeOffset? UpdatedAt,
    string? UpdatedByDisplayName,
    int Version);

/// <param name="Effect">
/// What it would do. The client uses this to decide whether to show an approval
/// dialog, and never to decide whether one is needed — that is the server's call.
/// </param>
public sealed record AiToolDescriptorModel(
    string Name,
    int Version,
    string Description,
    ToolEffect Effect,
    string RequiredPermission);

public sealed record AgentRunFilter(
    AgentKind? Kind = null,
    AgentRunStatus? Status = null,
    bool MineOnly = true,
    DateOnly? StartedAfter = null);

/// <summary>Reads AI runs, approvals and configuration.</summary>
public interface IAiQueries
{
    Task<IReadOnlyList<AgentRunSummaryModel>> ListRunsAsync(
        OrganizationId organizationId,
        UserId userId,
        AgentRunFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<AgentRunDetailModel?> GetRunAsync(
        OrganizationId organizationId,
        AgentRunId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiApprovalModel>> ListPendingApprovalsAsync(
        OrganizationId organizationId,
        UserId userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    Task<AiApprovalModel?> GetApprovalAsync(
        OrganizationId organizationId,
        AiApprovalId id,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiProviderPolicyModel>> ListPoliciesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The authorized read surface for the AI runtime.
/// </summary>
/// <remarks>
/// <para>
/// A run belongs to the person who started it. Somebody else holding ai.use does
/// not read it: a task somebody typed can name a person, a deal or a confidence,
/// and a run history is the one place in AgencyOS where a question is recorded
/// alongside what it turned up (§52).
/// </para>
/// <para>
/// Nothing here exposes a system prompt, a raw provider request or a provider
/// response body. The prompt version identifies which wording ran, which is what
/// reproducibility needs; the wording itself is in source control where a
/// developer reads it and a tenant user does not (§20, §52).
/// </para>
/// </remarks>
public sealed class AiQueryService
{
    private const int MaximumPageSize = 100;
    private const int DefaultPageSize = 25;

    private readonly IAiQueries _queries;
    private readonly TenantGuard _guard;
    private readonly Abstractions.IClock _clock;

    public AiQueryService(IAiQueries queries, TenantGuard guard, Abstractions.IClock clock)
    {
        _queries = queries;
        _guard = guard;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AgentRunSummaryModel>> ListRunsAsync(
        OrganizationId organizationId,
        AgentRunFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await _guard
            .AuthorizeAsync(Permission.AiUse, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListRunsAsync(
                organizationId,
                actor,
                filter ?? new AgentRunFilter(),
                Math.Clamp(limit ?? DefaultPageSize, 1, MaximumPageSize),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>One run, refused unless it is the caller's own.</summary>
    public async Task<AgentRunDetailModel?> GetRunAsync(
        OrganizationId organizationId,
        AgentRunId id,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await _guard
            .AuthorizeAsync(Permission.AiUse, organizationId, cancellationToken)
            .ConfigureAwait(false);

        AgentRunDetailModel? run = await _queries
            .GetRunAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            return null;
        }

        // Answered as missing rather than as forbidden. Unlike a classified record,
        // where refusing tells somebody a grant exists to ask for, there is no
        // grant that would let one person read another's run — so saying it exists
        // would disclose without offering a remedy.
        return await _queries
            .ListRunsAsync(
                organizationId,
                actor,
                new AgentRunFilter(MineOnly: true),
                MaximumPageSize,
                cancellationToken)
            .ConfigureAwait(false) is { } mine
            && mine.Any(x => x.Id == id)
                ? run
                : null;
    }

    public async Task<IReadOnlyList<AiApprovalModel>> ListPendingApprovalsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        UserId actor = await _guard
            .AuthorizeAsync(Permission.AiApprove, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListPendingApprovalsAsync(organizationId, actor, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AiApprovalModel?> GetApprovalAsync(
        OrganizationId organizationId,
        AiApprovalId id,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.AiApprove, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetApprovalAsync(organizationId, id, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>What this organization permits, per provider.</summary>
    public async Task<IReadOnlyList<AiProviderPolicyModel>> ListPoliciesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.AiUse, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListPoliciesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The agents this build offers.</summary>
    public async Task<IReadOnlyList<AgentKind>> ListAgentsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.AiUse, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return [.. AgentCatalog.ByKind.Keys];
    }

    /// <summary>The tools this caller may reach, for one agent.</summary>
    /// <remarks>
    /// Filtered exactly as the runtime filters them, so what a person is shown on
    /// the configuration screen is what a model would actually be offered.
    /// </remarks>
    public async Task<IReadOnlyList<AiToolDescriptorModel>> ListToolsAsync(
        OrganizationId organizationId,
        AgentKind kind,
        IAiToolRegistry registry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        await _guard
            .AuthorizeAsync(Permission.AiUse, organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<IAiTool> tools = await registry
            .AvailableAsync(organizationId, kind, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. tools.Select(x => new AiToolDescriptorModel(
                x.Name, x.Version, x.Description, x.Effect, x.RequiredPermission)),
        ];
    }
}
