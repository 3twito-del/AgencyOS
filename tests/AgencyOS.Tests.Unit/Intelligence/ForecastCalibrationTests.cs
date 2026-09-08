using AgencyOS.Application.Intelligence;
using AgencyOS.Domain.Common;
using Xunit;

namespace AgencyOS.Tests.Unit.Intelligence;

/// <summary>
/// Scoring forecasts against what happened.
/// </summary>
/// <remarks>
/// The arithmetic is four lines, which is exactly why it is worth pinning: a
/// calibration record that quietly scores unresolvable questions, or that rounds a
/// probability into a float, produces numbers that look authoritative and are
/// wrong (ADR-0030).
/// </remarks>
public sealed class ForecastCalibrationTests
{
    /// <summary>A certain, correct forecast scores zero.</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void CertainAndCorrect_ScoresZero(int probability, bool happened) =>
        Assert.Equal(0m, ForecastCalibration.BrierScore(probability, happened));

    /// <summary>A certain, wrong forecast scores one.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void CertainAndWrong_ScoresOne(int probability, bool happened) =>
        Assert.Equal(1m, ForecastCalibration.BrierScore(probability, happened));

    /// <summary>
    /// A coin-flip forecast scores the same whatever happens.
    /// </summary>
    /// <remarks>
    /// The property that makes the score worth using: it rewards being right and
    /// being willing to say so, and refusing to commit earns 0.25 either way.
    /// </remarks>
    [Fact]
    public void HalfScoresTheSameEitherWay()
    {
        Assert.Equal(0.25m, ForecastCalibration.BrierScore(0.5m, happened: true));
        Assert.Equal(0.25m, ForecastCalibration.BrierScore(0.5m, happened: false));
    }

    /// <summary>The score is always between zero and one.</summary>
    [Theory]
    [InlineData(0.0001)]
    [InlineData(0.05)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.65)]
    [InlineData(0.9999)]
    public void TheScoreIsAlwaysBetweenZeroAndOne(decimal probability)
    {
        foreach (bool happened in new[] { true, false })
        {
            decimal score = ForecastCalibration.BrierScore(probability, happened);

            Assert.InRange(score, 0m, 1m);
        }
    }

    /// <summary>A confident forecast beats a hedged one when it is right.</summary>
    [Fact]
    public void ConfidenceIsRewardedWhenCorrectAndPunishedWhenNot()
    {
        Assert.True(
            ForecastCalibration.BrierScore(0.9m, happened: true)
                < ForecastCalibration.BrierScore(0.6m, happened: true));

        Assert.True(
            ForecastCalibration.BrierScore(0.9m, happened: false)
                > ForecastCalibration.BrierScore(0.6m, happened: false));
    }

    /// <summary>Anything that is not a probability is refused.</summary>
    [Theory]
    [InlineData(-0.0001)]
    [InlineData(-1)]
    [InlineData(1.0001)]
    [InlineData(2)]
    [InlineData(100)]
    public void SomethingThatIsNotAProbability_IsRefused(decimal probability) =>
        Assert.Throws<DomainException>(
            () => ForecastCalibration.BrierScore(probability, happened: true));

    /// <summary>A mean of nothing is nothing, not zero.</summary>
    /// <remarks>
    /// Zero is a perfect score. Reporting it for a forecaster who has resolved
    /// nothing would be the most flattering possible lie.
    /// </remarks>
    [Fact]
    public void AMeanOfNothing_IsNull()
    {
        Assert.Null(ForecastCalibration.MeanBrierScore([]));
        Assert.Null(ForecastCalibration.MeanProbability([]));
        Assert.Null(ForecastCalibration.ObservedFrequency([]));
    }

    /// <summary>The mean is the mean.</summary>
    [Fact]
    public void TheMeanIsTheMean()
    {
        ResolvedForecast[] forecasts =
        [
            new(1m, true),      // 0
            new(0m, true),      // 1
            new(0.5m, true),    // 0.25
        ];

        Assert.Equal(
            decimal.Round(1.25m / 3m, ForecastCalibration.Scale, MidpointRounding.ToEven),
            ForecastCalibration.MeanBrierScore(forecasts));
    }

    /// <summary>
    /// A calibrated forecaster's mean probability matches how often things happened.
    /// </summary>
    /// <remarks>
    /// The pair is the point. Neither number says anything about calibration alone,
    /// which is why both are reported and neither is turned into a verdict.
    /// </remarks>
    [Fact]
    public void MeanProbabilityAndObservedFrequencyAreReportedSeparately()
    {
        // Said 70% ten times; it happened seven.
        ResolvedForecast[] forecasts =
        [
            .. Enumerable.Repeat(new ResolvedForecast(0.7m, true), 7),
            .. Enumerable.Repeat(new ResolvedForecast(0.7m, false), 3),
        ];

        Assert.Equal(0.7m, ForecastCalibration.MeanProbability(forecasts));
        Assert.Equal(0.7m, ForecastCalibration.ObservedFrequency(forecasts));

        // And the Brier score still is not zero, because individual calls were
        // wrong. A calibrated forecaster is not an omniscient one.
        Assert.True(ForecastCalibration.MeanBrierScore(forecasts) > 0m);
    }

    /// <summary>Probabilities stay decimal, so equal forecasts stay equal.</summary>
    /// <remarks>
    /// Binary floating point would make 0.1 + 0.2 unequal to 0.3, and a
    /// calibration record built on that would disagree with itself (ADR-0023).
    /// </remarks>
    [Fact]
    public void ArithmeticIsExact()
    {
        ResolvedForecast[] forecasts =
        [
            new(0.1m, false),
            new(0.2m, false),
            new(0.3m, false),
        ];

        // 0.01 + 0.04 + 0.09 = 0.14, over three.
        decimal? mean = ForecastCalibration.MeanBrierScore(forecasts);

        Assert.Equal(
            decimal.Round(0.14m / 3m, ForecastCalibration.Scale, MidpointRounding.ToEven),
            mean);

        Assert.Equal(0.2m, ForecastCalibration.MeanProbability(forecasts));
        Assert.Equal(0m, ForecastCalibration.ObservedFrequency(forecasts));
    }

    /// <summary>The score type is decimal, never a float.</summary>
    [Fact]
    public void NoBinaryFloatingPointAnywhere()
    {
        Assert.Equal(
            typeof(decimal),
            typeof(ForecastCalibration)
                .GetMethod(nameof(ForecastCalibration.BrierScore))!
                .ReturnType);

        Assert.Equal(typeof(decimal), typeof(ResolvedForecast).GetProperty("Probability")!.PropertyType);
    }
}
