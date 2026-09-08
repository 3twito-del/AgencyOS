using System.Security.Cryptography;
using System.Text;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Ai;

/// <summary>Opaque, immutable identifier for an <see cref="AiToolRequest"/>.</summary>
public readonly record struct AiToolRequestId(Guid Value)
{
    public static AiToolRequestId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What a tool does, and therefore what it takes to run one.
/// </summary>
/// <remarks>
/// The classification is on the tool rather than on the request, so a model cannot
/// influence it by asking differently. It is the single input to the human-in-the-
/// loop default in §41 (ADR-0031).
/// </remarks>
public enum ToolEffect
{
    /// <summary>
    /// Reads data the caller may already read. Runs without asking.
    /// </summary>
    /// <remarks>
    /// "Without asking" is not "without checking": authorization is re-checked
    /// server-side on every call, and the data policy still decides what may leave
    /// (§9, §41).
    /// </remarks>
    ReadOnly = 1,

    /// <summary>
    /// Changes business truth through a canonical command. Always needs a person.
    /// </summary>
    /// <remarks>
    /// Every one of these goes through the same application handler a human user
    /// would go through, with the same validation, concurrency and audit. The tool
    /// is a facade over the command and never a second way in (§11, §31).
    /// </remarks>
    CanonicalWrite = 2,

    /// <summary>
    /// Causes something outside AgencyOS. Never available to a model in M12.
    /// </summary>
    /// <remarks>
    /// Reserved and deliberately unused. M10 proved that an external send cannot be
    /// taken back and can rest in an unknown outcome; putting that behind a model
    /// request in the first milestone that has models would be the wrong order
    /// (§12, §15).
    /// </remarks>
    ExternalEffect = 3,
}

/// <summary>Where a tool request stands.</summary>
public enum ToolRequestStatus
{
    /// <summary>A read tool, or a write tool not yet put to a person.</summary>
    Proposed = 1,

    AwaitingApproval = 2,
    Approved = 3,
    Rejected = 4,

    /// <summary>Approved and run. Terminal, whatever the command answered.</summary>
    Executed = 5,

    /// <summary>Nobody answered in time.</summary>
    Expired = 6,

    /// <summary>The run was cancelled before this was decided.</summary>
    Abandoned = 7,
}

/// <summary>
/// A tool the model asked for, as AgencyOS understood it.
/// </summary>
/// <remarks>
/// <para>
/// The fingerprint is the whole point of this type. An approval binds to
/// <em>these arguments</em>, normalized and hashed, so that approving
/// <c>task.create(title: "Call Jane")</c> cannot be turned into
/// <c>task.create(title: "Send confidential contract")</c> by a second model turn
/// (§13).
/// </para>
/// <para>
/// Arguments are stored as the JSON AgencyOS validated against the tool's schema,
/// not as the model emitted them. What is executed is what a person saw.
/// </para>
/// </remarks>
public sealed class AiToolRequest
{
    private AiToolRequest()
    {
    }

    public AiToolRequestId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AgentRunId AgentRunId { get; private set; }

    /// <summary>Which registered tool. Never a name the model invented.</summary>
    public string ToolName { get; private set; } = string.Empty;

    /// <summary>
    /// Which version of that tool.
    /// </summary>
    /// <remarks>
    /// Part of the fingerprint. A tool whose meaning changed is a different tool,
    /// and an approval granted against the old one does not carry over.
    /// </remarks>
    public int ToolVersion { get; private set; }

    public ToolEffect Effect { get; private set; }

    public ToolRequestStatus Status { get; private set; }

    /// <summary>The validated arguments, canonicalized.</summary>
    public string Arguments { get; private set; } = string.Empty;

    /// <summary>
    /// SHA-256 over tenant, run, tool, version and normalized arguments.
    /// </summary>
    /// <remarks>
    /// Computed here rather than supplied, so nothing upstream can present a
    /// fingerprint for arguments other than the ones stored (§48).
    /// </remarks>
    public string Fingerprint { get; private set; } = string.Empty;

    /// <summary>
    /// What a person will be shown when asked to approve this.
    /// </summary>
    /// <remarks>
    /// Written by AgencyOS from the validated arguments, never by the model. A
    /// model-authored description of its own request is a request to be trusted
    /// about what it is asking for (§65).
    /// </remarks>
    public string Summary { get; private set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>What the tool answered, bounded. Null until it ran.</summary>
    public string? Result { get; private set; }

    /// <summary>Why it did not run, when it did not.</summary>
    public string? Refusal { get; private set; }

    public int Version { get; private set; }

    public bool IsTerminal => Status
        is ToolRequestStatus.Executed
        or ToolRequestStatus.Rejected
        or ToolRequestStatus.Expired
        or ToolRequestStatus.Abandoned;

    public static AiToolRequest Propose(
        OrganizationId organizationId,
        AgentRunId runId,
        string toolName,
        int toolVersion,
        ToolEffect effect,
        string canonicalArguments,
        string summary,
        DateTimeOffset now)
    {
        if (effect == ToolEffect.ExternalEffect)
        {
            throw new DomainException(
                "No external-effect tool is available to a model in this build. "
                    + "Sending is a canonical workflow a person carries out.");
        }

        string name = Ensure.NotBlankMax(toolName, nameof(toolName), 100);
        string arguments = Ensure.NotBlankMax(
            canonicalArguments, nameof(canonicalArguments), 20_000);

        if (toolVersion < 1)
        {
            throw new DomainException("A tool version starts at one.");
        }

        return new AiToolRequest
        {
            Id = AiToolRequestId.New(),
            OrganizationId = organizationId,
            AgentRunId = runId,
            ToolName = name,
            ToolVersion = toolVersion,
            Effect = effect,

            // A read tool needs no person. A write tool is not executable until one
            // says so, and starts life saying that about itself (§41).
            Status = effect == ToolEffect.ReadOnly
                ? ToolRequestStatus.Proposed
                : ToolRequestStatus.AwaitingApproval,

            Arguments = arguments,
            Fingerprint = ComputeFingerprint(organizationId, runId, name, toolVersion, arguments),
            Summary = Ensure.NotBlankMax(summary, nameof(summary), 1000),
            RequestedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// The value an approval binds to.
    /// </summary>
    /// <remarks>
    /// Includes the tenant and the run as well as the arguments, so a fingerprint
    /// cannot be replayed across organizations or lifted from one run into another.
    /// </remarks>
    public static string ComputeFingerprint(
        OrganizationId organizationId,
        AgentRunId runId,
        string toolName,
        int toolVersion,
        string canonicalArguments)
    {
        // A unit separator, written as an escape rather than as a raw control
        // character in source. JSON escapes control characters, so no argument
        // value can contain this byte and shift the field boundaries to collide
        // with a different request.
        string material = string.Join(
            '',
            organizationId.Value.ToString("D"),
            runId.Value.ToString("D"),
            toolName,
            toolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            canonicalArguments);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>Records that a person allowed this exact request.</summary>
    public void Approve(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);
        Require(ToolRequestStatus.AwaitingApproval);

        Status = ToolRequestStatus.Approved;
        ResolvedAt = now;
        Version++;
    }

    public void Reject(string? reason, DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);
        Require(ToolRequestStatus.AwaitingApproval);

        Status = ToolRequestStatus.Rejected;
        Refusal = Ensure.OptionalMax(reason, nameof(reason), 1000);
        ResolvedAt = now;
        Version++;
    }

    /// <summary>
    /// Records that the tool ran.
    /// </summary>
    /// <remarks>
    /// Terminal whatever the tool answered. A command that refused on concurrency
    /// grounds still consumed the approval: approval means a person allowed
    /// AgencyOS to <em>attempt</em> the action, not that the action would succeed
    /// (§14).
    /// </remarks>
    public void Execute(string result, DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);

        if (Status is not (ToolRequestStatus.Approved or ToolRequestStatus.Proposed))
        {
            throw new DomainException(
                $"A tool request that is {Status} cannot run. A write tool runs "
                    + "only while approved, and runs once.");
        }

        Status = ToolRequestStatus.Executed;
        Result = Ensure.NotBlankMax(result, nameof(result), 20_000);
        ResolvedAt = now;
        Version++;
    }

    public void Expire(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);
        Require(ToolRequestStatus.AwaitingApproval);

        Status = ToolRequestStatus.Expired;
        ResolvedAt = now;
        Version++;
    }

    /// <summary>Records that the run ended before anybody decided.</summary>
    public void Abandon(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);

        if (IsTerminal)
        {
            return;
        }

        Status = ToolRequestStatus.Abandoned;
        ResolvedAt = now;
        Version++;
    }

    private void Require(params ToolRequestStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(
                $"A tool request that is {Status} cannot do that. Expected one of: "
                    + string.Join(", ", allowed) + ".");
        }
    }

    private void Guard(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(AiToolRequest), Id.ToString(), expectedVersion, Version);
        }
    }
}
