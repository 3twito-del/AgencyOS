using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Intelligence;

/// <summary>
/// What a prediction promises about its own history.
/// </summary>
/// <remarks>
/// The whole value of a forecasting record is that it cannot be improved after the
/// fact. Every test here is about something that would be invisible if it broke:
/// an overwritten probability reads exactly like one that was always there
/// (ADR-0030).
/// </remarks>
public sealed class PredictionTests
{
    private static readonly OrganizationId Org = new(Guid.CreateVersion7());
    private static readonly UserId Analyst = new(Guid.CreateVersion7());
    private static readonly UserId Other = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A new prediction opens with its first forecast already recorded.</summary>
    [Fact]
    public void ANewPrediction_OpensWithItsFirstForecast()
    {
        Prediction prediction = Create(0.65m);

        PredictionRevision opening = Assert.Single(prediction.Revisions);

        Assert.Equal(1, opening.Sequence);
        Assert.Equal(0.65m, opening.Probability);
        Assert.Equal(0.65m, prediction.CurrentProbability);
        Assert.True(prediction.IsOpen);
        Assert.Null(prediction.Outcome);
        Assert.Null(prediction.BrierScore);
    }

    /// <summary>
    /// Revising appends. It never overwrites what was said before.
    /// </summary>
    /// <remarks>
    /// The test the model exists for. Overwriting 65% with 45% would let the record
    /// claim the analyst always thought 45%.
    /// </remarks>
    [Fact]
    public void Revising_PreservesEveryEarlierForecast()
    {
        Prediction prediction = Create(0.65m);

        prediction.Revise(0.80m, Analyst, Now.AddDays(55), prediction.Version, "Trades confirmed it.");
        prediction.Revise(0.45m, Other, Now.AddDays(134), prediction.Version, "The showrunner left.");

        Assert.Equal(3, prediction.Revisions.Count);

        Assert.Equal([0.65m, 0.80m, 0.45m], prediction.Revisions.Select(x => x.Probability));
        Assert.Equal([1, 2, 3], prediction.Revisions.Select(x => x.Sequence));

        // The current view is the latest, and the history is all of it.
        Assert.Equal(0.45m, prediction.CurrentProbability);

        // The first forecast still says what it said, by whom and when.
        Assert.Equal(Now, prediction.Revisions[0].RecordedAt);
        Assert.Equal(Analyst, prediction.Revisions[0].RecordedBy);
        Assert.Equal(Other, prediction.Revisions[2].RecordedBy);
    }

    /// <summary>The current probability is derived, not stored.</summary>
    [Fact]
    public void TheCurrentProbability_IsTheLatestRevision()
    {
        Prediction prediction = Create(0.10m);

        Assert.Equal(prediction.Revisions[^1].Probability, prediction.CurrentProbability);

        prediction.Revise(0.90m, Analyst, Now.AddDays(1), prediction.Version);

        Assert.Equal(prediction.Revisions[^1].Probability, prediction.CurrentProbability);
        Assert.Equal(prediction.LatestRevision!.Probability, prediction.CurrentProbability);
    }

    /// <summary>Anything that is not a probability is refused.</summary>
    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [InlineData(1.01)]
    [InlineData(2)]
    public void SomethingThatIsNotAProbability_IsRefused(decimal probability) =>
        Assert.Throws<DomainException>(() => Create(probability));

    /// <summary>Precision nobody stated is refused.</summary>
    /// <remarks>
    /// A forecaster picked a number. Storing 0.65432199 would imply a confidence
    /// interval that never existed.
    /// </remarks>
    [Fact]
    public void PrecisionNobodyStated_IsRefused()
    {
        Assert.Throws<DomainException>(() => Create(0.123456m));

        // Four places is the limit, and is accepted.
        Prediction fine = Create(0.1234m);

        Assert.Equal(0.1234m, fine.CurrentProbability);
    }

    /// <summary>Zero and one are real forecasts, and are allowed.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CertaintyIsAllowed(decimal probability) =>
        Assert.Equal(probability, Create(probability).CurrentProbability);

