using System;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Opens a contract against a negotiation whose terms are agreed.
/// </summary>
/// <remarks>
/// <para>
/// The accepted offer is asked for explicitly and never inferred. It is the
/// baseline every later draft is reconciled against, so a contract that guessed
/// which agreement it papered would produce a comparison against the wrong thing
/// (ADR-0022).
/// </para>
/// <para>
/// Choosing Amendment surfaces a note rather than changing the form: an amendment
/// is a separate executed instrument that is linked to the paper it changes, not a
/// later drafting version of it.
/// </para>
/// </remarks>
public sealed partial class CreateContractDialog : ContentDialog
{
    public CreateContractDialog() => InitializeComponent();

    /// <summary>Opens the dialog with the negotiation already known.</summary>
    public CreateContractDialog(Guid dealId, Guid acceptedOfferId, string? suggestedTitle = null)
    {
        InitializeComponent();

        DealIdBox.Text = dealId.ToString();
        OfferIdBox.Text = acceptedOfferId.ToString();

        if (!string.IsNullOrWhiteSpace(suggestedTitle))
        {
            TitleBox.Text = suggestedTitle;
        }

        UpdateReady();
    }

    public CreateContractRequest ToRequest() =>
        new(
            Guid.Parse(DealIdBox.Text.Trim()),
            Guid.Parse(OfferIdBox.Text.Trim()),
            TitleBox.Text.Trim(),
            SelectedTag(KindBox) ?? "LongForm",
            Guid.Parse(OwnerIdBox.Text.Trim()),
            Empty(ReferenceBox.Text),
            Empty(SummaryBox.Text),
            Empty(AnalysisBox.Text),
            Empty(StrategyBox.Text),
            SelectedTag(PrivilegeBox) ?? "Ordinary");

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void UpdateReady()
    {
        if (AmendmentBar is not null)
        {
            AmendmentBar.IsOpen = SelectedTag(KindBox) == "Amendment";
        }

        IsPrimaryButtonEnabled =
            Guid.TryParse(DealIdBox.Text?.Trim(), out _)
            && Guid.TryParse(OfferIdBox.Text?.Trim(), out _)
            && Guid.TryParse(OwnerIdBox.Text?.Trim(), out _)
            && !string.IsNullOrWhiteSpace(TitleBox.Text);
    }

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
