using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Infrastructure.Authorization;

/// <summary>
/// Evaluates permissions from the user's active memberships.
/// </summary>
/// <remarks>
/// <para>
/// Reads current state on every call rather than trusting anything carried in a
/// token or a session. Revoking a membership therefore takes effect on the next
/// request, not whenever a cached claim happens to expire - which is what makes
/// revocation an actual control.
/// </para>
/// <para>
/// A suspended or deactivated user is refused regardless of what their
/// memberships say.
/// </para>
/// </remarks>
internal sealed class PermissionEvaluator : IPermissionEvaluator
{
    private readonly IMembershipRepository _memberships;
    private readonly IUserRepository _users;

    public PermissionEvaluator(IMembershipRepository memberships, IUserRepository users)
    {
        _memberships = memberships;
        _users = users;
    }

    public async Task<bool> HasPermissionAsync(
        UserId userId,
        string permission,
        OrganizationId? scope,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);

        if (user is null || !user.CanAct)
        {
            return false;
        }

        IReadOnlyList<Membership> active = await _memberships
            .ListActiveForUserAsync(userId, cancellationToken)
            .ConfigureAwait(false);

        foreach (Membership membership in active)
        {
            // A scoped check only accepts authority held inside that organization.
            if (scope.HasValue && membership.OrganizationId != scope.Value)
            {
                continue;
            }

            if (membership.EffectivePermissions.Contains(permission))
            {
                return true;
            }
        }

        return false;
    }
}
