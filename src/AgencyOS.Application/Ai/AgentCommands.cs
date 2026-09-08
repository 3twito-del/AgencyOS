using System.Text.Json;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai;

public sealed record StartAgentRunCommand(
    OrganizationId OrganizationId,
    AgentKind Kind,
    string Task,
    AgentSubjectKind SubjectKind = AgentSubjectKind.None,
    Guid? SubjectId = null,
    string? ModelKey = null);

public sealed record CancelAgentRunCommand(
    OrganizationId OrganizationId,
    AgentRunId RunId,
    int ExpectedVersion);

public sealed record DecideApprovalCommand(
    OrganizationId OrganizationId,
    AiApprovalId ApprovalId,
    bool Approve,
    int ExpectedVersion,
    string? Reason = null);

public sealed record ExecuteApprovedToolCommand(
    OrganizationId OrganizationId,
    AiToolRequestId ToolRequestId);

/// <summary>
/// Starts, cancels and resumes agent runs.
/// </summary>
/// <remarks>
/// Every entry point authorizes first and records the run before a provider is
/// touched, so a run that fails at the first call is still a run somebody can find
/// and read (§16).
/// </remarks>
public sealed class AgentRunHandler
{
    private readonly AgentRuntime _runtime;
    private readonly IAgentRunRepository _runs;
    private readonly IAiToolRequestRepository _toolRequests;
    private readonly IModelGateway _gateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;

    public AgentRunHandler(
        AgentRuntime runtime,
        IAgentRunRepository runs,
        IAiToolRequestRepository toolRequests,
        IModelGateway gateway,
        IUnitOfWork unitOfWork,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock)
    {
        _runtime = runtime;
        _runs = runs;
        _toolRequests = toolRequests;
        _gateway = gateway;
        _unitOfWork = unitOfWork;
        _guard = guard;
        _audit = audit;
        _clock = clock;
    }

