using AgencyOS.Contracts.Opportunities;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>Moves a target through the pipeline.</summary>
/// <remarks>
/// One dialog rather than an interest button, a pass button and a withdraw button.
/// They are the same act - moving to a stage the transition table allows - and
/// three buttons would restate that table in the surface, where it would drift.
/// </remarks>
public sealed partial class MoveTargetDialog : ContentDialog
{
    public MoveTargetDialog(string currentStage)
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

    public MoveOpportunityTargetRequest ToRequest(int expectedVersion) => new(
        SelectedTag(StageBox) ?? "Identified",
        expectedVersion,
        OccurredAt: null,
        Empty(NoteBox.Text));

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
