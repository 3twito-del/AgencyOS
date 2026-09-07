using System;
using System.Threading;
using System.Threading.Tasks;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts;
using AgencyOS.Contracts.Search;
using AgencyOS.Windows.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace AgencyOS.Windows;

/// <summary>
/// Shell window: navigation, connection state, and the command palette.
/// </summary>
/// <remarks>
/// Per <c>CLAUDE.md</c> section 6, no business logic lives in code-behind. This
/// class routes navigation and keyboard input; every decision about data is made
/// by a view model in <c>AgencyOS.Client</c>, and every decision about authority is
/// made by the server.
/// </remarks>
public sealed partial class MainWindow : Window
{
    private readonly CommandPaletteViewModel _palette = new();
    private readonly SearchViewModel? _search;
    private readonly SyncStatusViewModel? _sync;

    /// <summary>
    /// Cancels the previous search when the user keeps typing.
    /// </summary>
    /// <remarks>
    /// Without this, results arrive out of order and the list settles on whichever
    /// request happened to finish last rather than on what was typed.
    /// </remarks>
    private CancellationTokenSource? _searchCancellation;

    public MainWindow()
    {
        InitializeComponent();

        if (AppServices.Api is { } api)
        {
            _search = new SearchViewModel(api, AppServices.Cache);
            _search.PropertyChanged += (_, _) => RenderSearch();
            SearchResults.ItemsSource = _search.Results;
        }

        if (AppServices.Sync is { } engine && AppServices.Cache is { } cache)
        {
            _sync = new SyncStatusViewModel(engine, cache);
            _sync.PropertyChanged += (_, _) => RenderSync();
        }

        RenderSync();

        ConnectionText.Text = AppServices.Settings.Describe();
        BuildText.Text = $"{BuildInfo.Version} · {BuildInfo.Channel} · contract v{ApiContract.Current}";

        PaletteResults.ItemsSource = _palette.Results;

        Navigation.SelectedItem = Navigation.MenuItems[0];
        Navigate("command-center");

        AddAccelerator(VirtualKey.P, VirtualKeyModifiers.Control, (_, args) =>
        {
            TogglePalette();
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.Number1, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectMenu(0);
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.Number2, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectMenu(1);
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.Number3, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectMenu(2);
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.Number4, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectMenu(3);
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.Number5, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectMenu(4);
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.Number6, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectMenu(5);
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.Number7, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectMenu(6);
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.Number8, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectMenu(7);
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.Number9, VirtualKeyModifiers.Control, (_, args) =>
        {
            SelectMenu(8);
            args.Handled = true;
        });

        // Sync moves to F8 rather than Ctrl+0, which several keyboard layouts
        // intercept for zoom.
        AddAccelerator(VirtualKey.F8, VirtualKeyModifiers.None, (_, args) =>
        {
            SelectMenu(9);
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.K, VirtualKeyModifiers.Control, (_, args) =>
        {
            ToggleSearch();
            args.Handled = true;
        });

        AddAccelerator(VirtualKey.F9, VirtualKeyModifiers.None, (sender, args) =>
        {
            _ = SynchronizeAsync();
            args.Handled = true;
        });
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        KeyboardAccelerator accelerator = new() { Key = key, Modifiers = modifiers };
        accelerator.Invoked += handler;

        if (Content is UIElement root)
        {
            root.KeyboardAccelerators.Add(accelerator);
        }
    }

    private void SelectMenu(int index)
    {
        if (index >= 0 && index < Navigation.MenuItems.Count)
        {
            Navigation.SelectedItem = Navigation.MenuItems[index];
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        Type page = tag switch
        {
            "people" => typeof(PeoplePage),
            "companies" => typeof(CompaniesPage),
            "talent" => typeof(TalentPage),
            "prospects" => typeof(ProspectsPage),
            "projects" => typeof(ProjectsPage),
            "packages" => typeof(PackagesPage),
            "pipeline" => typeof(PipelinePage),
            "saved-views" => typeof(SavedViewsPage),
            "sync" => typeof(SyncPage),
            _ => typeof(CommandCenterPage),
        };

        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }

    // ------------------------------------------------------- command palette

    private void TogglePalette()
    {
        bool showing = PaletteLayer.Visibility == Visibility.Collapsed;

        PaletteLayer.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;

        if (showing)
        {
            PaletteQuery.Text = string.Empty;
            _palette.Query = string.Empty;
            PaletteQuery.Focus(FocusState.Programmatic);
        }
    }

    private void OnPaletteQueryChanged(object sender, TextChangedEventArgs e) =>
        _palette.Query = PaletteQuery.Text;

    private void OnPaletteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                PaletteLayer.Visibility = Visibility.Collapsed;
                e.Handled = true;
                break;

            case VirtualKey.Down:
                _palette.MoveSelection(1);
                PaletteResults.SelectedItem = _palette.Selected;
                e.Handled = true;
                break;

            case VirtualKey.Up:
                _palette.MoveSelection(-1);
                PaletteResults.SelectedItem = _palette.Selected;
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                if (_palette.Selected is { } selected)
                {
                    Invoke(selected.Id);
                }

                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void OnPaletteItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PaletteCommand command)
        {
            Invoke(command.Id);
        }
    }

    /// <summary>
    /// Carries out a palette command.
    /// </summary>
    /// <remarks>
    /// Navigation commands are handled here; creation and capture commands are
    /// delegated to the page currently showing, because the page holds the context
    /// they need.
    /// </remarks>
    private void Invoke(string commandId)
    {
        PaletteLayer.Visibility = Visibility.Collapsed;

        switch (commandId)
        {
            case "go.command-center":
                SelectMenu(0);
                return;

            case "go.people":
                SelectMenu(1);
                return;

            case "go.companies":
                SelectMenu(2);
                return;

            case "go.talent":
                SelectMenu(3);
                return;

            case "go.prospects":
                SelectMenu(4);
                return;

            case "go.projects":
                SelectMenu(5);
                return;

            case "go.packages":
                SelectMenu(6);
                return;

            case "go.pipeline":
                SelectMenu(7);
                return;

            case "go.saved-views":
                SelectMenu(8);
                return;

            case "go.sync":
                SelectMenu(9);
                return;

            case "search.open":
                ToggleSearch();
                return;

            case "sync.now":
                _ = SynchronizeAsync();
                return;

            default:
                if (ContentFrame.Content is IPaletteCommandTarget target)
                {
                    target.Execute(commandId);
                }

                return;
        }
    }

    // -------------------------------------------------------- global search

    private void ToggleSearch()
    {
        bool showing = SearchLayer.Visibility == Visibility.Collapsed;

        SearchLayer.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;

        if (!showing)
        {
            return;
        }

        SearchQuery.Text = string.Empty;

        if (_search is not null)
        {
            _search.Query = string.Empty;
            _search.Results.Clear();
        }

        RenderSearch();
        SearchQuery.Focus(FocusState.Programmatic);
    }

    private void OnSearchQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (_search is null)
        {
            SearchErrorBar.Message = AppServices.Settings.Describe();
            SearchErrorBar.IsOpen = true;
            return;
        }

        _search.Query = SearchQuery.Text;
        _ = RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        if (_search is null)
        {
            return;
        }

        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _searchCancellation, cancellation);

