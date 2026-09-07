using System;
using System.Globalization;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records what became of an obligation.
/// </summary>
/// <remarks>
/// <para>
/// Five outcomes about different things. Satisfied is the obligor having done it,
/// waived is the obligee giving it up, breached is somebody's legal determination,
/// cancelled removes one recorded in error, and reinstated puts a waived or
/// breached obligation back. None of them is "the date passed": that is a fact
/// AgencyOS derives and shows without anybody recording anything (ADR-0022).
/// </para>
/// <para>
/// Breach requires a reason and the form enforces it before the button enables,
/// because a breach with no stated basis is a conclusion nobody can review later.
/// </para>
/// </remarks>
public sealed partial class ResolveObligationDialog : ContentDialog
{
    public ResolveObligationDialog(ObligationResponse obligation)
    {
        ArgumentNullException.ThrowIfNull(obligation);

        InitializeComponent();

        HeadlineText.Text = obligation.Description;

        // Says which of the three honest reasons applies when there is no date,
        // rather than showing a blank field.
        DueText.Text = obligation.DueOn is { } due
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{obligation.Kind}, owed by {obligation.ObligorDisplayName}, due {due:yyyy-MM-dd}.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{obligation.Kind}, owed by {obligation.ObligorDisplayName}. "
                    + $"{obligation.DueUnresolvedReason ?? "No due date recorded."}");

        UpdateForOutcome();
    }

    public ResolveObligationRequest ToRequest(int expectedVersion)
    {
        LegalFollowUpRequest? followUp =
            FollowUpBox.IsChecked == true && !string.IsNullOrWhiteSpace(FollowUpTitleBox.Text)
                ? new LegalFollowUpRequest(
                    FollowUpTitleBox.Text.Trim(), DateTimeOffset.UtcNow.AddDays(3))
                : null;

        return new ResolveObligationRequest(
            SelectedTag(OutcomeBox) ?? "Satisfied",
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

    private void OnReasonChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void UpdateForOutcome()
    {
        if (BreachBar is null)
        {
            return;
        }

        string outcome = SelectedTag(OutcomeBox) ?? "Satisfied";
        bool breach = outcome == "Breached";
        bool needsDate = outcome is "Satisfied" or "Waived";

        BreachBar.IsOpen = breach;
        OccurredPicker.Visibility = needsDate ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButtonText = breach ? "Record a breach" : "Record";

        UpdateReady();
    }

    private void UpdateReady()
    {
        if (OccurredPicker is null)
        {
            return;
        }

        string outcome = SelectedTag(OutcomeBox) ?? "Satisfied";

        bool dateReady =
            OccurredPicker.Visibility == Visibility.Collapsed || OccurredPicker.Date is not null;

        bool reasonReady = outcome != "Breached" || !string.IsNullOrWhiteSpace(ReasonBox.Text);

        IsPrimaryButtonEnabled = dateReady && reasonReady;
    }

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
