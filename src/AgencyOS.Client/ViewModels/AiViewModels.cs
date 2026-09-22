using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Contracts.Ai;
using AgencyOS.Client.Presentation;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// How the AI runtime is worded on screen.
/// </summary>
/// <remarks>
/// <para>
/// The wording is load-bearing, for the same reason M11's is. A screen that says
/// "AgencyOS created a task" where the truth is "a model proposed one and Ariel
/// approved it" would put an act on the record that nobody performed. Everything
/// here is phrased so that the model proposes and a person decides (§1, §64).
/// </para>
/// <para>
/// Nothing here presents model output as a finding. A result is what the model
/// said, and it is labelled as such wherever it appears.
/// </para>
/// </remarks>
public static class AiFormatting
{
    /// <summary>What a run is doing, in words a user can act on.</summary>
    public static string Status(string? status) => status switch
    {
        "Queued" => "Queued",
        "Running" => "Working",
        "AwaitingApproval" => "Waiting for your decision",
        "Completed" => "Finished",
        "Failed" => "Failed",
        "Cancelled" => "Cancelled",
        "Expired" => "Timed out",
        _ => "Unknown",
    };

    /// <summary>Whether a run is still going and worth polling.</summary>
    public static bool IsActive(string? status) =>
        status is "Queued" or "Running" or "AwaitingApproval";

    /// <summary>
    /// What went wrong, in terms of what a user can do about it.
    /// </summary>
    /// <remarks>
    /// Categorized rather than free text, because "it failed" tells somebody
    /// nothing and a raw provider error tells them something they cannot act on.
    /// Whether to retry, ask an administrator or narrow the question are different
    /// answers, and the failure kind is what distinguishes them (§68).
    /// </remarks>
    public static string Failure(string? failure) => failure switch
    {
        "None" => string.Empty,
        "ProviderUnavailable" => "The model provider could not be reached. Nothing was changed.",
        "ProviderTimeout" => "The model took too long to answer. Nothing was changed.",
        "ProviderRateLimited" => "The model provider is rate limiting this build. Try again shortly.",
        "ProviderInvalidResponse" => "The model returned something AgencyOS could not read. Nothing was changed.",
        "PolicyRefused" => "This organization does not permit sending this material to a model provider.",
        "AuthorizationRefused" => "You are not permitted to read something this task needs.",
        "ContextTooLarge" => "There was too much material for one run. Narrow the question.",
        "ToolFailed" => "A step failed while gathering material. Nothing was changed.",
        "CommandRefused" => "AgencyOS refused the action that was approved. Nothing was changed.",
        "LimitReached" => "The run hit its limit before finishing. Nothing was changed.",
        _ => "The run failed. Nothing was changed.",
    };

    /// <summary>
    /// What a step was, distinguishing the model's part from AgencyOS's.
    /// </summary>
    /// <remarks>
    /// The whole reason the trace exists. "The model asked for" and "AgencyOS ran"
    /// are different sentences and a reader must never have to guess which one they
    /// are looking at (§64).
    /// </remarks>
    public static string Step(string? kind) => kind switch
    {
        "ContextAssembled" => "AgencyOS gathered material",
        "ModelInvocation" => "The model was asked",
        "ToolCall" => "The model asked for",
        "ToolResult" => "AgencyOS answered",
        "ApprovalRequested" => "AgencyOS asked you to decide",
        "ApprovalResolved" => "You decided",
        "CommandExecuted" => "AgencyOS ran",
        "ProposalProduced" => "The model proposed",
        "Refused" => "AgencyOS refused",
        _ => "Step",
    };

    /// <summary>What a tool would do, in the terms an approval turns on.</summary>
    public static string Effect(string? effect) => effect switch
    {
        "ReadOnly" => "Reads only",
        "CanonicalWrite" => "Changes AgencyOS",
        "ExternalEffect" => "Leaves AgencyOS",
        _ => "Unknown",
    };

    /// <summary>Whether an effect is one a person has to decide about.</summary>
    public static bool NeedsDecision(string? effect) => effect is not "ReadOnly";

    /// <summary>
    /// How long an approval has left.
    /// </summary>
    /// <remarks>
    /// Shown because an approval is deliberately short-lived. A person deciding
    /// against a screen that no longer reflects the world is the case the window
    /// exists to bound, and a countdown says so without needing explanation (§13).
    /// </remarks>
    public static string Remaining(AiApprovalResponse approval, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approval);

