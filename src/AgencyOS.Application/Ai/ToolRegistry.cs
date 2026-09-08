using System.Text.Json;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Ai;

/// <summary>
/// What a tool run produced.
/// </summary>
/// <param name="Content">
/// What the model is told, bounded. Structured where the tool has structure, so
/// the next turn reasons over fields rather than over prose it has to re-parse.
/// </param>
/// <param name="Truncated">
/// Whether there was more. Said out loud, because a model given the first twenty
/// of two hundred rows and not told so will reason as though it saw all of them
/// (§33).
/// </param>
/// <param name="References">
/// The canonical objects this result came from, so anything the model says about
/// them can be cited and checked (§24).
/// </param>
public sealed record ToolResult(
    bool Succeeded,
    string Content,
    bool Truncated = false,
    IReadOnlyList<AiCitationReference>? References = null,
    string? Failure = null)
{
    public static ToolResult Ok(
        string content,
        bool truncated = false,
        IReadOnlyList<AiCitationReference>? references = null) =>
        new(true, content, truncated, references);

    public static ToolResult Refused(string failure) => new(false, string.Empty, Failure: failure);
}

/// <summary>Who is asking, and on whose authority.</summary>
/// <remarks>
/// Carried explicitly rather than read from ambient state, so a tool cannot be
/// invoked without somebody having decided which user it runs as. The run supplies
/// it and the run cannot change it (§9).
/// </remarks>
public sealed record ToolExecutionContext(
    OrganizationId OrganizationId,
    UserId UserId,
    AgentRunId AgentRunId,
    string ProviderKey);

/// <summary>
/// One thing a model may ask AgencyOS to do.
/// </summary>
/// <remarks>
/// <para>
/// Registered in code. There is no reflection over application services, no
/// discovery, and no way for model output to introduce a tool — a name the model
/// invents resolves to nothing, and resolving to nothing is a refusal rather than
/// an attempt (§32).
/// </para>
/// <para>
/// Every implementation re-checks authorization itself, using the user from the
/// execution context. A tool that trusted the run to have checked would be a
/// confused deputy: the run's own authorization was established when it started,
/// and a permission can be revoked between then and now (§9, §47).
/// </para>
/// </remarks>
public interface IAiTool
{
    /// <summary>Stable name. What the model sees and what an approval binds to.</summary>
    string Name { get; }

    /// <summary>
    /// Bumped when the meaning changes.
    /// </summary>
    /// <remarks>
    /// Part of the approval fingerprint, so an approval granted against version 1
    /// does not authorize version 2 of a tool that now does something else.
    /// </remarks>
    int Version { get; }

    /// <summary>What it does, as the model is told.</summary>
    string Description { get; }

    /// <summary>JSON Schema for the arguments. Validated before anything runs.</summary>
    string JsonSchema { get; }

    /// <summary>What it does to the world, and therefore what it takes to run it.</summary>
    ToolEffect Effect { get; }

    /// <summary>The permission the caller must hold, beyond the AI grants.</summary>
    /// <remarks>
    /// The tool's own domain permission: a read tool over contracts requires
    /// contracts.read. AI grants authorize using AI, never the underlying data
    /// (§42).
    /// </remarks>
    string RequiredPermission { get; }

    /// <summary>
    /// What a person is shown when asked to approve this.
    /// </summary>
    /// <remarks>
    /// Written by the tool from validated arguments, never by the model. An
    /// approval dialog showing the model's own description of its request is a
    /// dialog that asks the user to trust the thing being checked (§65).
    /// </remarks>
    string Describe(JsonElement arguments);

    Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Every tool this build has, and the only way to reach one.
/// </summary>
/// <remarks>
/// <para>
/// The registry is the second of M12's three boundaries. Context assembly decides
/// what the model may see; this decides what it may ask for; approval decides what
/// actually happens. The model influences none of them (ADR-0031).
/// </para>
/// <para>
/// A tool the model names that is not here is not an error to recover from — it is
/// a refusal, recorded on the run, and the run continues without it. Recovering by
/// guessing which tool was meant would let a model reach a tool by describing it.
/// </para>
/// </remarks>
public interface IAiToolRegistry
{
    /// <summary>Every registered tool, whatever the caller may use.</summary>
    IReadOnlyList<IAiTool> All { get; }

    /// <summary>
    /// The tools this caller may be offered, for this agent.
    /// </summary>
    /// <remarks>
    /// Filtered by the caller's permissions and by what the agent needs. A model is
    /// not offered a tool the user could not use: offering one and refusing the
    /// call would tell the model the tool exists, and a refusal message is a
    /// disclosure channel like any other (§6).
    /// </remarks>
    Task<IReadOnlyList<IAiTool>> AvailableAsync(
        OrganizationId organizationId,
        AgentKind agentKind,
        CancellationToken cancellationToken = default);

    /// <summary>Looks a tool up by name, or null when there is no such tool.</summary>
    IAiTool? Find(string name);
}

/// <summary>
/// Which tools each agent may be offered.
/// </summary>
/// <remarks>
/// An allow-list per agent rather than "every tool the user may use". A
/// relationship brief has no business proposing a thesis, and an agent that could
/// reach every tool would make the effect classification the only boundary
/// (§8, §25).
/// </remarks>
public static class AgentToolAllowList
{
    /// <summary>The tool names each agent may be offered.</summary>
    public static IReadOnlyDictionary<AgentKind, IReadOnlySet<string>> ByAgent { get; } =
        new Dictionary<AgentKind, IReadOnlySet<string>>
        {
            // The research copilot is the only agent that proposes, and everything
            // it proposes is an M11 object a person then accepts or dismisses.
            [AgentKind.ResearchCopilot] = Freeze(
                "research_case.get",
                "signals.search",
                "thesis.get",
                "prediction.get",
                "agency.search",
                "signal.propose",
                "thesis.propose",
                "prediction.propose",
                "task.create"),

            [AgentKind.RelationshipBrief] = Freeze(
                "person.get",
                "company.get",
                "relationship.intelligence",
                "signals.search",
                "task.create"),

            [AgentKind.DealBrief] = Freeze(
                "deal.get",
                "offer.compare",
                "task.create"),

            [AgentKind.ContractBrief] = Freeze(
                "contract.get",
                "contract.reconciliation",
                "obligations.list",
                "task.create"),

            // Read only, and no task creation either: a finance brief that could
            // create work items would be a finance workflow, and M12 keeps finance
            // strictly read-only (§28).
            [AgentKind.FinanceBrief] = Freeze(
                "receivables.list",
                "finance.reconciliation"),

            // Drafting needs context and nothing else. There is no send tool in
            // this build, for any agent (§29).
            [AgentKind.CommunicationDraft] = Freeze(
                "person.get",
                "company.get",
                "deal.get"),
        };

    private static IReadOnlySet<string> Freeze(params string[] names) =>
        new HashSet<string>(names, StringComparer.Ordinal);
}
