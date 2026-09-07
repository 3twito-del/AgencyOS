using AgencyOS.Application.Projects;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for the project model.
/// </summary>
/// <remarks>
/// <para>
/// Names are resolved by loading the tenant's people, companies and users once per
/// request rather than joining through five relationships on every row. That is
/// the same trade the directory and representation projections make: at one
/// agency's volume it is faster, and far easier to verify, than a query nobody can
/// read.
/// </para>
/// <para>
/// Deliberately unauthorized. <c>ProjectQueryService</c> applies the tenant-scoped
/// permission checks and the strategy redaction, so both live in one place.
/// </para>
/// </remarks>
internal sealed class ProjectQueries : IProjectQueries
{
    private readonly AgencyOsDbContext _context;

    public ProjectQueries(AgencyOsDbContext context) => _context = context;

    public async Task<IReadOnlyList<ProjectSummaryModel>> ListProjectsAsync(
        OrganizationId organizationId,
        ProjectFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Project> query = _context.Projects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Stage is { } stage)
        {
            query = query.Where(x => x.Stage == stage);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(x => x.Type == type);
        }

        if (filter.LeadUserId is { } lead)
        {
            // Compared as the converted type. Reaching through .Value.Value on a
            // nullable value-converted key is untranslatable, and fails at runtime
            // rather than at compile time.
            UserId leadKey = new(lead);

            query = query.Where(x => x.LeadUserId == leadKey);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string pattern = $"%{filter.Search.Trim()}%";

            query = query.Where(x =>
                EF.Functions.ILike(x.Title, pattern)
                || (x.WorkingTitle != null && EF.Functions.ILike(x.WorkingTitle, pattern)));
        }

        if (filter.AttachedPersonId is { } attached)
        {
            // Currently holding, not merely ever attached: the useful question is
            // who is on this now.
            PersonId attachedKey = new(attached);

            query = query.Where(x => _context.Attachments.Any(a =>
                a.ProjectId == x.Id
                && a.PersonId == attachedKey
                && (a.Status == AttachmentStatus.Attached || a.Status == AttachmentStatus.Conditional)));
        }

        if (filter.MissingRoleType is { } missing)
        {
            // "Missing a director" means nobody currently holds a directing role -
            // whether or not such a role row exists at all. A project that never
            // created the role is missing one just as much as a project whose
            // director walked away, and a rule that only looked at open role rows
            // would quietly exclude the first case.
            query = query.Where(x => !_context.Attachments.Any(a =>
                a.ProjectId == x.Id
                && (a.Status == AttachmentStatus.Attached || a.Status == AttachmentStatus.Conditional)
                && _context.ProjectRoles.Any(r => r.Id == a.ProjectRoleId && r.Type == missing)));
        }