        if (approval.Decision != "Pending")
        {
            return string.Empty;
        }

        TimeSpan left = approval.ExpiresAt - now;

        if (left <= TimeSpan.Zero)
        {
            return "Expired";
        }

        return left.TotalMinutes < 1
            ? "Expires in under a minute"
            : string.Create(
                CultureInfo.CurrentCulture,
                $"Expires in {(int)left.TotalMinutes} minutes");
    }

    /// <summary>
    /// What the sensitivity ceiling permits, spelled out.
    /// </summary>
    /// <remarks>
    /// Named in terms of what would be transmitted rather than by the label alone,
    /// because a person setting this is deciding what leaves the building and
    /// "Protected" on its own does not tell them what that includes (§42).
    /// </remarks>
    public static string Ceiling(string? sensitivity) => sensitivity switch
    {
        "Internal" => "Ordinary internal working material only",
        "Confidential" => "Internal material and commercial terms",
        "Protected" => "Including confidential sources and privileged material",
        "Restricted" => "Not available. Restricted material is never transmitted.",
        _ => "Unknown",
    };

    /// <summary>The ceilings a person may actually choose.</summary>
    /// <remarks>
    /// Restricted is absent because no ceiling reaches it. Offering it as a
    /// disabled option would suggest a permission exists to be granted; it does
    /// not, at any level (§5, §43).
    /// </remarks>
    public static IReadOnlyList<string> Ceilings { get; } =
    [
        "Internal",
        "Confidential",
        "Protected",
    ];
}

