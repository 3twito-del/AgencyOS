using System;
using System.Globalization;
using AgencyOS.Contracts.Legal;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records when a contract takes effect.
/// </summary>
/// <remarks>
/// <para>
/// The date AgencyOS refuses to infer. Execution is derived from signatures and
/// effectiveness is not derived from anything: a contract can be signed in March
/// and in force from January, and the page has always shown the two separately
/// because the difference is the whole question when somebody asks what was in
/// force on a day (ADR-0022).
/// </para>
/// <para>
/// So this dialog carries the distinction rather than assuming the operator holds
/// it. It shows what the contract already says about execution, states plainly
/// that recording this changes no status, and asks for one date.
/// </para>
/// </remarks>
public sealed partial class RecordEffectiveDateDialog : ContentDialog
{
    /// <param name="contract">The instrument, for its title and its execution facts.</param>
    public RecordEffectiveDateDialog(ContractSummaryResponse contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        InitializeComponent();

        HeadlineText.Text = contract.Title;

        string executed = contract.ExecutedOn is { } signed
            ? string.Create(CultureInfo.InvariantCulture, $"Executed {signed:yyyy-MM-dd}")
            : "Not fully executed";

        string effective = contract.EffectiveOn is { } from
            ? string.Create(
                CultureInfo.InvariantCulture,
                $". Currently effective from {from:yyyy-MM-dd}; recording again replaces that.")
            : ". No effective date recorded yet.";

        ExecutionText.Text = executed + effective;

        // Deliberately no default. Today is a guess, and the executed date is the
        // one inference this screen exists to refuse.
        if (contract.EffectiveOn is { } current)
        {
            EffectivePicker.Date =
                new DateTimeOffset(current.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        UpdateReady();
    }

    /// <summary>The date chosen, as the request expects it.</summary>
    public DateOnly EffectiveOn =>
        EffectivePicker.Date is { } chosen
            ? DateOnly.FromDateTime(chosen.DateTime)
            : default;

    public RecordEffectiveDateRequest ToRequest(int expectedVersion) =>
        new(EffectiveOn, expectedVersion);

    private void OnDateChanged(
        CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args) => UpdateReady();

    private void UpdateReady() => IsPrimaryButtonEnabled = EffectivePicker.Date is not null;
}
