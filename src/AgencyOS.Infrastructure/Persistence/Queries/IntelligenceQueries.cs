using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Intelligence;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for intelligence.
/// </summary>
/// <remarks>
/// <para>
/// Two rules run through every method here.
/// </para>
/// <para>
/// <strong>Classification is applied in the SQL.</strong> Never to a materialized
/// list, because filtering afterwards produces correct rows and an incorrect count,
/// and "three hidden signals about this person" is itself the disclosure (§28,
/// ADR-0030).
/// </para>
/// <para>
/// <strong>A detail read fetches the object without narrowing it, and narrows only
/// what hangs off it.</strong> The object's own classification is refused by the
/// application with a 403 rather than hidden as a 404, so somebody who followed a
/// citation learns a grant exists to ask for. Its citations, evidence and links are
/// narrowed silently, because a citation naming a source-sensitive signal would
/// disclose that signal just as surely as a list would.
/// </para>
/// </remarks>
public sealed partial class IntelligenceQueries : IIntelligenceQueries
{
    /// <summary>How much curated history a detail read carries.</summary>
    private const int HistoryLimit = 200;

    /// <summary>How many related rows a detail read carries per kind.</summary>
    private const int RelatedLimit = 50;

    /// <summary>How many rows the command centre shows per panel.</summary>
    private const int PanelLimit = 20;

    private readonly AgencyOsDbContext _context;
    private readonly IIntelligenceSubjectValidator _subjects;

    public IntelligenceQueries(AgencyOsDbContext context, IIntelligenceSubjectValidator subjects)
    {
        _context = context;
        _subjects = subjects;
    }

