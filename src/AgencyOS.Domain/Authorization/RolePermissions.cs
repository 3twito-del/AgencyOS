namespace AgencyOS.Domain.Authorization;

/// <summary>
/// Maps a role to the permissions it confers.
/// </summary>
/// <remarks>
/// A single in-domain table, so the grant model is reviewable in one place rather
/// than scattered across endpoint declarations. Each role is written out in full
/// rather than inheriting from the role below it: a widening should be visible in
/// the diff, not implied by a chain.
/// </remarks>
public static class RolePermissions
{
    private static readonly IReadOnlySet<string> ObserverPermissions = Freeze(
        Permission.OrganizationsRead,
        Permission.MembershipsRead,
        Permission.ReleasePolicyRead);

    private static readonly IReadOnlySet<string> MemberPermissions = Freeze(
        Permission.OrganizationsRead,
        Permission.MembershipsRead,
        Permission.ReleasePolicyRead);

    private static readonly IReadOnlySet<string> AdministratorPermissions = Freeze(
        Permission.OrganizationsRead,
        Permission.OrganizationsCreate,
        Permission.MembershipsRead,
        Permission.MembershipsGrant,
        Permission.MembershipsRevoke,
        Permission.AuditRead,
        Permission.ReleasePolicyRead);

    private static readonly IReadOnlySet<string> OwnerPermissions = Freeze(
        Permission.OrganizationsRead,
        Permission.OrganizationsCreate,
        Permission.OrganizationsArchive,
        Permission.MembershipsRead,
        Permission.MembershipsGrant,
        Permission.MembershipsRevoke,
        Permission.AuditRead,
        Permission.ReleasePolicyRead,
        Permission.ReleasePolicyManage);

    private static readonly IReadOnlySet<string> NoPermissions = Freeze();

    /// <summary>Gets the permissions conferred by <paramref name="role"/>.</summary>
    /// <remarks>
    /// An unrecognized role confers nothing. Failing closed matters here: a role
    /// value arriving from a newer schema must not be read as unrestricted.
    /// </remarks>
    public static IReadOnlySet<string> For(AgencyRole role) => role switch
    {
        AgencyRole.Observer => ObserverPermissions,
        AgencyRole.Member => MemberPermissions,
        AgencyRole.Administrator => AdministratorPermissions,
        AgencyRole.Owner => OwnerPermissions,
        _ => NoPermissions,
    };

    private static IReadOnlySet<string> Freeze(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.Ordinal);
}
