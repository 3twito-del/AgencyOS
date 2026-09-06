using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Authorization;

/// <summary>
/// Decides whether a user holds a permission, optionally within an organization.
/// </summary>
/// <remarks>
/// <para>
/// This is the authoritative check. It runs inside the application layer, so it
/// applies to every caller of a command handler rather than only to callers that
/// arrived over HTTP. The API declares the same permission on its endpoints as an
/// early gate, but the endpoint declaration is not what makes the operation safe.
/// </para>
/// <para>
/// <c>docs/07_SECURITY_AND_AUDIT.md</c>: never trust the Windows client to
/// enforce permissions.
/// </para>
/// </remarks>
public interface IPermissionEvaluator
{
    /// <summary>
    /// Determines whether <paramref name="userId"/> holds <paramref name="permission"/>.
    /// </summary>
    /// <param name="userId">The acting user.</param>
    /// <param name="permission">A value from <c>Permission</c>.</param>
    /// <param name="scope">
    /// The organization the permission must be held within, or <see langword="null"/>
    /// to accept the permission from any active membership.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasPermissionAsync(
        UserId userId,
        string permission,
        OrganizationId? scope,
        CancellationToken cancellationToken = default);
}
