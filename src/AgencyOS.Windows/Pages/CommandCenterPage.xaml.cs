using System;
using System.Globalization;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The operational picture, backed entirely by the server's Command Center query.
/// </summary>
/// <remarks>
/// Nothing on this screen is computed locally. The buckets, the counts and the
/// ordering all come from the domain, so what the user sees is what the system
/// believes rather than a client-side approximation of it.
/// </remarks>
public sealed partial class CommandCenterPage : Page, IPaletteCommandTarget
{
    private readonly CommandCenterViewModel? _viewModel;

    public CommandCenterPage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _viewModel = new CommandCenterViewModel(api);
            _viewModel.PropertyChanged += (_, _) => Render();

            OverdueList.ItemsSource = _viewModel.Overdue;
            DueSoonList.ItemsSource = _viewModel.DueSoon;
            UnscheduledList.ItemsSource = _viewModel.Unscheduled;
            RecentList.ItemsSource = _viewModel.RecentInteractions;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    public void Execute(string commandId)
    {
        if (commandId == "view.refresh")
        {
            _ = LoadAsync();
        }
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        if (_viewModel is null)
        {
            ShowUnconfigured();
            return;
        }

        await _viewModel.LoadAsync().ConfigureAwait(true);
        Render();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private async void OnCompleteClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        TaskResponse? task = OverdueList.SelectedItem as TaskResponse
            ?? DueSoonList.SelectedItem as TaskResponse
            ?? UnscheduledList.SelectedItem as TaskResponse;

        if (task is null)
        {
            return;
        }

        await _viewModel.CompleteAsync(task.Id).ConfigureAwait(true);
        Render();
    }

    private void ShowUnconfigured()
    {
        ErrorBar.Message = AppServices.Settings.Describe();
        ErrorBar.IsOpen = true;
    }

    /// <summary>Reflects loading, error and empty state explicitly, never by inference.</summary>
    private void Render()
    {
        if (_viewModel is null)
        {
            return;
        }

        Busy.Visibility = _viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        ErrorBar.IsOpen = _viewModel.HasError;
        ErrorBar.Message = _viewModel.ErrorMessage ?? string.Empty;

        EmptyBar.IsOpen = _viewModel.IsEmpty;

        OpenTasksText.Text = _viewModel.OpenTaskCount.ToString(CultureInfo.CurrentCulture);
        PeopleCountText.Text = _viewModel.PeopleCount.ToString(CultureInfo.CurrentCulture);
        CompanyCountText.Text = _viewModel.CompanyCount.ToString(CultureInfo.CurrentCulture);
    }
}
