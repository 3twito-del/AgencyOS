using AgencyOS.Contracts.Intelligence;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Creates a standing list of records somebody is watching.</summary>
public sealed partial class CreateWatchlistDialog : ContentDialog
{
    public CreateWatchlistDialog() => InitializeComponent();

    public CreateWatchlistRequest ToRequest() =>
        new(
            (NameBox.Text ?? string.Empty).Trim(),
            (SensitivityBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Internal",
            OwnerUserId: null,
            string.IsNullOrWhiteSpace(PurposeBox.Text) ? null : PurposeBox.Text.Trim());

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
}
