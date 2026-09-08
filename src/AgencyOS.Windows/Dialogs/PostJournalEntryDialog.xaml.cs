using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>One line of a hand-written entry, as the list renders it.</summary>
internal sealed record JournalLineRow(string Account, string Side, decimal Amount)
{
    public string AmountDisplay { get; init; } = string.Empty;
}

/// <summary>
/// Posts a balanced entry somebody wrote by hand.
/// </summary>
/// <remarks>
/// <para>
/// Every line carries a side and a positive amount. There are no signed numbers
/// here: a minus sign means one thing on an asset and the opposite on a liability,
/// and encoding direction that way is the mistake that turns a ledger into a table
/// of numbers nobody can reason about (ADR-0023).
/// </para>
/// <para>
/// The balance is checked here before the request goes out. The database enforces
/// it too, through a deferred constraint trigger, so this is a courtesy rather
/// than the guarantee.
/// </para>
/// </remarks>
public sealed partial class PostJournalEntryDialog : ContentDialog
{
    private readonly ObservableCollection<JournalLineRow> _rows = [];

    public PostJournalEntryDialog()
    {
        InitializeComponent();

        LineList.ItemsSource = _rows;
        OccurredPicker.Date = DateTimeOffset.UtcNow;

        UpdateBalance();
    }

    public PostJournalEntryRequest ToRequest() =>
        new(
            (MemoBox.Text ?? string.Empty).Trim(),
            Currency,
            OccurredPicker.Date is { } date
                ? DateOnly.FromDateTime(date.DateTime)
                : DateOnly.FromDateTime(DateTime.UtcNow),
            [
                .. _rows.Select(row => new JournalLineRequest(
                    row.Account, row.Side, new MoneyRequest(row.Amount, Currency))),
            ]);

    private string Currency => (CurrencyBox.Text ?? string.Empty).Trim().ToUpperInvariant();

    private decimal Debits =>
        _rows.Where(x => x.Side == "Debit").Sum(x => x.Amount);

    private decimal Credits =>
        _rows.Where(x => x.Side == "Credit").Sum(x => x.Amount);

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateBalance();

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        UpdateBalance();

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (AccountBox.SelectedItem is not ComboBoxItem { Tag: string account })
        {
            return;
        }

        string side = (SideBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Debit";

        if (!decimal.TryParse(
                LineAmountBox.Text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal amount)
            || amount <= 0m)
        {
            return;
        }

        _rows.Add(new JournalLineRow(account, side, amount)
        {
            AmountDisplay = MoneyFormatting.Format(new MoneyResponse(amount, Currency)),
        });

        LineAmountBox.Text = string.Empty;

        UpdateBalance();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        UpdateBalance();
    }

    private void UpdateBalance()
    {
        if (BalanceText is null)
        {
            return;
        }

        decimal debits = Debits;
        decimal credits = Credits;
        decimal difference = debits - credits;
        bool balanced = difference == 0m && _rows.Count > 0;

        DebitText.Text = MoneyFormatting.Format(new MoneyResponse(debits, Currency));
        CreditText.Text = MoneyFormatting.Format(new MoneyResponse(credits, Currency));

        BalanceText.Text = balanced
            ? "Balanced"
            : MoneyFormatting.Format(new MoneyResponse(difference, Currency));

        UnbalancedBar.IsOpen = _rows.Count > 0 && !balanced;

        // A single-sided entry is refused as well: two lines on the same side that
        // happen to sum to zero would be arithmetic without meaning.
        bool bothSides = _rows.Any(x => x.Side == "Debit") && _rows.Any(x => x.Side == "Credit");

        IsPrimaryButtonEnabled =
            balanced
            && bothSides
            && Currency.Length == 3
            && !string.IsNullOrWhiteSpace(MemoBox.Text)
            && OccurredPicker.Date is not null;
    }
}
