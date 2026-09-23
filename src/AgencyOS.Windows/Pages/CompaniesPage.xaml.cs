using System;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace AgencyOS.Windows.Pages;

/// <summary>The company directory and one company's record.</summary>
public sealed partial class CompaniesPage : Page, IPaletteCommandTarget
{
    private readonly CompanyListViewModel? _list;

    public CompaniesPage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _list = new CompanyListViewModel(api);
            _list.PropertyChanged += (_, _) => RenderList();
            CompanyList.ItemsSource = _list.Companies;

            _intelligence = new EntityIntelligenceViewModel(api);
            CompanyIntelligenceList.ItemsSource = _intelligence.Items;
        }
    }

    /// <summary>What the agency believes about the company being read.</summary>
    /// <remarks>
    /// The other half of a relationship the product only recorded in one
    /// direction: intelligence names its subjects, and the subjects could not
    /// name their intelligence.
    /// </remarks>
    private readonly EntityIntelligenceViewModel? _intelligence;

    private void OnIntelligenceInvoked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EntityIntelligenceRow row && App.Window is MainWindow window)
        {
            window.Reveal("intelligence", row.Id, row.Kind);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadListAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "company.create":
                _ = CreateCompanyAsync();
                break;

            case "view.refresh":
                _ = LoadListAsync();
                break;

            default:
                break;
        }
    }

    private async Task LoadListAsync()
    {
        if (_list is null)
        {
            ListError.Message = AppServices.Settings.Describe();
            ListError.IsOpen = true;
            return;
        }

        _list.SearchText = SearchBox.Text;
        await _list.LoadAsync().ConfigureAwait(true);
        RenderList();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = LoadListAsync();
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
    }

    private void OnCompanySelected(object sender, SelectionChangedEventArgs e)
    {
        if (CompanyList.SelectedItem is CompanySummaryResponse company)
        {
            _ = LoadDetailAsync(company.Id);
        }
    }

    private async Task LoadDetailAsync(Guid companyId)
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        try
        {
            DetailError.IsOpen = false;

            CompanyDetailResponse detail = await api.GetCompanyAsync(companyId).ConfigureAwait(true);

            CompanyName.Text = detail.Company.Name;

            CompanySubtitle.Text = string.Join(
                " · ",
                new[] { detail.Company.Type, detail.Company.LegalName, detail.Company.Website }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            StaffList.ItemsSource = detail.People;
            DetailEmpty.IsOpen = false;

            TimelineList.ItemsSource = await api.GetCompanyTimelineAsync(companyId).ConfigureAwait(true);

            if (_intelligence is not null)
            {
                await _intelligence.LoadAsync("Company", companyId).ConfigureAwait(true);

                // An empty list under a heading reading "Intelligence" says
                // nobody has recorded anything about them. A load that failed
                // must not be allowed to say it (F-01).
                if (_intelligence.ErrorMessage is { Length: > 0 } failure)
                {
                    // Whatever is in the list belongs to whoever was read
                    // last, and it is not this company.
                    _intelligence.Clear();

                    DetailError.Message = failure;
                    DetailError.IsOpen = true;
                }
            }
        }
        catch (AgencyOsApiException ex)
        {
            // One company that cannot be read is not the directory failing to
            // load, and the list is still on screen behind it (AOS-R002-012).
            DetailError.Message = ex.Detail ?? ex.Message;
            DetailError.IsOpen = true;
        }
    }

    private void OnNewCompanyClick(object sender, RoutedEventArgs e) => _ = CreateCompanyAsync();

    private async Task CreateCompanyAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        CommandError.IsOpen = false;

        // The dialog owns the create, so a refusal it can answer keeps the dialog,
        // the typing and the focus rather than landing here (AOS-R002-010).
        NewCompanyDialog dialog = new(api) { XamlRoot = XamlRoot };

        await dialog.ShowAsync();

        if (dialog.Terminal is { } terminal)
        {
            Refused("Could not create the company", terminal);

            return;
        }

        if (dialog.Created is not { } created)
        {
            return;
        }

        await LoadListAsync().ConfigureAwait(true);
        await LoadDetailAsync(created.Company.Id).ConfigureAwait(true);
    }

    /// <summary>Shows a refusal under the name of what was actually attempted.</summary>
    /// <remarks>
    /// The list's error bar is titled for a load. Writing a refused create into it
    /// told the operator that companies could not be loaded, which is not what
    /// happened and not what they had just done (AOS-R002-012).
    /// </remarks>
    private void Refused(string title, AgencyOsApiException failure)
    {
        CommandError.Title = title;

        // The server's own explanation, not the problem's title (AOS-R002-024).
        CommandError.Message = failure.Detail ?? failure.Message;
        CommandError.IsOpen = true;
    }
}
