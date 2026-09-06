using AgencyOS.Windows.Presentation;
using Microsoft.UI.Xaml;

namespace AgencyOS.Windows;

/// <summary>
/// Shell window of the AgencyOS Windows client.
/// </summary>
/// <remarks>
/// Per <c>CLAUDE.md</c> section 6, no business logic lives in code-behind. This
/// window binds to <see cref="ShellViewModel"/> and does nothing else.
/// </remarks>
public sealed partial class MainWindow : Window
{
    /// <summary>Initializes the shell window.</summary>
    public MainWindow()
    {
        ViewModel = new ShellViewModel();
        InitializeComponent();
    }

    /// <summary>Gets the view model backing this window.</summary>
    public ShellViewModel ViewModel { get; }
}
