using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Projects;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Talent;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <remarks>
/// Every query filters on the tenant as well as the identifier. The predicate is
/// not redundant with the composite foreign keys: those stop bad data being
/// written, this stops good data being read by the wrong tenant.
/// </remarks>
internal sealed class ProjectRepository : IProjectRepository
{
    private readonly AgencyOsDbContext _context;

    public ProjectRepository(AgencyOsDbContext context) => _context = context;

    /// <summary>
    /// Loads a project with the parts its commands reason about.
    /// </summary>
    /// <remarks>
    /// The roles and attachments come with it because the exclusive-role invariant
    /// is a statement about the whole roster: whether a second director can be
    /// recorded depends on every other attachment to that role, so a single row is
    /// not enough to decide it.
    /// </remarks>
    public Task<Project?> FindAsync(
        OrganizationId organizationId,
        ProjectId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Projects
            .Include(x => x.Roles)
            .Include(x => x.Attachments)
            .Include(x => x.CompanyParticipations)
            .Include(x => x.SourceProperties)
            .Include(x => x.Materials)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public Task<bool> ExistsAsync(
        OrganizationId organizationId,
        ProjectId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Projects
            .AnyAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public void Add(Project project) => _context.Projects.Add(project);
}

internal sealed class SourcePropertyRepository : ISourcePropertyRepository
{
    private readonly AgencyOsDbContext _context;

    public SourcePropertyRepository(AgencyOsDbContext context) => _context = context;

    public Task<SourceProperty?> FindAsync(
        OrganizationId organizationId,
        SourcePropertyId id,
        CancellationToken cancellationToken = default)
    {
        return _context.SourceProperties
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public void Add(SourceProperty sourceProperty) => _context.SourceProperties.Add(sourceProperty);
}

internal sealed class PackageRepository : IPackageRepository
{
    private readonly AgencyOsDbContext _context;

    public PackageRepository(AgencyOsDbContext context) => _context = context;

    public Task<Package?> FindAsync(
        OrganizationId organizationId,
        PackageId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Packages
            .Include(x => x.Elements)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public void Add(Package package) => _context.Packages.Add(package);
}

/// <summary>
/// Confirms a package element points at something real, in this tenant.
/// </summary>
/// <remarks>
/// <para>
/// A package element is a raw identifier interpreted by its kind, which is a
/// compact model and an easy one to abuse: without this check a package could
/// reference another tenant's person by pasting in an identifier, and the reference
/// would render, because rendering only needs the id.
/// </para>
/// <para>
/// A foreign key per kind would enforce the same thing structurally, but at the
/// cost of six mostly-null columns and a check constraint nobody could read. The
/// trade is deliberate: one narrow validator, called on the only path that writes
/// elements, and a test that proves each kind is checked.
/// </para>
/// </remarks>
internal sealed class PackageElementTargets : IPackageElementTargets
{
    private readonly AgencyOsDbContext _context;

    public PackageElementTargets(AgencyOsDbContext context) => _context = context;

    public async Task RequireAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        PackageElementKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        bool exists = kind switch
        {
            PackageElementKind.AttachedParty => await _context.Attachments
                .AnyAsync(
                    x => x.Id == new AttachmentId(targetId)
                        && x.OrganizationId == organizationId
                        && x.ProjectId == projectId,
                    cancellationToken)
                .ConfigureAwait(false),

            PackageElementKind.ProposedPerson => await _context.People
                .AnyAsync(
                    x => x.Id == new PersonId(targetId) && x.OrganizationId == organizationId,
                    cancellationToken)
                .ConfigureAwait(false),

            PackageElementKind.ProposedCompany => await _context.Companies
                .AnyAsync(
                    x => x.Id == new CompanyId(targetId) && x.OrganizationId == organizationId,
                    cancellationToken)
                .ConfigureAwait(false),

            PackageElementKind.OpenRole => await _context.ProjectRoles
                .AnyAsync(
                    x => x.Id == new ProjectRoleId(targetId)
                        && x.OrganizationId == organizationId
                        && x.ProjectId == projectId,
                    cancellationToken)
                .ConfigureAwait(false),

            PackageElementKind.Material => await _context.Materials
                .AnyAsync(
                    x => x.Id == new MaterialId(targetId) && x.OrganizationId == organizationId,
                    cancellationToken)
                .ConfigureAwait(false),

            PackageElementKind.SourceProperty => await _context.SourceProperties
                .AnyAsync(
                    x => x.Id == new SourcePropertyId(targetId) && x.OrganizationId == organizationId,
                    cancellationToken)
                .ConfigureAwait(false),

            _ => false,
        };

        if (!exists)
        {
            throw new EntityNotFoundException(
                kind.ToString(),
                $"{targetId} is not a {kind} available to this package.");
        }
    }
}
