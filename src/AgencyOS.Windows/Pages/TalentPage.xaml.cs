using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Representation;
using AgencyOS.Windows.Dialogs;
using AgencyOS.Windows.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The talent and client workspace.
/// </summary>
/// <remarks>
/// <para>
/// One list with filters rather than separate Clients and Talent screens: being a
/// client is a state a person is in, and two screens would make a former client
/// look like a different person from the one the agency signed.
/// </para>
/// <para>
/// Internal positioning is shown only when the server returned it. A caller
/// without <c>talent.notes.read</c> receives the field absent, which is
/// deliberately indistinguishable from it being empty - so the screen says nothing
/// rather than hinting that something was withheld (ADR-0017).
/// </para>
/// </remarks>
public sealed partial class TalentPage : Page, IPaletteCommandTarget
{
    private readonly TalentListViewModel? _list;
    private readonly ClientOverviewViewModel? _overview;

    private Guid? _selectedPersonId;

    public TalentPage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _list = new TalentListViewModel(api);
            _list.PropertyChanged += (_, _) => RenderList();

            _overview = new ClientOverviewViewModel(api);
            _overview.PropertyChanged += (_, _) => RenderDetail();

            TalentList.ItemsSource = _list.Talent;
            CreditList.ItemsSource = _overview.Credits;
            MaterialList.ItemsSource = _overview.Materials;
            HistoryList.ItemsSource = _overview.History;
            TeamList.ItemsSource = _overview.Team;
        }

        DisciplineBox.SelectedIndex = 0;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "view.refresh":
                _ = LoadAsync();
                break;

            case "credit.add":
                _ = AddCreditAsync();
                break;

            case "material.add":
                _ = AddMaterialAsync();
                break;

            case "representation.status.change":
                _ = ChangeStatusAsync();
                break;

            case "representation.scope.change":
                _ = ChangeScopeAsync();
                break;

            case "representation.team.change":
                _ = ChangeTeamAsync();
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

        await _list.LoadAsync().ConfigureAwait(true);
        RenderList();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || _list is null)
        {
            return;
        }

        _list.Search = SearchBox.Text;
        e.Handled = true;

        _ = LoadAsync();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_list is null)
        {
            return;
        }

        _list.ClientsOnly = ClientsOnlyBox.IsChecked ?? false;
        _list.FormerClientsOnly = FormerOnlyBox.IsChecked ?? false;

        // The view model refuses the contradictory pair, so reflect what it settled on.
        ClientsOnlyBox.IsChecked = _list.ClientsOnly;
        FormerOnlyBox.IsChecked = _list.FormerClientsOnly;

        _ = LoadAsync();
    }

    private void OnDisciplineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_list is null)
        {
            return;
        }

        string? tag = (DisciplineBox.SelectedItem as ComboBoxItem)?.Tag as string;

        _list.Discipline = string.IsNullOrEmpty(tag) ? null : tag;

        _ = LoadAsync();
    }

    private void OnTalentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_overview is null || TalentList.SelectedItem is not TalentSummaryResponse selected)
        {
            return;
        }

        _selectedPersonId = selected.PersonId;

        _ = OpenAsync(selected.PersonId);
    }

    /// <summary>Opens somebody's working surface. Used by the palette and by search.</summary>
    public async Task OpenAsync(Guid personId)
    {
        if (_overview is null)
        {
            return;
        }

        _selectedPersonId = personId;

        await _overview.LoadAsync(personId).ConfigureAwait(true);
        RenderDetail();
    }

    private void OnChangeScopeClick(object sender, RoutedEventArgs e) => _ = ChangeScopeAsync();

    /// <summary>
    /// Begins or ends representing an area.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R001-010</c>. The two commands have existed on the server since M4
    /// and this tab has always shown their result; nothing called them.
    /// </remarks>
    private async Task ChangeScopeAsync()
    {
        if (_selectedPersonId is not { } personId
            || AppServices.Api is not { } api
            || _overview?.Overview?.Representation is not { } representation)
        {
            return;
        }

        ChangeRepresentationScopeDialog dialog = new(representation.Scopes) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ChangeRepresentationScopeRequest request = dialog.ToRequest(representation.Version);

        try
        {
            CommandError.IsOpen = false;

            if (dialog.IsBeginning)
            {
                await api.AddRepresentationScopeAsync(representation.Id, request).ConfigureAwait(true);
            }
            else
            {
                await api.EndRepresentationScopeAsync(representation.Id, request).ConfigureAwait(true);
            }
        }
        catch (AgencyOS.Client.AgencyOsApiException failure)
        {
            Refused(
                dialog.IsBeginning ? "Could not begin representing that" : "Could not end that scope",
                failure);

            return;
        }

        await OpenAsync(personId).ConfigureAwait(true);
    }

    private void OnChangeStatusClick(object sender, RoutedEventArgs e) => _ = ChangeStatusAsync();

    /// <summary>
    /// Pauses, ends or resumes representing somebody.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reality Closure wave 5. M4 shipped a representation status lifecycle and a
    /// published transition table, and this tab has shown the status since; nothing
    /// could change it. Converting a prospect produces an Active representation, so
    /// signing a client worked — ending one did not, which left the product able to
    /// acquire clients and unable to lose them.
    /// </para>
    /// <para>
    /// The dialog offers the statuses and the server decides which this
    /// representation can reach. A refusal keeps the page as it was, because the
    /// change did not happen.
    /// </para>
    /// </remarks>
    private async Task ChangeStatusAsync()
    {
        if (_selectedPersonId is not { } personId
            || AppServices.Api is not { } api
            || _overview?.Overview?.Representation is not { } representation)
        {
            return;
        }

        TransitionRepresentationDialog dialog =
            new(_overview.DisplayName, representation) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            CommandError.IsOpen = false;

            await api.TransitionRepresentationAsync(
                    representation.Id, dialog.ToRequest(representation.Version))
                .ConfigureAwait(true);
        }
        catch (AgencyOS.Client.AgencyOsApiException failure)
        {
            Refused("Could not change the representation status", failure);

            return;
        }

        await OpenAsync(personId).ConfigureAwait(true);
    }

    private void OnChangeTeamClick(object sender, RoutedEventArgs e) => _ = ChangeTeamAsync();

    /// <summary>Puts somebody on the team, changes their role, or takes them off.</summary>
    private async Task ChangeTeamAsync()
    {
        if (_selectedPersonId is not { } personId
            || AppServices.Api is not { } api
            || _overview?.Overview?.Representation is not { } representation)
        {
            return;
        }

        IReadOnlyList<OrganizationMemberResponse> members;

        try
        {
            CommandError.IsOpen = false;

            members = await api.ListOrganizationMembersAsync().ConfigureAwait(true);
        }
        catch (AgencyOS.Client.AgencyOsApiException failure)
        {
            Refused("Could not read who is in this organization", failure);

            return;
        }

        ChangeRepresentationTeamDialog dialog = new(members, representation.Team)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            if (dialog.IsAssigning)
            {
                await api
                    .AssignRepresentationTeamMemberAsync(
                        representation.Id, dialog.ToAssignRequest(representation.Version))
                    .ConfigureAwait(true);
            }
            else
            {
                await api
                    .RemoveRepresentationTeamMemberAsync(
                        representation.Id, dialog.ToRemoveRequest(representation.Version))
                    .ConfigureAwait(true);
            }
        }
        catch (AgencyOS.Client.AgencyOsApiException failure)
        {
            Refused(
                dialog.IsAssigning ? "Could not change the team" : "Could not take them off the team",
                failure);

            return;
        }

        await OpenAsync(personId).ConfigureAwait(true);
    }

    /// <summary>Shows a refusal under the name of what was actually attempted.</summary>
    /// <remarks>
    /// The list's error bar is titled for a load, and this is not one
    /// (<c>AOS-R002-012</c>).
    /// </remarks>
    private void Refused(string title, AgencyOS.Client.AgencyOsApiException failure)
    {
        CommandError.Title = title;

        // The server's own explanation, not the problem's title (AOS-R002-024).
        CommandError.Message = failure.Detail ?? failure.Message;
        CommandError.IsOpen = true;
    }

    private void OnAddCreditClick(object sender, RoutedEventArgs e) => _ = AddCreditAsync();

    private async Task AddCreditAsync()
    {
        try
        {
            if (_selectedPersonId is not { } personId || AppServices.Api is not { } api)
            {
                return;
            }

            AddCreditDialog dialog = new() { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await api.AddCreditAsync(dialog.ToRequest(personId)).ConfigureAwait(true);
            await OpenAsync(personId).ConfigureAwait(true);
        }
        catch (AgencyOS.Client.AgencyOsApiException failure)
        {
            Refused("Could not add the credit", failure);
        }
    }

    private void OnAddMaterialClick(object sender, RoutedEventArgs e) => _ = AddMaterialAsync();

    private async Task AddMaterialAsync()
    {
        try
        {
            if (_selectedPersonId is not { } personId || AppServices.Api is not { } api)
            {
                return;
            }

            AddMaterialDialog dialog = new() { XamlRoot = XamlRoot };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await api.AddMaterialAsync(dialog.ToRequest(personId)).ConfigureAwait(true);
            await OpenAsync(personId).ConfigureAwait(true);
        }
        catch (AgencyOS.Client.AgencyOsApiException failure)
        {
            Refused("Could not add the material", failure);
        }
    }

    private void RenderList()
    {
        if (_list is null)
        {
            return;
        }

        ListBusy.Visibility = _list.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ListError.IsOpen = _list.HasError;
        ListError.Message = _list.ErrorMessage ?? string.Empty;
        ListEmpty.IsOpen = _list.IsEmpty;

        CountText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_list.Talent.Count} shown · {_list.ClientCount} client(s)");
    }

    /// <summary>Reflects the selected client's working surface.</summary>
    private void RenderDetail()
    {
        if (_overview?.Overview is not { } overview)
        {
            DetailEmpty.IsOpen = true;
            ClientTabs.Visibility = Visibility.Collapsed;
            return;
        }

        DetailEmpty.IsOpen = false;
        ClientTabs.Visibility = Visibility.Visible;

        ClientName.Text = _overview.DisplayName;
        ClientStatusLine.Text = _overview.StatusLine;

        SummaryText.Text = string.IsNullOrWhiteSpace(overview.Talent.Summary)
            ? "No summary recorded."
            : overview.Talent.Summary;

        // Absent and withheld are the same to this screen, on purpose.
        PositioningPanel.Visibility = _overview.HasPositioningNotes ? Visibility.Visible : Visibility.Collapsed;
        PositioningText.Text = overview.Talent.PositioningNotes ?? string.Empty;

        DisciplinesText.Text = overview.Talent.Talent.Disciplines.Count == 0
            ? "None recorded."
            : string.Join(", ", overview.Talent.Talent.Disciplines);

        MarketText.Text = overview.Talent.BaseMarket ?? "Not recorded.";

        NoRepresentationBar.IsOpen = overview.Representation is null;

        // Nothing to change scopes or a team on until there is a relationship.
        ChangeScopeButton.IsEnabled = overview.Representation is not null;
        ChangeTeamButton.IsEnabled = overview.Representation is not null;

        // Terminated and Expired are terminal: the domain publishes an empty set of
        // transitions out of both, so offering the control would promise something
        // the server can only refuse. Signing somebody again creates a new
        // representation rather than reopening this one.
        ChangeStatusButton.IsEnabled = overview.Representation is { } live
            && live.Status is not ("Terminated" or "Expired");

        if (overview.Representation is { } representation)
        {
            string ends = representation.EndsOn is { } end
                ? string.Create(CultureInfo.InvariantCulture, $" until {end:yyyy-MM-dd}")
                : string.Empty;

            RepresentationSummary.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{representation.Status} since {representation.StartsOn:yyyy-MM-dd}{ends}. Territory: {representation.Territory ?? "not recorded"}.");

            ScopeList.ItemsSource = representation.Scopes.Where(x => x.EndsOn is null).ToArray();
        }
        else
        {
            RepresentationSummary.Text = string.Empty;
            ScopeList.ItemsSource = null;
        }

        TaskList.ItemsSource = overview.OpenTasks;

        InteractionList.ItemsSource = overview.RecentInteractions;
        InteractionEmpty.Visibility = overview.RecentInteractions.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        NextActionBanner.Apply(
            NextActionBar,
            NextActionFrom.Of(
                overview.OpenTasks.Select(
                    x => new NextActionFrom.Candidate(
                        x.Title, x.State, x.DueAt, x.AssigneeDisplayName, x.AssigneeUserId)),
                DateTimeOffset.UtcNow));
    }
    /// <summary>Runs a command and shows the server's reason if it refuses.</summary>
    /// <remarks>
    /// Dispatched and not awaited, so an unhandled refusal used to be lost
    /// entirely — the same class reproduced on Intelligence.
    /// </remarks>
    private async Task Guarded(string title, Func<Task> command)
    {
        try
        {
            CommandError.IsOpen = false;

            await command().ConfigureAwait(true);
        }
        catch (AgencyOS.Client.AgencyOsApiException failure)
        {
            Refused(title, failure);
        }
    }

}
