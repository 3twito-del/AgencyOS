using System;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Connects a person to a company through a professional relationship.
/// </summary>
/// <remarks>
/// The relationship types offered here are the ones the domain accepts for a
/// person-to-company pairing. Offering the rest and letting the server refuse
/// would be a worse experience than not offering them.
/// </remarks>
public sealed partial class ConnectCompanyDialog : ContentDialog
{
    private readonly IAgencyOsApi _api;
    private readonly Guid _personId;
    private readonly CompanyListViewModel _companies;

    public ConnectCompanyDialog(IAgencyOsApi api, Guid personId)
    {
        InitializeComponent();

        _api = api;
        _personId = personId;
        _companies = new CompanyListViewModel(api);

        CompanyList.ItemsSource = _companies.Companies;
        _companies.PropertyChanged += (_, _) =>
            Busy.Visibility = _companies.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        PrimaryButtonClick += OnPrimaryButtonClick;
        Opened += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Gets a value indicating whether a relationship was created.</summary>
    public bool Connected { get; private set; }

    private async Task LoadAsync()
    {
        _companies.SearchText = SearchBox.Text;
        await _companies.LoadAsync().ConfigureAwait(true);

        if (_companies.HasError)
        {
            ErrorBar.Message = _companies.ErrorMessage ?? string.Empty;
            ErrorBar.IsOpen = true;
        }
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = LoadAsync();
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();

        try
        {
            if (CompanyList.SelectedItem is not CompanySummaryResponse company)
            {
                ErrorBar.Message = "Choose a company to connect to.";
                ErrorBar.IsOpen = true;
                args.Cancel = true;
                return;
            }

            CreateRelationshipRequest request = new(
                new PartyRefRequest("Person", _personId),
                new PartyRefRequest("Company", company.Id),
                TypeBox.SelectedItem as string ?? "Employment",
                Notes: string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
                StartedAt: DateTimeOffset.UtcNow);

            await _api.CreateRelationshipAsync(request).ConfigureAwait(true);
            Connected = true;
        }
        catch (AgencyOsApiException ex)
        {
            ErrorBar.Message = ex.Detail ?? ex.Message;
            ErrorBar.IsOpen = true;
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
