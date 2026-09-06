using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Memberships;

/// <param name="OrganizationId">Organization the membership is granted within.</param>
/// <param name="UserId">User receiving the membership.</param>
/// <param name="Role">Role conferred.</param>
public sealed record GrantMembershipCommand(OrganizationId OrganizationId, UserId UserId, AgencyRole Role);

/// <param name="Id">Identifier of the granted membership.</param>
public sealed record GrantMembershipResult(MembershipId Id);

/// <summary>
/// Grants a user a role within an organization.
/// </summary>
/// <remarks>
/// The permission is required <em>within the target organization</em>. Holding
/// <c>memberships.grant</c> somewhere else confers no authority here, which is
/// the contextual half of the authorization model in
/// <c>docs/07_SECURITY_AND_AUDIT.md</c>.
/// </remarks>
public sealed class GrantMembershipHandler
{
    private readonly IOrganizationRepository _organizations;
    private readonly IMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly IPermissionEvaluator _permissions;
    private readonly IExecutionContext _execution;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public GrantMembershipHandler(
        IOrganizationRepository organizations,
        IMembershipRepository memberships,
        IUserRepository users,
        IPermissionEvaluator permissions,
        IExecutionContext execution,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _organizations = organizations;
        _memberships = memberships;
        _users = users;
        _permissions = permissions;
        _execution = execution;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<GrantMembershipResult> HandleAsync(
        GrantMembershipCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = _execution.UserId ?? throw new NotAuthenticatedException();

        bool permitted = await _permissions
            .HasPermissionAsync(actor, Permission.MembershipsGrant, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!permitted)
        {
            throw new PermissionDeniedException(Permission.MembershipsGrant);
        }

        Organization organization =
            await _organizations.FindByIdAsync(command.OrganizationId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Organization), command.OrganizationId.ToString());

        if (organization.Status != OrganizationStatus.Active)
        {
            throw new DomainException("Cannot grant a membership in an archived organization.");
        }

        _ = await _users.FindByIdAsync(command.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(User), command.UserId.ToString());

        Membership? existing = await _memberships
            .FindActiveAsync(command.OrganizationId, command.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            throw new DomainException("The user already holds an active membership in this organization.");
        }

        Membership membership = Membership.Grant(
            command.OrganizationId,
            command.UserId,
            command.Role,
            actor,
            _clock.UtcNow);

        _memberships.Add(membership);

        _audit.Record(
            AuditAction.MembershipGranted,
            entityType: nameof(Membership),
            entityId: membership.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.MembershipsGrant,
            semanticDelta: new
            {
                UserId = command.UserId.ToString(),
                Role = command.Role.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new GrantMembershipResult(membership.Id);
    }
}
