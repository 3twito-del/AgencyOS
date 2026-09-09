using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Ai;

/// <summary>Opaque, immutable identifier for an <see cref="AgentRun"/>.</summary>
public readonly record struct AgentRunId(Guid Value)
{
    public static AgentRunId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Which bounded task a run performs.
/// </summary>
/// <remarks>
/// A closed set, registered in code. There is deliberately no "general assistant"
/// member: an agent whose task is unbounded has no way to say what context it
/// needs, what tools it may use or when it is finished, and every one of those is
/// a security decision here rather than a product preference (§63, ADR-0031).
/// </remarks>
public enum AgentKind
{
    /// <summary>Reads a research case and proposes what it found (§25).</summary>
    ResearchCopilot = 1,

    /// <summary>Prepares somebody for a conversation with a person or company (§26).</summary>
    RelationshipBrief = 2,

    /// <summary>Summarizes where a negotiation stands (§27).</summary>
    DealBrief = 3,

    /// <summary>Summarizes an instrument and its open items (§27).</summary>
    ContractBrief = 4,

    /// <summary>Summarizes what is owed and what has arrived (§28).</summary>
    FinanceBrief = 5,

    /// <summary>Drafts a message. It never sends one (§29).</summary>
    CommunicationDraft = 6,
}

/// <summary>
/// Where a run stands.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AwaitingApproval"/> is a real resting state, not a transient one. A
/// run can sit there for days while somebody decides, which is why it lives in
/// PostgreSQL rather than in memory: the process that started the run will not be
/// the process that finishes it (ADR-0031).
/// </para>
/// <para>
/// The four terminal states are distinguished because they need different answers
/// from a person. Completed needs reading, Failed needs investigating, Cancelled
/// needs nothing, and Expired means nobody decided in time.
/// </para>
/// </remarks>
public enum AgentRunStatus
{
    Queued = 1,
    Running = 2,
    AwaitingApproval = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,

    /// <summary>Nobody answered an approval request before it lapsed.</summary>
    Expired = 7,

    /// <summary>
    /// Waiting for the user's own workstation to run the model.
    /// </summary>
    /// <remarks>
    /// A real state, not an absence of one. Device-local inference happens in a
    /// process AgencyOS does not control, so the run has to survive the client
    /// closing, crashing or simply never coming back — and a run that is waiting
    /// must be distinguishable from one that has hung (ADR-0035).
    /// </remarks>
    AwaitingLocalExecution = 8,
}

/// <summary>
/// Why a run stopped, when it stopped badly.
/// </summary>
/// <remarks>
/// Categorized rather than left as prose because the categories need different
/// handling and different words on screen. "The provider was unreachable" and "the
/// policy refused to transmit this" are the same shape of failure to a log and
/// completely different to the person who asked (§68).
/// </remarks>
public enum AgentFailureKind
{
    None = 0,

    /// <summary>The provider could not be reached at all.</summary>
    ProviderUnavailable = 1,

    /// <summary>The provider took longer than the run was allowed.</summary>
    ProviderTimeout = 2,

    /// <summary>The provider refused for rate reasons.</summary>
    ProviderRateLimited = 3,

    /// <summary>The provider answered with something this build cannot read.</summary>
    ProviderInvalidResponse = 4,

    /// <summary>The data policy refused to transmit the context (§5).</summary>
    PolicyRefused = 5,

    /// <summary>
    /// The workstation has no device-local model available.
    /// </summary>
    /// <remarks>
    /// Terminal, and deliberately so. A local-only run that cannot run locally
    /// fails; it does not quietly become a cloud run, because that would move the
    /// material somewhere the policy did not permit (§E, ADR-0035).
    /// </remarks>
    LocalProviderUnavailable = 12,

    /// <summary>The device-local model exists but is not provisioned yet.</summary>
    LocalModelNotReady = 13,

    /// <summary>The workstation never came back with a result.</summary>
    LocalExecutionAbandoned = 14,

    /// <summary>The caller lacked a permission the run needed.</summary>
    AuthorizationRefused = 6,

    /// <summary>The assembled context exceeded what the model accepts.</summary>
    ContextTooLarge = 7,

    /// <summary>A tool the model requested failed on its own terms.</summary>
    ToolFailed = 8,

    /// <summary>A canonical command refused: concurrency, or a domain rule.</summary>
    CommandRefused = 9,

    /// <summary>The run reached a limit: turns, tool calls or wall clock (§17).</summary>
    LimitReached = 10,

    /// <summary>Something this build did not anticipate.</summary>
    Unexpected = 99,
}

/// <summary>
/// One bounded AI task, from the moment somebody asked for it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is execution history, never business truth.</strong> A completed
/// run proves that a model was asked something and answered; it proves nothing
/// about the agency. Every business fact the run produced exists because a person
/// approved a canonical command, and that fact lives in its own aggregate with its
/// own audit entry naming the person rather than the model (§16, §23).
/// </para>
/// <para>
/// The run records which model answered and which prompt version asked, because a
/// brief nobody can reproduce is a brief nobody can check. It does not record the
/// prompt body: that would duplicate protected M10 and M11 content into a store
/// with weaker rules, which is exactly the leak §19 exists to prevent.
/// </para>
/// </remarks>
public sealed class AgentRun
{
    private readonly List<AgentRunStep> _steps = [];

    private AgentRun()
    {
    }

    public AgentRunId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>
    /// Who asked. Every authorization decision in the run is made as this user.
    /// </summary>
    /// <remarks>
    /// Not "the agent" and not a service account. A run has exactly the reach of
    /// the person who started it, re-checked at each step rather than captured once
    /// — somebody whose access is revoked mid-run stops being able to reach the
    /// data through it (§9, §47).
    /// </remarks>
    public UserId UserId { get; private set; }

    public AgentKind Kind { get; private set; }

    public AgentRunStatus Status { get; private set; }

    /// <summary>What the person asked for, in their words.</summary>
    public string Task { get; private set; } = string.Empty;

    /// <summary>
    /// The canonical object the run is about, when there is one.
    /// </summary>
    /// <remarks>
    /// A research brief is about a research case; a relationship brief is about a
    /// person. Held as a typed arc rather than a loose identifier so the database
    /// can refuse a subject in another tenant (ADR-0011).
    /// </remarks>
    public AgentSubjectKind SubjectKind { get; private set; }

    public Guid? SubjectId { get; private set; }

    /// <summary>Which provider answered, as configured rather than as advertised.</summary>
    public string ProviderKey { get; private set; } = string.Empty;

    /// <summary>The model identifier this run was configured to use.</summary>
    public string ModelKey { get; private set; } = string.Empty;

    /// <summary>
    /// How sensitive the material this result was produced from was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded at generation time and never recomputed. A result is a derived
    /// work of everything the run was given, so it inherits the strongest
    /// classification among its inputs: a paragraph drawn from a source-sensitive
    /// signal is source-sensitive even though nothing in it looks like one
    /// (ADR-0035, §L).
    /// </para>
    /// <para>
    /// This is what makes a stored result re-authorizable. Without it a run would
    /// carry prose whose provenance nobody could reconstruct, and the only
    /// available check would be "did this person start the run" - which is
    /// historical authorization, and does not expire.
    /// </para>
    /// </remarks>
    public ModelDataSensitivity ResultSensitivity { get; private set; }

    /// <summary>Where this run's inference executes.</summary>
    /// <remarks>
    /// Recorded on the run so a reader can tell afterwards where the material
    /// went, which is the question residency exists to answer. Set at start and
    /// never changed: a run cannot migrate between residencies, because doing so
    /// would move data under a policy decision already made (§E).
    /// </remarks>
    public ModelResidency Residency { get; private set; }

    /// <summary>
    /// Which device actually ran it, when the client reported one.
    /// </summary>
    /// <remarks>
    /// Free text from the workstation and treated as provenance rather than as
    /// fact: the client says what its provider told it, and nothing depends on the
    /// value. Null for server-side runs.
    /// </remarks>
    public string? ExecutionDevice { get; private set; }

    /// <summary>
    /// Which version of the agent's prompt asked the question.
    /// </summary>
    /// <remarks>
    /// A run whose prompt version is unknown cannot be reproduced or explained, and
    /// a prompt is product behaviour rather than a detail (§20).
    /// </remarks>
    public string PromptTemplateId { get; private set; } = string.Empty;

    public int PromptTemplateVersion { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public AgentFailureKind Failure { get; private set; }

    /// <summary>What went wrong, in words a person can act on. Never a stack trace.</summary>
    public string? FailureDetail { get; private set; }

    /// <summary>
    /// The user-visible answer, once there is one.
    /// </summary>
    /// <remarks>
    /// The result is kept and the prompt is not, which is the retention decision
    /// §19 asks for stated as a shape: what the person was shown is what they may
    /// need to revisit, and the material it was drawn from already lives under its
    /// own classification in M10 and M11.
    /// </remarks>
    public string? Result { get; private set; }

    /// <summary>How many times a provider was called, across the whole run.</summary>
    public int ModelInvocationCount { get; private set; }

    public int ToolCallCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation (ADR-0014).</summary>
    public int Version { get; private set; }

    /// <summary>Every step, in order. Append-only.</summary>
    public IReadOnlyList<AgentRunStep> Steps => _steps;

    public bool IsTerminal => Status
        is AgentRunStatus.Completed
        or AgentRunStatus.Failed
        or AgentRunStatus.Cancelled
        or AgentRunStatus.Expired;

    public static AgentRun Start(
        OrganizationId organizationId,
        UserId userId,
        AgentKind kind,
        string task,
        string providerKey,
        string modelKey,
        string promptTemplateId,
        int promptTemplateVersion,
        DateTimeOffset now,
        AgentSubjectKind subjectKind = AgentSubjectKind.None,
        Guid? subjectId = null,
        ModelResidency residency = ModelResidency.ExternalCloud)
    {
        if (subjectKind is AgentSubjectKind.None != (subjectId is null))
        {
            throw new DomainException(
                "A run either names a subject and says what kind it is, or names "
                    + "neither. Half of an arc points at nothing.");
        }

        return new AgentRun
        {
            Id = AgentRunId.New(),
            OrganizationId = organizationId,
            UserId = userId,
            Kind = kind,
            Status = AgentRunStatus.Queued,
            Task = Ensure.NotBlankMax(task, nameof(task), 2000),
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            ProviderKey = Ensure.NotBlankMax(providerKey, nameof(providerKey), 100),
            ModelKey = Ensure.NotBlankMax(modelKey, nameof(modelKey), 200),
            PromptTemplateId =
                Ensure.NotBlankMax(promptTemplateId, nameof(promptTemplateId), 100),
            PromptTemplateVersion = promptTemplateVersion >= 1
                ? promptTemplateVersion
                : throw new DomainException("A prompt version starts at one."),
            Residency = residency,
            StartedAt = now,
            CreatedAt = now,
            Failure = AgentFailureKind.None,
            Version = 1,
        };
    }

    /// <summary>Records that the run has begun doing work.</summary>
    public void Begin(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);
        Require(
            AgentRunStatus.Queued,
            AgentRunStatus.AwaitingApproval,
            AgentRunStatus.AwaitingLocalExecution);

        Status = AgentRunStatus.Running;
        Touch(now);
    }

    /// <summary>
    /// Records that the run is waiting for a person.
    /// </summary>
    /// <remarks>
    /// The state exists because the alternative — holding a request in memory until
    /// somebody clicks — loses the request on deploy, and because a run that is
    /// waiting looks identical to one that has hung unless it says so (§13).
    /// </remarks>
    public void AwaitApproval(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);
        Require(AgentRunStatus.Running);

        Status = AgentRunStatus.AwaitingApproval;
        Touch(now);
    }

    /// <summary>
    /// Records that the run is waiting for the user's workstation.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="AwaitApproval"/>, and for the same reason: the
    /// thing being waited for happens outside this process, so holding the run in
    /// memory would lose it on deploy and make a waiting run indistinguishable
    /// from a hung one (ADR-0035).
    /// </remarks>
    public void AwaitLocalExecution(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);

        // Re-enterable. A run enters this state when it starts and again whenever
        // a lease is issued, because a workstation may legitimately take a second
        // lease after the first lapsed or was refused. "Waiting for the device" is
        // a state you can arrive at more than once.
        Require(
            AgentRunStatus.Queued,
            AgentRunStatus.Running,
            AgentRunStatus.AwaitingLocalExecution);

        Status = AgentRunStatus.AwaitingLocalExecution;
        Touch(now);
    }

    /// <summary>Records which device the workstation said it used.</summary>
    /// <remarks>
    /// Provenance, not fact. The client reports what its provider told it, and
    /// nothing in AgencyOS depends on the value being true.
    /// </remarks>
    public void RecordExecutionDevice(string? device, DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);

        ExecutionDevice = device is { Length: > 0 }
            ? Ensure.NotBlankMax(device, nameof(device), 100)
            : null;

        Touch(now);
    }

    /// <param name="sensitivity">
    /// The strongest classification among the inputs this result was produced
    /// from. Stored so later reads can be authorized against it.
    /// </param>
    public void Complete(
        string result,
        ModelDataSensitivity sensitivity,
        DateTimeOffset now,
        int expectedVersion)
    {
        Guard(expectedVersion);

        // AwaitingLocalExecution completes for the same reason AwaitingApproval
        // does: the run was waiting on something outside this process, and that
        // something answered. The answer is validated before it reaches here.
        Require(
            AgentRunStatus.Running,
            AgentRunStatus.AwaitingApproval,
            AgentRunStatus.AwaitingLocalExecution);

        Status = AgentRunStatus.Completed;
        Result = Ensure.NotBlankMax(result, nameof(result), 60_000);
        ResultSensitivity = sensitivity;
        CompletedAt = now;
        Touch(now);
    }

    public void Fail(
        AgentFailureKind failure,
        string? detail,
        DateTimeOffset now,
        int expectedVersion)
    {
        Guard(expectedVersion);

        if (IsTerminal)
        {
            throw new DomainException("That run has already finished.");
        }

        if (failure == AgentFailureKind.None)
        {
            throw new DomainException("A failure says what kind of failure it was.");
        }

        Status = AgentRunStatus.Failed;
        Failure = failure;
        FailureDetail = Ensure.OptionalMax(detail, nameof(detail), 2000);
        CompletedAt = now;
        Touch(now);
    }

    /// <summary>
    /// Stops the run at the next opportunity.
    /// </summary>
    /// <remarks>
    /// Cancellation stops <em>future</em> steps. It does not undo a canonical write
    /// that already committed, and this method deliberately offers no way to try:
    /// a task somebody approved and AgencyOS created is a real task, and pretending
    /// a distributed rollback occurred would be a lie told to make a state machine
    /// tidier (§45).
    /// </remarks>
    public void Cancel(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);

        if (IsTerminal)
        {
            throw new DomainException("That run has already finished.");
        }

        Status = AgentRunStatus.Cancelled;
        CompletedAt = now;
        Touch(now);
    }

    /// <summary>Records that nobody answered an approval request in time.</summary>
    public void Expire(DateTimeOffset now, int expectedVersion)
    {
        Guard(expectedVersion);
        Require(AgentRunStatus.AwaitingApproval);

        Status = AgentRunStatus.Expired;
        CompletedAt = now;
        Touch(now);
    }

    /// <summary>
    /// Appends a step. Steps are never rewritten.
    /// </summary>
    /// <remarks>
    /// The model proposes what happens next; it does not get to revise what already
    /// happened. An execution history the model could edit would be worth nothing
    /// as an explanation of what the model did (§17).
    /// </remarks>
    public AgentRunStep AppendStep(
        AgentStepKind kind,
        string summary,
        DateTimeOffset now,
        string? detail = null,
        Guid? referenceId = null)
    {
        if (IsTerminal)
        {
            throw new DomainException(
                "That run has finished. Its history is what happened, and nothing "
                    + "further happened.");
        }

        AgentRunStep step = AgentRunStep.Record(
            OrganizationId, Id, _steps.Count + 1, kind, summary, now, detail, referenceId);

        _steps.Add(step);

        if (kind == AgentStepKind.ModelInvocation)
        {
            ModelInvocationCount++;
        }
        else if (kind == AgentStepKind.ToolCall)
        {
            ToolCallCount++;
        }

        return step;
    }

    private void Require(params AgentRunStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(
                $"A run that is {Status} cannot do that. Expected one of: "
                    + string.Join(", ", allowed) + ".");
        }
    }

    private void Guard(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(AgentRun), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        _ = now;
        Version++;
    }
}

/// <summary>
/// What kind of record a run is about.
/// </summary>
/// <remarks>
/// Deliberately narrower than M11's ten subject kinds. A run is about the one
/// thing it was started from, and each member here corresponds to an agent that
/// exists (§16).
/// </remarks>
public enum AgentSubjectKind
{
    None = 0,
    ResearchCase = 1,
    Person = 2,
    Company = 3,
    Deal = 4,
    Contract = 5,
}
