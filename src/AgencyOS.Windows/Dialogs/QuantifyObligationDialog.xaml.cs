using System;
using System.Globalization;
using AgencyOS.Contracts.Finance;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Puts a figure on an obligation that was recorded without one.
/// </summary>
/// <remarks>
/// <para>
/// The path a contingent bonus takes once its condition occurs, and the path a
/// participation takes if a statement ever arrives to value it. AgencyOS records
/// four honest answers to "how much" - a stated sum, a quantity at a rate, a sum
/// payable only if something happens, and genuinely unknown - and refuses to write
/// zero for the last two, because a false figure propagates into every total that
/// touches it (ADR-0023).
/// </para>
/// <para>
/// Which leaves the moment the answer arrives, and this is it. The server decides
/// whether the obligation is still in a state that can take a figure; this
/// collects the figure.
/// </para>
/// </remarks>
public sealed partial class QuantifyObligationDialog : ContentDialog
{
    public QuantifyObligationDialog(MonetaryObligationResponse obligation)
    {
        ArgumentNullException.ThrowIfNull(obligation);

        InitializeComponent();

        HeadlineText.Text = obligation.Description ?? obligation.Category;

        string due = obligation.DueOn is { } dueOn
            ? string.Create(CultureInfo.InvariantCulture, $", due {dueOn:yyyy-MM-dd}.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $". {obligation.DueUnresolvedReason ?? "No due date this build can work out."}");

        ObligationText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{obligation.PayerDisplayName} owes {obligation.PayeeDisplayName}. "
                + $"Recorded as {Describe(obligation.AmountKind)}{due}");

        // A formula obligation already carries the currency it will settle in, so
        // the operator is spared retyping the one part that is not in question.
        if (obligation.UnitAmount is { } unit)
        {
            CurrencyBox.Text = unit.Currency;
        }

        UpdateReady();
    }

    public QuantifyObligationRequest ToRequest(int expectedVersion) =>
        new(Money()!, expectedVersion, Empty(ReasonBox.Text));

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void UpdateReady()
    {
        if (AmountBox is null)
        {
            return;
        }

        IsPrimaryButtonEnabled = Money() is not null;
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

    /// <summary>The kind in the operator's words, not the enumeration's.</summary>
    private static string Describe(string amountKind) => amountKind switch
    {
        "Formula" => "a quantity at a rate",
        "Contingent" => "payable only if something happens",
        "Unknown" => "an amount AgencyOS cannot work out",
        _ => "a stated sum",
    };

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
