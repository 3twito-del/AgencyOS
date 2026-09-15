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

/// <summary>One person's standing in an organization.</summary>
/// <param name="MembershipId">The membership.</param>
/// <param name="UserId">The person.</param>
/// <param name="DisplayName">Their name, as the identity provider gave it.</param>
/// <param name="Email">Their address.</param>
/// <param name="Role">The role they hold.</param>
/// <param name="GrantedAt">When they were given it.</param>
/// <param name="IsSelf">Whether this is the caller.</param>
public sealed record OrganizationMember(
    MembershipId MembershipId,
    UserId UserId,
    string DisplayName,
    string Email,
    AgencyRole Role,
    DateTimeOffset GrantedAt,
    bool IsSelf);

/// <summary>Asks who is in an organization.</summary>
/// <param name="OrganizationId">The organization.</param>
public sealed record ListMembersQuery(OrganizationId OrganizationId);

/// <summary>
/// Reads an organization's active members.
/// </summary>
/// <remarks>
/// <c>memberships.read</c> is held by every role including Observer, and until
/// this handler nothing in the product exercised it. Knowing who else is in the
/// organization is not privileged information to the people in it.
/// </remarks>
public sealed class ListMembersHandler
{
    private readonly IMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly IPermissionEvaluator _permissions;
    private readonly IExecutionContext _execution;

    /// <summary>Creates the handler.</summary>
    /// <param name="memberships">Membership storage.</param>
    /// <param name="users">User storage.</param>
    /// <param name="permissions">Authorization.</param>
    /// <param name="execution">Who is asking.</param>
    public ListMembersHandler(
        IMembershipRepository memberships,
        IUserRepository users,
        IPermissionEvaluator permissions,
        IExecutionContext execution)
    {
        _memberships = memberships;
        _users = users;
        _permissions = permissions;
        _execution = execution;
    }

