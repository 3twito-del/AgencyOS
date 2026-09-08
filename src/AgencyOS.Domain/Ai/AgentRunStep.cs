using AgencyOS.Domain.Common;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Ai;

/// <summary>What happened at one point in a run.</summary>
public enum AgentStepKind
{
    /// <summary>Context was assembled and checked against the data policy.</summary>
    ContextAssembled = 1,

    /// <summary>A provider was called.</summary>
    ModelInvocation = 2,

    /// <summary>The model asked for a tool.</summary>
    ToolCall = 3,

    /// <summary>A tool answered.</summary>
    ToolResult = 4,

    /// <summary>A person was asked to approve something.</summary>
    ApprovalRequested = 5,

    /// <summary>A person answered.</summary>
    ApprovalResolved = 6,

    /// <summary>A canonical command ran, because a person approved it.</summary>
    CommandExecuted = 7,

    /// <summary>A proposal was produced for a person to accept or dismiss.</summary>
    ProposalProduced = 8,

    /// <summary>Something was refused: a policy, a permission or a limit.</summary>
    Refused = 9,
}

/// <summary>
/// One step of a run, written once.
/// </summary>
/// <remarks>
/// <para>
/// The point of the history is that a person can tell <strong>what the model said
/// from what AgencyOS did</strong> (§64). A step that said "created a task" without
/// distinguishing "the model asked" from "a person approved and the command ran"
/// would collapse exactly the difference the milestone exists to keep.
/// </para>
/// <para>
/// Steps carry a summary and a bounded detail, never a prompt body and never the
/// content of a document the run read. That material lives in M10 and M11 under
/// its own classification, and copying it here would put it in a store with weaker
/// rules (§19).
/// </para>
/// </remarks>
public sealed class AgentRunStep
{
    private AgentRunStep()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AgentRunId AgentRunId { get; private set; }

    /// <summary>Position in the run, from one. Unique within the run.</summary>
    public int Sequence { get; private set; }

    public AgentStepKind Kind { get; private set; }

    /// <summary>What happened, in one line a person can read.</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>
    /// Bounded supporting detail, or null.
    /// </summary>
    /// <remarks>
    /// Never a prompt, never a model response body, never the text of a source. A
    /// tool's arguments may appear here only where they are already the kind of
    /// thing a person would see in the approval dialog (§55).
    /// </remarks>
    public string? Detail { get; private set; }

    /// <summary>
    /// The row this step is about, when there is one.
    /// </summary>
    /// <remarks>
    /// A tool request, an approval, or the canonical object a command created. Held
    /// as a bare identifier rather than a typed arc because a step points at
    /// whichever of several kinds its own <see cref="Kind"/> implies, and the arc
    /// would be nine columns to say what the kind already says.
    /// </remarks>
    public Guid? ReferenceId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    internal static AgentRunStep Record(
        OrganizationId organizationId,
        AgentRunId runId,
        int sequence,
        AgentStepKind kind,
        string summary,
        DateTimeOffset occurredAt,
        string? detail,
        Guid? referenceId)
    {
        if (sequence < 1)
        {
            throw new DomainException("A step sequence starts at one.");
        }

        return new AgentRunStep
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            AgentRunId = runId,
            Sequence = sequence,
            Kind = kind,
            Summary = Ensure.NotBlankMax(summary, nameof(summary), 500),
            Detail = Ensure.OptionalMax(detail, nameof(detail), 4000),
            ReferenceId = referenceId,
            OccurredAt = occurredAt,
        };
    }
}
