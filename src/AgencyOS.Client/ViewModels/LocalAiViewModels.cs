using AgencyOS.Contracts.Ai;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// How residency and local capability are worded on screen.
/// </summary>
/// <remarks>
/// <para>
/// The wording is load-bearing here in a way it rarely is. A screen that implied
/// "private mode" while the model ran in somebody else's datacentre would be
/// making a claim about where an agency's material went, and a person deciding
/// what to ask about would reasonably act on it (§42, ADR-0035).
/// </para>
/// <para>
/// So the vocabulary is deliberately literal. It says where the computation
/// happens, and it never says "private", "secure" or "safe" — residency is not a
/// privacy guarantee, it is a fact about a machine.
/// </para>
/// </remarks>
public static class LocalAiFormatting
{
    /// <summary>Where inference runs, in words that claim only that.</summary>
    public static string Residency(string? residency) => residency switch
    {
        "DeviceLocal" => "Runs on this device",
        "OrganizationControlled" => "Runs on your organization's own infrastructure",
        "ExternalCloud" => "Runs at an external model provider",
        _ => "Where this runs is not known",
    };

    /// <summary>
    /// A short badge for a run row.
    /// </summary>
    /// <remarks>
    /// Kept apart from the sentence above because a badge beside a list row and an
    /// explanation on a detail page are read differently, and shortening the
    /// sentence would drop the part that carries the meaning.
    /// </remarks>
    public static string ResidencyBadge(string? residency) => residency switch
    {
        "DeviceLocal" => "This device",
        "OrganizationControlled" => "Your infrastructure",
        "ExternalCloud" => "External provider",
        _ => "Unknown",
    };

    /// <summary>
    /// What the workstation can do, said plainly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The unavailable case is the normal one.</strong> Windows AI text
    /// generation needs a Copilot+ PC with a neural processor, which most machines
    /// — including every one AgencyOS has been developed on — do not have. Saying
    /// so factually beats an error that invites somebody to go looking for a fix
    /// that does not exist (§F).
    /// </para>
    /// <para>
    /// None of these offers to install anything. Downloading a model because a
    /// screen was opened is not a decision a UI makes on somebody's behalf (§31).
    /// </para>
    /// </remarks>
    public static string Readiness(string? readiness) => readiness switch
    {
        "Ready" => "A local model is ready on this device.",
        "NotReady" =>
            "This device supports a local model, and it is not installed yet. "
                + "AgencyOS will not download one for you.",
        "NotSupported" =>
            "This device has no local model. Windows AI text generation needs a "
                + "Copilot+ PC with a neural processor.",
        _ => "Whether this device can run a local model could not be determined.",
    };

    /// <summary>Whether a device-local run can be offered at all.</summary>
    public static bool CanRunLocally(string? readiness) => readiness == "Ready";

    /// <summary>
    /// Why a device-local run did not happen.
    /// </summary>
    /// <remarks>
    /// Each says what it says and stops. In particular none of them suggests
    /// trying the cloud instead: crossing a residency boundary is a policy
    /// decision, not a retry, and offering it as one here would teach people it
    /// is the same thing (§E).
    /// </remarks>
    public static string LocalFailure(string? failure) => failure switch
    {
        "LocalProviderUnavailable" =>
            "This device cannot run the model, so nothing ran. Nothing was sent "
                + "anywhere else.",
        "LocalModelNotReady" =>
            "The local model is not installed on this device, so nothing ran. "
                + "Nothing was sent anywhere else.",
        "ProviderTimeout" =>
            "The local model took too long. Nothing was sent anywhere else.",
        "Cancelled" => "You stopped it. Nothing was changed.",
        _ => "The local model did not run. Nothing was sent anywhere else.",
    };

    /// <summary>
    /// Why the server declined a returned result.
    /// </summary>
    /// <remarks>
    /// Distinct from the failures above: those are this machine's limitations,
    /// these are a lease that stopped being valid while the model ran. A person
    /// can act on the difference — one calls for a different machine, the other
    /// for asking again.
    /// </remarks>
    public static string Refusal(string? refusal) => refusal switch
    {
        "Expired" =>
            "The permission to run this expired before the answer came back. Ask "
                + "again to start over.",
        "Consumed" => "That answer was already recorded.",
        "Invalidated" => "This run was cancelled while the model was working.",
        "ContextMismatch" =>
            "The records changed while the model was working, so the answer was "
                + "about something that has since moved. Ask again.",
        "RunNotWaiting" => "This run is no longer waiting for an answer.",
        "PermissionRevoked" =>
            "You no longer have access to some of what this was based on.",
        "EmptyResult" => "The local model returned nothing.",
        "NotFound" => "There is nothing here to answer.",
        _ => "The answer was not accepted.",
    };
}

/// <summary>
/// What this workstation and this server can execute.
/// </summary>
/// <remarks>
/// Two halves that never merge. The server says which models exist and where each
/// runs; the workstation says whether it can run the device-local one. A client
/// told by the server what its own hardware can do would be trusting the wrong
/// party with the one question only it can answer (§G).
/// </remarks>
public sealed class AiExecutionCapabilityViewModel : ViewModelBase
{
    private readonly ILocalInferenceApi _api;

    private bool _loaded;
    private string _readiness = "Unknown";
    private string _readinessDetail = string.Empty;
    private IReadOnlyList<string> _devices = [];

    public AiExecutionCapabilityViewModel(ILocalInferenceApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <summary>What the server can execute, and where.</summary>
    public System.Collections.ObjectModel.ObservableCollection<AiExecutionTargetResponse>
        Targets { get; } = [];

    /// <summary>What this device reported. Never asked of the server.</summary>
    public string Readiness
    {
        get => _readiness;
        private set
        {
            if (Set(ref _readiness, value))
            {
                OnPropertyChanged(nameof(ReadinessText));
                OnPropertyChanged(nameof(CanRunLocally));
            }
        }
    }

    /// <summary>The probe's own explanation, for the diagnostics pane.</summary>
    public string ReadinessDetail
    {
        get => _readinessDetail;
        private set => Set(ref _readinessDetail, value);
    }

    /// <summary>Execution devices this machine reported as ready.</summary>
    public IReadOnlyList<string> Devices
    {
        get => _devices;
        private set => Set(ref _devices, value);
    }

    public string ReadinessText => LocalAiFormatting.Readiness(_readiness);

    public bool CanRunLocally => LocalAiFormatting.CanRunLocally(_readiness);

    /// <summary>Whether this build could offer a device-local run at all.</summary>
    /// <remarks>
    /// Both halves have to agree: the server must know a device-local model, and
    /// this machine must be able to run it. Either missing means the option is not
    /// offered rather than offered and then refused.
    /// </remarks>
    public bool LocalRunIsAvailable =>
        CanRunLocally && Targets.Any(x => x.Residency == "DeviceLocal");

    public override bool IsEmpty => _loaded && Targets.Count == 0;

    /// <summary>Reads both halves.</summary>
    /// <param name="readiness">What the workstation's own probe reported.</param>
    /// <param name="detail">The probe's explanation.</param>
    /// <param name="devices">Devices the probe found ready.</param>
    public Task LoadAsync(
        string readiness,
        string detail,
        IReadOnlyList<string> devices,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                Readiness = readiness;
                ReadinessDetail = detail;
                Devices = devices;

                IReadOnlyList<AiExecutionTargetResponse> targets = await _api
                    .ListAiExecutionTargetsAsync(token)
                    .ConfigureAwait(true);

                Replace(Targets, targets);
                _loaded = true;

                OnPropertyChanged(nameof(LocalRunIsAvailable));
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);
}
