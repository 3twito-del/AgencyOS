using System;
using System.Globalization;
using System.Threading.Tasks;
using AgencyOS.Client.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// What has reached the server, what has not, and what needs a decision.
/// </summary>
/// <remarks>
/// <para>
/// Everything is stated rather than implied. A user working offline needs to know
/// that they are, how stale the screen is, and exactly which of their changes the
/// server has never seen. Hiding that is how an application loses somebody's
/// afternoon.
/// </para>
/// <para>
/// A conflict is presented as a decision, not an error: what the user meant and
/// what the record now says, with neither applied until they choose. Discarding
/// only ever happens because somebody asked.
/// </para>
/// </remarks>
public sealed partial class SyncPage : Page, IPaletteCommandTarget
{
    private readonly SyncStatusViewModel? _viewModel;

    public SyncPage()
    {
        InitializeComponent();

        if (AppServices.Sync is { } engine && AppServices.Cache is { } cache)
        {
            _viewModel = new SyncStatusViewModel(engine, cache);
            _viewModel.PropertyChanged += (_, _) => Render();

            QueueList.ItemsSource = _viewModel.Pending;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _viewModel?.Refresh();
        Render();
    }

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "sync.now":
                _ = SyncAsync();
                break;

            case "cache.reset":
                _ = ResetAsync();
                break;

            case "view.refresh":
                _viewModel?.Refresh();
                Render();
                break;

            default:
                break;
        }
    }

    private void OnSyncClick(object sender, RoutedEventArgs e) => _ = SyncAsync();

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.Refresh();
        Render();
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => _ = ResetAsync();

    private void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || QueueList.SelectedItem is not PendingChangeItem item)
        {
            return;
        }

        _viewModel.Discard(item.Id);
        Render();
    }

    private async Task SyncAsync()
    {
        if (_viewModel is null)
        {
            Render();
            return;
        }

        await _viewModel.SynchronizeAsync().ConfigureAwait(true);
        Render();
    }

    /// <summary>
    /// Deletes the cache after saying, in numbers, what goes with it.
    /// </summary>
    /// <remarks>
    /// The confirmation names the count of unsent changes. "This cannot be undone"
    /// is not enough when the thing being discarded is work the user did and the
    /// server has never seen.
    /// </remarks>
    private async Task ResetAsync()
    {
        int outstanding = _viewModel?.OutstandingCount ?? 0;
        int attention = _viewModel?.AttentionCount ?? 0;
        int unsent = outstanding + attention;

        string message = unsent == 0
            ? "The cache will be rebuilt from the server the next time you synchronize. "
                + "Nothing will be lost: it holds no data that is not on the server."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{unsent} change(s) have not reached the server and will be lost permanently. Everything else will be rebuilt from the server on the next synchronization.");

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = "Reset the local cache?",
            Content = message,
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        AppServices.ResetCache();

        CacheBar.Title = "The local cache was reset";
        CacheBar.Message = "Restart AgencyOS to rebuild it from the server.";
        CacheBar.Severity = InfoBarSeverity.Informational;
        CacheBar.IsOpen = true;
    }

    /// <summary>Reflects connection, staleness, queue and conflict state explicitly.</summary>
    private void Render()
    {
        if (_viewModel is null)
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Message = AppServices.CacheFailure ?? AppServices.Settings.Describe();

            CacheBar.IsOpen = AppServices.CacheFailure is not null;
            ResetButton.IsEnabled = false;
            return;
        }

        Busy.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        StatusBar.Message = _viewModel.StatusLine;
        StatusBar.Severity = _viewModel.Connection switch
        {
            ConnectionState.Online => InfoBarSeverity.Success,
            ConnectionState.Offline => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Informational,
        };

        ConflictBar.IsOpen = _viewModel.HasConflicts;
        QueueEmpty.IsOpen = _viewModel.IsEmpty;

        CacheText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Cached: {_viewModel.CachedPeople} people, {_viewModel.CachedCompanies} companies, {_viewModel.CachedTasks} tasks. Change-feed position {_viewModel.Cursor}. The cache is never canonical; the server is.");
    }
}
