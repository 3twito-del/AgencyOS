using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Ai;

/// <summary>Opaque, immutable identifier for an <see cref="AiApproval"/>.</summary>
public readonly record struct AiApprovalId(Guid Value)
{
    public static AiApprovalId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>What a person decided.</summary>
public enum ApprovalDecision
{
    /// <summary>Nobody has answered yet.</summary>
    Pending = 1,

    Approved = 2,
    Rejected = 3,

    /// <summary>Nobody answered before it lapsed.</summary>
    Expired = 4,
}

/// <summary>
/// A person's decision about one exact proposed action.
/// </summary>
/// <remarks>
/// <para>
/// The approval binds to a fingerprint rather than to a tool request row, and the
/// distinction matters. If a later model turn rewrites the arguments, the request
/// row is the same row but the fingerprint is different, and the approval no
/// longer authorizes anything (§13, §14).
/// </para>
/// <para>
/// There is no "approve everything from now on". A standing approval is a
/// permission grant dressed as a click, and M12 does not offer one (§13).
/// </para>
/// <para>
/// Approval means <em>a person allowed AgencyOS to attempt this</em>. It does not
/// promise the attempt will succeed: the canonical command still applies its own
/// concurrency and domain rules, and may refuse (§14).
/// </para>
/// </remarks>
public sealed class AiApproval
{
    private AiApproval()
    {
    }

    public AiApprovalId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AgentRunId AgentRunId { get; private set; }

    public AiToolRequestId ToolRequestId { get; private set; }

    /// <summary>
    /// The exact action this authorizes.
    /// </summary>
    /// <remarks>
    /// Copied from the request at the moment the question was asked. Execution
    /// re-computes the fingerprint from what it is about to run and compares; a
    /// mismatch means the arguments moved and the approval is void.
    /// </remarks>
    public string Fingerprint { get; private set; } = string.Empty;

    /// <summary>Who was asked.</summary>
    /// <remarks>
    /// The person who started the run. An approval is not a request to whoever
    /// happens to be looking: it is put to the user whose authority the run
    /// borrows, and executing it uses that same user's permissions, re-checked.
    /// </remarks>
    public UserId RequestedOf { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>
    /// When the question stops being answerable.
    /// </summary>
    /// <remarks>
    /// A decision made about a state of the world hours ago is a decision about a
    /// different state of the world. Expiry is short by default and is checked at
    /// execution as well as by the sweeper, because a row can lapse while somebody
    /// is looking at it.
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; private set; }

    public ApprovalDecision Decision { get; private set; }

    public UserId? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Why, when somebody refused and said so.</summary>
    public string? Reason { get; private set; }

    public int Version { get; private set; }

    public static AiApproval Request(
        OrganizationId organizationId,
        AgentRunId runId,
        AiToolRequestId toolRequestId,
        string fingerprint,
        UserId requestedOf,
        DateTimeOffset now,
        TimeSpan validFor)
    {
        if (validFor <= TimeSpan.Zero)
        {
            throw new DomainException("An approval that is already expired asks nothing.");
        }

        return new AiApproval
        {
            Id = AiApprovalId.New(),
            OrganizationId = organizationId,
            AgentRunId = runId,
            ToolRequestId = toolRequestId,
            Fingerprint = Ensure.NotBlankMax(fingerprint, nameof(fingerprint), 64),
            RequestedOf = requestedOf,
            RequestedAt = now,
            ExpiresAt = now + validFor,
            Decision = ApprovalDecision.Pending,
            Version = 1,
        };
    }

    /// <summary>Whether this authorizes the given action, right now.</summary>
    /// <remarks>
    /// Every condition is re-read at the moment of use. An approval that was valid
    /// when the screen rendered may not be valid when the button is pressed.
    /// </remarks>
    public bool Authorizes(string fingerprint, DateTimeOffset now) =>
        Decision == ApprovalDecision.Approved
        && string.Equals(Fingerprint, fingerprint, StringComparison.Ordinal)
        && now <= ExpiresAt;

    public void Approve(UserId approver, DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);
        RequirePending(now);

        Decision = ApprovalDecision.Approved;
        DecidedBy = approver;
        DecidedAt = now;
        Version++;
    }

    public void Reject(UserId approver, string? reason, DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);
        RequirePending(now);

        Decision = ApprovalDecision.Rejected;
        DecidedBy = approver;
        DecidedAt = now;
        Reason = Ensure.OptionalMax(reason, nameof(reason), 1000);
        Version++;
    }

    /// <summary>Records that the question lapsed unanswered.</summary>
    public void Expire(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);

        if (Decision != ApprovalDecision.Pending)
        {
            throw new DomainException("That approval has already been decided.");
        }

        Decision = ApprovalDecision.Expired;
        DecidedAt = now;
        Version++;
    }

    /// <summary>
    /// Refuses a second decision, and refuses one made too late.
    /// </summary>
    /// <remarks>
    /// The first half is what makes a duplicated click harmless: the second attempt
    /// finds a decided approval and stops. The concurrency token makes it safe under
    /// two simultaneous clicks rather than two sequential ones (§14, §47).
    /// </remarks>
    private void RequirePending(DateTimeOffset now)
    {
        if (Decision != ApprovalDecision.Pending)
        {
            throw new DomainException(
                $"That approval was already {Decision.ToString().ToLowerInvariant()}.");
        }

        if (now > ExpiresAt)
        {
            throw new DomainException(
                "That approval has expired. The agent proposed it against a state "
                    + "of the world that may have moved; start again if it still "
                    + "makes sense.");
        }
    }

    private void Guard(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(AiApproval), Id.ToString(), expectedVersion, Version);
        }
    }
}