/// <summary>
/// Starting a task, and watching the run it produces.
/// </summary>
/// <remarks>
/// <para>
/// A run is started and awaited in one call, and what comes back is an identifier
/// rather than an answer. The screen then reads the run: that way a person who
/// closes the window still has a run they can find, and a run that ends in an
/// approval request is on the same footing as one that ends in a result (§63).
/// </para>
/// <para>
/// <see cref="Result"/> is the model's output and the screen labels it as such.
/// It is never merged into an AgencyOS record by this view model, and there is no
/// method here that would (§66).
/// </para>
/// </remarks>
public sealed class AgentRunViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string _kind = "ResearchCopilot";
    private string _task = string.Empty;
    private string _subjectKind = "None";
    private Guid? _subjectId;
    private string? _modelKey;
    private AgentRunDetailResponse? _run;

    public AgentRunViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <summary>
    /// The agents this build offers.
    /// </summary>
    /// <remarks>
    /// A closed list. There is no general assistant, because an agent whose task is
    /// unbounded cannot say what context it needs, what tools it should hold or
    /// when it is finished (§8).
    /// </remarks>
    public static IReadOnlyList<string> Kinds { get; } =
    [
        "ResearchCopilot",
        "RelationshipBrief",
        "DealBrief",
        "ContractBrief",
        "FinanceBrief",
        "CommunicationDraft",
    ];

    public static IReadOnlyList<string> SubjectKinds { get; } =
    [
        "None",
        "ResearchCase",
        "Person",
        "Company",
        "Deal",
        "Contract",
    ];

    public ObservableCollection<AgentStepResponse> Steps { get; } = [];

    public ObservableCollection<AiToolRequestResponse> ToolRequests { get; } = [];

    public string Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    /// <summary>What the user is asking for, in their own words.</summary>
    public string Task
    {
        get => _task;
        set => Set(ref _task, value);
    }

    public string SubjectKind
    {
        get => _subjectKind;
        set => Set(ref _subjectKind, value);
    }

    public Guid? SubjectId
    {
        get => _subjectId;
        set => Set(ref _subjectId, value);
    }

    /// <summary>Which model, or null for the configured default.</summary>
    public string? ModelKey
    {
        get => _modelKey;
        set => Set(ref _modelKey, value);
    }

    public AgentRunDetailResponse? Run
    {
        get => _run;
        private set
        {
            if (Set(ref _run, value))
            {
                OnPropertyChanged(nameof(Result));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(FailureText));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsAwaitingApproval));
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    /// <summary>What the model said. Not a finding, and labelled as such.</summary>
    public string? Result => _run?.Result;

    public bool HasResult => !string.IsNullOrWhiteSpace(_run?.Result);

    public string StatusText => AiFormatting.Status(_run?.Run.Status);

    public string FailureText =>
        _run is null ? string.Empty : AiFormatting.Failure(_run.Run.Failure);

    public bool IsActive => AiFormatting.IsActive(_run?.Run.Status);

    public bool IsAwaitingApproval => _run?.Run.Status == "AwaitingApproval";

    public bool CanStart => !string.IsNullOrWhiteSpace(_task) && !IsLoading;

    public override bool IsEmpty => _loaded && _run is null;

    /// <summary>Starts a run and reads it back.</summary>
    /// <remarks>
    /// The idempotency key is generated per attempt, so a lost response on a
    /// started run does not start a second one when the user presses the button
    /// again (ADR-0014).
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                AgentRunIdResponse started = await _api
                    .StartAiRunAsync(
                        new StartAgentRunRequest(
                            Kind,
                            Task.Trim(),
                            SubjectKind,
                            SubjectId,
                            ModelKey),
                        Guid.CreateVersion7().ToString(),
                        token)
                    .ConfigureAwait(true);

                await LoadCoreAsync(started.Id, token).ConfigureAwait(true);
            },
            cancellationToken);

    public Task LoadAsync(Guid runId, CancellationToken cancellationToken = default) =>
        RunAsync(token => LoadCoreAsync(runId, token), cancellationToken);

    /// <summary>
    /// Stops the run. Anything already committed stands.
    /// </summary>
    /// <remarks>
    /// The wording matters on the button as much as here. A task that was approved
    /// and created is a real task, and cancelling the run that proposed it does not
    /// unmake it (§45).
    /// </remarks>
    public Task CancelAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                if (_run is null)
                {
                    return;
                }

                await _api
                    .CancelAiRunAsync(
                        _run.Run.Id,
                        new CancelAgentRunRequest(_run.Run.Version),
                        token)
                    .ConfigureAwait(true);

                await LoadCoreAsync(_run.Run.Id, token).ConfigureAwait(true);
            },
            cancellationToken);

    private async Task LoadCoreAsync(Guid runId, CancellationToken cancellationToken)
    {
        AgentRunDetailResponse run = await _api
            .GetAiRunAsync(runId, cancellationToken)
            .ConfigureAwait(true);

        Run = run;
        Replace(Steps, run.Steps);
        Replace(ToolRequests, run.ToolRequests);
        _loaded = true;
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>The caller's own runs.</summary>
/// <remarks>
/// There is no filter that shows anybody else's. A task somebody typed can name a
/// person, a deal or a confidence, and no permission in AgencyOS grants reading
/// another person's questions (§52).
/// </remarks>
public sealed class AgentRunListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _kind;
    private string? _status;

    public AgentRunListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<AgentRunResponse> Runs { get; } = [];

    public static IReadOnlyList<string> Statuses { get; } =
    [
        "Queued",
        "Running",
        "AwaitingApproval",
        "Completed",
        "Failed",
        "Cancelled",
        "Expired",
    ];

    public string? Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <summary>Runs still going, for the active list on the AI page.</summary>
    public IReadOnlyList<AgentRunResponse> Active =>
        [.. Runs.Where(x => AiFormatting.IsActive(x.Status))];

    public override bool IsEmpty => _loaded && Runs.Count == 0;
    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<AgentRunResponse> runs = await _api
                    .ListAiRunsAsync(Kind, Status, startedAfter: null, limit: null, token)
                    .ConfigureAwait(true);

                Replace(Runs, runs);
                _loaded = true;
                OnPropertyChanged(nameof(Active));
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);
}

