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
    /// <summary>Initializes the application.</summary>
    public App() => InitializeComponent();

    /// <summary>
    /// The main window, once there is one.
    /// </summary>
    /// <remarks>
    /// Exposed because the Windows App SDK file pickers are told which window they
    /// belong to. A picker with no owner appears behind the application on a
    /// multi-monitor desktop, which reads as the application having hung.
    /// </remarks>
    internal static Window? Window { get; private set; }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}
