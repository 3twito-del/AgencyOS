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
            CompanyDetailResponse detail = await api.GetCompanyAsync(companyId).ConfigureAwait(true);

            CompanyName.Text = detail.Company.Name;

            CompanySubtitle.Text = string.Join(
                " · ",
                new[] { detail.Company.Type, detail.Company.LegalName, detail.Company.Website }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            StaffList.ItemsSource = detail.People;
            DetailEmpty.IsOpen = false;

            TimelineList.ItemsSource = await api.GetCompanyTimelineAsync(companyId).ConfigureAwait(true);
        }
        catch (AgencyOsApiException ex)
        {
            ListError.Message = ex.Detail ?? ex.Message;
            ListError.IsOpen = true;
        }
    }

    private void OnNewCompanyClick(object sender, RoutedEventArgs e) => _ = CreateCompanyAsync();

    private async Task CreateCompanyAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        NewCompanyDialog dialog = new() { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            CompanyDetailResponse created = await api.CreateCompanyAsync(dialog.ToRequest()).ConfigureAwait(true);

            await LoadListAsync().ConfigureAwait(true);
            await LoadDetailAsync(created.Company.Id).ConfigureAwait(true);
        }
        catch (AgencyOsApiException ex)
        {
            ListError.Message = ex.Detail ?? ex.Message;
            ListError.IsOpen = true;
        }
    }
}