/// <summary>
/// What is waiting for a person to decide, and deciding it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Approving does not execute.</strong> The decision and the execution are
/// separate calls here because they are separate acts on the server: a decision is
/// a person's, completes on its own and is recorded; execution re-derives the
/// fingerprint, re-checks the permission, and may still be refused (§14).
/// </para>
/// <para>
/// The summary and arguments shown are the server's account of what would run,
/// read from the tool request itself. Never the model's description of its own
/// request — a model that could write its own approval prompt could describe one
/// action and request another (§65).
/// </para>
/// </remarks>
public sealed class AiApprovalViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private AiApprovalResponse? _selected;
    private ExecuteApprovedToolResponse? _lastExecution;

    public AiApprovalViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<AiApprovalResponse> Pending { get; } = [];

    public AiApprovalResponse? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(CanDecide));
            }
        }
    }

    /// <summary>What happened when the approved action ran, if it has.</summary>
    public ExecuteApprovedToolResponse? LastExecution
    {
        get => _lastExecution;
        private set => Set(ref _lastExecution, value);
    }

    /// <summary>
    /// Whether the selected approval can still be decided.
    /// </summary>
    /// <remarks>
    /// False for an expired one. The server refuses it anyway; disabling the button
    /// means a person is not told they approved something and then told they did
    /// not (§13).
    /// </remarks>
    public bool CanDecide =>
        _selected is { Decision: "Pending", HasExpired: false } && !IsLoading;

    public override bool IsEmpty => _loaded && Pending.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<AiApprovalResponse> pending = await _api
                    .ListPendingAiApprovalsAsync(token)
                    .ConfigureAwait(true);

                Replace(Pending, pending);
                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);

    /// <summary>
    /// Records a decision, and runs it when approved.
    /// </summary>
    /// <remarks>
    /// The execution is a second call rather than a server-side consequence of the
    /// first, so a refusal at execution still leaves a recorded decision saying who
    /// allowed what. A rejection stops here and never reaches execution.
    /// </remarks>
    public Task DecideAsync(
        bool approve,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                if (_selected is not { } approval)
                {
                    return;
                }

                LastExecution = null;

                await _api
                    .DecideAiApprovalAsync(
                        approval.Id,
                        new DecideApprovalRequest(approve, approval.Version, reason),
                        token)
                    .ConfigureAwait(true);

                if (approve)
                {
                    LastExecution = await _api
                        .ExecuteApprovedAiToolAsync(
                            approval.ToolRequestId,
                            Guid.CreateVersion7().ToString(),
                            token)
                        .ConfigureAwait(true);
                }

                Selected = null;

                IReadOnlyList<AiApprovalResponse> pending = await _api
                    .ListPendingAiApprovalsAsync(token)
                    .ConfigureAwait(true);

                Replace(Pending, pending);
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);
}

/// <summary>
/// What this organization permits AgencyOS to transmit, per provider.
/// </summary>
/// <remarks>
/// A separate screen from anything about using a model, because it answers a
/// different question. "May Ariel read this" is authorization; "may AgencyOS send
/// it to a provider" is this, and a firm can perfectly reasonably let its analysts
/// read something it will not put in anybody else's datacentre (§5, §42).
/// </remarks>
public sealed class AiProviderPolicyViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;

    public AiProviderPolicyViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<AiProviderPolicyResponse> Policies { get; } = [];

    public ObservableCollection<AiModelDescriptorResponse> Models { get; } = [];

    public ObservableCollection<AiToolDescriptorResponse> Tools { get; } = [];

    public static IReadOnlyList<string> Ceilings => AiFormatting.Ceilings;

    public override bool IsEmpty => _loaded && Policies.Count == 0;

    public Task LoadAsync(
        string agentKind = "ResearchCopilot",
        CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<AiProviderPolicyResponse> policies = await _api
                    .ListAiProviderPoliciesAsync(token)
                    .ConfigureAwait(true);

                IReadOnlyList<AiModelDescriptorResponse> models = await _api
                    .ListAiModelsAsync(token)
                    .ConfigureAwait(true);

                IReadOnlyList<AiToolDescriptorResponse> tools = await _api
                    .ListAiToolsAsync(agentKind, token)
                    .ConfigureAwait(true);

                Replace(Policies, policies);
                Replace(Models, models);
                Replace(Tools, tools);
                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);

    public Task SaveAsync(
        AiProviderPolicyResponse policy,
        bool isEnabled,
        string ceiling,
        bool allowsWriteProposals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return RunAsync(
            async token =>
            {
                await _api
                    .SetAiProviderPolicyAsync(
                        policy.ProviderKey,
                        new UpdateAiProviderPolicyRequest(
                            isEnabled, ceiling, allowsWriteProposals, policy.Version),
                        token)
                    .ConfigureAwait(true);

                IReadOnlyList<AiProviderPolicyResponse> policies = await _api
                    .ListAiProviderPoliciesAsync(token)
                    .ConfigureAwait(true);

                Replace(Policies, policies);
            },
            cancellationToken);
    }
}
