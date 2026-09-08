using AgencyOS.Application.Intelligence;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Theses and predictions: the two places AgencyOS records judgment.
/// </summary>
/// <remarks>
/// Neither is ever projected as a fact. A thesis carries a status that has no
/// "true" in it and a prediction carries a probability somebody stated, with the
/// forecaster and the date beside it. The system contributes arithmetic and nothing
/// else (§1, ADR-0030).
/// </remarks>
public sealed partial class IntelligenceQueries
{
    // ------------------------------------------------------------------- theses

    /// <inheritdoc />
    public async Task<IReadOnlyList<ThesisSummaryModel>> ListThesesAsync(
        OrganizationId organizationId,
        IntelligenceScope<ThesisFilter> scope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IQueryable<Thesis> query = NarrowTheses(organizationId, scope.Readable);
        ThesisFilter filter = scope.Filter;

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Confidence is { } confidence)
        {
            query = query.Where(x => x.Confidence == confidence);
        }

        if (filter.Sensitivity is { } sensitivity)
        {
            query = query.Where(x => x.Sensitivity == sensitivity);
        }

        if (filter.SubjectKind is { } subjectKind)
        {
            query = filter.SubjectId is { } subjectId
                ? query.Where(x => x.Subjects.Any(
                    s => s.Kind == subjectKind && s.SubjectId == subjectId))
                : query.Where(x => x.Subjects.Any(s => s.Kind == subjectKind));
        }

        if (filter.OwnerUserId is { } ownerUserId)
        {
            UserId owner = new(ownerUserId);
            query = query.Where(x => x.OwnerUserId == owner);
        }

        if (filter.UpdatedAfter is { } updatedAfter)
        {
            DateTimeOffset from = From(updatedAfter);
            query = query.Where(x => x.UpdatedAt >= from);
        }

        if (filter.TextContains is { Length: > 0 } text)
        {
            string pattern = Pattern(text);

            query = query.Where(x =>
                EF.Functions.ILike(x.Title, pattern)
                || EF.Functions.ILike(x.Proposition, pattern));
        }

