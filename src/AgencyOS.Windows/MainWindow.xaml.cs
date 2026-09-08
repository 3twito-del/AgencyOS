using System;
using System.Threading;
using System.Threading.Tasks;
using AgencyOS.Client.Commands;
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

        // Every gesture comes from the registry, which validated at construction
        // that no two commands claim one. Before M13 they were installed by hand
        // here and addressed the navigation menu by position, which is how Ctrl+9
        // came to mean two different things (ADR-0032).
        foreach (CommandDefinition command in CommandRegistry.Default.GlobalGestures)
        {
            if (!TryMapGesture(command.Gesture, out VirtualKey key, out VirtualKeyModifiers modifiers))
            {
                continue;
            }

            string id = command.Id;

            AddAccelerator(key, modifiers, (_, args) =>
            {
                Dispatch(id);
                args.Handled = true;
            });
        }

        // The palette is the one gesture with no command of its own: it is how a
        // user reaches every other command, so it cannot be one of them.
        AddAccelerator(VirtualKey.P, VirtualKeyModifiers.Control, (_, args) =>
        {
            TogglePalette();
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

    /// <summary>
    /// Selects a workspace by tag, never by position.
    /// </summary>
    /// <remarks>
    /// The M12 defect in one method. <c>SelectMenu(int)</c> addressed the
    /// navigation pane by index, so inserting AI above Saved Views silently
    /// repointed Ctrl+9. A tag cannot shift (ADR-0032).
    /// </remarks>
    private void SelectWorkspace(string tag)
    {
        foreach (object item in Navigation.MenuItems)
        {
            if (item is NavigationViewItem { Tag: string candidate }
                && string.Equals(candidate, tag, StringComparison.Ordinal))
            {
                Navigation.SelectedItem = item;

                return;
            }
        }
    }

    /// <summary>
    /// Maps a registry gesture onto the WinRT keys that express it.
    /// </summary>
    /// <remarks>
    /// The registry cannot name <c>VirtualKey</c>: it lives in the cross-platform
    /// client assembly so the palette and the accelerators share one list. This is
    /// the only place the two vocabularies meet, and a gesture it cannot map
    /// installs nothing rather than installing something wrong.
    /// </remarks>
    private static bool TryMapGesture(
        CommandGesture gesture, out VirtualKey key, out VirtualKeyModifiers modifiers)
    {
        modifiers = VirtualKeyModifiers.None;

        if (gesture.Modifiers.HasFlag(CommandModifiers.Control))
        {
            modifiers |= VirtualKeyModifiers.Control;
        }

        if (gesture.Modifiers.HasFlag(CommandModifiers.Shift))
        {
            modifiers |= VirtualKeyModifiers.Shift;
        }

        if (gesture.Modifiers.HasFlag(CommandModifiers.Alt))
        {
            modifiers |= VirtualKeyModifiers.Menu;
        }

        key = gesture.Key switch
        {
            >= CommandKey.D0 and <= CommandKey.D9 =>
                VirtualKey.Number0 + (gesture.Key - CommandKey.D0),
            >= CommandKey.A and <= CommandKey.Z =>
                VirtualKey.A + (gesture.Key - CommandKey.A),
            >= CommandKey.F1 and <= CommandKey.F12 =>
                VirtualKey.F1 + (gesture.Key - CommandKey.F1),
            CommandKey.Escape => VirtualKey.Escape,
            CommandKey.Enter => VirtualKey.Enter,
            _ => VirtualKey.None,
        };

        return key != VirtualKey.None;
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
            "deals" => typeof(DealsPage),
            "contracts" => typeof(ContractsPage),
            "finance" => typeof(FinancePage),
            "documents" => typeof(DocumentsPage),
            "communications" => typeof(CommunicationsPage),
            "intelligence" => typeof(IntelligencePage),
            "ai" => typeof(AiPage),
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
                    Dispatch(selected.Id);
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
            Dispatch(command.Id);
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
    /// <summary>
    /// Carries out a command, wherever it came from.
    /// </summary>
    /// <remarks>
    /// One route for the palette, the accelerators and, from M13, activation. A
    /// navigation command moves to its workspace by tag; an action command
    /// navigates to the workspace that answers it, when it names one, and is then
    /// handed to whichever page is showing. Before M13 this switch listed every
    /// destination twice over - once here and once in the constructor - and both
    /// lists addressed the menu by index (ADR-0032).
    /// </remarks>
    private void Dispatch(string commandId)
    {
        PaletteLayer.Visibility = Visibility.Collapsed;

        if (CommandRegistry.Default.Find(commandId) is not { } command)
        {
            return;
        }

        if (command.Action == CommandActionKind.Navigate)
        {
            SelectWorkspace(command.Workspace!);

            return;
        }

        // Shell-owned actions: they belong to the window rather than to any
        // workspace, so no page can answer them.
        switch (commandId)
        {
            case "search.open":
                ToggleSearch();
                return;

            case "sync.now":
                _ = SynchronizeAsync();
                return;

            default:
                break;
        }

        // An action that names a workspace is answered there, so invoking it from
        // anywhere goes to the right page first. Before M13 it was dispatched to
        // whatever happened to be showing, which is why "Open receivables" did
        // nothing unless Finance was already open.
        if (command.Workspace is { } workspace)
        {
            SelectWorkspace(workspace);
        }

        if (ContentFrame.Content is IPaletteCommandTarget target)
        {
            target.Execute(commandId);
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
                SelectWorkspace("talent");

                if (ContentFrame.Content is TalentPage talent)
                {
                    _ = talent.OpenAsync(hit.Id);
                }

                break;

            case "Company":
                SelectWorkspace("companies");
                break;

            default:
                SelectWorkspace("command-center");
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
