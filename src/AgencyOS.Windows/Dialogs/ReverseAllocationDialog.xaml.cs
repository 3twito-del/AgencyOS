using System;
using System.Globalization;
using System.Linq;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Undoes the decision that a payment answered a particular receivable.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from reversing the payment, and the distinction is the reason this
/// exists separately. Reversing a payment says the money never arrived; reversing
/// an allocation says it arrived and was applied to the wrong thing. Recording the
/// second as the first would take a real receipt off the books (ADR-0023).
/// </para>
/// <para>
/// So the payment is not touched. The allocation is marked reversed with its
/// reason, its money returns to unapplied and can be applied again, and both rows
/// stay readable.
/// </para>
/// </remarks>
public sealed partial class ReverseAllocationDialog : ContentDialog
{
    /// <param name="payment">The payment whose allocations are on offer.</param>
    public ReverseAllocationDialog(PaymentResponse payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        InitializeComponent();

        HeadlineText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{MoneyFormatting.Format(payment.Amount)} from {payment.PayerDisplayName}, "
                + $"received {payment.ReceivedOn:yyyy-MM-dd}");

        // Only the ones still standing. An allocation already reversed has nothing
        // left to reverse, and offering it would be a refusal waiting to happen.
        AllocationBox.ItemsSource = payment.Allocations
            .Where(x => x.IsApplied)
            .Select(x => new AllocationChoice(x.Id, Describe(x)))
            .ToArray();

        if (AllocationBox.Items.Count == 1)
        {
            AllocationBox.SelectedIndex = 0;
        }

        UpdateReady();
    }

    /// <summary>The allocation chosen, or empty when none is.</summary>
    public Guid SelectedAllocationId =>
        (AllocationBox.SelectedItem as AllocationChoice)?.Id ?? Guid.Empty;

    /// <summary>The reason, trimmed. Never empty: the button does not enable without one.</summary>
    public string Reason => (ReasonBox.Text ?? string.Empty).Trim();

    public ReverseAllocationRequest ToRequest(int expectedVersion) =>
        new(SelectedAllocationId, Reason, expectedVersion);

    private void OnAllocationChanged(object sender, SelectionChangedEventArgs e) => UpdateReady();

    private void OnReasonChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void UpdateReady()
    {
        if (ReasonBox is null)
        {
            return;
        }

        IsPrimaryButtonEnabled =
            SelectedAllocationId != Guid.Empty && !string.IsNullOrWhiteSpace(ReasonBox.Text);
    }

    /// <summary>What the allocation went to, in enough detail to tell two apart.</summary>
    private static string Describe(PaymentAllocationResponse allocation) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{MoneyFormatting.Format(allocation.Amount)} to "
                + $"{allocation.ReceivableReference ?? allocation.ContractTitle} "
                + $"({allocation.AppliedAt:yyyy-MM-dd})");

    /// <summary>One allocation, as the picker shows it.</summary>
    private sealed record AllocationChoice(Guid Id, string Label);
}
