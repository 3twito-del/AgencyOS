using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;

namespace AgencyOS.Application.Abstractions;

/// <summary>
/// Repositories for the M5 project and packaging model.
/// </summary>
/// <remarks>
/// Every lookup takes the tenant explicitly, as M2 and M4 do: a method that can be
/// called without a tenant is a cross-tenant read waiting to be written. The
/// database enforces the same containment through composite foreign keys
/// (ADR-0011).
/// </remarks>
public interface IProjectRepository
{
    /// <summary>Loads a project with the parts a command needs to reason about.</summary>
    /// <remarks>
    /// The roster comes with it - roles and attachments - because the exclusive
    /// role invariant is a statement about the whole roster and cannot be checked
    /// from a single row.
    /// </remarks>
    Task<Project?> FindAsync(
        OrganizationId organizationId,
        ProjectId id,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms a project exists in this tenant without loading its graph.</summary>
    Task<bool> ExistsAsync(
        OrganizationId organizationId,
        ProjectId id,
        CancellationToken cancellationToken = default);

    void Add(Project project);
}

public interface ISourcePropertyRepository
{
    Task<SourceProperty?> FindAsync(
        OrganizationId organizationId,
        SourcePropertyId id,
        CancellationToken cancellationToken = default);

    void Add(SourceProperty sourceProperty);
}

public interface IPackageRepository
{
    Task<Package?> FindAsync(
        OrganizationId organizationId,
        PackageId id,
        CancellationToken cancellationToken = default);

    void Add(Package package);
}
