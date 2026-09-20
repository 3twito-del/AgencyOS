using System.Globalization;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Legal;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Companies;
using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for the contract model.
/// </summary>
/// <remarks>
/// <para>
/// Every fact about where a contract stands is <em>derived</em> here rather than
/// stored: whether it is fully executed, who still has to sign, whether it is in
/// force today, which obligations are past due, how far the draft has drifted from
/// what was agreed. There is no <c>is_executed</c> column to disagree with the
/// signature rows and no <c>difference_count</c> to go stale when a version is
/// recorded (ADR-0022).
/// </para>
/// <para>
/// The deadline lists are the same idea taken one step further: there is no
/// deadlines table at all. A date is read from the option, obligation or notice
/// requirement that carries it, and a row whose rule could not be resolved is
/// absent rather than guessed onto a day.
/// </para>
/// <para>
/// Deliberately unauthorized. <see cref="ContractQueryService"/> applies the
/// tenant-scoped checks and <see cref="ContractRedaction"/> removes the terms,
/// economics and privileged content a caller may not read.
/// </para>
/// </remarks>
internal sealed class ContractQueries : IContractQueries
{
    private readonly AgencyOsDbContext _context;
    private readonly IClock _clock;

    public ContractQueries(AgencyOsDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

    public async Task<IReadOnlyList<ContractSummaryModel>> ListContractsAsync(
        OrganizationId organizationId,
        ContractFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        List<Contract> contracts = await Filtered(organizationId, filter)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit * 4)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (contracts.Count == 0)
        {
            return [];
        }

        LegalContext context = await LoadContextAsync(
            organizationId, [.. contracts.Select(x => x.Id)], cancellationToken).ConfigureAwait(false);

        IEnumerable<ContractSummaryModel> summaries =
            contracts.Select(contract => ToSummary(contract, context));

        // Applied after projection, because each depends on rows other tables carry
        // rather than on anything the contract row holds.
        if (filter.AwaitingSignature)
        {
            summaries = summaries.Where(x => x.OutstandingSignatureCount > 0);
        }

        if (filter.EffectiveOnly)
        {
            summaries = summaries.Where(x => x.IsEffective);
        }

        if (filter.HasUnresolvedReconciliation)
        {
            summaries = summaries.Where(x => x.UnresolvedDifferenceCount > 0);
        }

        if (filter.TalentProfileId is { } talent)
        {
            summaries = summaries.Where(x => context.IsAboutTalent(x.DealId, talent));
        }

        if (filter.ProjectId is { } project)
        {
            summaries = summaries.Where(x => context.TouchesProject(x.Id, project));
        }

        return [.. summaries.Take(limit)];
    }

    public async Task<ContractDetailModel?> GetContractAsync(
        OrganizationId organizationId,
        ContractId id,
        CancellationToken cancellationToken = default)
    {
        Contract? contract = await _context.Contracts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return null;
        }

        LegalContext context = await LoadContextAsync(organizationId, [id], cancellationToken)
            .ConfigureAwait(false);

        List<ContractTerm> terms = await _context.ContractTerms
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && context.Versions[id].Select(v => v.Id).Contains(x.ContractVersionId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ILookup<ContractVersionId, ContractTerm> termsByVersion = terms.ToLookup(x => x.ContractVersionId);

        List<ContractRelationship> relationships = await _context.ContractRelationships
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && (x.ContractId == id || x.RelatedContractId == id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ContractId[] relatedIds =
        [
            .. relationships
                .Select(x => x.ContractId == id ? x.RelatedContractId : x.ContractId)
                .Distinct(),
        ];

        Dictionary<ContractId, Contract> related = relatedIds.Length == 0
            ? []
            : await _context.Contracts
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && relatedIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken)
                .ConfigureAwait(false);

        List<NoticeRecord> notices = await _context.NoticeRecords
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ContractId == id)
            .OrderByDescending(x => x.OccurredOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<NoticeRequirementId, int> noticeCounts = notices
            .Where(x => x.NoticeRequirementId is not null)
            .GroupBy(x => x.NoticeRequirementId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        return new ContractDetailModel(
            ToSummary(contract, context),
            contract.Summary,
            contract.LegalAnalysis,
            contract.StrategyNotes,
            contract.Privilege,
            [.. context.Parties[id].OrderBy(x => x.Role).Select(party => ToPartyModel(party, context))],
            [
                .. context.Versions[id]
                    .OrderByDescending(x => x.VersionNumber)
                    .Select(version => ToVersionModel(version, termsByVersion[version.Id], context)),
            ],
            [.. relationships.Select(x => ToRelationshipModel(x, id, related))],
            [.. context.Grants[id].Select(grant => ToGrantModel(grant, context, Today))],
            [.. context.Options[id].Select(option => ToOptionModel(option, context, Today))],
            [.. context.Obligations[id].Select(x => ToObligationModel(x, context, Today))],
            [
                .. context.NoticeRequirements[id]
                    .Select(requirement => ToRequirementModel(requirement, context, noticeCounts)),
            ],
            [.. notices.Select(notice => ToNoticeModel(notice, context))],
            context.TasksFor(id),
            contract.CreatedAt);
    }

    public async Task<ContractVersionModel?> GetVersionAsync(
        OrganizationId organizationId,
        ContractVersionId id,
        CancellationToken cancellationToken = default)
    {
        ContractVersion? version = await _context.ContractVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (version is null)
        {
            return null;
        }

        List<ContractTerm> terms = await _context.ContractTerms
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ContractVersionId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return ToVersionModel(version, terms, users);
    }

    /// <summary>
    /// Compares what was agreed against what a drafting version says.
    /// </summary>
    /// <remarks>
    /// A stateless projection. Nothing is written, nothing is cached, and the same
    /// pair always produces the same lines - which is what lets the answer be
    /// trusted after an amendment changes the version it was computed from.
    /// </remarks>
    public async Task<ReconciliationModel?> ReconcileAsync(
        OrganizationId organizationId,
        ContractId contractId,
        ContractVersionId versionId,
        CancellationToken cancellationToken = default)
    {
        Contract? contract = await _context.Contracts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.Id == contractId, cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return null;
        }

        ContractVersion? version = await _context.ContractVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.Id == versionId
                    && x.ContractId == contractId,
                cancellationToken)
            .ConfigureAwait(false);

        if (version is null)
        {
            return null;
        }

