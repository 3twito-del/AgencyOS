using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Projects;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The slate: every project, and one project's whole working surface.
/// </summary>
/// <remarks>
/// <para>
/// Status and stage are changed by separate commands, never by editing a field.
/// They are different facts about a project and a form that let somebody set both
/// at once would lose which one they meant (ADR-0018).
/// </para>
/// <para>
/// No business logic here. The page reads view models and calls the API through
/// them; what is legal and what it means is decided on the server.
/// </para>
/// </remarks>
public sealed partial class ProjectsPage : Page, IPaletteCommandTarget
{
    private readonly ProjectListViewModel? _list;
    private readonly ProjectDetailViewModel? _detail;

    public ProjectsPage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _list = new ProjectListViewModel(api);
            _list.PropertyChanged += (_, _) => Render();

            _detail = new ProjectDetailViewModel(api);
            _detail.PropertyChanged += (_, _) => RenderDetail();

            ProjectList.ItemsSource = _list.Projects;
            RoleList.ItemsSource = _detail.Roles;
            CompanyList.ItemsSource = _detail.Companies;
            SourceList.ItemsSource = _detail.SourceProperties;
            MaterialList.ItemsSource = _detail.Materials;
            PackageList.ItemsSource = _detail.Packages;
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

            case "project.create":
                _ = CreateProjectAsync();
                break;

            case "project.role.add":
                _ = AddRoleAsync();
                break;

            case "project.attach":
                _ = AttachAsync();
                break;

            case "project.company.add":
                _ = AddCompanyAsync();
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
        _list.Stage = SelectedTag(StageBox);
        _list.MissingRole = SelectedTag(MissingRoleBox);
        _list.Search = SearchBox.Text ?? string.Empty;

        await _list.LoadAsync().ConfigureAwait(true);

        Render();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = LoadAsync();

    private void OnProjectSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_detail is null || ProjectList.SelectedItem is not ProjectSummaryResponse selected)
        {
            return;
        }

        _ = _detail.LoadAsync(selected.Id);
    }

    private void OnNewProjectClick(object sender, RoutedEventArgs e) => _ = CreateProjectAsync();

    private void OnAddRoleClick(object sender, RoutedEventArgs e) => _ = AddRoleAsync();

    private void OnAttachClick(object sender, RoutedEventArgs e) => _ = AttachAsync();

    private void OnAddCompanyClick(object sender, RoutedEventArgs e) => _ = AddCompanyAsync();

    private void OnChangeStageClick(object sender, RoutedEventArgs e) => _ = ChangeStageAsync();

    private async Task CreateProjectAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        CreateProjectDialog dialog = new() { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.CreateProjectAsync(dialog.ToRequest(), Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task AddRoleAsync()
    {
        if (AppServices.Api is not { } api
            || _detail?.Project is not { } project)
        {
            return;
        }

        AddProjectRoleDialog dialog = new() { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.CreateProjectRoleAsync(
                project.Project.Id,
                dialog.ToRequest(project.Project.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(project.Project.Id).ConfigureAwait(true);
    }

    private async Task AttachAsync()
    {
        if (AppServices.Api is not { } api
            || _detail?.Project is not { } project
            || RoleList.SelectedItem is not ProjectRoleResponse role)
        {
            DetailError("Select a role first, then attach somebody to it.");
            return;
        }

        AttachToRoleDialog dialog = new(role.Type, role.Label) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.AttachToRoleAsync(
                project.Project.Id,
                role.Id,
                dialog.ToRequest(project.Project.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(project.Project.Id).ConfigureAwait(true);
    }

    private async Task AddCompanyAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Project is not { } project)
        {
            return;
        }

        AddProjectCompanyDialog dialog = new() { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.AddProjectCompanyAsync(
                project.Project.Id,
                dialog.ToRequest(project.Project.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(project.Project.Id).ConfigureAwait(true);
    }

    private async Task ChangeStageAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Project is not { } project)
        {
            return;
        }

        ChangeProjectStageDialog dialog = new(project.Project.Stage) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await Guarded(() => api.ChangeProjectStageAsync(
                project.Project.Id,
                dialog.ToRequest(project.Project.Version),
                Guid.NewGuid().ToString("N")))
            .ConfigureAwait(true);

        await _detail.LoadAsync(project.Project.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Runs a call and shows the server's own explanation if it refuses.</summary>
    /// <remarks>
    /// The message comes from the server rather than being invented here. When a
    /// stage change is refused because the project was cancelled, the useful
    /// sentence is the one the domain wrote.
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
            $"{_list.Projects.Count} project(s); {_list.WithOpenRoles} with a role still to fill.");
    }

    private void RenderDetail()
    {
        if (_detail is null)
        {
            return;
        }

        bool loaded = _detail.Project is not null;

        AddRoleButton.IsEnabled = loaded;
        AttachButton.IsEnabled = loaded;
        AddCompanyButton.IsEnabled = loaded;
        StageButton.IsEnabled = loaded;

        if (_detail.Project is not { } project)
        {
            DetailTitle.Text = "Select a project";
            DetailStanding.Text = string.Empty;
            GapsBar.IsOpen = false;
            return;
        }

        DetailTitle.Text = project.Project.WorkingTitle is { Length: > 0 } working
            ? $"{project.Project.Title} ({working})"
            : project.Project.Title;

        DetailStanding.Text = _detail.Standing;

        // What the project still needs, said plainly rather than left to be
        // inferred from a list of roles.
        string[] gaps = [.. _detail.Gaps.Select(x => x.Label is { Length: > 0 } label ? $"{x.Type} ({label})" : x.Type)];

        GapsBar.IsOpen = gaps.Length > 0;
        GapsBar.Message = gaps.Length == 0 ? string.Empty : string.Join(", ", gaps);
    }

    private static string? SelectedTag(ComboBox box)
    {
        string? tag = (box.SelectedItem as ComboBoxItem)?.Tag as string;

        return string.IsNullOrWhiteSpace(tag) ? null : tag;
    }
}
