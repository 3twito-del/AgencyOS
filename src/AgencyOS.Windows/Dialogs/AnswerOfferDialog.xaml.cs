using System;
using System.Globalization;
using AgencyOS.Contracts.Deals;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records what happened to the offer on the table.
/// </summary>
/// <remarks>
/// <para>
/// Four distinct facts about different actors. Rejected is the recipient saying
/// no, withdrawn is the proposer pulling it, and expired is a stated lapse having
/// passed. None of them is "nobody replied": silence is never recorded, here or
/// anywhere else in the system (ADR-0020, ADR-0021).
/// </para>
/// <para>
/// The default button is Cancel rather than Record, and acceptance carries an
/// explicit warning about what it does and does not mean. Not theatre: accepting
/// fixes the agreed commercial snapshot, and undoing it needs an explicit reopen.
/// </para>
/// </remarks>
public sealed partial class AnswerOfferDialog : ContentDialog
{
    public AnswerOfferDialog(OfferResponse offer, string counterparty)
    {
        ArgumentNullException.ThrowIfNull(offer);

        InitializeComponent();

        HeadlineText.Text = offer.Direction == "Inbound"
            ? $"The offer {counterparty} made"
            : $"The offer we made to {counterparty}";

        OfferText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Offer {offer.Sequence}, recorded {offer.CommunicatedAt:yyyy-MM-dd}. {offer.Summary}");

        TermList.ItemsSource = offer.Terms;

        // An empty list is what a caller without deals.economics.read sees, and
        // also what an offer of purely structural terms looks like. The wording
        // does not distinguish the two, by design.
        TermsCaption.Text = offer.Terms.Count == 0
            ? "No terms to show."
            : "The terms being answered:";

        UpdateWarning();
    }

    public AnswerOfferRequest ToRequest(int expectedVersion)
    {
        DealFollowUpRequest? followUp =
            FollowUpBox.IsChecked == true && !string.IsNullOrWhiteSpace(FollowUpTitleBox.Text)
                ? new DealFollowUpRequest(
                    FollowUpTitleBox.Text.Trim(), DateTimeOffset.UtcNow.AddDays(3))
                : null;

        return new AnswerOfferRequest(
            SelectedTag(AnswerBox) ?? "Reject",
            expectedVersion,
            Empty(ReasonBox.Text),
            followUp);
    }

    private void OnAnswerChanged(object sender, SelectionChangedEventArgs e) => UpdateWarning();

    private void UpdateWarning()
    {
        if (AcceptanceBar is null)
        {
            return;
        }

        bool accepting = SelectedTag(AnswerBox) == "Accept";

        AcceptanceBar.IsOpen = accepting;

        // Named for what it does, so a keyboard user reading the button knows
        // which of the four they are about to record.
        PrimaryButtonText = accepting ? "Accept and agree terms" : "Record";
    }

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
