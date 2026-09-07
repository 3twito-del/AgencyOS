using System;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts;
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

    public MainWindow()
    {
        InitializeComponent();

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

            default:
                if (ContentFrame.Content is IPaletteCommandTarget target)
                {
                    target.Execute(commandId);
                }

                return;
        }
    }
}

/// <summary>A page that can carry out palette commands aimed at its own context.</summary>
public interface IPaletteCommandTarget
{
    /// <summary>Carries out a command, ignoring ones it does not recognize.</summary>
    void Execute(string commandId);
}