        previous?.Cancel();
        previous?.Dispose();

        try
        {
            await _search.SearchAsync(cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke. Not a failure worth showing.
        }

        RenderSearch();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                SearchLayer.Visibility = Visibility.Collapsed;
                e.Handled = true;
                break;

            case VirtualKey.Down:
                MoveSearchSelection(1);
                e.Handled = true;
                break;

            case VirtualKey.Up:
                MoveSearchSelection(-1);
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                OpenSelectedSearchResult();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void MoveSearchSelection(int delta)
    {
        if (_search is null || _search.Results.Count == 0)
        {
            return;
        }

        int next = SearchResults.SelectedIndex + delta;

        if (next < 0)
        {
            next = _search.Results.Count - 1;
        }
        else if (next >= _search.Results.Count)
        {
            next = 0;
        }

        SearchResults.SelectedIndex = next;
    }

    private void OnSearchItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchHit hit)
        {
            OpenHit(hit);
        }
    }

    private void OpenSelectedSearchResult()
    {
        if (SearchResults.SelectedItem is SearchHit hit)
        {
            OpenHit(hit);
        }
    }

    /// <summary>
    /// Navigates to the record a hit points at.
    /// </summary>
    /// <remarks>
    /// A hit carries its own type and identity, so opening it is a navigation
    /// rather than a guess. Tasks live on the Command Center, which is where a
    /// task is actually acted on.
    /// </remarks>
    private void OpenHit(SearchHit hit)
    {
        SearchLayer.Visibility = Visibility.Collapsed;

        switch (hit.Type)
        {
            case "Person":
                // The talent workspace, not the directory: a search hit on a person
                // is almost always the start of working on them.
                SelectMenu(3);

                if (ContentFrame.Content is TalentPage talent)
                {
                    _ = talent.OpenAsync(hit.Id);
                }

                break;

            case "Company":
                SelectMenu(2);
                break;

            default:
                SelectMenu(0);
                break;
        }
    }

    private void RenderSearch()
    {
        if (_search is null)
        {
            return;
        }

        SearchBusy.Visibility = _search.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        SearchOfflineBar.IsOpen = _search.IsOffline;
        SearchEmptyBar.IsOpen = _search.IsEmpty;
        SearchErrorBar.IsOpen = _search.HasError;
        SearchErrorBar.Message = _search.ErrorMessage ?? string.Empty;
    }

    // ------------------------------------------------- synchronization state

    private async Task SynchronizeAsync()
    {
        if (_sync is null)
        {
            return;
        }

        await _sync.SynchronizeAsync().ConfigureAwait(true);
        RenderSync();
    }

    /// <summary>Keeps the always-visible status line honest about where the client stands.</summary>
    private void RenderSync()
    {
        if (_sync is null)
        {
            SyncText.Text = AppServices.CacheFailure is null
                ? "Online only - no local cache."
                : "Online only - the local cache is unavailable.";

            return;
        }

        SyncText.Text = _sync.StatusLine;
    }
}

/// <summary>A page that can carry out palette commands aimed at its own context.</summary>
public interface IPaletteCommandTarget
{
    /// <summary>Carries out a command, ignoring ones it does not recognize.</summary>
    void Execute(string commandId);
}
