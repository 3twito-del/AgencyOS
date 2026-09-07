using System;
using System.Globalization;
using System.Threading.Tasks;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.SavedViews;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The user's own saved views.
/// </summary>
/// <remarks>
/// The editor exposes the closed filter set the server validates, not a query
/// box. A saved view is a named, reviewable filter document; letting the user
/// type a query would make the client the author of something the server has to
/// execute.
/// </remarks>
public sealed partial class SavedViewsPage : Page, IPaletteCommandTarget
{
    private readonly SavedViewsViewModel? _viewModel;

    public SavedViewsPage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _viewModel = new SavedViewsViewModel(api);
            _viewModel.PropertyChanged += (_, _) => Render();

            ViewList.ItemsSource = _viewModel.Views;
            ResultList.ItemsSource = _viewModel.Results;
        }

        TargetBox.SelectedIndex = 0;
        StatusBox.SelectedIndex = 0;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "view.refresh":
                _ = LoadAsync();
                break;

            case "view.save":
                _ = SaveAsync();
                break;

            case "view.run":
                _ = RunAsync();
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

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Selected = null;
        }

        ViewList.SelectedItem = null;
        NameBox.Text = string.Empty;
        TextContainsBox.Text = string.Empty;
        TitleContainsBox.Text = string.Empty;
        OverdueOnlyBox.IsChecked = false;
        TargetBox.SelectedIndex = 0;
        StatusBox.SelectedIndex = 0;

        Render();
    }

    private void OnViewSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || ViewList.SelectedItem is not SavedViewResponse view)
        {
            return;
        }

        _viewModel.Selected = view;
        _viewModel.Results.Clear();

        NameBox.Text = view.Name;
        SelectByTag(TargetBox, view.Target);
        SelectByTag(StatusBox, view.Definition.Filters?.Status ?? string.Empty);
        TextContainsBox.Text = view.Definition.Filters?.TextContains ?? string.Empty;
        TitleContainsBox.Text = view.Definition.Filters?.TitleContains ?? string.Empty;
        OverdueOnlyBox.IsChecked = view.Definition.Filters?.OverdueOnly ?? false;

        Render();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => _ = SaveAsync();

    private void OnRunClick(object sender, RoutedEventArgs e) => _ = RunAsync();

    /// <summary>
    /// Runs the selected view.
    /// </summary>
    /// <remarks>
    /// The server re-checks the target's read permission as it runs, so a view
    /// saved while a grant was held stops returning rows once that grant is
    /// revoked. The client neither decides that nor caches the answer.
    /// </remarks>
    private async Task RunAsync()
    {
        if (_viewModel?.Selected is not { } selected)
        {
            return;
        }

        await _viewModel.RunAsync(selected).ConfigureAwait(true);
        Render();
    }

    private async Task SaveAsync()
    {
        if (_viewModel is null || string.IsNullOrWhiteSpace(NameBox.Text))
        {
            return;
        }

        SavedViewDefinitionModel definition = BuildDefinition();

        if (_viewModel.Selected is { } existing)
        {
            // The version carried on the record the user is looking at. The
            // server refuses it if the view moved on, which is the whole point.
            await _viewModel
                .UpdateAsync(existing, NameBox.Text.Trim(), definition)
                .ConfigureAwait(true);
        }
        else
        {
            await _viewModel.CreateAsync(NameBox.Text.Trim(), definition).ConfigureAwait(true);
        }

        Render();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.Selected is not { } selected)
        {
            return;
        }

        await _viewModel.DeleteAsync(selected).ConfigureAwait(true);

        OnNewClick(sender, e);
    }

    private SavedViewDefinitionModel BuildDefinition()
    {
        string target = Tag(TargetBox) ?? "People";
        string status = Tag(StatusBox) ?? string.Empty;

        return new SavedViewDefinitionModel(
            SavedViewDefinitionVersion,
            target,
            new SavedViewFiltersModel(
                Status: string.IsNullOrEmpty(status) ? null : status,
                CompanyId: null,
                TitleContains: Empty(TitleContainsBox.Text),
                TextContains: Empty(TextContainsBox.Text),
                TaskState: null,
                DueWithinDays: null,
                OverdueOnly: OverdueOnlyBox.IsChecked ?? false));
    }

    /// <summary>
    /// The definition schema version this build writes.
    /// </summary>
    /// <remarks>
    /// Stated explicitly rather than left to a default, so a change to the filter
    /// format is a visible edit here and a rejected document on the server, not a
    /// silent reinterpretation of views saved months ago.
    /// </remarks>
    private const int SavedViewDefinitionVersion = 1;

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Tag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void SelectByTag(ComboBox box, string tag)
    {
        foreach (object item in box.Items)
        {
            if (item is ComboBoxItem entry && string.Equals(entry.Tag as string, tag, StringComparison.Ordinal))
            {
                box.SelectedItem = entry;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    /// <summary>Reflects loading, error, empty and conflict state explicitly.</summary>
    private void Render()
    {
        if (_viewModel is null)
        {
            return;
        }

        ListBusy.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        bool conflicted = _viewModel.ErrorMessage?.Contains("version", StringComparison.OrdinalIgnoreCase) ?? false;

        ConflictBar.IsOpen = conflicted;
        ListError.IsOpen = _viewModel.HasError && !conflicted;
        ListError.Message = _viewModel.ErrorMessage ?? string.Empty;
        ListEmpty.IsOpen = _viewModel.IsEmpty;

        DetailEmpty.IsOpen = _viewModel.Selected is null;
        DeleteButton.IsEnabled = _viewModel.Selected is not null;
        RunButton.IsEnabled = _viewModel.Selected is not null;
        NoResultsBar.IsOpen = _viewModel.HasNoResults;
        SaveButton.Content = _viewModel.Selected is null ? "Create" : "Save";

        VersionText.Text = _viewModel.Selected is { } selected
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Version {selected.Version} · definition format v{selected.DefinitionVersion} · updated {selected.UpdatedAt:g}")
            : string.Empty;
    }
}
