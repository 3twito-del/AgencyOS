using System;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Links a document or a message to a record it bears on.
/// </summary>
/// <remarks>
/// <para>
/// One dialog for both, because it asks the same question: which record, and why.
/// The fourteen targets are the ones the database can actually enforce a foreign
/// key against, so a link cannot end up pointing at a record that was removed or
/// that belongs to another tenant (ADR-0025).
/// </para>
/// <para>
/// The notice about access is not decoration. A link is the most natural place for
/// somebody to assume permission flows along it, and it does not: a privileged
/// document filed against a deal stays privileged.
/// </para>
/// </remarks>
public sealed partial class LinkRecordDialog : ContentDialog
{
    /// <summary>The targets the database can enforce a real foreign key against.</summary>
    private static readonly string[] Targets =
    [
        "Person", "Company", "TalentProfile", "Material", "Project", "Package",
        "Opportunity", "Submission", "Deal", "Offer", "Contract", "ContractVersion",
        "Invoice", "Payment",
    ];

    /// <param name="headline">What is being linked, in the operator's words.</param>
    public LinkRecordDialog(string headline)
    {
        InitializeComponent();

        HeadlineText.Text = headline;

        foreach (string target in Targets)
        {
            TargetBox.Items.Add(target);
        }
    }

    public string Target => TargetBox.SelectedItem as string ?? string.Empty;

    public Guid TargetId =>
        Guid.TryParse(TargetIdBox.Text, out Guid id) ? id : Guid.Empty;

    public string? Note =>
        string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

    private void OnChanged(object sender, object e) =>
        IsPrimaryButtonEnabled =
            TargetBox.SelectedItem is not null
            && Guid.TryParse(TargetIdBox.Text, out Guid id)
            && id != Guid.Empty;
}
