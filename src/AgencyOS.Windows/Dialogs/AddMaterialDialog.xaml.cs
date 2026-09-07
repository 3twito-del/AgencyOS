using System;
using AgencyOS.Contracts.Representation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Captures a material record.</summary>
/// <remarks>
/// Metadata only. The server refuses anything that is not an absolute http or
/// https address, so a device-local path is rejected rather than stored as though
/// other people could open it.
/// </remarks>
public sealed partial class AddMaterialDialog : ContentDialog
{
    public AddMaterialDialog()
    {
        InitializeComponent();

        TypeBox.SelectedIndex = 0;
        StatusBox.SelectedIndex = 0;
    }

    public AddMaterialRequest ToRequest(Guid personId) => new(
        personId,
        TitleBox.Text.Trim(),
        SelectedTag(TypeBox) ?? "Other",
        SelectedTag(StatusBox),
        Empty(VersionBox.Text),
        Empty(UriBox.Text));

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(TitleBox.Text);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
