using AgencyOS.Contracts.Intelligence;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// States a view the agency holds.
/// </summary>
/// <remarks>
/// A thesis starts in Draft and is activated separately, which is not ceremony: the
/// gap is where somebody reads it back and decides whether they are willing to hold
/// the position in front of colleagues (§1).
/// </remarks>
public sealed partial class CreateThesisDialog : ContentDialog
{
    public CreateThesisDialog() => InitializeComponent();

    /// <summary>What the operator filled in, as the API expects it.</summary>
    public CreateThesisRequest ToRequest() =>
        new(
            (TitleBox.Text ?? string.Empty).Trim(),
            (PropositionBox.Text ?? string.Empty).Trim(),
            Selection(SensitivityBox) ?? "Internal",
            OwnerUserId: null,
            Selection(ConfidenceBox) ?? "Unstated",
            string.IsNullOrWhiteSpace(RationaleBox.Text) ? null : RationaleBox.Text.Trim());

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(TitleBox.Text)
            && !string.IsNullOrWhiteSpace(PropositionBox.Text);

    private static string? Selection(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
