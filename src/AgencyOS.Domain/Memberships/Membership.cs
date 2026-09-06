using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Memberships;

/// <summary>
/// A user's role within an organization. This is the unit authorization is evaluated over.
/// </summary>
/// <remarks>
/// <para>
/// A membership is never deleted. Revoking sets <see cref="MembershipStatus.Revoked"/>
/// and stamps <see cref="RevokedAt"/>, so the question "what could this person do
/// last March?" stays answerable - the effective-date history that
/// <c>docs/08_DATA_MODEL_FOUNDATION.md</c> asks for.
/// </para>
/// <para>
/// A revoked membership confers no permissions, which is what makes revocation an
/// authorization change rather than a cosmetic one.
/// </para>
/// </remarks>
public sealed class Membership
{
    private Membership()
    {
    }

    public MembershipId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public UserId UserId { get; private set; }

    public AgencyRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public UserId GrantedBy { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public UserId? RevokedBy { get; private set; }

    public static Membership Grant(
        OrganizationId organizationId,
        UserId userId,
        AgencyRole role,
        UserId grantedBy,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainException($"Unknown role '{role}'.");
        }

        return new Membership
        {
            Id = MembershipId.New(),
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            Status = MembershipStatus.Active,
            GrantedAt = now,
            GrantedBy = grantedBy,
        };
    }

    public void Revoke(UserId revokedBy, DateTimeOffset now)
    {
        if (Status == MembershipStatus.Revoked)
        {
            throw new DomainException("Membership is already revoked.");
        }

        Status = MembershipStatus.Revoked;
        RevokedAt = now;
        RevokedBy = revokedBy;
    }

    /// <summary>Gets the permissions this membership currently confers.</summary>
    public IReadOnlySet<string> EffectivePermissions =>
        Status == MembershipStatus.Active
            ? RolePermissions.For(Role)
            : RolePermissions.For((AgencyRole)0);
}
