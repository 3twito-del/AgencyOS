using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The market workspace: every pursuit, and one pursuit's targets and activity.
/// </summary>
/// <remarks>
/// <para>
/// A dense list rather than a board. A board shows stage and hides the rest, and
/// the questions an agent actually has - what is overdue, what has had no reply -
/// are ones a board has nowhere to put.
/// </para>
/// <para>
/// The submission surface says plainly that AgencyOS records rather than sends.
/// Nothing here transmits anything, and a screen that implied otherwise would be a
/// lie the user acts on (ADR-0020).
/// </para>
/// </remarks>
public sealed partial class PipelinePage : Page, IPaletteCommandTarget, IRecordTarget
{
    private readonly OpportunityListViewModel? _list;
    private readonly OpportunityDetailViewModel? _detail;

    public PipelinePage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _list = new OpportunityListViewModel(api);
            _list.PropertyChanged += (_, _) => Render();

            _detail = new OpportunityDetailViewModel(api);
            _detail.PropertyChanged += (_, _) => RenderDetail();

            OpportunityList.ItemsSource = _list.Opportunities;
            TargetList.ItemsSource = _detail.Targets;
            SubmissionList.ItemsSource = _detail.Submissions;
            PitchList.ItemsSource = _detail.Pitches;
            SubjectList.ItemsSource = _detail.Subjects;
            TaskList.ItemsSource = _detail.Tasks;
            HistoryList.ItemsSource = _detail.History;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "view.refresh":
                _ = LoadAsync();
                break;

            case "opportunity.create":
                _ = CreateAsync();
                break;

            case "opportunity.target.add":
                _ = AddTargetAsync();
                break;

            case "submission.record":
                _ = RecordSubmissionAsync();
                break;

            case "pitch.record":
                _ = RecordPitchAsync();
                break;

            case "target.response.record":
                _ = MoveTargetAsync();
                break;

            case "go.overdue":
                ShowOverdue();
                break;

