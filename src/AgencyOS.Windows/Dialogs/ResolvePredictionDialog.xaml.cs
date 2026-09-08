using System;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Intelligence;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Closes a question with what actually happened.
/// </summary>
/// <remarks>
/// <para>
/// The forecast is shown before the outcome is chosen, on purpose. Resolving a
/// prediction without seeing what was predicted is how a forecaster convinces
/// themselves they were roughly right.
/// </para>
/// <para>
/// Unresolvable is a first-class answer. It is counted and never scored: treating it
/// as half right would manufacture a number from an absence, and treating it as
/// wrong would push people towards questions that are easy to grade rather than
/// questions worth asking (§14).
/// </para>
/// </remarks>
public sealed partial class ResolvePredictionDialog : ContentDialog
{
    public ResolvePredictionDialog(PredictionResponse prediction, string? resolutionCriteria)
    {
        ArgumentNullException.ThrowIfNull(prediction);

        InitializeComponent();

        StatementText.Text = prediction.Statement;
        ForecastText.Text = "Last forecast: " + IntelligenceFormatting.Forecast(prediction);

        CriteriaText.Text = string.IsNullOrWhiteSpace(resolutionCriteria)
            ? "No resolution criteria were recorded when this was stated."
            : "Agreed criteria: " + resolutionCriteria;

        ApplyScoring();
    }

    /// <summary>Yes, No or Unresolvable.</summary>
    public string Outcome =>
        (OutcomeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Yes";

    /// <summary>What settled it, or null.</summary>
    public string? Note =>
        string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

    private void OnOutcomeChanged(object sender, SelectionChangedEventArgs e) => ApplyScoring();

    private void ApplyScoring()
    {
        bool scored = Outcome is "Yes" or "No";

        ScoringBar.Severity = scored ? InfoBarSeverity.Informational : InfoBarSeverity.Warning;

        ScoringBar.Message = scored
            ? "A Brier score will be computed from the last probability stated before "
                + "this moment. Zero is a perfect forecast and one is confidently wrong."
            : "This will be counted and never scored. A question whose answer never "
                + "became knowable has nothing to measure a forecast against.";
    }
}
