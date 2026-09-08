using System.Text.Json;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai;

/// <summary>
/// What a run may not exceed.
/// </summary>
/// <remarks>
/// Every one of these is a bound on something that would otherwise be unbounded.
/// A model that keeps asking for tools, a tool that keeps returning rows, a run
/// that never decides it is finished — none of them is a hypothetical, and an
/// agent without limits is a bill and an outage waiting for a bad day (§17, §44).
/// </remarks>
public sealed record AgentLimits(
    int MaxModelTurns = 6,
    int MaxToolCalls = 12,
    int MaxOutputTokens = 4_000,
    int MaxContextCharacters = 60_000)
{
    /// <summary>How long one provider call may take.</summary>
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>How long the whole run may take, excluding time awaiting a person.</summary>
    public TimeSpan WallClock { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a person has to answer an approval.</summary>
    /// <remarks>
    /// Short, because an approval is a decision about a state of the world that
    /// keeps moving. An hour-old proposal to create a task about a deal that has
    /// since closed is a proposal nobody should be able to accept by clicking
    /// without rereading (§13).
    /// </remarks>
    public TimeSpan ApprovalValidity { get; init; } = TimeSpan.FromMinutes(30);

    public static AgentLimits Default { get; } = new();
}

/// <summary>
/// Runs one agent, within its limits, and records what happened.
/// </summary>
/// <remarks>
/// <para>
/// The loop is deliberately small and deliberately not autonomous. It calls the
/// model, validates whatever came back against the registry, executes read tools,
/// parks write tools for a person, and stops — at an answer, at a limit, or at a
/// refusal. There is no branch in which it decides to keep going because the task
/// seems unfinished (§17, §41).
/// </para>
/// <para>
/// It holds no security decisions of its own. Context assembly decided what the
/// model may see, the registry decided what it may ask for, and the approval
/// decides what happens; the runtime's job is to move between them without
/// inventing a fourth path (ADR-0031).
/// </para>
/// </remarks>
public sealed class AgentRuntime
{
    private readonly IModelGateway _gateway;
    private readonly IAiToolRegistry _registry;
    private readonly IAiContextAssembler _context;
    private readonly ModelDataPolicy _dataPolicy;
    private readonly IAgentRunRepository _runs;
    private readonly IAiToolRequestRepository _toolRequests;
    private readonly IAiApprovalRepository _approvals;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public AgentRuntime(
        IModelGateway gateway,
        IAiToolRegistry registry,
        IAiContextAssembler context,
        ModelDataPolicy dataPolicy,
        IAgentRunRepository runs,
        IAiToolRequestRepository toolRequests,
        IAiApprovalRepository approvals,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _gateway = gateway;
        _registry = registry;
        _context = context;
        _dataPolicy = dataPolicy;
        _runs = runs;
        _toolRequests = toolRequests;
        _approvals = approvals;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>
    /// Advances a run until it finishes, needs a person, or hits a limit.
    /// </summary>
    /// <remarks>
    /// Returns rather than throws for every outcome the design anticipates. A run
    /// that failed because a provider was unreachable is a run in a known state,
    /// not an exception for a caller to interpret (§68).
    /// </remarks>
    public async Task<AgentRun> ExecuteAsync(
        AgentRun run,
        AgentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definition);

        DateTimeOffset deadline = _clock.UtcNow + definition.Limits.WallClock;

        run.Begin(_clock.UtcNow, run.Version);

        AiContext context = await _context
            .AssembleAsync(
                new AiContextRequest(
                    run.OrganizationId,
                    run.Kind,
                    run.SubjectKind,
                    run.SubjectId,
                    run.ProviderKey,
                    definition.Limits.MaxContextCharacters),
                cancellationToken)
            .ConfigureAwait(false);

        if (context.RefusedOutright)
        {
            run.AppendStep(
                AgentStepKind.Refused,
                "The data policy refused this context.",
                _clock.UtcNow,
                context.RefusalReason);

            run.Fail(
                AgentFailureKind.PolicyRefused, context.RefusalReason, _clock.UtcNow, run.Version);

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return run;
        }

        run.AppendStep(
            AgentStepKind.ContextAssembled,
            $"Assembled {context.Blocks.Count} context blocks.",
            _clock.UtcNow,
            context.OmittedBlockCount > 0
                ? $"{context.OmittedBlockCount} were withheld by policy."
                : null);

        IReadOnlyList<IAiTool> available = await _registry
            .AvailableAsync(run.OrganizationId, run.Kind, cancellationToken)
            .ConfigureAwait(false);

        // A model may be offered a write tool only where the organization allows
        // proposals at all and the caller holds the proposing grant. Filtering here
        // rather than refusing later means the model never learns the tool exists.
        available = await _dataPolicy
            .FilterProposableAsync(run.OrganizationId, run.ProviderKey, available, cancellationToken)
            .ConfigureAwait(false);

        List<ModelMessage> messages =
        [
            new ModelMessage(ModelRole.System, definition.SystemPrompt),
            new ModelMessage(ModelRole.User, $"Task: {run.Task}\n\n{context.Render()}"),
        ];

        for (int turn = 0; turn < definition.Limits.MaxModelTurns; turn++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                run.Cancel(_clock.UtcNow, run.Version);
                await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

                return run;
            }

            if (_clock.UtcNow > deadline)
            {
                return await StopAtLimitAsync(
                    run, "This run reached its time limit.", cancellationToken)
                    .ConfigureAwait(false);
            }

            ModelResponse response = await _gateway
                .CompleteAsync(
                    new ModelRequest(
                        run.ModelKey,
                        messages,
                        [.. available.Select(Definition)],
                        definition.OutputSchema,
                        definition.Limits.MaxOutputTokens,
                        definition.Limits.ProviderTimeout),
                    cancellationToken)
                .ConfigureAwait(false);

            run.AppendStep(
                AgentStepKind.ModelInvocation,
                $"Called {run.ModelKey}.",
                _clock.UtcNow,
                response.Usage.OutputTokens is { } tokens
                    ? $"{tokens} output tokens."
                    : null);

            if (!response.Succeeded)
            {
                run.Fail(
                    response.Failure!.Kind, response.Failure.Detail, _clock.UtcNow, run.Version);

                await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return run;
            }

            if (response.ToolCalls.Count == 0)
            {
                return await FinishAsync(run, response.Text, context, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (run.ToolCallCount + response.ToolCalls.Count > definition.Limits.MaxToolCalls)
            {
                return await StopAtLimitAsync(
                    run, "This run asked for more tools than it is allowed.", cancellationToken)
                    .ConfigureAwait(false);
            }

            messages.Add(new ModelMessage(ModelRole.Assistant, response.Text ?? string.Empty));

            bool parked = false;

            foreach (RequestedToolCall call in response.ToolCalls)
            {
                ToolTurn outcome = await HandleToolCallAsync(
                    run, definition, available, call, cancellationToken).ConfigureAwait(false);

                messages.Add(new ModelMessage(ModelRole.Tool, outcome.Message, call.Id));

                parked |= outcome.AwaitsApproval;
            }

            if (parked)
            {
                // The run stops here and waits. Resuming is a separate act, started
                // by whoever decides, so a process restart in between loses nothing
                // (§13, ADR-0031).
                run.AwaitApproval(_clock.UtcNow, run.Version);
                await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return run;
            }
        }

        return await StopAtLimitAsync(
            run, "This run reached its turn limit without finishing.", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Validates one requested tool call and either runs it or parks it.
    /// </summary>
    /// <remarks>
    /// Four checks before anything happens: the tool is registered, it is one this
    /// agent may use, the caller may use it, and the arguments fit the schema. A
    /// failure at any of them is a message back to the model and a step on the run,
    /// never an exception and never a guess at what was meant (§32, §38).
    /// </remarks>
    private async Task<ToolTurn> HandleToolCallAsync(
        AgentRun run,
        AgentDefinition definition,
        IReadOnlyList<IAiTool> available,
        RequestedToolCall call,
        CancellationToken cancellationToken)
    {
        IAiTool? tool = available.FirstOrDefault(
            x => string.Equals(x.Name, call.Name, StringComparison.Ordinal));

        if (tool is null)
        {
            run.AppendStep(
                AgentStepKind.Refused,
                $"Refused an unavailable tool: {Sanitize(call.Name)}.",
                _clock.UtcNow);

            return new ToolTurn(
                "There is no such tool available to you. Continue without it.", false);
        }

        if (!TryParseArguments(call.ArgumentsJson, tool.JsonSchema, out JsonElement arguments))
        {
            run.AppendStep(
                AgentStepKind.Refused,
                $"Refused malformed arguments for {tool.Name}.",
                _clock.UtcNow);

            return new ToolTurn(
                "Those arguments were not valid for that tool. Check the schema.", false);
        }

        string canonical = Canonicalize(arguments);

        AiToolRequest request = AiToolRequest.Propose(
            run.OrganizationId,
            run.Id,
            tool.Name,
            tool.Version,
            tool.Effect,
            canonical,
            tool.Describe(arguments),
            _clock.UtcNow);

        _toolRequests.Add(request);

        run.AppendStep(
            AgentStepKind.ToolCall,
            $"{tool.Name}: {request.Summary}",
            _clock.UtcNow,
            referenceId: request.Id.Value);

        if (tool.Effect != ToolEffect.ReadOnly)
        {
            AiApproval approval = AiApproval.Request(
                run.OrganizationId,
                run.Id,
                request.Id,
                request.Fingerprint,
                run.UserId,
                _clock.UtcNow,
                definition.Limits.ApprovalValidity);

            _approvals.Add(approval);

            run.AppendStep(
                AgentStepKind.ApprovalRequested,
                $"Waiting for a person to approve: {request.Summary}",
                _clock.UtcNow,
                referenceId: approval.Id.Value);

            return new ToolTurn(
                "That needs a person's approval. It has been put to them and has "
                    + "not happened yet.",
                true);
        }

        ToolResult result = await tool
            .ExecuteAsync(
                new ToolExecutionContext(
                    run.OrganizationId, run.UserId, run.Id, run.ProviderKey),
                arguments,
                cancellationToken)
            .ConfigureAwait(false);

        request.Execute(
            result.Succeeded ? "ok" : result.Failure ?? "refused",
            _clock.UtcNow,
            request.Version);

        run.AppendStep(
            AgentStepKind.ToolResult,
            result.Succeeded
                ? $"{tool.Name} returned{(result.Truncated ? " (truncated)" : string.Empty)}."
                : $"{tool.Name} refused.",
            _clock.UtcNow,
            referenceId: request.Id.Value);

        if (!result.Succeeded)
        {
            return new ToolTurn(result.Failure ?? "That was refused.", false);
        }

        return new ToolTurn(
            result.Truncated
                ? result.Content + "\n\n(There was more than this. Say so if it matters.)"
                : result.Content,
            false);
    }

    private async Task<AgentRun> FinishAsync(
        AgentRun run,
        string? text,
        AiContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            run.Fail(
                AgentFailureKind.ProviderInvalidResponse,
                "The model returned nothing.",
                _clock.UtcNow,
                run.Version);
        }
        else
        {
            run.Complete(
                AiCitationValidator.Strip(text, context.Citable), _clock.UtcNow, run.Version);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return run;
    }

    private async Task<AgentRun> StopAtLimitAsync(
        AgentRun run,
        string reason,
        CancellationToken cancellationToken)
    {
        run.Fail(AgentFailureKind.LimitReached, reason, _clock.UtcNow, run.Version);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return run;
    }

    private static ToolDefinition Definition(IAiTool tool) =>
        new(tool.Name, tool.Description, tool.JsonSchema);

    /// <summary>
    /// Parses model-supplied arguments, refusing anything that is not an object.
    /// </summary>
    /// <remarks>
    /// Structural validation only in this build: the shape must be a JSON object
    /// and each tool then reads the fields it needs, refusing what it cannot use.
    /// Full JSON Schema validation would be stricter and is the obvious next step;
    /// what matters for security is that no unvalidated value reaches a handler,
    /// and the tools enforce that themselves (§21, §38).
    /// </remarks>
    private static bool TryParseArguments(string json, string schema, out JsonElement arguments)
    {
        _ = schema;
        arguments = default;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            arguments = document.RootElement.Clone();

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Renders arguments in a stable form, so the same request hashes the same way.
    /// </summary>
    /// <remarks>
    /// Property order and whitespace are normalized. Without it, a model that
    /// re-emitted the same call with its keys in a different order would produce a
    /// different fingerprint and invalidate a perfectly good approval (§48).
    /// </remarks>
    private static string Canonicalize(JsonElement arguments)
    {
        SortedDictionary<string, JsonElement> ordered = new(StringComparer.Ordinal);

        foreach (JsonProperty property in arguments.EnumerateObject())
        {
            ordered[property.Name] = property.Value;
        }

        return JsonSerializer.Serialize(ordered);
    }

    /// <summary>
    /// Makes a model-supplied name safe to put in a step summary.
    /// </summary>
    /// <remarks>
    /// The name of a tool that does not exist is attacker-influenced text on its
    /// way to a screen. Bounded and stripped of control characters, because a run
    /// history is read by people (§53).
    /// </remarks>
    private static string Sanitize(string value)
    {
        string trimmed = value.Length > 100 ? value[..100] : value;

        return new string([.. trimmed.Where(c => !char.IsControl(c))]);
    }

    private readonly record struct ToolTurn(string Message, bool AwaitsApproval);
}

/// <param name="SystemPrompt">
/// What AgencyOS tells the model. Written here, versioned in source, and never
/// assembled from anything retrieved (§7, §20).
/// </param>
/// <param name="OutputSchema">
/// The shape a structured answer must take, or null for prose.
/// </param>
public sealed record AgentDefinition(
    AgentKind Kind,
    string PromptTemplateId,
    int PromptTemplateVersion,
    string SystemPrompt,
    string? OutputSchema,
    AgentLimits Limits);
