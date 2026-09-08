using System;
using System.Globalization;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records a deduction that reduces what will ever arrive on a receivable.
/// </summary>
/// <remarks>
/// The dialog opens pre-filled with the receivable's current variance, because
/// that is almost always the figure being explained. It is a suggestion the
/// operator can overwrite, not an inference: AgencyOS does not decide that a
/// shortfall was withholding, and an unexplained gap stays unexplained (ADR-0023).
/// </remarks>
public sealed partial class RecordAdjustmentDialog : ContentDialog
{
    private readonly int _expectedVersion;

    public RecordAdjustmentDialog(ReceivableResponse receivable, MoneyResponse? variance = null)
    {
        ArgumentNullException.ThrowIfNull(receivable);

        InitializeComponent();

        _expectedVersion = receivable.Version;

        HeadlineText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{receivable.ContractTitle} - {receivable.PayerDisplayName}");

        VarianceText.Text = variance is null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{MoneyFormatting.Format(receivable.Outstanding)} outstanding of "
                    + $"{MoneyFormatting.Format(receivable.OriginalAmount)}.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{MoneyFormatting.Format(variance)} is unexplained on this receivable.");

        CurrencyBox.Text = receivable.Outstanding.Currency;
        OccurredPicker.Date = DateTimeOffset.UtcNow;

        if (variance is { Amount: > 0m })
        {
            AmountBox.Text = variance.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        }

        UpdateReady();
    }

    public RecordAdjustmentRequest ToRequest() =>
        new(
            SelectedTag(KindBox) ?? "Other",
            new MoneyRequest(Amount, Currency),
            (DescriptionBox.Text ?? string.Empty).Trim(),
            OccurredPicker.Date is { } date
                ? DateOnly.FromDateTime(date.DateTime)
                : DateOnly.FromDateTime(DateTime.UtcNow),
            _expectedVersion,
            PaymentId: null,
            ExternalReference: Empty(ExternalReferenceBox.Text));

    private decimal Amount =>
        decimal.TryParse(
            AmountBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : 0m;

    private string Currency => (CurrencyBox.Text ?? string.Empty).Trim().ToUpperInvariant();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        UpdateReady();

    private void UpdateReady() =>
        IsPrimaryButtonEnabled =
            Amount > 0m
            && Currency.Length == 3
            && !string.IsNullOrWhiteSpace(DescriptionBox?.Text)
            && OccurredPicker?.Date is not null;

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
