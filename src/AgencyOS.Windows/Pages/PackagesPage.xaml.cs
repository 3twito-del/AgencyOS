using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Projects;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The package workspace: what is attached, what is proposed, and what is missing.
/// </summary>
/// <remarks>
/// <para>
/// Attached and proposed are separate columns rather than one list with a badge.
/// A package holds facts and hopes side by side, and a single list would eventually
/// be read as a roster - which is exactly the confusion the model exists to prevent
/// (ADR-0019).
/// </para>
/// <para>
/// Strategy is shown only when the server sent it. A caller without
/// <c>packages.strategy.read</c> gets the field absent, and the surface shows
/// nothing at all rather than a "hidden" placeholder, because a placeholder would
/// leak that a note exists.
/// </para>
/// </remarks>
public sealed partial class PackagesPage : Page, IPaletteCommandTarget
{
    private readonly PackageListViewModel? _list;
    private readonly PackageViewModel? _detail;

    public PackagesPage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _list = new PackageListViewModel(api);
            _list.PropertyChanged += (_, _) => Render();

            _detail = new PackageViewModel(api);
            _detail.PropertyChanged += (_, _) => RenderDetail();

            PackageList.ItemsSource = _list.Packages;
            AttachedList.ItemsSource = _detail.Attached;
            ProposedList.ItemsSource = _detail.Proposed;
            GapList.ItemsSource = _detail.Gaps;
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

            case "package.create":
                _ = CreatePackageAsync();
                break;

            case "package.element.add":
                _ = AddElementAsync();
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

        await _list.LoadAsync().ConfigureAwait(true);

        Render();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnPackageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_detail is null || PackageList.SelectedItem is not PackageSummaryResponse selected)
        {
            return;
        }

        _ = _detail.LoadAsync(selected.Id);
    }

    private void OnNewPackageClick(object sender, RoutedEventArgs e) => _ = CreatePackageAsync();

    private void OnChangeStatusClick(object sender, RoutedEventArgs e) => _ = ChangeStatusAsync();

    private void OnAddElementClick(object sender, RoutedEventArgs e) => _ = AddElementAsync();

    private async Task CreatePackageAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        IReadOnlyList<ProjectSummaryResponse> projects = [];
        IReadOnlyList<OrganizationMemberResponse> members = [];


        await Guarded(async () =>
                projects = await api.ListProjectsAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        if (projects.Count == 0)
        {
            DetailNotice(
                "No projects yet",
                "A package is built from a project, chosen from the projects this "
                    + "organization holds. There are none yet - create the project first.");

            return;
        }

        // The owner is chosen from this organization's people rather than typed
        // as an identifier (AOS-R001-006).
        await Guarded(async () =>
                members = await api.ListOrganizationMembersAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        CreatePackageDialog dialog = new(projects, members) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.CreatePackageAsync(dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task ChangeStatusAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Package is not { } package)
        {
            return;
        }

        ChangePackageStatusDialog dialog = new(package.Package.Status) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.ChangePackageStatusAsync(
                package.Package.Id,
                dialog.ToRequest(package.Package.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(package.Package.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task AddElementAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Package is not { } package)
        {
            return;
        }

        // Everything an element can point at comes from the package's own project,
        // plus this organization's people and companies. One read of the project
        // rather than six tenant-wide lists (SS9).
        PackageElementSources? sources = null;

        await Guarded(async () =>
        {
            ProjectDetailResponse project = await api
                .GetProjectAsync(package.Package.ProjectId).ConfigureAwait(true);

            IReadOnlyList<PersonSummaryResponse> people =
                await api.ListPeopleAsync().ConfigureAwait(true);

            IReadOnlyList<CompanySummaryResponse> companies =
                await api.ListCompaniesAsync().ConfigureAwait(true);

            sources = PackageElementSources.From(project, people, companies);
        }).ConfigureAwait(true);

        if (sources is null)
        {
            return;
        }

        AddPackageElementDialog dialog = new(sources) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.AddPackageElementAsync(
                package.Package.Id,
                dialog.ToRequest(package.Package.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(package.Package.Id).ConfigureAwait(true);
    }

    /// <summary>Says why a workflow cannot start, without calling it a failure.</summary>
    private void DetailNotice(string title, string message)
    {
        DetailBar.Title = title;
        DetailBar.Message = message;
        DetailBar.Severity = InfoBarSeverity.Informational;
        DetailBar.IsOpen = true;
    }

    /// <summary>Runs a call and shows the server's own explanation if it refuses.</summary>
    private async Task Guarded(Func<Task> action)
    {
        try
        {
            DetailBar.IsOpen = false;

            await action().ConfigureAwait(true);
        }
        catch (AgencyOsApiException failure)
        {
            DetailBar.Title = "That did not happen";
            // The server's own explanation, not the title of the problem
            // (AOS-R002-024).
            DetailBar.Message = failure.Detail ?? failure.Message;
            DetailBar.Severity = InfoBarSeverity.Error;
            DetailBar.IsOpen = true;
        }
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
    }

    private void RenderDetail()
    {
        if (_detail is null)
        {
            return;
        }

        bool loaded = _detail.Package is not null;

        StatusButton.IsEnabled = loaded;
        AddElementButton.IsEnabled = loaded;

        if (_detail.Package is not { } package)
        {
            DetailTitle.Text = "Select a package";
            DetailReadiness.Text = string.Empty;
            ThesisText.Text = string.Empty;
            StrategyText.Visibility = Visibility.Collapsed;
            return;
        }

        DetailTitle.Text = package.Package.Name;
        DetailReadiness.Text = _detail.Readiness;
        ThesisText.Text = package.Thesis ?? string.Empty;

        // Nothing is shown when the strategy is absent, and absent is
        // indistinguishable from empty by design.
        StrategyText.Visibility = _detail.HasStrategy ? Visibility.Visible : Visibility.Collapsed;
        StrategyText.Text = package.StrategyNotes ?? string.Empty;
    }

    private static string? SelectedTag(ComboBox box)
    {
        string? tag = (box.SelectedItem as ComboBoxItem)?.Tag as string;

        return string.IsNullOrWhiteSpace(tag) ? null : tag;
    }
}
