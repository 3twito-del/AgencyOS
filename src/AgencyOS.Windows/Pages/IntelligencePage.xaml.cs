using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Client.Commands;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The intelligence workspace.
/// </summary>
/// <remarks>
/// <para>
/// The tabs are the chain, in order: what the desk has to look at, then claims,
/// then the evidence under them, then the views built on them, then the forecasts
/// those views imply, then the standing programmes. Keeping them apart on screen is
/// the same decision as keeping them apart in the schema — a single "intelligence"
/// list would let a rumour, a considered position and a falsifiable forecast sit in
/// one column looking identical (§1, ADR-0030).
/// </para>
/// <para>
/// <strong>Nothing on this page generates anything.</strong> There is no summarize
/// button, no "extract signals from this document" and no suggested probability. The
/// system contributes counts, Brier arithmetic over resolved forecasts, and the
/// discipline of asking for provenance; every judgment here was typed by a person
/// (§18, §54).
/// </para>
/// <para>
/// Nothing is cached. Intelligence is ONLINE_ONLY in
/// <c>docs/13_OFFLINE_CLASSIFICATION.md</c>: a source-sensitive signal names somebody
/// who spoke in confidence, and a copy of it on a laptop is not something a later
/// revocation can take back (§51).
/// </para>
/// </remarks>
public sealed partial class IntelligencePage : Page, IPaletteCommandTarget, IRecordTarget
{
    private readonly IAgencyOsApi? _api;

    private readonly IntelligenceCommandCenterViewModel? _desk;
    private readonly SignalListViewModel? _signals;
    private readonly SignalDetailViewModel? _signal;
    private readonly IntelligenceSourceListViewModel? _sources;
    private readonly ThesisListViewModel? _theses;
    private readonly ThesisDetailViewModel? _thesis;
    private readonly PredictionListViewModel? _predictions;
    private readonly WatchlistListViewModel? _watchlists;
    private readonly WatchlistActivityViewModel? _watchlistActivity;
    private readonly TalentRadarViewModel? _radar;
    private readonly ResearchCaseListViewModel? _research;
    private readonly ResearchCaseDetailViewModel? _researchCase;
    private readonly RelationshipIntelligenceViewModel? _relationship;

