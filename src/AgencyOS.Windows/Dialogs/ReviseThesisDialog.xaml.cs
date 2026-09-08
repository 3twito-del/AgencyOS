using System;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records a new position for a thesis.
/// </summary>
/// <remarks>
/// The box opens pre-filled with the current proposition, so a revision starts from
/// what is actually held rather than from a blank page. "What changed your mind" is
/// the field that makes the history worth keeping: a sequence of propositions with
/// no reasons between them records that somebody moved without recording why.
/// </remarks>
public sealed partial class ReviseThesisDialog : ContentDialog
{
    public ReviseThesisDialog(string proposition, string confidence)
    {
        InitializeComponent();

        PreviousText.Text = "Currently: " + proposition;
        PropositionBox.Text = proposition;

        Select(confidence);
        OnRequiredChanged(this, null!);
    }

    public string Proposition => (PropositionBox.Text ?? string.Empty).Trim();

    public string Confidence =>
        (ConfidenceBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Unstated";

    public string? Rationale =>
        string.IsNullOrWhiteSpace(RationaleBox.Text) ? null : RationaleBox.Text.Trim();

    public string? ChangeNote =>
        string.IsNullOrWhiteSpace(ChangeNoteBox.Text) ? null : ChangeNoteBox.Text.Trim();

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(PropositionBox.Text);

    private void Select(string confidence)
    {
        for (int index = 0; index < ConfidenceBox.Items.Count; index++)
        {
            if (ConfidenceBox.Items[index] is ComboBoxItem item
                && item.Tag as string == confidence)
            {
                ConfidenceBox.SelectedIndex = index;
                return;
            }
        }

        ConfidenceBox.SelectedIndex = 0;
    }
}
