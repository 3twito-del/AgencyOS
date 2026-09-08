using AgencyOS.Application.Intelligence;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// The standing programmes: watchlists, the talent radar and research cases, plus
/// the two derived views that read across them.
/// </summary>
/// <remarks>
/// Nothing here is ranked or scored. A watchlist is a list somebody keeps, a radar
/// entry is a person somebody is looking at, and relationship intelligence is a set
/// of counts with the recorded assessment kept visibly apart from them. Fourteen
/// emails is not a strong relationship, and these projections refuse to say it is
/// (§18, ADR-0030).
/// </remarks>
public sealed partial class IntelligenceQueries
{
    /// <summary>How far ahead the command centre looks for a resolution date.</summary>
    private static readonly TimeSpan DueSoon = TimeSpan.FromDays(30);

    /// <summary>How many interactions the recent-kinds summary reads.</summary>
    private const int RecentInteractions = 20;

    // --------------------------------------------------------------- watchlists

    /// <inheritdoc />
    public async Task<IReadOnlyList<WatchlistSummaryModel>> ListWatchlistsAsync(
        OrganizationId organizationId,
        IntelligenceScope<WatchlistFilter> scope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IQueryable<Watchlist> query = NarrowWatchlists(organizationId, scope.Readable);
        WatchlistFilter filter = scope.Filter;

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Sensitivity is { } sensitivity)
        {
            query = query.Where(x => x.Sensitivity == sensitivity);
        }

        if (filter.OwnerUserId is { } ownerUserId)
        {
            UserId owner = new(ownerUserId);
            query = query.Where(x => x.OwnerUserId == owner);
        }

        if (filter.SubjectKind is { } subjectKind)
        {
            query = filter.SubjectId is { } subjectId
                ? query.Where(x => x.Entries.Any(
                    e => e.Kind == subjectKind && e.SubjectId == subjectId))
                : query.Where(x => x.Entries.Any(e => e.Kind == subjectKind));
        }

        // "Not looked at since." A watchlist nobody has ever reviewed counts, which
        // is the point: it is the one most likely to have gone stale.
        if (filter.ReviewedBefore is { } reviewedBefore)
        {
            DateTimeOffset to = To(reviewedBefore);
            query = query.Where(x => x.LastReviewedAt == null || x.LastReviewedAt <= to);
        }

        if (filter.TextContains is { Length: > 0 } text)
        {
            string pattern = Pattern(text);

            query = query.Where(x =>
                EF.Functions.ILike(x.Name, pattern)
                || (x.Purpose != null && EF.Functions.ILike(x.Purpose, pattern)));
        }

