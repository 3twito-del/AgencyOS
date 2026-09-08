using AgencyOS.Domain.Common;
using AgencyOS.Domain.Intelligence;

namespace AgencyOS.Application.Intelligence;

/// <summary>
/// One resolved forecast, reduced to the two numbers calibration needs.
/// </summary>
/// <param name="Probability">What the forecaster last said, from 0 to 1.</param>
/// <param name="Happened">What actually happened.</param>
public readonly record struct ResolvedForecast(decimal Probability, bool Happened);

/// <summary>
/// Deterministic scoring of forecasts against what happened.
/// </summary>
/// <remarks>
/// <para>
/// Pure arithmetic over <c>decimal</c>, in C#. No model, no library, no F# kernel:
/// the whole calculation is a subtraction and a multiplication, and a new project
/// for it would be ceremony rather than correctness (ADR-0030).
/// </para>
/// <para>
/// It produces numbers and never adjectives. There is no "well calibrated" and no
/// "strong forecaster" anywhere, because a mean Brier score over eleven
/// predictions supports neither — which is why the sample count travels beside
/// every aggregate.
/// </para>
/// <para>
/// Unresolvable predictions are excluded entirely rather than scored as half
/// right. A question nobody could answer is a badly written prediction, not a
/// forecast the analyst got wrong.
/// </para>
/// </remarks>
public static class ForecastCalibration
{
    /// <summary>Decimal places kept in an aggregate.</summary>
    /// <remarks>
    /// Six. Enough that averaging a few hundred four-place probabilities does not
    /// lose anything, and few enough that the number does not imply a precision the
    /// inputs never had.
    /// </remarks>
    public const int Scale = 6;

    /// <summary>
    /// The Brier score for one forecast: <c>(probability - outcome)²</c>.
    /// </summary>
    /// <remarks>
    /// Zero is a perfect forecast. One is confident and wrong. A forecast of 0.5 on
    /// anything scores 0.25 whatever happens, which is the property that makes the
    /// score worth using: it rewards being right <em>and</em> being willing to say
    /// so.
    /// </remarks>
    public static decimal BrierScore(decimal probability, bool happened)
    {
        RequireProbability(probability);

        decimal actual = happened ? 1m : 0m;
        decimal error = probability - actual;

        return error * error;
    }

    /// <summary>
    /// The mean Brier score over a set of resolved forecasts.
    /// </summary>
    /// <returns>Null for an empty set. A mean of nothing is not zero.</returns>
    public static decimal? MeanBrierScore(IReadOnlyCollection<ResolvedForecast> forecasts)
    {
        ArgumentNullException.ThrowIfNull(forecasts);

        if (forecasts.Count == 0)
        {
            return null;
        }

        decimal total = 0m;

        foreach (ResolvedForecast forecast in forecasts)
        {
            total += BrierScore(forecast.Probability, forecast.Happened);
        }

        return decimal.Round(total / forecasts.Count, Scale, MidpointRounding.ToEven);
    }

    /// <summary>The mean of what was forecast, for comparison against what happened.</summary>
    public static decimal? MeanProbability(IReadOnlyCollection<ResolvedForecast> forecasts)
    {
        ArgumentNullException.ThrowIfNull(forecasts);

        if (forecasts.Count == 0)
        {
            return null;
        }

        decimal total = 0m;

        foreach (ResolvedForecast forecast in forecasts)
        {
            RequireProbability(forecast.Probability);
            total += forecast.Probability;
        }

        return decimal.Round(total / forecasts.Count, Scale, MidpointRounding.ToEven);
    }

    /// <summary>
    /// How often the predicted thing actually happened.
    /// </summary>
    /// <remarks>
    /// Shown beside <see cref="MeanProbability"/> because the pair is the whole
    /// point: a forecaster who says 70% on things that happen 70% of the time is
    /// calibrated, and neither number says that alone.
    /// </remarks>
    public static decimal? ObservedFrequency(IReadOnlyCollection<ResolvedForecast> forecasts)
    {
        ArgumentNullException.ThrowIfNull(forecasts);

        if (forecasts.Count == 0)
        {
            return null;
        }

        int happened = forecasts.Count(x => x.Happened);

        return decimal.Round((decimal)happened / forecasts.Count, Scale, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Reduces predictions to the resolved binary ones worth scoring.
    /// </summary>
    /// <remarks>
    /// Drops everything unresolved, cancelled, unresolvable or without a stated
    /// forecast. Each exclusion is a case where scoring would invent a result: an
    /// open prediction has no outcome, a cancelled one was withdrawn, and an
    /// unresolvable one is a question that turned out not to be answerable.
    /// </remarks>
    public static IReadOnlyList<ResolvedForecast> Scoreable(
        IEnumerable<Prediction> predictions)
    {
        ArgumentNullException.ThrowIfNull(predictions);

        List<ResolvedForecast> scoreable = [];

        foreach (Prediction prediction in predictions)
        {
            if (prediction.Outcome is not (PredictionOutcome.Yes or PredictionOutcome.No))
            {
                continue;
            }

            if (prediction.LatestRevision is null)
            {
                continue;
            }

            scoreable.Add(new ResolvedForecast(
                prediction.CurrentProbability,
                prediction.Outcome == PredictionOutcome.Yes));
        }

        return scoreable;
    }

    private static void RequireProbability(decimal probability)
    {
        if (probability < 0m || probability > 1m)
        {
            throw new DomainException(
                $"A probability is between 0 and 1. {probability} is not one.");
        }
    }
}
