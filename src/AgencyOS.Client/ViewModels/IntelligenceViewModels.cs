using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Contracts.Intelligence;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// How intelligence is worded on screen.
/// </summary>
/// <remarks>
/// <para>
/// The wording is load-bearing. A screen that says "Verified" where the server said
/// <c>Corroborated</c> would assert that a claim is true, which nothing in M11 ever
/// asserts; a screen that renders a probability without the forecaster and the date
/// would present somebody's opinion as the system's finding (§1, §11).
/// </para>
/// <para>
/// Nothing here computes a score, a rank or a grade. The only numbers this file
/// formats are ones the server already produced.
/// </para>
/// </remarks>
public static class IntelligenceFormatting
{
    /// <summary>What a verification state means, in words that do not overclaim.</summary>
    /// <remarks>
    /// "Corroborated" reads as "other evidence agrees", never as "true". There is no
    /// Verified in the domain and there is deliberately no way to render one here.
    /// </remarks>
    public static string Verification(string? verification) => verification switch
    {
        "Corroborated" => "Corroborated by other evidence",
        "Disputed" => "Disputed",
        "Retracted" => "Retracted",
        _ => "Unverified",
    };

    /// <summary>Whether a verification state is one a reader should pause at.</summary>
    public static bool NeedsCare(string? verification) =>
        verification is "Disputed" or "Retracted";

    /// <summary>What a source's reliability rating means.</summary>
    /// <remarks>
    /// "Not assessed" for the ordinary case. A source nobody has judged is not a
    /// weak source and not a strong one, and defaulting it to either would put a
    /// judgment on the record that nobody made.
    /// </remarks>
    public static string Reliability(string? reliability) => reliability switch
    {
        "Primary" => "Primary source",
        "High" => "High reliability",
        "Medium" => "Medium reliability",
        "Low" => "Low reliability",
        _ => "Not assessed",
    };

    /// <summary>
    /// Whether AgencyOS holds the evidence or only a pointer to it.
    /// </summary>
    /// <remarks>
    /// A URL is a reference. AgencyOS has not archived the page, cannot prove what
    /// it said, and this line is what stops a reader assuming otherwise (ADR-0030).
    /// </remarks>
    public static string Custody(bool isHeldByAgencyOS) =>
        isHeldByAgencyOS ? "Held by AgencyOS" : "Reference only, not archived";

    /// <summary>A probability, as a percentage with one decimal.</summary>
    public static string Probability(decimal probability) =>
        (probability * 100m).ToString("0.#", CultureInfo.CurrentCulture) + "%";

    /// <summary>
    /// The forecast, with whose it is and when.
    /// </summary>
    /// <remarks>
    /// The attribution is not decoration. A probability with no name beside it reads
    /// as the system's estimate, and AgencyOS never has one (§11).
    /// </remarks>
    public static string Forecast(PredictionResponse prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);

        string probability = Probability(prediction.CurrentProbability);

        if (prediction.CurrentProbabilityByDisplayName is not { Length: > 0 } forecaster)
        {
            return probability;
        }

