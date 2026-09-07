using AgencyOS.Application.Directory;
using AgencyOS.Application.Opportunities;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;
using static AgencyOS.Infrastructure.Persistence.Queries.PeopleSliceProjection;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for the pursuit model.
/// </summary>
/// <remarks>
/// <para>
/// Everything about the state of the pipeline is <em>derived</em> here rather than
/// stored: submission counts, last activity, whether a reply is overdue. There is
/// no <c>HasSubmission</c> column to disagree with the submission rows, and no row
/// is ever written to represent silence - "awaiting a response" is the absence of
/// anything recorded after an expected date, which is exactly what it means in
/// life (ADR-0020).
/// </para>
/// <para>
/// Names resolve by loading the tenant's people, companies and users once per
/// request, as the directory, representation and project projections do.
/// Deliberately unauthorized: <c>OpportunityQueryService</c> applies the
/// tenant-scoped checks and the strategy redaction.
/// </para>
/// </remarks>
internal sealed class OpportunityQueries : IOpportunityQueries
{
    private readonly AgencyOsDbContext _context;

    public OpportunityQueries(AgencyOsDbContext context) => _context = context;

    public async Task<IReadOnlyList<OpportunitySummaryModel>> ListOpportunitiesAsync(
        OrganizationId organizationId,
        OpportunityFilter filter,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Opportunity> query = _context.Opportunities
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (filter.OwnerUserId is { } owner)
        {
            Domain.Identity.UserId key = new(owner);

            query = query.Where(x => x.OwnerUserId == key);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string pattern = $"%{filter.Search.Trim()}%";

            // Deliberately not strategy_notes. A caller without the grant must not
            // be able to confirm what a note says by searching for a phrase.
            query = query.Where(x =>
                EF.Functions.ILike(x.Name, pattern)
                || (x.Description != null && EF.Functions.ILike(x.Description, pattern)));
        }

        query = ApplySubjectFilters(query, organizationId, filter);
        query = ApplyTargetFilters(query, organizationId, filter, now);

        List<Opportunity> opportunities = await query
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToSummariesAsync(organizationId, opportunities, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityDetailModel?> GetOpportunityAsync(
        OrganizationId organizationId,
        OpportunityId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        Opportunity? opportunity = await _context.Opportunities
            .AsNoTracking()
            .Include(x => x.Subjects)
            .FirstOrDefaultAsync(
                x => x.Id == id && x.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (opportunity is null)
        {
            return null;
        }

        OpportunityNames names = await LoadNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        OpportunitySummaryModel summary = (await ToSummariesAsync(
            organizationId, [opportunity], now, cancellationToken).ConfigureAwait(false))[0];

        IReadOnlyList<OpportunityTargetModel> targets = await ListTargetsAsync(
            organizationId, id, now, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SubmissionModel> submissions = await ListSubmissionsAsync(
            organizationId, id, null, now, 200, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PitchModel> pitches = await ListPitchesAsync(
            organizationId, id, null, 200, cancellationToken).ConfigureAwait(false);

        List<TaskModel> openTasks = await LoadOpenTasksAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);

        List<OpportunitySubjectModel> subjects =
            await ToSubjectModelsAsync(organizationId, opportunity, names, cancellationToken)
                .ConfigureAwait(false);

        return new OpportunityDetailModel(
            summary,
            opportunity.Description,
            opportunity.StrategyNotes,
            subjects,
            targets,
            submissions,
            pitches,
            openTasks,
            opportunity.CreatedAt);
    }

    public async Task<IReadOnlyList<OpportunityTargetModel>> ListTargetsAsync(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        List<OpportunityTarget> targets = await _context.OpportunityTargets
            .AsNoTracking()
            .Where(x => x.OpportunityId == opportunityId && x.OrganizationId == organizationId)
            .OrderBy(x => x.Stage)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToTargetModelsAsync(organizationId, targets, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OpportunityTargetModel?> GetTargetAsync(
        OrganizationId organizationId,
        OpportunityTargetId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        OpportunityTarget? target = await _context.OpportunityTargets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id && x.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (target is null)
        {
            return null;
        }

        IReadOnlyList<OpportunityTargetModel> models = await ToTargetModelsAsync(
            organizationId, [target], now, cancellationToken).ConfigureAwait(false);

        return models[0];
    }

    public async Task<IReadOnlyList<SubmissionModel>> ListSubmissionsAsync(
        OrganizationId organizationId,
        OpportunityId? opportunityId,
        OpportunityTargetId? targetId,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Submission> query = _context.Submissions
            .AsNoTracking()
            .Include(x => x.Materials)
            .Where(x => x.OrganizationId == organizationId);

        if (opportunityId is { } opportunity)
        {
            query = query.Where(x => x.OpportunityId == opportunity);
        }

        if (targetId is { } target)
        {
            query = query.Where(x => x.OpportunityTargetId == target);
        }

        List<Submission> submissions = await query
            .OrderByDescending(x => x.SentAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToSubmissionModelsAsync(organizationId, submissions, now, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SubmissionModel?> GetSubmissionAsync(
        OrganizationId organizationId,
        SubmissionId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        Submission? submission = await _context.Submissions
            .AsNoTracking()
            .Include(x => x.Materials)
            .FirstOrDefaultAsync(
                x => x.Id == id && x.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (submission is null)
        {
            return null;
        }

        IReadOnlyList<SubmissionModel> models = await ToSubmissionModelsAsync(
            organizationId, [submission], now, cancellationToken).ConfigureAwait(false);

        return models[0];
    }

    public async Task<IReadOnlyList<PitchModel>> ListPitchesAsync(
        OrganizationId organizationId,
        OpportunityId? opportunityId,
        OpportunityTargetId? targetId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<OpportunityPitch> query = _context.OpportunityPitches
            .AsNoTracking()
            .Include(x => x.Materials)
            .Where(x => x.OrganizationId == organizationId);

        if (opportunityId is { } opportunity)
        {
            query = query.Where(x => x.OpportunityId == opportunity);
        }

        if (targetId is { } target)
        {
            query = query.Where(x => x.OpportunityTargetId == target);
        }

        List<OpportunityPitch> pitches = await query
            .OrderByDescending(x => x.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pitches.Count == 0)
        {
            return [];
        }

        OpportunityNames names = await LoadNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> targetNames = await LoadTargetNamesAsync(
            organizationId, names, cancellationToken).ConfigureAwait(false);

        List<Domain.Interactions.InteractionId> interactionKeys =
            [.. pitches.Select(x => x.InteractionId)];

        Dictionary<Guid, List<string>> participants = await LoadParticipantNamesAsync(
            organizationId, interactionKeys, names, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> materialTitles = await LoadMaterialTitlesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        return
        [
            .. pitches.Select(x => new PitchModel(
                x.Id.Value,
                x.OpportunityId.Value,
                x.OpportunityTargetId.Value,
                targetNames.TryGetValue(x.OpportunityTargetId.Value, out string? name)
                    ? name
                    : "(unknown)",
                x.InteractionId.Value,
                x.Kind.ToString(),
                x.Outcome.ToString(),
                x.Subject,
                x.Notes,
                x.OccurredAt,
                participants.TryGetValue(x.InteractionId.Value, out List<string>? who) ? who : [],
                [
                    .. x.Materials
                        .OrderBy(m => m.Position)
                        .Select(m => new SubmissionMaterialModel(
                            m.MaterialId.Value,
                            m.TitleAtPitch,
                            m.TypeAtPitch.ToString(),
                            m.VersionLabelAtPitch,
                            materialTitles.TryGetValue(m.MaterialId.Value, out string? current)
                                ? current
                                : null,
                            null)),
                ],
                x.Version)),
        ];
    }

    /// <summary>
    /// The curated pursuit timeline.
    /// </summary>
    /// <remarks>
    /// A pitch appears once. It has an interaction and pitch metadata, and showing
    /// both would put the same meeting on the page twice under two headings, which
    /// is how a timeline stops being readable.
    /// </remarks>
    public async Task<IReadOnlyList<OpportunityHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        OpportunityId id,
        int limit,
        CancellationToken cancellationToken = default)
    {
        bool exists = await _context.Opportunities
            .AnyAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return [];
        }

        OpportunityNames names = await LoadNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> targetNames = await LoadTargetNamesAsync(
            organizationId, names, cancellationToken).ConfigureAwait(false);

        List<OpportunityHistoryEntryModel> entries = [];

        List<OpportunityEvent> opportunityEvents = await _context.OpportunityEvents
            .AsNoTracking()
            .Where(x => x.OpportunityId == id && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (OpportunityEvent entry in opportunityEvents)
        {
            entries.Add(new OpportunityHistoryEntryModel(
                entry.RecordedAt,
                entry.Kind.ToString(),
                entry.Kind == OpportunityChangeKind.Created
                    ? "Opportunity opened"
                    : $"Moved from {entry.FromStatus} to {entry.ToStatus}",
                entry.Reason,
                null,
                names.User(entry.RecordedBy.Value)));
        }

        List<OpportunityTargetId> targetKeys = await _context.OpportunityTargets
            .AsNoTracking()
            .Where(x => x.OpportunityId == id && x.OrganizationId == organizationId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (targetKeys.Count > 0)
        {
            List<OpportunityTargetEvent> targetEvents = await _context.OpportunityTargetEvents
                .AsNoTracking()
                .Where(x => targetKeys.Contains(x.OpportunityTargetId)
                    && x.OrganizationId == organizationId)
                .OrderByDescending(x => x.OccurredAt)
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (OpportunityTargetEvent entry in targetEvents)
            {
                string summary = entry.Kind == OpportunityTargetEventKind.StageChanged
                    ? entry.FromStage is null
                        ? $"Added as a target at {entry.ToStage}"
                        : $"Moved from {entry.FromStage} to {entry.ToStage}"
                    : Humanize(entry.Kind);

                entries.Add(new OpportunityHistoryEntryModel(
                    entry.OccurredAt,
                    entry.Kind == OpportunityTargetEventKind.StageChanged ? "TargetStage" : "TargetEvent",
                    summary,
                    entry.Note,
                    targetNames.TryGetValue(entry.OpportunityTargetId.Value, out string? who)
                        ? who
                        : null,
                    names.User(entry.RecordedBy.Value)));
            }
        }

        List<Submission> submissions = await _context.Submissions
            .AsNoTracking()
            .Where(x => x.OpportunityId == id && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.SentAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Submission submission in submissions)
        {
            entries.Add(new OpportunityHistoryEntryModel(
                submission.SentAt,
                "Submission",
                $"Submitted by {submission.Channel}",
                submission.Subject,
                targetNames.TryGetValue(submission.OpportunityTargetId.Value, out string? who)
                    ? who
                    : null,
                names.User(submission.SentByUserId.Value)));
        }

        List<OpportunityPitch> pitches = await _context.OpportunityPitches
            .AsNoTracking()
            .Where(x => x.OpportunityId == id && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (OpportunityPitch pitch in pitches)
        {
            entries.Add(new OpportunityHistoryEntryModel(
                pitch.OccurredAt,
                "Pitch",
                $"{pitch.Kind} pitch: {pitch.Outcome}",
                pitch.Subject ?? pitch.Notes,
                targetNames.TryGetValue(pitch.OpportunityTargetId.Value, out string? who)
                    ? who
                    : null,
                names.User(pitch.CreatedBy.Value)));
        }

        return [.. entries.OrderByDescending(x => x.OccurredAt).Take(limit)];
    }

    public async Task<IReadOnlyList<PipelineColumnModel>> GetPipelineAsync(
        OrganizationId organizationId,
        Guid? ownerUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Opportunity> opportunities = _context.Opportunities
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Status == OpportunityStatus.Active);

        if (ownerUserId is { } owner)
        {
            Domain.Identity.UserId key = new(owner);

            opportunities = opportunities.Where(x => x.OwnerUserId == key);
        }

        Dictionary<Guid, string> names = await opportunities
            .Select(x => new { Id = x.Id.Value, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            .ConfigureAwait(false);

        if (names.Count == 0)
        {
            return [];
        }

        List<OpportunityId> keys = [.. names.Keys.Select(x => new OpportunityId(x))];

        List<OpportunityTarget> targets = await _context.OpportunityTargets
            .AsNoTracking()
            .Where(x => keys.Contains(x.OpportunityId) && x.OrganizationId == organizationId)
            .OrderBy(x => x.NextActionOn ?? DateOnly.MaxValue)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<OpportunityTargetModel> models = await ToTargetModelsAsync(
            organizationId, targets, now, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, OpportunityTarget> byId = targets.ToDictionary(x => x.Id.Value, x => x);

        List<PipelineColumnModel> columns = [];

        // Only the stages a target can still move from. A column of things that
        // finished is a report, not a pipeline.
        foreach (OpportunityTargetStage stage in OpportunityTarget.OpenStages.OrderBy(x => (int)x))
        {
            List<PipelineEntryModel> entries =
            [
                .. models
                    .Where(x => x.Stage == stage.ToString())
                    .Select(x => new PipelineEntryModel(
                        byId[x.Id].OpportunityId.Value,
                        names.TryGetValue(byId[x.Id].OpportunityId.Value, out string? name)
                            ? name
                            : "(unknown)",
                        x)),
            ];

            columns.Add(new PipelineColumnModel(stage.ToString(), entries));
        }

        return columns;
    }

    public async Task<OpportunityCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        Dictionary<Guid, string> activeNames = await _context.Opportunities
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Status == OpportunityStatus.Active)
            .Select(x => new { Id = x.Id.Value, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            .ConfigureAwait(false);

        List<OpportunityId> activeKeys = [.. activeNames.Keys.Select(x => new OpportunityId(x))];

        List<OpportunityTarget> targets = activeKeys.Count == 0
            ? []
            : await _context.OpportunityTargets
                .AsNoTracking()
                .Where(x => activeKeys.Contains(x.OpportunityId) && x.OrganizationId == organizationId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<OpportunityTargetModel> models = await ToTargetModelsAsync(
            organizationId, targets, now, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, OpportunityTarget> byId = targets.ToDictionary(x => x.Id.Value, x => x);

        PipelineEntryModel Entry(OpportunityTargetModel model) => new(
            byId[model.Id].OpportunityId.Value,
            activeNames.TryGetValue(byId[model.Id].OpportunityId.Value, out string? name)
                ? name
                : "(unknown)",
            model);

        // Past due, by a date somebody set. Nothing here is scored or predicted.
        List<PipelineEntryModel> overdue =
        [
            .. models
                .Where(x => x.IsOpen && x.NextActionOn is { } due && due < today)
                .OrderBy(x => x.NextActionOn)
                .Take(20)
                .Select(Entry),
        ];

        DateTimeOffset recent = now.AddDays(-14);

        List<PipelineEntryModel> interested =
        [
            .. models
                .Where(x => x.Stage == nameof(OpportunityTargetStage.Interested)
                    && x.LastActivityAt is { } at && at >= recent)
                .OrderByDescending(x => x.LastActivityAt)
                .Take(10)
                .Select(Entry),
        ];

        List<PipelineEntryModel> passed =
        [
            .. models
                .Where(x => x.Stage == nameof(OpportunityTargetStage.Passed)
                    && x.LastActivityAt is { } at && at >= recent)
                .OrderByDescending(x => x.LastActivityAt)
                .Take(10)
                .Select(Entry),
        ];

        IReadOnlyList<SubmissionModel> submissions = await ListSubmissionsAsync(
            organizationId, null, null, now, 200, cancellationToken).ConfigureAwait(false);

        List<SubmissionModel> awaiting =
        [
            .. submissions
                .Where(x => x.IsAwaitingResponse)
                .OrderBy(x => x.ResponseExpectedBy)
                .Take(20),
        ];

        return new OpportunityCommandCenterModel(
            overdue,
            awaiting,
            interested,
            passed,
            activeNames.Count,
            models.Count(x => x.IsOpen));
    }

    // --------------------------------------------------------------- plumbing

    private IQueryable<Opportunity> ApplySubjectFilters(
        IQueryable<Opportunity> query,
        OrganizationId organizationId,
        OpportunityFilter filter)
    {
        if (filter.TalentProfileId is { } talent)
        {
            Domain.Talent.TalentProfileId key = new(talent);

            query = query.Where(x => _context.OpportunitySubjects.Any(
                s => s.OpportunityId == x.Id && s.TalentProfileId == key));
        }

        if (filter.ProjectId is { } project)
        {
            Domain.Projects.ProjectId key = new(project);

            query = query.Where(x => _context.OpportunitySubjects.Any(
                s => s.OpportunityId == x.Id && s.ProjectId == key));
        }

        if (filter.PackageId is { } package)
        {
            Domain.Projects.PackageId key = new(package);

            query = query.Where(x => _context.OpportunitySubjects.Any(
                s => s.OpportunityId == x.Id && s.PackageId == key));
        }

        return query;
    }

    private IQueryable<Opportunity> ApplyTargetFilters(
        IQueryable<Opportunity> query,
        OrganizationId organizationId,
        OpportunityFilter filter,
        DateTimeOffset now)
    {
        if (filter.TargetCompanyId is { } company)
        {
            Domain.Companies.CompanyId key = new(company);

            query = query.Where(x => _context.OpportunityTargets.Any(
                t => t.OpportunityId == x.Id && t.CompanyId == key));
        }

        if (filter.TargetPersonId is { } person)
        {
            Domain.People.PersonId key = new(person);

            query = query.Where(x => _context.OpportunityTargets.Any(
                t => t.OpportunityId == x.Id && t.PersonId == key));
        }

        if (filter.TargetStage is { } stage)
        {
            query = query.Where(x => _context.OpportunityTargets.Any(
                t => t.OpportunityId == x.Id && t.Stage == stage));
        }

        if (filter.FollowUpDueOnOrBefore is { } due)
        {
            query = query.Where(x => _context.OpportunityTargets.Any(
                t => t.OpportunityId == x.Id
                    && t.NextActionOn != null
                    && t.NextActionOn <= due));
        }

        // Derived from the submission rows themselves, never from a flag on the
        // opportunity that could disagree with them.
        if (filter.HasSubmission)
        {
            query = query.Where(x => _context.Submissions.Any(s => s.OpportunityId == x.Id));
        }

        if (filter.AwaitingResponse)
        {
            DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

            query = query.Where(x => _context.Submissions.Any(
                s => s.OpportunityId == x.Id
                    && s.ResponseExpectedBy != null
                    && s.ResponseExpectedBy < today
                    && !_context.OpportunityTargetEvents.Any(
                        e => e.SubmissionId == s.Id && e.OccurredAt > s.SentAt)));
        }

        return query;
    }

    private async Task<IReadOnlyList<OpportunitySummaryModel>> ToSummariesAsync(
        OrganizationId organizationId,
        List<Opportunity> opportunities,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (opportunities.Count == 0)
        {
            return [];
        }

        OpportunityNames names = await LoadNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        List<OpportunityId> keys = [.. opportunities.Select(x => x.Id)];

        var targetStats = await _context.OpportunityTargets
            .AsNoTracking()
            .Where(x => keys.Contains(x.OpportunityId))
            .GroupBy(x => x.OpportunityId)
            .Select(g => new
            {
                OpportunityId = g.Key.Value,
                Total = g.Count(),
                Open = g.Count(x => x.Stage != OpportunityTargetStage.Passed
                    && x.Stage != OpportunityTargetStage.Withdrawn
                    && x.Stage != OpportunityTargetStage.Exhausted),
                NextAction = g.Min(x => x.NextActionOn),
                LastTouched = g.Max(x => x.UpdatedAt),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, (int Total, int Open, DateOnly? NextAction, DateTimeOffset? LastTouched)>
            stats = targetStats.ToDictionary(
                x => x.OpportunityId,
                x => (x.Total, x.Open, x.NextAction, (DateTimeOffset?)x.LastTouched));

        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        var submissionStats = await _context.Submissions
            .AsNoTracking()
            .Where(x => keys.Contains(x.OpportunityId))
            .GroupBy(x => x.OpportunityId)
            .Select(g => new
            {
                OpportunityId = g.Key.Value,
                Count = g.Count(),
                LastSent = g.Max(x => x.SentAt),
                Awaiting = g.Count(x => x.ResponseExpectedBy != null
                    && x.ResponseExpectedBy < today
                    && !_context.OpportunityTargetEvents.Any(
                        e => e.SubmissionId == x.Id && e.OccurredAt > x.SentAt)),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, (int Count, DateTimeOffset LastSent, int Awaiting)> submissions =
            submissionStats.ToDictionary(
                x => x.OpportunityId, x => (x.Count, x.LastSent, x.Awaiting));

        Dictionary<Guid, List<OpportunitySubjectModel>> subjectsById = [];

        foreach (Opportunity opportunity in opportunities)
        {
            subjectsById[opportunity.Id.Value] =
                await ToSubjectModelsAsync(organizationId, opportunity, names, cancellationToken)
                    .ConfigureAwait(false);
        }

        return
        [
            .. opportunities.Select(x =>
            {
                stats.TryGetValue(x.Id.Value, out var counts);
                submissions.TryGetValue(x.Id.Value, out var sent);

                List<OpportunitySubjectModel> subjects = subjectsById[x.Id.Value];

                OpportunitySubjectModel? primary = x.PrimarySubject is { } chosen
                    ? subjects.FirstOrDefault(s => s.Id == chosen.Id)
                    : subjects.FirstOrDefault();

                DateTimeOffset? lastActivity = Latest(
                    counts.LastTouched,
                    sent.Count > 0 ? sent.LastSent : null,
                    x.UpdatedAt);

                return new OpportunitySummaryModel(
                    x.Id.Value,
                    x.Name,
                    x.Kind.ToString(),
                    x.Status.ToString(),
                    x.Priority.ToString(),
                    x.OwnerUserId.Value,
                    names.User(x.OwnerUserId.Value),
                    x.OpenedOn,
                    x.ClosedOn,
                    x.Outcome?.ToString(),
                    primary,
                    counts.Total,
                    counts.Open,
                    sent.Count,
                    sent.Awaiting,
                    counts.NextAction,
                    lastActivity,
                    x.UpdatedAt,
                    x.Version);
            }),
        ];
    }

    private async Task<List<OpportunitySubjectModel>> ToSubjectModelsAsync(
        OrganizationId organizationId,
        Opportunity opportunity,
        OpportunityNames names,
        CancellationToken cancellationToken)
    {
        if (opportunity.Subjects.Count == 0)
        {
            return [];
        }

        Dictionary<Guid, string> talent = await _context.TalentProfiles
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.People.AsNoTracking(),
                profile => profile.PersonId,
                person => person.Id,
                (profile, person) => new { Id = profile.Id.Value, person.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> projects = await _context.Projects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> packages = await _context.Packages
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            .ConfigureAwait(false);

        var roles = await _context.ProjectRoles
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Type, x.Label })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, (string Type, string? Label)> roleById =
            roles.ToDictionary(x => x.Id, x => (x.Type.ToString(), x.Label));

        return
        [
            .. opportunity.Subjects
                .OrderBy(x => x.Role)
                .ThenBy(x => x.AddedAt)
                .Select(subject =>
                {
                    (string display, string? detail) = subject.Kind switch
                    {
                        OpportunitySubjectKind.TalentProfile =>
                            (talent.TryGetValue(subject.TargetId, out string? t) ? t : "(unknown)",
                                (string?)"Client"),

                        OpportunitySubjectKind.Project =>
                            (projects.TryGetValue(subject.TargetId, out string? p) ? p : "(unknown)",
                                "Project"),

                        OpportunitySubjectKind.Package =>
                            (packages.TryGetValue(subject.TargetId, out string? k) ? k : "(unknown)",
                                "Package"),

                        _ => roleById.TryGetValue(subject.TargetId, out var role)
                            ? (role.Label is { Length: > 0 }
                                ? $"{role.Type} ({role.Label})"
                                : role.Type, "Role")
                            : ("(unknown)", "Role"),
                    };

                    return new OpportunitySubjectModel(
                        subject.Id,
                        subject.Kind.ToString(),
                        subject.Role.ToString(),
                        subject.TargetId,
                        display,
                        detail,
                        subject.Note);
                }),
        ];
    }

    private async Task<IReadOnlyList<OpportunityTargetModel>> ToTargetModelsAsync(
        OrganizationId organizationId,
        List<OpportunityTarget> targets,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        OpportunityNames names = await LoadNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        List<OpportunityTargetId> keys = [.. targets.Select(x => x.Id)];

        var submissionStats = await _context.Submissions
            .AsNoTracking()
            .Where(x => keys.Contains(x.OpportunityTargetId))
            .GroupBy(x => x.OpportunityTargetId)
            .Select(g => new
            {
                TargetId = g.Key.Value,
                Count = g.Count(),
                LastSent = g.Max(x => x.SentAt),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pitchStats = await _context.OpportunityPitches
            .AsNoTracking()
            .Where(x => keys.Contains(x.OpportunityTargetId))
            .GroupBy(x => x.OpportunityTargetId)
            .Select(g => new
            {
                TargetId = g.Key.Value,
                Count = g.Count(),
                LastPitched = g.Max(x => x.OccurredAt),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var eventStats = await _context.OpportunityTargetEvents
            .AsNoTracking()
            .Where(x => keys.Contains(x.OpportunityTargetId))
            .GroupBy(x => x.OpportunityTargetId)
            .Select(g => new { TargetId = g.Key.Value, Last = g.Max(x => x.OccurredAt) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Awaiting a reply is the absence of anything after an expected date. It is
        // computed, never stored: silence produces no row anywhere in the model.
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        var awaiting = await _context.Submissions
            .AsNoTracking()
            .Where(x => keys.Contains(x.OpportunityTargetId)
                && x.ResponseExpectedBy != null
                && x.ResponseExpectedBy < today
                && !_context.OpportunityTargetEvents.Any(
                    e => e.SubmissionId == x.Id && e.OccurredAt > x.SentAt))
            .GroupBy(x => x.OpportunityTargetId)
            .Select(g => new { TargetId = g.Key.Value, Since = g.Min(x => x.ResponseExpectedBy) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, (int Count, DateTimeOffset Last)> submissions =
            submissionStats.ToDictionary(x => x.TargetId, x => (x.Count, x.LastSent));

        Dictionary<Guid, (int Count, DateTimeOffset Last)> pitches =
            pitchStats.ToDictionary(x => x.TargetId, x => (x.Count, x.LastPitched));

        Dictionary<Guid, DateTimeOffset> lastEvent =
            eventStats.ToDictionary(x => x.TargetId, x => x.Last);

        Dictionary<Guid, DateOnly?> awaitingSince =
            awaiting.ToDictionary(x => x.TargetId, x => x.Since);

        return
        [
            .. targets.Select(x =>
            {
                submissions.TryGetValue(x.Id.Value, out var sent);
                pitches.TryGetValue(x.Id.Value, out var pitched);
                lastEvent.TryGetValue(x.Id.Value, out DateTimeOffset lastActivity);

                return new OpportunityTargetModel(
                    x.Id.Value,
                    x.CompanyId?.Value,
                    x.PersonId?.Value,
                    names.Party(x.PersonId?.Value, x.CompanyId?.Value),
                    x.ContactPersonId?.Value,
                    x.ContactPersonId is { } contact ? names.Person(contact.Value) : null,
                    x.Stage.ToString(),
                    x.IsOpen,
                    x.OwnerUserId?.Value,
                    x.OwnerUserId is { } owner ? names.User(owner.Value) : null,
                    x.NextActionOn,
                    x.ClosedOn,
                    x.Notes,
                    sent.Count,
                    sent.Count > 0 ? sent.Last : null,
                    pitched.Count,
                    pitched.Count > 0 ? pitched.Last : null,
                    Latest(
                        lastActivity == default ? null : lastActivity,
                        sent.Count > 0 ? sent.Last : null,
                        pitched.Count > 0 ? pitched.Last : null),
                    awaitingSince.TryGetValue(x.Id.Value, out DateOnly? since) ? since : null,
                    x.UpdatedAt,
                    x.Version);
            }),
        ];
    }

    private async Task<IReadOnlyList<SubmissionModel>> ToSubmissionModelsAsync(
        OrganizationId organizationId,
        List<Submission> submissions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (submissions.Count == 0)
        {
            return [];
        }

        OpportunityNames names = await LoadNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> targetNames = await LoadTargetNamesAsync(
            organizationId, names, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> materialTitles = await LoadMaterialTitlesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        // Typed as nullable to match the column. Comparing a non-nullable list
        // against a nullable value-converted key does not translate, and fails at
        // runtime rather than at compile time.
        List<SubmissionId?> keys = [.. submissions.Select(x => (SubmissionId?)x.Id)];

        var responses = await _context.OpportunityTargetEvents
            .AsNoTracking()
            .Where(x => keys.Contains(x.SubmissionId))
            .GroupBy(x => x.SubmissionId)
            .Select(g => new { Key = g.Key, Last = g.Max(x => x.OccurredAt) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, DateTimeOffset> lastResponse = responses
            .Where(x => x.Key is not null)
            .ToDictionary(x => x.Key!.Value.Value, x => x.Last);

        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        return
        [
            .. submissions.Select(x =>
            {
                bool hasResponse = lastResponse.TryGetValue(x.Id.Value, out DateTimeOffset last)
                    && last > x.SentAt;

                return new SubmissionModel(
                    x.Id.Value,
                    x.OpportunityId.Value,
                    x.OpportunityTargetId.Value,
                    targetNames.TryGetValue(x.OpportunityTargetId.Value, out string? who)
                        ? who
                        : "(unknown)",
                    x.SentAt,
                    x.SentByUserId.Value,
                    names.User(x.SentByUserId.Value),
                    x.Channel.ToString(),
                    x.Subject,
                    x.Notes,
                    x.ResponseExpectedBy,
                    x.ExternalReference,
                    [
                        .. x.Materials
                            .OrderBy(m => m.Position)
                            .Select(m => new SubmissionMaterialModel(
                                m.MaterialId.Value,
                                m.TitleAtSubmission,
                                m.TypeAtSubmission.ToString(),
                                m.VersionLabelAtSubmission,
                                materialTitles.TryGetValue(m.MaterialId.Value, out string? current)
                                    ? current
                                    : null,
                                m.Note)),
                    ],
                    hasResponse ? last : null,
                    x.ResponseExpectedBy is { } expected && expected < today && !hasResponse,
                    x.Version);
            }),
        ];
    }

    private async Task<List<TaskModel>> LoadOpenTasksAsync(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        CancellationToken cancellationToken)
    {
        List<TaskItemId> keys = await _context.OpportunityTaskLinks
            .AsNoTracking()
            .Where(x => x.OpportunityId == opportunityId && x.OrganizationId == organizationId)
            .Select(x => x.TaskItemId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (keys.Count == 0)
        {
            return [];
        }

        List<TaskItem> tasks = await _context.Tasks
            .AsNoTracking()
            .Where(x => keys.Contains(x.Id)
                && x.OrganizationId == organizationId
                && x.State == TaskState.Open)
            .OrderBy(x => x.DueAt ?? DateTimeOffset.MaxValue)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PartyNameLookup names = await LoadPartyNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return [.. tasks.Select(x => PeopleSliceProjection.ToModel(x, names))];
    }

    private async Task<Dictionary<Guid, string>> LoadTargetNamesAsync(
        OrganizationId organizationId,
        OpportunityNames names,
        CancellationToken cancellationToken)
    {
        var targets = await _context.OpportunityTargets
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new
            {
                Id = x.Id.Value,
                PersonId = x.PersonId,
                CompanyId = x.CompanyId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return targets.ToDictionary(
            x => x.Id,
            x => names.Party(x.PersonId?.Value, x.CompanyId?.Value));
    }

    private Task<Dictionary<Guid, string>> LoadMaterialTitlesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        _context.Materials
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken);

    private async Task<Dictionary<Guid, List<string>>> LoadParticipantNamesAsync(
        OrganizationId organizationId,
        List<Domain.Interactions.InteractionId> interactionIds,
        OpportunityNames names,
        CancellationToken cancellationToken)
    {
        var participants = await _context.InteractionParticipants
            .AsNoTracking()
            .Where(x => interactionIds.Contains(x.InteractionId) && x.OrganizationId == organizationId)
            .Select(x => new
            {
                InteractionId = x.InteractionId.Value,
                PersonId = x.PersonId,
                CompanyId = x.CompanyId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, List<string>> byInteraction = [];

        foreach (var participant in participants)
        {
            if (!byInteraction.TryGetValue(participant.InteractionId, out List<string>? who))
            {
                byInteraction[participant.InteractionId] = who = [];
            }

            who.Add(names.Party(participant.PersonId?.Value, participant.CompanyId?.Value));
        }

        return byInteraction;
    }

    private async Task<PartyNameLookup> LoadPartyNamesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> people = await _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> companies = await _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            .ConfigureAwait(false);

        return new PartyNameLookup(people, companies);
    }

    private async Task<OpportunityNames> LoadNamesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        PartyNameLookup parties = await LoadPartyNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await _context.Memberships
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { Id = u.Id.Value, u.DisplayName })
            .Distinct()
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        return new OpportunityNames(parties.People, parties.Companies, users);
    }

    private static DateTimeOffset? Latest(params DateTimeOffset?[] instants)
    {
        DateTimeOffset? latest = null;

        foreach (DateTimeOffset? instant in instants)
        {
            if (instant is { } value && (latest is null || value > latest))
            {
                latest = value;
            }
        }

        return latest;
    }

    private static string Humanize(OpportunityTargetEventKind kind) => kind switch
    {
        OpportunityTargetEventKind.Acknowledged => "Acknowledged receipt",
        OpportunityTargetEventKind.MoreMaterialRequested => "Asked for more material",
        OpportunityTargetEventKind.MeetingRequested => "Asked to meet",
        _ => "Noted",
    };

    /// <summary>Names for one tenant, resolved once per request.</summary>
    private sealed record OpportunityNames(
        Dictionary<Guid, string> People,
        Dictionary<Guid, string> Companies,
        Dictionary<Guid, string> Users)
    {
        public string Person(Guid id) => People.TryGetValue(id, out string? name) ? name : "(unknown)";

        public string Company(Guid id) =>
            Companies.TryGetValue(id, out string? name) ? name : "(unknown)";

        // An unresolvable name is shown as unknown rather than as an error: a
        // pipeline is still worth reading when one party has since been archived.
        public string? User(Guid id) => Users.TryGetValue(id, out string? name) ? name : null;

        public string Party(Guid? personId, Guid? companyId) =>
            personId is { } person ? Person(person)
            : companyId is { } company ? Company(company)
            : "(unknown)";
    }
}
