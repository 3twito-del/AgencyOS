using System;
using System.Globalization;
using AgencyOS.Contracts.Representation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Moves a representation to another status.
/// </summary>
/// <remarks>
/// <para>
/// Active is what makes somebody a client, so this is the control that ends a
/// client relationship, pauses one by agreement, or resumes it. Converting a
/// prospect already produces an Active representation; everything after that
/// happens here, and until Reality Closure wave 5 it happened nowhere.
/// </para>
/// <para>
/// <strong>The dialog holds no copy of the transition table.</strong> The domain
/// publishes it and the server enforces it — Terminated and Expired are terminal,
/// Suspended can resume, and a change cannot be dated before the representation
/// began. This offers the statuses that exist, and lets the refusal say which of
/// them this representation cannot reach. Keeping a second edition of that table
/// here is how the two would drift.
/// </para>
/// </remarks>
public sealed partial class TransitionRepresentationDialog : ContentDialog
{
    /// <param name="displayName">Whose representation it is.</param>
    /// <param name="representation">The relationship as the server last described it.</param>
    public TransitionRepresentationDialog(
        string displayName,
        RepresentationResponse representation)
    {
        ArgumentNullException.ThrowIfNull(representation);

        InitializeComponent();

        HeadlineText.Text = displayName;

        string ends = representation.EndsOn is { } end
            ? string.Create(CultureInfo.InvariantCulture, $", ending {end:yyyy-MM-dd}")
            : string.Empty;

        CurrentText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Currently {representation.Status.ToLowerInvariant()}, since "
                + $"{representation.StartsOn:yyyy-MM-dd}{ends}.");

        ConsequenceBar.Message =
            "Nothing is deleted. The representation keeps its start date, and its scopes and "
                + "team as they stood; ending it closes them on the day you give and leaves the "
                + "history readable. Somebody who is represented again later gets a new "
                + "representation, because that is what happened.";

        foreach (string status in (string[])["Active", "Suspended", "Terminated", "Expired"])
        {
            if (string.Equals(status, representation.Status, StringComparison.Ordinal))
            {
                continue;
            }

            StatusBox.Items.Add(new ComboBoxItem { Content = Describe(status), Tag = status });
        }

        // Today, as the dialog that begins a representation already does. A status
        // change is normally recorded on the day it happens, and the operator can
        // date it otherwise; leaving it blank made the common case two interactions
        // and the rare one no easier.
        OccurredPicker.Date = DateTimeOffset.UtcNow;

        UpdateReady();
    }

    public TransitionRepresentationRequest ToRequest(int expectedVersion) =>
        new(
            SelectedTag(StatusBox) ?? string.Empty,
            OccurredPicker.Date is { } occurred
                ? DateOnly.FromDateTime(occurred.DateTime)
                : default,
            expectedVersion,
            Empty(ReasonBox.Text));

    private void OnStatusChanged(object sender, SelectionChangedEventArgs e) => UpdateReady();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void OnDateChanged(
        CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args) => UpdateReady();

    private void UpdateReady() =>
        IsPrimaryButtonEnabled =
            SelectedTag(StatusBox) is { Length: > 0 } && OccurredPicker.Date is not null;

    /// <summary>The status in the words the agency uses for it.</summary>
    private static string Describe(string status) => status switch
    {
        "Active" => "Active — the agency represents them",
        "Suspended" => "Suspended — not acting for now, by agreement",
        "Terminated" => "Terminated — the relationship ended",
        "Expired" => "Expired — it ran to its end date",
        _ => status,
    };

    private static string? SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
