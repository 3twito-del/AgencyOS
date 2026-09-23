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

    /// <summary>What the agency believes about the person being read.</summary>
    /// <remarks>
    /// The other half of a relationship the product only recorded in one
    /// direction: intelligence names its subjects, and the subjects could not
    /// name their intelligence.
    /// </remarks>
    private readonly EntityIntelligenceViewModel? _intelligence;

    public PeoplePage()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _list = new PeopleListViewModel(api);
            _detail = new PersonDetailViewModel(api);

            _list.PropertyChanged += (_, _) => RenderList();
            _detail.PropertyChanged += (_, _) => RenderDetail();

            _intelligence = new EntityIntelligenceViewModel(api);

            PeopleList.ItemsSource = _list.People;
            TimelineList.ItemsSource = _detail.Timeline;
            PersonIntelligenceList.ItemsSource = _intelligence.Items;
        }
    }

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

        if (_intelligence is null)
        {
            return;
        }

        // A person is a Person subject. They may also hold a talent profile,
        // which is a different subject with its own intelligence, and reading
        // one as the other would put claims about a career under a name.
        await _intelligence.LoadAsync("Person", personId).ConfigureAwait(true);

        // An empty list under a heading reading "Intelligence" says nobody has
        // recorded anything about them. A load that failed must not be allowed
        // to say it (F-01).
        if (_intelligence.ErrorMessage is { Length: > 0 } failure)
        {
            // Whatever is in the list belongs to whoever was read last, and it
            // is not this person.
            _intelligence.Clear();

            DetailError.Message = failure;
            DetailError.IsOpen = true;
        }
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

        CommandError.IsOpen = false;

        // The dialog owns the create, so a refusal it can answer keeps the dialog,
        // the typing and the focus rather than landing here (AOS-R002-010).
        NewPersonDialog dialog = new(api) { XamlRoot = XamlRoot };

        await dialog.ShowAsync();

        if (dialog.Terminal is { } terminal)
        {
            Refused("Could not create the person", terminal);

            return;
        }

        if (dialog.Created is not { } created)
        {
            return;
        }

        await LoadListAsync().ConfigureAwait(true);
        await LoadDetailAsync(created.Person.Id).ConfigureAwait(true);
    }

    /// <summary>Shows a refusal under the name of what was actually attempted.</summary>
    /// <remarks>
    /// The list's error bar is titled for a load. Writing a refused create into it
    /// told the operator that people could not be loaded, which is not what
    /// happened and not what they had just done (AOS-R002-012).
    /// </remarks>
    private void Refused(string title, AgencyOS.Client.AgencyOsApiException failure)
    {
        CommandError.Title = title;

        // The server's own explanation, not the problem's title (AOS-R002-024).
        CommandError.Message = failure.Detail ?? failure.Message;
        CommandError.IsOpen = true;
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