        return prediction.CurrentProbabilityAsOf is { } asOf
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{probability} — {forecaster}, {asOf.LocalDateTime:d}")
            : string.Create(CultureInfo.CurrentCulture, $"{probability} — {forecaster}");
    }

    /// <summary>Where a prediction stands.</summary>
    public static string PredictionStatus(string? status) => status switch
    {
        "AwaitingResolution" => "Past its date, not resolved",
        "Resolved" => "Resolved",
        "Cancelled" => "Cancelled",
        _ => "Open",
    };

    /// <summary>
    /// The outcome, said plainly.
    /// </summary>
    /// <remarks>
    /// Unresolvable is a real answer and says so. It is never scored, and calling it
    /// a failure would push forecasters towards questions that are easy to grade
    /// rather than questions worth asking (§14).
    /// </remarks>
    public static string Outcome(string? outcome) => outcome switch
    {
        "Yes" => "Happened",
        "No" => "Did not happen",
        "Unresolvable" => "Could not be resolved",
        _ => "Not yet resolved",
    };

    /// <summary>
    /// A Brier score, with what it measures.
    /// </summary>
    /// <remarks>
    /// Zero is a perfect forecast and one is confidently wrong. Rendered as a bare
    /// number with that note rather than as a grade: "good" and "poor" are
    /// judgments, and three resolved predictions do not support one (§14).
    /// </remarks>
    public static string BrierScore(decimal? score) =>
        score is { } value
            ? value.ToString("0.000", CultureInfo.CurrentCulture) + " (0 is perfect, 1 is worst)"
            : "Not scored";

    /// <summary>
    /// A calibration figure, or an honest refusal to state one.
    /// </summary>
    /// <remarks>
    /// A mean over a handful of predictions supports very little, so the sample
    /// count is rendered beside every figure and never hidden behind it.
    /// </remarks>
    public static string Calibration(decimal? value, int sampleCount) =>
        value is { } number
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"{number:0.000} over {sampleCount} resolved")
            : "Nothing resolved yet";

    /// <summary>How long since somebody last spoke to them.</summary>
    public static string SinceLastInteraction(int? days) => days switch
    {
        null => "No recorded interaction",
        0 => "Today",
        1 => "Yesterday",
        _ => string.Create(CultureInfo.CurrentCulture, $"{days} days ago"),
    };

    /// <summary>
    /// The recorded relationship strength, or an explicit absence.
    /// </summary>
    /// <remarks>
    /// Never inferred from activity. Fourteen emails is not a strong relationship,
    /// and this returns "Not recorded" rather than manufacturing a rating from a
    /// count (§18).
    /// </remarks>
    public static string RecordedStrength(string? strength) =>
        strength is { Length: > 0 } value ? value : "Not recorded";
}

/// <summary>
/// The signal list.
/// </summary>
/// <remarks>
/// <para>
/// Every row carries its evidence count and its verification state, because a claim
/// read without either is a rumour with better typography. The count is of citations
/// <em>this reader may follow</em>: the server narrows evidence by classification
/// before counting it, so the number never advertises a source they cannot see
/// (§28).
/// </para>
/// <para>
/// The search box matches titles and claims. It does not match excerpts: an excerpt
/// quotes an M10 artifact, and matching one would report that artifact's contents to
/// whoever ran the search (ADR-0025).
/// </para>
/// </remarks>
public sealed class SignalListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _kind;
    private string? _verification;
    private string? _sensitivity;
    private string? _subjectKind;
    private Guid? _subjectId;
    private Guid? _watchlistId;
    private string _search = string.Empty;

    public SignalListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<SignalResponse> Signals { get; } = [];

    /// <summary>What kind of thing was observed. Business meaning, not format.</summary>
    public static IReadOnlyList<string> Kinds { get; } =
    [
        "PersonnelChange",
        "ProjectMovement",
        "MarketActivity",
        "CompanyChange",
        "Financing",
        "Availability",
        "Relationship",
        "Other",
    ];

    /// <summary>
    /// The four states a claim can be in.
    /// </summary>
    /// <remarks>
    /// There is no Verified, here or anywhere. Corroboration is about evidence
    /// agreeing, not about truth, and offering the word would invite the reading it
    /// exists to prevent (§1).
    /// </remarks>
    public static IReadOnlyList<string> Verifications { get; } =
    [
        "Unverified",
        "Corroborated",
        "Disputed",
        "Retracted",
    ];

    public static IReadOnlyList<string> Sensitivities { get; } =
    [
        "Internal",
        "Confidential",
        "SourceSensitive",
        "Restricted",
    ];

    public string? Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    public string? Verification
    {
        get => _verification;
        set => Set(ref _verification, value);
    }

    public string? Sensitivity
    {
        get => _sensitivity;
        set => Set(ref _sensitivity, value);
    }

    public string? SubjectKind
    {
        get => _subjectKind;
        set => Set(ref _subjectKind, value);
    }

    public Guid? SubjectId
    {
        get => _subjectId;
        set => Set(ref _subjectId, value);
    }

    public Guid? WatchlistId
    {
        get => _watchlistId;
        set => Set(ref _watchlistId, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value);
    }

    public override bool IsEmpty => _loaded && Signals.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<SignalResponse> signals = await _api
                    .ListSignalsAsync(
                        Kind,
                        Verification,
                        Sensitivity,
                        SubjectKind,
                        SubjectId,
                        sourceId: null,
                        WatchlistId,
                        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                        limit: null,
                        token)
                    .ConfigureAwait(true);

                Replace(Signals, signals);
                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);
}

