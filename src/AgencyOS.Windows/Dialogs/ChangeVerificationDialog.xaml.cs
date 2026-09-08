using System;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records what corroboration a claim has.
/// </summary>
/// <remarks>
/// Four states and no fifth. The absent one is <c>Verified</c>: a claim that other
/// evidence agrees with is corroborated, which is a statement about the evidence
/// rather than about the world. Offering a word that reads as "true" would invite
/// exactly the reading M11 exists to prevent (§1, ADR-0030).
/// </remarks>
public sealed partial class ChangeVerificationDialog : ContentDialog
{
    /// <param name="claim">The claim this is about, so the operator can reread it.</param>
    /// <param name="current">The state it is in now.</param>
    public ChangeVerificationDialog(string claim, string current)
    {
        InitializeComponent();

        ClaimText.Text = claim;
        Select(current);
        ApplyMeaning();
    }

    /// <summary>The chosen state.</summary>
    public string Verification =>
        (VerificationBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Unverified";

    /// <summary>The note, or null when the operator left it empty.</summary>
    public string? Note =>
        string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

    private void OnStateChanged(object sender, SelectionChangedEventArgs e) => ApplyMeaning();

    private void ApplyMeaning() => MeaningText.Text = Verification switch
    {
        "Corroborated" =>
            "Somebody found independent evidence that agrees. The claim stays a claim.",
        "Disputed" =>
            "Somebody found evidence that disagrees. The claim stays on the record with "
                + "the disagreement attached, because removing it would lose the fact "
                + "that it was once believed.",
        "Retracted" =>
            "The source withdrew it. The claim is kept and marked, never deleted: what "
                + "the agency acted on last month is part of the record.",
        _ => "Nothing else has been found either way. This is the honest default.",
    };

    private void Select(string state)
    {
        for (int index = 0; index < VerificationBox.Items.Count; index++)
        {
            if (VerificationBox.Items[index] is ComboBoxItem item
                && item.Tag as string == state)
            {
                VerificationBox.SelectedIndex = index;
                return;
            }
        }

        VerificationBox.SelectedIndex = 0;
    }
}
