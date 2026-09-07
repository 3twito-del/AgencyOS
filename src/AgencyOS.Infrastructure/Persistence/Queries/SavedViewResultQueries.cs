using AgencyOS.Application.Directory;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Deals;
using AgencyOS.Application.Deals;
using AgencyOS.Application.Opportunities;
using AgencyOS.Application.Projects;
using AgencyOS.Application.Representations;
using AgencyOS.Application.SavedViews;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
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
    private readonly IProjectQueries _projects;
    private readonly IOpportunityQueries _opportunities;
    private readonly IDealQueries _deals;

    public SavedViewResultQueries(
        AgencyOsDbContext context,
        IRepresentationQueries representation,
        IProjectQueries projects,
        IOpportunityQueries opportunities,
        IDealQueries deals)
    {
        _context = context;
        _representation = representation;
        _projects = projects;
        _opportunities = opportunities;
        _deals = deals;
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

            SavedViewTarget.Projects =>
                await RunProjectsAsync(organizationId, definition, limit, cancellationToken)
                    .ConfigureAwait(false),

            SavedViewTarget.Packages =>
                await RunPackagesAsync(organizationId, definition, limit, cancellationToken)
                    .ConfigureAwait(false),

            SavedViewTarget.Opportunities =>
                await RunOpportunitiesAsync(organizationId, definition, now, limit, cancellationToken)
                    .ConfigureAwait(false),

            SavedViewTarget.Deals =>
                await RunDealsAsync(organizationId, definition, limit, cancellationToken)
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

        return SavedViewResultModel.OfPeople(
            [.. people.Select(person => ToSummary(person, companyNames))]);
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

        return SavedViewResultModel.OfCompanies([.. companies.Select(ToSummary)]);
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

        return SavedViewResultModel.OfTasks([.. tasks.Select(task => ToModel(task, names))]);
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

        return SavedViewResultModel.OfTalent(talent);
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

        return SavedViewResultModel.OfProspects(prospects);
    }

    /// <summary>
    /// Runs a project view by handing its filters to the project projection.
    /// </summary>
    /// <remarks>
    /// Reuses the query the project list already uses rather than writing a second
    /// one. Two implementations of "which projects match this" would eventually
    /// disagree, and the saved view would quietly become the wrong answer.
    /// </remarks>
    private async Task<SavedViewResultModel> RunProjectsAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        int limit,
        CancellationToken cancellationToken)
    {
        SavedViewFilters filters = definition.Filters;

        ProjectFilter filter = new(
            Parse<ProjectStatus>(filters.ProjectStatus),
            Parse<DevelopmentStage>(filters.DevelopmentStage),
            Parse<ProjectType>(filters.ProjectType),
            filters.LeadUserId,
            filters.AttachedPersonId,
            Parse<ProjectRoleType>(filters.MissingRoleType),
            filters.TextContains);

        IReadOnlyList<ProjectSummaryModel> projects = await _projects
            .ListProjectsAsync(organizationId, filter, limit, cancellationToken)
            .ConfigureAwait(false);

        return SavedViewResultModel.OfProjects(projects);
    }

    /// <summary>Runs a package view.</summary>
    private async Task<SavedViewResultModel> RunPackagesAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        int limit,
        CancellationToken cancellationToken)
    {
        SavedViewFilters filters = definition.Filters;

        PackageFilter filter = new(
            Parse<PackageStatus>(filters.PackageStatus),
            filters.ProjectId,
            filters.LeadUserId,
            filters.TextContains);

        IReadOnlyList<PackageSummaryModel> packages = await _projects
            .ListPackagesAsync(organizationId, filter, limit, cancellationToken)
            .ConfigureAwait(false);

        return SavedViewResultModel.OfPackages(packages);
    }

    /// <summary>Runs an opportunity view against the pursuit projection.</summary>
    /// <remarks>
    /// Reuses the query the pipeline list already uses. Two implementations of
    /// "which pursuits match this" would eventually disagree, and the saved view
    /// would quietly become the wrong answer.
    /// </remarks>
    private async Task<SavedViewResultModel> RunOpportunitiesAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        SavedViewFilters filters = definition.Filters;

        OpportunityFilter filter = new(
            Parse<OpportunityStatus>(filters.OpportunityStatus),
            Parse<OpportunityKind>(filters.OpportunityKind),
            filters.OwnerUserId,
            filters.TalentProfileId,
            filters.ProjectId,
            filters.PackageId,
            filters.TargetCompanyId,
            filters.TargetPersonId,
            Parse<OpportunityTargetStage>(filters.TargetStage),
            filters.HasSubmission,
            filters.AwaitingResponse,
            filters.FollowUpDueWithinDays is { } days
                ? DateOnly.FromDateTime(now.UtcDateTime).AddDays(days)
                : null,
            filters.TextContains);

        IReadOnlyList<OpportunitySummaryModel> opportunities = await _opportunities
            .ListOpportunitiesAsync(organizationId, filter, now, limit, cancellationToken)
            .ConfigureAwait(false);

        return SavedViewResultModel.OfOpportunities(opportunities);
    }

    /// <summary>
    /// Runs a deals view.
    /// </summary>
    /// <remarks>
    /// There is deliberately nothing economic to map. A saved view is a query
    /// somebody else may later run, and one that narrowed by a compensation figure
    /// would tell its reader that figure whether or not they hold
    /// <c>deals.economics.read</c>. The summaries this returns carry counts and
    /// dates only, so they are safe for any caller who may read the deal at all
    /// (ADR-0021).
    /// </remarks>
    private async Task<SavedViewResultModel> RunDealsAsync(
        OrganizationId organizationId,
        SavedViewDefinition definition,
        int limit,
        CancellationToken cancellationToken)
    {
        SavedViewFilters filters = definition.Filters;

        DealFilter filter = new(
            Parse<DealStatus>(filters.DealStatus),
            Parse<DealKind>(filters.DealKind),
            filters.OwnerUserId is { } owner ? new UserId(owner) : null,
            filters.OpportunityId is { } opportunity ? new OpportunityId(opportunity) : null,
            filters.OpportunityTargetId is { } target ? new OpportunityTargetId(target) : null,
            filters.CounterpartyCompanyId,
            filters.CounterpartyPersonId,
            filters.TalentProfileId,
            filters.ProjectId,
            filters.HasOpenOffer,
            filters.TermsAgreedOnly,
            filters.OpenedAfter,
            filters.OpenedBefore,
            filters.TextContains);

        IReadOnlyList<DealSummaryModel> deals = await _deals
            .ListDealsAsync(organizationId, filter, limit, cancellationToken)
            .ConfigureAwait(false);

        return SavedViewResultModel.OfDeals(deals);
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
