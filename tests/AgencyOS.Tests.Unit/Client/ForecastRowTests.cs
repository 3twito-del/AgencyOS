using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Contracts.Projects;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// A prediction row states the forecast, says whose it is, and writes its date
/// one way; a project role row states the title that tells it apart.
/// </summary>
/// <remarks>
/// <para>
/// F-14. The Predictions list showed the statement, a bare name, the status and a
/// raw instant. The name was the member who last set the probability; the
/// probability itself, the outcome and the Brier score were published and never
/// rendered. The spoken row meanwhile named the <em>owner</em>, so on a prediction
/// whose owner and forecaster differ the two channels named different people.
/// </para>
/// <para>
/// These execute the shipped formatter against the shipped response type. Every
/// fixture gives the owner and the forecaster different names, so no test here can
/// pass by finding both names somewhere in the row: swapping them fails.
/// </para>
/// </remarks>
public sealed class ForecastRowTests
{
    private const string OwnerName = "Perrin Halloway";
    private const string ForecasterName = "Ines Dorel";

    // ------------------------------------------------------------ the forecast

    /// <summary>The caption states the probability and who set it.</summary>
    [Fact]
    public void TheCaptionStatesTheForecastAndItsAuthor()
    {
        Assert.Equal(
            "Forecast 80% by " + ForecasterName,
            ForecastLine.Caption(Prediction()));
    }

