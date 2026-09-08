using AgencyOS.Api.Authorization;
using AgencyOS.Application.Ai;
using AgencyOS.Contracts.Ai;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M12 AI runtime surface.
/// </summary>
/// <remarks>
/// <para>
/// The shape of this surface is the security argument. A run is started, read and
/// cancelled; an approval is read and decided; an approved request is executed as
/// a separate call. There is deliberately no route that hands the model an action
/// to perform, because the model is untrusted input rather than a caller (§1).
/// </para>
/// <para>
/// <strong>Nothing here accepts a tool name or arguments from a client.</strong>
/// A client cannot ask AgencyOS to run <c>task.create</c>; it can only decide an
/// approval that a run already produced, and execute the request that approval
/// binds to by fingerprint. That is why there is no generic execute route and no
/// route carrying a tool payload (§32).
/// </para>
/// <para>
/// No route returns a system prompt, a provider request or a response body. A run
/// exposes which prompt version produced it, because that is what reproducing it
/// needs, and the wording lives in source control where a developer reads it
/// (§20, §52, §53).
/// </para>
/// </remarks>
internal static class M12Endpoints
{
    public static void MapAiRuntime(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}/ai");

        MapRuns(tenant);
        MapApprovals(tenant);
        MapCatalog(tenant);
        MapPolicies(tenant);
    }

    // ----------------------------------------------------------------- runs

    private static void MapRuns(RouteGroupBuilder tenant)
    {
        // Returns the caller's own runs. There is no parameter that widens it,
        // because a task somebody typed can name a person, a deal or a confidence
        // and no permission in AgencyOS grants reading another person's questions
        // (§52).
        tenant.MapGet("/runs", async (
                Guid organizationId,
                AiQueryService queries,
                string? kind,
                string? status,
                DateOnly? startedAfter,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                AgentRunFilter filter = new(
                    EndpointParsing.ParseNullableEnum<AgentKind>(kind, nameof(kind)),
                    EndpointParsing.ParseNullableEnum<AgentRunStatus>(status, nameof(status)),
                    MineOnly: true,
                    startedAfter);

                IReadOnlyList<AgentRunSummaryModel> runs = await queries
                    .ListRunsAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(runs.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("ListAiRuns");

        tenant.MapGet("/runs/{runId:guid}", async (
                Guid organizationId,
                Guid runId,
                AiQueryService queries,
                CancellationToken cancellationToken) =>
            {
                AgentRunDetailModel? run = await queries
                    .GetRunAsync(
                        new OrganizationId(organizationId),
                        new AgentRunId(runId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return run is null ? Results.NotFound() : Results.Ok(Map(run));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("GetAiRun");

        // Runs to completion, refusal or an approval request before responding.
        // Synchronous on purpose: a run bounded to six model turns and ten minutes
        // is a request, and a background job here would need its own delivery,
        // retry and cancellation story to say the same thing (§16).
        tenant.MapPost("/runs", async (
                Guid organizationId,
                StartAgentRunRequest request,
                AgentRunHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                AgentRunId id = await handler.HandleAsync(
                        new StartAgentRunCommand(
                            new OrganizationId(organizationId),
                            EndpointParsing.ParseEnum<AgentKind>(
                                request.Kind, nameof(request.Kind)),
                            request.Task,
                            EndpointParsing.ParseEnum<AgentSubjectKind>(
                                request.SubjectKind, nameof(request.SubjectKind)),
                            request.SubjectId,
                            request.ModelKey),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/ai/runs/{id.Value}",
                    new AgentRunIdResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("StartAiRun");

        // Stops the run and abandons whatever was waiting on a decision. It does
        // not undo anything already committed: a task that was approved and created
        // is a real task, and cancelling the run that proposed it does not unmake
        // it (§45).
        tenant.MapPost("/runs/{runId:guid}/cancel", async (
                Guid organizationId,
                Guid runId,
                CancelAgentRunRequest request,
                AgentRunHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new CancelAgentRunCommand(
                            new OrganizationId(organizationId),
                            new AgentRunId(runId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("CancelAiRun");
    }

    // ------------------------------------------------------------ approvals

    private static void MapApprovals(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/approvals", async (
                Guid organizationId,
                AiQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<AiApprovalModel> approvals = await queries
                    .ListPendingApprovalsAsync(
                        new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(approvals.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiApprove))
            .WithName("ListPendingAiApprovals");

        tenant.MapGet("/approvals/{approvalId:guid}", async (
                Guid organizationId,
                Guid approvalId,
                AiQueryService queries,
                CancellationToken cancellationToken) =>
            {
                AiApprovalModel? approval = await queries
                    .GetApprovalAsync(
                        new OrganizationId(organizationId),
                        new AiApprovalId(approvalId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return approval is null ? Results.NotFound() : Results.Ok(Map(approval));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiApprove))
            .WithName("GetAiApproval");

        // A decision, and nothing else. Approving does not execute: the decision is
        // a person's act and completes on its own, and execution re-derives the
        // fingerprint, re-checks the permission and may still be refused. Fusing
        // them would mean a refusal at execution had no record of who allowed
        // what (§14).
        tenant.MapPost("/approvals/{approvalId:guid}/decision", async (
                Guid organizationId,
                Guid approvalId,
                DecideApprovalRequest request,
                AiApprovalHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new DecideApprovalCommand(
                            new OrganizationId(organizationId),
                            new AiApprovalId(approvalId),
                            request.Approve,
                            request.ExpectedVersion,
                            request.Reason),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiApprove))
            .WithName("DecideAiApproval");

        // Carries no arguments. What runs is what was proposed and approved, read
        // from the stored request and re-fingerprinted; a body here would be a way
        // to approve one action and execute another (§13, §32).
        tenant.MapPost("/tool-requests/{toolRequestId:guid}/execute", async (
                Guid organizationId,
                Guid toolRequestId,
                AiApprovalHandler handler,
                CancellationToken cancellationToken) =>
            {
                ToolResult result = await handler.HandleAsync(
                        new ExecuteApprovedToolCommand(
                            new OrganizationId(organizationId),
                            new AiToolRequestId(toolRequestId)),
                        cancellationToken)
                    .ConfigureAwait(false);

                // A refusal is an ordinary outcome rather than an error: the
                // approval permitted an attempt, and the domain is entitled to
                // refuse it on concurrency or on its own rules (§48).
                return Results.Ok(new ExecuteApprovedToolResponse(
                    result.Succeeded,
                    result.Succeeded ? result.Content : null,
                    result.Failure));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiApprove))
            .WithName("ExecuteApprovedAiTool");
    }

    // -------------------------------------------------------------- catalog

    private static void MapCatalog(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/agents", async (
                Guid organizationId,
                AiQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<AgentKind> agents = await queries
                    .ListAgentsAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(agents.Select(x => x.ToString()).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("ListAiAgents");

        // What one agent may reach, filtered exactly as the runtime filters it, so
        // the screen shows what a model would actually be offered rather than what
        // the registry contains (§10).
        tenant.MapGet("/agents/{kind}/tools", async (
                Guid organizationId,
                string kind,
                AiQueryService queries,
                IAiToolRegistry registry,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<AiToolDescriptorModel> tools = await queries
                    .ListToolsAsync(
                        new OrganizationId(organizationId),
                        EndpointParsing.ParseEnum<AgentKind>(kind, nameof(kind)),
                        registry,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(tools.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("ListAiTools");

        // Capabilities as configured, never the provider's model identifier and
        // never whether a credential is present. What a client needs to know is
        // whether a model takes tools and structured output (§4, §71).
        tenant.MapGet("/models", async (
                Guid organizationId,
                AiQueryService queries,
                IModelGateway gateway,
                CancellationToken cancellationToken) =>
            {
                await queries
                    .ListAgentsAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(gateway.Catalog
                    .Select(x => new AiModelDescriptorResponse(
                        x.Key,
                        x.ProviderKey,
                        x.SupportsTools,
                        x.SupportsStructuredOutput,
                        x.SupportsStreaming,
                        x.MaxContextTokens))
                    .ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("ListAiModels");
    }

    // ------------------------------------------------------------- policies

    private static void MapPolicies(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/policies", async (
                Guid organizationId,
                AiQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<AiProviderPolicyModel> policies = await queries
                    .ListPoliciesAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(policies.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiUse))
            .WithName("ListAiProviderPolicies");

        // ai.administer, not ai.use. Deciding what leaves the building is a
        // different authority from using a model, and a person can reasonably hold
        // either without the other (§42).
        tenant.MapPut("/policies/{providerKey}", async (
                Guid organizationId,
                string providerKey,
                UpdateAiProviderPolicyRequest request,
                AiProviderPolicyHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new SetAiProviderPolicyCommand(
                            new OrganizationId(organizationId),
                            providerKey,
                            request.IsEnabled,
                            EndpointParsing.ParseEnum<ModelDataSensitivity>(
                                request.MaximumSensitivity,
                                nameof(request.MaximumSensitivity)),
                            request.AllowsCanonicalWriteProposals,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AiAdminister))
            .WithName("SetAiProviderPolicy");
    }

    // -------------------------------------------------------------- mapping

    private static AgentRunResponse Map(AgentRunSummaryModel run) =>
        new(
            run.Id.Value,
            run.Kind.ToString(),
            run.Status.ToString(),
            run.Task,
            run.SubjectKind.ToString(),
            run.SubjectId,
            run.ProviderKey,
            run.ModelKey,
            run.StartedByDisplayName,
            run.StartedAt,
            run.CompletedAt,
            run.Failure.ToString(),
            run.ModelInvocationCount,
            run.ToolCallCount,
            run.Version);

    private static AgentRunDetailResponse Map(AgentRunDetailModel run) =>
        new(
            Map(run.Run),
            run.Result,
            run.FailureDetail,
            run.PromptTemplateId,
            run.PromptTemplateVersion,
            [
                .. run.Steps.Select(x => new AgentStepResponse(
                    x.Sequence,
                    x.Kind.ToString(),
                    x.Summary,
                    x.Detail,
                    x.ReferenceId,
                    x.OccurredAt)),
            ],
            [.. run.ToolRequests.Select(Map)]);

    private static AiToolRequestResponse Map(AiToolRequestModel request) =>
        new(
            request.Id.Value,
            request.ToolName,
            request.ToolVersion,
            request.Effect.ToString(),
            request.Status.ToString(),
            request.Summary,
            request.Arguments,
            request.RequestedAt,
            request.Result,
            request.Refusal,
            request.Version);

    private static AiApprovalResponse Map(AiApprovalModel approval) =>
        new(
            approval.Id.Value,
            approval.AgentRunId.Value,
            approval.ToolRequestId.Value,
            approval.AgentKind.ToString(),
            approval.ToolName,
            approval.Summary,
            approval.Arguments,
            approval.Decision.ToString(),
            approval.RequestedAt,
            approval.ExpiresAt,
            approval.HasExpired,
            approval.DecidedByDisplayName,
            approval.DecidedAt,
            approval.Version);

    private static AiProviderPolicyResponse Map(AiProviderPolicyModel policy) =>
        new(
            policy.ProviderKey,
            policy.IsProviderConfigured,
            policy.IsEnabled,
            policy.MaximumSensitivity.ToString(),
            policy.AllowsCanonicalWriteProposals,
            policy.UpdatedAt,
            policy.UpdatedByDisplayName,
            policy.Version);

    private static AiToolDescriptorResponse Map(AiToolDescriptorModel tool) =>
        new(
            tool.Name,
            tool.Version,
            tool.Description,
            tool.Effect.ToString(),
            tool.RequiredPermission);
}