    public async Task<AgentRunId> HandleAsync(
        StartAgentRunCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.AiUse, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        AgentDefinition definition = AgentCatalog.For(command.Kind);

        string modelKey = command.ModelKey ?? "fake-default";

        // The model is resolved before the run exists, so an unknown key is a
        // refusal the caller sees rather than a run that fails a moment later.
        ModelDescriptor descriptor = _gateway.Describe(modelKey)
            ?? throw new DomainException(
                $"No model is configured under the key '{modelKey}'.");

        AgentRun run = AgentRun.Start(
            command.OrganizationId,
            actor,
            command.Kind,
            command.Task,
            descriptor.ProviderKey,
            descriptor.Key,
            definition.PromptTemplateId,
            definition.PromptTemplateVersion,
            _clock.UtcNow,
            command.SubjectKind,
            command.SubjectId);

        _runs.Add(run);

        // The kind and the model, never the task text. A task somebody typed can
        // name a person, a deal or a confidence, and an audit delta is read by
        // people who hold no AI grant at all (ADR-0012).
        _audit.Record(
            AuditAction.AgentRunStarted,
            entityType: nameof(AgentRun),
            entityId: run.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.AiUse,
            semanticDelta: new
            {
                agent = command.Kind.ToString(),
                provider = descriptor.ProviderKey,
                model = descriptor.Key,
                promptVersion = definition.PromptTemplateVersion,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _runtime.ExecuteAsync(run, definition, cancellationToken).ConfigureAwait(false);

        return run.Id;
    }

    /// <summary>
    /// Stops a run.
    /// </summary>
    /// <remarks>
    /// Abandons whatever was waiting for a decision, so a cancelled run does not
    /// leave an approval somebody could grant afterwards. It does not touch
    /// anything already committed: a task that was approved and created is a real
    /// task, and cancelling the run that proposed it does not unmake it (§45).
    /// </remarks>
    public async Task HandleAsync(
        CancelAgentRunCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.AiUse, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        AgentRun run = await _runs
            .FindAsync(command.OrganizationId, command.RunId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(AgentRun), command.RunId.ToString());

        run.Cancel(_clock.UtcNow, command.ExpectedVersion);

        IReadOnlyList<AiToolRequest> pending = await _toolRequests
            .ListPendingAsync(command.OrganizationId, run.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (AiToolRequest request in pending)
        {
            request.Abandon(_clock.UtcNow, request.Version);
        }

        _audit.Record(
            AuditAction.AgentRunCancelled,
            entityType: nameof(AgentRun),
            entityId: run.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.AiUse,
            semanticDelta: new { abandonedRequests = pending.Count },
            reason: "Cancelled by the user. Anything already committed stands.");

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Decides an approval, and runs what it authorizes.
/// </summary>
/// <remarks>
/// <para>
/// The most security-sensitive handler in the milestone. Deciding and executing
/// are separate commands on purpose: a decision is a person's act and completes on
/// its own, while execution re-derives the fingerprint, re-checks the permission
/// and may still be refused by the domain (§14).
/// </para>
/// </remarks>
public sealed class AiApprovalHandler
{
    private readonly IAiApprovalRepository _approvals;
    private readonly IAiToolRequestRepository _toolRequests;
    private readonly IAgentRunRepository _runs;
    private readonly IAiToolRegistry _registry;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;

    public AiApprovalHandler(
        IAiApprovalRepository approvals,
        IAiToolRequestRepository toolRequests,
        IAgentRunRepository runs,
        IAiToolRegistry registry,
        IUnitOfWork unitOfWork,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock)
    {
        _approvals = approvals;
        _toolRequests = toolRequests;
        _runs = runs;
        _registry = registry;
        _unitOfWork = unitOfWork;
        _guard = guard;
        _audit = audit;
        _clock = clock;
    }

    public async Task HandleAsync(
        DecideApprovalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.AiApprove, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        AiApproval approval = await _approvals
            .FindAsync(command.OrganizationId, command.ApprovalId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(AiApproval), command.ApprovalId.ToString());

        // An approval is put to a person, and that person decides it. Holding
        // ai.approve is permission to answer what is asked of you, not permission
        // to answer for somebody else — and the proposal was made inside a run this
        // caller may not even read. Answered as missing rather than as forbidden,
        // for the same reason the run itself is (§13, §52).
        if (approval.RequestedOf != actor)
        {
            throw new EntityNotFoundException(
                nameof(AiApproval), command.ApprovalId.ToString());
        }

        AiToolRequest request = await _toolRequests
            .FindAsync(command.OrganizationId, approval.ToolRequestId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(AiToolRequest), approval.ToolRequestId.ToString());

        if (command.Approve)
        {
            approval.Approve(actor, _clock.UtcNow, command.ExpectedVersion);
            request.Approve(_clock.UtcNow, request.Version);
        }
        else
        {
            approval.Reject(actor, command.Reason, _clock.UtcNow, command.ExpectedVersion);
            request.Reject(command.Reason, _clock.UtcNow, request.Version);
        }

        AgentRun? run = await _runs
            .FindAsync(command.OrganizationId, approval.AgentRunId, cancellationToken)
            .ConfigureAwait(false);

        run?.AppendStep(
            AgentStepKind.ApprovalResolved,
            command.Approve
                ? $"Approved: {request.Summary}"
                : $"Rejected: {request.Summary}",
            _clock.UtcNow,
            command.Reason,
            approval.Id.Value);

        // The tool and the fingerprint, not the arguments. What was approved is
        // identified by a hash that can be compared, without copying whatever the
        // arguments named into a second store with different readers (§55).
        _audit.Record(
            command.Approve ? AuditAction.AiApprovalGranted : AuditAction.AiApprovalRejected,
            entityType: nameof(AiApproval),
            entityId: approval.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.AiApprove,
            semanticDelta: new
            {
                tool = request.ToolName,
                toolVersion = request.ToolVersion,
                fingerprint = request.Fingerprint,
            },
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs an approved tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Six things are re-established here, none of them trusted from earlier: the
    /// request is still approved, an approval exists that authorizes
    /// <em>these exact arguments</em>, it has not expired, the tool is still
    /// registered, the caller still holds the permission, and the arguments still
    /// parse. Any of them may have changed since the model asked (§14, §47).
    /// </para>
    /// <para>
    /// The fingerprint is recomputed from the stored arguments rather than compared
    /// to a stored copy of itself. That is what makes rewritten arguments fail
    /// rather than pass with an old hash attached (§13, §48).
    /// </para>
    /// </remarks>
    public async Task<ToolResult> HandleAsync(
        ExecuteApprovedToolCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.AiApprove, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        AiToolRequest request = await _toolRequests
            .FindAsync(command.OrganizationId, command.ToolRequestId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(AiToolRequest), command.ToolRequestId.ToString());

        AiApproval? approval = await _approvals
            .FindForRequestAsync(command.OrganizationId, request.Id, cancellationToken)
            .ConfigureAwait(false);

        // Checked before anything is said about the request's state. The person who
        // was asked is the person who runs it, and telling somebody else that the
        // request exists and what it is waiting for would be a disclosure about a
        // run they cannot read (§14, §46, §52).
        if (approval is null || approval.RequestedOf != actor)
        {
            throw new EntityNotFoundException(
                nameof(AiToolRequest), command.ToolRequestId.ToString());
        }

        if (request.Status != ToolRequestStatus.Approved)
        {
            return ToolResult.Refused(
                $"That request is {request.Status.ToString().ToLowerInvariant()} and "
                    + "cannot run.");
        }

        // Recomputed from what is about to run, never read back from the approval.
        string fingerprint = AiToolRequest.ComputeFingerprint(
            request.OrganizationId,
            request.AgentRunId,
            request.ToolName,
            request.ToolVersion,
            request.Arguments);

        if (!approval.Authorizes(fingerprint, _clock.UtcNow))
        {
            return ToolResult.Refused(
                "No current approval authorizes exactly this action. If the proposal "
                    + "changed or the approval lapsed, it has to be approved again.");
        }

        IAiTool? tool = _registry.Find(request.ToolName);

        if (tool is null || tool.Version != request.ToolVersion)
        {
            return ToolResult.Refused(
                "That tool is no longer available in the version that was approved.");
        }

        AgentRun? run = await _runs
            .FindAsync(command.OrganizationId, request.AgentRunId, cancellationToken)
            .ConfigureAwait(false);

        using JsonDocument arguments = JsonDocument.Parse(request.Arguments);

        // The tool re-checks its own permission as its first act, so a grant
        // revoked while the approval sat unanswered stops this here (§47).
        ToolResult result = await tool
            .ExecuteAsync(
                new ToolExecutionContext(
                    command.OrganizationId,
                    run?.UserId ?? actor,
                    request.AgentRunId,
                    run?.ProviderKey ?? "unknown"),
                arguments.RootElement,
                cancellationToken)
            .ConfigureAwait(false);

        // Terminal either way. A command that refused on concurrency grounds still
        // consumed the approval: approval permits an attempt, not a result (§14).
        request.Execute(
            result.Succeeded ? result.Content : result.Failure ?? "refused",
            _clock.UtcNow,
            request.Version);

        run?.AppendStep(
            result.Succeeded ? AgentStepKind.CommandExecuted : AgentStepKind.Refused,
            result.Succeeded
                ? $"AgencyOS did this: {request.Summary}"
                : $"Refused: {request.Summary}",
            _clock.UtcNow,
            result.Succeeded ? null : result.Failure,
            request.Id.Value);

        if (result.Succeeded)
        {
            // Recorded in addition to the command's own audit entry, not instead of
            // it. That one names the human actor because a human authorized this;
            // this one records that the proposal came from a run, which is the
            // provenance a reviewer needs later (§23, §51).
            _audit.Record(
                AuditAction.AiProposedWriteExecuted,
                entityType: nameof(AiToolRequest),
                entityId: request.Id.ToString(),
                organizationId: command.OrganizationId,
                permission: Permission.AiApprove,
                semanticDelta: new
                {
                    tool = request.ToolName,
                    toolVersion = request.ToolVersion,
                    agentRun = request.AgentRunId.ToString(),
                });
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }
}
