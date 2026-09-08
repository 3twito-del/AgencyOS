using AgencyOS.Application.Ai;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for the AI runtime.
/// </summary>
/// <remarks>
/// <para>
/// Runs are scoped to their own user in the query, not filtered afterwards. A run
/// carries the question somebody asked, and a question can name a person, a deal
/// or a confidence — so "whose run is this" is narrowed in SQL for the same reason
/// M11 narrows classification there (§52).
/// </para>
/// <para>
/// No projection here returns a system prompt, a provider request or a response
/// body, because none of those is stored. What is stored is the prompt version,
/// which is what reproducing a run needs (§19).
/// </para>
/// </remarks>
public sealed class AiQueries : IAiQueries
{
    private const int HistoryLimit = 200;

    private readonly AgencyOsDbContext _context;

    public AiQueries(AgencyOsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentRunSummaryModel>> ListRunsAsync(
        OrganizationId organizationId,
        UserId userId,
        AgentRunFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<AgentRun> query = _context.AgentRuns
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        // Defaults to the caller's own, and the only way to widen it is a filter
        // this build never sets from the API. Somebody else's run is not readable
        // by holding a permission (§52).
        if (filter.MineOnly)
        {
            query = query.Where(x => x.UserId == userId);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.StartedAfter is { } after)
        {
            DateTimeOffset from = new(after.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(x => x.StartedAt >= from);
        }

        List<AgentRun> runs = await query
            .OrderByDescending(x => x.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        return [.. runs.Select(x => ToSummary(x, users))];
    }

    /// <inheritdoc />
    public async Task<AgentRunDetailModel?> GetRunAsync(
        OrganizationId organizationId,
        AgentRunId id,
        CancellationToken cancellationToken = default)
    {
        AgentRun? run = await _context.AgentRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            return null;
        }

        List<AgentRunStep> steps = await _context.AgentRunSteps
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.AgentRunId == id)
            .OrderBy(x => x.Sequence)
            .Take(HistoryLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AiToolRequest> requests = await _context.AiToolRequests
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.AgentRunId == id)
            .OrderBy(x => x.RequestedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        return new AgentRunDetailModel(
            ToSummary(run, users),
            run.Result,
            run.FailureDetail,
            run.PromptTemplateId,
            run.PromptTemplateVersion,
            [
                .. steps.Select(x => new AgentStepModel(
                    x.Sequence, x.Kind, x.Summary, x.Detail, x.ReferenceId, x.OccurredAt)),
            ],
            [.. requests.Select(ToRequest)]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiApprovalModel>> ListPendingApprovalsAsync(
        OrganizationId organizationId,
        UserId userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        List<AiApproval> approvals = await _context.AiApprovals
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.RequestedOf == userId
                && x.Decision == ApprovalDecision.Pending)
            .OrderBy(x => x.ExpiresAt)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await HydrateAsync(organizationId, approvals, asOf, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AiApprovalModel?> GetApprovalAsync(
        OrganizationId organizationId,
        AiApprovalId id,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        AiApproval? approval = await _context.AiApprovals
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (approval is null)
        {
            return null;
        }

        return (await HydrateAsync(organizationId, [approval], asOf, cancellationToken)
            .ConfigureAwait(false))
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiProviderPolicyModel>> ListPoliciesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        List<AiProviderPolicy> policies = await _context.AiProviderPolicies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.ProviderKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. policies.Select(x => new AiProviderPolicyModel(
                x.ProviderKey,

                // Whether the server holds a credential is deliberately not
                // answered here. It is server infrastructure, and a tenant screen
                // reporting it would leak the shape of the deployment (§3).
                IsProviderConfigured: false,
                x.IsEnabled,
                x.MaximumSensitivity,
                x.AllowsCanonicalWriteProposals,
                x.UpdatedAt,
                users.GetValueOrDefault(x.UpdatedBy.Value),
                x.Version)),
        ];
    }

    /// <summary>
    /// Fills in what an approval is about, from its tool request and run.
    /// </summary>
    /// <remarks>
    /// The summary and arguments come from the request rather than from the
    /// approval, because they are what will actually run. An approval screen built
    /// from a copy taken when the question was asked could show something the
    /// execution would not do (§65).
    /// </remarks>
    private async Task<IReadOnlyList<AiApprovalModel>> HydrateAsync(
        OrganizationId organizationId,
        IReadOnlyList<AiApproval> approvals,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (approvals.Count == 0)
        {
            return [];
        }

        AiToolRequestId[] requestIds = [.. approvals.Select(x => x.ToolRequestId).Distinct()];
        AgentRunId[] runIds = [.. approvals.Select(x => x.AgentRunId).Distinct()];

        Dictionary<AiToolRequestId, AiToolRequest> requests = await _context.AiToolRequests
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && requestIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<AgentRunId, AgentKind> kinds = await _context.AgentRuns
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && runIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Kind, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        List<AiApprovalModel> models = [];

        foreach (AiApproval approval in approvals)
        {
            if (!requests.TryGetValue(approval.ToolRequestId, out AiToolRequest? request))
            {
                continue;
            }

            models.Add(new AiApprovalModel(
                approval.Id,
                approval.AgentRunId,
                approval.ToolRequestId,
                kinds.GetValueOrDefault(approval.AgentRunId),
                request.ToolName,
                request.Summary,
                request.Arguments,
                approval.Decision,
                approval.RequestedAt,
                approval.ExpiresAt,

                // Computed against the supplied clock rather than stored, so a row
                // that lapsed while a screen was open reads as expired without
                // anything having had to update it.
                approval.Decision == ApprovalDecision.Pending && asOf > approval.ExpiresAt,
                approval.DecidedBy is { } decider
                    ? users.GetValueOrDefault(decider.Value)
                    : null,
                approval.DecidedAt,
                approval.Version));
        }

        return models;
    }

    private static AgentRunSummaryModel ToSummary(AgentRun run, Dictionary<Guid, string> users) =>
        new(
            run.Id,
            run.Kind,
            run.Status,
            run.Task,
            run.SubjectKind,
            run.SubjectId,
            run.ProviderKey,
            run.ModelKey,
            users.GetValueOrDefault(run.UserId.Value),
            run.StartedAt,
            run.CompletedAt,
            run.Failure,
            run.ModelInvocationCount,
            run.ToolCallCount,
            run.Version);

    private static AiToolRequestModel ToRequest(AiToolRequest request) =>
        new(
            request.Id,
            request.ToolName,
            request.ToolVersion,
            request.Effect,
            request.Status,
            request.Summary,
            request.Arguments,
            request.RequestedAt,
            request.Result,
            request.Refusal,
            request.Version);

    private async Task<Dictionary<Guid, string>> UsersAsync(CancellationToken cancellationToken) =>
        await _context.Users
            .AsNoTracking()
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id.Value, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);
}
