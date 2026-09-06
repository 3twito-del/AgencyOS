using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Organizations;

/// <param name="Name">Display name of the organization.</param>
/// <param name="LegalName">Registered legal name, when it differs.</param>
/// <param name="Type">Broad classification.</param>
public sealed record CreateOrganizationCommand(string Name, string? LegalName, OrganizationType Type);

/// <param name="Id">Identifier of the created organization.</param>
/// <param name="Name">Display name as stored.</param>
public sealed record CreateOrganizationResult(OrganizationId Id, string Name);

/// <summary>
/// Creates an organization and makes the creator its owner.
/// </summary>
/// <remarks>
/// The permission check, the business change and the audit records all happen
/// here, in one transaction. A caller cannot obtain the change without the check
/// or without the record.
/// </remarks>
public sealed class CreateOrganizationHandler
{
    private readonly IOrganizationRepository _organizations;
    private readonly IMembershipRepository _memberships;
    private readonly IPermissionEvaluator _permissions;
    private readonly IExecutionContext _execution;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CreateOrganizationHandler(
        IOrganizationRepository organizations,
        IMembershipRepository memberships,
        IPermissionEvaluator permissions,
        IExecutionContext execution,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _organizations = organizations;
        _memberships = memberships;
        _permissions = permissions;
        _execution = execution;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<CreateOrganizationResult> HandleAsync(
        CreateOrganizationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = _execution.UserId ?? throw new NotAuthenticatedException();

        bool permitted = await _permissions
            .HasPermissionAsync(actor, Permission.OrganizationsCreate, scope: null, cancellationToken)
            .ConfigureAwait(false);

        if (!permitted)
        {
            throw new PermissionDeniedException(Permission.OrganizationsCreate);
        }

        DateTimeOffset now = _clock.UtcNow;

        Organization organization = Organization.Create(
            command.Name,
            command.LegalName,
            command.Type,
            actor,
            now);

        _organizations.Add(organization);

        // The creator owns what they create, otherwise a new organization would be
        // immediately unadministrable.
        Membership ownership = Membership.Grant(
            organization.Id,
            actor,
            AgencyRole.Owner,
            actor,
            now);

        _memberships.Add(ownership);

        _audit.Record(
            AuditAction.OrganizationCreated,
            entityType: nameof(Organization),
            entityId: organization.Id.ToString(),
            organizationId: organization.Id,
            permission: Permission.OrganizationsCreate,
            semanticDelta: new
            {
                organization.Name,
                organization.LegalName,
                Type = organization.Type.ToString(),
                Status = organization.Status.ToString(),
            });

        _audit.Record(
            AuditAction.MembershipGranted,
            entityType: nameof(Membership),
            entityId: ownership.Id.ToString(),
            organizationId: organization.Id,
            permission: Permission.OrganizationsCreate,
            semanticDelta: new
            {
                UserId = actor.ToString(),
                Role = ownership.Role.ToString(),
                Reason = "Creator of the organization.",
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CreateOrganizationResult(organization.Id, organization.Name);
    }
}