        List<Project> projects = await query
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToSummariesAsync(organizationId, projects, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectDetailModel?> GetProjectAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        Project? project = await _context.Projects
            .AsNoTracking()
            .Include(x => x.Roles)
            .Include(x => x.CompanyParticipations)
            .Include(x => x.SourceProperties)
            .Include(x => x.Materials)
            .FirstOrDefaultAsync(
                x => x.Id == projectId && x.OrganizationId == organizationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (project is null)
        {
            return null;
        }

        ProjectNames names = await LoadNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        ProjectSummaryModel summary =
            (await ToSummariesAsync(organizationId, [project], cancellationToken).ConfigureAwait(false))[0];

        List<Attachment> attachments = await _context.Attachments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.StartsOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, List<AttachmentModel>> byRole = [];

        foreach (Attachment attachment in attachments)
        {
            ProjectRole? role = project.Roles.FirstOrDefault(r => r.Id == attachment.ProjectRoleId);

            if (!byRole.TryGetValue(attachment.ProjectRoleId.Value, out List<AttachmentModel>? list))
            {
                byRole[attachment.ProjectRoleId.Value] = list = [];
            }

            list.Add(ToModel(attachment, role, names));
        }

        List<ProjectRoleModel> roles =
        [
            .. project.Roles
                .OrderBy(x => x.Type)
                .ThenBy(x => x.Label)
                .Select(role => new ProjectRoleModel(
                    role.Id.Value,
                    role.Type.ToString(),
                    role.Label,
                    role.Status.ToString(),
                    role.IsExclusive,
                    role.Notes,
                    byRole.TryGetValue(role.Id.Value, out List<AttachmentModel>? held) ? held : [])),
        ];

        List<ProjectCompanyModel> companies =
        [
            .. project.CompanyParticipations
                .OrderBy(x => x.EndsOn.HasValue)
                .ThenBy(x => x.Capacity)
                .Select(x => new ProjectCompanyModel(
                    x.Id,
                    x.CompanyId.Value,
                    names.Company(x.CompanyId.Value),
                    x.Capacity.ToString(),
                    x.StartsOn,
                    x.EndsOn,
                    x.Notes)),
        ];

        List<Guid> sourceIds = [.. project.SourceProperties.Select(x => x.SourcePropertyId.Value)];

        List<SourcePropertyModel> sources = sourceIds.Count == 0
            ? []
            : await ListSourcePropertiesByIdAsync(organizationId, sourceIds, cancellationToken)
                .ConfigureAwait(false);

        List<ProjectMaterialModel> materials =
            await LoadProjectMaterialsAsync(organizationId, project, names, cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<PackageSummaryModel> packages = await ListPackagesAsync(
            organizationId,
            new PackageFilter(ProjectId: projectId.Value),
            limit: 50,
            cancellationToken).ConfigureAwait(false);

        return new ProjectDetailModel(
            summary,
            project.Logline,
            project.Synopsis,
            project.Notes,
            roles,
            companies,
            sources,
            materials,
            packages,
            project.CreatedAt);
    }

    public async Task<IReadOnlyList<AttachmentModel>> ListAttachmentsAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        bool currentOnly,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Attachment> query = _context.Attachments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OrganizationId == organizationId);

        if (currentOnly)
        {
            query = query.Where(x =>
                x.Status != AttachmentStatus.Ended && x.Status != AttachmentStatus.Withdrawn);
        }

        List<Attachment> attachments = await query
            .OrderByDescending(x => x.StartsOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (attachments.Count == 0)
        {
            return [];
        }

        ProjectNames names = await LoadNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, ProjectRole> roles = await _context.ProjectRoles
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OrganizationId == organizationId)
            .ToDictionaryAsync(x => x.Id.Value, x => x, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. attachments.Select(x => ToModel(
                x,
                roles.TryGetValue(x.ProjectRoleId.Value, out ProjectRole? role) ? role : null,
                names)),
        ];
    }

    /// <summary>
    /// The curated project history.
    /// </summary>
    /// <remarks>
    /// Composed from domain events and effective-dated rows, never from the audit
    /// trail. The audit trail answers a security question and includes reads,
    /// permissions and client identity; a project timeline answers "what happened
    /// to this" for the people working it (ADR-0012).
    /// </remarks>
    public async Task<IReadOnlyList<ProjectHistoryEntryModel>> GetProjectHistoryAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        bool exists = await _context.Projects
            .AnyAsync(x => x.Id == projectId && x.OrganizationId == organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return [];
        }

        ProjectNames names = await LoadNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        List<ProjectHistoryEntryModel> entries = [];

        List<ProjectEvent> projectEvents = await _context.ProjectEvents
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (ProjectEvent entry in projectEvents)
        {
            string summary = entry.Kind switch
            {
                ProjectChangeKind.Created => "Project created",
                ProjectChangeKind.StatusChanged =>
                    $"Status changed from {entry.FromStatus} to {entry.ToStatus}",
                ProjectChangeKind.StageChanged =>
                    $"Stage moved from {entry.FromStage} to {entry.ToStage}",
                _ => "Project changed",
            };

            entries.Add(new ProjectHistoryEntryModel(
                entry.RecordedAt,
                entry.Kind.ToString(),
                summary,
                entry.Reason,
                names.User(entry.RecordedBy.Value)));
        }