    /// <summary>A deadline in the past is a question about the past.</summary>
    [Fact]
    public void ADeadlineThatHasPassed_IsRefused()
    {
        DomainException failure = Assert.Throws<DomainException>(() => Prediction.Create(
            Org,
            "Studio X orders a limited series",
            Now.AddDays(-1),
            0.5m,
            IntelligenceSensitivity.Internal,
            Analyst,
            Analyst,
            Now));

        Assert.Contains("future", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolution does not disturb the forecasting history.</summary>
    [Fact]
    public void Resolving_PreservesEveryRecordedProbability()
    {
        Prediction prediction = Create(0.65m);

        prediction.Revise(0.80m, Analyst, Now.AddDays(10), prediction.Version);

        decimal[] before = [.. prediction.Revisions.Select(x => x.Probability)];

        prediction.Resolve(PredictionOutcome.Yes, Analyst, Now.AddDays(200), prediction.Version);

        Assert.Equal(before, prediction.Revisions.Select(x => x.Probability));
        Assert.Equal(2, prediction.Revisions.Count);
        Assert.True(prediction.IsResolved);
    }

    /// <summary>The score uses the forecast the analyst actually ended on.</summary>
    [Fact]
    public void TheScoreUsesTheLastForecastBeforeResolution()
    {
        Prediction prediction = Create(0.50m);

        prediction.Revise(0.90m, Analyst, Now.AddDays(10), prediction.Version);
        prediction.Resolve(PredictionOutcome.Yes, Analyst, Now.AddDays(200), prediction.Version);

        // (0.9 - 1)² = 0.01
        Assert.Equal(0.01m, prediction.BrierScore);
    }

    /// <summary>An unresolvable prediction is never scored.</summary>
    /// <remarks>
    /// Not zero, not 0.25, and not counted as wrong. A question nobody could answer
    /// is a badly written prediction rather than a failed forecast.
    /// </remarks>
    [Fact]
    public void AnUnresolvablePrediction_IsNeverScored()
    {
        Prediction prediction = Create(0.65m);

        prediction.Resolve(
            PredictionOutcome.Unresolvable, Analyst, Now.AddDays(200), prediction.Version);

        Assert.True(prediction.IsResolved);
        Assert.Equal(PredictionOutcome.Unresolvable, prediction.Outcome);
        Assert.Null(prediction.BrierScore);

        // And it is excluded from any aggregate.
        Assert.Empty(ForecastCalibrationBridge(prediction));
    }

    /// <summary>A forecast after the answer is known is not a forecast.</summary>
    [Fact]
    public void RevisingAfterResolution_IsRefused()
    {
        Prediction prediction = Create(0.65m);

        prediction.Resolve(PredictionOutcome.No, Analyst, Now.AddDays(200), prediction.Version);

        DomainException failure = Assert.Throws<DomainException>(
            () => prediction.Revise(0.01m, Analyst, Now.AddDays(201), prediction.Version));

        Assert.Contains("resolved", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A prediction resolves once.</summary>
    [Fact]
    public void ResolvingTwice_IsRefused()
    {
        Prediction prediction = Create(0.65m);

        prediction.Resolve(PredictionOutcome.Yes, Analyst, Now.AddDays(200), prediction.Version);

        Assert.Throws<DomainException>(() => prediction.Resolve(
            PredictionOutcome.No, Analyst, Now.AddDays(201), prediction.Version));
    }

    /// <summary>A cancelled prediction has no outcome and is not scored.</summary>
    [Fact]
    public void Cancelling_IsNotAnOutcome()
    {
        Prediction prediction = Create(0.65m);

        prediction.Cancel("The project was abandoned.", Now.AddDays(10), prediction.Version);

        Assert.True(prediction.IsCancelled);
        Assert.Null(prediction.Outcome);
        Assert.Null(prediction.BrierScore);
        Assert.False(prediction.IsOpen);

        Assert.Throws<DomainException>(() => prediction.Resolve(
            PredictionOutcome.Yes, Analyst, Now.AddDays(11), prediction.Version));
    }

    /// <summary>Status follows the clock, and is asked rather than stored.</summary>
    /// <remarks>
    /// A stored status would be wrong between the deadline passing and the next
    /// write, and a test that read the wall clock would be flaky for the same
    /// reason.
    /// </remarks>
    [Fact]
    public void StatusIsAskedAsOfAMoment()
    {
        Prediction prediction = Create(0.65m);

        Assert.Equal(PredictionStatus.Open, prediction.StatusAt(Now.AddDays(1)));
        Assert.Equal(PredictionStatus.AwaitingResolution, prediction.StatusAt(Now.AddDays(400)));

        prediction.Resolve(PredictionOutcome.Yes, Analyst, Now.AddDays(401), prediction.Version);

        Assert.Equal(PredictionStatus.Resolved, prediction.StatusAt(Now.AddDays(402)));
    }

    /// <summary>A stale write is refused rather than silently applied.</summary>
    [Fact]
    public void AStaleVersion_IsRefused()
    {
        Prediction prediction = Create(0.65m);

        int stale = prediction.Version;

        prediction.Revise(0.70m, Analyst, Now.AddDays(1), stale);

        Assert.Throws<ConcurrencyConflictException>(
            () => prediction.Revise(0.75m, Other, Now.AddDays(2), stale));
    }

    /// <summary>Evidence names exactly one thing.</summary>
    [Fact]
    public void EvidenceNamesOneThing()
    {
        Prediction prediction = Create(0.65m);

        Assert.Throws<DomainException>(() => prediction.AddEvidence(
            null, null, Analyst, Now, "neither"));

        Assert.Throws<DomainException>(() => prediction.AddEvidence(
            IntelligenceSourceId.New(), SignalId.New(), Analyst, Now, "both"));

        PredictionEvidence evidence = prediction.AddEvidence(
            IntelligenceSourceId.New(), null, Analyst, Now);

        Assert.NotNull(evidence.SourceId);
        Assert.Null(evidence.SignalId);
    }

    private static Prediction Create(decimal probability) => Prediction.Create(
        Org,
        "Studio X orders at least one creator-led limited series before 2027-06-30",
        Now.AddYears(1),
        probability,
        IntelligenceSensitivity.Internal,
        Analyst,
        Analyst,
        Now,
        "Counts an order announced publicly or confirmed by the studio.");

    /// <summary>What the calibration aggregate would see for this prediction.</summary>
    private static IReadOnlyList<Application.Intelligence.ResolvedForecast>
        ForecastCalibrationBridge(Prediction prediction) =>
        Application.Intelligence.ForecastCalibration.Scoreable([prediction]);
}
