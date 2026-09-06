using System.Security.Claims;
using AgencyOS.Api.Authentication;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace AgencyOS.Api.Authorization;

/// <summary>Requires a named permission.</summary>
/// <param name="Permission">A value from <c>AgencyOS.Domain.Authorization.Permission</c>.</param>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Policy naming convention for permission-backed endpoints.</summary>
public static class PermissionPolicy
{
    /// <summary>Builds the policy name for a permission.</summary>
    public static string Name(string permission) => $"perm:{permission}";
}

/// <summary>
/// Evaluates a <see cref="PermissionRequirement"/> against the caller's memberships.
/// </summary>
/// <remarks>
/// <para>
/// This is an early gate, not the authoritative check. It asks whether the caller
/// holds the permission through <em>any</em> active membership, which is enough to
/// reject an anonymous or plainly unauthorized caller before a handler runs.
/// </para>
/// <para>
/// The authoritative, organization-scoped check happens inside the command
/// handler, so it also applies to callers that never came through this pipeline.
/// See <c>IPermissionEvaluator</c>.
/// </para>
/// </remarks>
internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionEvaluator _permissions;

    public PermissionAuthorizationHandler(IPermissionEvaluator permissions) => _permissions = permissions;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        string? raw = context.User.FindFirstValue(AgencyOsAuthentication.UserIdClaim);

        if (!Guid.TryParse(raw, out Guid userId))
        {
            return;
        }

        bool permitted = await _permissions
            .HasPermissionAsync(new UserId(userId), requirement.Permission, scope: null)
            .ConfigureAwait(false);

        if (permitted)
        {
            context.Succeed(requirement);
        }
    }
}
