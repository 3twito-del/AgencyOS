using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Representations;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;

namespace AgencyOS.Application.Projects;

public sealed record CreatePackageCommand(
    OrganizationId OrganizationId,
    ProjectId ProjectId,
    string Name,
    UserId LeadUserId,
    string? Thesis,
    string? StrategyNotes);

public sealed record UpdatePackageCommand(
    OrganizationId OrganizationId,
    PackageId PackageId,
    string Name,
    UserId LeadUserId,
    string? Thesis,
    string? StrategyNotes,
    int ExpectedVersion);

public sealed record ChangePackageStatusCommand(
    OrganizationId OrganizationId,
    PackageId PackageId,
    PackageStatus Status,
    string? Reason,
    int ExpectedVersion);

public sealed record AddPackageElementCommand(
    OrganizationId OrganizationId,
    PackageId PackageId,
    PackageElementKind Kind,
    Guid TargetId,
    string? Note,
    int ExpectedVersion);

public sealed record RemovePackageElementCommand(
    OrganizationId OrganizationId,
    PackageId PackageId,
    Guid ElementId,
    int ExpectedVersion);

/// <summary>Creates and maintains packages.</summary>
public sealed class PackageHandler
{
    private readonly IPackageRepository _packages;
    private readonly IProjectRepository _projects;
    private readonly IMembershipRepository _memberships;
    private readonly IPackageElementTargets _elementTargets;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public PackageHandler(
        IPackageRepository packages,
        IProjectRepository projects,
        IMembershipRepository memberships,
        IPackageElementTargets elementTargets,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _packages = packages;
        _projects = projects;
        _memberships = memberships;
        _elementTargets = elementTargets;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<PackageId> HandleAsync(
        CreatePackageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.PackagesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        bool projectExists = await _projects
            .ExistsAsync(command.OrganizationId, command.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (!projectExists)
        {
            throw new EntityNotFoundException(nameof(Project), command.ProjectId.ToString());
        }

        await CreateProspectHandler
            .RequireTenantMemberAsync(_memberships, command.OrganizationId, command.LeadUserId, cancellationToken)
            .ConfigureAwait(false);

        Package package = Package.Create(
            command.OrganizationId,
            command.ProjectId,
            command.Name,
            command.LeadUserId,
            actor,
            _clock.UtcNow,
            command.Thesis,
            command.StrategyNotes);

        _packages.Add(package);

        _audit.Record(
            AuditAction.PackageCreated,
            entityType: nameof(Package),
            entityId: package.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.PackagesWrite,
            semanticDelta: new
            {
                ProjectId = command.ProjectId.ToString(),
                package.Name,
                Lead = command.LeadUserId.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return package.Id;
    }

    public async Task HandleAsync(
        UpdatePackageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.PackagesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        await CreateProspectHandler
            .RequireTenantMemberAsync(_memberships, command.OrganizationId, command.LeadUserId, cancellationToken)
            .ConfigureAwait(false);

        Package package = await RequirePackageAsync(command.OrganizationId, command.PackageId, cancellationToken)
            .ConfigureAwait(false);

        package.Update(
            command.Name,
            command.LeadUserId,
            _clock.UtcNow,
            command.ExpectedVersion,
            command.Thesis,
            command.StrategyNotes);

        _audit.Record(
            AuditAction.PackageUpdated,
            entityType: nameof(Package),
            entityId: package.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.PackagesWrite,
            semanticDelta: new { package.Name, Lead = command.LeadUserId.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAsync(
        ChangePackageStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.PackagesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Package package = await RequirePackageAsync(command.OrganizationId, command.PackageId, cancellationToken)
            .ConfigureAwait(false);

        PackageStatus from = package.Status;

        package.ChangeStatus(
            command.Status, _clock.UtcNow, actor, command.ExpectedVersion, command.Reason);

        _audit.Record(
            AuditAction.PackageStatusChanged,
            entityType: nameof(Package),
            entityId: package.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.PackagesWrite,
            semanticDelta: new { From = from.ToString(), To = package.Status.ToString() },
            reason: command.Reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds an element to a package, having checked the target is real and in this tenant.
    /// </summary>
    /// <remarks>
    /// The check matters more here than it looks. A package element is a raw
    /// identifier interpreted by its kind, so without validation a package could
    /// reference another tenant's person by pasting in a GUID - and the reference
    /// would render, because rendering only needs the id. Every kind is resolved
    /// against this tenant before it is stored.
    /// </remarks>
    public async Task<Guid> HandleAsync(
        AddPackageElementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.PackagesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Package package = await RequirePackageAsync(command.OrganizationId, command.PackageId, cancellationToken)
            .ConfigureAwait(false);

        await _elementTargets
            .RequireAsync(command.OrganizationId, package.ProjectId, command.Kind, command.TargetId, cancellationToken)
            .ConfigureAwait(false);

        PackageElement element = package.AddElement(
            command.Kind,
            command.TargetId,
            _clock.UtcNow,
            actor,
            command.ExpectedVersion,
            command.Note);

        _audit.Record(
            AuditAction.PackageElementAdded,
            entityType: nameof(PackageElement),
            entityId: element.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.PackagesWrite,
            semanticDelta: new
            {
                PackageId = package.Id.ToString(),
                Kind = command.Kind.ToString(),
                TargetId = command.TargetId,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return element.Id;
    }

    public async Task HandleAsync(
        RemovePackageElementCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(Permission.PackagesWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Package package = await RequirePackageAsync(command.OrganizationId, command.PackageId, cancellationToken)
            .ConfigureAwait(false);

        package.RemoveElement(command.ElementId, _clock.UtcNow, command.ExpectedVersion);

        _audit.Record(
            AuditAction.PackageElementRemoved,
            entityType: nameof(PackageElement),
            entityId: command.ElementId.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.PackagesWrite,
            semanticDelta: new { PackageId = package.Id.ToString() });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Package> RequirePackageAsync(
        OrganizationId organizationId,
        PackageId packageId,
        CancellationToken cancellationToken)
    {
        Package? package = await _packages
            .FindAsync(organizationId, packageId, cancellationToken)
            .ConfigureAwait(false);

        return package ?? throw new EntityNotFoundException(nameof(Package), packageId.ToString());
    }
}

/// <summary>
/// Confirms a package element points at something real, in this tenant.
/// </summary>
/// <remarks>
/// Implemented in infrastructure because the check is a handful of existence
/// queries across tables the application layer does not own. It exists as an
/// interface so the rule - every element target is validated, none is taken on
/// trust - is stated where the command handler can be read.
/// </remarks>
public interface IPackageElementTargets
{
    Task RequireAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        PackageElementKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default);
}
