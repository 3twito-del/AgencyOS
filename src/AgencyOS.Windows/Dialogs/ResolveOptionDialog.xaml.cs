using System;
using System.Globalization;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records what became of an option.
/// </summary>
/// <remarks>
/// <para>
/// Five outcomes, all of them acts somebody performed. Expiry is one of them
/// rather than something the system does: an option past its deadline stays
/// available until a person records that it lapsed, because whether a missed
/// deadline ended the right is a legal position, not arithmetic on a date
/// (ADR-0022).
/// </para>
/// <para>
/// Nothing here infers an exercise from a payment either. Money arriving is M9's
/// subject and would be, at most, evidence somebody uses when they decide to
/// record an exercise.
/// </para>
/// </remarks>
public sealed partial class ResolveOptionDialog : ContentDialog
{
    public ResolveOptionDialog(ContractOptionResponse option)
    {
        ArgumentNullException.ThrowIfNull(option);

        InitializeComponent();

        HeadlineText.Text = option.Subject;

        DeadlineText.Text = option.DeadlineOn is { } deadline
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{option.Kind}, held by {option.HolderDisplayName}, "
                    + $"exercisable until {deadline:yyyy-MM-dd}.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{option.Kind}, held by {option.HolderDisplayName}. "
                    + $"{option.DeadlineUnresolvedReason ?? "No deadline recorded."}");

        // Expiry needs a deadline the system can point at. Without one there is no
        // date to record it against, so the choice is not offered.
        if (option.DeadlineOn is null)
        {
            foreach (object item in OutcomeBox.Items)
            {
                if (item is ComboBoxItem entry && (entry.Tag as string) == "Expired")
                {
                    entry.IsEnabled = false;
                }
            }
        }

        UpdateForOutcome();
    }

    public ResolveOptionRequest ToRequest(int expectedVersion)
    {
        LegalFollowUpRequest? followUp =
            FollowUpBox.IsChecked == true && !string.IsNullOrWhiteSpace(FollowUpTitleBox.Text)
                ? new LegalFollowUpRequest(
                    FollowUpTitleBox.Text.Trim(), DateTimeOffset.UtcNow.AddDays(3))
                : null;

        return new ResolveOptionRequest(
            SelectedTag(OutcomeBox) ?? "Exercised",
            expectedVersion,
            OccurredPicker.Date is { } date && OccurredPicker.Visibility == Visibility.Visible
                ? DateOnly.FromDateTime(date.DateTime)
                : null,
            Empty(ReasonBox.Text),
            followUp);
    }

    private void OnOutcomeChanged(object sender, SelectionChangedEventArgs e) => UpdateForOutcome();

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        UpdateReady();

    private void UpdateForOutcome()
    {
        if (ExpiryBar is null)
        {
            return;
        }

        string outcome = SelectedTag(OutcomeBox) ?? "Exercised";
        bool needsDate = outcome is "Exercised" or "Declined" or "Waived";

        ExpiryBar.IsOpen = outcome == "Expired";
        OccurredPicker.Visibility = needsDate ? Visibility.Visible : Visibility.Collapsed;

        UpdateReady();
    }

    private void UpdateReady() =>
        IsPrimaryButtonEnabled =
            OccurredPicker.Visibility == Visibility.Collapsed || OccurredPicker.Date is not null;

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