    public IntelligencePage()
    {
        InitializeComponent();

        BuildAuthoringMenu();

        if (AppServices.Api is not { } api)
        {
            return;
        }

        _api = api;

        _desk = new IntelligenceCommandCenterViewModel(api);
        _desk.PropertyChanged += (_, _) => RenderDesk();

        _signals = new SignalListViewModel(api);
        _signals.PropertyChanged += (_, _) => RenderSignalList();

        _signal = new SignalDetailViewModel(api);
        _signal.PropertyChanged += (_, _) => RenderSignalDetail();

        _sources = new IntelligenceSourceListViewModel(api);
        _sources.PropertyChanged += (_, _) => RenderSourceList();

        _theses = new ThesisListViewModel(api);
        _theses.PropertyChanged += (_, _) => RenderThesisList();

        _thesis = new ThesisDetailViewModel(api);
        _thesis.PropertyChanged += (_, _) => RenderThesisDetail();

        _predictions = new PredictionListViewModel(api);
        _predictions.PropertyChanged += (_, _) => RenderPredictions();

        _watchlists = new WatchlistListViewModel(api);
        _watchlists.PropertyChanged += (_, _) => RenderWatchlists();

        _watchlistActivity = new WatchlistActivityViewModel(api);
        _watchlistActivity.PropertyChanged += (_, _) => RenderWatchlistActivity();

        _radar = new TalentRadarViewModel(api);
        _radar.PropertyChanged += (_, _) => RenderRadar();

        _research = new ResearchCaseListViewModel(api);
        _research.PropertyChanged += (_, _) => RenderResearchList();

        _researchCase = new ResearchCaseDetailViewModel(api);
        _researchCase.PropertyChanged += (_, _) => RenderResearchDetail();

        _relationship = new RelationshipIntelligenceViewModel(api);
        _relationship.PropertyChanged += (_, _) => RenderRelationship();

        SignalList.ItemsSource = _signals.Signals;
        SourceList.ItemsSource = _sources.Sources;
        ThesisList.ItemsSource = _theses.Theses;
        PredictionList.ItemsSource = _predictions.Predictions;
        WatchlistList.ItemsSource = _watchlists.Watchlists;
        RadarList.ItemsSource = _radar.Entries;
        ResearchList.ItemsSource = _research.Cases;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadCurrentAsync();

    /// <inheritdoc />
    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "view.refresh":
                _ = LoadCurrentAsync();
                break;

            case "intelligence.source.record":
                _ = RecordSourceAsync();
                break;

            case "intelligence.signal.record":
                _ = RecordSignalAsync();
                break;

            case "intelligence.signal.verification":
                _ = ChangeVerificationAsync();
                break;

            case "intelligence.thesis.create":
                _ = CreateThesisAsync();
                break;

            case "intelligence.thesis.revise":
                _ = ReviseThesisAsync();
                break;

            case "intelligence.thesis.retire":
                _ = RetireThesisAsync();
                break;

            case "intelligence.prediction.create":
                _ = CreatePredictionAsync();
                break;

            case "intelligence.prediction.forecast":
                _ = RecordForecastAsync();
                break;

            case "intelligence.prediction.resolve":
                _ = ResolvePredictionAsync();
                break;

            case "intelligence.watchlist.create":
                _ = CreateWatchlistAsync();
                break;

            case "intelligence.watchlist.review":
                _ = ReviewWatchlistAsync();
                break;

            case "intelligence.radar.add":
                _ = AddRadarEntryAsync();
                break;

            case "intelligence.radar.convert":
                _ = ConvertRadarEntryAsync();
                break;

            case "intelligence.radar.dismiss":
                _ = DismissRadarEntryAsync();
                break;

            case "intelligence.research.open":
                _ = OpenResearchCaseAsync();
                break;

            case "intelligence.research.link":
                _ = LinkResearchItemAsync();
                break;

            case "go.intelligence.desk":
                Tabs.SelectedIndex = 0;
                _ = LoadCurrentAsync();
                break;

            case "go.intelligence.predictions":
                Tabs.SelectedIndex = 4;
                _ = LoadCurrentAsync();
                break;

            case "go.intelligence.radar":
                Tabs.SelectedIndex = 6;
                _ = LoadCurrentAsync();
                break;

            default:
                break;
        }
    }

    // ------------------------------------------------------------- loading

    /// <summary>
    /// Loads only the tab in front of the user.
    /// </summary>
    /// <remarks>
    /// Eight surfaces loaded together would be eight round trips on every visit,
    /// most of them for panels nobody opened.
    /// </remarks>
    private Task LoadCurrentAsync() => Tabs.SelectedIndex switch
    {
        8 => LoadRelationshipSubjectsAsync(),
        0 => _desk?.LoadAsync() ?? Task.CompletedTask,
        1 => _signals?.LoadAsync() ?? Task.CompletedTask,
        2 => _sources?.LoadAsync() ?? Task.CompletedTask,
        3 => _theses?.LoadAsync() ?? Task.CompletedTask,
        4 => _predictions?.LoadAsync() ?? Task.CompletedTask,
        5 => _watchlists?.LoadAsync() ?? Task.CompletedTask,
        6 => _radar?.LoadAsync() ?? Task.CompletedTask,
        7 => _research?.LoadAsync() ?? Task.CompletedTask,
        _ => Task.CompletedTask,
    };

    private void OnTabChanged(object sender, SelectionChangedEventArgs e) =>
        _ = LoadCurrentAsync();


    /// <summary>
    /// Opens the case the desk is reporting overdue work on.
    /// </summary>
    /// <remarks>
    /// The desk said research had overdue work and gave no way to reach it, so an
    /// operator could see that something needed doing and still had to already
    /// know which case it was - blind testing found the tasks only after
    /// selecting the right one (J10). The task itself is not duplicated here; the
    /// row simply leads to the case that owns it.
    /// </remarks>
    private void OnOverdueResearchInvoked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ResearchCaseResponse overdue)
        {
            return;
        }

        // The same route an entity's intelligence row takes, from inside the
        // workspace rather than from outside it.
        Reveal(overdue.Id, "Research");
    }

    private static void Choose(ComboBox box, string tag)
    {
        foreach (object item in box.Items)
        {
            if (item is ComboBoxItem { Tag: string candidate }
                && string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                box.SelectedItem = item;

                return;
            }
        }
    }

    /// <summary>Where the Research tab sits, as <see cref="LoadCurrentAsync"/> counts.</summary>
    private const int ResearchTabIndex = 7;

    /// <summary>The record another workspace asked this page to open on.</summary>
    private (Guid Id, string? Kind)? _reveal;

    /// <inheritdoc />
    public void Reveal(Guid record) => Reveal(record, null);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The return path a person or a company had none of. Intelligence records
    /// carry their subjects and render them, so the workspace could always say
    /// who a thesis was about; the entity could not say what was believed about
    /// it, and there was no way back.
    /// </para>
    /// <para>
    /// The kind picks the tab, because a thesis, a signal and a research case are
    /// different claims kept deliberately apart. Without one there is nothing
    /// sensible to do, so the page stays where it is rather than guessing.
    /// </para>
    /// </remarks>
    public void Reveal(Guid record, string? kind)
    {
        if (TabFor(kind) is not { } tab)
        {
            return;
        }

        _reveal = (record, kind);

        // An operator arriving from an entity is asking for one record, and these
        // lists default to the live ones.
        switch (kind)
        {
            case "Thesis":
                Choose(ThesisStatusBox, string.Empty);
                break;

            case "Research":
                Choose(ResearchStatusBox, string.Empty);
                ResearchSearchBox.Text = string.Empty;
                break;

            default:
                break;
        }

        Tabs.SelectedIndex = tab;
    }

    /// <summary>Which tab owns a kind of record, or nothing for an unknown one.</summary>
    private static int? TabFor(string? kind) => kind switch
    {
        "Signal" => 1,
        "Thesis" => 3,
        "Research" => ResearchTabIndex,
        _ => null,
    };

    /// <summary>Selects the requested record, once its row exists.</summary>
    private void SelectRevealed()
    {
        if (_reveal is not { } wanted)
        {
            return;
        }

        switch (wanted.Kind)
        {
            case "Signal" when _signals?.Signals.FirstOrDefault(x => x.Id == wanted.Id) is { } signal:
                _reveal = null;
                SignalList.SelectedItem = signal;
                SignalList.ScrollIntoView(signal);
                break;

            case "Thesis" when _theses?.Theses.FirstOrDefault(x => x.Id == wanted.Id) is { } thesis:
                _reveal = null;
                ThesisList.SelectedItem = thesis;
                ThesisList.ScrollIntoView(thesis);
                break;

            case "Research" when _research?.Cases.FirstOrDefault(x => x.Id == wanted.Id) is { } found:
                _reveal = null;
                ResearchList.SelectedItem = found;
                ResearchList.ScrollIntoView(found);
                break;

            default:
                break;
        }
    }

    // -------------------------------------------------------------- render

    private void RenderDesk()
    {
        if (_desk is null)
        {
            return;
        }

        Show(DeskBusy, _desk.IsLoading);
        ShowError(_desk.ErrorMessage);

        DeskClear.IsOpen = _desk.Model is not null && !_desk.NeedsAttention;

        if (_desk.Model is not { } model)
        {
            return;
        }

        AwaitingHeader.Text = Count("Forecasts past their date", model.AwaitingResolutionCount);
        DisputedHeader.Text = Count("Disputed claims", model.DisputedSignalCount);
        RadarReviewHeader.Text = Count("Radar entries ready for review", model.RadarAwaitingReviewCount);

        AwaitingList.ItemsSource = model.PredictionsAwaitingResolution;
        DisputedList.ItemsSource = model.DisputedSignals;
        RadarReviewList.ItemsSource = model.RadarAwaitingReview;
        WatchlistActivityList.ItemsSource = model.WatchlistsWithNewActivity;
        OverdueResearchList.ItemsSource = model.ResearchCasesWithOverdueTasks;
    }

    private void RenderSignalList()
    {
        if (_signals is null)
        {
            return;
        }

        Show(SignalBusy, _signals.IsLoading);
        SignalEmpty.IsOpen = _signals.IsEmpty;
        ShowError(_signals.ErrorMessage);

        SelectRevealed();

        SelectRevealed();
    }

    private void RenderSignalDetail()
    {
        if (_signal?.Signal is not { } detail)
        {
            SignalTitleText.Text = string.Empty;
            SignalClaimText.Text = string.Empty;
            SignalVerificationBar.IsOpen = false;
            WithheldBar.IsOpen = false;
            return;
        }

        ShowError(_signal.ErrorMessage);

        SignalTitleText.Text = detail.Signal.Title;
        SignalClaimText.Text = detail.Signal.Claim;

        SignalVerificationBar.IsOpen = true;
        SignalVerificationBar.Title = IntelligenceFormatting.Verification(detail.Signal.Verification);

        SignalVerificationBar.Severity = IntelligenceFormatting.NeedsCare(detail.Signal.Verification)
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Informational;

        SignalVerificationBar.Message = detail.VerificationNote ?? string.Empty;

        SignalMetaText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{detail.Signal.Kind} · observed {detail.Signal.ObservedAt.LocalDateTime:d} · "
                + $"{detail.Signal.Sensitivity} · recorded by "
                + $"{detail.Signal.RecordedByDisplayName ?? "somebody no longer here"}");

        // Said out loud. A silently missing excerpt reads as an analyst who never
        // took one, which is a different and misleading fact.
        WithheldBar.IsOpen = _signal.HasWithheldExcerpts;

        SignalEvidenceList.ItemsSource = detail.Evidence;
        SignalSubjectList.ItemsSource = detail.Signal.Subjects;
        SignalHistoryList.ItemsSource = detail.History;
    }

    private void RenderSourceList()
    {
        if (_sources is null)
        {
            return;
        }

        Show(SourceBusy, _sources.IsLoading);
        SourceEmpty.IsOpen = _sources.IsEmpty;
        ShowError(_sources.ErrorMessage);
    }

    private void RenderThesisList()
    {
        if (_theses is null)
        {
            return;
        }

        Show(ThesisBusy, _theses.IsLoading);
        ThesisEmpty.IsOpen = _theses.IsEmpty;
        ShowError(_theses.ErrorMessage);

        SelectRevealed();
    }

    private void RenderThesisDetail()
    {
        if (_thesis?.Thesis is not { } detail)
        {
            ThesisTitleText.Text = string.Empty;
            ThesisPropositionText.Text = string.Empty;
            ThesisEvidenceBalanceText.Text = string.Empty;
            return;
        }

        ShowError(_thesis.ErrorMessage);

        ThesisTitleText.Text = detail.Thesis.Title;
        ThesisPropositionText.Text = detail.Thesis.Proposition;

        ThesisMetaText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{detail.Thesis.Status} · confidence {detail.Thesis.Confidence} · "
                + $"{detail.Thesis.Sensitivity} · "
                + $"{detail.Thesis.OwnerDisplayName ?? "no owner recorded"}");

        // Side by side, never netted. Five weak agreements do not outweigh one
        // strong contradiction, and a single figure would assert that they do.
        ThesisEvidenceBalanceText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{detail.Thesis.SupportingCount} supporting · "
                + $"{detail.Thesis.ChallengingCount} challenging");

        ThesisRevisionList.ItemsSource = detail.Revisions;
        ThesisEvidenceList.ItemsSource = detail.Evidence;
    }

    private void RenderPredictions()
    {
        if (_predictions is null)
        {
            return;
        }

        Show(PredictionBusy, _predictions.IsLoading);
        PredictionEmpty.IsOpen = _predictions.IsEmpty;
        ShowError(_predictions.ErrorMessage);

        if (_predictions.Calibration is not { } calibration)
        {
            CalibrationText.Text = string.Empty;
            CalibrationCaveatText.Text = string.Empty;
            return;
        }

        CalibrationText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"Mean Brier score: "
                + $"{IntelligenceFormatting.Calibration(calibration.MeanBrierScore, calibration.ResolvedCount)}"
                + $" · mean forecast {Percent(calibration.MeanProbability)}"
                + $" · observed {Percent(calibration.ObservedFrequency)}");

        // The caveat is not boilerplate. It is the difference between a number and
        // a verdict about a colleague.
        CalibrationCaveatText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{calibration.ResolvedCount} resolved "
                + $"({calibration.YesCount} happened, {calibration.NoCount} did not), "
                + $"{calibration.UnresolvableCount} unresolvable and never scored, "
                + $"{calibration.OpenCount} still open. "
                + $"Zero is a perfect forecast and one is confidently wrong. A mean over "
                + $"a small number of questions says very little.");
    }

    private void RenderWatchlists()
    {
        if (_watchlists is null)
        {
            return;
        }

        Show(WatchlistBusy, _watchlists.IsLoading);
        WatchlistEmpty.IsOpen = _watchlists.IsEmpty;
        ShowError(_watchlists.ErrorMessage);
    }

    private void RenderWatchlistActivity()
    {
        if (_watchlistActivity?.Activity is not { } activity)
        {
            WatchlistNameText.Text = string.Empty;
            WatchlistReviewText.Text = string.Empty;
            WatchlistNewActivityBar.IsOpen = false;
            return;
        }

        ShowError(_watchlistActivity.ErrorMessage);

        WatchlistNameText.Text = activity.Watchlist.Name;

        WatchlistReviewText.Text = activity.Watchlist.LastReviewedAt is { } reviewed
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"Last reviewed {reviewed.LocalDateTime:d} by "
                    + $"{activity.Watchlist.LastReviewedByDisplayName ?? "somebody"}")
            : "Nobody has reviewed this list yet.";

        WatchlistNewActivityBar.IsOpen = _watchlistActivity.HasNewActivity;

        WatchlistNewActivityBar.Message = string.Create(
            CultureInfo.CurrentCulture,
            $"{activity.SignalsSinceLastReview} signals recorded about these records since "
                + $"the last review.");

        WatchlistSignalList.ItemsSource = activity.RecentSignals;
        WatchlistPredictionList.ItemsSource = activity.OpenPredictions;
    }

    private void RenderRadar()
    {
        if (_radar is null)
        {
            return;
        }

        Show(RadarBusy, _radar.IsLoading);
        RadarEmpty.IsOpen = _radar.IsEmpty;
        ShowError(_radar.ErrorMessage);
    }

    private void RenderResearchList()
    {
        if (_research is null)
        {
            return;
        }

        Show(ResearchBusy, _research.IsLoading);
        ResearchEmpty.IsOpen = _research.IsEmpty;
        ShowError(_research.ErrorMessage);

        SelectRevealed();
    }

    private void RenderResearchDetail()
    {
        if (_researchCase?.ResearchCase is not { } detail)
        {
            ResearchQuestionText.Text = string.Empty;
            ResearchContextText.Text = string.Empty;
            ResearchCountsText.Text = string.Empty;
            ResearchOverdueBar.IsOpen = false;
            return;
        }

        ShowError(_researchCase.ErrorMessage);

        ResearchQuestionText.Text = detail.ResearchCase.Question;
        ResearchContextText.Text = detail.Context ?? string.Empty;

        ResearchCountsText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{detail.ResearchCase.SourceCount} sources · "
                + $"{detail.ResearchCase.SignalCount} signals · "
                + $"{detail.ResearchCase.ThesisCount} theses · "
                + $"{detail.ResearchCase.PredictionCount} predictions · "
                + $"{detail.ResearchCase.OpenTaskCount} of "
                + $"{detail.ResearchCase.TaskCount} tasks open");

        ResearchOverdueBar.IsOpen = _researchCase.HasOverdueWork;
        ResearchOverdueBar.Message = "One or more tasks on this case are past their due date.";

        ResearchTaskList.ItemsSource = detail.Tasks;
        ResearchSignalList.ItemsSource = detail.Signals;
        ResearchSourceList.ItemsSource = detail.Sources;
    }

    private void RenderRelationship()
    {
        if (_relationship is null)
        {
            return;
        }

        Show(RelationshipBusy, _relationship.IsLoading);
        ShowError(_relationship.ErrorMessage);

        if (_relationship.Relationship is not { } model)
        {
            RelationshipNameText.Text = string.Empty;
            return;
        }

        RelationshipNameText.Text = model.DisplayName;

        // What a person wrote down, said as such. Never filled in from the counts
        // below it, because activity is not affection and the model refuses to
        // pretend otherwise.
        RecordedStrengthText.Text =
            IntelligenceFormatting.RecordedStrength(model.RecordedStrength)
            + (model.RecordedRelationshipNote is { Length: > 0 } note ? " — " + note : string.Empty);

        RelationshipOwnerText.Text = model.RelationshipOwnerDisplayName is { Length: > 0 } owner
            ? "Relationship owned by " + owner
            : "No lead agent recorded.";

        InteractionCountsText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{model.Interactions30Days} interactions in 30 days · "
                + $"{model.Interactions90Days} in 90 · "
                + $"{model.Interactions365Days} in a year");

        string lastKind = model.LastInteractionKind is { Length: > 0 } kind
            ? $" ({kind})"
            : string.Empty;

        LastInteractionText.Text =
            "Last contact: "
            + IntelligenceFormatting.SinceLastInteraction(model.DaysSinceLastInteraction)
            + lastKind;

        RelationshipTasksText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{model.OpenTaskCount} open tasks, {model.OverdueTaskCount} overdue");

        RelationshipSignalList.ItemsSource = model.RecentSignals;
        RelationshipWatchlistList.ItemsSource = model.Watchlists;
    }

    // ------------------------------------------------------------ commands

    private async Task RecordSourceAsync()
    {
        try
        {
            if (_api is null || _sources is null)
            {
                return;
            }

            RecordSourceDialog dialog = new() { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _api
                .RecordIntelligenceSourceAsync(
                    dialog.ToRequest(), Guid.CreateVersion7().ToString())
                .ConfigureAwait(true);

            await _sources.LoadAsync().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not record the source";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private async Task RecordSignalAsync()
    {
        try
        {
            if (_api is null || _signals is null)
            {
                return;
            }

            IReadOnlyList<IntelligenceSourceResponse> sources = await _api
                .ListIntelligenceSourcesAsync()
                .ConfigureAwait(true);

            // Refused before the dialog opens, with the reason. A signal cannot be
            // recorded without a source, so offering an empty picker would be an
            // invitation to a refusal (§1).
            if (sources.Count == 0)
            {
                await NoteAsync(
                    "Record a source first",
                    "A signal is recorded with at least one source, and there are none yet. "
                        + "Record what you saw as a source, then record the claim it supports.")
                    .ConfigureAwait(true);

                return;
            }

            RecordSignalDialog dialog = new(sources) { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _api
                .RecordSignalAsync(dialog.ToRequest(), Guid.CreateVersion7().ToString())
                .ConfigureAwait(true);

            await _signals.LoadAsync().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not record the signal";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private async Task ChangeVerificationAsync()
    {
        if (_signal?.Signal is not { } detail)
        {
            return;
        }

        ChangeVerificationDialog dialog =
            new(detail.Signal.Claim, detail.Signal.Verification) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await _signal
            .ChangeVerificationAsync(dialog.Verification, dialog.Note)
            .ConfigureAwait(true);

        await (_signals?.LoadAsync() ?? Task.CompletedTask).ConfigureAwait(true);
    }

    private async Task CreateThesisAsync()
    {
        try
        {
            if (_api is null || _theses is null)
            {
                return;
            }

            CreateThesisDialog dialog = new() { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _api
                .CreateThesisAsync(dialog.ToRequest(), Guid.CreateVersion7().ToString())
                .ConfigureAwait(true);

            await _theses.LoadAsync().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not state the thesis";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private async Task ReviseThesisAsync()
    {
        if (_thesis?.Thesis is not { } detail)
        {
            return;
        }

        ReviseThesisDialog dialog =
            new(detail.Thesis.Proposition, detail.Thesis.Confidence) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await _thesis
            .ReviseAsync(
                dialog.Proposition, dialog.Confidence, dialog.Rationale, dialog.ChangeNote)
            .ConfigureAwait(true);

        await (_theses?.LoadAsync() ?? Task.CompletedTask).ConfigureAwait(true);
    }

    private async Task RetireThesisAsync()
    {
        try
        {
            if (_api is null || _thesis?.Thesis is not { } detail)
            {
                return;
            }

            IntelligenceReasonDialog dialog = new(
                "Retire this thesis",
                detail.Thesis.Title,
                "The thesis stays on the record with your reason attached. Nothing is deleted: "
                    + "what the agency used to think is part of the history.",
                "Retire")
            { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _api
                .CloseThesisAsync(
                    detail.Thesis.Id,
                    new CloseThesisRequest(dialog.Reason, detail.Thesis.Version))
                .ConfigureAwait(true);

            await _thesis.LoadAsync(detail.Thesis.Id).ConfigureAwait(true);
            await (_theses?.LoadAsync() ?? Task.CompletedTask).ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not retire the thesis";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private async Task CreatePredictionAsync()
    {
        try
        {
            if (_api is null || _predictions is null)
            {
                return;
            }

            CreatePredictionDialog dialog = new() { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _api
                .CreatePredictionAsync(dialog.ToRequest(), Guid.CreateVersion7().ToString())
                .ConfigureAwait(true);

            await _predictions.LoadAsync().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not record the prediction";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private async Task RecordForecastAsync()
    {
        if (_predictions is null
            || PredictionList.SelectedItem is not PredictionResponse prediction)
        {
            return;
        }

        RecordForecastDialog dialog = new(prediction) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await _predictions
            .ReviseAsync(prediction, dialog.Probability, dialog.Rationale)
            .ConfigureAwait(true);
    }

    private async Task ResolvePredictionAsync()
    {
        try
        {
            if (_api is null || _predictions is null)
            {
                return;
            }

            // Said rather than ignored. The command palette offers this from every
            // tab and takes no argument, so running it with nothing selected is an
            // ordinary thing for an operator to do - and until AOS-R002-019 it
            // returned in silence, which is indistinguishable from the command
            // being broken.
            if (PredictionList.SelectedItem is not PredictionResponse prediction)
            {
                await NoteAsync(
                    "Choose a prediction first",
                    "Resolving records what actually happened against one stated forecast, "
                        + "so there is nothing to resolve until one is selected. Open the "
                        + "Predictions tab and pick the question you are settling.")
                    .ConfigureAwait(true);

                return;
            }

            PredictionDetailResponse detail = await _api
                .GetPredictionAsync(prediction.Id).ConfigureAwait(true);

            ResolvePredictionDialog dialog =
                new(prediction, detail.ResolutionCriteria) { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _predictions
                .ResolveAsync(prediction, dialog.Outcome, dialog.Note)
                .ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not resolve the prediction";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private async Task CreateWatchlistAsync()
    {
        try
        {
            if (_api is null || _watchlists is null)
            {
                return;
            }

            CreateWatchlistDialog dialog = new() { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _api
                .CreateWatchlistAsync(dialog.ToRequest(), Guid.CreateVersion7().ToString())
                .ConfigureAwait(true);

            await _watchlists.LoadAsync().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not create the watchlist";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private async Task ReviewWatchlistAsync()
    {
        if (_watchlists is null
            || WatchlistList.SelectedItem is not WatchlistResponse watchlist)
        {
            return;
        }

        await _watchlists.RecordReviewAsync(watchlist).ConfigureAwait(true);

        await (_watchlistActivity?.LoadAsync(watchlist.Id) ?? Task.CompletedTask)
            .ConfigureAwait(true);
    }

    private async Task AddRadarEntryAsync()
    {
        try
        {
            if (_api is null || _radar is null)
            {
                return;
            }

            IReadOnlyList<PersonSummaryResponse> people =
                await _api.ListPeopleAsync().ConfigureAwait(true);

            if (people.Count == 0)
            {
                await NoteAsync(
                    "No people to watch",
                    "The radar points at an existing person record. Create the person first, "
                        + "so two spellings of the same name do not become two pursuits.")
                    .ConfigureAwait(true);

                return;
            }

            AddRadarEntryDialog dialog = new(people) { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _api
                .CreateTalentRadarEntryAsync(dialog.ToRequest(), Guid.CreateVersion7().ToString())
                .ConfigureAwait(true);

            await _radar.LoadAsync().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not add the radar entry";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private async Task ConvertRadarEntryAsync()
    {
        if (_radar is null || RadarList.SelectedItem is not TalentRadarResponse entry)
        {
            return;
        }

        ContentDialog confirm = new()
        {
            Title = "Hand this to representation",
            Content =
                $"{entry.PersonDisplayName} becomes a prospect in M4, and the radar entry "
                    + "closes as converted. Courting, signing and representation are "
                    + "tracked there from that point on.",
            PrimaryButtonText = "Convert",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        RadarConversionResponse? conversion =
            await _radar.ConvertAsync(entry).ConfigureAwait(true);

        if (conversion is not null)
        {
            await NoteAsync(
                "Handed over",
                conversion.CreatedTalentProfile
                    ? "A prospect and a talent profile were created."
                    : "A prospect was created against the existing talent profile.")
                .ConfigureAwait(true);
        }
    }

    private async Task DismissRadarEntryAsync()
    {
        if (_radar is null || RadarList.SelectedItem is not TalentRadarResponse entry)
        {
            return;
        }

        IntelligenceReasonDialog dialog = new(
            "Take this person off the radar",
            entry.PersonDisplayName,
            "The entry stays on the record with your reason attached. Nothing about the "
                + "person is deleted, and they can be put back on the radar later.",
            "Dismiss")
        { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await _radar.DismissAsync(entry, dialog.Reason).ConfigureAwait(true);
    }

    private async Task OpenResearchCaseAsync()
    {
        try
        {
            if (_api is null || _research is null)
            {
                return;
            }

            OpenResearchCaseDialog dialog = new() { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _api
                .OpenResearchCaseAsync(dialog.ToRequest(), Guid.CreateVersion7().ToString())
                .ConfigureAwait(true);

            await _research.LoadAsync().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not open the research case";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private async Task LinkResearchItemAsync()
    {
        try
        {
            if (_api is null
                || _researchCase?.ResearchCase is not { } detail)
            {
                return;
            }

            LinkResearchItemDialog dialog = new(_api) { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _api
                .LinkResearchItemAsync(
                    detail.ResearchCase.Id,
                    new LinkResearchItemRequest(
                        dialog.Kind,
                        dialog.LinkedId,
                        detail.ResearchCase.Version,
                        dialog.Note))
                .ConfigureAwait(true);

            await _researchCase.LoadAsync(detail.ResearchCase.Id).ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not attach that";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    // --------------------------------------------------------------- input

    private void OnSignalFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_signals is null)
        {
            return;
        }

        _signals.Kind = Selection(SignalKindBox);
        _signals.Verification = Selection(SignalVerificationBox);
        _ = _signals.LoadAsync();
    }

    private void OnSignalSearchSubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_signals is null)
        {
            return;
        }

        _signals.Search = args.QueryText ?? string.Empty;
        _ = _signals.LoadAsync();
    }

    private void OnSignalSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SignalList.SelectedItem is SignalResponse signal)
        {
            _ = _signal?.LoadAsync(signal.Id);
        }
    }

    private void OnSourceFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sources is null)
        {
            return;
        }

        _sources.Kind = Selection(SourceKindBox);
        _sources.Reliability = Selection(SourceReliabilityBox);
        _ = _sources.LoadAsync();
    }

    private void OnSourceSearchSubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_sources is null)
        {
            return;
        }

        _sources.Search = args.QueryText ?? string.Empty;
        _ = _sources.LoadAsync();
    }

    private void OnThesisFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_theses is null)
        {
            return;
        }

        _theses.Status = Selection(ThesisStatusBox);
        _ = _theses.LoadAsync();
    }

    private void OnThesisSearchSubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_theses is null)
        {
            return;
        }

        _theses.Search = args.QueryText ?? string.Empty;
        _ = _theses.LoadAsync();
    }

    private void OnThesisSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ThesisList.SelectedItem is ThesisResponse thesis)
        {
            _ = _thesis?.LoadAsync(thesis.Id);
        }
    }

    private void OnPredictionFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_predictions is null)
        {
            return;
        }

        _predictions.Status = Selection(PredictionStatusBox);
        _ = _predictions.LoadAsync();
    }

    private void OnPredictionSearchSubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_predictions is null)
        {
            return;
        }

        _predictions.Search = args.QueryText ?? string.Empty;
        _ = _predictions.LoadAsync();
    }

    private void OnWatchlistFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_watchlists is null)
        {
            return;
        }

        _watchlists.Status = Selection(WatchlistStatusBox);
        _ = _watchlists.LoadAsync();
    }

    private void OnWatchlistSelected(object sender, SelectionChangedEventArgs e)
    {
        if (WatchlistList.SelectedItem is WatchlistResponse watchlist)
        {
            _ = _watchlistActivity?.LoadAsync(watchlist.Id);
        }
    }

    private void OnRadarFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_radar is null)
        {
            return;
        }

        _radar.Status = Selection(RadarStatusBox);
        _radar.Priority = Selection(RadarPriorityBox);
        _ = _radar.LoadAsync();
    }

    private void OnRadarSearchSubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_radar is null)
        {
            return;
        }

        _radar.Search = args.QueryText ?? string.Empty;
        _ = _radar.LoadAsync();
    }

    private void OnResearchFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_research is null)
        {
            return;
        }

        _research.Status = Selection(ResearchStatusBox);
        _ = _research.LoadAsync();
    }

    private void OnResearchSearchSubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_research is null)
        {
            return;
        }

        _research.Search = args.QueryText ?? string.Empty;
        _ = _research.LoadAsync();
    }

    private void OnResearchSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ResearchList.SelectedItem is ResearchCaseResponse researchCase)
        {
            _ = _researchCase?.LoadAsync(researchCase.Id);
        }
    }

    /// <summary>
    /// Fills the picker with whichever kind of counterparty is selected.
    /// </summary>
    /// <remarks>
    /// A relationship view is about a person or a company, so the picker holds real
    /// records rather than asking for an identifier. A screen that wanted a GUID
    /// typed in would be a screen nobody uses.
    /// </remarks>
    private async Task LoadRelationshipSubjectsAsync()
    {
        try
        {
            if (_api is null)
            {
                return;
            }

            if (Selection(RelationshipKindBox) == "Company")
            {
                IReadOnlyList<CompanySummaryResponse> companies =
                    await _api.ListCompaniesAsync().ConfigureAwait(true);

                RelationshipSubjectBox.ItemsSource =
                    companies.Select(x => new Counterparty(x.Id, x.Name)).ToList();
            }
            else
            {
                IReadOnlyList<PersonSummaryResponse> people =
                    await _api.ListPeopleAsync().ConfigureAwait(true);

                RelationshipSubjectBox.ItemsSource =
                    people.Select(x => new Counterparty(x.Id, x.DisplayName)).ToList();
            }
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = "Could not load the subjects";

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    private void OnRelationshipKindChanged(object sender, SelectionChangedEventArgs e) =>
        _ = LoadRelationshipSubjectsAsync();

    private void OnRelationshipRequested(object sender, RoutedEventArgs e)
    {
        if (_relationship is null
            || RelationshipSubjectBox.SelectedItem is not Counterparty chosen)
        {
            return;
        }

        _ = _relationship.LoadAsync(
            Selection(RelationshipKindBox) ?? "Person", chosen.Id);
    }

    /// <summary>A person or a company, as the picker shows them.</summary>
    private sealed record Counterparty(Guid Id, string DisplayName);

    // -------------------------------------------------------------- helpers

    private Task NoteAsync(string title, string message) =>
        new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        }.ShowAsync().AsTask();

    private void ShowError(string? message)
    {
        PageError.IsOpen = !string.IsNullOrEmpty(message);
        PageError.Message = message ?? string.Empty;
    }

    private static void Show(ProgressBar bar, bool visible) =>
        bar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static string Count(string label, int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{label} ({count})");

    private static string Percent(decimal? value) =>
        value is { } number
            ? (number * 100m).ToString("0.#", CultureInfo.CurrentCulture) + "%"
            : "—";

    /// <summary>The tag of the selected item, or null for the "any" entry.</summary>
    private static string? Selection(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } tag ? tag : null;
    /// <summary>
    /// Runs a command and shows the server's reason if it refuses.
    /// </summary>
    /// <remarks>
    /// These commands are dispatched and not awaited, so an unhandled refusal was
    /// simply lost: the dialog closed, nothing was created, and the operator was
    /// told nothing. Reproduced as an observer, whose thesis create answers 403.
    /// </remarks>
    private async Task Guarded(string title, Func<Task> command)
    {
        try
        {
            PageError.IsOpen = false;

            await command().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            PageError.Title = title;

            // The server's own explanation, not the problem's title (AOS-R002-024).
            ShowError(failure.Detail ?? failure.Message);
        }
    }

    /// <summary>
    /// Fills the workspace's authoring launcher from the command registry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AOS-R002-003</c>. Every authoring command here was reachable only by
    /// opening the command palette and knowing what to type — thirteen dialogs and
    /// one button in the whole workspace, which was for something else.
    /// </para>
    /// <para>
    /// Built from <see cref="CommandRegistry"/> rather than written out, so the
    /// launcher cannot drift from the palette: both dispatch the same identifier
    /// through the same <see cref="Execute"/>, and a command added later appears
    /// here without anybody remembering to add it. The palette is unchanged — it
    /// stays the accelerator, and this is the way somebody finds these at all.
    /// </para>
    /// </remarks>
    private void BuildAuthoringMenu()
    {
        foreach (CommandDefinition command in CommandRegistry.Default.Commands
            .Where(x => x.Action == CommandActionKind.Invoke
                && string.Equals(x.Workspace, "intelligence", StringComparison.Ordinal)

                // Authoring only. The workspace's own "go." commands are dispatched
                // here too, but a menu called New does not offer to open a tab.
                && !x.Id.StartsWith("go.", StringComparison.Ordinal))
            .OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            string id = command.Id;

            MenuFlyoutItem item = new() { Text = command.Label };

            // The label is what a screen reader announces, never the definition.
            AutomationProperties.SetName(item, command.Label);

            item.Click += (_, _) => Execute(id);

            AuthorMenu.Items.Add(item);
        }
    }

}
