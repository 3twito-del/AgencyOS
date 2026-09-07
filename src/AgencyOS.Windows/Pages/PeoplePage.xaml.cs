using System;
using System.Linq;
using System.Threading.Tasks;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Windows.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// The people directory and one person's full record.
/// </summary>
/// <remarks>
/// This page is the M2 workflow's spine: find a person, see how they are connected,
/// read their history, record what just happened and capture the next move without
/// leaving the screen.
/// </remarks>
public sealed partial class PeoplePage : Page, IPaletteCommandTarget
{
    private readonly PeopleListViewModel? _list;
    private readonly PersonDetailViewModel? _detail;

    public PeoplePage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _list = new PeopleListViewModel(api);
            _detail = new PersonDetailViewModel(api);

            _list.PropertyChanged += (_, _) => RenderList();
            _detail.PropertyChanged += (_, _) => RenderDetail();

            PeopleList.ItemsSource = _list.People;
            TimelineList.ItemsSource = _detail.Timeline;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _ = LoadListAsync();

    public void Execute(string commandId)
    {
        switch (commandId)
        {
            case "person.create":
                _ = CreatePersonAsync();
                break;

            case "interaction.record":
                _ = RecordInteractionAsync();
                break;

            case "relationship.create":
                _ = ConnectAsync();
                break;

            case "view.refresh":
                _ = LoadListAsync();
                break;

            default:
                break;
        }
    }

    // ------------------------------------------------------------------ list

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

    private void OnPersonSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleList.SelectedItem is PersonSummaryResponse person)
        {
            _ = LoadDetailAsync(person.Id);
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

    // ---------------------------------------------------------------- detail

    private async Task LoadDetailAsync(Guid personId)
    {
        if (_detail is null)
        {
            return;
        }

        await _detail.LoadAsync(personId).ConfigureAwait(true);
        RenderDetail();
    }

    private void RenderDetail()
    {
        if (_detail is null)
        {
            return;
        }

        DetailBusy.Visibility = _detail.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        DetailError.IsOpen = _detail.HasError;
        DetailError.Message = _detail.ErrorMessage ?? string.Empty;
        DetailEmpty.IsOpen = _detail.Person is null && !_detail.IsLoading && !_detail.HasError;

        RelationshipList.ItemsSource = _detail.Relationships;

        if (_detail.Person is { } person)
        {
            PersonName.Text = person.Person.DisplayName;

            PersonSubtitle.Text = string.Join(
                " · ",
                new[] { person.Person.Title, person.Person.PrimaryCompanyName, person.Person.Email }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            RecordButton.IsEnabled = true;
            ConnectButton.IsEnabled = true;
        }
        else
        {
            PersonName.Text = string.Empty;
            PersonSubtitle.Text = string.Empty;
            RecordButton.IsEnabled = false;
            ConnectButton.IsEnabled = false;
        }
    }

    // -------------------------------------------------------------- commands

    private void OnNewPersonClick(object sender, RoutedEventArgs e) => _ = CreatePersonAsync();

    private async Task CreatePersonAsync()
    {
        if (AppServices.Api is not { } api)
        {
            return;
        }

        NewPersonDialog dialog = new() { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            PersonDetailResponse created = await api
                .CreatePersonAsync(dialog.ToRequest())
                .ConfigureAwait(true);

            await LoadListAsync().ConfigureAwait(true);
            await LoadDetailAsync(created.Person.Id).ConfigureAwait(true);
        }
        catch (AgencyOS.Client.AgencyOsApiException ex)
        {
            ListError.Message = ex.Detail ?? ex.Message;
            ListError.IsOpen = true;
        }
    }

    private void OnRecordClick(object sender, RoutedEventArgs e) => _ = RecordInteractionAsync();

    /// <summary>
    /// Records an interaction with the selected person, optionally capturing the
    /// next action in the same command.
    /// </summary>
    private async Task RecordInteractionAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Person is not { } person)
        {
            return;
        }

        RecordInteractionDialog dialog = new(api, person.Person.Id, person.Person.DisplayName)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Submitted)
        {
            await LoadDetailAsync(person.Person.Id).ConfigureAwait(true);
        }
    }

    private void OnConnectClick(object sender, RoutedEventArgs e) => _ = ConnectAsync();

    private async Task ConnectAsync()
    {
        if (AppServices.Api is not { } api || _detail?.Person is not { } person)
        {
            return;
        }

        ConnectCompanyDialog dialog = new(api, person.Person.Id) { XamlRoot = XamlRoot };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Connected)
        {
            await LoadDetailAsync(person.Person.Id).ConfigureAwait(true);
        }
    }

    private async void OnEndRelationshipClick(object sender, RoutedEventArgs e)
    {
        if (_detail is null || RelationshipList.SelectedItem is not RelationshipResponse relationship)
        {
            return;
        }

        await _detail.EndRelationshipAsync(relationship.Id).ConfigureAwait(true);
        RenderDetail();
    }
}
