namespace AgencyOS.Domain.Authorization;

/// <summary>
/// Broad role held through a membership.
/// </summary>
/// <remarks>
/// <c>docs/07_SECURITY_AND_AUDIT.md</c> asks for RBAC for broad roles plus
/// policy/attribute checks for context. This enum is the RBAC half; the
/// contextual half is the organization scope the role is held within, so a role
/// never confers authority outside its own membership.
/// </remarks>
public enum AgencyRole
{
    /// <summary>Read-only access within the organization.</summary>
    Observer = 1,

    /// <summary>Day-to-day operational access.</summary>
    Member = 2,

    /// <summary>Manages memberships within the organization.</summary>
    Administrator = 3,

    /// <summary>Full control within the organization, including release policy.</summary>
    Owner = 4,
}
