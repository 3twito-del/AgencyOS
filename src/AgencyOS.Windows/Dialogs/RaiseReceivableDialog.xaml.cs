using System;
using System.Globalization;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Raises a receivable from a quantified obligation.
/// </summary>
/// <remarks>
/// The beneficiary is the field that decides everything downstream. A client
/// receivable that is collected becomes money the agency holds and owes onward,
/// posted to client funds payable and never to revenue; an agency receivable is
/// the agency's own. Getting it wrong would report somebody else's money as income
/// (ADR-0023).
/// </remarks>
public sealed partial class RaiseReceivableDialog : ContentDialog
{
    private Guid? _clientPersonId;

    public RaiseReceivableDialog(MonetaryObligationResponse obligation)
    {
        ArgumentNullException.ThrowIfNull(obligation);

        InitializeComponent();

        HeadlineText.Text = obligation.Description ?? obligation.Category;

        // The due clause says which of the honest answers applies rather than
        // showing a blank where a date would be.
        string due = obligation.DueOn is { } dueOn
            ? string.Create(CultureInfo.InvariantCulture, $", due {dueOn:yyyy-MM-dd}.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $". {obligation.DueUnresolvedReason ?? "No due date this build can work out."}");

        ObligationText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{obligation.PayerDisplayName} owes {obligation.PayeeDisplayName}. "
                + $"{MoneyFormatting.FormatOrUnknown(obligation.Amount)}{due}");

        if (obligation.Amount is { } amount)
        {
            AmountBox.Text = amount.Amount.ToString("0.##", CultureInfo.InvariantCulture);
            CurrencyBox.Text = amount.Currency;
        }

        UpdateBeneficiaryNote();
        UpdateReady();
    }

    /// <summary>The client the money belongs to, when it is client money.</summary>
    public Guid? ClientPersonId
    {
        get => _clientPersonId;
        set => _clientPersonId = value;
    }

    public RaiseReceivableRequest ToRequest() =>
        new(
            SelectedTag(BeneficiaryBox) ?? "Client",
            Money(),
            DuePicker.Date is { } due ? DateOnly.FromDateTime(due.DateTime) : null,
            _clientPersonId,
            RepresentationId: null,
            Reference: Empty(ReferenceBox.Text),
            Notes: Empty(NotesBox.Text));

    private void OnBeneficiaryChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateBeneficiaryNote();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void UpdateBeneficiaryNote()
    {
        if (BeneficiaryBar is null)
        {
            return;
        }

        bool client = (SelectedTag(BeneficiaryBox) ?? "Client") == "Client";

        BeneficiaryBar.Title = client ? "Client funds" : "Agency funds";

        BeneficiaryBar.Message = client
            ? "When this is collected the agency holds the money and owes it onward. It is not agency revenue, and the ledger posts it to client funds payable."
            : "When this is collected it is the agency's own money.";
    }

    private void UpdateReady()
    {
        if (AmountBox is null)
        {
            return;
        }

        // Blank amount is valid: the receivable then takes the obligation's own
        // figure. A partly filled amount is not.
        bool blank = string.IsNullOrWhiteSpace(AmountBox.Text)
            && string.IsNullOrWhiteSpace(CurrencyBox.Text);

        IsPrimaryButtonEnabled = blank || Money() is not null;
    }

    private MoneyRequest? Money()
    {
        string currency = (CurrencyBox.Text ?? string.Empty).Trim().ToUpperInvariant();

        return currency.Length == 3
            && decimal.TryParse(
                AmountBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            && parsed > 0m
                ? new MoneyRequest(parsed, currency)
                : null;
    }

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