        List<Watchlist> rows = await query
            .Include(x => x.Entries)
            .OrderBy(x => x.Name)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(x => ToWatchlist(x, users))];
    }

    /// <inheritdoc />
    public async Task<WatchlistDetailModel?> GetWatchlistAsync(
        OrganizationId organizationId,
        WatchlistId id,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken = default)
    {
        Watchlist? watchlist = await _context.Watchlists
            .AsNoTracking()
            .Include(x => x.Entries)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (watchlist is null)
        {
            return null;
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<(IntelligenceSubjectKind, Guid), string> labels =
            await LabelsAsync(organizationId, watchlist.Entries, cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<IntelligenceEventModel> history = await HistoryAsync(
                organizationId, IntelligenceOwnerKind.Watchlist, id.Value, users, cancellationToken)
            .ConfigureAwait(false);

        return new WatchlistDetailModel(
            ToWatchlist(watchlist, users),
            Subjects(watchlist.Entries, labels),
            history);
    }

    /// <inheritdoc />
    public async Task<WatchlistActivityModel?> GetWatchlistActivityAsync(
        OrganizationId organizationId,
        WatchlistId id,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        Watchlist? watchlist = await _context.Watchlists
            .AsNoTracking()
            .Include(x => x.Entries)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (watchlist is null)
        {
            return null;
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        IQueryable<WatchlistEntry> members = Members(organizationId, id);

        // Overlap by subject, computed at read time. Nothing is stamped on a signal
        // saying it is watched: such a flag would have to be maintained on every
        // membership change, and would be silently wrong the first time one was
        // missed (ADR-0030).
        IQueryable<Signal> about = NarrowSignals(organizationId, readable)
            .Where(x => x.Subjects.Any(
                s => members.Any(m => m.Kind == s.Kind && m.SubjectId == s.SubjectId)));

        List<Signal> recent = await about
            .Include(x => x.Subjects)
            .OrderByDescending(x => x.ObservedAt)
            .Take(RelatedLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int since = watchlist.LastReviewedAt is { } reviewed
            ? await about.CountAsync(x => x.RecordedAt > reviewed, cancellationToken)
                .ConfigureAwait(false)
            : await about.CountAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset? newest = await about
            .OrderByDescending(x => x.RecordedAt)
            .Select(x => (DateTimeOffset?)x.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Thesis> theses = await NarrowTheses(organizationId, readable)
            .Where(x => x.Status == ThesisStatus.Active
                && x.Subjects.Any(
                    s => members.Any(m => m.Kind == s.Kind && m.SubjectId == s.SubjectId)))
            .Include(x => x.Subjects)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(RelatedLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Prediction> predictions = await NarrowPredictions(organizationId, readable)
            .Where(x => x.Outcome == null
                && x.CancelledAt == null
                && x.Subjects.Any(
                    s => members.Any(m => m.Kind == s.Kind && m.SubjectId == s.SubjectId)))
            .Include(x => x.Subjects)
            .OrderBy(x => x.ResolvesBy)
            .Take(RelatedLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new WatchlistActivityModel(
            ToWatchlist(watchlist, users),
            await ToSignalSummariesAsync(organizationId, recent, readable, cancellationToken)
                .ConfigureAwait(false),
            since,
            await ToThesisSummariesAsync(organizationId, theses, readable, cancellationToken)
                .ConfigureAwait(false),
            await ToPredictionSummariesAsync(organizationId, predictions, asOf, cancellationToken)
                .ConfigureAwait(false),
            newest);
    }

    // -------------------------------------------------------------------- radar

    /// <inheritdoc />
    public async Task<IReadOnlyList<TalentRadarSummaryModel>> ListRadarAsync(
        OrganizationId organizationId,
        IntelligenceScope<TalentRadarFilter> scope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IQueryable<TalentRadarEntry> query = NarrowRadar(organizationId, scope.Readable);
        TalentRadarFilter filter = scope.Filter;

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Priority is { } priority)
        {
            query = query.Where(x => x.Priority == priority);
        }

        if (filter.Sensitivity is { } sensitivity)
        {
            query = query.Where(x => x.Sensitivity == sensitivity);
        }

        if (filter.OwnerUserId is { } ownerUserId)
        {
            UserId owner = new(ownerUserId);
            query = query.Where(x => x.OwnerUserId == owner);
        }

        if (filter.PersonId is { } personId)
        {
            PersonId person = new(personId);
            query = query.Where(x => x.PersonId == person);
        }

        if (filter.WatchlistId is { } watchlistId)
        {
            IQueryable<WatchlistEntry> members =
                Members(organizationId, new WatchlistId(watchlistId));

            query = query.Where(x => members.Any(
                m => m.Kind == IntelligenceSubjectKind.Person && m.SubjectId == x.PersonId.Value));
        }

        if (filter.Discipline is { Length: > 0 } discipline)
        {
            string pattern = Pattern(discipline);

            query = query.Where(x =>
                x.IntendedDisciplines != null
                && EF.Functions.ILike(x.IntendedDisciplines, pattern));
        }

        if (filter.ReviewedBefore is { } reviewedBefore)
        {
            DateTimeOffset to = To(reviewedBefore);
            query = query.Where(x => x.LastReviewedAt == null || x.LastReviewedAt <= to);
        }

        if (filter.TextContains is { Length: > 0 } text)
        {
            string pattern = Pattern(text);

            query = query.Where(x =>
                EF.Functions.ILike(x.Rationale, pattern)
                || (x.IntendedDisciplines != null
                    && EF.Functions.ILike(x.IntendedDisciplines, pattern))
                || _context.People.Any(
                    p => p.Id == x.PersonId && EF.Functions.ILike(p.DisplayName, pattern)));
        }

        List<TalentRadarEntry> rows = await query
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.FirstObservedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToRadarSummariesAsync(organizationId, rows, scope.Readable, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TalentRadarDetailModel?> GetRadarEntryAsync(
        OrganizationId organizationId,
        TalentRadarEntryId id,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        TalentRadarEntry? entry = await _context.TalentRadarEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return null;
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        TalentRadarSummaryModel summary = (await ToRadarSummariesAsync(
                organizationId, [entry], readable, cancellationToken).ConfigureAwait(false))[0];

        // Projected from M4 rather than copied. A second copy of somebody's credits
        // would go stale the first time the real one was corrected (ADR-0030).
        List<RadarCreditModel> credits = await _context.Credits
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.PersonId == entry.PersonId)
            .OrderByDescending(x => x.Year)
            .ThenBy(x => x.Title)
            .Take(RelatedLimit)
            .Select(x => new RadarCreditModel(
                x.Id.Value, x.Title, x.Role, x.Type.ToString(), x.Year))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid subjectId = entry.PersonId.Value;

        return new TalentRadarDetailModel(
            summary,
            entry.DismissedReason,
            entry.ConvertedAt,
            Name(users, entry.ConvertedBy),
            credits,
            Disciplines(entry.IntendedDisciplines),
            await SignalsAboutAsync(
                    organizationId,
                    IntelligenceSubjectKind.Person,
                    subjectId,
                    readable,
                    cancellationToken)
                .ConfigureAwait(false),
            await ThesesAboutAsync(
                    organizationId,
                    IntelligenceSubjectKind.Person,
                    subjectId,
                    readable,
                    activeOnly: false,
                    cancellationToken)
                .ConfigureAwait(false),
            await PredictionsAboutAsync(
                    organizationId,
                    IntelligenceSubjectKind.Person,
                    subjectId,
                    readable,
                    openOnly: false,
                    asOf,
                    cancellationToken)
                .ConfigureAwait(false),
            await WatchlistsContainingAsync(
                    organizationId,
                    IntelligenceSubjectKind.Person,
                    subjectId,
                    readable,
                    cancellationToken)
                .ConfigureAwait(false),
            await HistoryAsync(
                    organizationId,
                    IntelligenceOwnerKind.TalentRadarEntry,
                    id.Value,
                    users,
                    cancellationToken)
                .ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<TalentRadarSummaryModel>> ToRadarSummariesAsync(
        OrganizationId organizationId,
        IReadOnlyList<TalentRadarEntry> entries,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        PersonId[] personIds = [.. entries.Select(x => x.PersonId).Distinct()];

        List<RadarPerson> people = await _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && personIds.Contains(x.Id))
            .Select(x => new RadarPerson(
                x.Id,
                x.DisplayName,
                x.Title,
                x.PrimaryCompanyId == null
                    ? null
                    : _context.Companies
                        .Where(c => c.Id == x.PrimaryCompanyId)
                        .Select(c => c.Name)
                        .FirstOrDefault()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<PersonId, RadarPerson> byPerson = people.ToDictionary(x => x.Id);

        Guid[] subjectIds = [.. personIds.Select(x => x.Value)];
        IntelligenceSensitivity[] allowed = [.. readable];

        Dictionary<Guid, int> signalCounts = await _context.Signals
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Sensitivity))
            .SelectMany(x => x.Subjects
                .Where(s => s.Kind == IntelligenceSubjectKind.Person
                    && subjectIds.Contains(s.SubjectId))
                .Select(s => s.SubjectId))
            .GroupBy(x => x)
            .Select(g => new Counted<Guid>(g.Key, g.Count()))
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. entries.Select(x =>
            {
                RadarPerson? person = byPerson.GetValueOrDefault(x.PersonId);

                return new TalentRadarSummaryModel(
                    x.Id,
                    x.PersonId.Value,
                    person?.DisplayName ?? "Unknown person",
                    person?.Title,
                    person?.CompanyName,
                    x.Status,
                    x.Priority,
                    x.Rationale,
                    x.IntendedDisciplines,
                    x.Sensitivity,
                    Name(users, x.OwnerUserId),
                    x.FirstObservedAt,
                    x.LastReviewedAt,
                    x.ProspectId?.Value,
                    signalCounts.GetValueOrDefault(x.PersonId.Value),
                    x.Version);
            }),
        ];
    }

    // ---------------------------------------------------------- research cases

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResearchCaseSummaryModel>> ListResearchCasesAsync(
        OrganizationId organizationId,
        IntelligenceScope<ResearchCaseFilter> scope,
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IQueryable<ResearchCase> query = NarrowResearchCases(organizationId, scope.Readable);
        ResearchCaseFilter filter = scope.Filter;

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Sensitivity is { } sensitivity)
        {
            query = query.Where(x => x.Sensitivity == sensitivity);
        }

        if (filter.OwnerUserId is { } ownerUserId)
        {
            UserId owner = new(ownerUserId);
            query = query.Where(x => x.OwnerUserId == owner);
        }

        if (filter.SubjectKind is { } subjectKind)
        {
            query = filter.SubjectId is { } subjectId
                ? query.Where(x => x.Subjects.Any(
                    s => s.Kind == subjectKind && s.SubjectId == subjectId))
                : query.Where(x => x.Subjects.Any(s => s.Kind == subjectKind));
        }

        if (filter.TextContains is { Length: > 0 } text)
        {
            string pattern = Pattern(text);

            query = query.Where(x =>
                EF.Functions.ILike(x.Question, pattern)
                || (x.Context != null && EF.Functions.ILike(x.Context, pattern)));
        }

        List<ResearchCase> rows = await query
            .Include(x => x.Subjects)
            .Include(x => x.Links)
            .OrderByDescending(x => x.OpenedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToResearchSummariesAsync(
                organizationId, rows, scope.Readable, asOf, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ResearchCaseDetailModel?> GetResearchCaseAsync(
        OrganizationId organizationId,
        ResearchCaseId id,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ResearchCase? researchCase = await _context.ResearchCases
            .AsNoTracking()
            .Include(x => x.Subjects)
            .Include(x => x.Links)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (researchCase is null)
        {
            return null;
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        ResearchCaseSummaryModel summary = (await ToResearchSummariesAsync(
                organizationId, [researchCase], readable, asOf, cancellationToken)
            .ConfigureAwait(false))[0];

        IntelligenceSourceId[] sourceIds =
        [
            .. Linked(researchCase, ResearchLinkKind.Source)
                .Select(x => new IntelligenceSourceId(x)),
        ];

        SignalId[] signalIds =
            [.. Linked(researchCase, ResearchLinkKind.Signal).Select(x => new SignalId(x))];

        ThesisId[] thesisIds =
            [.. Linked(researchCase, ResearchLinkKind.Thesis).Select(x => new ThesisId(x))];

        PredictionId[] predictionIds =
        [
            .. Linked(researchCase, ResearchLinkKind.Prediction)
                .Select(x => new PredictionId(x)),
        ];

        TaskItemId[] taskIds =
            [.. Linked(researchCase, ResearchLinkKind.Task).Select(x => new TaskItemId(x))];

        List<IntelligenceSource> sources = sourceIds.Length == 0
            ? []
            : await NarrowSources(organizationId, readable)
                .Where(x => sourceIds.Contains(x.Id))
                .OrderByDescending(x => x.ObservedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        Dictionary<IntelligenceSourceId, int> citations = await SourceCitationsAsync(
                organizationId, [.. sources.Select(x => x.Id)], readable, cancellationToken)
            .ConfigureAwait(false);

        List<Signal> signals = signalIds.Length == 0
            ? []
            : await NarrowSignals(organizationId, readable)
                .Where(x => signalIds.Contains(x.Id))
                .Include(x => x.Subjects)
                .OrderByDescending(x => x.ObservedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        List<Thesis> theses = thesisIds.Length == 0
            ? []
            : await NarrowTheses(organizationId, readable)
                .Where(x => thesisIds.Contains(x.Id))
                .Include(x => x.Subjects)
                .OrderByDescending(x => x.UpdatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        List<Prediction> predictions = predictionIds.Length == 0
            ? []
            : await NarrowPredictions(organizationId, readable)
                .Where(x => predictionIds.Contains(x.Id))
                .Include(x => x.Subjects)
                .OrderBy(x => x.ResolvesBy)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<ResearchTaskModel> tasks =
            await TasksAsync(organizationId, taskIds, users, asOf, cancellationToken)
                .ConfigureAwait(false);

        return new ResearchCaseDetailModel(
            summary,
            researchCase.Context,
            researchCase.Conclusion,
            [.. sources.Select(x => ToSource(x, users, citations.GetValueOrDefault(x.Id)))],
            await ToSignalSummariesAsync(organizationId, signals, readable, cancellationToken)
                .ConfigureAwait(false),
            await ToThesisSummariesAsync(organizationId, theses, readable, cancellationToken)
                .ConfigureAwait(false),
            await ToPredictionSummariesAsync(organizationId, predictions, asOf, cancellationToken)
                .ConfigureAwait(false),
            tasks,
            await HistoryAsync(
                    organizationId,
                    IntelligenceOwnerKind.ResearchCase,
                    id.Value,
                    users,
                    cancellationToken)
                .ConfigureAwait(false));
    }

    /// <summary>
    /// Counts what a research case has gathered, over what the caller may read.
    /// </summary>
    /// <remarks>
    /// Links are stored per kind, so the counts are taken from the link rows and
    /// then narrowed against the things they point at. A case that gathered four
    /// signals shows two to somebody who may read two, and the count says two —
    /// never four with two rows underneath it (§28).
    /// </remarks>
    private async Task<IReadOnlyList<ResearchCaseSummaryModel>> ToResearchSummariesAsync(
        OrganizationId organizationId,
        IReadOnlyList<ResearchCase> cases,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (cases.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<(IntelligenceSubjectKind, Guid), string> labels = await LabelsAsync(
                organizationId, cases.SelectMany(x => x.Subjects), cancellationToken)
            .ConfigureAwait(false);

        HashSet<Guid> readableSources = await ReadableIdsAsync(
                NarrowSources(organizationId, readable).Select(x => x.Id.Value),
                Linked(cases, ResearchLinkKind.Source),
                cancellationToken)
            .ConfigureAwait(false);

        HashSet<Guid> readableSignals = await ReadableIdsAsync(
                NarrowSignals(organizationId, readable).Select(x => x.Id.Value),
                Linked(cases, ResearchLinkKind.Signal),
                cancellationToken)
            .ConfigureAwait(false);

        HashSet<Guid> readableTheses = await ReadableIdsAsync(
                NarrowTheses(organizationId, readable).Select(x => x.Id.Value),
                Linked(cases, ResearchLinkKind.Thesis),
                cancellationToken)
            .ConfigureAwait(false);

        HashSet<Guid> readablePredictions = await ReadableIdsAsync(
                NarrowPredictions(organizationId, readable).Select(x => x.Id.Value),
                Linked(cases, ResearchLinkKind.Prediction),
                cancellationToken)
            .ConfigureAwait(false);

        Guid[] allTasks = Linked(cases, ResearchLinkKind.Task);

        HashSet<Guid> openTasks = allTasks.Length == 0
            ? []
            : [.. await _context.Tasks
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId
                    && allTasks.Contains(x.Id.Value)
                    && x.State == TaskState.Open)
                .Select(x => x.Id.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)];

        return
        [
            .. cases.Select(x => new ResearchCaseSummaryModel(
                x.Id,
                x.Question,
                x.Status,
                x.Sensitivity,
                Name(users, x.OwnerUserId),
                x.OpenedAt,
                x.ClosedAt,
                Subjects(x.Subjects, labels),
                Count(x, ResearchLinkKind.Source, readableSources),
                Count(x, ResearchLinkKind.Signal, readableSignals),
                Count(x, ResearchLinkKind.Thesis, readableTheses),
                Count(x, ResearchLinkKind.Prediction, readablePredictions),
                Linked(x, ResearchLinkKind.Task).Length,
                Count(x, ResearchLinkKind.Task, openTasks),
                x.Version)),
        ];
    }

    /// <summary>The M2 tasks a research case is working through.</summary>
    private async Task<IReadOnlyList<ResearchTaskModel>> TasksAsync(
        OrganizationId organizationId,
        TaskItemId[] taskIds,
        Dictionary<Guid, string> users,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        if (taskIds.Length == 0)
        {
            return [];
        }

        List<TaskItem> tasks = await _context.Tasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && taskIds.Contains(x.Id))
            .OrderBy(x => x.DueAt == null)
            .ThenBy(x => x.DueAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. tasks.Select(x => new ResearchTaskModel(
                x.Id.Value,
                x.Title,
                x.State.ToString(),
                x.DueAt,
                x.State == TaskState.Open && x.DueAt is { } due && due < asOf,
                Name(users, x.AssignedTo))),
        ];
    }

    // --------------------------------------------------- relationship intelligence

    /// <inheritdoc />
    public async Task<RelationshipIntelligenceModel?> GetRelationshipIntelligenceAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        // A working relationship is with a person or with a company. The other eight
        // subject kinds are records, not counterparties, and inventing a
        // relationship view for a contract would be arithmetic in search of a
        // meaning (§18).
        if (kind is not (IntelligenceSubjectKind.Person or IntelligenceSubjectKind.Company))
        {
            return null;
        }

        PersonId? person =
            kind == IntelligenceSubjectKind.Person ? new PersonId(subjectId) : null;

        CompanyId? company =
            kind == IntelligenceSubjectKind.Company ? new CompanyId(subjectId) : null;

        RadarPerson? party = person is { } p
            ? await _context.People
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.Id == p)
                .Select(x => new RadarPerson(
                    x.Id,
                    x.DisplayName,
                    x.Title,
                    x.PrimaryCompanyId == null
                        ? null
                        : _context.Companies
                            .Where(c => c.Id == x.PrimaryCompanyId)
                            .Select(c => c.Name)
                            .FirstOrDefault()))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : await _context.Companies
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.Id == company!.Value)
                .Select(x => new RadarPerson(default, x.Name, null, null))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        if (party is null)
        {
            return null;
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        // ---- what a person wrote down ----

        IQueryable<ProfessionalRelationship> recorded = person is { } forPerson
            ? _context.Relationships.AsNoTracking().Where(
                x => x.OrganizationId == organizationId
                    && (x.FromPersonId == forPerson || x.ToPersonId == forPerson))
            : _context.Relationships.AsNoTracking().Where(
                x => x.OrganizationId == organizationId
                    && (x.FromCompanyId == company!.Value || x.ToCompanyId == company!.Value));

        RecordedAssessment? assessment = await recorded
            .Where(x => x.Status == RelationshipStatus.Active && x.Strength != null)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new RecordedAssessment(x.Strength, x.Notes))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        UserId? owner = person is { } represented
            ? await LeadAgentAsync(organizationId, represented, cancellationToken)
                .ConfigureAwait(false)
            : null;

        // ---- counted from M2 rows ----

        IQueryable<Interaction> interactions = person is { } participant
            ? _context.Interactions.AsNoTracking().Where(
                i => i.OrganizationId == organizationId
                    && _context.InteractionParticipants.Any(
                        x => x.InteractionId == i.Id && x.PersonId == participant))
            : _context.Interactions.AsNoTracking().Where(
                i => i.OrganizationId == organizationId
                    && _context.InteractionParticipants.Any(
                        x => x.InteractionId == i.Id && x.CompanyId == company!.Value));

        IQueryable<Interaction> past = interactions.Where(i => i.OccurredAt <= asOf);

        LastInteraction? last = await past
            .OrderByDescending(i => i.OccurredAt)
            .Select(i => new LastInteraction(i.OccurredAt, i.Type))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset from30 = asOf.AddDays(-30);
        DateTimeOffset from90 = asOf.AddDays(-90);
        DateTimeOffset from365 = asOf.AddDays(-365);

        int in30 = await past.CountAsync(i => i.OccurredAt >= from30, cancellationToken)
            .ConfigureAwait(false);

        int in90 = await past.CountAsync(i => i.OccurredAt >= from90, cancellationToken)
            .ConfigureAwait(false);

        int in365 = await past.CountAsync(i => i.OccurredAt >= from365, cancellationToken)
            .ConfigureAwait(false);

        List<InteractionType> kinds = await past
            .OrderByDescending(i => i.OccurredAt)
            .Take(RecentInteractions)
            .Select(i => i.Type)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IQueryable<TaskItem> tasks = person is { } about
            ? _context.Tasks.AsNoTracking().Where(
                x => x.OrganizationId == organizationId && x.RelatedPersonId == about)
            : _context.Tasks.AsNoTracking().Where(
                x => x.OrganizationId == organizationId && x.RelatedCompanyId == company!.Value);

        int openTasks = await tasks.CountAsync(x => x.State == TaskState.Open, cancellationToken)
            .ConfigureAwait(false);

        int overdueTasks = await tasks
            .CountAsync(
                x => x.State == TaskState.Open && x.DueAt != null && x.DueAt < asOf,
                cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset? nextDue = await tasks
            .Where(x => x.State == TaskState.Open && x.DueAt != null && x.DueAt >= asOf)
            .OrderBy(x => x.DueAt)
            .Select(x => x.DueAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new RelationshipIntelligenceModel(
            subjectId,
            kind,
            party.DisplayName,
            party.Title,
            kind == IntelligenceSubjectKind.Person ? party.CompanyName : null,

            // Recorded assessment, kept visibly apart from everything counted below
            // it. "4 of 5" is somebody's judgment and says so; the counts are facts
            // and say only that (§18).
            assessment?.Strength is { } strength ? $"{strength} of 5" : null,
            assessment?.Notes,
            Name(users, owner),

            last?.OccurredAt,
            last?.Type.ToString(),
            last is null ? null : (int)(asOf - last.OccurredAt).TotalDays,
            in30,
            in90,
            in365,
            [.. kinds.Distinct().Select(x => x.ToString())],
            openTasks,
            overdueTasks,
            nextDue,

            await SignalsAboutAsync(organizationId, kind, subjectId, readable, cancellationToken)
                .ConfigureAwait(false),
            await WatchlistsContainingAsync(
                    organizationId, kind, subjectId, readable, cancellationToken)
                .ConfigureAwait(false),
            await ThesesAboutAsync(
                    organizationId, kind, subjectId, readable, activeOnly: true, cancellationToken)
                .ConfigureAwait(false),
            await PredictionsAboutAsync(
                    organizationId, kind, subjectId, readable, openOnly: true, asOf,
                    cancellationToken)
                .ConfigureAwait(false));
    }

    /// <summary>Who owns the relationship, when M4 says somebody does.</summary>
    /// <remarks>
    /// The open lead agent of a live representation. Deliberately not whoever
    /// created the contact record: that is who typed it in, which is a different
    /// question and usually a different person.
    /// </remarks>
    private async Task<UserId?> LeadAgentAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        RepresentationStatus[] live = [.. Representation.NonTerminalStatuses];

        List<UserId> lead = await _context.RepresentationTeamMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId
                && m.EndsOn == null
                && m.Role == RepresentationTeamRole.Lead
                && _context.Representations.Any(
                    r => r.Id == m.RepresentationId
                        && r.PersonId == personId
                        && live.Contains(r.Status)))
            .Select(m => m.UserId)
            .Take(1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return lead.Count == 0 ? null : lead[0];
    }

    // ------------------------------------------------------------ command centre

    /// <inheritdoc />
    public async Task<IntelligenceCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset soon = asOf + DueSoon;

        IQueryable<Prediction> awaiting = NarrowPredictions(organizationId, readable)
            .Where(x => x.CancelledAt == null && x.Outcome == null && x.ResolvesBy < asOf);

        IQueryable<Prediction> dueSoon = NarrowPredictions(organizationId, readable)
            .Where(x => x.CancelledAt == null
                && x.Outcome == null
                && x.ResolvesBy >= asOf
                && x.ResolvesBy <= soon);

        IQueryable<Signal> disputed = NarrowSignals(organizationId, readable)
            .Where(x => x.Verification == SignalVerification.Disputed);

        IQueryable<TalentRadarEntry> awaitingReview = NarrowRadar(organizationId, readable)
            .Where(x => x.Status == TalentRadarStatus.ReadyForReview);

        List<Prediction> awaitingRows = await awaiting
            .Include(x => x.Subjects)
            .OrderBy(x => x.ResolvesBy)
            .Take(PanelLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Prediction> dueSoonRows = await dueSoon
            .Include(x => x.Subjects)
            .OrderBy(x => x.ResolvesBy)
            .Take(PanelLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Signal> disputedRows = await disputed
            .Include(x => x.Subjects)
            .OrderByDescending(x => x.ObservedAt)
            .Take(PanelLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<TalentRadarEntry> radarRows = await awaitingReview
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.LastReviewedAt)
            .Take(PanelLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new IntelligenceCommandCenterModel(
            await ToPredictionSummariesAsync(
                    organizationId, awaitingRows, asOf, cancellationToken)
                .ConfigureAwait(false),
            await ToPredictionSummariesAsync(organizationId, dueSoonRows, asOf, cancellationToken)
                .ConfigureAwait(false),
            await ToSignalSummariesAsync(
                    organizationId, disputedRows, readable, cancellationToken)
                .ConfigureAwait(false),
            await ToRadarSummariesAsync(organizationId, radarRows, readable, cancellationToken)
                .ConfigureAwait(false),
            await UnreviewedWatchlistsAsync(organizationId, readable, cancellationToken)
                .ConfigureAwait(false),
            await OverdueResearchAsync(organizationId, readable, asOf, cancellationToken)
                .ConfigureAwait(false),

            // Counted over the whole set rather than over the page, so the panel can
            // say "20 of 63" honestly.
            await awaiting.CountAsync(cancellationToken).ConfigureAwait(false),
            await dueSoon.CountAsync(cancellationToken).ConfigureAwait(false),
            await disputed.CountAsync(cancellationToken).ConfigureAwait(false),
            await awaitingReview.CountAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Active watchlists with a readable signal recorded since they were last
    /// reviewed.
    /// </summary>
    /// <remarks>
    /// Derived by subject overlap in two passes: the memberships, then the newest
    /// readable signal per subject. A watchlist nobody has ever reviewed qualifies
    /// on its first signal, which is the behaviour somebody setting one up expects.
    /// </remarks>
    private async Task<IReadOnlyList<WatchlistSummaryModel>> UnreviewedWatchlistsAsync(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken)
    {
        List<Watchlist> active = await NarrowWatchlists(organizationId, readable)
            .Where(x => x.Status == WatchlistStatus.Active)
            .Include(x => x.Entries)
            .OrderBy(x => x.Name)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid[] watched =
            [.. active.SelectMany(x => x.Entries).Select(x => x.SubjectId).Distinct()];

        if (watched.Length == 0)
        {
            return [];
        }

        List<SubjectActivity> activity = await NarrowSignals(organizationId, readable)
            .Where(x => x.Subjects.Any(s => watched.Contains(s.SubjectId)))
            .SelectMany(x => x.Subjects
                .Where(s => watched.Contains(s.SubjectId))
                .Select(s => new SubjectActivity(s.SubjectId, x.RecordedAt)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, DateTimeOffset> newest = activity
            .GroupBy(x => x.SubjectId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.RecordedAt));

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        List<WatchlistSummaryModel> withActivity = [];

        foreach (Watchlist watchlist in active)
        {
            DateTimeOffset? latest = watchlist.Entries
                .Select(x => newest.TryGetValue(x.SubjectId, out DateTimeOffset at)
                    ? at
                    : (DateTimeOffset?)null)
                .Where(x => x is not null)
                .DefaultIfEmpty(null)
                .Max();

            if (latest is null)
            {
                continue;
            }

            if (watchlist.LastReviewedAt is { } reviewed && latest <= reviewed)
            {
                continue;
            }

            withActivity.Add(ToWatchlist(watchlist, users));
        }

        return [.. withActivity.Take(PanelLimit)];
    }

    /// <summary>Research cases with an open task that is already late.</summary>
    private async Task<IReadOnlyList<ResearchCaseSummaryModel>> OverdueResearchAsync(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        List<ResearchCase> cases = await NarrowResearchCases(organizationId, readable)
            .Where(x => x.Status == ResearchCaseStatus.Open
                && x.Links.Any(l => l.Kind == ResearchLinkKind.Task
                    && _context.Tasks.Any(t => t.Id.Value == l.LinkedId
                        && t.State == TaskState.Open
                        && t.DueAt != null
                        && t.DueAt < asOf)))
            .Include(x => x.Subjects)
            .Include(x => x.Links)
            .OrderBy(x => x.OpenedAt)
            .Take(PanelLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToResearchSummariesAsync(
                organizationId, cases, readable, asOf, cancellationToken)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------- shared projections

    /// <summary>Readable signals about one subject, newest first.</summary>
    private async Task<IReadOnlyList<SignalSummaryModel>> SignalsAboutAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken)
    {
        List<Signal> signals = await NarrowSignals(organizationId, readable)
            .Where(x => x.Subjects.Any(s => s.Kind == kind && s.SubjectId == subjectId))
            .Include(x => x.Subjects)
            .OrderByDescending(x => x.ObservedAt)
            .Take(RelatedLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToSignalSummariesAsync(organizationId, signals, readable, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ThesisSummaryModel>> ThesesAboutAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        IQueryable<Thesis> query = NarrowTheses(organizationId, readable)
            .Where(x => x.Subjects.Any(s => s.Kind == kind && s.SubjectId == subjectId));

        if (activeOnly)
        {
            query = query.Where(x => x.Status == ThesisStatus.Active);
        }

        List<Thesis> theses = await query
            .Include(x => x.Subjects)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(RelatedLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToThesisSummariesAsync(organizationId, theses, readable, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PredictionSummaryModel>> PredictionsAboutAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        bool openOnly,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        IQueryable<Prediction> query = NarrowPredictions(organizationId, readable)
            .Where(x => x.Subjects.Any(s => s.Kind == kind && s.SubjectId == subjectId));

        if (openOnly)
        {
            query = query.Where(x => x.Outcome == null && x.CancelledAt == null);
        }

        List<Prediction> predictions = await query
            .Include(x => x.Subjects)
            .OrderBy(x => x.ResolvesBy)
            .Take(RelatedLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToPredictionSummariesAsync(
                organizationId, predictions, asOf, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<WatchlistSummaryModel>> WatchlistsContainingAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken)
    {
        List<Watchlist> watchlists = await NarrowWatchlists(organizationId, readable)
            .Where(x => x.Entries.Any(e => e.Kind == kind && e.SubjectId == subjectId))
            .Include(x => x.Entries)
            .OrderBy(x => x.Name)
            .Take(RelatedLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        return [.. watchlists.Select(x => ToWatchlist(x, users))];
    }

    private static WatchlistSummaryModel ToWatchlist(
        Watchlist watchlist,
        Dictionary<Guid, string> users) =>
        new(
            watchlist.Id,
            watchlist.Name,
            watchlist.Purpose,
            watchlist.Status,
            watchlist.Sensitivity,
            Name(users, watchlist.OwnerUserId),
            watchlist.LastReviewedAt,
            Name(users, watchlist.LastReviewedBy),
            watchlist.CreatedAt,
            watchlist.Entries.Count,
            watchlist.Version);

    /// <summary>
    /// Splits a free-text discipline note into the disciplines it lists.
    /// </summary>
    /// <remarks>
    /// A radar entry is about somebody who is not a client, so there is no M4 talent
    /// profile to read disciplines from. What an analyst typed is kept as typed and
    /// only split for display; it is deliberately not coerced into the
    /// <c>ProfessionalDiscipline</c> enum, because guessing which member "writer /
    /// showrunner" means would record a fact nobody asserted (§18).
    /// </remarks>
    private static IReadOnlyList<string> Disciplines(string? intended) =>
        intended is not { Length: > 0 }
            ? []
            : [.. intended
                .Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Which of these identifiers name something the caller may read.</summary>
    private static async Task<HashSet<Guid>> ReadableIdsAsync(
        IQueryable<Guid> readable,
        Guid[] wanted,
        CancellationToken cancellationToken)
    {
        if (wanted.Length == 0)
        {
            return [];
        }

        List<Guid> found = await readable
            .Where(x => wanted.Contains(x))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. found];
    }

    private static Guid[] Linked(ResearchCase researchCase, ResearchLinkKind kind) =>
        [.. researchCase.Links.Where(x => x.Kind == kind).Select(x => x.LinkedId)];

    private static Guid[] Linked(IReadOnlyList<ResearchCase> cases, ResearchLinkKind kind) =>
    [
        .. cases.SelectMany(x => x.Links)
            .Where(x => x.Kind == kind)
            .Select(x => x.LinkedId)
            .Distinct(),
    ];

    private static int Count(
        ResearchCase researchCase,
        ResearchLinkKind kind,
        HashSet<Guid> allowed) =>
        researchCase.Links.Count(x => x.Kind == kind && allowed.Contains(x.LinkedId));

    private IQueryable<Watchlist> NarrowWatchlists(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable)
    {
        IntelligenceSensitivity[] allowed = [.. readable];

        return _context.Watchlists
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Sensitivity));
    }

    private IQueryable<TalentRadarEntry> NarrowRadar(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable)
    {
        IntelligenceSensitivity[] allowed = [.. readable];

        return _context.TalentRadarEntries
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Sensitivity));
    }

    private IQueryable<ResearchCase> NarrowResearchCases(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable)
    {
        IntelligenceSensitivity[] allowed = [.. readable];

        return _context.ResearchCases
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Sensitivity));
    }

    private sealed record RadarPerson(
        PersonId Id,
        string DisplayName,
        string? Title,
        string? CompanyName);

    private sealed record RecordedAssessment(int? Strength, string? Notes);

    private sealed record LastInteraction(DateTimeOffset OccurredAt, InteractionType Type);

    private sealed record SubjectActivity(Guid SubjectId, DateTimeOffset RecordedAt);
}