    /// <summary>The spoken row carries the same forecast, in the same words.</summary>
    /// <remarks>
    /// It announced the statement, the owner, the status and the raw outcome, and
    /// never the probability.
    /// </remarks>
    [Fact]
    public void TheSpokenRowCarriesTheForecastTheCaptionShows()
    {
        PredictionResponse prediction = Prediction();

        Assert.Contains(
            ForecastLine.Caption(prediction),
            RowLabel.For(prediction),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The forecaster is named as the author of the figure, and the owner as the owner.
    /// </summary>
    [Fact]
    public void TheForecasterAndTheOwnerAreEachNamedInTheirOwnRole()
    {
        PredictionResponse prediction = Prediction();

        string seen = ForecastLine.Caption(prediction);
        string spoken = RowLabel.For(prediction);

        Assert.Contains("by " + ForecasterName, seen, StringComparison.Ordinal);
        Assert.DoesNotContain(OwnerName, seen, StringComparison.Ordinal);

        Assert.Contains("Owner: " + OwnerName, spoken, StringComparison.Ordinal);
        Assert.Contains("by " + ForecasterName, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("Owner: " + ForecasterName, spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("by " + OwnerName, spoken, StringComparison.Ordinal);
    }

    /// <summary>Swapping the owner and the forecaster changes what each channel says.</summary>
    [Fact]
    public void SwappingTheOwnerAndTheForecasterChangesTheRow()
    {
        PredictionResponse right = Prediction();
        PredictionResponse swapped = Prediction(owner: ForecasterName, forecaster: OwnerName);

        Assert.NotEqual(ForecastLine.Caption(right), ForecastLine.Caption(swapped));
        Assert.NotEqual(RowLabel.For(right), RowLabel.For(swapped));

        Assert.Contains("by " + OwnerName, ForecastLine.Caption(swapped), StringComparison.Ordinal);
        Assert.Contains("Owner: " + ForecasterName, RowLabel.For(swapped), StringComparison.Ordinal);
    }

    /// <summary>
    /// A forecast whose author did not resolve says so, and is never a bare figure.
    /// </summary>
    /// <remarks>
    /// A percentage with nothing beside it reads as the system's estimate, and
    /// AgencyOS never has one (§11). The dialogs used to return exactly that.
    /// </remarks>
    [Fact]
    public void AForecastWithNoResolvedAuthorSaysSo()
    {
        PredictionResponse prediction = Prediction(forecaster: null);

        Assert.Equal(
            "Forecast 80%, forecaster name unavailable",
            ForecastLine.Caption(prediction));
        Assert.StartsWith(
            "80%, forecaster name unavailable",
            IntelligenceFormatting.Forecast(prediction),
            StringComparison.Ordinal);
    }

    /// <summary>The dialogs attribute the figure in the row's words, with an unambiguous date.</summary>
    [Fact]
    public void TheDialogsAttributeTheForecastInTheRowsWords()
    {
        DateTimeOffset asOf = LocalMidnight(2026, 9, 10);

        string rendered = IntelligenceFormatting.Forecast(Prediction(asOf: asOf));

        Assert.Equal("80% by " + ForecasterName + ", 2026-09-10", rendered);
    }

    // ------------------------------------------------------------- the result

    /// <summary>A resolved prediction shows how it came out and how it scored.</summary>
    [Fact]
    public void AResolvedPredictionStatesItsOutcomeAndScore()
    {
        PredictionResponse prediction = Prediction(
            status: "Resolved", outcome: "Yes", brier: 0.04m);

        Assert.Equal(
            "Forecast 80% by " + ForecasterName + " · Happened, Brier score 0.040",
            ForecastLine.Caption(prediction));

        string spoken = RowLabel.For(prediction);

        Assert.Contains("Happened, Brier score 0.040", spoken, StringComparison.Ordinal);

        // The raw token is not read out as though it were a status.
        Assert.DoesNotContain(", Yes", spoken, StringComparison.Ordinal);
    }

    /// <summary>A question that could not be resolved says so, and says it is not scored.</summary>
    [Fact]
    public void AnUnresolvableQuestionIsNotScored()
    {
        PredictionResponse prediction = Prediction(
            status: "Resolved", outcome: "Unresolvable", brier: null);

        Assert.Equal(
            "Could not be resolved, not scored",
            ForecastLine.Result(prediction));

        // The row read live, whose status was the part the budget cut.
        string spoken = RowLabel.For(Prediction(
            statement: "The Harbour pilot is picked up to series",
            status: "Resolved",
            owner: "Review member",
            forecaster: "Review Owner",
            outcome: "Unresolvable"));

        Assert.Contains(", Resolved, ", spoken, StringComparison.Ordinal);
        Assert.Contains("Could not be resolved, not scored", spoken, StringComparison.Ordinal);
    }

    /// <summary>An open prediction claims no outcome at all.</summary>
    /// <remarks>
    /// Its status already says where it stands. "Not scored" beside an open
    /// question would read as a judgement about it.
    /// </remarks>
    [Theory]
    [InlineData("Open")]
    [InlineData("AwaitingResolution")]
    [InlineData("Cancelled")]
    public void AnUnresolvedPredictionStatesNoResult(string status)
    {
        PredictionResponse prediction = Prediction(status: status);

        Assert.Null(ForecastLine.Result(prediction));
        Assert.DoesNotContain("scored", RowLabel.For(prediction), StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- the date

    /// <summary>
    /// The resolution date is the local day the operator picked, written one way.
    /// </summary>
    /// <remarks>
    /// F-15. The server returns the instant in UTC. Bound raw it rendered as
    /// <c>30/12/2026 22:00:00 +00:00</c> for a question meant to resolve on the
    /// 31st.
    /// </remarks>
    [Fact]
    public void TheResolutionDateIsTheLocalDayInOneFormat()
    {
        DateTimeOffset utc = LocalMidnight(2026, 12, 31).ToUniversalTime();

        PredictionResponse prediction = Prediction(resolvesBy: utc);

        Assert.Equal("Resolves by 2026-12-31", ForecastLine.ResolvesBy(prediction));
        Assert.Contains("Resolves by 2026-12-31", RowLabel.For(prediction), StringComparison.Ordinal);
    }

    /// <summary>The prediction picker writes the same date the row does.</summary>
    [Fact]
    public void ThePredictionPickerWritesTheSameDate()
    {
        DateTimeOffset utc = LocalMidnight(2026, 12, 31).ToUniversalTime();

        EntityChoice choice = Assert.Single(
            EntityChoice.ForPredictions([Prediction(resolvesBy: utc)]));

        Assert.Contains("resolves 2026-12-31", choice.Label, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- the budget

    /// <summary>
    /// A statement long enough to fill the budget yields before the forecast does.
    /// </summary>
    /// <remarks>
    /// And before the status and the owner. The first live reading of build 94's
    /// candidate announced <c>…, Owner: Review member,…, Forecast 30%</c>: the
    /// statement survived whole and the status was the part cut.
    /// </remarks>
    [Fact]
    public void ALongStatementDoesNotCostTheForecastOrTheDate()
    {
        PredictionResponse prediction = Prediction(
            statement: string.Join(' ', Enumerable.Repeat("Vesper Kestrel casts the lead", 12)),
            status: "Resolved",
            outcome: "No",
            brier: 0.64m);

        string spoken = RowLabel.For(prediction);

        Assert.True(spoken.Length <= 160, $"{spoken.Length} characters: {spoken}");
        Assert.Contains("Owner: " + OwnerName, spoken, StringComparison.Ordinal);
        Assert.Contains(", Resolved, ", spoken, StringComparison.Ordinal);
        Assert.Contains("Forecast 80% by " + ForecasterName, spoken, StringComparison.Ordinal);
        Assert.Contains("Did not happen, Brier score 0.640", spoken, StringComparison.Ordinal);
        Assert.Contains("Resolves by ", spoken, StringComparison.Ordinal);
    }

    // ------------------------------------------------------ project role rows

    /// <summary>
    /// Two roles of one type are told apart by their title, in the spoken row.
    /// </summary>
    /// <remarks>
    /// F-16. Three <c>Actor</c> roles rendered as three identical rows; the title
    /// that distinguishes them was stored and published and never shown. The page
    /// now binds it; this pins that the announcement keeps it too.
    /// </remarks>
    [Fact]
    public void RolesOfOneTypeAreToldApartByTheirTitle()
    {
        string marin = RowLabel.For(Role("Lead - Marin"));
        string harbour = RowLabel.For(Role("Harbourmaster"));

        Assert.StartsWith("Lead - Marin", marin, StringComparison.Ordinal);
        Assert.StartsWith("Harbourmaster", harbour, StringComparison.Ordinal);
        Assert.Contains("Actor", marin, StringComparison.Ordinal);
    }

    /// <summary>An untitled role still says what kind of role it is.</summary>
    [Fact]
    public void AnUntitledRoleStillSaysItsType()
    {
        string spoken = RowLabel.For(Role(null));

        Assert.Contains("Actor", spoken, StringComparison.Ordinal);
        Assert.Contains("Filled", spoken, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- fixtures

    private static DateTimeOffset LocalMidnight(int year, int month, int day)
    {
        DateTime local = new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static PredictionResponse Prediction(
        string statement = "Vesper Kestrel casts a lead for the spring shoot",
        string status = "Open",
        string? owner = OwnerName,
        string? forecaster = ForecasterName,
        string? outcome = null,
        decimal? brier = null,
        DateTimeOffset? resolvesBy = null,
        DateTimeOffset? asOf = null) =>
        new(
            Guid.CreateVersion7(),
            statement,
            resolvesBy ?? DateTimeOffset.UtcNow.AddDays(30),
            status,
            0.8m,
            asOf,
            forecaster,
            outcome,
            outcome is null ? null : DateTimeOffset.UtcNow,
            brier,
            "Internal",
            owner,
            DateTimeOffset.UtcNow,
            [],
            1,
            1);

    private static ProjectRoleResponse Role(string? label) =>
        new(Guid.CreateVersion7(), "Actor", label, "Filled", false, null, []);
}
