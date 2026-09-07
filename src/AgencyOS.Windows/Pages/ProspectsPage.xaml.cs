using System;
using System.Globalization;
using System.Threading.Tasks;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Representation;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The prospect pipeline and the conversion workflow.
/// </summary>
/// <remarks>
/// Stage changes are ordinary buttons; conversion is a dialog, because it creates
/// a representation and needs a start date, a lead and scopes. Treating them the
/// same would make signing a client one careless click.
/// </remarks>
public sealed partial class ProspectsPage : Page, IPaletteCommandTarget
{
    private readonly ProspectsViewModel? _viewModel;

    public ProspectsPage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _viewModel = new ProspectsViewModel(api);
            _viewModel.PropertyChanged += (_, _) => Render();

            ProspectList.ItemsSource = _viewModel.Prospects;
        }

        StageBox.SelectedIndex = 0;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "view.refresh":
                _ = LoadAsync();
                break;

            case "prospect.convert":
                _ = ConvertAsync();
                break;

            default:
                break;
        }
    }

    private async Task LoadAsync()
    {
        if (_viewModel is null)
        {
            ListError.Message = AppServices.Settings.Describe();
            ListError.IsOpen = true;
            return;
        }

        await _viewModel.LoadAsync().ConfigureAwait(true);
        Render();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.OpenOnly = OpenOnlyBox.IsChecked ?? true;
        _ = LoadAsync();
    }

    private void OnStageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        string? tag = (StageBox.SelectedItem as ComboBoxItem)?.Tag as string;

        _viewModel.Stage = string.IsNullOrEmpty(tag) ? null : tag;
        _ = LoadAsync();
    }

    private void OnProspectSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.Selected = ProspectList.SelectedItem as ProspectResponse;
        Render();
    }

    private void OnContactedClick(object sender, RoutedEventArgs e) => _ = AdvanceAsync("Contacted");

    private void OnCourtingClick(object sender, RoutedEventArgs e) => _ = AdvanceAsync("Courting");

    private void OnDeclinedClick(object sender, RoutedEventArgs e) => _ = AdvanceAsync("Declined");

    private async Task AdvanceAsync(string stage)
    {
        if (_viewModel?.Selected is not { } selected)
        {
            return;
        }

        await _viewModel
            .AdvanceAsync(selected, stage, DateOnly.FromDateTime(DateTime.UtcNow))
            .ConfigureAwait(true);

        Render();
    }

    private void OnConvertClick(object sender, RoutedEventArgs e) => _ = ConvertAsync();

    /// <summary>
    /// Converts the selected pursuit into a representation.
    /// </summary>
    /// <remarks>
    /// The view model generates the idempotency key before the first attempt, so a
    /// retry after a lost response replays the original answer rather than signing
    /// the client twice.
    /// </remarks>
    private async Task ConvertAsync()
    {
        if (_viewModel?.Selected is not { } selected || !_viewModel.CanConvert)
        {
            return;
        }

        ConvertProspectDialog dialog = new(selected.DisplayName) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        RepresentationResponse? representation = await _viewModel
            .ConvertAsync(
                selected,
                dialog.StartsOn,
                selected.OwnerUserId,
                dialog.Scopes,
                dialog.IsExclusive,
                dialog.Territory)
            .ConfigureAwait(true);

        Render();

        if (representation is not null)
        {
            ProspectLine.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Signed. {representation.DisplayName} is now a client, represented for {string.Join(", ", dialog.Scopes)}.");
        }
    }

    /// <summary>Reflects loading, error, empty and selection state explicitly.</summary>
    private void Render()
    {
        if (_viewModel is null)
        {
            return;
        }

        ListBusy.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ListError.IsOpen = _viewModel.HasError;
        ListError.Message = _viewModel.ErrorMessage ?? string.Empty;
        ListEmpty.IsOpen = _viewModel.IsEmpty;

        ProspectResponse? selected = _viewModel.Selected;

        DetailEmpty.IsOpen = selected is null;
        ClosedBar.IsOpen = selected is not null && !_viewModel.CanAdvance;

        ContactedButton.IsEnabled = _viewModel.CanAdvance;
        CourtingButton.IsEnabled = _viewModel.CanAdvance;
        DeclinedButton.IsEnabled = _viewModel.CanAdvance;
        ConvertButton.IsEnabled = _viewModel.CanConvert;

        if (selected is null)
        {
            ProspectName.Text = string.Empty;
            ProspectLine.Text = string.Empty;
            StrategyPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ProspectName.Text = selected.DisplayName;

        string follow = selected.NextFollowUpOn is { } due
            ? string.Create(CultureInfo.InvariantCulture, $"follow up {due:d}")
            : "no follow-up set";

        ProspectLine.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{selected.Stage} · identified {selected.IdentifiedOn:d} · owned by {selected.OwnerDisplayName ?? "nobody"} · {follow}.");

        // Strategy notes appear only when the server returned them. Withheld and
        // empty look identical, deliberately (ADR-0017).
        bool hasStrategy = !string.IsNullOrWhiteSpace(selected.StrategyNotes);

        StrategyPanel.Visibility = hasStrategy ? Visibility.Visible : Visibility.Collapsed;
        StrategyText.Text = selected.StrategyNotes ?? string.Empty;
    }
}