/// <summary>
/// One signal, with everything it rests on.
/// </summary>
/// <remarks>
/// The evidence list is the point of the screen. A signal that lost its provenance
/// would be indistinguishable from something somebody made up, which is why the
/// aggregate refuses to let the last citation be removed and why this screen shows
/// the citations before the claim's own notes.
/// </remarks>
public sealed class SignalDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private SignalDetailResponse? _signal;

    public SignalDetailViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public SignalDetailResponse? Signal
    {
        get => _signal;
        private set
        {
            if (Set(ref _signal, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasWithheldExcerpts));
            }
        }
    }

    /// <summary>
    /// Whether any citation quotes something this reader may not open.
    /// </summary>
    /// <remarks>
    /// Said out loud, because a silently missing excerpt reads as an analyst who
    /// never took one. The citation itself was already legitimate; only the
    /// quotation is above this reader's M10 grant (ADR-0025).
    /// </remarks>
    public bool HasWithheldExcerpts =>
        _signal?.Evidence.Any(x => x.ExcerptWithheld) ?? false;

    public override bool IsEmpty => _signal is null;

    public Task LoadAsync(Guid signalId, CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                Signal = await _api.GetSignalAsync(signalId, token).ConfigureAwait(true);
            },
            cancellationToken);

    /// <summary>Records that other evidence agrees, disputes or retracts the claim.</summary>
    public Task ChangeVerificationAsync(
        string verification,
        string? note,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                if (_signal is null)
                {
                    return;
                }

                await _api
                    .ChangeSignalVerificationAsync(
                        _signal.Signal.Id,
                        new ChangeSignalVerificationRequest(
                            verification, _signal.Signal.Version, note),
                        token)
                    .ConfigureAwait(true);

                Signal = await _api
                    .GetSignalAsync(_signal.Signal.Id, token).ConfigureAwait(true);
            },
            cancellationToken);
}

/// <summary>The evidence list.</summary>
public sealed class IntelligenceSourceListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _kind;
    private string? _reliability;
    private string? _sensitivity;
    private string _search = string.Empty;

    public IntelligenceSourceListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<IntelligenceSourceResponse> Sources { get; } = [];

    /// <summary>
    /// What a source is.
    /// </summary>
    /// <remarks>
    /// The first two are things AgencyOS holds; the rest are references to things it
    /// does not. The list keeps that difference visible rather than flattening every
    /// source into "a document" (ADR-0030).
    /// </remarks>
    public static IReadOnlyList<string> Kinds { get; } =
    [
        "DocumentVersion",
        "Message",
        "ExternalUrl",
        "ManualObservation",
        "Other",
    ];

    public static IReadOnlyList<string> Reliabilities { get; } =
    [
        "Unassessed",
        "Low",
        "Medium",
        "High",
        "Primary",
    ];

    public string? Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    public string? Reliability
    {
        get => _reliability;
        set => Set(ref _reliability, value);
    }

    public string? Sensitivity
    {
        get => _sensitivity;
        set => Set(ref _sensitivity, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value);
    }

    public override bool IsEmpty => _loaded && Sources.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<IntelligenceSourceResponse> sources = await _api
                    .ListIntelligenceSourcesAsync(
                        Kind,
                        Reliability,
                        Sensitivity,
                        recordedBy: null,
                        observedAfter: null,
                        observedBefore: null,
                        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                        limit: null,
                        token)
                    .ConfigureAwait(true);

                Replace(Sources, sources);
                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);

    /// <summary>Records somebody's judgment of how reliable a source is.</summary>
    /// <remarks>
    /// Their judgment, with their name on it. Never derived from what the source
    /// says: a well written rumour reads exactly like a well written fact.
    /// </remarks>
    public Task AssessAsync(
        IntelligenceSourceResponse source,
        string reliability,
        string? rationale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return RunAsync(
            async token =>
            {
                await _api
                    .AssessSourceReliabilityAsync(
                        source.Id,
                        new AssessSourceReliabilityRequest(
                            reliability, source.Version, rationale),
                        token)
                    .ConfigureAwait(true);

                await LoadAsync(token).ConfigureAwait(true);
            },
            cancellationToken);
    }
}

