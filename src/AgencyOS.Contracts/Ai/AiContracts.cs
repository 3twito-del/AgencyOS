namespace AgencyOS.Contracts.Ai;

// ------------------------------------------------------------------- requests

/// <param name="Kind">
/// ResearchCopilot, RelationshipBrief, DealBrief, ContractBrief, FinanceBrief or
/// CommunicationDraft. A closed set: there is no general assistant, because an
/// agent whose task is unbounded cannot say what context it needs or when it is
/// finished.
/// </param>
/// <param name="SubjectKind">
/// ResearchCase, Person, Company, Deal or Contract, or None.
/// </param>
public sealed record StartAgentRunRequest(
    string Kind,
    string Task,
    string SubjectKind = "None",
    Guid? SubjectId = null,
    string? ModelKey = null);

public sealed record CancelAgentRunRequest(int ExpectedVersion);

/// <summary>
/// Decides one exact proposed action.
/// </summary>
/// <remarks>
/// There is deliberately no "approve everything from this run" and no standing
/// approval. A standing approval is a permission grant dressed as a click.
/// </remarks>
public sealed record DecideApprovalRequest(
    bool Approve,
    int ExpectedVersion,
    string? Reason = null);

/// <param name="MaximumSensitivity">
/// Internal, Confidential or Protected. Restricted is refused: material an
/// organization marked as never leaving has no ceiling that reaches it.
/// </param>
public sealed record UpdateAiProviderPolicyRequest(
    bool IsEnabled,
    string MaximumSensitivity,
    bool AllowsCanonicalWriteProposals,
    int ExpectedVersion);

// ------------------------------------------------------------------ responses

public sealed record AgentRunResponse(
    Guid Id,
    string Kind,
    string Status,
    string Task,
    string SubjectKind,
    Guid? SubjectId,
    string ProviderKey,
    string ModelKey,
    string? StartedByDisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Failure,
    int ModelInvocationCount,
    int ToolCallCount,
    int Version);

/// <param name="Steps">
/// What happened, in order. The reason this is exposed at all is so a person can
/// tell what the model said from what AgencyOS did.
/// </param>
/// <param name="PromptTemplateVersion">
/// Which wording produced this. The wording itself lives in source control; a run
/// is traceable to it without the prompt being readable here.
/// </param>
public sealed record AgentRunDetailResponse(
    AgentRunResponse Run,
    string? Result,
    string? FailureDetail,
    string PromptTemplateId,
    int PromptTemplateVersion,
    IReadOnlyList<AgentStepResponse> Steps,
    IReadOnlyList<AiToolRequestResponse> ToolRequests);

/// <param name="Kind">
/// ContextAssembled, ModelInvocation, ToolCall, ToolResult, ApprovalRequested,
/// ApprovalResolved, CommandExecuted, ProposalProduced or Refused.
/// </param>
public sealed record AgentStepResponse(
    int Sequence,
    string Kind,
    string Summary,
    string? Detail,
    Guid? ReferenceId,
    DateTimeOffset OccurredAt);

/// <param name="Effect">ReadOnly or CanonicalWrite.</param>
/// <param name="Summary">
/// What AgencyOS says this would do, written from the validated arguments rather
/// than by the model.
/// </param>
public sealed record AiToolRequestResponse(
    Guid Id,
    string ToolName,
    int ToolVersion,
    string Effect,
    string Status,
    string Summary,
    string Arguments,
    DateTimeOffset RequestedAt,
    string? Result,
    string? Refusal,
    int Version);

public sealed record AiApprovalResponse(
    Guid Id,
    Guid AgentRunId,
    Guid ToolRequestId,
    string AgentKind,
    string ToolName,
    string Summary,
    string Arguments,
    string Decision,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    bool HasExpired,
    string? DecidedByDisplayName,
    DateTimeOffset? DecidedAt,
    int Version);

/// <param name="IsProviderConfigured">
/// Always false in this build. Whether the server holds a credential is
/// deployment infrastructure, and reporting it on a tenant screen would describe
/// the deployment to whoever can open one.
/// </param>
public sealed record AiProviderPolicyResponse(
    string ProviderKey,
    bool IsProviderConfigured,
    bool IsEnabled,
    string MaximumSensitivity,
    bool AllowsCanonicalWriteProposals,
    DateTimeOffset? UpdatedAt,
    string? UpdatedByDisplayName,
    int Version);

public sealed record AiToolDescriptorResponse(
    string Name,
    int Version,
    string Description,
    string Effect,
    string RequiredPermission);

public sealed record AiModelDescriptorResponse(
    string Key,
    string ProviderKey,
    bool SupportsTools,
    bool SupportsStructuredOutput,
    bool SupportsStreaming,
    int? MaxContextTokens);

/// <summary>The identifier of a run just started.</summary>
public sealed record AgentRunIdResponse(Guid Id);

/// <param name="Succeeded">
/// Whether the canonical command ran. False is an ordinary outcome: an approval
/// permits an attempt, and the domain may still refuse on concurrency or its own
/// rules.
/// </param>
public sealed record ExecuteApprovedToolResponse(
    bool Succeeded,
    string? Content,
    string? Failure);
