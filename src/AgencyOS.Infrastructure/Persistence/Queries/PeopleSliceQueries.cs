using AgencyOS.Application.Directory;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using static AgencyOS.Infrastructure.Persistence.Queries.PeopleSliceProjection;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for the people slice.
/// </summary>
/// <remarks>
/// <para>
/// Every query is tenant-filtered and untracked. Authorization is applied above,
/// in <c>PeopleSliceQueryService</c>; this type is not registered for direct use
/// by the API.
/// </para>
/// <para>
/// Party names are resolved through per-request dictionaries rather than through
/// joins across the exclusive arc. At one agency's data volumes this is both
/// faster to read and far easier to verify than four conditional joins, and it
/// keeps the arc's shape out of every query.
/// </para>
/// </remarks>
internal sealed class PeopleSliceQueries : IPeopleSliceQueries
{
    /// <summary>How far ahead "due soon" looks on the Command Center.</summary>
    private static readonly TimeSpan DueSoonHorizon = TimeSpan.FromDays(7);

    /// <summary>
    /// Audit actions surfaced on a timeline as record changes.
    /// </summary>
    /// <remarks>
    /// An explicit allow-list, not a filter over everything. The timeline is a
    /// product surface and the audit trail is a security record; deciding here
    /// which changes are worth showing keeps the two from drifting into each other
    /// (ADR-0012).
    /// </remarks>
    private static readonly string[] TimelineRecordChangeActions =
    [
        AuditAction.PersonUpdated,
        AuditAction.CompanyUpdated,
    ];

    private readonly AgencyOsDbContext _context;

    public PeopleSliceQueries(AgencyOsDbContext context) => _context = context;

    // ------------------------------------------------------------ directory

    public async Task<IReadOnlyList<PersonSummaryModel>> ListPeopleAsync(
        OrganizationId organizationId,
        string? search,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Person> query = _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (!includeArchived)
        {
            query = query.Where(x => x.Status == PersonStatus.Active);
        }

        if (search is not null)
        {
            string pattern = $"%{search}%";

            query = query.Where(x =>
                EF.Functions.ILike(x.DisplayName, pattern)
                || EF.Functions.ILike(x.FirstName, pattern)
                || (x.LastName != null && EF.Functions.ILike(x.LastName, pattern))
                || (x.Email != null && EF.Functions.ILike(x.Email, pattern))
                || (x.Title != null && EF.Functions.ILike(x.Title, pattern)));
        }

        List<Person> people = await query
            .OrderBy(x => x.DisplayName)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> companyNames = await LoadCompanyNamesAsync(
            organizationId,
            people.Where(p => p.PrimaryCompanyId.HasValue).Select(p => p.PrimaryCompanyId!.Value.Value),
            cancellationToken).ConfigureAwait(false);

        return [.. people.Select(person => ToSummary(person, companyNames))];
    }

