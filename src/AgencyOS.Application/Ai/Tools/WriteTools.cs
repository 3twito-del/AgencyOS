using System.Globalization;
using System.Text.Json;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Intelligence;
using AgencyOS.Application.Legal;
using AgencyOS.Application.Deals;
using AgencyOS.Application.Finance;
using AgencyOS.Application.Tasks;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Application.Ai.Tools;

/// <summary>
/// Creates a task, once a person has approved it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The only tool in this build that changes business truth.</strong> It is
/// a facade over <see cref="CreateTaskHandler"/> and nothing more: the same
/// handler a human user reaches, with the same validation, the same audit entry
/// and the same actor. There is no second write path, and an architecture test
/// asserts there is not (§31).
/// </para>
/// <para>
/// A task was chosen as the first and only canonical write deliberately. It is
/// reversible, it is low-consequence, and it exercises the whole approval and
/// execution protocol end to end — which is what M12 needs to prove before
/// anything with real consequences is put behind a model request (§11, §12).
/// </para>
/// </remarks>
public sealed class TaskCreateTool : AiToolBase
{
    private readonly CreateTaskHandler _handler;

    public TaskCreateTool(TenantGuard guard, CreateTaskHandler handler)
        : base(guard) => _handler = handler;

    public override string Name => "task.create";

    public override int Version => 1;

    public override string Description =>
        "Proposes creating a task. This does not happen until a person approves it, "
            + "and you will be told the outcome.";

    public override string JsonSchema =>
        """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "maxLength": 512 },
            "notes": { "type": "string", "maxLength": 2000 },
            "dueOn": { "type": "string", "format": "date" },
            "priority": { "type": "string", "enum": ["Low", "Normal", "High", "Urgent"] }
          },
          "required": ["title"],
          "additionalProperties": false
        }
        """;

    public override ToolEffect Effect => ToolEffect.CanonicalWrite;

    public override string RequiredPermission => Permission.TasksWrite;

    /// <summary>
    /// What the approval dialog shows.
    /// </summary>
    /// <remarks>
    /// Built from the validated arguments, so what a person reads is what would run.
    /// The model's own account of its request never reaches this screen (§65).
    /// </remarks>
    public override string Describe(JsonElement arguments)
    {
        string title = OptionalString(arguments, "title") ?? "(no title)";
        string? due = OptionalString(arguments, "dueOn");
        string? priority = OptionalString(arguments, "priority");

        string described = $"Create task \"{title}\"";

        if (priority is { Length: > 0 })
        {
            described += $", priority {priority}";
        }

        if (due is { Length: > 0 })
        {
            described += $", due {due}";
        }

        return described;
    }

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        string? title = OptionalString(arguments, "title");

        if (string.IsNullOrWhiteSpace(title))
        {
            return ToolResult.Refused("title is required.");
        }

        DateTimeOffset? dueAt = null;

        if (OptionalString(arguments, "dueOn") is { Length: > 0 } due)
        {
            // Parsed strictly. A model that emits "next Tuesday" gets a refusal
            // rather than a date somebody has to notice is wrong later (§38).
            if (!DateOnly.TryParse(due, CultureInfo.InvariantCulture, out DateOnly date))
            {
                return ToolResult.Refused("dueOn must be an ISO date, such as 2026-09-30.");
            }

            dueAt = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        if (!Enum.TryParse(
            OptionalString(arguments, "priority") ?? nameof(TaskPriority.Normal),
            out TaskPriority priority))
        {
            return ToolResult.Refused("priority must be Low, Normal, High or Urgent.");
        }

        // The canonical command. Everything a person doing this by hand would get:
        // validation, the audit entry naming them, and the same refusals.
        TaskItemId id = await _handler
            .HandleAsync(
                new CreateTaskCommand(
                    context.OrganizationId,
                    title,
                    priority,
                    dueAt,
                    Subject: null,
                    OptionalString(arguments, "notes")),
                cancellationToken)
            .ConfigureAwait(false);

        return ToolResult.Ok(
            $"Task created: {id.Value}",
            truncated: false,
            [new AiCitationReference("Task", id.Value)]);
    }
}

/// <summary>Reads one research case and what it has gathered.</summary>
public sealed class ResearchCaseGetTool : AiToolBase
{
    private readonly IntelligenceQueryService _intelligence;

