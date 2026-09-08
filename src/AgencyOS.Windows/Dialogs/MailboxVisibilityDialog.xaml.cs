using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Changes who in the organization may read a connected mailbox.
/// </summary>
/// <remarks>
/// A separate, deliberate act rather than a field on a settings page. Widening
/// visibility exposes correspondence that already exists, which is not what people
/// expect a setting to do, so the dialog says it (ADR-0026).
/// </remarks>
public sealed partial class MailboxVisibilityDialog : ContentDialog
{
    public MailboxVisibilityDialog(string mailboxAddress, string current)
    {
        InitializeComponent();

        MailboxText.Text = mailboxAddress;

        RadioButton? selected = VisibilityChoice.Items
            .OfType<RadioButton>()
            .FirstOrDefault(x => string.Equals(x.Tag as string, current, StringComparison.Ordinal));

        VisibilityChoice.SelectedItem = selected ?? VisibilityChoice.Items.FirstOrDefault();
    }

    /// <summary>
    /// The chosen visibility. Named apart from <c>UIElement.Visibility</c>, which
    /// every control already has and which means something else entirely.
    /// </summary>
    public string MailboxVisibility =>
        (VisibilityChoice.SelectedItem as RadioButton)?.Tag as string ?? "Private";
}
