using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Intelligence;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// What the intelligence screens say, and what they refuse to say.
/// </summary>
/// <remarks>
/// The wording is the feature. A screen that renders <c>Corroborated</c> as
/// "Verified" would assert a claim is true; one that renders a probability without
/// the forecaster would present an opinion as a finding. These tests are about the
/// difference (§1, §11, §18).
/// </remarks>
public sealed class IntelligenceFormattingTests
{
    /// <summary>
    /// No verification state reads as an assertion that the claim is true.
    /// </summary>
    /// <remarks>
    /// "Unverified" is allowed to contain the word, because it is the negation.
    /// What is refused is any wording that asserts the claim holds: verified,
    /// confirmed, proven, true, or a statement of fact (§1).
    /// </remarks>
    [Theory]
    [InlineData("Unverified")]
    [InlineData("Corroborated")]
    [InlineData("Disputed")]
    [InlineData("Retracted")]
    public void NoVerificationState_AssertsTheClaimIsTrue(string state)
    {
        string wording = IntelligenceFormatting.Verification(state);

        Assert.DoesNotContain("confirmed", wording, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proven", wording, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("true", wording, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fact", wording, StringComparison.OrdinalIgnoreCase);

        // "Verified" as a standalone claim, as opposed to "Unverified".
        Assert.DoesNotContain(
            "verified",
            wording.Replace("Unverified", string.Empty, StringComparison.OrdinalIgnoreCase),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Corroboration is described as evidence agreeing, not as fact.</summary>
    [Fact]
    public void Corroborated_SaysWhatItMeans()
    {
        Assert.Equal(
            "Corroborated by other evidence",
            IntelligenceFormatting.Verification("Corroborated"));
    }

    /// <summary>An unassessed source is not called weak, and not called strong.</summary>
    [Fact]
    public void AnUnassessedSource_IsSaidToBeUnassessed()
    {
        Assert.Equal("Not assessed", IntelligenceFormatting.Reliability("Unassessed"));
        Assert.Equal("Not assessed", IntelligenceFormatting.Reliability(null));
    }

    /// <summary>A URL is described as a reference, never as something held.</summary>
    [Fact]
    public void AReference_IsNotDescribedAsHeld()
    {
        Assert.Equal("Held by AgencyOS", IntelligenceFormatting.Custody(true));
        Assert.Contains("not archived", IntelligenceFormatting.Custody(false), StringComparison.Ordinal);
    }

    /// <summary>
    /// A forecast is rendered with whose it is.
    /// </summary>
    /// <remarks>
    /// A bare percentage reads as the system's estimate, and AgencyOS never has one.
    /// </remarks>
    [Fact]
    public void AForecast_CarriesItsForecaster()
    {
        PredictionResponse prediction = Prediction(
            probability: 0.65m, forecaster: "Ines Dorel", brier: null);

        string rendered = IntelligenceFormatting.Forecast(prediction);

        Assert.Contains("65%", rendered, StringComparison.Ordinal);
        Assert.Contains("Ines Dorel", rendered, StringComparison.Ordinal);
    }

    /// <summary>An unresolved or unresolvable prediction is not scored.</summary>
    [Fact]
    public void AnUnscoredPrediction_SaysSo()
    {
        Assert.Equal("Not scored", IntelligenceFormatting.BrierScore(null));
    }

    /// <summary>A Brier score is rendered with what it measures, never as a grade.</summary>
    [Fact]
    public void ABrierScore_ExplainsItsScale()
    {
        string rendered = IntelligenceFormatting.BrierScore(0.36m);

        Assert.Contains("0.360", rendered, StringComparison.Ordinal);
        Assert.Contains("0 is perfect", rendered, StringComparison.Ordinal);

        // No verdict about the forecaster anywhere in it.
        Assert.DoesNotContain("good", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("poor", rendered, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A calibration figure never appears without its sample count.</summary>
    [Fact]
    public void ACalibrationFigure_CarriesItsSampleCount()
    {
        string rendered = IntelligenceFormatting.Calibration(0.21m, 11);

        Assert.Contains("11", rendered, StringComparison.Ordinal);
        Assert.Equal("Nothing resolved yet", IntelligenceFormatting.Calibration(null, 0));
    }

    /// <summary>
    /// An unrecorded relationship strength is an absence, not a zero.
    /// </summary>
    /// <remarks>
    /// The one place a composite score would be easiest to fake. Fourteen emails is
    /// not a strong relationship, and this says "Not recorded" rather than filling
    /// the gap from a count (§18).
    /// </remarks>
    [Fact]
    public void AnUnrecordedStrength_IsNotInventedFromActivity()
    {
        Assert.Equal("Not recorded", IntelligenceFormatting.RecordedStrength(null));
        Assert.Equal("Not recorded", IntelligenceFormatting.RecordedStrength(""));
        Assert.Equal("4 of 5", IntelligenceFormatting.RecordedStrength("4 of 5"));
    }

    /// <summary>Unresolvable is described as a real answer, not as a failure.</summary>
    [Fact]
    public void Unresolvable_IsNotDescribedAsAFailure()
    {
        string rendered = IntelligenceFormatting.Outcome("Unresolvable");

        Assert.Equal("Could not be resolved", rendered);
        Assert.DoesNotContain("wrong", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fail", rendered, StringComparison.OrdinalIgnoreCase);
    }

    internal static PredictionResponse Prediction(
        decimal probability,
        string? forecaster,
        decimal? brier,
        string status = "Open") =>
        new(
            Guid.CreateVersion7(),
            "Something falsifiable",
            DateTimeOffset.UtcNow.AddDays(30),
            status,
            probability,
            DateTimeOffset.UtcNow,
            forecaster,
            null,
            null,
            brier,
            "Internal",
            forecaster,
            DateTimeOffset.UtcNow,
            [],
            1,
            1);
}

/// <summary>The intelligence view models, against a fake server.</summary>
public sealed class IntelligenceViewModelTests
{
    /// <summary>The list reports loading, then empty, and never confuses the two.</summary>
    [Fact]
    public async Task ASignalList_DistinguishesEmptyFromUnloaded()
    {
        FakeAgencyOsApi api = new();
        SignalListViewModel model = new(api);

        Assert.False(model.IsEmpty);

        await model.LoadAsync();

        Assert.True(model.IsEmpty);
        Assert.Null(model.ErrorMessage);
    }

    /// <summary>The verification filter is sent to the server, not applied locally.</summary>
    /// <remarks>
    /// Filtering a materialized list would produce correct rows and an incorrect
    /// count, and the count is the thing the classification rules protect (§28).
    /// </remarks>
    [Fact]
    public async Task ASignalFilter_IsSentToTheServer()
    {
        FakeAgencyOsApi api = new();
        SignalListViewModel model = new(api) { Verification = "Disputed" };

        await model.LoadAsync();

        Assert.Equal("Disputed", Assert.Single(api.SignalFilters));
    }

    /// <summary>A withheld excerpt is announced rather than left blank.</summary>
    /// <remarks>
    /// A silently missing quotation reads as an analyst who never took one, which is
    /// a different and misleading fact (ADR-0025).
    /// </remarks>
    [Fact]
    public async Task AWithheldExcerpt_IsVisibleAsWithheld()
    {
        FakeAgencyOsApi api = new()
        {
            SignalDetail = Detail(withheld: true),
        };

        SignalDetailViewModel model = new(api);

        await model.LoadAsync(Guid.CreateVersion7());

        Assert.True(model.HasWithheldExcerpts);
    }

    [Fact]
    public async Task AReadableExcerpt_IsNotAnnouncedAsWithheld()
    {
        FakeAgencyOsApi api = new()
        {
            SignalDetail = Detail(withheld: false),
        };

        SignalDetailViewModel model = new(api);

        await model.LoadAsync(Guid.CreateVersion7());

        Assert.False(model.HasWithheldExcerpts);
    }

    /// <summary>Changing verification sends the version the reader was looking at.</summary>
    [Fact]
    public async Task ChangingVerification_CarriesTheVersion()
    {
        FakeAgencyOsApi api = new()
        {
            SignalDetail = Detail(withheld: false),
        };

        SignalDetailViewModel model = new(api);

        await model.LoadAsync(Guid.CreateVersion7());
        await model.ChangeVerificationAsync("Disputed", "Another trade contradicts it");

        Assert.NotNull(api.LastVerification);
        Assert.Equal("Disputed", api.LastVerification!.Verification);
        Assert.Equal(7, api.LastVerification.ExpectedVersion);
    }

    /// <summary>The calibration panel loads with the list, so a figure is never stale.</summary>
    [Fact]
    public async Task ThePredictionList_LoadsItsCalibrationWithIt()
    {
        FakeAgencyOsApi api = new()
        {
            Calibration = new PredictionCalibrationResponse(3, 2, 1, 1, 4, 0.2m, 0.6m, 0.667m),
        };

        PredictionListViewModel model = new(api);

        await model.LoadAsync();

        Assert.NotNull(model.Calibration);
        Assert.Equal(3, model.Calibration!.ResolvedCount);
        Assert.Equal(1, model.Calibration.UnresolvableCount);
    }

    /// <summary>A radar conversion is sent with an idempotency key.</summary>
    /// <remarks>
    /// It creates records in another milestone. A retry must not produce a second
    /// prospect for the same person (ADR-0013).
    /// </remarks>
    [Fact]
    public async Task ConvertingARadarEntry_IsIdempotent()
    {
        FakeAgencyOsApi api = new()
        {
            Conversion = new RadarConversionResponse(
                Guid.CreateVersion7(), Guid.CreateVersion7(), true),
        };

        TalentRadarViewModel model = new(api);

        RadarConversionResponse? conversion = await model.ConvertAsync(Entry());

        Assert.NotNull(conversion);
        Assert.NotNull(api.LastConversion);
        Assert.Contains(api.IdempotencyKeys, x => !string.IsNullOrWhiteSpace(x));
    }

    /// <summary>A dismissal carries the reason and the version.</summary>
    [Fact]
    public async Task DismissingARadarEntry_CarriesItsReason()
    {
        FakeAgencyOsApi api = new();
        TalentRadarViewModel model = new(api);

        await model.DismissAsync(Entry(), "Signed elsewhere");

        Assert.NotNull(api.LastDismissal);
        Assert.Equal("Signed elsewhere", api.LastDismissal!.Reason);
        Assert.Equal(4, api.LastDismissal.ExpectedVersion);
    }

    /// <summary>The desk is quiet when nothing needs attention, and says so.</summary>
    [Fact]
    public async Task TheDesk_SaysWhenNothingIsWaiting()
    {
        FakeAgencyOsApi api = new();
        IntelligenceCommandCenterViewModel model = new(api);

        await model.LoadAsync();

        Assert.False(model.NeedsAttention);
        Assert.True(model.IsEmpty);
    }

    [Fact]
    public async Task TheDesk_ReportsWhatIsWaiting()
    {
        FakeAgencyOsApi api = new()
        {
            IntelligenceCommandCenter = new IntelligenceCommandCenterResponse(
                [], [], [], [], [], [], 3, 0, 0, 0),
        };

        IntelligenceCommandCenterViewModel model = new(api);

        await model.LoadAsync();

        Assert.True(model.NeedsAttention);
        Assert.False(model.IsEmpty);
    }

    private static SignalDetailResponse Detail(bool withheld) =>
        new(
            new SignalResponse(
                Guid.CreateVersion7(),
                "A claim",
                "Something somebody said happened.",
                "Observation",
                "Unverified",
                "Unstated",
                "Internal",
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "Ines Dorel",
                [],
                1,
                7),
            null,
            null,
            null,
            null,
            [
                new SignalEvidenceResponse(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    "The source",
                    "DocumentVersion",
                    "Medium",
                    "Primary",
                    withheld ? null : "The relevant passage.",
                    withheld,
                    null,
                    DateTimeOffset.UtcNow,
                    "Ines Dorel"),
            ],
            []);

    private static TalentRadarResponse Entry() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Tobias Wren",
            null,
            null,
            "Watching",
            "High",
            "Two shorts at festivals this year.",
            "Writer",
            "Internal",
            "Ines Dorel",
            DateTimeOffset.UtcNow,
            null,
            null,
            0,
            4);
}