    /// <summary>Lists the organization's active members.</summary>
    /// <param name="query">Which organization.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>One entry per active membership.</returns>
    public async Task<IReadOnlyList<OrganizationMember>> HandleAsync(
        ListMembersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        UserId actor = _execution.UserId ?? throw new NotAuthenticatedException();

        bool permitted = await _permissions
            .HasPermissionAsync(
                actor, Permission.MembershipsRead, query.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!permitted)
        {
            // Refused on permission before the organization is looked at, so a
            // caller outside it learns nothing about whether it exists (ADR-0038).
            throw new PermissionDeniedException(Permission.MembershipsRead);
        }

        IReadOnlyList<Membership> memberships = await _memberships
            .ListActiveForOrganizationAsync(query.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        List<OrganizationMember> members = [];

        foreach (Membership membership in memberships)
        {
            User? user = await _users
                .FindByIdAsync(membership.UserId, cancellationToken)
                .ConfigureAwait(false);

            members.Add(new OrganizationMember(
                membership.Id,
                membership.UserId,
                user?.DisplayName ?? "Unknown user",
                user?.Email ?? string.Empty,
                membership.Role,
                membership.GrantedAt,
                membership.UserId == actor));
        }

        return members;
    }
}

/// <summary>Brings a person into an organization.</summary>
/// <param name="OrganizationId">The organization.</param>
/// <param name="ExternalSubject">Their identity-provider subject.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="Email">Their address.</param>
/// <param name="Role">The role to give them.</param>
public sealed record AddMemberCommand(
    OrganizationId OrganizationId,
    string ExternalSubject,
    string DisplayName,
    string Email,
    AgencyRole Role);

/// <summary>What adding a member produced.</summary>
/// <param name="MembershipId">The new membership.</param>
/// <param name="UserId">The person, whether newly registered or already known.</param>
/// <param name="UserWasRegistered">Whether this call registered the user.</param>
public sealed record AddMemberResult(MembershipId MembershipId, UserId UserId, bool UserWasRegistered);

/// <summary>
/// Registers a person if AgencyOS has not met them, and gives them a role.
/// </summary>
/// <remarks>
/// <para>
/// Until now <c>User.Register</c> was called from exactly one place — first-run
/// bootstrap — so an organization's population was fixed at one forever. The
/// membership endpoint existed and could never be used, because it needs a user
/// identifier and nothing could produce one.
/// </para>
/// <para>
/// <strong>This is not just-in-time provisioning.</strong> The development
/// authentication handler deliberately refuses to turn an unknown subject into an
/// identity on the strength of a request, and that stays true. A person is
/// registered because somebody holding <c>memberships.grant</c> said so, naming
/// the subject their identity provider issues. When a real provider replaces the
/// development one, that subject is what it hands over, and nothing here changes.
/// </para>
/// </remarks>
public sealed class AddMemberHandler
{
    private readonly IOrganizationRepository _organizations;
    private readonly IMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly IPermissionEvaluator _permissions;
    private readonly IExecutionContext _execution;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Creates the handler.</summary>
    /// <param name="organizations">Organization storage.</param>
    /// <param name="memberships">Membership storage.</param>
    /// <param name="users">User storage.</param>
    /// <param name="permissions">Authorization.</param>
    /// <param name="execution">Who is asking.</param>
    /// <param name="audit">The append-only trail.</param>
    /// <param name="clock">Time.</param>
    /// <param name="unitOfWork">The transaction.</param>
    public AddMemberHandler(
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

    /// <summary>Adds the person, registering them first if necessary.</summary>
    /// <param name="command">Who, where, and as what.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The membership, and whether a user was registered.</returns>
    public async Task<AddMemberResult> HandleAsync(
        AddMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = _execution.UserId ?? throw new NotAuthenticatedException();

        bool permitted = await _permissions
            .HasPermissionAsync(
                actor, Permission.MembershipsGrant, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!permitted)
        {
            throw new PermissionDeniedException(Permission.MembershipsGrant);
        }

        if (!Enum.IsDefined(command.Role))
        {
            throw new DomainException($"Unknown role '{command.Role}'.");
        }

        // Nobody may hand out authority above their own. An administrator can
        // build a team; making another owner is the owner's decision.
        await EnsureMayGrantAsync(actor, command.OrganizationId, command.Role, cancellationToken)
            .ConfigureAwait(false);

        Organization organization =
            await _organizations.FindByIdAsync(command.OrganizationId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(Organization), command.OrganizationId.ToString());

        if (organization.Status != OrganizationStatus.Active)
        {
            throw new DomainException("Cannot add a member to an archived organization.");
        }

        User? user = await _users
            .FindBySubjectAsync(command.ExternalSubject, cancellationToken)
            .ConfigureAwait(false);

        bool registered = false;

        if (user is null)
        {
            user = User.Register(
                command.ExternalSubject, command.DisplayName, command.Email, _clock.UtcNow);

            _users.Add(user);
            registered = true;

            _audit.Record(
                AuditAction.UserRegistered,
                entityType: nameof(User),
                entityId: user.Id.ToString(),
                organizationId: command.OrganizationId,
                permission: Permission.MembershipsGrant,
                semanticDelta: new
                {
                    DisplayName = command.DisplayName,

                    // The subject is an identifier the provider issues, not a
                    // secret. No token or credential is recorded anywhere here.
                    ExternalSubject = command.ExternalSubject,
                });
        }

        Membership? existing = await _memberships
            .FindActiveAsync(command.OrganizationId, user.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            throw new DomainException(
                "That person is already in this organization. Change their role instead.");
        }

        Membership membership = Membership.Grant(
            command.OrganizationId, user.Id, command.Role, actor, _clock.UtcNow);

        _memberships.Add(membership);

        _audit.Record(
            AuditAction.MembershipGranted,
            entityType: nameof(Membership),
            entityId: membership.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.MembershipsGrant,
            semanticDelta: new
            {
                UserId = user.Id.ToString(),
                Role = command.Role.ToString(),
                UserWasRegistered = registered,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AddMemberResult(membership.Id, user.Id, registered);
    }

    /// <summary>
    /// Refuses a grant of authority the caller does not itself hold.
    /// </summary>
    /// <remarks>
    /// An administrator holds <c>memberships.grant</c>, which is what lets them
    /// build a team. Without this check it would also let them make themselves an
    /// owner by adding a second account, which is privilege escalation with extra
    /// steps.
    /// </remarks>
    private async Task EnsureMayGrantAsync(
        UserId actor,
        OrganizationId organizationId,
        AgencyRole role,
        CancellationToken cancellationToken)
    {
        if (role != AgencyRole.Owner)
        {
            return;
        }

        Membership? own = await _memberships
            .FindActiveAsync(organizationId, actor, cancellationToken)
            .ConfigureAwait(false);

        if (own?.Role != AgencyRole.Owner)
        {
            throw new PermissionDeniedException(Permission.MembershipsGrant);
        }
    }
}

/// <summary>Ends somebody's membership.</summary>
/// <param name="OrganizationId">The organization.</param>
/// <param name="MembershipId">The membership to end.</param>
public sealed record RevokeMembershipCommand(OrganizationId OrganizationId, MembershipId MembershipId);

/// <summary>
/// Ends a membership, refusing to leave an organization without an owner.
/// </summary>
/// <remarks>
/// A role change is a revocation followed by a grant, because a membership is
/// never edited: the row is retained so that what somebody could do last March
/// stays answerable. So this handler is also the second half of every demotion,
/// and the last-owner rule covers both.
/// </remarks>
public sealed class RevokeMembershipHandler
{
    private readonly IMembershipRepository _memberships;
    private readonly IPermissionEvaluator _permissions;
    private readonly IExecutionContext _execution;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Creates the handler.</summary>
    /// <param name="memberships">Membership storage.</param>
    /// <param name="permissions">Authorization.</param>
    /// <param name="execution">Who is asking.</param>
    /// <param name="audit">The append-only trail.</param>
    /// <param name="clock">Time.</param>
    /// <param name="unitOfWork">The transaction.</param>
    public RevokeMembershipHandler(
        IMembershipRepository memberships,
        IPermissionEvaluator permissions,
        IExecutionContext execution,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _memberships = memberships;
        _permissions = permissions;
        _execution = execution;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Ends the membership.</summary>
    /// <param name="command">Which membership, in which organization.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task HandleAsync(
        RevokeMembershipCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = _execution.UserId ?? throw new NotAuthenticatedException();

        bool permitted = await _permissions
            .HasPermissionAsync(
                actor, Permission.MembershipsRevoke, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (!permitted)
        {
            throw new PermissionDeniedException(Permission.MembershipsRevoke);
        }

        Membership membership =
            await _memberships.FindByIdAsync(command.MembershipId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(Membership), command.MembershipId.ToString());

        // The membership must belong to the organization the caller was authorized
        // for. Without this, holding memberships.revoke anywhere would revoke
        // memberships everywhere.
        if (membership.OrganizationId != command.OrganizationId)
        {
            throw new EntityNotFoundException(
                nameof(Membership), command.MembershipId.ToString());
        }

        IReadOnlyList<Membership> active = await _memberships
            .ListActiveForOrganizationAsync(command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        int otherActiveOwners = active.Count(x =>
            x.Role == AgencyRole.Owner && x.Id != membership.Id);

        AgencyRole priorRole = membership.Role;

        membership.Revoke(actor, _clock.UtcNow, otherActiveOwners);

        _audit.Record(
            AuditAction.MembershipRevoked,
            entityType: nameof(Membership),
            entityId: membership.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.MembershipsRevoke,
            semanticDelta: new
            {
                UserId = membership.UserId.ToString(),
                PriorRole = priorRole.ToString(),
                Status = MembershipStatus.Revoked.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Moves somebody to a different role.</summary>
/// <param name="OrganizationId">The organization.</param>
/// <param name="MembershipId">The membership to change.</param>
/// <param name="Role">The role they should hold instead.</param>
public sealed record ChangeMemberRoleCommand(
    OrganizationId OrganizationId,
    MembershipId MembershipId,
    AgencyRole Role);

/// <summary>What the change produced.</summary>
/// <param name="MembershipId">The new membership carrying the new role.</param>
public sealed record ChangeMemberRoleResult(MembershipId MembershipId);

/// <summary>
/// Changes a role by ending one membership and granting another, in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// A membership is never edited: the row is retained so that what somebody could
/// do last March stays answerable. So a role change is a revocation followed by a
/// grant, and the audit trail shows both.
/// </para>
/// <para>
/// <strong>One transaction, not two calls.</strong> Leaving the client to revoke
/// and then grant would mean a failure between them takes somebody's access away
/// and gives nothing back. Both halves commit together or neither does.
/// </para>
/// <para>
/// Both halves keep their own rule: the revoke refuses to remove the last owner,
/// and the grant refuses to hand out an authority the caller does not hold.
/// </para>
/// </remarks>
public sealed class ChangeMemberRoleHandler
{
    private readonly IMembershipRepository _memberships;
    private readonly IPermissionEvaluator _permissions;
    private readonly IExecutionContext _execution;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Creates the handler.</summary>
    /// <param name="memberships">Membership storage.</param>
    /// <param name="permissions">Authorization.</param>
    /// <param name="execution">Who is asking.</param>
    /// <param name="audit">The append-only trail.</param>
    /// <param name="clock">Time.</param>
    /// <param name="unitOfWork">The transaction.</param>
    public ChangeMemberRoleHandler(
        IMembershipRepository memberships,
        IPermissionEvaluator permissions,
        IExecutionContext execution,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _memberships = memberships;
        _permissions = permissions;
        _execution = execution;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Changes the role.</summary>
    /// <param name="command">Which membership, and to what.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The membership that now carries the role.</returns>
    public async Task<ChangeMemberRoleResult> HandleAsync(
        ChangeMemberRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = _execution.UserId ?? throw new NotAuthenticatedException();

        // Changing a role both ends one membership and grants another, so the
        // caller needs the authority for both halves.
        foreach (string permission in (string[])
            [Permission.MembershipsRevoke, Permission.MembershipsGrant])
        {
            bool permitted = await _permissions
                .HasPermissionAsync(actor, permission, command.OrganizationId, cancellationToken)
                .ConfigureAwait(false);

            if (!permitted)
            {
                throw new PermissionDeniedException(permission);
            }
        }

        if (!Enum.IsDefined(command.Role))
        {
            throw new DomainException($"Unknown role '{command.Role}'.");
        }

        Membership membership =
            await _memberships.FindByIdAsync(command.MembershipId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(Membership), command.MembershipId.ToString());

        if (membership.OrganizationId != command.OrganizationId)
        {
            throw new EntityNotFoundException(
                nameof(Membership), command.MembershipId.ToString());
        }

        if (membership.Role == command.Role)
        {
            throw new DomainException("That is the role they already hold.");
        }

        // Only an owner may create another owner.
        if (command.Role == AgencyRole.Owner)
        {
            Membership? own = await _memberships
                .FindActiveAsync(command.OrganizationId, actor, cancellationToken)
                .ConfigureAwait(false);

            if (own?.Role != AgencyRole.Owner)
            {
                throw new PermissionDeniedException(Permission.MembershipsGrant);
            }
        }

        IReadOnlyList<Membership> active = await _memberships
            .ListActiveForOrganizationAsync(command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        int otherActiveOwners = active.Count(x =>
            x.Role == AgencyRole.Owner && x.Id != membership.Id);

        AgencyRole priorRole = membership.Role;

        // Demoting the last owner is removing the last owner.
        membership.Revoke(actor, _clock.UtcNow, otherActiveOwners);

        Membership replacement = Membership.Grant(
            command.OrganizationId, membership.UserId, command.Role, actor, _clock.UtcNow);

        _memberships.Add(replacement);

        _audit.Record(
            AuditAction.MembershipRevoked,
            entityType: nameof(Membership),
            entityId: membership.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.MembershipsRevoke,
            semanticDelta: new
            {
                UserId = membership.UserId.ToString(),
                PriorRole = priorRole.ToString(),
                Reason = "Role changed",
            });

        _audit.Record(
            AuditAction.MembershipGranted,
            entityType: nameof(Membership),
            entityId: replacement.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.MembershipsGrant,
            semanticDelta: new
            {
                UserId = membership.UserId.ToString(),
                PriorRole = priorRole.ToString(),
                Role = command.Role.ToString(),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ChangeMemberRoleResult(replacement.Id);
    }
}
