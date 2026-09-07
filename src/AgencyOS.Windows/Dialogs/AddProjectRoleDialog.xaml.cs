using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Captures a position on a project.</summary>
/// <remarks>
/// Exclusivity is opt-in and the dialog says why. Claiming it wrongly blocks
/// legitimate data entry; omitting it wrongly only fails to catch a duplicate, so
/// the default fails open.
/// </remarks>
public sealed partial class AddProjectRoleDialog : ContentDialog
{
    public AddProjectRoleDialog()
    {
        InitializeComponent();

        TypeBox.SelectedIndex = 0;
    }

    public CreateProjectRoleRequest ToRequest(int expectedVersion) => new(
        SelectedTag(TypeBox) ?? "Other",
        expectedVersion,
        Empty(LabelBox.Text),
        ExclusiveBox.IsChecked == true,
        Empty(NotesBox.Text));

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
