using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Moves a package to a different status.</summary>
public sealed partial class ChangePackageStatusDialog : ContentDialog
{
    public ChangePackageStatusDialog(string currentStatus)
    {
        InitializeComponent();

        CurrentText.Text = $"Currently {currentStatus}";

        for (int index = 0; index < StatusBox.Items.Count; index++)
        {
            if (StatusBox.Items[index] is ComboBoxItem item && (item.Tag as string) == currentStatus)
            {
                StatusBox.SelectedIndex = index;
                return;
            }
        }

        StatusBox.SelectedIndex = 0;
    }

    public ChangePackageStatusRequest ToRequest(int expectedVersion) => new(
        SelectedTag(StatusBox) ?? "Draft",
        expectedVersion,
        Empty(ReasonBox.Text));

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
