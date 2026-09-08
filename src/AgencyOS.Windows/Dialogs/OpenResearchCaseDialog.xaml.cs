using AgencyOS.Contracts.Intelligence;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Opens a research case.
/// </summary>
/// <remarks>
/// A case has no findings of its own, deliberately. A finding that lived on the case
/// would be a fifth kind of claim with no provenance rules, sitting beside the four
/// that have them; what the research concluded belongs in a thesis, where it can be
/// revised, challenged and retired (§1).
/// </remarks>
public sealed partial class OpenResearchCaseDialog : ContentDialog
{
    public OpenResearchCaseDialog() => InitializeComponent();

    public OpenResearchCaseRequest ToRequest() =>
        new(
            (QuestionBox.Text ?? string.Empty).Trim(),
            (SensitivityBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Internal",
            OwnerUserId: null,
            string.IsNullOrWhiteSpace(ContextBox.Text) ? null : ContextBox.Text.Trim());

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(QuestionBox.Text);
}
