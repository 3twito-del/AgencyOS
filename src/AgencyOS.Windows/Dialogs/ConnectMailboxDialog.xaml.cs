using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AgencyOS.Contracts.Documents;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Connects a mailbox by completing an OAuth authorization the user performs.
/// </summary>
/// <remarks>
/// <para>
/// The consent page opens in the operator's own browser, and what comes back into
/// this dialog is an authorization code. The code goes straight to the AgencyOS
/// server, which exchanges it; the tokens that exchange produces are encrypted at
/// rest there and are never returned to this application. There is no field on this
/// dialog, and no property on this class, that could hold one (ADR-0027).
/// </para>
/// <para>
/// Visibility defaults to private. A mailbox is somebody's correspondence, and the
/// default that makes it readable by the agency is the wrong default to pick on
/// their behalf (ADR-0026).
/// </para>
/// </remarks>
public sealed partial class ConnectMailboxDialog : ContentDialog
{
    private readonly IReadOnlyList<CommunicationProviderResponse> _providers;

    /// <param name="providers">
    /// What the server says it can connect to. An unconfigured provider is listed
    /// and unselectable rather than hidden, so an operator learns that Outlook is
    /// possible and not yet provisioned instead of concluding it is unsupported.
    /// </param>
    /// <param name="redirectUri">
    /// The redirect the authorization URL was built with. Shown and editable,
    /// because it must match the application registration exactly and a mismatch is
    /// otherwise a silent failure at the provider.
    /// </param>
    public ConnectMailboxDialog(
        IReadOnlyList<CommunicationProviderResponse> providers,
        string redirectUri)
    {
        InitializeComponent();

        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers;

        RedirectBox.Text = redirectUri;

        foreach (CommunicationProviderResponse provider in providers)
        {
            ProviderBox.Items.Add(new ComboBoxItem
            {
                Content = provider.IsConfigured
                    ? provider.DisplayName
                    : $"{provider.DisplayName} (not configured on this server)",
                Tag = provider.Provider,
                IsEnabled = provider.IsConfigured,
            });
        }

        ComboBoxItem? first = ProviderBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(x => x.IsEnabled);

        if (first is not null)
        {
            ProviderBox.SelectedItem = first;
        }

        Update();
    }

    public string Provider =>
        (ProviderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    public string AuthorizationCode => (CodeBox.Text ?? string.Empty).Trim();

    public string RedirectUri => (RedirectBox.Text ?? string.Empty).Trim();

    public string Visibility =>
        (VisibilityBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Private";

    private void OnOpenConsent(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        CommunicationProviderResponse? provider = _providers
            .FirstOrDefault(x => string.Equals(x.Provider, Provider, StringComparison.Ordinal));

        if (provider?.AuthorizationUrl is not { Length: > 0 } url)
        {
            return;
        }

        // The system browser rather than an embedded WebView. A credential prompt
        // inside the application is a credential prompt the application could be
        // reading, and the user has no way to check the address bar (ADR-0027).
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    }

    private void OnChanged(object sender, object e) => Update();

    private void Update()
    {
        CommunicationProviderResponse? provider = _providers
            .FirstOrDefault(x => string.Equals(x.Provider, Provider, StringComparison.Ordinal));

        ConsentButton.IsEnabled = provider?.AuthorizationUrl is { Length: > 0 };

        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(Provider)
            && !string.IsNullOrWhiteSpace(AuthorizationCode)
            && Uri.TryCreate(RedirectUri, UriKind.Absolute, out _);
    }
}