        List<ContractTerm> drafted = await _context.ContractTerms
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ContractVersionId == versionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<OfferTerm> negotiated = await _context.OfferTerms
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.OfferId == contract.AcceptedOfferId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Reconcile(contract, version, negotiated, drafted);
    }

    public async Task<IReadOnlyList<ContractOptionModel>> ListOptionsAsync(
        OrganizationId organizationId,
        OptionFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<ContractOption> query = _context.ContractOptions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (filter.ContractId is { } contractId)
        {
            query = query.Where(x => x.ContractId == contractId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (filter.DeadlineBefore is { } before)
        {
            query = query.Where(x => x.ResolvedDeadlineOn != null && x.ResolvedDeadlineOn <= before);
        }

        List<ContractOption> options = await query
            .OrderBy(x => x.ResolvedDeadlineOn ?? DateOnly.MaxValue)
            .ThenByDescending(x => x.RecordedAt)
            .Take(limit * 2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (options.Count == 0)
        {
            return [];
        }

        DateOnly today = Today;

        LegalContext context = await LoadContextAsync(
            organizationId, [.. options.Select(x => x.ContractId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<ContractOptionModel> models =
            options.Select(option => ToOptionModel(option, context, today));

        // Both are questions about a date against today, so they are answered on
        // the projected rows rather than in SQL that would have to hardcode a clock.
        if (filter.ExercisableOnly)
        {
            models = models.Where(x => x.IsExercisable);
        }

        if (filter.PastDeadlineOnly)
        {
            models = models.Where(x => x.IsPastDeadline);
        }

        return [.. models.Take(limit)];
    }

    public async Task<IReadOnlyList<ObligationModel>> ListObligationsAsync(
        OrganizationId organizationId,
        ObligationFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Obligation> query = _context.Obligations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (filter.ContractId is { } contractId)
        {
            query = query.Where(x => x.ContractId == contractId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(x => x.Kind == kind);
        }

        if (filter.ObligorPartyId is { } obligor)
        {
            query = query.Where(x => x.ObligorPartyId == obligor);
        }

        if (filter.DueBefore is { } before)
        {
            query = query.Where(x => x.ResolvedDueOn != null && x.ResolvedDueOn <= before);
        }

        List<Obligation> obligations = await query
            .OrderBy(x => x.ResolvedDueOn ?? DateOnly.MaxValue)
            .ThenByDescending(x => x.RecordedAt)
            .Take(limit * 2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (obligations.Count == 0)
        {
            return [];
        }

        DateOnly today = Today;

        LegalContext context = await LoadContextAsync(
            organizationId, [.. obligations.Select(x => x.ContractId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<ObligationModel> models =
            obligations.Select(obligation => ToObligationModel(obligation, context, today));

        if (filter.OutstandingOnly)
        {
            models = models.Where(x => DealRules.IsObligationOutstanding((int)x.Status));
        }

        // Past due, never breached. Whether a missed date is a breach is a legal
        // determination somebody records, not something a query decides (ADR-0022).
        if (filter.OverdueOnly)
        {
            models = models.Where(x => x.IsPastDue);
        }

        return [.. models.Take(limit)];
    }

    public async Task<IReadOnlyList<RightsGrantModel>> ListRightsGrantsAsync(
        OrganizationId organizationId,
        ContractId? contractId,
        Guid? projectId,
        bool currentOnly,
        CancellationToken cancellationToken = default)
    {
        IQueryable<RightsGrant> query = _context.RightsGrants
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (contractId is { } contract)
        {
            query = query.Where(x => x.ContractId == contract);
        }

        if (projectId is { } project)
        {
            ProjectId typed = new(project);

            query = query.Where(x => x.ProjectId == typed);
        }

        if (currentOnly)
        {
            query = query.Where(x => x.Status == RightsGrantStatus.Active);
        }

        List<RightsGrant> grants = await query
            .OrderBy(x => x.RightType)
            .ThenBy(x => x.Medium)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (grants.Count == 0)
        {
            return [];
        }

        DateOnly today = Today;

        LegalContext context = await LoadContextAsync(
            organizationId, [.. grants.Select(x => x.ContractId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        return [.. grants.Select(grant => ToGrantModel(grant, context, today))];
    }

    /// <summary>
    /// A contract's legal timeline.
    /// </summary>
    /// <remarks>
    /// Composed from the contract, option and obligation event rows plus the
    /// signatures and notices recorded against it. Never raw audit rows: the audit
    /// log answers "who did what to this system" in a security vocabulary, and a
    /// lawyer reading a contract's history is asking a different question
    /// (ADR-0012, ADR-0022).
    /// </remarks>
    public async Task<IReadOnlyList<ContractHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        ContractId id,
        int limit,
        CancellationToken cancellationToken = default)
    {
        List<ContractEvent> contractEvents = await _context.ContractEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ContractId == id)
            .OrderByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ContractSignature> signatures = await _context.ContractSignatures
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ContractId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<NoticeRecord> notices = await _context.NoticeRecords
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ContractId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var optionEvents = await _context.OptionEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.ContractOptions.AsNoTracking().Where(o => o.ContractId == id),
                entry => entry.ContractOptionId,
                option => option.Id,
                (entry, option) => new { Event = entry, option.Subject })
            .OrderByDescending(x => x.Event.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var obligationEvents = await _context.ObligationEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.Obligations.AsNoTracking().Where(o => o.ContractId == id),
                entry => entry.ObligationId,
                obligation => obligation.Id,
                (entry, obligation) => new { Event = entry, obligation.Description })
            .OrderByDescending(x => x.Event.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        List<ContractParty> parties = await _context.ContractParties
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ContractId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PartyNames names = await LoadPartyNamesAsync(organizationId, parties, cancellationToken)
            .ConfigureAwait(false);

        List<ContractHistoryEntryModel> entries =
        [
            .. contractEvents.Select(entry => new ContractHistoryEntryModel(
                entry.RecordedAt,
                "Contract",
                DescribeContractEvent(entry),
                entry.Detail ?? entry.Reason,
                Name(users, entry.RecordedBy))),

            .. signatures.Select(signature => new ContractHistoryEntryModel(
                signature.RecordedAt,
                "Signature",
                $"{names.Display(signature.ContractPartyId)} signed",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{signature.Method} signature reported on {signature.SignedOn:yyyy-MM-dd}"),
                Name(users, signature.RecordedBy))),

            .. notices.Select(notice => new ContractHistoryEntryModel(
                notice.RecordedAt,
                "Notice",
                notice.Direction == NoticeDirection.Given
                    ? $"Notice recorded as given to {names.Display(notice.RecipientPartyId)}"
                    : $"Notice recorded as received from {names.Display(notice.SenderPartyId)}",
                notice.Summary,
                Name(users, notice.RecordedBy))),

            .. optionEvents.Select(x => new ContractHistoryEntryModel(
                x.Event.RecordedAt,
                "Option",
                $"{DescribeOptionEvent(x.Event)}: {x.Subject}",
                x.Event.Reason,
                Name(users, x.Event.RecordedBy))),

            .. obligationEvents.Select(x => new ContractHistoryEntryModel(
                x.Event.RecordedAt,
                "Obligation",
                $"{DescribeObligationEvent(x.Event)}: {x.Description}",
                x.Event.Reason,
                Name(users, x.Event.RecordedBy))),
        ];

        return [.. entries.OrderByDescending(entry => entry.OccurredAt).Take(limit)];
    }

    /// <summary>
    /// Every legal date inside a horizon, unioned from the rows that carry one.
    /// </summary>
    /// <remarks>
    /// Options, obligations and notice requirements each keep their own resolved
    /// date, and this reads all three. A row whose rule could not be resolved -
    /// because its anchor event has not happened, or because it counts business
    /// days this build cannot count - is simply not here. Inventing a date for it
    /// would put a deadline in a lawyer's calendar that the contract never set
    /// (ADR-0022).
    /// </remarks>
    public async Task<IReadOnlyList<LegalDeadlineModel>> GetDeadlinesAsync(
        OrganizationId organizationId,
        DateOnly from,
        int withinDays,
        CancellationToken cancellationToken = default)
    {
        DateOnly until = from.AddDays(withinDays);

        var options = await _context.ContractOptions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.Status == OptionStatus.Available
                && x.ResolvedDeadlineOn != null
                && x.ResolvedDeadlineOn <= until)
            .Join(
                _context.Contracts.AsNoTracking(),
                option => option.ContractId,
                contract => contract.Id,
                (option, contract) => new
                {
                    option.Id,
                    option.ContractId,
                    contract.Title,
                    Due = option.ResolvedDeadlineOn!.Value,
                    option.Subject,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var obligations = await _context.Obligations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.Status == ObligationStatus.Pending
                && x.ResolvedDueOn != null
                && x.ResolvedDueOn <= until)
            .Join(
                _context.Contracts.AsNoTracking(),
                obligation => obligation.ContractId,
                contract => contract.Id,
                (obligation, contract) => new
                {
                    obligation.Id,
                    obligation.ContractId,
                    contract.Title,
                    Due = obligation.ResolvedDueOn!.Value,
                    obligation.Description,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var notices = await _context.NoticeRequirements
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.ResolvedDueOn != null
                && x.ResolvedDueOn <= until)
            .Join(
                _context.Contracts.AsNoTracking(),
                requirement => requirement.ContractId,
                contract => contract.Id,
                (requirement, contract) => new
                {
                    requirement.Id,
                    requirement.ContractId,
                    contract.Title,
                    Due = requirement.ResolvedDueOn!.Value,
                    requirement.Description,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<LegalDeadlineModel> deadlines =
        [
            .. options.Select(x => Deadline(
                LegalDeadlineSource.Option, x.Id.Value, x.ContractId, x.Title, x.Due, x.Subject, from)),

            .. obligations.Select(x => Deadline(
                LegalDeadlineSource.Obligation,
                x.Id.Value,
                x.ContractId,
                x.Title,
                x.Due,
                x.Description,
                from)),

            .. notices.Select(x => Deadline(
                LegalDeadlineSource.Notice,
                x.Id.Value,
                x.ContractId,
                x.Title,
                x.Due,
                x.Description,
                from)),
        ];

        return [.. deadlines.OrderBy(x => x.DueOn).ThenBy(x => x.ContractTitle)];
    }

    /// <summary>
    /// What the legal side of the desk has to look at.
    /// </summary>
    /// <remarks>
    /// Counts, dates and lists. No risk score, no reading of what a clause means,
    /// and no expected payment: whether a difference matters is a legal judgement
    /// and whether money arrives is M9's subject (ADR-0022).
    /// </remarks>
    public async Task<ContractCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        const int Recent = 30;

        IReadOnlyList<ContractSummaryModel> underReview = await ListContractsAsync(
            organizationId,
            new ContractFilter(Status: ContractStatus.UnderReview),
            25,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ContractSummaryModel> awaitingSignature = await ListContractsAsync(
            organizationId,
            new ContractFilter(AwaitingSignature: true),
            25,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ContractSummaryModel> differing = await ListContractsAsync(
            organizationId,
            new ContractFilter(HasUnresolvedReconciliation: true),
            25,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ContractSummaryModel> executed = await ListContractsAsync(
            organizationId,
            new ContractFilter(ExecutedAfter: today.AddDays(-Recent)),
            25,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<LegalDeadlineModel> deadlines =
            await GetDeadlinesAsync(organizationId, today, 60, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ObligationModel> overdue = await ListObligationsAsync(
            organizationId,
            new ObligationFilter(OverdueOnly: true, OutstandingOnly: true),
            50,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ContractOptionModel> lapsing = await ListOptionsAsync(
            organizationId,
            new OptionFilter(PastDeadlineOnly: true),
            50,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ContractTaskModel> overdueTasks =
            await LoadOverdueTasksAsync(organizationId, now, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<DealsWithoutContractModel> unpapered =
            await LoadUnpaperedDealsAsync(organizationId, cancellationToken).ConfigureAwait(false);

        int underReviewCount = await _context.Contracts
            .AsNoTracking()
            .CountAsync(
                x => x.OrganizationId == organizationId && x.Status == ContractStatus.UnderReview,
                cancellationToken)
            .ConfigureAwait(false);

        int effectiveCount = await _context.Contracts
            .AsNoTracking()
            .CountAsync(
                x => x.OrganizationId == organizationId
                    && x.EffectiveOn != null
                    && x.EffectiveOn <= today
                    && (x.TerminatedOn == null || x.TerminatedOn >= today)
                    && x.Status != ContractStatus.Abandoned
                    && x.Status != ContractStatus.Superseded,
                cancellationToken)
            .ConfigureAwait(false);

        return new ContractCommandCenterModel(
            underReview,
            awaitingSignature,
            differing,
            executed,
            unpapered,
            deadlines,
            overdue,
            lapsing,
            overdueTasks,
            underReviewCount,
            awaitingSignature.Count,
            effectiveCount);
    }

    // ---------------------------------------------------------------- filtering

    private IQueryable<Contract> Filtered(OrganizationId organizationId, ContractFilter filter)
    {
        IQueryable<Contract> query = _context.Contracts
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
            query = query.Where(x => x.OwnerUserId == owner);
        }

        if (filter.DealId is { } deal)
        {
            query = query.Where(x => x.DealId == deal);
        }

        if (filter.ExecutedAfter is { } after)
        {
            query = query.Where(x => x.ExecutedOn != null && x.ExecutedOn >= after);
        }

        if (filter.ExecutedBefore is { } before)
        {
            query = query.Where(x => x.ExecutedOn != null && x.ExecutedOn <= before);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string pattern = $"%{filter.Search.Trim()}%";

            // The parties are the relationship a contract genuinely has to people and
            // companies, so a human name reaches the paper they signed. Before this,
            // searching a client's name here returned nothing and the instrument was
            // findable only by its title.
            IQueryable<ContractId> signedByName = _context.ContractParties
                .Where(party =>
                    _context.People.Any(p =>
                        p.Id == party.PersonId && EF.Functions.ILike(p.DisplayName, pattern))
                    || _context.Companies.Any(c =>
                        c.Id == party.CompanyId && EF.Functions.ILike(c.Name, pattern))
                    || (party.ExternalName != null
                        && EF.Functions.ILike(party.ExternalName, pattern)))
                .Select(party => party.ContractId);

            query = query.Where(x =>
                EF.Functions.ILike(x.Title, pattern)
                || (x.Reference != null && EF.Functions.ILike(x.Reference, pattern))
                || signedByName.Contains(x.Id));
        }

        // Party filters narrow through the party table rather than through a column
        // this one does not carry. A contract has several parties and any of them
        // may be the one somebody is looking for.
        if (filter.PartyCompanyId is { } company)
        {
            CompanyId typed = new(company);

            query = query.Where(x => _context.ContractParties
                .Any(party => party.ContractId == x.Id && party.CompanyId == typed));
        }

        if (filter.PartyPersonId is { } person)
        {
            PersonId typed = new(person);

            query = query.Where(x => _context.ContractParties
                .Any(party => party.ContractId == x.Id && party.PersonId == typed));
        }

        return query;
    }

    // -------------------------------------------------------------- projections

    private static ContractSummaryModel ToSummary(Contract contract, LegalContext context)
    {
        List<ContractVersion> versions =
            [.. context.Versions[contract.Id].OrderBy(x => x.VersionNumber)];

        ContractVersion? latest = versions.LastOrDefault();

        List<ContractParty> parties = [.. context.Parties[contract.Id]];

        int outstanding = parties.Count(party =>
            party.IsRequiredSignatory && !context.HasSigned(contract.Id, party.Id));

        List<Obligation> obligations = [.. context.Obligations[contract.Id]];

        LegalDeadlineModel? next = context.NextDeadline(contract.Id);

        return new ContractSummaryModel(
            contract.Id,
            contract.Title,
            contract.Reference,
            contract.Kind,
            contract.Status,
            contract.DealId,
            context.DealName(contract.DealId),
            contract.AcceptedOfferId,
            context.Subject(contract.DealId),
            context.Counterparty(contract.DealId),
            contract.OwnerUserId,
            Name(context.Users, contract.OwnerUserId),
            contract.ExecutedOn,
            contract.EffectiveOn,
            contract.TerminatedOn,
            contract.IsEffectiveOn(context.Today),
            versions.Count,
            latest?.VersionNumber,
            latest?.Label,
            latest?.Id,
            outstanding,
            parties.Count,
            context.Grants[contract.Id].Count(),
            context.Options[contract.Id].Count(option => option.Status == OptionStatus.Available),
            obligations.Count(x => DealRules.IsObligationOutstanding((int)x.Status)),
            obligations.Count(x => x.IsPastDueOn(context.Today)),
            next?.DueOn,
            next?.Description,
            context.DifferenceCount(contract.Id),
            contract.UpdatedAt,
            contract.Version);
    }

    private static ContractPartyModel ToPartyModel(ContractParty party, LegalContext context)
    {
        ContractSignature? signature = context.SignatureFor(party.ContractId, party.Id);

        return new ContractPartyModel(
            party.Id,
            party.Role,
            context.Names.Display(party),
            party.PersonId?.Value,
            party.CompanyId?.Value,
            party.IsResolved,
            party.Provenance,
            party.IsRequiredSignatory,
            signature is not null,
            signature?.SignedOn);
    }

    private static ContractVersionModel ToVersionModel(
        ContractVersion version,
        IEnumerable<ContractTerm> terms,
        LegalContext context) =>
        ToVersionModel(version, terms, context.Users);

    private static ContractVersionModel ToVersionModel(
        ContractVersion version,
        IEnumerable<ContractTerm> terms,
        Dictionary<Guid, string> users) =>
        new(
            version.Id,
            version.ContractId,
            version.VersionNumber,
            version.Label,
            version.Direction,
            version.Status,
            version.RecordedAt,
            version.ReceivedOn,
            version.SentOn,
            Name(users, version.RecordedBy),
            version.ExternalReference,
            version.SourceSystem,
            version.DisplayFileName,
            version.MediaType,

            // Stated rather than assumed. False for every version M8 recorded,
            // and true only once M10 has actually stored the bytes, so the surface
            // says which it is instead of implying one (ADR-0022, ADR-0024).
            version.HoldsDocument,

            version.Notes,
            [.. terms.OrderBy(term => term.Sequence).Select(ToTermModel)],
            version.Version);

    private static ContractTermModel ToTermModel(ContractTerm term)
    {
        ContractTermDefinition? definition = ContractTermCatalog.Find(term.Code);

        return new ContractTermModel(
            term.Code,
            term.Label ?? definition?.DisplayName ?? term.Code.ToString(),
            term.ValueKind,
            ContractTermCatalog.IsEconomic(term.Code),
            term.Privilege is PrivilegeClass.LegalStrategy or PrivilegeClass.AttorneyClientPrivileged,
            definition?.IsCommercial ?? false,
            term.AmountValue,
            term.CurrencyCodeValue,
            term.NumericValue,
            term.IntegerValue,
            term.TextValue,
            term.BooleanValue,
            term.DateValue,
            term.Unit,
            Render(term.ValueKind, term.AmountValue, term.CurrencyCodeValue, term.NumericValue,
                term.IntegerValue, term.TextValue, term.BooleanValue, term.DateValue, term.Unit),
            term.ClauseReference,
            term.Sequence,
            term.Notes);
    }

    private static RightsGrantModel ToGrantModel(
        RightsGrant grant,
        LegalContext context,
        DateOnly today)
    {
        GrantPeriodInput period = new()
        {
            Kind = (int)grant.PeriodKind,
            Starts = grant.StartsOn,
            Ends = grant.EndsOn,
        };

        return new RightsGrantModel(
            grant.Id,
            grant.ContractId,
            grant.ContractVersionId,
            grant.ClauseReference,
            grant.GrantorPartyId,
            context.Names.Display(grant.GrantorPartyId),
            grant.GranteePartyId,
            context.Names.Display(grant.GranteePartyId),
            grant.RightType,
            grant.Medium,
            grant.Territory,
            grant.TerritoryDetail,
            grant.Exclusivity,
            grant.PeriodKind,
            grant.StartsOn,
            grant.EndsOn,

            // Running today, as the contract describes it. Not a finding that the
            // grantor held the right, and not a clearance (ADR-0022).
            grant.Status == RightsGrantStatus.Active && DealRules.PeriodCoversOn(period, today),

            grant.SourcePropertyId?.Value,
            context.SourcePropertyTitle(grant.SourcePropertyId?.Value),
            grant.ProjectId?.Value,
            context.ProjectTitle(grant.ProjectId?.Value),
            grant.Reservations,
            grant.Notes,
            grant.Status,
            grant.SupersededByGrantId,
            grant.Version);
    }

    private static ContractOptionModel ToOptionModel(
        ContractOption option,
        LegalContext context,
        DateOnly today) =>
        new(
            option.Id,
            option.ContractId,
            option.ContractVersionId,
            option.ClauseReference,
            option.Kind,
            option.HolderPartyId,
            context.Names.Display(option.HolderPartyId),
            option.Subject,
            option.ProjectId?.Value,
            context.ProjectTitle(option.ProjectId?.Value),
            option.WindowOpensOn,
            option.ResolvedDeadlineOn,

            // Present exactly when the date is not, and it says which of the three
            // honest reasons applies rather than leaving a blank column.
            option.ResolvedDeadlineOn is null ? option.Deadline.WhyUnresolved(null) : null,

            option.Deadline.Description,
            option.ExerciseMethod,
            option.Status,
            option.ResolvedOn,
            option.IsExercisableOn(today),
            option.IsPastDeadlineOn(today),
            option.EconomicsTermId,
            option.NoticeRequirementId is { } requirement
                ? new NoticeRequirementId(requirement)
                : null,
            option.Notes,
            option.Version);

    private static ObligationModel ToObligationModel(
        Obligation obligation,
        LegalContext context,
        DateOnly today) =>
        new(
            obligation.Id,
            obligation.ContractId,
            obligation.ContractVersionId,
            obligation.ClauseReference,
            obligation.ObligorPartyId,
            context.Names.Display(obligation.ObligorPartyId),
            obligation.ObligeePartyId,
            context.Names.Display(obligation.ObligeePartyId),
            obligation.Kind,
            obligation.Description,
            obligation.ResolvedDueOn,
            obligation.ResolvedDueOn is null ? obligation.Due.WhyUnresolved(null) : null,
            obligation.Due.Description,
            obligation.Status,
            obligation.ResolvedOn,

            // Past due is a fact about a date. Breach is a determination somebody
            // records, and the two are separate fields for that reason (ADR-0022).
            obligation.IsPastDueOn(today),

            obligation.RelatedOptionId,
            obligation.RelatedRightsGrantId,
            obligation.Privilege
                is PrivilegeClass.LegalStrategy or PrivilegeClass.AttorneyClientPrivileged,
            obligation.Notes,
            obligation.Version);

    private static NoticeRequirementModel ToRequirementModel(
        NoticeRequirement requirement,
        LegalContext context,
        Dictionary<NoticeRequirementId, int> counts) =>
        new(
            requirement.Id,
            requirement.ContractId,
            requirement.ClauseReference,
            requirement.ObligorPartyId,
            context.Names.Display(requirement.ObligorPartyId),
            requirement.RecipientPartyId,
            context.Names.Display(requirement.RecipientPartyId),
            requirement.Description,
            requirement.ResolvedDueOn,
            requirement.ResolvedDueOn is null ? requirement.Due.WhyUnresolved(null) : null,
            requirement.Due.Description,
            requirement.Method,
            requirement.AddressReference,
            requirement.RelatedOptionId,
            requirement.RelatedObligationId,
            counts.TryGetValue(requirement.Id, out int count) ? count : 0,
            requirement.Version);

    private static NoticeRecordModel ToNoticeModel(NoticeRecord notice, LegalContext context) =>
        new(
            notice.Id,
            notice.ContractId,
            notice.NoticeRequirementId,
            notice.Direction,
            context.Names.Display(notice.SenderPartyId),
            context.Names.Display(notice.RecipientPartyId),
            notice.OccurredOn,
            notice.Method,
            notice.ExternalReference,
            notice.Summary,
            Name(context.Users, notice.RecordedBy));

    private static ContractRelationshipModel ToRelationshipModel(
        ContractRelationship relationship,
        ContractId subject,
        Dictionary<ContractId, Contract> related)
    {
        ContractId other = relationship.ContractId == subject
            ? relationship.RelatedContractId
            : relationship.ContractId;

        return new ContractRelationshipModel(
            relationship.Id,
            relationship.Kind,
            other,
            related.TryGetValue(other, out Contract? contract) ? contract.Title : "(unknown)",
            contract?.Status ?? ContractStatus.Draft,
            relationship.Notes);
    }

    private static LegalDeadlineModel Deadline(
        LegalDeadlineSource source,
        Guid sourceId,
        ContractId contractId,
        string title,
        DateOnly due,
        string description,
        DateOnly from) =>
        new(
            source,
            sourceId,
            contractId,
            title,
            due,
            description,
            DealRules.DaysUntilDeadline(from, due),
            DealRules.IsDeadlinePast(from, due));

    // ------------------------------------------------------------ reconciliation

    private static ReconciliationModel Reconcile(
        Contract contract,
        ContractVersion version,
        IReadOnlyList<OfferTerm> negotiated,
        IReadOnlyList<ContractTerm> drafted)
    {
        ReconciliationLine[] lines = DealRules.Reconcile(
            [.. negotiated.OrderBy(x => x.Sequence).Select(x => x.ToRulesInput())],
            [.. drafted.OrderBy(x => x.Sequence).Select(x => x.ToRulesInput())]);

        Dictionary<ContractTermCode, ContractTermModel> negotiatedByCode = negotiated
            .ToDictionary(x => (ContractTermCode)(int)x.Code, ToNegotiatedModel);

        Dictionary<ContractTermCode, ContractTermModel> draftedByCode = drafted
            .ToDictionary(x => x.Code, ToTermModel);

        return new ReconciliationModel(
            contract.Id,
            version.Id,
            contract.AcceptedOfferId,
            [.. lines.Select(line => ToLineModel(line, negotiatedByCode, draftedByCode))],
            DealRules.ReconciliationDifferenceCount(lines),
            DealRules.IsDraftFaithful(lines));
    }

    private static ReconciliationLineModel ToLineModel(
        ReconciliationLine line,
        Dictionary<ContractTermCode, ContractTermModel> negotiated,
        Dictionary<ContractTermCode, ContractTermModel> drafted)
    {
        ContractTermCode code = (ContractTermCode)line.TermCode;

        return new ReconciliationLineModel(
            code,
            ContractTermCatalog.Find(code)?.DisplayName ?? code.ToString(),
            line.Result.ToString(),
            line.Direction.ToString(),
            negotiated.TryGetValue(code, out ContractTermModel? was) ? was : null,
            drafted.TryGetValue(code, out ContractTermModel? now) ? now : null);
    }

    /// <summary>
    /// Renders a negotiated term in the contract vocabulary.
    /// </summary>
    /// <remarks>
    /// The two term codes share their integer values on purpose, so this is a cast
    /// rather than a translation table. Presenting both sides of a comparison in
    /// one shape is what lets a reader see a change rather than a difference in
    /// how the two systems describe the same thing (ADR-0022).
    /// </remarks>
    private static ContractTermModel ToNegotiatedModel(OfferTerm term)
    {
        ContractTermCode code = (ContractTermCode)(int)term.Code;
        ContractTermDefinition? definition = ContractTermCatalog.Find(code);

        return new ContractTermModel(
            code,
            term.Label ?? definition?.DisplayName ?? code.ToString(),
            term.ValueKind,
            DealTermCatalog.IsEconomic(term.Code),

            // A negotiated term carries no privilege classification. Privilege is a
            // property of legal work product, and an offer is a commercial position.
            false,

            definition?.IsCommercial ?? true,
            term.AmountValue,
            term.CurrencyCodeValue,
            term.NumericValue,
            term.IntegerValue,
            term.TextValue,
            term.BooleanValue,
            term.DateValue,
            term.Unit,
            Render(term.ValueKind, term.AmountValue, term.CurrencyCodeValue, term.NumericValue,
                term.IntegerValue, term.TextValue, term.BooleanValue, term.DateValue, term.Unit),
            null,
            term.Sequence,
            term.Notes);
    }

    /// <summary>
    /// Renders a term value for a screen.
    /// </summary>
    /// <remarks>
    /// Never canonical and never parsed back. The typed columns are what
    /// reconciliation compares; this is produced in invariant culture so it reads
    /// the same for everybody looking at the same contract.
    /// </remarks>
    private static string Render(
        TermValueKind kind,
        decimal? amount,
        string? currency,
        decimal? number,
        long? whole,
        string? text,
        bool? flag,
        DateOnly? date,
        TermUnit? unit) => kind switch
        {
            TermValueKind.Money when amount is { } money =>
                string.Create(CultureInfo.InvariantCulture, $"{money:N2} {currency}"),

            TermValueKind.Percentage when number is { } percentage =>
                string.Create(CultureInfo.InvariantCulture, $"{percentage:0.####}%"),

            TermValueKind.Decimal when number is { } value =>
                value.ToString("0.####", CultureInfo.InvariantCulture),

            TermValueKind.Integer when whole is { } value =>
                value.ToString(CultureInfo.InvariantCulture),

            TermValueKind.Count when whole is { } quantity =>
                unit is { } counted
                    ? string.Create(CultureInfo.InvariantCulture, $"{quantity} {counted}")
                    : quantity.ToString(CultureInfo.InvariantCulture),

            TermValueKind.Duration when whole is { } length =>
                string.Create(CultureInfo.InvariantCulture, $"{length} {unit}"),

            TermValueKind.Date when date is { } day => day.ToString("O"),
            TermValueKind.Boolean when flag is { } value => value ? "Yes" : "No",
            TermValueKind.Text => text ?? string.Empty,
            _ => string.Empty,
        };

    // ------------------------------------------------------------- descriptions

    private static string DescribeContractEvent(ContractEvent entry) => entry.Kind switch
    {
        ContractEventKind.Opened => "Contract opened",
        ContractEventKind.MetadataUpdated => "Contract details updated",
        ContractEventKind.VersionRecorded => "Drafting version recorded",
        ContractEventKind.SignatureRecorded => "Signature recorded",
        ContractEventKind.EffectivenessRecorded => "Effective date recorded",
        _ => entry.Transition switch
        {
            ContractTransition.SentForReview => "Sent for review",
            ContractTransition.ReturnedToDrafting => "Returned to drafting",
            ContractTransition.ApprovedForSignature => "Approved for signature",
            ContractTransition.SignatureRecorded => "Signature recorded",
            ContractTransition.ExecutionCompleted => "Fully executed",
            ContractTransition.DraftingAbandoned => "Drafting abandoned",
            ContractTransition.ReplacedByAnother => "Superseded by another agreement",
            ContractTransition.TerminationRecorded => "Termination recorded",
            _ => string.Create(CultureInfo.InvariantCulture, $"Moved to {entry.ToStatus}"),
        },
    };

    private static string DescribeOptionEvent(OptionEvent entry) => entry.Transition switch
    {
        OptionTransition.Exercise => "Option exercised",
        OptionTransition.Decline => "Option declined",
        OptionTransition.Waive => "Option waived",
        OptionTransition.RecordExpiry => "Option lapsed at its deadline",
        OptionTransition.Cancel => "Option cancelled",
        _ => "Option recorded",
    };

    private static string DescribeObligationEvent(ObligationEvent entry) => entry.Transition switch
    {
        ObligationTransition.Satisfy => "Obligation satisfied",
        ObligationTransition.Waive => "Obligation waived",
        ObligationTransition.RecordBreach => "Breach recorded",
        ObligationTransition.Reinstate => "Obligation reinstated",
        ObligationTransition.Cancel => "Obligation cancelled",
        _ => "Obligation recorded",
    };

    private static string? Name(Dictionary<Guid, string> users, UserId id) =>
        users.TryGetValue(id.Value, out string? name) ? name : null;

    // ------------------------------------------------------------------ loading

    private Task<Dictionary<Guid, string>> LoadUserNamesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        _context.Memberships
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { Id = u.Id.Value, u.DisplayName })
            .Distinct()
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);

    private async Task<PartyNames> LoadPartyNamesAsync(
        OrganizationId organizationId,
        IReadOnlyList<ContractParty> parties,
        CancellationToken cancellationToken)
    {
        // Typed identifiers, not raw Guids. EF cannot translate a Contains over the
        // inner value of a converted key, and unwrapping it here would push the
        // whole comparison into memory.
        PersonId[] personIds =
            [.. parties.Where(x => x.PersonId is not null).Select(x => x.PersonId!.Value)];

        CompanyId[] companyIds =
            [.. parties.Where(x => x.CompanyId is not null).Select(x => x.CompanyId!.Value)];

        Dictionary<Guid, string> people = personIds.Length == 0
            ? []
            : await _context.People
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && personIds.Contains(x.Id))
                .Select(x => new { Id = x.Id.Value, x.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
                .ConfigureAwait(false);

        Dictionary<Guid, string> companies = companyIds.Length == 0
            ? []
            : await _context.Companies
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && companyIds.Contains(x.Id))
                .Select(x => new { Id = x.Id.Value, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
                .ConfigureAwait(false);

        return new PartyNames(parties.ToDictionary(x => x.Id), people, companies);
    }

    private async Task<IReadOnlyList<ContractTaskModel>> LoadOverdueTasksAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await _context.ContractTaskLinks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.Tasks.AsNoTracking()
                    .Where(t => t.State == TaskState.Open && t.DueAt != null && t.DueAt < now),
                link => link.TaskItemId,
                task => task.Id,
                (link, task) => new
                {
                    TaskId = task.Id.Value,
                    task.Title,
                    task.State,
                    task.Priority,
                    task.DueAt,
                    link.ObligationId,
                    link.ContractOptionId,
                })
            .OrderBy(x => x.DueAt)
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(x => new ContractTaskModel(
                x.TaskId,
                x.Title,
                x.State.ToString(),
                x.Priority.ToString(),
                x.DueAt,
                x.ObligationId,
                x.ContractOptionId)),
        ];
    }

    /// <summary>
    /// Negotiations whose terms are agreed and which nobody has papered.
    /// </summary>
    /// <remarks>
    /// The gap between M7 and M8, stated as a list. A deal with an accepted offer
    /// and no contract row is not an error - the paper is often days behind the
    /// handshake - but it is the thing a legal desk most needs to see.
    /// </remarks>
    private async Task<IReadOnlyList<DealsWithoutContractModel>> LoadUnpaperedDealsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        var rows = await _context.Offers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Status == OfferStatus.Accepted)
            .Where(x => !_context.Contracts.Any(contract =>
                contract.OrganizationId == organizationId && contract.DealId == x.DealId))
            .Join(
                _context.Deals.AsNoTracking(),
                offer => offer.DealId,
                deal => deal.Id,
                (offer, deal) => new
                {
                    deal.Id,
                    deal.Name,
                    deal.OpportunityTargetId,
                    offer.UpdatedAt,
                })
            .OrderByDescending(x => x.UpdatedAt)
            .Take(25)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        OpportunityTargetId[] targetIds = [.. rows.Select(x => x.OpportunityTargetId).Distinct()];

        List<OpportunityTarget> targets = await _context.OpportunityTargets
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && targetIds.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> people = await LoadPeopleAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> companies = await LoadCompaniesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<OpportunityTargetId, OpportunityTarget> byId = targets.ToDictionary(x => x.Id);

        return
        [
            .. rows.Select(x => new DealsWithoutContractModel(
                x.Id,
                x.Name,
                Counterparty(byId, x.OpportunityTargetId, people, companies),
                x.UpdatedAt)),
        ];
    }

    private Task<Dictionary<Guid, string>> LoadPeopleAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);

    private Task<Dictionary<Guid, string>> LoadCompaniesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

    private static string Counterparty(
        Dictionary<OpportunityTargetId, OpportunityTarget> targets,
        OpportunityTargetId id,
        Dictionary<Guid, string> people,
        Dictionary<Guid, string> companies)
    {
        if (!targets.TryGetValue(id, out OpportunityTarget? target))
        {
            return "(unknown)";
        }

        if (target.CompanyId is { } company)
        {
            return companies.TryGetValue(company.Value, out string? name) ? name : "(unknown)";
        }

        return target.PersonId is { } person && people.TryGetValue(person.Value, out string? personName)
            ? personName
            : "(unknown)";
    }

    /// <summary>
    /// Everything a batch of contracts needs, loaded once.
    /// </summary>
    /// <remarks>
    /// Parties, signatures, versions, grants, options, obligations, notices, tasks
    /// and the terms reconciliation needs, in a fixed number of queries rather than
    /// a number that grows with the list. The derived facts are then computed in
    /// memory, which is what keeps them impossible to disagree with the rows they
    /// came from.
    /// </remarks>
    private async Task<LegalContext> LoadContextAsync(
        OrganizationId organizationId,
        IReadOnlyList<ContractId> contractIds,
        CancellationToken cancellationToken)
    {
        ContractId[] ids = [.. contractIds.Distinct()];

        List<Contract> contracts = await _context.Contracts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ContractParty> parties = await _context.ContractParties
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ContractSignature> signatures = await _context.ContractSignatures
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ContractVersion> versions = await _context.ContractVersions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<RightsGrant> grants = await _context.RightsGrants
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ContractOption> options = await _context.ContractOptions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Obligation> obligations = await _context.Obligations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<NoticeRequirement> requirements = await _context.NoticeRequirements
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var links = await _context.ContractTaskLinks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContractId))
            .Join(
                _context.Tasks.AsNoTracking().Where(t => t.State == TaskState.Open),
                link => link.TaskItemId,
                task => task.Id,
                (link, task) => new
                {
                    link.ContractId,
                    link.ObligationId,
                    link.ContractOptionId,
                    TaskId = task.Id.Value,
                    task.Title,
                    task.State,
                    task.Priority,
                    task.DueAt,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        DealId[] dealIds = [.. contracts.Select(x => x.DealId).Distinct()];

        List<Deal> deals = await _context.Deals
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && dealIds.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        OpportunityId[] opportunityIds = [.. deals.Select(x => x.OpportunityId).Distinct()];
        OpportunityTargetId[] targetIds = [.. deals.Select(x => x.OpportunityTargetId).Distinct()];

        List<OpportunityTarget> targets = await _context.OpportunityTargets
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && targetIds.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<OpportunitySubject> subjects = await _context.OpportunitySubjects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && opportunityIds.Contains(x.OpportunityId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Reconciliation needs both sides for every contract in the batch: what the
        // accepted offer said, and what the latest recorded version says.
        OfferId[] offerIds = [.. contracts.Select(x => x.AcceptedOfferId).Distinct()];

        List<OfferTerm> negotiated = await _context.OfferTerms
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && offerIds.Contains(x.OfferId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ContractVersionId[] latestVersionIds =
        [
            .. versions
                .GroupBy(x => x.ContractId)
                .Select(group => group.OrderBy(x => x.VersionNumber).Last().Id),
        ];

        List<ContractTerm> drafted = latestVersionIds.Length == 0
            ? []
            : await _context.ContractTerms
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId
                    && latestVersionIds.Contains(x.ContractVersionId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        PartyNames names = await LoadPartyNamesAsync(organizationId, parties, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> projects = await _context.Projects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> properties = await _context.SourceProperties
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> companies = await LoadCompaniesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> people = await LoadPeopleAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

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

        return new LegalContext(
            Today,
            contracts.ToDictionary(x => x.Id),
            parties.ToLookup(x => x.ContractId),
            signatures.ToLookup(x => x.ContractId),
            versions.ToLookup(x => x.ContractId),
            grants.ToLookup(x => x.ContractId),
            options.ToLookup(x => x.ContractId),
            obligations.ToLookup(x => x.ContractId),
            requirements.ToLookup(x => x.ContractId),
            [
                .. links.Select(x => (
                    x.ContractId,
                    Task: new ContractTaskModel(
                        x.TaskId,
                        x.Title,
                        x.State.ToString(),
                        x.Priority.ToString(),
                        x.DueAt,
                        x.ObligationId,
                        x.ContractOptionId))),
            ],
            deals.ToDictionary(x => x.Id),
            targets.ToDictionary(x => x.Id),
            subjects.ToLookup(x => x.OpportunityId),
            negotiated.ToLookup(x => x.OfferId),
            drafted.ToLookup(x => x.ContractVersionId),
            names,
            users,
            projects,
            properties,
            people,
            companies,
            talent);
    }

    /// <summary>Party display names, resolved once for a batch.</summary>
    /// <remarks>
    /// A party is either somebody AgencyOS knows or a name it was told. The joined
    /// record wins when there is one, because a free-text name that shadows a real
    /// company is how a party silently stops being the same party (ADR-0022).
    /// </remarks>
    private sealed record PartyNames(
        Dictionary<Guid, ContractParty> Parties,
        Dictionary<Guid, string> People,
        Dictionary<Guid, string> Companies)
    {
        public string Display(Guid partyId) =>
            Parties.TryGetValue(partyId, out ContractParty? party) ? Display(party) : "(unknown party)";

        public string Display(ContractParty party)
        {
            ArgumentNullException.ThrowIfNull(party);

            if (party.CompanyId is { } company)
            {
                return Companies.TryGetValue(company.Value, out string? name) ? name : "(unknown)";
            }

            if (party.PersonId is { } person)
            {
                return People.TryGetValue(person.Value, out string? name) ? name : "(unknown)";
            }

            return party.ExternalName ?? "(unnamed party)";
        }
    }

    /// <summary>Everything a batch of contracts needs, resolved once per request.</summary>
    private sealed record LegalContext(
        DateOnly Today,
        Dictionary<ContractId, Contract> Contracts,
        ILookup<ContractId, ContractParty> Parties,
        ILookup<ContractId, ContractSignature> Signatures,
        ILookup<ContractId, ContractVersion> Versions,
        ILookup<ContractId, RightsGrant> Grants,
        ILookup<ContractId, ContractOption> Options,
        ILookup<ContractId, Obligation> Obligations,
        ILookup<ContractId, NoticeRequirement> NoticeRequirements,
        IReadOnlyList<(ContractId ContractId, ContractTaskModel Task)> Tasks,
        Dictionary<DealId, Deal> Deals,
        Dictionary<OpportunityTargetId, OpportunityTarget> Targets,
        ILookup<OpportunityId, OpportunitySubject> Subjects,
        ILookup<OfferId, OfferTerm> NegotiatedTerms,
        ILookup<ContractVersionId, ContractTerm> DraftedTerms,
        PartyNames Names,
        Dictionary<Guid, string> Users,
        Dictionary<Guid, string> Projects,
        Dictionary<Guid, string> Properties,
        Dictionary<Guid, string> People,
        Dictionary<Guid, string> Companies,
        Dictionary<Guid, string> Talent)
    {
        public string DealName(DealId id) =>
            Deals.TryGetValue(id, out Deal? deal) ? deal.Name : "(unknown)";

        public string Counterparty(DealId id) =>
            Deals.TryGetValue(id, out Deal? deal)
                ? ContractQueries.Counterparty(Targets, deal.OpportunityTargetId, People, Companies)
                : "(unknown)";

        /// <summary>What the pursuit behind the contract is about.</summary>
        /// <remarks>
        /// Read through the M6 subject rather than copied onto the contract, on the
        /// M7 precedent. A copy would be a second answer that stops agreeing the
        /// moment somebody corrects the pursuit.
        /// </remarks>
        public string? Subject(DealId id)
        {
            if (!Deals.TryGetValue(id, out Deal? deal))
            {
                return null;
            }

            OpportunitySubject? subject = Subjects[deal.OpportunityId].FirstOrDefault();

            return subject?.Kind switch
            {
                OpportunitySubjectKind.TalentProfile when subject.TalentProfileId is { } profile =>
                    Talent.TryGetValue(profile.Value, out string? name) ? name : null,
                OpportunitySubjectKind.Project when subject.ProjectId is { } project =>
                    Projects.TryGetValue(project.Value, out string? title) ? title : null,
                _ => null,
            };
        }

        public bool IsAboutTalent(DealId id, Guid talentProfileId) =>
            Deals.TryGetValue(id, out Deal? deal)
            && Subjects[deal.OpportunityId]
                .Any(x => x.TalentProfileId is { } profile && profile.Value == talentProfileId);

        /// <summary>Whether any grant or option on this contract names the project.</summary>
        public bool TouchesProject(ContractId id, Guid projectId) =>
            Grants[id].Any(x => x.ProjectId is { } project && project.Value == projectId)
            || Options[id].Any(x => x.ProjectId is { } project && project.Value == projectId);

        public bool HasSigned(ContractId contractId, Guid partyId) =>
            Signatures[contractId].Any(x => x.ContractPartyId == partyId);

        public ContractSignature? SignatureFor(ContractId contractId, Guid partyId) =>
            Signatures[contractId].FirstOrDefault(x => x.ContractPartyId == partyId);

        public string? ProjectTitle(Guid? id) =>
            id is { } value && Projects.TryGetValue(value, out string? title) ? title : null;

        public string? SourcePropertyTitle(Guid? id) =>
            id is { } value && Properties.TryGetValue(value, out string? title) ? title : null;

        public IReadOnlyList<ContractTaskModel> TasksFor(ContractId id) =>
            [.. Tasks.Where(x => x.ContractId == id).Select(x => x.Task).OrderBy(x => x.DueAt)];

        /// <summary>
        /// The earliest date still ahead on this contract.
        /// </summary>
        /// <remarks>
        /// Unioned across options, obligations and notice requirements, and only
        /// over rows whose rule actually resolved. A contract whose every deadline
        /// hangs off an event that has not happened reports no next deadline, which
        /// is true (ADR-0022).
        /// </remarks>
        public LegalDeadlineModel? NextDeadline(ContractId id)
        {
            string title = Contracts.TryGetValue(id, out Contract? contract)
                ? contract.Title
                : "(unknown)";

            IEnumerable<(DateOnly Due, LegalDeadlineSource Source, Guid Id, string What)> dates =
            [
                .. Options[id]
                    .Where(x => x.Status == OptionStatus.Available && x.ResolvedDeadlineOn is not null)
                    .Select(x => (x.ResolvedDeadlineOn!.Value, LegalDeadlineSource.Option, x.Id.Value,
                        x.Subject)),

                .. Obligations[id]
                    .Where(x => x.Status == ObligationStatus.Pending && x.ResolvedDueOn is not null)
                    .Select(x => (x.ResolvedDueOn!.Value, LegalDeadlineSource.Obligation, x.Id.Value,
                        x.Description)),

                .. NoticeRequirements[id]
                    .Where(x => x.ResolvedDueOn is not null)
                    .Select(x => (x.ResolvedDueOn!.Value, LegalDeadlineSource.Notice, x.Id.Value,
                        x.Description)),
            ];

            var next = dates.Where(x => x.Due >= Today).OrderBy(x => x.Due).FirstOrDefault();

            return next.What is null
                ? null
                : Deadline(next.Source, next.Id, id, title, next.Due, next.What, Today);
        }

        /// <summary>
        /// How far the latest recorded version has drifted from what was agreed.
        /// </summary>
        /// <remarks>
        /// Recomputed from both sides every time, never stored. A count in a column
        /// would be right until the next version was recorded and wrong afterwards,
        /// and nobody would know which (ADR-0022).
        /// </remarks>
        public int? DifferenceCount(ContractId id)
        {
            if (!Contracts.TryGetValue(id, out Contract? contract))
            {
                return null;
            }

            ContractVersion? latest = Versions[id].OrderBy(x => x.VersionNumber).LastOrDefault();

            if (latest is null)
            {
                return null;
            }

            ReconciliationLine[] lines = DealRules.Reconcile(
                [
                    .. NegotiatedTerms[contract.AcceptedOfferId]
                        .OrderBy(x => x.Sequence)
                        .Select(x => x.ToRulesInput()),
                ],
                [.. DraftedTerms[latest.Id].OrderBy(x => x.Sequence).Select(x => x.ToRulesInput())]);

            return DealRules.ReconciliationDifferenceCount(lines);
        }
    }
}
