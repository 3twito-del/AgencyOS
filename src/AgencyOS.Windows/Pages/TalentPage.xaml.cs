using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Representation;
using AgencyOS.Windows.Dialogs;
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

    private void OnAddCreditClick(object sender, RoutedEventArgs e) => _ = AddCreditAsync();

    private async Task AddCreditAsync()
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

    private void OnAddMaterialClick(object sender, RoutedEventArgs e) => _ = AddMaterialAsync();

    private async Task AddMaterialAsync()
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

        if (overview.Representation is { } representation)
        {
            string ends = representation.EndsOn is { } end
                ? string.Create(CultureInfo.InvariantCulture, $" until {end:d}")
                : string.Empty;

            RepresentationSummary.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{representation.Status} since {representation.StartsOn:d}{ends}. Territory: {representation.Territory ?? "not recorded"}.");

            ScopeList.ItemsSource = representation.Scopes.Where(x => x.EndsOn is null).ToArray();
        }
        else
        {
            RepresentationSummary.Text = string.Empty;
            ScopeList.ItemsSource = null;
        }

        TaskList.ItemsSource = overview.OpenTasks;
    }
}
