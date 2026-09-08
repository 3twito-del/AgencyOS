using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Intelligence;

/// <summary>
/// Reads intelligence, narrowed to what the caller may see.
/// </summary>
/// <remarks>
/// The database side of the projection. Every method takes the readable
/// classifications and applies them <em>in the SQL</em>, so a caller never learns
/// the size of what was withheld (ADR-0030).
/// </remarks>
public interface IIntelligenceQueries
{
    Task<IReadOnlyList<IntelligenceSourceModel>> ListSourcesAsync(
        OrganizationId organizationId,
        IntelligenceScope<SourceFilter> scope,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IntelligenceSourceModel?> GetSourceAsync(
        OrganizationId organizationId,
        IntelligenceSourceId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SignalSummaryModel>> ListSignalsAsync(
        OrganizationId organizationId,
        IntelligenceScope<SignalFilter> scope,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SignalDetailModel?> GetSignalAsync(
        OrganizationId organizationId,
        SignalId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThesisSummaryModel>> ListThesesAsync(
        OrganizationId organizationId,
        IntelligenceScope<ThesisFilter> scope,
        int limit,
        CancellationToken cancellationToken = default);

    Task<ThesisDetailModel?> GetThesisAsync(
        OrganizationId organizationId,
        ThesisId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PredictionSummaryModel>> ListPredictionsAsync(
        OrganizationId organizationId,
        IntelligenceScope<PredictionFilter> scope,
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default);

    Task<PredictionDetailModel?> GetPredictionAsync(
        OrganizationId organizationId,
        PredictionId id,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The resolved forecasts a calibration aggregate may see.
    /// </summary>
    /// <remarks>
    /// Narrowed the same way everything else is. A calibration figure computed over
    /// predictions the caller cannot read would disclose them through the sample
    /// count and the mean (ADR-0030).
    /// </remarks>
    Task<IReadOnlyList<(decimal Probability, PredictionOutcome Outcome)>>
        ListResolvedForecastsAsync(
            OrganizationId organizationId,
            IReadOnlySet<IntelligenceSensitivity> readable,
            Guid? ownerUserId,
            DateOnly? resolvedAfter,
            DateOnly? resolvedBefore,
            CancellationToken cancellationToken = default);

    Task<(int Open, int Unresolvable)> CountPredictionStatesAsync(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        Guid? ownerUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WatchlistSummaryModel>> ListWatchlistsAsync(
        OrganizationId organizationId,
        IntelligenceScope<WatchlistFilter> scope,
        int limit,
        CancellationToken cancellationToken = default);

    Task<WatchlistDetailModel?> GetWatchlistAsync(
        OrganizationId organizationId,
        WatchlistId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TalentRadarSummaryModel>> ListRadarAsync(
        OrganizationId organizationId,
        IntelligenceScope<TalentRadarFilter> scope,
        int limit,
        CancellationToken cancellationToken = default);

    Task<TalentRadarDetailModel?> GetRadarEntryAsync(
        OrganizationId organizationId,
        TalentRadarEntryId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResearchCaseSummaryModel>> ListResearchCasesAsync(
        OrganizationId organizationId,
        IntelligenceScope<ResearchCaseFilter> scope,
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default);

    Task<ResearchCaseDetailModel?> GetResearchCaseAsync(
        OrganizationId organizationId,
        ResearchCaseId id,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What is factually known about a working relationship.
    /// </summary>
    /// <remarks>
    /// Counted from M2 rows against a supplied clock, so the windows are
    /// reproducible and a test does not depend on wall-clock time (ADR-0030).
    /// </remarks>
    Task<RelationshipIntelligenceModel?> GetRelationshipIntelligenceAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    Task<WatchlistActivityModel?> GetWatchlistActivityAsync(
        OrganizationId organizationId,
        WatchlistId id,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    Task<IntelligenceCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The authorized read surface for intelligence.
/// </summary>
/// <remarks>
/// <para>
/// Every method resolves what the caller may read and hands that to the query
/// layer, which applies it in SQL. Nothing filters a materialized list, because
/// filtering afterwards leaks the count — and a count of source-sensitive signals
/// about a named person is itself the disclosure (ADR-0030).
/// </para>
/// <para>
/// Saved views run through this service too, so a view can never widen what its
/// runner may see.
/// </para>
/// </remarks>
public sealed class IntelligenceQueryService
{
    /// <summary>The largest page any intelligence list returns.</summary>
    private const int MaximumPageSize = 200;

    private const int DefaultPageSize = 50;

    private readonly IIntelligenceQueries _queries;
    private readonly IntelligenceAuthorization _authorization;
    private readonly IClock _clock;

    public IntelligenceQueryService(
        IIntelligenceQueries queries,
        IntelligenceAuthorization authorization,
        IClock clock)
    {
        _queries = queries;
        _authorization = authorization;
        _clock = clock;
    }

    public async Task<IReadOnlyList<IntelligenceSourceModel>> ListSourcesAsync(
        OrganizationId organizationId,
        SourceFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await _queries
            .ListSourcesAsync(
                organizationId,
                await ScopeAsync(organizationId, filter ?? new SourceFilter(), cancellationToken)
                    .ConfigureAwait(false),
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>One source, refused if the caller may not read its classification.</summary>
    public async Task<IntelligenceSourceModel?> GetSourceAsync(
        OrganizationId organizationId,
        IntelligenceSourceId id,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        IntelligenceSourceModel? source = await _queries
            .GetSourceAsync(organizationId, id, cancellationToken).ConfigureAwait(false);

        if (source is null)
        {
            return null;
        }

        // Refuses rather than redacts, as documents do. Somebody who followed a
        // citation to a source they may not read learns a grant exists to ask for.
        await _authorization
            .AuthorizeReadAsync(organizationId, source.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        return source;
    }

    public async Task<IReadOnlyList<SignalSummaryModel>> ListSignalsAsync(
        OrganizationId organizationId,
        SignalFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await _queries
            .ListSignalsAsync(
                organizationId,
                await ScopeAsync(organizationId, filter ?? new SignalFilter(), cancellationToken)
                    .ConfigureAwait(false),
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<SignalDetailModel?> GetSignalAsync(
        OrganizationId organizationId,
        SignalId id,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        SignalDetailModel? signal = await _queries
            .GetSignalAsync(organizationId, id, cancellationToken).ConfigureAwait(false);

        if (signal is null)
        {
            return null;
        }

        await _authorization
            .AuthorizeReadAsync(organizationId, signal.Signal.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        return signal;
    }

    public async Task<IReadOnlyList<ThesisSummaryModel>> ListThesesAsync(
        OrganizationId organizationId,
        ThesisFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await _queries
            .ListThesesAsync(
                organizationId,
                await ScopeAsync(organizationId, filter ?? new ThesisFilter(), cancellationToken)
                    .ConfigureAwait(false),
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<ThesisDetailModel?> GetThesisAsync(
        OrganizationId organizationId,
        ThesisId id,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        ThesisDetailModel? thesis = await _queries
            .GetThesisAsync(organizationId, id, cancellationToken).ConfigureAwait(false);

        if (thesis is null)
        {
            return null;
        }

        await _authorization
            .AuthorizeReadAsync(organizationId, thesis.Thesis.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        return thesis;
    }

    public async Task<IReadOnlyList<PredictionSummaryModel>> ListPredictionsAsync(
        OrganizationId organizationId,
        PredictionFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await _queries
            .ListPredictionsAsync(
                organizationId,
                await ScopeAsync(organizationId, filter ?? new PredictionFilter(), cancellationToken)
                    .ConfigureAwait(false),
                _clock.UtcNow,
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<PredictionDetailModel?> GetPredictionAsync(
        OrganizationId organizationId,
        PredictionId id,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        PredictionDetailModel? prediction = await _queries
            .GetPredictionAsync(organizationId, id, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (prediction is null)
        {
            return null;
        }

        await _authorization
            .AuthorizeReadAsync(organizationId, prediction.Prediction.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        return prediction;
    }

    /// <summary>
    /// Calibration over the resolved predictions the caller may read.
    /// </summary>
    /// <remarks>
    /// Numbers and a sample count, never a verdict. The count is there so a reader
    /// can see for themselves that a mean over eleven predictions supports very
    /// little (ADR-0030).
    /// </remarks>
    public async Task<PredictionCalibrationModel> GetCalibrationAsync(
        OrganizationId organizationId,
        Guid? ownerUserId = null,
        DateOnly? resolvedAfter = null,
        DateOnly? resolvedBefore = null,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<IntelligenceSensitivity> readable = await _authorization
            .ReadableSensitivitiesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<(decimal Probability, PredictionOutcome Outcome)> resolved = await _queries
            .ListResolvedForecastsAsync(
                organizationId, readable, ownerUserId, resolvedAfter, resolvedBefore,
                cancellationToken)
            .ConfigureAwait(false);

        // Unresolvable predictions are counted and never scored. Treating one as
        // half right would manufacture a number from an absence.
        ResolvedForecast[] scoreable =
        [
            .. resolved
                .Where(x => x.Outcome is PredictionOutcome.Yes or PredictionOutcome.No)
                .Select(x => new ResolvedForecast(
                    x.Probability, x.Outcome == PredictionOutcome.Yes)),
        ];

        (int open, int unresolvable) = await _queries
            .CountPredictionStatesAsync(organizationId, readable, ownerUserId, cancellationToken)
            .ConfigureAwait(false);

        return new PredictionCalibrationModel(
            scoreable.Length,
            scoreable.Count(x => x.Happened),
            scoreable.Count(x => !x.Happened),
            unresolvable,
            open,
            ForecastCalibration.MeanBrierScore(scoreable),
            ForecastCalibration.MeanProbability(scoreable),
            ForecastCalibration.ObservedFrequency(scoreable));
    }

    public async Task<IReadOnlyList<WatchlistSummaryModel>> ListWatchlistsAsync(
        OrganizationId organizationId,
        WatchlistFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await _queries
            .ListWatchlistsAsync(
                organizationId,
                await ScopeAsync(organizationId, filter ?? new WatchlistFilter(), cancellationToken)
                    .ConfigureAwait(false),
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<WatchlistDetailModel?> GetWatchlistAsync(
        OrganizationId organizationId,
        WatchlistId id,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        WatchlistDetailModel? watchlist = await _queries
            .GetWatchlistAsync(organizationId, id, cancellationToken).ConfigureAwait(false);

        if (watchlist is null)
        {
            return null;
        }

        await _authorization
            .AuthorizeReadAsync(organizationId, watchlist.Watchlist.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        return watchlist;
    }

    /// <summary>
    /// What has happened around the things a watchlist watches.
    /// </summary>
    /// <remarks>
    /// Derived by subject overlap, and narrowed to what the caller may read. A
    /// watchlist the caller can open does not entitle them to signals about its
    /// members that they could not otherwise see (ADR-0030).
    /// </remarks>
    public async Task<WatchlistActivityModel?> GetWatchlistActivityAsync(
        OrganizationId organizationId,
        WatchlistId id,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<IntelligenceSensitivity> readable = await _authorization
            .ReadableSensitivitiesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        WatchlistActivityModel? activity = await _queries
            .GetWatchlistActivityAsync(
                organizationId, id, readable, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (activity is null)
        {
            return null;
        }

        await _authorization
            .AuthorizeReadAsync(organizationId, activity.Watchlist.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        return activity;
    }

    public async Task<IReadOnlyList<TalentRadarSummaryModel>> ListRadarAsync(
        OrganizationId organizationId,
        TalentRadarFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await _queries
            .ListRadarAsync(
                organizationId,
                await ScopeAsync(organizationId, filter ?? new TalentRadarFilter(), cancellationToken)
                    .ConfigureAwait(false),
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<TalentRadarDetailModel?> GetRadarEntryAsync(
        OrganizationId organizationId,
        TalentRadarEntryId id,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        TalentRadarDetailModel? entry = await _queries
            .GetRadarEntryAsync(organizationId, id, cancellationToken).ConfigureAwait(false);

        if (entry is null)
        {
            return null;
        }

        await _authorization
            .AuthorizeReadAsync(organizationId, entry.Entry.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        return entry;
    }

    public async Task<IReadOnlyList<ResearchCaseSummaryModel>> ListResearchCasesAsync(
        OrganizationId organizationId,
        ResearchCaseFilter? filter = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await _queries
            .ListResearchCasesAsync(
                organizationId,
                await ScopeAsync(
                        organizationId, filter ?? new ResearchCaseFilter(), cancellationToken)
                    .ConfigureAwait(false),
                _clock.UtcNow,
                Clamp(limit),
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<ResearchCaseDetailModel?> GetResearchCaseAsync(
        OrganizationId organizationId,
        ResearchCaseId id,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        ResearchCaseDetailModel? researchCase = await _queries
            .GetResearchCaseAsync(organizationId, id, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (researchCase is null)
        {
            return null;
        }

        await _authorization
            .AuthorizeReadAsync(
                organizationId, researchCase.ResearchCase.Sensitivity, cancellationToken)
            .ConfigureAwait(false);

        return researchCase;
    }

    /// <summary>
    /// What is factually known about a working relationship.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dimensions, never a score. There is no composite number anywhere in the
    /// result, because one would be arithmetic over incommensurable things and its
    /// apparent precision would be believed (ADR-0030).
    /// </para>
    /// <para>
    /// Needs the relationship's own read grant as well as the intelligence one:
    /// this projects M2 contact history, and holding an intelligence grant is not
    /// a reason to see somebody's contact record.
    /// </para>
    /// </remarks>
    public async Task<RelationshipIntelligenceModel?> GetRelationshipIntelligenceAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<IntelligenceSensitivity> readable = await _authorization
            .ReadableSensitivitiesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .GetRelationshipIntelligenceAsync(
                organizationId, kind, subjectId, readable, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>What the intelligence desk has to look at.</summary>
    public async Task<IntelligenceCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<IntelligenceSensitivity> readable = await _authorization
            .ReadableSensitivitiesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return await _queries
            .GetCommandCenterAsync(organizationId, readable, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IntelligenceScope<TFilter>> ScopeAsync<TFilter>(
        OrganizationId organizationId,
        TFilter filter,
        CancellationToken cancellationToken)
    {
        await _authorization.AuthorizeReadAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<IntelligenceSensitivity> readable = await _authorization
            .ReadableSensitivitiesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return new IntelligenceScope<TFilter>(readable, filter);
    }

    private static int Clamp(int? limit) =>
        limit is null ? DefaultPageSize : Math.Clamp(limit.Value, 1, MaximumPageSize);
}
