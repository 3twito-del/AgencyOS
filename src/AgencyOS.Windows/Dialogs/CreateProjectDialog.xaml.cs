using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Captures a new project.</summary>
/// <remarks>
/// A title and a type are the minimum that makes a project meaningful. Everything
/// else is optional, because an agency routinely opens a project on a phone call
/// and fills the rest in later.
/// </remarks>
public sealed partial class CreateProjectDialog : ContentDialog
{
    public CreateProjectDialog()
    {
        InitializeComponent();

        TypeBox.SelectedIndex = 0;
        StageBox.SelectedIndex = 0;
    }

    public CreateProjectRequest ToRequest() => new(
        TitleBox.Text.Trim(),
        SelectedTag(TypeBox) ?? "Other",
        Empty(WorkingTitleBox.Text),
        SelectedTag(StageBox),
        Empty(LoglineBox.Text),
        Synopsis: null,
        PrimaryCompanyId: null,
        double.IsNaN(YearBox.Value) ? null : (int)YearBox.Value);

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(TitleBox.Text);

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
