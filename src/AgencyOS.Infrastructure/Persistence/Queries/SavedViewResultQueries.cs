using AgencyOS.Application.Directory;
using AgencyOS.Application.Representations;
using AgencyOS.Application.SavedViews;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.SavedViews;
using AgencyOS.Domain.Talent;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using static AgencyOS.Infrastructure.Persistence.Queries.PeopleSliceProjection;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Turns a saved view's definition into a query.
/// </summary>
/// <remarks>
/// <para>
/// Every filter maps to one reviewed clause here. The definition is a closed set
/// of typed predicates rather than an expression language precisely so that this
/// file can be read in full and the complete set of things a saved view can ask
/// for can be seen at once.
/// </para>
/// <para>
/// Sorting is applied from an allow-list the domain validated, so a field name
/// arriving in a stored document can never reach the query as text.
/// </para>
/// </remarks>
internal sealed class SavedViewResultQueries : ISavedViewResultQueries
{
    private readonly AgencyOsDbContext _context;
    private readonly IRepresentationQueries _representation;

    public SavedViewResultQueries(AgencyOsDbContext context, IRepresentationQueries representation)
    {
        _context = context;
        _representation = representation;
    }

    public async Task<SavedViewResultModel> RunAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Validated again on the way out, not only on the way in. A document
        // stored before a format change must be refused rather than reinterpreted.
        definition.Validate();