/// <summary>
/// The thesis list.
/// </summary>
/// <remarks>
/// Supporting and challenging counts are shown side by side and never netted. Five
/// weak supporting signals do not outweigh one strong contradiction, and a single
/// "evidence score" would assert that they do (§18).
/// </remarks>
public sealed class ThesisListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status;
    private string? _sensitivity;
    private string _search = string.Empty;

    public ThesisListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<ThesisResponse> Theses { get; } = [];

    /// <summary>
    /// The four states a thesis can be in.
    /// </summary>
    /// <remarks>
    /// No True and no False. A thesis is a position somebody holds; the honest ways
    /// to stop holding it are to retire it with a reason or to supersede it with a
    /// better one (§1).
    /// </remarks>
    public static IReadOnlyList<string> Statuses { get; } =
    [
        "Draft",
        "Active",
        "Retired",
        "Superseded",
    ];

    public static IReadOnlyList<string> Confidences { get; } =
    [
        "Unstated",
        "Low",
        "Medium",
        "High",
    ];

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string? Sensitivity
    {
        get => _sensitivity;
        set => Set(ref _sensitivity, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value);
    }

    public override bool IsEmpty => _loaded && Theses.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<ThesisResponse> theses = await _api
                    .ListThesesAsync(
                        Status,
                        confidence: null,
                        Sensitivity,
                        subjectKind: null,
                        subjectId: null,
                        ownerUserId: null,
                        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                        limit: null,
                        token)
                    .ConfigureAwait(true);

                Replace(Theses, theses);
                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);
}

