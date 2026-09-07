using AgencyOS.Contracts.Projects;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Moves a project to a different development stage.</summary>
/// <remarks>
/// Separate from changing the operational status, because they are separate facts.
/// A dialog that set both would let somebody cancel a project and advance its stage
/// in one action, with one reason covering two decisions (ADR-0018).
/// </remarks>
public sealed partial class ChangeProjectStageDialog : ContentDialog
{
    public ChangeProjectStageDialog(string currentStage)
    {
        InitializeComponent();

        CurrentText.Text = $"Currently {currentStage}";

        for (int index = 0; index < StageBox.Items.Count; index++)
        {
            if (StageBox.Items[index] is ComboBoxItem item && (item.Tag as string) == currentStage)
            {
                StageBox.SelectedIndex = index;
                return;
            }
        }

        StageBox.SelectedIndex = 0;
    }

    public ChangeProjectStageRequest ToRequest(int expectedVersion) => new(
        SelectedTag(StageBox) ?? "Concept",
        expectedVersion,
        Empty(ReasonBox.Text));

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