        return definition.Target switch
        {
            SavedViewTarget.People =>
                await RunPeopleAsync(organizationId, definition, limit, cancellationToken).ConfigureAwait(false),

            SavedViewTarget.Companies =>
                await RunCompaniesAsync(organizationId, definition, limit, cancellationToken).ConfigureAwait(false),

            SavedViewTarget.Tasks =>
                await RunTasksAsync(organizationId, definition, now, limit, cancellationToken).ConfigureAwait(false),

            SavedViewTarget.Talent =>
                await RunTalentAsync(organizationId, definition, limit, cancellationToken).ConfigureAwait(false),

            SavedViewTarget.Prospects =>
                await RunProspectsAsync(organizationId, definition, now, limit, cancellationToken)
                    .ConfigureAwait(false),

            _ => SavedViewResultModel.Empty(definition.Target),
        };
    }

    private async Task<SavedViewResultModel> RunPeopleAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        int limit,
        CancellationToken cancellationToken)
    {
        SavedViewFilters filters = definition.Filters;

        IQueryable<Person> query = _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (Parse<PersonStatus>(filters.Status) is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filters.CompanyId is { } companyId)
        {
            CompanyId typed = new(companyId);
            query = query.Where(x => x.PrimaryCompanyId == typed);
        }

        if (filters.TitleContains is { Length: > 0 } title)
        {
            string pattern = $"%{Escape(title)}%";
            query = query.Where(x => x.Title != null && EF.Functions.ILike(x.Title, pattern, "\\"));
        }

        if (filters.TextContains is { Length: > 0 } text)
        {
            string pattern = $"%{Escape(text)}%";
            query = query.Where(x => EF.Functions.ILike(x.DisplayName, pattern, "\\"));
        }

        query = definition.Sort switch
        {
            { Field: "UpdatedAt", Direction: SavedViewSortDirection.Descending } =>
                query.OrderByDescending(x => x.UpdatedAt),
            { Field: "UpdatedAt" } => query.OrderBy(x => x.UpdatedAt),
            { Field: "CreatedAt", Direction: SavedViewSortDirection.Descending } =>
                query.OrderByDescending(x => x.CreatedAt),
            { Field: "CreatedAt" } => query.OrderBy(x => x.CreatedAt),
            { Field: "DisplayName", Direction: SavedViewSortDirection.Descending } =>
                query.OrderByDescending(x => x.DisplayName),
            _ => query.OrderBy(x => x.DisplayName),
        };

        List<Person> people = await query
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> companyNames = await LoadCompanyNamesAsync(
            organizationId,
            people.Where(x => x.PrimaryCompanyId.HasValue).Select(x => x.PrimaryCompanyId!.Value),
            cancellationToken).ConfigureAwait(false);

        return new SavedViewResultModel(
            SavedViewTarget.People,
            [.. people.Select(person => ToSummary(person, companyNames))],
            [],
            [],
            [],
            []);
    }

    private async Task<SavedViewResultModel> RunCompaniesAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        int limit,
        CancellationToken cancellationToken)
    {
        SavedViewFilters filters = definition.Filters;

        IQueryable<Company> query = _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (Parse<CompanyStatus>(filters.Status) is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filters.TextContains is { Length: > 0 } text)
        {
            string pattern = $"%{Escape(text)}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\"));
        }

        query = definition.Sort switch
        {
            { Field: "UpdatedAt", Direction: SavedViewSortDirection.Descending } =>
                query.OrderByDescending(x => x.UpdatedAt),
            { Field: "UpdatedAt" } => query.OrderBy(x => x.UpdatedAt),
            { Field: "CreatedAt", Direction: SavedViewSortDirection.Descending } =>
                query.OrderByDescending(x => x.CreatedAt),
            { Field: "CreatedAt" } => query.OrderBy(x => x.CreatedAt),
            { Field: "Name", Direction: SavedViewSortDirection.Descending } =>
                query.OrderByDescending(x => x.Name),
            _ => query.OrderBy(x => x.Name),
        };

        List<Company> companies = await query
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SavedViewResultModel(
            SavedViewTarget.Companies,
            [],
            [.. companies.Select(ToSummary)],
            [],
            [],
            []);
    }

    private async Task<SavedViewResultModel> RunTasksAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        SavedViewFilters filters = definition.Filters;

        IQueryable<TaskItem> query = _context.Tasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (Parse<TaskState>(filters.TaskState) is { } state)
        {
            query = query.Where(x => x.State == state);
        }

        if (filters.TextContains is { Length: > 0 } text)
        {
            string pattern = $"%{Escape(text)}%";
            query = query.Where(x => EF.Functions.ILike(x.Title, pattern, "\\"));
        }

        if (filters.OverdueOnly)
        {
            // Overdue means open and past due. A completed task that was once late
            // is not something that needs attention now.
            query = query.Where(x => x.DueAt != null && x.DueAt < now && x.State == TaskState.Open);
        }

        if (filters.DueWithinDays is { } days)
        {
            DateTimeOffset horizon = now.AddDays(days);
            query = query.Where(x => x.DueAt != null && x.DueAt <= horizon);
        }

        query = definition.Sort switch
        {
            { Field: "CreatedAt", Direction: SavedViewSortDirection.Descending } =>
                query.OrderByDescending(x => x.CreatedAt),
            { Field: "CreatedAt" } => query.OrderBy(x => x.CreatedAt),
            { Field: "Priority", Direction: SavedViewSortDirection.Descending } =>
                query.OrderByDescending(x => x.Priority),
            { Field: "Priority" } => query.OrderBy(x => x.Priority),
            { Field: "Title", Direction: SavedViewSortDirection.Descending } =>
                query.OrderByDescending(x => x.Title),
            { Field: "Title" } => query.OrderBy(x => x.Title),
            { Direction: SavedViewSortDirection.Descending } => query.OrderByDescending(x => x.DueAt),

            // Undated tasks last by default: a list of what is due should not open
            // with everything that has no date at all.
            _ => query.OrderBy(x => x.DueAt == null).ThenBy(x => x.DueAt),
        };

        List<TaskItem> tasks = await query
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        PartyNameLookup names = await LoadPartyNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        return new SavedViewResultModel(
            SavedViewTarget.Tasks,
            [],
            [],
            [.. tasks.Select(task => ToModel(task, names))],
            [],
            []);
    }

    /// <summary>
    /// Runs a talent view by handing its filters to the representation projection.
    /// </summary>
    /// <remarks>
    /// Reuses the query the talent list already uses rather than writing a second
    /// one. Two implementations of "which clients match this" would eventually
    /// disagree, and the saved view would quietly become the wrong answer.
    /// </remarks>
    private async Task<SavedViewResultModel> RunTalentAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        int limit,
        CancellationToken cancellationToken)
    {
        SavedViewFilters filters = definition.Filters;

        TalentFilter filter = new(
            filters.ClientsOnly,
            filters.FormerClientsOnly,
            Parse<ProfessionalDiscipline>(filters.Discipline),
            Parse<RepresentationScopeArea>(filters.ScopeArea),
            filters.LeadUserId,
            filters.TextContains);

        IReadOnlyList<TalentSummaryModel> talent = await _representation
            .ListTalentAsync(organizationId, filter, limit, cancellationToken)
            .ConfigureAwait(false);

        return new SavedViewResultModel(SavedViewTarget.Talent, [], [], [], talent, []);
    }

    /// <summary>Runs a prospect view.</summary>
    /// <remarks>
    /// An explicit stage relaxes the open-only default: somebody asking for
    /// declined prospects plainly wants the closed ones.
    /// </remarks>
    private async Task<SavedViewResultModel> RunProspectsAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        SavedViewFilters filters = definition.Filters;

        DateOnly? due = filters.FollowUpWithinDays is { } days
            ? DateOnly.FromDateTime(now.UtcDateTime).AddDays(days)
            : null;

        ProspectFilter filter = new(
            OpenOnly: filters.ProspectStage is null,
            Parse<ProspectStage>(filters.ProspectStage),
            filters.OwnerUserId,
            due);

        IReadOnlyList<ProspectModel> prospects = await _representation
            .ListProspectsAsync(organizationId, filter, limit, cancellationToken)
            .ConfigureAwait(false);

        return new SavedViewResultModel(SavedViewTarget.Prospects, [], [], [], [], prospects);
    }

    private async Task<Dictionary<Guid, string>> LoadCompanyNamesAsync(
        OrganizationId organizationId,
        IEnumerable<CompanyId> companyIds,
        CancellationToken cancellationToken)
    {
        List<CompanyId> ids = [.. companyIds.Distinct()];

        if (ids.Count == 0)
        {
            return [];
        }

        return await _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
            .Select(x => new { Id = x.Id.Value, x.Name })
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

    /// <summary>Parses a stored status name, treating an unknown one as no filter.</summary>
    /// <remarks>
    /// The domain validates status values on the way in, so this cannot normally
    /// fail. Ignoring an unparseable value rather than throwing keeps an old saved
    /// view usable if a status is ever renamed, which is the friendlier failure.
    /// </remarks>
    private static T? Parse<T>(string? value)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out T parsed) ? parsed : null;

    /// <summary>Escapes a user string so <c>%</c> and <c>_</c> are text, not wildcards.</summary>
    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