/// <summary>One thesis, with every position it has held.</summary>
public sealed class ThesisDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private ThesisDetailResponse? _thesis;

    public ThesisDetailViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ThesisDetailResponse? Thesis
    {
        get => _thesis;
        private set
        {
            if (Set(ref _thesis, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public override bool IsEmpty => _thesis is null;

    public Task LoadAsync(Guid thesisId, CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                Thesis = await _api.GetThesisAsync(thesisId, token).ConfigureAwait(true);
            },
            cancellationToken);

    /// <summary>States a new position, keeping the old one on the record.</summary>
    public Task ReviseAsync(
        string proposition,
        string confidence,
        string? rationale,
        string? changeNote,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                if (_thesis is null)
                {
                    return;
                }

                await _api
                    .ReviseThesisAsync(
                        _thesis.Thesis.Id,
                        new ReviseThesisRequest(
                            proposition,
                            confidence,
                            _thesis.Thesis.Version,
                            rationale,
                            changeNote),
                        token)
                    .ConfigureAwait(true);

                Thesis = await _api
                    .GetThesisAsync(_thesis.Thesis.Id, token).ConfigureAwait(true);
            },
            cancellationToken);
}

/// <summary>
/// The prediction list, and the calibration figure over it.
/// </summary>
/// <remarks>
/// The calibration panel reports arithmetic and a sample count and stops there.
/// There is no "well calibrated" badge and no forecaster leaderboard: a mean Brier
/// score over eleven predictions supports very little, and a ranking built on one
/// would be read as a verdict on a colleague (§14).
/// </remarks>
public sealed class PredictionListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status;
    private string? _outcome;
    private Guid? _ownerUserId;
    private string _search = string.Empty;
    private PredictionCalibrationResponse? _calibration;

    public PredictionListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<PredictionResponse> Predictions { get; } = [];

    public static IReadOnlyList<string> Statuses { get; } =
    [
        "Open",
        "AwaitingResolution",
        "Resolved",
        "Cancelled",
    ];

    /// <summary>
    /// The three ways a question can end.
    /// </summary>
    /// <remarks>
    /// Unresolvable is offered deliberately. Without it, a question whose answer
    /// never became knowable would be forced into Yes or No, and the calibration
    /// figure would be computed against a guess (§14).
    /// </remarks>
    public static IReadOnlyList<string> Outcomes { get; } =
    [
        "Yes",
        "No",
        "Unresolvable",
    ];

    public PredictionCalibrationResponse? Calibration
    {
        get => _calibration;
        private set => Set(ref _calibration, value);
    }

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string? Outcome
    {
        get => _outcome;
        set => Set(ref _outcome, value);
    }

    public Guid? OwnerUserId
    {
        get => _ownerUserId;
        set => Set(ref _ownerUserId, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value);
    }

    public override bool IsEmpty => _loaded && Predictions.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<PredictionResponse> predictions = await _api
                    .ListPredictionsAsync(
                        Status,
                        Outcome,
                        sensitivity: null,
                        subjectKind: null,
                        subjectId: null,
                        OwnerUserId,
                        resolvesBefore: null,
                        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                        limit: null,
                        token)
                    .ConfigureAwait(true);

                Replace(Predictions, predictions);

                Calibration = await _api
                    .GetPredictionCalibrationAsync(
                        OwnerUserId, resolvedAfter: null, resolvedBefore: null, token)
                    .ConfigureAwait(true);

                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);

    /// <summary>States a new probability. The previous one stays on the record.</summary>
    public Task ReviseAsync(
        PredictionResponse prediction,
        decimal probability,
        string? rationale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prediction);

        return RunAsync(
            async token =>
            {
                await _api
                    .RecordPredictionRevisionAsync(
                        prediction.Id,
                        new RecordPredictionRevisionRequest(
                            probability, prediction.Version, rationale),
                        token)
                    .ConfigureAwait(true);

                await LoadAsync(token).ConfigureAwait(true);
            },
            cancellationToken);
    }

    /// <summary>Closes a question with what actually happened.</summary>
    public Task ResolveAsync(
        PredictionResponse prediction,
        string outcome,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prediction);

        return RunAsync(
            async token =>
            {
                await _api
                    .ResolvePredictionAsync(
                        prediction.Id,
                        new ResolvePredictionRequest(outcome, prediction.Version, note),
                        token)
                    .ConfigureAwait(true);

                await LoadAsync(token).ConfigureAwait(true);
            },
            cancellationToken);
    }
}

/// <summary>The watchlist list.</summary>
public sealed class WatchlistListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status = "Active";
    private string _search = string.Empty;

    public WatchlistListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<WatchlistResponse> Watchlists { get; } = [];

    public static IReadOnlyList<string> Statuses { get; } = ["Active", "Archived"];

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value);
    }

    public override bool IsEmpty => _loaded && Watchlists.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<WatchlistResponse> watchlists = await _api
                    .ListWatchlistsAsync(
                        Status,
                        sensitivity: null,
                        ownerUserId: null,
                        subjectKind: null,
                        subjectId: null,
                        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                        limit: null,
                        token)
                    .ConfigureAwait(true);

                Replace(Watchlists, watchlists);
                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);

    /// <summary>Records that somebody looked at the list today.</summary>
    /// <remarks>
    /// A watchlist nobody reviews has quietly stopped being watched, and the only
    /// way to tell is to record the looking.
    /// </remarks>
    public Task RecordReviewAsync(
        WatchlistResponse watchlist,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(watchlist);

        return RunAsync(
            async token =>
            {
                await _api
                    .RecordWatchlistReviewAsync(
                        watchlist.Id, new RecordReviewRequest(watchlist.Version), token)
                    .ConfigureAwait(true);

                await LoadAsync(token).ConfigureAwait(true);
            },
            cancellationToken);
    }
}