    // ------------------------------------------------------------------ sources

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntelligenceSourceModel>> ListSourcesAsync(
        OrganizationId organizationId,
        IntelligenceScope<SourceFilter> scope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IQueryable<IntelligenceSource> query = NarrowSources(organizationId, scope.Readable);
        SourceFilter filter = scope.Filter;

        if (filter.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (filter.Reliability is { } reliability)
        {
            query = query.Where(x => x.Reliability == reliability);
        }

        if (filter.Sensitivity is { } sensitivity)
        {
            query = query.Where(x => x.Sensitivity == sensitivity);
        }

        if (filter.RecordedByUserId is { } recordedBy)
        {
            UserId user = new(recordedBy);
            query = query.Where(x => x.RecordedBy == user);
        }

        if (filter.ObservedAfter is { } after)
        {
            DateTimeOffset from = From(after);
            query = query.Where(x => x.ObservedAt >= from);
        }

        if (filter.ObservedBefore is { } before)
        {
            DateTimeOffset to = To(before);
            query = query.Where(x => x.ObservedAt <= to);
        }

        if (filter.TextContains is { Length: > 0 } text)
        {
            string pattern = Pattern(text);

            // Title, publisher, author and external reference. Never the notes: an
            // analyst's note on a source-sensitive source is the sensitive part.
            query = query.Where(x =>
                EF.Functions.ILike(x.Title, pattern)
                || (x.Publisher != null && EF.Functions.ILike(x.Publisher, pattern))
                || (x.Author != null && EF.Functions.ILike(x.Author, pattern))
                || (x.ExternalReference != null
                    && EF.Functions.ILike(x.ExternalReference, pattern)));
        }

        List<IntelligenceSource> rows = await query
            .OrderByDescending(x => x.ObservedAt)
            .ThenByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<IntelligenceSourceId, int> citations = await SourceCitationsAsync(
                organizationId, [.. rows.Select(x => x.Id)], scope.Readable, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(x => ToSource(x, users, citations.GetValueOrDefault(x.Id)))];
    }

    /// <inheritdoc />
    public async Task<IntelligenceSourceModel?> GetSourceAsync(
        OrganizationId organizationId,
        IntelligenceSourceId id,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken = default)
    {
        // Deliberately not narrowed by classification. The application refuses this
        // one with a 403 once it can see what it is; hiding it as a 404 would tell
        // the reader the agency never recorded the source at all.
        IntelligenceSource? source = await _context.IntelligenceSources
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (source is null)
        {
            return null;
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<IntelligenceSourceId, int> citations =
            await SourceCitationsAsync(organizationId, [id], readable, cancellationToken)
                .ConfigureAwait(false);

        return ToSource(source, users, citations.GetValueOrDefault(id));
    }

    // ------------------------------------------------------------------ signals

    /// <inheritdoc />
    public async Task<IReadOnlyList<SignalSummaryModel>> ListSignalsAsync(
        OrganizationId organizationId,
        IntelligenceScope<SignalFilter> scope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IQueryable<Signal> query = NarrowSignals(organizationId, scope.Readable);
        SignalFilter filter = scope.Filter;

        if (filter.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (filter.Verification is { } verification)
        {
            query = query.Where(x => x.Verification == verification);
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

        if (filter.SourceId is { } sourceId)
        {
            query = query.Where(x => x.Evidence.Any(e => e.SourceId == sourceId));
        }

        if (filter.RecordedByUserId is { } recordedBy)
        {
            UserId user = new(recordedBy);
            query = query.Where(x => x.RecordedBy == user);
        }

        if (filter.WatchlistId is { } watchlistId)
        {
            IQueryable<WatchlistEntry> members =
                Members(organizationId, new WatchlistId(watchlistId));

            query = query.Where(x => x.Subjects.Any(
                s => members.Any(m => m.Kind == s.Kind && m.SubjectId == s.SubjectId)));
        }

        if (filter.ObservedAfter is { } observedAfter)
        {
            DateTimeOffset from = From(observedAfter);
            query = query.Where(x => x.ObservedAt >= from);
        }

        if (filter.ObservedBefore is { } observedBefore)
        {
            DateTimeOffset to = To(observedBefore);
            query = query.Where(x => x.ObservedAt <= to);
        }

        if (filter.OccurredAfter is { } occurredAfter)
        {
            DateTimeOffset from = From(occurredAfter);
            query = query.Where(x => x.OccurredAt != null && x.OccurredAt >= from);
        }

        if (filter.OccurredBefore is { } occurredBefore)
        {
            DateTimeOffset to = To(occurredBefore);
            query = query.Where(x => x.OccurredAt != null && x.OccurredAt <= to);
        }

        if (filter.TextContains is { Length: > 0 } text)
        {
            string pattern = Pattern(text);

            // The title and the claim. Never an excerpt: an excerpt is a quotation
            // from an M10 artifact, and matching one would report the artifact's
            // contents to whoever ran the search (ADR-0025).
            query = query.Where(x =>
                EF.Functions.ILike(x.Title, pattern) || EF.Functions.ILike(x.Claim, pattern));
        }

        List<Signal> rows = await query
            .Include(x => x.Subjects)
            .OrderByDescending(x => x.ObservedAt)
            .ThenByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToSignalSummariesAsync(
                organizationId, rows, scope.Readable, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SignalDetailModel?> GetSignalAsync(
        OrganizationId organizationId,
        SignalId id,
        IReadOnlySet<IntelligenceSensitivity> readable,
        IReadOnlySet<DocumentSensitivity> readableDocuments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readableDocuments);

        Signal? signal = await _context.Signals
            .AsNoTracking()
            .Include(x => x.Subjects)
            .Include(x => x.Evidence)
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (signal is null)
        {
            return null;
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        SignalSummaryModel summary = (await ToSignalSummariesAsync(
                organizationId, [signal], readable, cancellationToken).ConfigureAwait(false))[0];

        IReadOnlyList<SignalEvidenceModel> evidence = await ToEvidenceAsync(
                organizationId, signal, readable, readableDocuments, users, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<IntelligenceEventModel> history = await HistoryAsync(
                organizationId, IntelligenceOwnerKind.Signal, id.Value, users, cancellationToken)
            .ConfigureAwait(false);

        return new SignalDetailModel(
            summary,
            signal.Notes,
            signal.VerificationNote,
            Name(users, signal.VerificationChangedBy),
            signal.VerificationChangedAt,
            evidence,
            history);
    }

    /// <summary>
    /// Projects a signal's citations, dropping the ones the caller may not follow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A citation of a source the caller cannot read is omitted entirely rather than
    /// shown as withheld. Its title would name the source, and naming it is the
    /// disclosure the classification exists to prevent (§28).
    /// </para>
    /// <para>
    /// What remains can still carry an excerpt the caller may not see: the source is
    /// readable as intelligence, but the M10 artifact it quotes is classified above
    /// them. That case is <em>visible</em> — <c>ExcerptWithheld</c> — because the
    /// citation itself was already legitimate, and a silently missing excerpt would
    /// read as an analyst who never took one (ADR-0025).
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<SignalEvidenceModel>> ToEvidenceAsync(
        OrganizationId organizationId,
        Signal signal,
        IReadOnlySet<IntelligenceSensitivity> readable,
        IReadOnlySet<DocumentSensitivity> readableDocuments,
        Dictionary<Guid, string> users,
        CancellationToken cancellationToken)
    {
        if (signal.Evidence.Count == 0)
        {
            return [];
        }

        IntelligenceSourceId[] cited = [.. signal.Evidence.Select(x => x.SourceId).Distinct()];
        IntelligenceSensitivity[] allowed = [.. readable];

        List<CitedSource> sources = await _context.IntelligenceSources
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && cited.Contains(x.Id)
                && allowed.Contains(x.Sensitivity))
            .Select(x => new CitedSource(x.Id, x.Title, x.Kind, x.Reliability, x.DocumentVersionId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<IntelligenceSourceId, CitedSource> byId =
            sources.ToDictionary(x => x.Id);

        // Which of the quoted document versions the caller may actually read. Asked
        // once for the whole signal rather than per citation.
        HashSet<Guid> readableVersions = await ReadableVersionsAsync(
                organizationId,
                [.. sources.Where(x => x.DocumentVersionId != null)
                    .Select(x => x.DocumentVersionId!.Value.Value)],
                readableDocuments,
                cancellationToken)
            .ConfigureAwait(false);

        List<SignalEvidenceModel> projected = [];

        foreach (SignalEvidence evidence in signal.Evidence.OrderBy(x => x.AddedAt))
        {
            if (!byId.TryGetValue(evidence.SourceId, out CitedSource? source))
            {
                continue;
            }

            bool withheld = source.DocumentVersionId is { } version
                && !readableVersions.Contains(version.Value);

            projected.Add(new SignalEvidenceModel(
                evidence.Id,
                evidence.SourceId,
                source.Title,
                source.Kind,
                source.Reliability,
                evidence.Role,
                withheld ? null : evidence.Excerpt,
                withheld,
                evidence.Locator,
                evidence.AddedAt,
                Name(users, evidence.AddedBy)));
        }

        return projected;
    }

    /// <summary>Turns signals into summaries, with subject names and citation counts.</summary>
    private async Task<IReadOnlyList<SignalSummaryModel>> ToSignalSummariesAsync(
        OrganizationId organizationId,
        IReadOnlyList<Signal> signals,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken)
    {
        if (signals.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> users =
            await UsersAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<(IntelligenceSubjectKind, Guid), string> labels = await LabelsAsync(
                organizationId, signals.SelectMany(x => x.Subjects), cancellationToken)
            .ConfigureAwait(false);

        SignalId[] ids = [.. signals.Select(x => x.Id)];
        IntelligenceSensitivity[] allowed = [.. readable];

        // Counted over readable sources only, so the count agrees with the citations
        // a detail read would actually show.
        Dictionary<SignalId, int> citations = await _context.SignalEvidence
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && ids.Contains(x.SignalId)
                && _context.IntelligenceSources.Any(
                    s => s.Id == x.SourceId && allowed.Contains(s.Sensitivity)))
            .GroupBy(x => x.SignalId)
            .Select(g => new Counted<SignalId>(g.Key, g.Count()))
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. signals.Select(x => new SignalSummaryModel(
                x.Id,
                x.Title,
                x.Claim,
                x.Kind,
                x.Verification,
                x.Confidence,
                x.Sensitivity,
                x.OccurredAt,
                x.ObservedAt,
                x.RecordedAt,
                Name(users, x.RecordedBy),
                Subjects(x.Subjects, labels),
                citations.GetValueOrDefault(x.Id),
                x.Version)),
        ];
    }

    private static IntelligenceSourceModel ToSource(
        IntelligenceSource source,
        Dictionary<Guid, string> users,
        int signalCount) =>
        new(
            source.Id,
            source.Kind,
            source.Title,
            source.DocumentVersionId?.Value,
            source.MessageId?.Value,
            source.Url,
            source.Publisher,
            source.Author,
            source.ExternalReference,
            source.PublishedAt,
            source.ObservedAt,
            source.RecordedAt,
            source.Reliability,
            source.ReliabilityRationale,
            Name(users, source.ReliabilityAssessedBy),
            source.ReliabilityAssessedAt,
            source.Sensitivity,
            source.Notes,
            source.IsHeldByAgencyOS,
            Name(users, source.RecordedBy),
            signalCount,
            source.Version);

    /// <summary>How many readable signals cite each of these sources.</summary>
    private async Task<Dictionary<IntelligenceSourceId, int>> SourceCitationsAsync(
        OrganizationId organizationId,
        IntelligenceSourceId[] sourceIds,
        IReadOnlySet<IntelligenceSensitivity> readable,
        CancellationToken cancellationToken)
    {
        if (sourceIds.Length == 0)
        {
            return [];
        }

        IntelligenceSensitivity[] allowed = [.. readable];

        return await _context.SignalEvidence
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && sourceIds.Contains(x.SourceId)
                && _context.Signals.Any(
                    s => s.Id == x.SignalId && allowed.Contains(s.Sensitivity)))
            .GroupBy(x => x.SourceId)
            .Select(g => new Counted<IntelligenceSourceId>(g.Key, g.Count()))
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Which of these document versions the caller may read.</summary>
    private async Task<HashSet<Guid>> ReadableVersionsAsync(
        OrganizationId organizationId,
        Guid[] versionIds,
        IReadOnlySet<DocumentSensitivity> readableDocuments,
        CancellationToken cancellationToken)
    {
        if (versionIds.Length == 0)
        {
            return [];
        }

        DocumentVersionId[] wanted = [.. versionIds.Select(x => new DocumentVersionId(x))];
        DocumentSensitivity[] allowed = [.. readableDocuments];

        List<Guid> found = await _context.DocumentVersions
            .AsNoTracking()
            .Where(v => v.OrganizationId == organizationId
                && wanted.Contains(v.Id)
                && _context.Documents.Any(
                    d => d.Id == v.DocumentId && allowed.Contains(d.Sensitivity)))
            .Select(v => v.Id.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. found];
    }

    // ------------------------------------------------------------------ helpers

    private IQueryable<IntelligenceSource> NarrowSources(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable)
    {
        IntelligenceSensitivity[] allowed = [.. readable];

        return _context.IntelligenceSources
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Sensitivity));
    }

    private IQueryable<Signal> NarrowSignals(
        OrganizationId organizationId,
        IReadOnlySet<IntelligenceSensitivity> readable)
    {
        IntelligenceSensitivity[] allowed = [.. readable];

        return _context.Signals
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && allowed.Contains(x.Sensitivity));
    }

    /// <summary>The subjects one watchlist watches.</summary>
    private IQueryable<WatchlistEntry> Members(
        OrganizationId organizationId,
        WatchlistId watchlistId) =>
        _context.IntelligenceSubjects
            .AsNoTracking()
            .OfType<WatchlistEntry>()
            .Where(x => x.OrganizationId == organizationId && x.WatchlistId == watchlistId);

    private async Task<IReadOnlyDictionary<(IntelligenceSubjectKind, Guid), string>> LabelsAsync(
        OrganizationId organizationId,
        IEnumerable<IntelligenceSubject> subjects,
        CancellationToken cancellationToken) =>
        await _subjects
            .DescribeAsync(
                organizationId,
                [.. subjects.Select(x => (x.Kind, x.SubjectId)).Distinct()],
                cancellationToken)
            .ConfigureAwait(false);

    private static IReadOnlyList<IntelligenceSubjectModel> Subjects(
        IEnumerable<IntelligenceSubject> subjects,
        IReadOnlyDictionary<(IntelligenceSubjectKind, Guid), string> labels) =>
        [
            .. subjects
                .OrderBy(x => x.AddedAt)
                .Select(x => new IntelligenceSubjectModel(
                    x.Id,
                    x.Kind,
                    x.SubjectId,
                    labels.TryGetValue((x.Kind, x.SubjectId), out string? label)
                        ? label
                        : x.Kind.ToString(),
                    x.Note)),
        ];

    /// <summary>The curated history of one intelligence object.</summary>
    private async Task<IReadOnlyList<IntelligenceEventModel>> HistoryAsync(
        OrganizationId organizationId,
        IntelligenceOwnerKind ownerKind,
        Guid ownerId,
        Dictionary<Guid, string> users,
        CancellationToken cancellationToken)
    {
        List<IntelligenceEvent> rows = await _context.IntelligenceEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.OwnerKind == ownerKind
                && x.OwnerId == ownerId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(HistoryLimit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(x => new IntelligenceEventModel(
                x.OccurredAt, x.Kind, x.Summary, x.Detail, Name(users, x.ActorUserId))),
        ];
    }

    private async Task<Dictionary<Guid, string>> UsersAsync(CancellationToken cancellationToken) =>
        await _context.Users
            .AsNoTracking()
            .Select(x => new { x.Id, x.DisplayName })
            .ToDictionaryAsync(x => x.Id.Value, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

    private static string? Name(Dictionary<Guid, string> users, UserId id) =>
        users.GetValueOrDefault(id.Value);

    private static string? Name(Dictionary<Guid, string> users, UserId? id) =>
        id is { } value ? users.GetValueOrDefault(value.Value) : null;

    /// <summary>Midnight at the start of the day, in UTC.</summary>
    private static DateTimeOffset From(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>The last instant of the day, in UTC.</summary>
    private static DateTimeOffset To(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

    /// <summary>Escapes a user's text so it matches literally inside an ILIKE.</summary>
    /// <remarks>
    /// Without this a search for "50%" matches everything, and a search for "_"
    /// matches every single character. Neither is what the person typed.
    /// </remarks>
    private static string Pattern(string text) =>
        "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary>One counted group. A named type, because EF cannot project a tuple.</summary>
    private sealed record Counted<TKey>(TKey Key, int Count);

    /// <summary>The parts of a source a citation needs.</summary>
    private sealed record CitedSource(
        IntelligenceSourceId Id,
        string Title,
        IntelligenceSourceKind Kind,
        SourceReliability Reliability,
        DocumentVersionId? DocumentVersionId);
}
