namespace AgencyOS.Domain.Audit;

/// <summary>
/// Action names recorded in the audit trail.
/// </summary>
/// <remarks>
/// Strings rather than an enum, for the same reason as <c>Permission</c>: audit
/// rows outlive the build that wrote them, and a stored value must stay readable
/// after an enum is renumbered or a member is removed.
/// </remarks>
public static class AuditAction
{
    public const string OrganizationCreated = "organization.created";
    public const string OrganizationArchived = "organization.archived";

    public const string MembershipGranted = "membership.granted";
    public const string MembershipRevoked = "membership.revoked";

    public const string UserRegistered = "user.registered";
}