/// <summary>
/// What has happened around the things a watchlist watches.
/// </summary>
/// <remarks>
/// Derived by subject overlap at read time, and narrowed by the server to what this
/// reader may see. Opening a watchlist is not a grant to the intelligence about its
/// members (§28).
/// </remarks>
public sealed class WatchlistActivityViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private WatchlistActivityResponse? _activity;

    public WatchlistActivityViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public WatchlistActivityResponse? Activity
    {
        get => _activity;
        private set
        {
            if (Set(ref _activity, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasNewActivity));
            }
        }
    }

    /// <summary>Whether anything has been recorded since the list was last reviewed.</summary>
    public bool HasNewActivity => _activity is { SignalsSinceLastReview: > 0 };

    public override bool IsEmpty => _activity is null;

    public Task LoadAsync(Guid watchlistId, CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                Activity = await _api
                    .GetWatchlistActivityAsync(watchlistId, token).ConfigureAwait(true);
            },
            cancellationToken);
}

/// <summary>
/// The talent radar.
/// </summary>
/// <remarks>
/// Priority is set by a person and is the only ordering the screen offers. There is
/// no fit score, no ranking and no "hot talent" list: whether to pursue somebody is
/// a judgment the agency makes, and a number beside their name would launder that
/// judgment into a recommendation (§18).
/// </remarks>
public sealed class TalentRadarViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status;
    private string? _priority;
    private string _search = string.Empty;

    public TalentRadarViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<TalentRadarResponse> Entries { get; } = [];

    /// <summary>
    /// Where an entry stands.
    /// </summary>
    /// <remarks>
    /// The list stops at ConvertedToProspect. Courting, signing and representation
    /// are M4's, and a second pursuit pipeline here would be two systems disagreeing
    /// about the same relationship (§40).
    /// </remarks>
    public static IReadOnlyList<string> Statuses { get; } =
    [
        "Watching",
        "Researching",
        "ReadyForReview",
        "ConvertedToProspect",
        "Dismissed",
    ];

    public static IReadOnlyList<string> Priorities { get; } =
    [
        "Unassigned",
        "Low",
        "Medium",
        "High",
    ];

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string? Priority
    {
        get => _priority;
        set => Set(ref _priority, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value);
    }

    public override bool IsEmpty => _loaded && Entries.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<TalentRadarResponse> entries = await _api
                    .ListTalentRadarAsync(
                        Status,
                        Priority,
                        sensitivity: null,
                        ownerUserId: null,
                        personId: null,
                        watchlistId: null,
                        discipline: null,
                        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                        limit: null,
                        token)
                    .ConfigureAwait(true);

                Replace(Entries, entries);
                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);

    /// <summary>Hands an entry to M4 as a prospect.</summary>
    public async Task<RadarConversionResponse?> ConvertAsync(
        TalentRadarResponse entry,
        string? source = null,
        string? strategyNotes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RadarConversionResponse? conversion = null;

        await RunAsync(
                async token =>
                {
                    conversion = await _api
                        .ConvertTalentRadarEntryAsync(
                            entry.Id,
                            new ConvertRadarEntryRequest(
                                entry.Version,
                                ProspectOwnerUserId: null,
                                IdentifiedOn: null,
                                source,
                                strategyNotes),
                            idempotencyKey: Guid.CreateVersion7().ToString(),
                            token)
                        .ConfigureAwait(true);

                    await LoadAsync(token).ConfigureAwait(true);
                },
                cancellationToken)
            .ConfigureAwait(true);

        return conversion;
    }

    /// <summary>Takes somebody off the radar, with a reason.</summary>
    public Task DismissAsync(
        TalentRadarResponse entry,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return RunAsync(
            async token =>
            {
                await _api
                    .DismissTalentRadarEntryAsync(
                        entry.Id,
                        new DismissRadarEntryRequest(reason, entry.Version),
                        token)
                    .ConfigureAwait(true);

                await LoadAsync(token).ConfigureAwait(true);
            },
            cancellationToken);
    }
}

