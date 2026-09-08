using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>One allocation line the operator has added, as the list renders it.</summary>
internal sealed record AllocationRow(Guid ReceivableId, decimal Amount, string Description)
{
    public string AmountDisplay { get; init; } = string.Empty;
}

/// <summary>
/// Records that money moved, and says what is left over before anything is written.
/// </summary>
/// <remarks>
/// <para>
/// The dialog exists for its third figure. Received, allocated and unapplied are
/// shown together and update as lines are added, so a residual is a decision the
/// operator made rather than something they discover a fortnight later. Nothing is
/// auto-matched: assigning leftover cash to whichever receivable looks closest
/// would be the system guessing at a payer's intent and acting on it (ADR-0023).
/// </para>
/// <para>
/// The preview arithmetic here is exactly that. The server recomputes every figure
/// inside the transaction that writes it, and its answer is the one that counts.
/// </para>
/// </remarks>
public sealed partial class RecordPaymentDialog : ContentDialog
{
    private readonly IReadOnlyList<ReceivableResponse> _receivables;
    private readonly ObservableCollection<AllocationRow> _rows = [];

    public RecordPaymentDialog(IReadOnlyList<ReceivableResponse> receivables)
    {
        ArgumentNullException.ThrowIfNull(receivables);

        InitializeComponent();

        _receivables = receivables;
        AllocationList.ItemsSource = _rows;
        ReceivedPicker.Date = DateTimeOffset.UtcNow;

        foreach (ReceivableResponse receivable in receivables)
        {
            ReceivableBox.Items.Add(new ComboBoxItem
            {
                Content = Describe(receivable),
                Tag = receivable.Id,
            });
        }

        UpdatePreview();
    }

    /// <summary>Builds the request. Every monetary value carries its currency.</summary>
    public RecordPaymentRequest ToRequest() =>
        new(
            SelectedTag(DirectionBox) ?? "Incoming",
            new MoneyRequest(Amount, Currency),
            ReceivedPicker.Date is { } date
                ? DateOnly.FromDateTime(date.DateTime)
                : DateOnly.FromDateTime(DateTime.UtcNow),
            SelectedTag(MethodBox) ?? "BankTransfer",
            PayerPartyId: null,
            PayerName: Empty(PayerBox.Text),
            PayeePartyId: null,
            PayeeName: null,
            ExternalReference: Empty(ReferenceBox.Text),
            SourceSystem: null,
            Notes: Empty(NotesBox.Text),
            Allocations:
            [
                .. _rows.Select(row => new AllocationRequest(
                    row.ReceivableId, new MoneyRequest(row.Amount, Currency))),
            ]);

    private decimal Amount =>
        decimal.TryParse(
            AmountBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : 0m;

    private string Currency => (CurrencyBox.Text ?? string.Empty).Trim().ToUpperInvariant();

    private decimal Allocated => _rows.Sum(x => x.Amount);

    private void OnAmountChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void OnCurrencyChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void OnReceivedChanged(
        CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        UpdatePreview();

    private void OnAddAllocationClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
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
            receivable is null ? receivableId.ToString("D", CultureInfo.InvariantCulture) : Describe(receivable))
        {
            AmountDisplay = MoneyFormatting.Format(new MoneyResponse(amount, Currency)),
        });

        LineAmountBox.Text = string.Empty;

        UpdatePreview();
    }

    private void OnClearAllocationsClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _rows.Clear();
        UpdatePreview();
    }

    /// <summary>
    /// Recomputes the three figures and the sentence under them.
    /// </summary>
    /// <remarks>
    /// The sentence says what happens to a residual rather than only naming it.
    /// "Unapplied" on its own reads like a formatting state; saying that the money
    /// waits until somebody explains it makes the leftover a choice.
    /// </remarks>
    private void UpdatePreview()
    {
        if (PreviewText is null)
        {
            return;
        }

        decimal amount = Amount;
        decimal allocated = Allocated;
        decimal unapplied = amount - allocated;
        bool over = allocated > amount;

        ReceivedText.Text = MoneyFormatting.Format(new MoneyResponse(amount, Currency));
        AllocatedText.Text = MoneyFormatting.Format(new MoneyResponse(allocated, Currency));
        UnappliedText.Text = MoneyFormatting.Format(new MoneyResponse(unapplied, Currency));

        OverAllocatedBar.IsOpen = over;

        PreviewText.Text = over
            ? "The allocations come to more than arrived."
            : unapplied == 0m && amount > 0m
                ? "All of it is allocated."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{AllocatedText.Text} of {ReceivedText.Text} allocated. "
                        + $"{UnappliedText.Text} stays unapplied until somebody says what it is for.");

        IsPrimaryButtonEnabled = amount > 0m && Currency.Length == 3 && !over;
    }

    private static string Describe(ReceivableResponse receivable) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{receivable.ContractTitle} - {receivable.PayerDisplayName} - "
                + $"{receivable.Outstanding.Amount:N2} {receivable.Outstanding.Currency} outstanding");

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