    public ResearchCaseGetTool(TenantGuard guard, IntelligenceQueryService intelligence)
        : base(guard) => _intelligence = intelligence;

    public override string Name => "research_case.get";

    public override int Version => 1;

    public override string Description =>
        "Reads a research case: the question it asks, its context, and the sources, "
            + "signals, theses, predictions and tasks attached to it.";

    public override string JsonSchema =>
        IdSchema("researchCaseId", "The research case to read.");

    public override ToolEffect Effect => ToolEffect.ReadOnly;

    public override string RequiredPermission => Permission.IntelligenceRead;

    public override string Describe(JsonElement arguments) =>
        TryGuid(arguments, "researchCaseId", out Guid id)
            ? $"Read research case {id}"
            : "Read a research case";

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGuid(arguments, "researchCaseId", out Guid caseId))
        {
            return ToolResult.Refused("researchCaseId must be a UUID.");
        }

        ResearchCaseDetailModel? detail = await _intelligence
            .GetResearchCaseAsync(
                context.OrganizationId,
                new Domain.Intelligence.ResearchCaseId(caseId),
                cancellationToken)
            .ConfigureAwait(false);

        if (detail is null)
        {
            return NotAvailable();
        }

        System.Text.StringBuilder builder = new();

        Field(builder, "Question", detail.ResearchCase.Question);
        Field(builder, "Status", detail.ResearchCase.Status);
        Field(builder, "Context", detail.Context);
        Field(builder, "Conclusion", detail.Conclusion);

        List<AiCitationReference> references =
            [new AiCitationReference("ResearchCase", caseId)];

        if (detail.Sources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Sources attached:");

            foreach (IntelligenceSourceModel source in detail.Sources.Take(MaximumRows))
            {
                builder.Append("  - ").Append(source.Title)
                    .Append(" [").Append(source.Kind).Append(", ")
                    .Append(source.Reliability).AppendLine("]");

                // Said explicitly, because it changes what a brief may claim. A
                // URL is a reference AgencyOS did not archive and cannot produce.
                if (!source.IsHeldByAgencyOS)
                {
                    builder.AppendLine("    (a reference; AgencyOS does not hold this)");
                }

                references.Add(new AiCitationReference("Source", source.Id.Value));
            }
        }

        if (detail.Signals.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Signals attached:");

            foreach (SignalSummaryModel signal in detail.Signals.Take(MaximumRows))
            {
                builder.Append("  - ").Append(signal.Title)
                    .Append(" [").Append(signal.Verification).AppendLine("]");
                builder.Append("    claim: ").AppendLine(signal.Claim);

                references.Add(new AiCitationReference("Signal", signal.Id.Value));
            }
        }

        if (detail.Tasks.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Tasks:");

            foreach (ResearchTaskModel task in detail.Tasks.Take(MaximumRows))
            {
                builder.Append("  - ").Append(task.Title).Append(" [").Append(task.State)
                    .Append(task.IsOverdue ? ", overdue" : string.Empty).AppendLine("]");
            }
        }

        return Bounded(builder.ToString(), references);
    }
}

/// <summary>Reads one negotiation.</summary>
public sealed class DealGetTool : AiToolBase
{
    private readonly DealQueryService _deals;

    public DealGetTool(TenantGuard guard, DealQueryService deals)
        : base(guard) => _deals = deals;

    public override string Name => "deal.get";

    public override int Version => 1;

    public override string Description =>
        "Reads a negotiation: its name, status, counterparties and where the offer "
            + "thread stands.";

    public override string JsonSchema => IdSchema("dealId", "The deal to read.");

    public override ToolEffect Effect => ToolEffect.ReadOnly;

    public override string RequiredPermission => Permission.DealsRead;

    public override string Describe(JsonElement arguments) =>
        TryGuid(arguments, "dealId", out Guid id) ? $"Read deal {id}" : "Read a deal";

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGuid(arguments, "dealId", out Guid dealId))
        {
            return ToolResult.Refused("dealId must be a UUID.");
        }

        DealDetailModel? deal = await _deals
            .GetDealAsync(
                context.OrganizationId, new Domain.Deals.DealId(dealId), cancellationToken)
            .ConfigureAwait(false);

        if (deal is null)
        {
            return NotAvailable();
        }

        System.Text.StringBuilder builder = new();

        Field(builder, "Deal", deal.Deal.Name);
        Field(builder, "Kind", deal.Deal.Kind);
        Field(builder, "Status", deal.Deal.Status);
        Field(builder, "Opened", deal.Deal.OpenedOn);

        // Economics are redacted where they are projected, by the query service
        // this calls. Whatever a caller without deals.economics.read cannot see on
        // the deal page, they cannot see here either (ADR-0021).
        return Bounded(
            builder.ToString(),
            [new AiCitationReference("Deal", dealId)]);
    }
}

