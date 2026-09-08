using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Applies a payment's unapplied cash to receivables.
/// </summary>
/// <remarks>
/// The residual is the reason this screen exists separately from recording the
/// payment: money often arrives before anybody knows what it settles, and the
/// honest handling is to hold it as unapplied until a person says. Nothing here
/// is inferred from amounts, dates or payer names (ADR-0023).
/// </remarks>
public sealed partial class AllocatePaymentDialog : ContentDialog
{
    private readonly PaymentResponse _payment;
    private readonly IReadOnlyList<ReceivableResponse> _receivables;
    private readonly ObservableCollection<AllocationRow> _rows = [];

    public AllocatePaymentDialog(
        PaymentResponse payment,
        IReadOnlyList<ReceivableResponse> receivables)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(receivables);

        InitializeComponent();

        _payment = payment;
        _receivables = receivables;
        AllocationList.ItemsSource = _rows;

        HeadlineText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{MoneyFormatting.Format(payment.Amount)} from {payment.PayerDisplayName}, "
                + $"received {payment.ReceivedOn:yyyy-MM-dd}.");

        UnappliedText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{MoneyFormatting.Format(payment.Unapplied)} has not been applied to anything yet.");

        // Only receivables in the payment's own currency. Allocating dollars to a
        // euro receivable is not a rounding question, it is a different amount, and
        // AgencyOS holds no rate that would make the conversion honest.
        foreach (ReceivableResponse receivable in receivables.Where(
            x => string.Equals(x.Outstanding.Currency, payment.Amount.Currency, StringComparison.Ordinal)))
        {
            ReceivableBox.Items.Add(new ComboBoxItem
            {
                Content = Describe(receivable),
                Tag = receivable.Id,
            });
        }

        UpdatePreview();
    }

    public AllocatePaymentRequest ToRequest() =>
        new(
            [
                .. _rows.Select(row => new AllocationRequest(
                    row.ReceivableId, new MoneyRequest(row.Amount, _payment.Amount.Currency))),
            ],
            _payment.Version);

    private decimal Allocated => _rows.Sum(x => x.Amount);

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (ReceivableBox.SelectedItem is not ComboBoxItem { Tag: Guid receivableId })
        {
            return;
        }

        if (!decimal.TryParse(
                LineAmountBox.Text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal amount)
            || amount <= 0m)
        {
            return;
        }

        ReceivableResponse? receivable = _receivables.FirstOrDefault(x => x.Id == receivableId);

        _rows.Add(new AllocationRow(
            receivableId,
            amount,
            receivable is null
                ? receivableId.ToString("D", CultureInfo.InvariantCulture)
                : Describe(receivable))
        {
            AmountDisplay = MoneyFormatting.Format(
                new MoneyResponse(amount, _payment.Amount.Currency)),
        });

        LineAmountBox.Text = string.Empty;

        UpdatePreview();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewText is null)
        {
            return;
        }

        decimal allocated = Allocated;
        decimal remaining = _payment.Unapplied.Amount - allocated;
        bool over = allocated > _payment.Unapplied.Amount;

        OverBar.IsOpen = over;

        PreviewText.Text = over
            ? "More allocated than this payment has left."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Allocating {MoneyFormatting.Format(new MoneyResponse(allocated, _payment.Amount.Currency))}. "
                    + $"{MoneyFormatting.Format(new MoneyResponse(remaining, _payment.Amount.Currency))} would stay unapplied.");

        IsPrimaryButtonEnabled = _rows.Count > 0 && !over;
    }

    private static string Describe(ReceivableResponse receivable) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{receivable.ContractTitle} - {receivable.PayerDisplayName} - "
                + $"{receivable.Outstanding.Amount:N2} {receivable.Outstanding.Currency} outstanding");
}
