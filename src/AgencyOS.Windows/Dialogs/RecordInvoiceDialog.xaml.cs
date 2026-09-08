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
/// Records an invoice against receivables that already exist.
/// </summary>
/// <remarks>
/// <para>
/// An invoice points at money that is already owed. It does not create the debt,
/// and not every receivable needs one: a payer settling on a schedule the contract
/// sets may never see an invoice at all. The dialog therefore builds from
/// receivables rather than from amounts somebody types in (ADR-0023).
/// </para>
/// <para>
/// The word on the button is "record". There is no document here and no transport,
/// so a button offering to send would be describing something the build cannot do.
/// </para>
/// </remarks>
public sealed partial class RecordInvoiceDialog : ContentDialog
{
    private readonly Guid _debtorPartyId;
    private readonly IReadOnlyList<ReceivableResponse> _receivables;
    private readonly ObservableCollection<AllocationRow> _rows = [];

    public RecordInvoiceDialog(
        string contractTitle,
        Guid debtorPartyId,
        IReadOnlyList<ReceivableResponse> receivables)
    {
        ArgumentNullException.ThrowIfNull(receivables);

        InitializeComponent();

        _debtorPartyId = debtorPartyId;
        _receivables = receivables;
        LineList.ItemsSource = _rows;
        HeadlineText.Text = contractTitle;

        foreach (ReceivableResponse receivable in receivables)
        {
            ReceivableBox.Items.Add(new ComboBoxItem
            {
                Content = Describe(receivable),
                Tag = receivable.Id,
            });
        }

        if (receivables.Count > 0)
        {
            CurrencyBox.Text = receivables[0].Outstanding.Currency;
        }

        UpdateTotal();
    }

    public RecordInvoiceRequest ToRequest() =>
        new(
            _debtorPartyId,
            Currency,
            [
                .. _rows.Select(row => new InvoiceLineRequest(
                    row.ReceivableId, new MoneyRequest(row.Amount, Currency), row.Description)),
            ],
            Reference: Empty(ReferenceBox.Text),
            DueOn: DuePicker.Date is { } due ? DateOnly.FromDateTime(due.DateTime) : null,
            ExternalReference: null,
            Notes: Empty(NotesBox.Text));

    private string Currency => (CurrencyBox.Text ?? string.Empty).Trim().ToUpperInvariant();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateTotal();

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (ReceivableBox.SelectedItem is not ComboBoxItem { Tag: Guid receivableId })
        {
            return;
        }

        ReceivableResponse? receivable = _receivables.FirstOrDefault(x => x.Id == receivableId);

        if (receivable is null)
        {
            return;
        }

        // An empty amount bills what is outstanding, which is the ordinary case.
        decimal amount =
            decimal.TryParse(
                LineAmountBox.Text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal typed)
            && typed > 0m
                ? typed
                : receivable.Outstanding.Amount;

        _rows.Add(new AllocationRow(receivableId, amount, Describe(receivable))
        {
            AmountDisplay = MoneyFormatting.Format(new MoneyResponse(amount, Currency)),
        });

        LineAmountBox.Text = string.Empty;

        UpdateTotal();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        UpdateTotal();
    }

    private void UpdateTotal()
    {
        if (TotalText is null)
        {
            return;
        }

        decimal total = _rows.Sum(x => x.Amount);

        TotalText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Total {MoneyFormatting.Format(new MoneyResponse(total, Currency))} "
                + $"across {_rows.Count} line(s).");

        // A reference is required to issue but not to record a draft, so the
        // primary button only wants lines and a currency.
        IsPrimaryButtonEnabled = _rows.Count > 0 && Currency.Length == 3;
    }

    private static string Describe(ReceivableResponse receivable) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{receivable.ContractTitle} - {receivable.PayerDisplayName} - "
                + $"{receivable.Outstanding.Amount:N2} {receivable.Outstanding.Currency} outstanding");

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
