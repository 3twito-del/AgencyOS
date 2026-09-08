using System;
using System.Globalization;
using AgencyOS.Contracts.Intelligence;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// States something falsifiable, with a date and a probability.
/// </summary>
/// <remarks>
/// <para>
/// The slider is in whole percent and the request carries a decimal from 0 to 1.
/// Whole percent because a forecaster who types 0.6237 is asserting a precision they
/// do not have; a decimal on the wire because the number is money-adjacent
/// arithmetic and a float would make 0.1 stop being 0.1 by the time a Brier score is
/// computed from it (§11).
/// </para>
/// <para>
/// The resolution criteria field is not decoration. A prediction whose answer can be
/// argued about is one that resolves as Unresolvable, which is honest but teaches
/// nobody anything.
/// </para>
/// </remarks>
public sealed partial class CreatePredictionDialog : ContentDialog
{
    public CreatePredictionDialog()
    {
        InitializeComponent();

        ResolvesPicker.Date = DateTimeOffset.Now.AddMonths(3);
        ApplyProbability();
    }

    /// <summary>What the operator filled in, as the API expects it.</summary>
    public CreatePredictionRequest ToRequest() =>
        new(
            (StatementBox.Text ?? string.Empty).Trim(),
            ResolvesPicker.Date ?? DateTimeOffset.Now.AddMonths(3),
            Probability,
            (SensitivityBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Internal",
            OwnerUserId: null,
            Optional(CriteriaBox),
            Optional(RationaleBox));

    /// <summary>The probability as a decimal from 0 to 1, at whole-percent precision.</summary>
    public decimal Probability =>
        Math.Round((decimal)ProbabilitySlider.Value / 100m, 4);

    private void OnRequiredChanged(object sender, TextChangedEventArgs e) => UpdatePrimary();

    private void OnDateChanged(
        CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args) => UpdatePrimary();

    private void OnProbabilityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyProbability();
        UpdatePrimary();
    }

    private void ApplyProbability()
    {
        int percent = (int)ProbabilitySlider.Value;

        // Said in words as well as in a number, because 50% is the one value that
        // means something different: it is a forecast that carries no information,
        // and a forecaster who lands there should notice.
        string reading = percent switch
        {
            0 => "You are certain this will not happen.",
            100 => "You are certain this will happen.",
            50 => "Even odds. This forecast says nothing either way.",
            < 50 => "You think it is more likely not to happen.",
            _ => "You think it is more likely to happen.",
        };

        ProbabilityText.Text = string.Create(
            CultureInfo.CurrentCulture, $"{percent}% — {reading}");
    }

    private void UpdatePrimary() =>
        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(StatementBox.Text) && ResolvesPicker.Date is not null;

    private static string? Optional(TextBox box) =>
        string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
}