    public async Task<PersonDetailModel?> GetPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default)
    {
        Person? person = await _context.People
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == personId && x.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (person is null)
        {
            return null;
        }

        List<ProfessionalRelationship> relationships = await _context.Relationships
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && (x.FromPersonId == personId || x.ToPersonId == personId))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PartyNameLookup names = await LoadPartyNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> companyNames = names.Companies;

        return new PersonDetailModel(
            ToSummary(person, companyNames),
            person.FirstName,
            person.MiddleName,
            person.LastName,
            person.PreferredName,
            person.Notes,
            person.CreatedAt,
            [.. relationships.Select(r => ToModel(r, names))]);
    }

    public async Task<IReadOnlyList<CompanySummaryModel>> ListCompaniesAsync(
        OrganizationId organizationId,
        string? search,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Company> query = _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (!includeArchived)
        {
            query = query.Where(x => x.Status == CompanyStatus.Active);
        }

        if (search is not null)
        {
            string pattern = $"%{search}%";

            query = query.Where(x =>
                EF.Functions.ILike(x.Name, pattern)
                || (x.LegalName != null && EF.Functions.ILike(x.LegalName, pattern)));
        }

        List<Company> companies = await query
            .OrderBy(x => x.Name)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. companies.Select(ToSummary)];
    }

    public async Task<CompanyDetailModel?> GetCompanyAsync(
        OrganizationId organizationId,
        CompanyId companyId,
        CancellationToken cancellationToken = default)
    {
        Company? company = await _context.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == companyId && x.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (company is null)
        {
            return null;
        }

        List<ProfessionalRelationship> relationships = await _context.Relationships
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && (x.FromCompanyId == companyId || x.ToCompanyId == companyId))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<Person> staff = await _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.PrimaryCompanyId == companyId
                && x.Status == PersonStatus.Active)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PartyNameLookup names = await LoadPartyNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return new CompanyDetailModel(
            ToSummary(company),
            company.Notes,
            company.CreatedAt,
            [.. relationships.Select(r => ToModel(r, names))],
            [.. staff.Select(p => ToSummary(p, names.Companies))]);
    }

    // ------------------------------------------------------------- timeline

    public async Task<IReadOnlyList<TimelineEntryModel>?> GetPersonTimelineAsync(
        OrganizationId organizationId,
        PersonId personId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        bool exists = await _context.People
            .AsNoTracking()
            .AnyAsync(x => x.Id == personId && x.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return null;
        }

        return await ComposeTimelineAsync(
            organizationId,
            RelationshipEndpoint.ForPerson(personId),
            nameof(Person),
            personId.Value,
            limit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TimelineEntryModel>?> GetCompanyTimelineAsync(
        OrganizationId organizationId,
        CompanyId companyId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        bool exists = await _context.Companies
            .AsNoTracking()
            .AnyAsync(x => x.Id == companyId && x.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return null;
        }

        return await ComposeTimelineAsync(
            organizationId,
            RelationshipEndpoint.ForCompany(companyId),
            nameof(Company),
            companyId.Value,
            limit,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a party's timeline from the records that describe it.
    /// </summary>
    /// <remarks>
    /// Composed at read time from four sources rather than maintained as a table,
    /// so it cannot drift from the records it describes (ADR-0012).
    /// </remarks>
    private async Task<IReadOnlyList<TimelineEntryModel>> ComposeTimelineAsync(
        OrganizationId organizationId,
        RelationshipEndpoint party,
        string entityType,
        Guid entityId,
        int limit,
        CancellationToken cancellationToken)
    {
        PersonId? personId = party.AsPerson;
        CompanyId? companyId = party.AsCompany;

        PartyNameLookup names = await LoadPartyNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        List<TimelineEntryModel> entries = [];

        // 1. Interactions the party took part in.
        List<Interaction> interactions = await _context.Interactions
            .AsNoTracking()
            .Include(x => x.Participants)
            .Where(x => x.OrganizationId == organizationId
                && x.Participants.Any(p =>
                    (personId != null && p.PersonId == personId)
                    || (companyId != null && p.CompanyId == companyId)))
            .OrderByDescending(x => x.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (Interaction interaction in interactions)
        {
            string others = string.Join(
                ", ",
                interaction.Participants
                    .Where(p => p.Party != party)
                    .Select(p => names.Describe(p.Party)));

            entries.Add(new TimelineEntryModel(
                interaction.OccurredAt,
                "interaction",
                interaction.Summary,
                string.IsNullOrEmpty(others) ? interaction.Type.ToString() : $"{interaction.Type} with {others}",
                interaction.Id.Value));
        }

        // 2. Relationships beginning and ending.
        List<ProfessionalRelationship> relationships = await _context.Relationships
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && ((personId != null && (x.FromPersonId == personId || x.ToPersonId == personId))
                    || (companyId != null && (x.FromCompanyId == companyId || x.ToCompanyId == companyId))))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (ProfessionalRelationship relationship in relationships)
        {
            RelationshipEndpoint other = relationship.From == party ? relationship.To : relationship.From;

            entries.Add(new TimelineEntryModel(
                relationship.StartedAt ?? relationship.CreatedAt,
                "relationship.created",
                $"{relationship.Type} relationship with {names.Describe(other)}",
                relationship.Notes,
                relationship.Id.Value));

            if (relationship.EndedAt is { } endedAt)
            {
                entries.Add(new TimelineEntryModel(
                    endedAt,
                    "relationship.ended",
                    $"{relationship.Type} relationship with {names.Describe(other)} ended",
                    null,
                    relationship.Id.Value));
            }
        }

        // 3. Tasks concerning the party.
        List<TaskItem> tasks = await _context.Tasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && ((personId != null && x.RelatedPersonId == personId)
                    || (companyId != null && x.RelatedCompanyId == companyId)))
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (TaskItem task in tasks)
        {
            entries.Add(new TimelineEntryModel(
                task.CreatedAt,
                "task.created",
                task.Title,
                task.DueAt is { } due ? $"Due {due:yyyy-MM-dd}" : null,
                task.Id.Value));

            if (task.CompletedAt is { } completedAt)
            {
                entries.Add(new TimelineEntryModel(
                    completedAt,
                    "task.completed",
                    $"Completed: {task.Title}",
                    null,
                    task.Id.Value));
            }
        }

        // 4. Record changes, from the allow-listed audit actions only.
        string entityKey = entityId.ToString();

        List<AuditEvent> changes = await _context.AuditEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EntityType == entityType
                && x.EntityId == entityKey
                && TimelineRecordChangeActions.Contains(x.Action))
            .OrderByDescending(x => x.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (AuditEvent change in changes)
        {
            entries.Add(new TimelineEntryModel(
                change.OccurredAt,
                "record.updated",
                $"{entityType} details updated",
                null,
                entityId));
        }

        return [.. entries.OrderByDescending(e => e.OccurredAt).ThenBy(e => e.Kind, StringComparer.Ordinal).Take(limit)];
    }

    // ------------------------------------------------------- command centre

    public async Task<CommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        List<TaskItem> openTasks = await _context.Tasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.State == TaskState.Open)
            .OrderBy(x => x.DueAt)
            .ThenByDescending(x => x.Priority)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PartyNameLookup names = await LoadPartyNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        DateTimeOffset horizon = now.Add(DueSoonHorizon);

        // Three disjoint buckets, so a task appears exactly once and the counts add up.
        List<TaskModel> overdue =
        [
            .. openTasks.Where(t => t.DueAt is { } due && due < now)
                .OrderBy(t => t.DueAt)
                .Select(t => ToModel(t, names)),
        ];

        List<TaskModel> dueSoon =
        [
            .. openTasks.Where(t => t.DueAt is { } due && due >= now && due <= horizon)
                .OrderBy(t => t.DueAt)
                .Select(t => ToModel(t, names)),
        ];

        List<TaskModel> unscheduled =
        [
            .. openTasks.Where(t => t.DueAt is null)
                .OrderByDescending(t => t.Priority)
                .ThenByDescending(t => t.CreatedAt)
                .Select(t => ToModel(t, names)),
        ];

        List<Interaction> recent = await _context.Interactions
            .AsNoTracking()
            .Include(x => x.Participants)
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int peopleCount = await _context.People
            .AsNoTracking()
            .CountAsync(x => x.OrganizationId == organizationId && x.Status == PersonStatus.Active, cancellationToken)
            .ConfigureAwait(false);

        int companyCount = await _context.Companies
            .AsNoTracking()
            .CountAsync(x => x.OrganizationId == organizationId && x.Status == CompanyStatus.Active, cancellationToken)
            .ConfigureAwait(false);

        return new CommandCenterModel(
            overdue,
            dueSoon,
            unscheduled,
            [.. recent.Select(i => ToModel(i, names))],
            openTasks.Count,
            peopleCount,
            companyCount);
    }

    public async Task<IReadOnlyList<TaskModel>> ListTasksAsync(
        OrganizationId organizationId,
        bool openOnly,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TaskItem> query = _context.Tasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (openOnly)
        {
            query = query.Where(x => x.State == TaskState.Open);
        }

        List<TaskItem> tasks = await query
            .OrderBy(x => x.State)
            .ThenBy(x => x.DueAt)
            .ThenByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PartyNameLookup names = await LoadPartyNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return [.. tasks.Select(t => ToModel(t, names))];
    }

    // --------------------------------------------------------------- naming

    private async Task<Dictionary<Guid, string>> LoadCompanyNamesAsync(
        OrganizationId organizationId,
        IEnumerable<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. companyIds.Distinct()];

        if (ids.Count == 0)
        {
            return [];
        }

        return await _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Name })
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            .ConfigureAwait(false);
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
}