        List<Guid> attachmentIds = await _context.Attachments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OrganizationId == organizationId)
            .Select(x => x.Id.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (attachmentIds.Count > 0)
        {
            List<AttachmentId> keys = [.. attachmentIds.Select(x => new AttachmentId(x))];

            var attachmentEvents = await _context.AttachmentEvents
                .AsNoTracking()
                .Where(x => keys.Contains(x.AttachmentId) && x.OrganizationId == organizationId)
                .OrderByDescending(x => x.RecordedAt)
                .Take(limit)
                .Join(
                    _context.Attachments.AsNoTracking(),
                    e => e.AttachmentId,
                    a => a.Id,
                    (e, a) => new { Event = e, Attachment = a })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var pair in attachmentEvents)
            {
                string who = names.Party(pair.Attachment.PersonId?.Value, pair.Attachment.CompanyId?.Value);

                string summary = pair.Event.FromStatus is null
                    ? $"{who} recorded as {pair.Event.ToStatus}"
                    : $"{who} moved from {pair.Event.FromStatus} to {pair.Event.ToStatus}";

                entries.Add(new ProjectHistoryEntryModel(
                    pair.Event.RecordedAt,
                    "Attachment",
                    summary,
                    pair.Event.Reason,
                    names.User(pair.Event.RecordedBy.Value)));
            }
        }

        List<ProjectCompanyParticipation> participations = await _context.ProjectCompanyParticipations
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (ProjectCompanyParticipation participation in participations)
        {
            entries.Add(new ProjectHistoryEntryModel(
                participation.CreatedAt,
                "Company",
                $"{names.Company(participation.CompanyId.Value)} recorded as {participation.Capacity}",
                participation.Notes,
                ActorDisplayName: null));
        }

        List<PackageEvent> packageEvents = await _context.PackageEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && _context.Packages.Any(p => p.Id == x.PackageId && p.ProjectId == projectId))
            .OrderByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (PackageEvent entry in packageEvents)
        {
            entries.Add(new ProjectHistoryEntryModel(
                entry.RecordedAt,
                "Package",
                entry.FromStatus is null
                    ? $"Package started as {entry.ToStatus}"
                    : $"Package moved from {entry.FromStatus} to {entry.ToStatus}",
                entry.Reason,
                names.User(entry.RecordedBy.Value)));
        }

        return [.. entries.OrderByDescending(x => x.OccurredAt).Take(limit)];
    }

    public async Task<IReadOnlyList<SourcePropertyModel>> ListSourcePropertiesAsync(
        OrganizationId organizationId,
        SourcePropertyFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<SourceProperty> query = _context.SourceProperties
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (filter.Type is { } type)
        {
            query = query.Where(x => x.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string pattern = $"%{filter.Search.Trim()}%";

            query = query.Where(x =>
                EF.Functions.ILike(x.Title, pattern)
                || (x.AttributedCreator != null && EF.Functions.ILike(x.AttributedCreator, pattern)));
        }

        List<SourceProperty> properties = await query
            .OrderBy(x => x.Title)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToSourceModelsAsync(organizationId, properties, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SourcePropertyModel?> GetSourcePropertyAsync(
        OrganizationId organizationId,
        SourcePropertyId id,
        CancellationToken cancellationToken = default)
    {
        SourceProperty? property = await _context.SourceProperties
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id && x.OrganizationId == organizationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (property is null)
        {
            return null;
        }

        IReadOnlyList<SourcePropertyModel> models =
            await ToSourceModelsAsync(organizationId, [property], cancellationToken).ConfigureAwait(false);

        return models[0];
    }

    public async Task<IReadOnlyList<PackageSummaryModel>> ListPackagesAsync(
        OrganizationId organizationId,
        PackageFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Package> query = _context.Packages
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.ProjectId is { } projectId)
        {
            query = query.Where(x => x.ProjectId == new ProjectId(projectId));
        }

        if (filter.LeadUserId is { } lead)
        {
            UserId leadKey = new(lead);

            query = query.Where(x => x.LeadUserId == leadKey);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string pattern = $"%{filter.Search.Trim()}%";

            query = query.Where(x => EF.Functions.ILike(x.Name, pattern));
        }

        List<Package> packages = await query
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (packages.Count == 0)
        {
            return [];
        }

        ProjectNames names = await LoadNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        List<ProjectId> projectKeys = [.. packages.Select(x => x.ProjectId).Distinct()];

        Dictionary<Guid, string> titles = await _context.Projects
            .AsNoTracking()
            .Where(x => projectKeys.Contains(x.Id) && x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken)
            .ConfigureAwait(false);

        List<PackageId> packageKeys = [.. packages.Select(x => x.Id)];

        Dictionary<Guid, int> counts = await _context.PackageElements
            .AsNoTracking()
            .Where(x => packageKeys.Contains(x.PackageId))
            .GroupBy(x => x.PackageId)
            .Select(g => new { PackageId = g.Key.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.PackageId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, int> openRoles = await _context.ProjectRoles
            .AsNoTracking()
            .Where(x => projectKeys.Contains(x.ProjectId) && x.Status == ProjectRoleStatus.Open)
            .GroupBy(x => x.ProjectId)
            .Select(g => new { ProjectId = g.Key.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. packages.Select(x => new PackageSummaryModel(
                x.Id.Value,
                x.ProjectId.Value,
                titles.TryGetValue(x.ProjectId.Value, out string? title) ? title : "(unknown)",
                x.Name,
                x.Status.ToString(),
                x.LeadUserId.Value,
                names.User(x.LeadUserId.Value),
                counts.TryGetValue(x.Id.Value, out int count) ? count : 0,
                openRoles.TryGetValue(x.ProjectId.Value, out int open) ? open : 0,
                x.UpdatedAt,
                x.Version)),
        ];
    }

    public async Task<PackageDetailModel?> GetPackageAsync(
        OrganizationId organizationId,
        PackageId id,
        CancellationToken cancellationToken = default)
    {
        Package? package = await _context.Packages
            .AsNoTracking()
            .Include(x => x.Elements)
            .FirstOrDefaultAsync(
                x => x.Id == id && x.OrganizationId == organizationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (package is null)
        {
            return null;
        }

        IReadOnlyList<PackageSummaryModel> summaries = await ListPackagesAsync(
            organizationId,
            new PackageFilter(ProjectId: package.ProjectId.Value),
            limit: 200,
            cancellationToken).ConfigureAwait(false);

        PackageSummaryModel summary = summaries.First(x => x.Id == package.Id.Value);

        ProjectNames names = await LoadNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, ProjectRole> roles = await _context.ProjectRoles
            .AsNoTracking()
            .Where(x => x.ProjectId == package.ProjectId && x.OrganizationId == organizationId)
            .ToDictionaryAsync(x => x.Id.Value, x => x, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, Attachment> attachments = await _context.Attachments
            .AsNoTracking()
            .Where(x => x.ProjectId == package.ProjectId && x.OrganizationId == organizationId)
            .ToDictionaryAsync(x => x.Id.Value, x => x, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> materials = await _context.Materials
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> sources = await _context.SourceProperties
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken)
            .ConfigureAwait(false);

        List<PackageElementModel> elements =
        [
            .. package.Elements
                .OrderBy(x => x.Position)
                .Select(element => ToModel(element, names, roles, attachments, materials, sources)),
        ];

        // Gaps are roles nothing currently holds. A package is largely a story
        // about what is still missing, so this is not decoration.
        List<ProjectRoleModel> gaps =
        [
            .. roles.Values
                .Where(role => role.Status != ProjectRoleStatus.Closed)
                .Where(role => !attachments.Values.Any(a =>
                    a.ProjectRoleId == role.Id && a.HoldsTheRole))
                .OrderBy(x => x.Type)
                .Select(role => new ProjectRoleModel(
                    role.Id.Value,
                    role.Type.ToString(),
                    role.Label,
                    role.Status.ToString(),
                    role.IsExclusive,
                    role.Notes,
                    [])),
        ];

        return new PackageDetailModel(
            summary,
            package.Thesis,
            package.StrategyNotes,
            elements,
            gaps,
            package.CreatedAt);
    }

    public async Task<ProjectCommandCenterModel> GetProjectCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProjectSummaryModel> recent = await ListProjectsAsync(
            organizationId,
            new ProjectFilter(Status: ProjectStatus.Active),
            limit: 10,
            cancellationToken).ConfigureAwait(false);

        List<Package> inProgress = await _context.Packages
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && (x.Status == PackageStatus.Assembling
                    || x.Status == PackageStatus.Ready
                    || x.Status == PackageStatus.Active))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<PackageSummaryModel> packages = inProgress.Count == 0
            ? []
            : [.. (await ListPackagesAsync(organizationId, new PackageFilter(), 50, cancellationToken)
                .ConfigureAwait(false))
                .Where(x => inProgress.Any(p => p.Id.Value == x.Id))];

        List<Attachment> changed = await _context.Attachments
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ProjectNames names = await LoadNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, ProjectRole> roles = changed.Count == 0
            ? []
            : await _context.ProjectRoles
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId)
                .ToDictionaryAsync(x => x.Id.Value, x => x, cancellationToken)
                .ConfigureAwait(false);

        int activeProjects = await _context.Projects
            .CountAsync(
                x => x.OrganizationId == organizationId && x.Status == ProjectStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

        return new ProjectCommandCenterModel(
            recent,
            packages,
            [
                .. changed.Select(x => ToModel(
                    x,
                    roles.TryGetValue(x.ProjectRoleId.Value, out ProjectRole? role) ? role : null,
                    names)),
            ],
            activeProjects,
            inProgress.Count);
    }

    // --------------------------------------------------------------- plumbing

    private async Task<IReadOnlyList<ProjectSummaryModel>> ToSummariesAsync(
        OrganizationId organizationId,
        List<Project> projects,
        CancellationToken cancellationToken)
    {
        if (projects.Count == 0)
        {
            return [];
        }

        ProjectNames names = await LoadNamesAsync(organizationId, cancellationToken).ConfigureAwait(false);

        List<ProjectId> keys = [.. projects.Select(x => x.Id)];

        Dictionary<Guid, int> openRoles = await _context.ProjectRoles
            .AsNoTracking()
            .Where(x => keys.Contains(x.ProjectId) && x.Status == ProjectRoleStatus.Open)
            .GroupBy(x => x.ProjectId)
            .Select(g => new { ProjectId = g.Key.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, int> attached = await _context.Attachments
            .AsNoTracking()
            .Where(x => keys.Contains(x.ProjectId)
                && (x.Status == AttachmentStatus.Attached || x.Status == AttachmentStatus.Conditional))
            .GroupBy(x => x.ProjectId)
            .Select(g => new { ProjectId = g.Key.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, int> packages = await _context.Packages
            .AsNoTracking()
            .Where(x => keys.Contains(x.ProjectId))
            .GroupBy(x => x.ProjectId)
            .Select(g => new { ProjectId = g.Key.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. projects.Select(x => new ProjectSummaryModel(
                x.Id.Value,
                x.Title,
                x.WorkingTitle,
                x.Type.ToString(),
                x.Status.ToString(),
                x.Stage.ToString(),
                x.Year,
                x.PrimaryCompanyId?.Value,
                x.PrimaryCompanyId is { } company ? names.Company(company.Value) : null,
                x.LeadUserId?.Value,
                x.LeadUserId is { } lead ? names.User(lead.Value) : null,
                openRoles.TryGetValue(x.Id.Value, out int open) ? open : 0,
                attached.TryGetValue(x.Id.Value, out int held) ? held : 0,
                packages.TryGetValue(x.Id.Value, out int count) ? count : 0,
                x.UpdatedAt,
                x.Version)),
        ];
    }

    private async Task<List<SourcePropertyModel>> ToSourceModelsAsync(
        OrganizationId organizationId,
        List<SourceProperty> properties,
        CancellationToken cancellationToken)
    {
        if (properties.Count == 0)
        {
            return [];
        }

        List<SourcePropertyId> keys = [.. properties.Select(x => x.Id)];

        Dictionary<Guid, int> counts = await _context.ProjectSourceProperties
            .AsNoTracking()
            .Where(x => keys.Contains(x.SourcePropertyId) && x.OrganizationId == organizationId)
            .GroupBy(x => x.SourcePropertyId)
            .Select(g => new { SourceId = g.Key.Value, Count = g.Count() })
            .ToDictionaryAsync(x => x.SourceId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. properties.Select(x => new SourcePropertyModel(
                x.Id.Value,
                x.Title,
                x.Type.ToString(),
                x.AttributedCreator,
                x.CreatorPersonId?.Value,
                x.SourceReference,
                x.Provenance,
                x.Year,
                x.Notes,
                counts.TryGetValue(x.Id.Value, out int count) ? count : 0,
                x.UpdatedAt,
                x.Version)),
        ];
    }

    private async Task<List<SourcePropertyModel>> ListSourcePropertiesByIdAsync(
        OrganizationId organizationId,
        List<Guid> ids,
        CancellationToken cancellationToken)
    {
        List<SourcePropertyId> keys = [.. ids.Select(x => new SourcePropertyId(x))];

        List<SourceProperty> properties = await _context.SourceProperties
            .AsNoTracking()
            .Where(x => keys.Contains(x.Id) && x.OrganizationId == organizationId)
            .OrderBy(x => x.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ToSourceModelsAsync(organizationId, properties, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<ProjectMaterialModel>> LoadProjectMaterialsAsync(
        OrganizationId organizationId,
        Project project,
        ProjectNames names,
        CancellationToken cancellationToken)
    {
        if (project.Materials.Count == 0)
        {
            return [];
        }

        List<Domain.Talent.MaterialId> keys = [.. project.Materials.Select(x => x.MaterialId)];

        var materials = await _context.Materials
            .AsNoTracking()
            .Where(x => keys.Contains(x.Id) && x.OrganizationId == organizationId)
            .Select(x => new
            {
                Id = x.Id.Value,
                x.Title,
                x.Type,
                x.Status,
                PersonId = x.PersonId.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string?> notes = project.Materials
            .ToDictionary(x => x.MaterialId.Value, x => x.Notes);

        return
        [
            .. materials.Select(x => new ProjectMaterialModel(
                x.Id,
                x.Title,
                x.Type.ToString(),
                x.Status.ToString(),
                x.PersonId,
                names.Person(x.PersonId),
                notes.TryGetValue(x.Id, out string? note) ? note : null)),
        ];
    }

    private static AttachmentModel ToModel(Attachment attachment, ProjectRole? role, ProjectNames names) =>
        new(
            attachment.Id.Value,
            attachment.ProjectRoleId.Value,
            role?.Type.ToString() ?? "Other",
            role?.Label,
            attachment.PersonId?.Value,
            attachment.CompanyId?.Value,
            names.Party(attachment.PersonId?.Value, attachment.CompanyId?.Value),
            attachment.Status.ToString(),
            attachment.StartsOn,
            attachment.EndsOn,
            attachment.HoldsTheRole,
            attachment.Source,
            attachment.Notes,
            attachment.UpdatedAt,
            attachment.Version);

    private static PackageElementModel ToModel(
        PackageElement element,
        ProjectNames names,
        Dictionary<Guid, ProjectRole> roles,
        Dictionary<Guid, Attachment> attachments,
        Dictionary<Guid, string> materials,
        Dictionary<Guid, string> sources)
    {
        string display;
        string? detail = null;
        bool isAttached = false;

        switch (element.Kind)
        {
            case PackageElementKind.AttachedParty:
                if (attachments.TryGetValue(element.TargetId, out Attachment? attachment))
                {
                    display = names.Party(attachment.PersonId?.Value, attachment.CompanyId?.Value);

                    detail = roles.TryGetValue(attachment.ProjectRoleId.Value, out ProjectRole? held)
                        ? held.Describe()
                        : null;

                    // Only a real attachment that still holds counts as attached. An
                    // element pointing at one that ended is history, not a claim.
                    isAttached = attachment.HoldsTheRole;
                }
                else
                {
                    display = "(unknown)";
                }

                break;

            case PackageElementKind.ProposedPerson:
                display = names.Person(element.TargetId);
                detail = "Proposed";
                break;

            case PackageElementKind.ProposedCompany:
                display = names.Company(element.TargetId);
                detail = "Proposed";
                break;

            case PackageElementKind.OpenRole:
                display = roles.TryGetValue(element.TargetId, out ProjectRole? role)
                    ? role.Describe()
                    : "(unknown role)";
                detail = "To fill";
                break;

            case PackageElementKind.Material:
                display = materials.TryGetValue(element.TargetId, out string? title) ? title : "(unknown)";
                detail = "Material";
                break;

            case PackageElementKind.SourceProperty:
                display = sources.TryGetValue(element.TargetId, out string? source) ? source : "(unknown)";
                detail = "Source property";
                break;

            default:
                display = "(unknown)";
                break;
        }

        return new PackageElementModel(
            element.Id,
            element.Kind.ToString(),
            element.TargetId,
            display,
            detail,
            isAttached,
            element.Note,
            element.Position);
    }

    private async Task<ProjectNames> LoadNamesAsync(
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

        return new ProjectNames(people, companies, users);
    }

    /// <summary>Names for one tenant, resolved once per request.</summary>
    private sealed record ProjectNames(
        Dictionary<Guid, string> People,
        Dictionary<Guid, string> Companies,
        Dictionary<Guid, string> Users)
    {
        public string Person(Guid id) => People.TryGetValue(id, out string? name) ? name : "(unknown)";

        public string Company(Guid id) => Companies.TryGetValue(id, out string? name) ? name : "(unknown)";

        // An unresolvable name is shown as unknown rather than as an error: a
        // history is still worth reading when one party has since been archived.
        public string? User(Guid id) => Users.TryGetValue(id, out string? name) ? name : null;

        public string Party(Guid? personId, Guid? companyId) =>
            personId is { } person ? Person(person)
            : companyId is { } company ? Company(company)
            : "(unknown)";
    }
}
