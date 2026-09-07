using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Opportunities;
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
public sealed partial class PipelinePage : Page, IPaletteCommandTarget
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
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => _ = LoadAsync();

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

        CreateOpportunityDialog dialog = new() { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.CreateOpportunityAsync(dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task AddTargetAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Opportunity is not { } opportunity)
        {
            return;
        }

        AddOpportunityTargetDialog dialog = new() { XamlRoot = XamlRoot };

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

        RecordSubmissionDialog dialog = new(target.DisplayName) { XamlRoot = XamlRoot };

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

        RecordPitchDialog dialog = new(target) { XamlRoot = XamlRoot };

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
            DetailError(failure.Message);
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
        ListEmpty.IsOpen = _list.IsEmpty;

        ListError.IsOpen = _list.HasError;
        ListError.Message = _list.ErrorMessage ?? string.Empty;

        SummaryText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_list.Opportunities.Count} pursuit(s); {_list.Overdue} overdue, {_list.Waiting} awaiting a reply.");
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
