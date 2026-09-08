using System;
using System.Collections.Generic;
using System.Globalization;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records a sum an operative contract says is payable.
/// </summary>
/// <remarks>
/// <para>
/// The four amount kinds are kept apart because they are different facts. A fixed
/// sum is a number; a formula is a quantity and a rate that will produce one; a
/// contingent amount waits on something happening; and an unknown amount is one
/// nobody can value yet. Only the first two can be turned into a receivable, and
/// the dialog does not pretend otherwise by defaulting the others to zero
/// (ADR-0023).
/// </para>
/// <para>
/// The due rule is the M8 deadline shape, reused unchanged. "The contract does not
/// say" is a supported answer, and it produces an obligation with no due date
/// rather than one silently due today.
/// </para>
/// </remarks>
public sealed partial class RecordMonetaryObligationDialog : ContentDialog
{
    private readonly Guid _contractVersionId;

    public RecordMonetaryObligationDialog(
        string contractTitle,
        Guid contractVersionId,
        IReadOnlyList<ContractPartyResponse> parties)
    {
        ArgumentNullException.ThrowIfNull(parties);

        InitializeComponent();

        _contractVersionId = contractVersionId;
        HeadlineText.Text = contractTitle;

        foreach (ContractPartyResponse party in parties)
        {
            PayerBox.Items.Add(new ComboBoxItem { Content = party.DisplayName, Tag = party.Id });
            PayeeBox.Items.Add(new ComboBoxItem { Content = party.DisplayName, Tag = party.Id });
        }

        UpdateReady();
    }

    public RecordMonetaryObligationRequest ToRequest()
    {
        string kind = SelectedTag(AmountKindBox) ?? "Fixed";
        string dueKind = SelectedTag(DueKindBox) ?? "Absolute";

        return new RecordMonetaryObligationRequest(
            _contractVersionId,
            SelectedGuid(PayerBox),
            SelectedGuid(PayeeBox),
            SelectedTag(CategoryBox) ?? "Compensation",
            kind,
            new DueRuleRequest(
                dueKind,
                dueKind == "Absolute" && DuePicker.Date is { } due
                    ? DateOnly.FromDateTime(due.DateTime)
                    : null,
                Description: dueKind == "Unstructured" ? Empty(DueWordingBox.Text) : null),
            kind == "Fixed" ? Money(AmountBox.Text, CurrencyBox.Text) : null,
            kind == "Formula" ? Integer(QuantityBox.Text) : null,
            kind == "Formula" ? Money(UnitAmountBox.Text, CurrencyBox.Text) : null,
            kind == "Formula" ? SelectedTag(UnitBox) : null,
            kind == "Contingent" ? Empty(ConditionBox.Text) : null,
            AnchorDate: null,
            SourceObligationId: null,
            SourceTermCode: null,
            Description: Empty(DescriptionBox.Text));
    }

    private void OnFieldChanged(object sender, SelectionChangedEventArgs e) => UpdateReady();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void OnDueDateChanged(
        CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        UpdateReady();

    private void OnAmountKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FixedPanel is null)
        {
            return;
        }

        string kind = SelectedTag(AmountKindBox) ?? "Fixed";

        FixedPanel.Visibility = kind == "Fixed" ? Visibility.Visible : Visibility.Collapsed;
        FormulaPanel.Visibility = kind == "Formula" ? Visibility.Visible : Visibility.Collapsed;
        ConditionBox.Visibility = kind == "Contingent" ? Visibility.Visible : Visibility.Collapsed;

        // The currency is still required for a formula: a rate without one is a
        // number nobody can add up.
        CurrencyBox.Visibility = kind is "Fixed" or "Formula" ? Visibility.Visible : Visibility.Collapsed;

        UpdateReady();
    }

    private void OnDueKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DuePicker is null)
        {
            return;
        }

        bool absolute = (SelectedTag(DueKindBox) ?? "Absolute") == "Absolute";

        DuePicker.Visibility = absolute ? Visibility.Visible : Visibility.Collapsed;
        DueWordingBox.Visibility = absolute ? Visibility.Collapsed : Visibility.Visible;

        UpdateReady();
    }

    private void UpdateReady()
    {
        if (DescriptionBox is null)
        {
            return;
        }

        string kind = SelectedTag(AmountKindBox) ?? "Fixed";
        string currency = (CurrencyBox.Text ?? string.Empty).Trim();

        bool amountReady = kind switch
        {
            "Fixed" => Money(AmountBox.Text, currency) is not null,
            "Formula" =>
                Integer(QuantityBox.Text) is > 0
                && Money(UnitAmountBox.Text, currency) is not null,

            // Contingent and unknown carry no figure at all. That is the point.
            _ => true,
        };

        bool dueReady = (SelectedTag(DueKindBox) ?? "Absolute") == "Absolute"
            ? DuePicker.Date is not null

            // Words, or nothing records what the contract requires.
            : !string.IsNullOrWhiteSpace(DueWordingBox.Text);

        IsPrimaryButtonEnabled =
            PayerBox.SelectedItem is not null
            && PayeeBox.SelectedItem is not null
            && !string.IsNullOrWhiteSpace(DescriptionBox.Text)
            && amountReady
            && dueReady;
    }

    private static MoneyRequest? Money(string? amount, string? currency)
    {
        string code = (currency ?? string.Empty).Trim().ToUpperInvariant();

        return code.Length == 3
            && decimal.TryParse(
                amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            && parsed > 0m
                ? new MoneyRequest(parsed, code)
                : null;
    }

    private static int? Integer(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static Guid SelectedGuid(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag is Guid id ? id : Guid.Empty;
}