/// <summary>Reads one contract.</summary>
public sealed class ContractGetTool : AiToolBase
{
    private readonly ContractQueryService _contracts;

    public ContractGetTool(TenantGuard guard, ContractQueryService contracts)
        : base(guard) => _contracts = contracts;

    public override string Name => "contract.get";

    public override int Version => 1;

    public override string Description =>
        "Reads a contract: title, kind, status, effective date and its open "
            + "obligations. Does not return drafted clause text or legal analysis.";

    public override string JsonSchema => IdSchema("contractId", "The contract to read.");

    public override ToolEffect Effect => ToolEffect.ReadOnly;

    public override string RequiredPermission => Permission.ContractsRead;

    public override string Describe(JsonElement arguments) =>
        TryGuid(arguments, "contractId", out Guid id)
            ? $"Read contract {id}"
            : "Read a contract";

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryGuid(arguments, "contractId", out Guid contractId))
        {
            return ToolResult.Refused("contractId must be a UUID.");
        }

        ContractDetailModel? contract = await _contracts
            .GetContractAsync(
                context.OrganizationId,
                new Domain.Legal.ContractId(contractId),
                cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return NotAvailable();
        }

        System.Text.StringBuilder builder = new();

        Field(builder, "Contract", contract.Contract.Title);
        Field(builder, "Kind", contract.Contract.Kind);
        Field(builder, "Status", contract.Contract.Status);
        Field(builder, "Executed", contract.Contract.ExecutedOn);
        Field(builder, "Effective", contract.Contract.EffectiveOn);

        // Privileged analysis is redacted by the query service, and a brief must
        // not describe legal risk it cannot see. The prompt says so as well; this
        // is where it is actually true (§27, ADR-0022).
        return Bounded(
            builder.ToString(),
            [new AiCitationReference("Contract", contractId)]);
    }
}

/// <summary>Lists receivables.</summary>
public sealed class ReceivablesListTool : AiToolBase
{
    private readonly FinanceQueryService _finance;

    public ReceivablesListTool(TenantGuard guard, FinanceQueryService finance)
        : base(guard) => _finance = finance;

    public override string Name => "receivables.list";

    public override int Version => 1;

    public override string Description =>
        "Lists receivables: what is owed, to whom, when it is due and whether it is "
            + "overdue.";

    public override string JsonSchema =>
        """
        {
          "type": "object",
          "properties": {
            "overdueOnly": { "type": "boolean" },
            "limit": { "type": "integer", "minimum": 1, "maximum": 25 }
          },
          "additionalProperties": false
        }
        """;

    public override ToolEffect Effect => ToolEffect.ReadOnly;

    public override string RequiredPermission => Permission.FinanceRead;

    public override string Describe(JsonElement arguments) => "List receivables";

    protected override async Task<ToolResult> RunAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        bool overdueOnly = arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("overdueOnly", out JsonElement flag)
            && flag.ValueKind == JsonValueKind.True;

        IReadOnlyList<ReceivableModel> receivables = await _finance
            .ListReceivablesAsync(
                context.OrganizationId,
                new ReceivableFilter(OverdueOnly: overdueOnly),
                Math.Min(OptionalInt(arguments, "limit") ?? MaximumRows, MaximumRows),
                cancellationToken)
            .ConfigureAwait(false);

        if (receivables.Count == 0)
        {
            return ToolResult.Ok("No receivables matched.");
        }

        System.Text.StringBuilder builder = new();
        List<AiCitationReference> references = [];

        foreach (ReceivableModel receivable in receivables)
        {
            builder.Append("- ").Append(receivable.Reference ?? "(no reference)")
                .Append(": ").Append(receivable.Status)
                .Append(", due ").AppendLine(
                    receivable.DueOn?.ToString("O", CultureInfo.InvariantCulture) ?? "unset");

            references.Add(new AiCitationReference("Receivable", receivable.Id.Value));
        }

        return Bounded(builder.ToString(), references);
    }
}
