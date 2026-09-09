using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Releases;
using Xunit;

namespace AgencyOS.Tests.Unit.Authorization;

/// <summary>
/// The grant model: which roles confer which permissions, and when a membership
/// stops conferring anything.
/// </summary>
public sealed class AuthorizationModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A stronger role never sees less than a weaker one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written after M15 found the owner of an agency unable to read their own
    /// deals, contracts and projects. Twenty-nine grants a member held were absent
    /// from the owner, and nine of those an <em>observer</em> held — the weakest
    /// role in the system could read records the strongest could not.
    /// </para>
    /// <para>
    /// It survived because the four role sets were four independent literal lists,
    /// and because every deal and project integration test acts as a member. Only
    /// running the application as the owner exposed it, as three workspaces
    /// answering 403.
    /// </para>
    /// <para>
    /// This asserts the shape of the model rather than individual grants, so a
    /// future milestone that adds a permission to one role cannot quietly leave a
    /// stronger role behind.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(AgencyRole.Observer, AgencyRole.Member)]
    [InlineData(AgencyRole.Member, AgencyRole.Owner)]
    [InlineData(AgencyRole.Administrator, AgencyRole.Owner)]
    public void AStrongerRoleContainsTheWeakerOne(AgencyRole weaker, AgencyRole stronger)
    {
        IReadOnlySet<string> lesser = RolePermissions.For(weaker);
        IReadOnlySet<string> greater = RolePermissions.For(stronger);

        List<string> missing = [.. lesser.Except(greater).Order()];

        Assert.True(
            missing.Count == 0,
            $"{stronger} is missing {missing.Count} permission(s) that {weaker} holds, "
                + $"so the weaker role can reach what the stronger cannot: "
                + string.Join(", ", missing));
    }

    /// <summary>
    /// Administrator and Member are different axes, not a ladder.
    /// </summary>
    /// <remarks>
    /// Asserted so the containment above is not mistaken for a total order. An
    /// administrator oversees and does not trade: they read the agency's records
    /// and cannot write a deal. A member does the opposite. Only the owner holds
    /// both, which is what makes ownership distinct from seniority.
    /// </remarks>
    [Fact]
    public void AdministratorAndMemberAreNotComparable()
    {
        IReadOnlySet<string> administrator = RolePermissions.For(AgencyRole.Administrator);
        IReadOnlySet<string> member = RolePermissions.For(AgencyRole.Member);

        Assert.NotEmpty(administrator.Except(member));
        Assert.NotEmpty(member.Except(administrator));
    }

    [Fact]
    public void EveryRoleGrantsOnlyKnownPermissions()
    {
        foreach (AgencyRole role in Enum.GetValues<AgencyRole>())
        {
            foreach (string permission in RolePermissions.For(role))
            {
                Assert.Contains(permission, Permission.All);
            }
        }
    }

    [Theory]
    [InlineData(AgencyRole.Observer, false)]
    [InlineData(AgencyRole.Member, false)]
    [InlineData(AgencyRole.Administrator, true)]
    [InlineData(AgencyRole.Owner, true)]
    public void OrganizationCreation_RequiresAnAdministrativeRole(AgencyRole role, bool expected)
    {
        Assert.Equal(expected, RolePermissions.For(role).Contains(Permission.OrganizationsCreate));
    }

    [Theory]
    [InlineData(AgencyRole.Observer, false)]
    [InlineData(AgencyRole.Member, false)]
    [InlineData(AgencyRole.Administrator, false)]
    [InlineData(AgencyRole.Owner, true)]
    public void ReleasePolicyManagement_IsOwnerOnly(AgencyRole role, bool expected)
    {
        Assert.Equal(expected, RolePermissions.For(role).Contains(Permission.ReleasePolicyManage));
    }

    [Theory]
    [InlineData(AgencyRole.Observer)]
    [InlineData(AgencyRole.Member)]
    public void NonAdministrativeRoles_CannotReadTheAuditTrail(AgencyRole role)
    {
        Assert.DoesNotContain(Permission.AuditRead, RolePermissions.For(role));
    }

    /// <summary>
    /// A role value the build does not recognize confers nothing. Failing closed
    /// matters if a row ever arrives from a newer schema.
    /// </summary>
    [Fact]
    public void UnknownRole_ConfersNothing()
    {
        Assert.Empty(RolePermissions.For((AgencyRole)999));
    }

    [Fact]
    public void ActiveMembership_ConfersItsRolePermissions()
    {
        Membership membership = Grant(AgencyRole.Administrator);

        Assert.Contains(Permission.OrganizationsCreate, membership.EffectivePermissions);
    }

    /// <summary>Revocation is an authorization change, not a cosmetic one.</summary>
    [Fact]
    public void RevokedMembership_ConfersNothing()
    {
        Membership membership = Grant(AgencyRole.Owner);
        membership.Revoke(UserId.New(), Now);

        Assert.Empty(membership.EffectivePermissions);
        Assert.Equal(MembershipStatus.Revoked, membership.Status);
        Assert.Equal(Now, membership.RevokedAt);
    }

    /// <summary>A membership is never deleted, so historical authority stays explainable.</summary>
    [Fact]
    public void RevokedMembership_RetainsItsHistory()
    {
        UserId revoker = UserId.New();
        Membership membership = Grant(AgencyRole.Member);

        membership.Revoke(revoker, Now);

        Assert.Equal(revoker, membership.RevokedBy);
        Assert.Equal(AgencyRole.Member, membership.Role);
    }

    [Fact]
    public void Membership_CannotBeRevokedTwice()
    {
        Membership membership = Grant(AgencyRole.Member);
        membership.Revoke(UserId.New(), Now);

        Assert.Throws<DomainException>(() => membership.Revoke(UserId.New(), Now));
    }

    [Fact]
    public void Membership_RejectsAnUndefinedRole()
    {
        Assert.Throws<DomainException>(() =>
            Membership.Grant(OrganizationId.New(), UserId.New(), (AgencyRole)999, UserId.New(), Now));
    }

    [Fact]
    public void SuspendedUser_CannotAct()
    {
        User user = User.Register("subject-1", "Test User", "test@agencyos.invalid", Now);
        Assert.True(user.CanAct);

        user.Suspend(Now);
        Assert.False(user.CanAct);

        user.Reinstate(Now);
        Assert.True(user.CanAct);
    }

    [Fact]
    public void Organization_ArchivesRatherThanDisappearing()
    {
        Organization organization = Organization.Create("Test Agency", null, OrganizationType.Agency, UserId.New(), Now);

        organization.Archive(Now);

        Assert.Equal(OrganizationStatus.Archived, organization.Status);
        Assert.Equal("Test Agency", organization.Name);
        Assert.Throws<DomainException>(() => organization.Archive(Now));
    }

    [Fact]
    public void Organization_RejectsABlankName()
    {
        Assert.Throws<DomainException>(() =>
            Organization.Create("   ", null, OrganizationType.Agency, UserId.New(), Now));
    }

    /// <summary>
    /// Mirrors <c>canonical_data_allowed_from: alpha</c> in config/version-policy.yaml.
    /// </summary>
    [Theory]
    [InlineData(ReleaseRing.Forge, false)]
    [InlineData(ReleaseRing.Lab, false)]
    [InlineData(ReleaseRing.Nightly, false)]
    [InlineData(ReleaseRing.Alpha, true)]
    [InlineData(ReleaseRing.Beta, true)]
    [InlineData(ReleaseRing.Rc, true)]
    [InlineData(ReleaseRing.Stable, true)]
    public void RealDataIsPermittedOnlyFromAlphaOnward(ReleaseRing ring, bool expected)
    {
        Assert.Equal(expected, ReleaseRingNames.AllowsRealData(ring));
    }

    [Theory]
    [InlineData("forge", ReleaseRing.Forge)]
    [InlineData("alpha", ReleaseRing.Alpha)]
    [InlineData("RC", ReleaseRing.Rc)]
    [InlineData(" stable ", ReleaseRing.Stable)]
    public void ReleaseRing_ParsesConfigWireNames(string value, ReleaseRing expected)
    {
        Assert.True(ReleaseRingNames.TryParse(value, out ReleaseRing ring));
        Assert.Equal(expected, ring);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("production")]
    public void ReleaseRing_RejectsUnknownNames(string? value)
    {
        Assert.False(ReleaseRingNames.TryParse(value, out _));
    }

    private static Membership Grant(AgencyRole role) =>
        Membership.Grant(OrganizationId.New(), UserId.New(), role, UserId.New(), Now);
}