            default:
                break;
        }
    }

    private async Task LoadAsync()
    {
        if (_list is null)
        {
            ListError.Title = LoadFailureTitle;
            ListError.Message = AppServices.Settings.Describe();
            ListError.IsOpen = true;
            return;
        }

        _list.Status = SelectedTag(StatusBox);
        _list.Kind = SelectedTag(KindBox);
        _list.AwaitingResponse = AwaitingBox.IsChecked == true;
        _list.Search = SearchBox.Text ?? string.Empty;

        await _list.LoadAsync().ConfigureAwait(true);

        Render();
        SelectRevealed();
    }

    /// <summary>The record another workspace asked this page to open on.</summary>
    private Guid? _reveal;

    /// <summary>
    /// Set while <see cref="Reveal"/> widens the filters, so the selection changes it
    /// makes do not each start a load of their own.
    /// </summary>
    /// <remarks>
    /// Setting the status and kind boxes raises their SelectionChanged. Each used to
    /// start a load against a half-widened filter; that load could finish without the
    /// pursuit, and <see cref="SelectRevealed"/> would then report it missing and drop
    /// the request before the fully widened load arrived.
    /// </remarks>
    private bool _widening;

    /// <inheritdoc />
    /// <remarks>
    /// The filters are widened first, because an operator arriving from a project
    /// is asking for one pursuit and not for whatever this page was last filtered
    /// to.
    /// </remarks>
    public void Reveal(Guid record)
    {
        _reveal = record;
        _widening = true;

        try
        {
            // The "any" items by name. Finding them by an empty Tag did not select
            // anything in the shipped client (the 2d83c8f release candidate stayed on
            // Active and the new Draft stayed hidden), so nothing here depends on how
            // an empty Tag reaches the runtime. They carry no Tag, which SelectedTag
            // reads as no filter.
            StatusBox.SelectedItem = AnyStatusItem;
            KindBox.SelectedItem = AnyKindItem;
            AwaitingBox.IsChecked = false;
            SearchBox.Text = string.Empty;
        }
        finally
        {
            _widening = false;
        }

        // One load, against the filters in their final widened state; it alone
        // settles the reveal.
        _ = LoadAsync();
    }

    /// <summary>Selects the requested record, once its row exists.</summary>
    private void SelectRevealed()
    {
        if (_reveal is not { } wanted || _list is null)
        {
            return;
        }

        if (_list.Opportunities.FirstOrDefault(x => x.Id == wanted) is not { } row)
        {
            _reveal = null;

            // A load that failed is already on the bar under its own title; its rows
            // say nothing about whether the pursuit exists.
            if (_list.HasError)
            {
                return;
            }

            // The list did load, so this is not a load failure and must not be titled
            // as one: only the pursuit asked for is missing from what came back.
            ListError.Title = RevealFailureTitle;
            ListError.Message =
                "The pipeline loaded, but the pursuit asked for is not among its rows. Search for it by name.";
            ListError.IsOpen = true;

            return;
        }

        _reveal = null;

        OpportunityList.SelectedItem = row;
        OpportunityList.ScrollIntoView(row);
    }

    /// <summary>The list bar's title when the list itself could not be loaded.</summary>
    private const string LoadFailureTitle = "Could not load the pipeline";

    /// <summary>The list bar's title when the list loaded without the pursuit asked for.</summary>
    private const string RevealFailureTitle = "Could not reveal the pursuit";

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        // Raised by the parser as well, for the status box's starting selection, before
        // the list exists; and by Reveal while it widens, which loads once afterwards.
        if (_list is null || _widening)
        {
            return;
        }

        _ = LoadAsync();
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = LoadAsync();

    private void OnOpportunitySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_detail is null || OpportunityList.SelectedItem is not OpportunitySummaryResponse selected)
        {
            return;
        }

        _ = _detail.LoadAsync(selected.Id);
    }

    private void OnTargetSelected(object sender, SelectionChangedEventArgs e) => RenderDetail();

    private void OnNewClick(object sender, RoutedEventArgs e) => _ = CreateAsync();

    private void OnActivateClick(object sender, RoutedEventArgs e) => _ = ActivateAsync();

    private void OnAddTargetClick(object sender, RoutedEventArgs e) => _ = AddTargetAsync();

    private void OnRecordSubmissionClick(object sender, RoutedEventArgs e) => _ = RecordSubmissionAsync();

    private void OnRecordPitchClick(object sender, RoutedEventArgs e) => _ = RecordPitchAsync();

    private void OnMoveTargetClick(object sender, RoutedEventArgs e) => _ = MoveTargetAsync();

    private async Task CreateAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        IReadOnlyList<ProjectSummaryResponse> projects = [];
        IReadOnlyList<PackageSummaryResponse> packages = [];
        IReadOnlyList<TalentSummaryResponse> talent = [];
        IReadOnlyList<OrganizationMemberResponse> members = [];


        await Guarded(async () =>
        {
            projects = await api.ListProjectsAsync().ConfigureAwait(true);
            packages = await api.ListPackagesAsync().ConfigureAwait(true);
            talent = await api.ListTalentAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        if (projects.Count == 0 && packages.Count == 0 && talent.Count == 0)
        {
            DetailNotice(
                "Nothing to pursue yet",
                "A pursuit is about a project, a package or a talent profile, and this "
                    + "organization holds none of them yet. Create one first.");

            return;
        }

        // The owner is chosen from this organization's people rather than typed
        // as an identifier (AOS-R001-006).
        await Guarded(async () =>
                members = await api.ListOrganizationMembersAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        CreateOpportunityDialog dialog = new(api, projects, packages, talent, members)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        OpportunityDetailResponse? created = null;

        await Guarded(async () =>
                created = await api.CreateOpportunityAsync(dialog.ToRequest(), Guid.NewGuid().ToString("N"))
                    .ConfigureAwait(true))
            .ConfigureAwait(true);

        // The new pursuit is a Draft, and the list defaults to Active: reloading
        // as it was would hide exactly what the operator just made. Reveal widens
        // the filters and selects it, still a Draft, where Activate is offered.
        if (created is { } made)
        {
            Reveal(made.Opportunity.Id);
        }
        else
        {
            await LoadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Activates the selected Draft pursuit, and shows the same pursuit, now Active.
    /// </summary>
    /// <remarks>
    /// The view model sends the existing status command and reloads what the server
    /// holds; a refusal is shown as the domain's own sentence and changes nothing
    /// here. Only when the server accepted it and the pursuit was then read back is it
    /// revealed again and announced as active, so the row and its standing both say
    /// Active rather than the Draft the operator last saw. When the command was
    /// accepted but that read failed, the page says so and asks for a refresh: it
    /// does not claim Active, does not call it a refusal, and sends nothing again.
    /// </remarks>
    private async Task ActivateAsync()
    {
        if (_detail?.Opportunity is not { } opportunity || !_detail.CanActivate)
        {
            return;
        }

        Guid id = opportunity.Opportunity.Id;
        OpportunityActivation outcome = OpportunityActivation.NotSent;

        await Guarded(async () => outcome = await _detail.ActivateAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        if (outcome == OpportunityActivation.AcceptedNotRefreshed)
        {
            DetailWarning(
                "Activation accepted, not refreshed",
                $"The server accepted the activation of {opportunity.Opportunity.Name}, but the pursuit could not be refreshed. Refresh before continuing.");

            return;
        }

        if (outcome != OpportunityActivation.Activated)
        {
            return;
        }

        Reveal(id);

        DetailNotice(
            "Opportunity activated",
            $"{opportunity.Opportunity.Name} is active. Targets can now be moved and negotiations opened.");
    }

    private async Task AddTargetAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Opportunity is not { } opportunity)
        {
            return;
        }

        IReadOnlyList<PersonSummaryResponse> people = [];
        IReadOnlyList<CompanySummaryResponse> companies = [];

        await Guarded(async () =>
        {
            people = await api.ListPeopleAsync().ConfigureAwait(true);
            companies = await api.ListCompaniesAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        if (people.Count == 0 && companies.Count == 0)
        {
            DetailNotice(
                "Nobody to pursue yet",
                "A target is a company or a person this organization already holds a record "
                    + "for, and there are none. Create the company or person first.");

            return;
        }

        AddOpportunityTargetDialog dialog = new(people, companies) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.AddOpportunityTargetAsync(
                opportunity.Opportunity.Id,
                dialog.ToRequest(opportunity.Opportunity.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(opportunity.Opportunity.Id).ConfigureAwait(true);
    }

    private async Task RecordSubmissionAsync()
    {
        if (AppServices.Api is not { } api
            || _detail?.Opportunity is not { } opportunity
            || SelectedTarget() is not { } target)
        {
            DetailError("Select a target first, then record what went to them.");
            return;
        }

        IReadOnlyList<EntityChoice> materials =
            await SubjectMaterialsAsync(api, opportunity).ConfigureAwait(true);

        RecordSubmissionDialog dialog = new(target.DisplayName, materials)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.RecordSubmissionAsync(
                target.Id, dialog.ToRequest(target.Version), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(opportunity.Opportunity.Id).ConfigureAwait(true);
    }

    private async Task RecordPitchAsync()
    {
        if (AppServices.Api is not { } api
            || _detail?.Opportunity is not { } opportunity
            || SelectedTarget() is not { } target)
        {
            DetailError("Select a target first, then record the pitch.");
            return;
        }

        IReadOnlyList<EntityChoice> materials =
            await SubjectMaterialsAsync(api, opportunity).ConfigureAwait(true);

        RecordPitchDialog dialog = new(target, materials) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.RecordPitchAsync(
                target.Id, dialog.ToRequest(target.Version), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(opportunity.Opportunity.Id).ConfigureAwait(true);
    }

    private async Task MoveTargetAsync()
    {
        if (AppServices.Api is not { } api
            || _detail?.Opportunity is not { } opportunity
            || SelectedTarget() is not { } target)
        {
            DetailError("Select a target first.");
            return;
        }

        MoveTargetDialog dialog = new(target.Stage) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.MoveOpportunityTargetAsync(
                target.Id, dialog.ToRequest(target.Version), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(opportunity.Opportunity.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Narrows the list to what is actually late.</summary>
    private void ShowOverdue()
    {
        AwaitingBox.IsChecked = true;
        _ = LoadAsync();
    }

    private OpportunityTargetResponse? SelectedTarget() =>
        TargetList.SelectedItem as OpportunityTargetResponse;

    /// <summary>
    /// The materials belonging to the people this pursuit is about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Materials hang off a person, and a pursuit names its subjects — so the
    /// smallest correct source is the materials of those subjects, not every
    /// material in the organization (§9). A pursuit has a handful of subjects, so
    /// this is a handful of requests rather than a scan.
    /// </para>
    /// <para>
    /// A subject that is not a person has no materials and is skipped rather than
    /// requested: a package is not somebody whose script one sends.
    /// </para>
    /// <para>
    /// The person behind a subject is looked up, not assumed: a pursuit names talent
    /// by profile and materials are listed by person (<c>AOS-R002-023</c>). The
    /// roster is the one the create dialog already reads.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<EntityChoice>> SubjectMaterialsAsync(
        IAgencyOsApi api,
        OpportunityDetailResponse opportunity)
    {
        if (!SubjectMaterials.NamesTalent(opportunity.Subjects))
        {
            return [];
        }

        IReadOnlyList<Guid> people;

        try
        {
            people = SubjectMaterials.People(
                opportunity.Subjects, await api.ListTalentAsync().ConfigureAwait(true));
        }
        catch (AgencyOsApiException)
        {
            // A caller who may not read the roster may not read its materials
            // either. The picker then offers "nothing", which is still true.
            return [];
        }

        List<MaterialResponse> materials = [];

        foreach (Guid person in people)
        {
            try
            {
                materials.AddRange(
                    await api.ListMaterialsAsync(person).ConfigureAwait(true));
            }
            catch (AgencyOsApiException)
            {
                // One subject the caller may not read does not empty the picker for
                // the others. The server decides what comes back; this only asks.
            }
        }

        return EntityChoice.ForMaterials(materials);
    }

    /// <summary>Says why a workflow cannot start, without calling it a failure.</summary>
    private void DetailNotice(string title, string message)
    {
        DetailBar.Title = title;
        DetailBar.Message = message;
        DetailBar.Severity = InfoBarSeverity.Informational;
        DetailBar.IsOpen = true;
    }

    /// <summary>Says something happened that the page could not then confirm.</summary>
    private void DetailWarning(string title, string message)
    {
        DetailBar.Title = title;
        DetailBar.Message = message;
        DetailBar.Severity = InfoBarSeverity.Warning;
        DetailBar.IsOpen = true;
    }

    /// <summary>Runs a call and shows the server's own explanation if it refuses.</summary>
    /// <remarks>
    /// The message comes from the server. When a submission is refused because the
    /// pursuit was closed last week, the useful sentence is the one the domain
    /// wrote.
    /// </remarks>
    private async Task Guarded(Func<Task> action)
    {
        try
        {
            DetailBar.IsOpen = false;

            await action().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            // The sentence the domain wrote, not the problem's title. This page's
            // own remark says the useful sentence is the one the domain wrote; it
            // was showing the other one (AOS-R002-024).
            DetailError(failure.Detail ?? failure.Message);
        }
    }

    private void DetailError(string message)
    {
        DetailBar.Title = "That did not happen";
        DetailBar.Message = message;
        DetailBar.Severity = InfoBarSeverity.Error;
        DetailBar.IsOpen = true;
    }

    private void Render()
    {
        if (_list is null)
        {
            return;
        }

        ListBusy.Visibility = _list.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        // The notice asserts an absence, so it waits on the same authority the
        // count does: a list that loaded empty and then failed to refresh is
        // still empty and no longer known.
        ListEmpty.IsOpen = SummaryAuthority.Knows(_list) && _list.IsEmpty;

        // Restated on every render, so a load failure never shows under the title a
        // missed reveal left behind.
        ListError.Title = LoadFailureTitle;
        ListError.IsOpen = _list.HasError;
        ListError.Message = _list.ErrorMessage ?? string.Empty;

        SummaryText.Text = SummaryAuthority.Of(
            () => string.Create(
                CultureInfo.InvariantCulture,
                $"{_list.Opportunities.Count} pursuit(s); {_list.Overdue} overdue, {_list.Waiting} awaiting a reply."),
            _list);
    }

    private void RenderDetail()
    {
        if (_detail is null)
        {
            return;
        }

        bool loaded = _detail.Opportunity is not null;
        bool hasTarget = SelectedTarget() is not null;

        AddTargetButton.IsEnabled = loaded;
        SubmissionButton.IsEnabled = loaded && hasTarget;
        PitchButton.IsEnabled = loaded && hasTarget;
        StageButton.IsEnabled = loaded && hasTarget;
        ActivateButton.Visibility = _detail.CanActivate ? Visibility.Visible : Visibility.Collapsed;

        if (_detail.Opportunity is not { } opportunity)
        {
            DetailTitle.Text = "Select an opportunity";
            DetailStanding.Text = string.Empty;
            StrategyText.Visibility = Visibility.Collapsed;
            OverdueBar.IsOpen = false;
            return;
        }

        DetailTitle.Text = opportunity.Opportunity.Name;
        DetailStanding.Text = _detail.Standing;

        // Nothing is shown when the strategy is absent, and absent is
        // indistinguishable from empty by design.
        StrategyText.Visibility = _detail.HasStrategy ? Visibility.Visible : Visibility.Collapsed;
        StrategyText.Text = opportunity.StrategyNotes ?? string.Empty;

        string[] overdue = [.. _detail.Overdue.Select(x => x.DisplayName)];

        OverdueBar.IsOpen = overdue.Length > 0;
        OverdueBar.Message = overdue.Length == 0 ? string.Empty : string.Join(", ", overdue);
    }

    private static string? SelectedTag(ComboBox box)
    {
        string? tag = (box.SelectedItem as ComboBoxItem)?.Tag as string;

        return string.IsNullOrWhiteSpace(tag) ? null : tag;
    }
}