/// <summary>The research case list.</summary>
public sealed class ResearchCaseListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status = "Open";
    private string _search = string.Empty;

    public ResearchCaseListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<ResearchCaseResponse> Cases { get; } = [];

    public static IReadOnlyList<string> Statuses { get; } =
    [
        "Open",
        "Paused",
        "Completed",
        "Cancelled",
    ];

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value);
    }

    public override bool IsEmpty => _loaded && Cases.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                IReadOnlyList<ResearchCaseResponse> cases = await _api
                    .ListResearchCasesAsync(
                        Status,
                        sensitivity: null,
                        ownerUserId: null,
                        subjectKind: null,
                        subjectId: null,
                        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                        limit: null,
                        token)
                    .ConfigureAwait(true);

                Replace(Cases, cases);
                _loaded = true;
                OnPropertyChanged(nameof(IsEmpty));
            },
            cancellationToken);
}

/// <summary>One research case, with everything it has gathered.</summary>
public sealed class ResearchCaseDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private ResearchCaseDetailResponse? _researchCase;

    public ResearchCaseDetailViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ResearchCaseDetailResponse? ResearchCase
    {
        get => _researchCase;
        private set
        {
            if (Set(ref _researchCase, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasOverdueWork));
            }
        }
    }

    public bool HasOverdueWork => _researchCase?.Tasks.Any(x => x.IsOverdue) ?? false;

    public override bool IsEmpty => _researchCase is null;

    public Task LoadAsync(Guid researchCaseId, CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                ResearchCase = await _api
                    .GetResearchCaseAsync(researchCaseId, token).ConfigureAwait(true);
            },
            cancellationToken);
}

/// <summary>
/// What is factually known about a working relationship.
/// </summary>
/// <remarks>
/// <para>
/// Dimensions, never a score. There is no health bar, no affinity ring and no
/// percentage anywhere on this screen: a composite number would be arithmetic over
/// incommensurable things, and its apparent precision would be believed (§18).
/// </para>
/// <para>
/// The recorded assessment is rendered apart from the counted activity, and
/// <see cref="IntelligenceFormatting.RecordedStrength"/> says "Not recorded" rather
/// than filling the gap from the interaction counts.
/// </para>
/// </remarks>
public sealed class RelationshipIntelligenceViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private RelationshipIntelligenceResponse? _relationship;

    public RelationshipIntelligenceViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public RelationshipIntelligenceResponse? Relationship
    {
        get => _relationship;
        private set
        {
            if (Set(ref _relationship, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasRecordedAssessment));
            }
        }
    }

    /// <summary>Whether anybody has actually written down what this relationship is.</summary>
    public bool HasRecordedAssessment =>
        _relationship?.RecordedStrength is { Length: > 0 };

    public override bool IsEmpty => _relationship is null;

    public Task LoadAsync(
        string kind,
        Guid subjectId,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                Relationship = await _api
                    .GetRelationshipIntelligenceAsync(kind, subjectId, token)
                    .ConfigureAwait(true);
            },
            cancellationToken);
}

/// <summary>
/// What the intelligence desk has to look at.
/// </summary>
/// <remarks>
/// Counts and real rows, in a fixed order. Nothing on this screen is ranked or
/// scored by the system: the panels are the states that need a person's attention,
/// and which of them matters most today is not something AgencyOS decides (§18).
/// </remarks>
public sealed class IntelligenceCommandCenterViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private IntelligenceCommandCenterResponse? _model;

    public IntelligenceCommandCenterViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public IntelligenceCommandCenterResponse? Model
    {
        get => _model;
        private set
        {
            if (Set(ref _model, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(NeedsAttention));
            }
        }
    }

    /// <summary>Whether anything at all is waiting.</summary>
    public bool NeedsAttention =>
        _model is not null
        && (_model.AwaitingResolutionCount > 0
            || _model.DueSoonCount > 0
            || _model.DisputedSignalCount > 0
            || _model.RadarAwaitingReviewCount > 0
            || _model.WatchlistsWithNewActivity.Count > 0
            || _model.ResearchCasesWithOverdueTasks.Count > 0);

    public override bool IsEmpty => _model is not null && !NeedsAttention;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async token =>
            {
                Model = await _api
                    .GetIntelligenceCommandCenterAsync(token).ConfigureAwait(true);
            },
            cancellationToken);
}
