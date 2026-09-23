using System.Globalization;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Intelligence;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// What a prediction row says: the forecast, whose it is, how it came out and by
/// when it resolves.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The row showed the forecaster and not the forecast (F-14).</strong> The
/// Predictions list bound <c>CurrentProbabilityByDisplayName</c> as a bare caption
/// beneath the statement and never bound <c>CurrentProbability</c>,
/// <c>Outcome</c> or <c>BrierScore</c>, all three of which the server publishes.
/// Reality Closure read a resolved prediction as
/// <c>statement / Review Owner / Resolved / 30/12/2026 22:00:00 +00:00</c>: a
/// forecast without its probability, a resolution without its outcome, and a
/// calibration record invisible on the surface that exists to hold it.
/// </para>
/// <para>
/// <strong>And the name it did show was unattributed.</strong> A prediction
/// carries two people - the member who owns the question and the member who last
/// set the probability - and the bare name sat where an operator reads an owner.
/// The spoken row meanwhile announced <c>OwnerDisplayName</c> through
/// <see cref="PartyLine"/>, so on a prediction whose owner and forecaster differ
/// the two channels named different people with only one of them saying which.
/// Here the forecaster is named only as the author of the figure they set.
/// </para>
/// <para>
/// <strong>The date was rendered raw (F-15).</strong> <c>ResolvesBy</c> bound with
/// no converter wrote a UTC instant in the host's day/month order. It is written
/// as the local calendar date, as <see cref="TaskLine.When"/> writes a due date,
/// because the operator chose it from a local date picker and that is the day
/// they meant.
/// </para>
/// <para>
/// Both channels read these words from here, so the caption an operator sees and
/// the phrase a screen reader announces cannot disagree about what the forecast
/// is or whose it is.
/// </para>
/// </remarks>
public static class ForecastLine
{
    /// <summary>Whether a row is a prediction this can describe.</summary>
    public static bool IsPrediction(object? row) => row is PredictionResponse;

    /// <summary>
    /// The current forecast and who set it, or null where the row is not a prediction.
    /// </summary>
    /// <remarks>
    /// The figure is never shown without its author: a bare percentage reads as
    /// the system's estimate, and AgencyOS never has one (§11). Where the author's
    /// name did not resolve the row says so, in the words a task row already uses
    /// for the same situation, rather than dropping the attribution.
    /// </remarks>
    public static string? Forecast(object? row) =>
        row is PredictionResponse prediction ? "Forecast " + Stated(prediction) : null;

    /// <summary>
    /// How a resolved prediction came out and how it scored, or null while it is
    /// unresolved.
    /// </summary>
    /// <remarks>
    /// An open, overdue or cancelled prediction has no outcome and says nothing
    /// here rather than "not yet resolved": its status already says where it
    /// stands, and a second phrase repeating that would be noise. A question that
    /// could not be resolved is a real answer and is never scored, and says both.
    /// </remarks>
    public static string? Result(object? row)
    {
        if (row is not PredictionResponse { Outcome: { Length: > 0 } outcome } prediction)
        {
            return null;
        }

        string came = IntelligenceFormatting.Outcome(outcome);

        return prediction.BrierScore is { } score
            ? string.Create(CultureInfo.CurrentCulture, $"{came}, Brier score {score:0.000}")
            : came + ", not scored";
    }

    /// <summary>The date the question resolves by, or null where the row is not a prediction.</summary>
    public static string? ResolvesBy(object? row) =>
        row is PredictionResponse prediction
            ? "Resolves by " + LocalDate(prediction.ResolvesBy)
            : null;

    /// <summary>
    /// The caption beneath a prediction's statement: the forecast, then the result
    /// where there is one.
    /// </summary>
    public static string Caption(object? row) =>
        (Forecast(row), Result(row)) switch
        {
            (null, _) => string.Empty,
            ({ } forecast, null) => forecast,
            ({ } forecast, { } result) => forecast + " · " + result,
        };

    /// <summary>
    /// What a spoken prediction row must keep, in the order it is read.
    /// </summary>
    /// <remarks>
    /// Everything the visible row carries besides its statement and status. These
    /// are what the row is scanned for, so they survive when a long statement has
    /// to be shortened to fit.
    /// </remarks>
    public static IReadOnlyList<string> Essentials(object? row)
    {
        List<string> essential = [];

        foreach (string? part in (string?[])[Forecast(row), Result(row), ResolvesBy(row)])
        {
            if (part is not null)
            {
                essential.Add(part);
            }
        }

        return essential;
    }

    /// <summary>A probability and whose it is, without a label in front.</summary>
    /// <remarks>
    /// Shared with <see cref="IntelligenceFormatting.Forecast"/>, which the
    /// forecast and resolve dialogs read, so a dialog and the row it was opened
    /// from attribute the same figure in the same words.
    /// </remarks>
    internal static string Stated(PredictionResponse prediction)
    {
        string probability = IntelligenceFormatting.Probability(prediction.CurrentProbability);

        return prediction.CurrentProbabilityByDisplayName is { } forecaster
            && !string.IsNullOrWhiteSpace(forecaster)
                ? probability + " by " + forecaster.Trim()
                : probability + ", forecaster name unavailable";
    }

    /// <summary>An instant as the local calendar date, the one way this product writes dates.</summary>
    internal static string LocalDate(DateTimeOffset instant) =>
        instant.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
