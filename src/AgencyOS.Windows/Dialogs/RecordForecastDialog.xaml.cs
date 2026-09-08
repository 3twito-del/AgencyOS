using System;
using System.Globalization;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Intelligence;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Adds a revision to a forecast.
/// </summary>
/// <remarks>
/// The slider opens where the current forecast is, so a revision is a deliberate
/// move rather than an accident of where the handle happened to sit. The previous
/// value is shown beside it with who stated it and when: a forecast is somebody's
/// assertion, and the attribution travels with it everywhere (§11).
/// </remarks>
public sealed partial class RecordForecastDialog : ContentDialog
{
    public RecordForecastDialog(PredictionResponse prediction)
    {
        ArgumentNullException.ThrowIfNull(prediction);

        InitializeComponent();

        StatementText.Text = prediction.Statement;
        CurrentText.Text = "Currently " + IntelligenceFormatting.Forecast(prediction);
        ProbabilitySlider.Value = (double)(prediction.CurrentProbability * 100m);

        ApplyProbability();
    }

    /// <summary>The probability as a decimal from 0 to 1, at whole-percent precision.</summary>
    public decimal Probability => Math.Round((decimal)ProbabilitySlider.Value / 100m, 4);

    /// <summary>Why it moved, or null.</summary>
    public string? Rationale =>
        string.IsNullOrWhiteSpace(RationaleBox.Text) ? null : RationaleBox.Text.Trim();

    private void OnProbabilityChanged(object sender, RangeBaseValueChangedEventArgs e) =>
        ApplyProbability();

    private void ApplyProbability() =>
        ProbabilityText.Text = string.Create(
            CultureInfo.CurrentCulture, $"New forecast: {(int)ProbabilitySlider.Value}%");
}