        List<Thesis> rows = await query
            .Include(x => x.Subjects)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToThesisSummariesAsync(
                organizationId, rows, scope.Readable, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ThesisDetailModel?> GetThesisAsync(
        OrganizationId organizationId,
        ThesisId id,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken = default)
    {
        Thesis? thesis = await _context.Theses
            .AsNoTracking()
            .Include(x => x.Subjects)
            .Include(x => x.Revisions)
            .Include(x => x.Evidence)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (thesis is null)
        {
            return null;
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        ThesisSummaryModel summary = (await ToThesisSummariesAsync(
                organizationId, [thesis], readable, cancellationToken).ConfigureAwait(false))[0];

        IReadOnlyList<ThesisEvidenceModel> evidence = await ToThesisEvidenceAsync(
                organizationId, thesis, readable, users, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<IntelligenceEventModel> history = await HistoryAsync(
                organizationId, IntelligenceOwnerKind.Thesis, id.Value, users, cancellationToken)
            .ConfigureAwait(false);

        return new ThesisDetailModel(
            summary,
            thesis.Rationale,
            thesis.ClosedReason,
            thesis.ClosedAt,
            thesis.SupersededByThesisId?.Value,
            [
                .. thesis.Revisions
                    .OrderBy(x => x.Sequence)
                    .Select(x => new ThesisRevisionModel(
                        x.Id,
                        x.Sequence,
                        x.Proposition,
                        x.Rationale,
                        x.Confidence,
                        x.ChangeNote,
                        x.RecordedAt,
                        Name(users, x.RecordedBy))),
            ],
            evidence,
            history);
    }

    /// <summary>
    /// Projects the signals a thesis rests on, dropping the ones the caller may not
    /// read.
    /// </summary>
    /// <remarks>
    /// A thesis a member may open can cite a source-sensitive signal. Naming that
    /// signal in the citation would disclose it, so the citation is omitted — and
    /// the supporting and challenging counts are computed over the same narrowed
    /// set, so the numbers agree with the rows (§28, ADR-0030).
    /// </remarks>
    private async Task<IReadOnlyList<ThesisEvidenceModel>> ToThesisEvidenceAsync(
        OrganizationId organizationId,
        Thesis thesis,
        IReadOnlySet<IntelligenceSensitivity> readable,
        Dictionary<Guid, string> users,
        CancellationToken cancellationToken)
    {
        if (thesis.Evidence.Count == 0)
        {
            return [];
        }

        SignalId[] cited = [.. thesis.Evidence.Select(x => x.SignalId).Distinct()];

        Dictionary<SignalId, CitedSignal> byId = await NarrowSignals(organizationId, readable)
            .Where(x => cited.Contains(x.Id))
            .Select(x => new CitedSignal(x.Id, x.Title, x.Verification))
            .ToDictionaryAsync(x => x.Id, x => x, cancellationToken)
            .ConfigureAwait(false);

        List<ThesisEvidenceModel> projected = [];

        foreach (ThesisEvidence evidence in thesis.Evidence.OrderBy(x => x.AddedAt))
        {
            if (!byId.TryGetValue(evidence.SignalId, out CitedSignal? signal))
            {
                continue;
            }

            projected.Add(new ThesisEvidenceModel(
                evidence.Id,
                evidence.SignalId,
                signal.Title,
                signal.Verification,
                evidence.Stance,
                evidence.Note,
                evidence.AddedAt,
                Name(users, evidence.AddedBy)));
        }

        return projected;
    }

    private async Task<IReadOnlyList<ThesisSummaryModel>> ToThesisSummariesAsync(
        OrganizationId organizationId,
        IReadOnlyList<Thesis> theses,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken)
    {
        if (theses.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<(IntelligenceSubjectKind, Guid), string> labels = await LabelsAsync(
                organizationId, theses.SelectMany(x => x.Subjects), cancellationToken)
            .ConfigureAwait(false);

        ThesisId[] ids = [.. theses.Select(x => x.Id)];
        IntelligenceSensitivity[] allowed = [.. readable];

        Dictionary<ThesisId, int> revisions = await _context.ThesisRevisions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ThesisId))
            .GroupBy(x => x.ThesisId)
            .Select(g => new Counted<ThesisId>(g.Key, g.Count()))
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        // Counted over readable signals only. Stances are never summed against each
        // other: they are reported side by side, because five weak supporting
        // signals do not mechanically outweigh one strong contradiction.
        List<StanceCount> stances = await _context.ThesisEvidence
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && ids.Contains(x.ThesisId)
                && _context.Signals.Any(
                    s => s.Id == x.SignalId && allowed.Contains(s.Sensitivity)))
            .GroupBy(x => new { x.ThesisId, x.Stance })
            .Select(g => new StanceCount(g.Key.ThesisId, g.Key.Stance, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<ThesisId, int> supporting = stances
            .Where(x => x.Stance == ThesisEvidenceStance.Supports)
            .ToDictionary(x => x.ThesisId, x => x.Count);

        Dictionary<ThesisId, int> challenging = stances
            .Where(x => x.Stance == ThesisEvidenceStance.Challenges)
            .ToDictionary(x => x.ThesisId, x => x.Count);

        return
        [
            .. theses.Select(x => new ThesisSummaryModel(
                x.Id,
                x.Title,
                x.Proposition,
                x.Status,
                x.Confidence,
                x.Sensitivity,
                Name(users, x.OwnerUserId),
                x.CreatedAt,
                x.UpdatedAt,
                Subjects(x.Subjects, labels),
                revisions.GetValueOrDefault(x.Id),
                supporting.GetValueOrDefault(x.Id),
                challenging.GetValueOrDefault(x.Id),
                x.Version)),
        ];
    }

    // -------------------------------------------------------------- predictions

    /// <inheritdoc />
    public async Task<IReadOnlyList<PredictionSummaryModel>> ListPredictionsAsync(
        OrganizationId organizationId,
        IntelligenceScope<PredictionFilter> scope,
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IQueryable<Prediction> query = NarrowPredictions(organizationId, scope.Readable);
        PredictionFilter filter = scope.Filter;

        if (filter.Status is { } status)
        {
            query = WithStatus(query, status, asOf);
        }

        if (filter.Outcome is { } outcome)
        {
            query = query.Where(x => x.Outcome == outcome);
        }

        if (filter.Sensitivity is { } sensitivity)
        {
            query = query.Where(x => x.Sensitivity == sensitivity);
        }

        if (filter.SubjectKind is { } subjectKind)
        {
            query = filter.SubjectId is { } subjectId
                ? query.Where(x => x.Subjects.Any(
                    s => s.Kind == subjectKind && s.SubjectId == subjectId))
                : query.Where(x => x.Subjects.Any(s => s.Kind == subjectKind));
        }

        if (filter.OwnerUserId is { } ownerUserId)
        {
            UserId owner = new(ownerUserId);
            query = query.Where(x => x.OwnerUserId == owner);
        }

        if (filter.ResolvesAfter is { } resolvesAfter)
        {
            DateTimeOffset from = From(resolvesAfter);
            query = query.Where(x => x.ResolvesBy >= from);
        }

        if (filter.ResolvesBefore is { } resolvesBefore)
        {
            DateTimeOffset to = To(resolvesBefore);
            query = query.Where(x => x.ResolvesBy <= to);
        }

        // Narrows by the current forecast, which is the last revision rather than a
        // stored column. There is no stored copy to filter on, because a stored copy
        // would be a second truth that drifts from the history (ADR-0023).
        if (filter.ProbabilityAtLeast is { } atLeast)
        {
            query = query.Where(x => x.Revisions
                .OrderByDescending(r => r.Sequence)
                .Select(r => r.Probability)
                .FirstOrDefault() >= atLeast);
        }

        if (filter.ProbabilityAtMost is { } atMost)
        {
            query = query.Where(x => x.Revisions
                .OrderByDescending(r => r.Sequence)
                .Select(r => r.Probability)
                .FirstOrDefault() <= atMost);
        }

        if (filter.TextContains is { Length: > 0 } text)
        {
            string pattern = Pattern(text);

            query = query.Where(x =>
                EF.Functions.ILike(x.Statement, pattern)
                || (x.ResolutionCriteria != null
                    && EF.Functions.ILike(x.ResolutionCriteria, pattern)));
        }

        List<Prediction> rows = await query
            .Include(x => x.Subjects)
            .OrderBy(x => x.ResolvesBy)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToPredictionSummariesAsync(organizationId, rows, asOf, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PredictionDetailModel?> GetPredictionAsync(
        OrganizationId organizationId,
        PredictionId id,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        Prediction? prediction = await _context.Predictions
            .AsNoTracking()
            .Include(x => x.Subjects)
            .Include(x => x.Evidence)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (prediction is null)
        {
            return null;
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        List<PredictionRevision> revisions = await _context.PredictionRevisions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.PredictionId == id)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PredictionSummaryModel summary = (await ToPredictionSummariesAsync(
                organizationId, [prediction], asOf, cancellationToken).ConfigureAwait(false))[0];

        IReadOnlyList<PredictionEvidenceModel> evidence = await ToPredictionEvidenceAsync(
                organizationId, prediction, readable, users, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<IntelligenceEventModel> history = await HistoryAsync(
                organizationId,
                IntelligenceOwnerKind.Prediction,
                id.Value,
                users,
                cancellationToken)
            .ConfigureAwait(false);

        return new PredictionDetailModel(
            summary,
            prediction.ResolutionCriteria,
            prediction.ResolutionNote,
            Name(users, prediction.ResolvedBy),
            prediction.CancelledReason,
            prediction.CancelledAt,
            [
                .. revisions.Select(x => new PredictionRevisionModel(
                    x.Id,
                    x.Sequence,
                    x.Probability,
                    x.Rationale,
                    x.RecordedAt,
                    Name(users, x.RecordedBy))),
            ],
            evidence,
            history);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(decimal Probability, PredictionOutcome Outcome)>>
        ListResolvedForecastsAsync(
            OrganizationId organizationId,
            IReadOnlySet<IntelligenceSensitivity> readable,
            Guid? ownerUserId,
            DateOnly? resolvedAfter,
            DateOnly? resolvedBefore,
            CancellationToken cancellationToken = default)
    {
        IQueryable<Prediction> query = NarrowPredictions(organizationId, readable)
            .Where(x => x.Outcome != null && x.CancelledAt == null);

        if (ownerUserId is { } owner)
        {
            UserId user = new(owner);
            query = query.Where(x => x.OwnerUserId == user);
        }

        if (resolvedAfter is { } after)
        {
            DateTimeOffset from = From(after);
            query = query.Where(x => x.ResolvedAt >= from);
        }

        if (resolvedBefore is { } before)
        {
            DateTimeOffset to = To(before);
            query = query.Where(x => x.ResolvedAt <= to);
        }

        // The last forecast stated before resolution. That is the number the
        // forecaster actually stood behind, rather than their opening guess.
        List<ResolvedRow> rows = await query
            .Select(x => new ResolvedRow(
                x.Revisions
                    .OrderByDescending(r => r.Sequence)
                    .Select(r => (decimal?)r.Probability)
                    .FirstOrDefault(),
                x.Outcome!.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(x => x.Probability is not null)
                .Select(x => (x.Probability!.Value, x.Outcome)),
        ];
    }

    /// <inheritdoc />
    public async Task<(int Open, int Unresolvable)> CountPredictionStatesAsync(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        Guid? ownerUserId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Prediction> query = NarrowPredictions(organizationId, readable);

        if (ownerUserId is { } owner)
        {
            UserId user = new(owner);
            query = query.Where(x => x.OwnerUserId == user);
        }

        int open = await query
            .CountAsync(x => x.Outcome == null && x.CancelledAt == null, cancellationToken)
            .ConfigureAwait(false);

        // Reported, never scored. An unresolvable question has no right answer to
        // measure a forecast against, and averaging it in as half right would
        // manufacture a number from an absence.
        int unresolvable = await query
            .CountAsync(x => x.Outcome == PredictionOutcome.Unresolvable, cancellationToken)
            .ConfigureAwait(false);

        return (open, unresolvable);
    }

    /// <summary>
    /// Projects what a prediction rests on, dropping what the caller may not read.
    /// </summary>
    /// <remarks>
    /// A prediction cites either a source or a signal, never both and never neither.
    /// Whichever it is, the citation is omitted when the caller may not read it,
    /// because the label would name it (§28).
    /// </remarks>
    private async Task<IReadOnlyList<PredictionEvidenceModel>> ToPredictionEvidenceAsync(
        OrganizationId organizationId,
        Prediction prediction,
        IReadOnlySet<IntelligenceSensitivity> readable,
        Dictionary<Guid, string> users,
        CancellationToken cancellationToken)
    {
        if (prediction.Evidence.Count == 0)
        {
            return [];
        }

        IntelligenceSourceId[] sourceIds =
        [
            .. prediction.Evidence.Where(x => x.SourceId != null)
                .Select(x => x.SourceId!.Value)
                .Distinct(),
        ];

        SignalId[] signalIds =
        [
            .. prediction.Evidence.Where(x => x.SignalId != null)
                .Select(x => x.SignalId!.Value)
                .Distinct(),
        ];

        Dictionary<IntelligenceSourceId, string> sources = sourceIds.Length == 0
            ? []
            : await NarrowSources(organizationId, readable)
                .Where(x => sourceIds.Contains(x.Id))
                .Select(x => new Labelled<IntelligenceSourceId>(x.Id, x.Title))
                .ToDictionaryAsync(x => x.Id, x => x.Label, cancellationToken)
                .ConfigureAwait(false);

        Dictionary<SignalId, string> signals = signalIds.Length == 0
            ? []
            : await NarrowSignals(organizationId, readable)
                .Where(x => signalIds.Contains(x.Id))
                .Select(x => new Labelled<SignalId>(x.Id, x.Title))
                .ToDictionaryAsync(x => x.Id, x => x.Label, cancellationToken)
                .ConfigureAwait(false);

        List<PredictionEvidenceModel> projected = [];

        foreach (PredictionEvidence evidence in prediction.Evidence.OrderBy(x => x.AddedAt))
        {
            string? label = evidence.SourceId is { } sourceId
                ? sources.GetValueOrDefault(sourceId)
                : evidence.SignalId is { } signalId
                    ? signals.GetValueOrDefault(signalId)
                    : null;

            if (label is null)
            {
                continue;
            }

            projected.Add(new PredictionEvidenceModel(
                evidence.Id,
                evidence.SourceId,
                evidence.SignalId,
                label,
                evidence.Note,
                evidence.AddedAt,
                Name(users, evidence.AddedBy)));
        }

        return projected;
    }

    private async Task<IReadOnlyList<PredictionSummaryModel>> ToPredictionSummariesAsync(
        OrganizationId organizationId,
        IReadOnlyList<Prediction> predictions,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (predictions.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<(IntelligenceSubjectKind, Guid), string> labels = await LabelsAsync(
                organizationId, predictions.SelectMany(x => x.Subjects), cancellationToken)
            .ConfigureAwait(false);

        PredictionId[] ids = [.. predictions.Select(x => x.Id)];

        List<ForecastRow> forecasts = await _context.PredictionRevisions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.PredictionId))
            .Select(x => new ForecastRow(
                x.PredictionId, x.Sequence, x.Probability, x.RecordedAt, x.RecordedBy))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<PredictionId, List<ForecastRow>> byPrediction = forecasts
            .GroupBy(x => x.PredictionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Sequence).ToList());

        List<PredictionSummaryModel> projected = [];

        foreach (Prediction prediction in predictions)
        {
            List<ForecastRow> history =
                byPrediction.GetValueOrDefault(prediction.Id) ?? [];

            ForecastRow? latest = history.Count == 0 ? null : history[^1];

            projected.Add(new PredictionSummaryModel(
                prediction.Id,
                prediction.Statement,
                prediction.ResolvesBy,
                StatusOf(prediction, asOf),
                latest?.Probability ?? 0m,
                latest?.RecordedAt,
                latest is null ? null : Name(users, latest.RecordedBy),
                prediction.Outcome,
                prediction.ResolvedAt,
                BrierOf(prediction, latest),
                prediction.Sensitivity,
                Name(users, prediction.OwnerUserId),
                prediction.CreatedAt,
                Subjects(prediction.Subjects, labels),
                history.Count,
                prediction.Version));
        }

        return projected;
    }

    /// <summary>
    /// The Brier score, or null.
    /// </summary>
    /// <remarks>
    /// The same arithmetic the aggregate does, applied to a projected row rather
    /// than a loaded aggregate. Null unless the question resolved Yes or No: a
    /// cancelled or unresolvable prediction has nothing to score against
    /// (§14, ADR-0030).
    /// </remarks>
    private static decimal? BrierOf(Prediction prediction, ForecastRow? latest)
    {
        if (latest is null)
        {
            return null;
        }

        return prediction.Outcome switch
        {
            PredictionOutcome.Yes => ForecastCalibration.BrierScore(latest.Probability, true),
            PredictionOutcome.No => ForecastCalibration.BrierScore(latest.Probability, false),
            _ => null,
        };
    }

    /// <summary>Where a prediction stands, as of a moment.</summary>
    /// <remarks>
    /// The projection of <see cref="Prediction.StatusAt"/>, computed here against a
    /// row rather than an aggregate. Status is derived from the clock and never
    /// stored, so a prediction becomes overdue without anybody running a job.
    /// </remarks>
    private static PredictionStatus StatusOf(Prediction prediction, DateTimeOffset asOf) =>
        prediction.CancelledAt is not null
            ? PredictionStatus.Cancelled
            : prediction.Outcome is not null
                ? PredictionStatus.Resolved
                : asOf > prediction.ResolvesBy
                    ? PredictionStatus.AwaitingResolution
                    : PredictionStatus.Open;

    /// <summary>The SQL form of <see cref="StatusOf"/>.</summary>
    private static IQueryable<Prediction> WithStatus(
        IQueryable<Prediction> query,
        PredictionStatus status,
        DateTimeOffset asOf) => status switch
    {
        PredictionStatus.Cancelled => query.Where(x => x.CancelledAt != null),

        PredictionStatus.Resolved =>
            query.Where(x => x.CancelledAt == null && x.Outcome != null),

        PredictionStatus.AwaitingResolution => query.Where(
            x => x.CancelledAt == null && x.Outcome == null && x.ResolvesBy < asOf),

        _ => query.Where(
            x => x.CancelledAt == null && x.Outcome == null && x.ResolvesBy >= asOf),
    };

    private IQueryable<Thesis> NarrowTheses(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable)
    {
        IntelligenceSensitivity[] allowed = [.. readable];

        return _context.Theses
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Sensitivity));
    }

    private IQueryable<Prediction> NarrowPredictions(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable)
    {
        IntelligenceSensitivity[] allowed = [.. readable];

        return _context.Predictions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Sensitivity));
    }

    /// <summary>One label, keyed. A named type, because EF cannot project a tuple.</summary>
    private sealed record Labelled<TKey>(TKey Id, string Label);

    private sealed record CitedSignal(SignalId Id, string Title, SignalVerification Verification);

    private sealed record StanceCount(ThesisId ThesisId, ThesisEvidenceStance Stance, int Count);

    private sealed record ForecastRow(
        PredictionId PredictionId,
        int Sequence,
        decimal Probability,
        DateTimeOffset RecordedAt,
        UserId RecordedBy);

    private sealed record ResolvedRow(decimal? Probability, PredictionOutcome Outcome);
}
