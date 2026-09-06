using Microsoft.UI.Xaml;

namespace AgencyOS.Windows;

/// <summary>
/// Application entry point for the AgencyOS Windows client.
/// </summary>
/// <remarks>
/// Milestone M0 ships the shell only. Navigation, the command palette and the
/// entity surfaces described in <c>docs/12_WINDOWS_NATIVE.md</c> arrive with M2.
/// </remarks>
public partial class App : Application
{
    private Window? _window;

    /// <summary>Initializes the application.</summary>
    public App() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
