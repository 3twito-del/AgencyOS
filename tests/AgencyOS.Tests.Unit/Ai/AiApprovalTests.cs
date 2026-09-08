using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Ai;

/// <summary>
/// What an approval will and will not authorize.
/// </summary>
/// <remarks>
/// The most security-sensitive object in M12, tested at the level where it is
/// enforced. Every failure here is silent in production: an approval that
/// authorized a different action than the one shown, or one that outlived its
/// window, looks exactly like an approval that worked (§13, §14).
/// </remarks>
public sealed class AiApprovalTests
{
    private static readonly OrganizationId Org = new(Guid.CreateVersion7());
    private static readonly AgentRunId Run = AgentRunId.New();
    private static readonly AiToolRequestId Request = AiToolRequestId.New();
    private static readonly UserId Approver = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private const string Fingerprint =
        "9f2c4b1a7d6e5f0938271645acbdef0123456789abcdef0123456789abcdef01";

    /// <summary>Nothing is authorized until somebody decides.</summary>
    [Fact]
    public void APendingApproval_AuthorizesNothing()
    {
        AiApproval approval = Pending();

        Assert.Equal(ApprovalDecision.Pending, approval.Decision);
        Assert.False(approval.Authorizes(Fingerprint, Now));
    }

    [Fact]
    public void AnApprovedApproval_AuthorizesItsOwnFingerprint()
    {
        AiApproval approval = Pending();
        approval.Approve(Approver, Now, approval.Version);

        Assert.True(approval.Authorizes(Fingerprint, Now));
        Assert.Equal(Approver, approval.DecidedBy);
        Assert.Equal(Now, approval.DecidedAt);
    }

    /// <summary>
    /// The property the whole design turns on.
    /// </summary>
    /// <remarks>
    /// Changing the arguments changes the fingerprint, and an approval granted
    /// against the old one authorizes nothing. This is what makes "approve one
    /// action, execute another" impossible rather than merely discouraged (§13).
    /// </remarks>
    [Fact]
    public void ADifferentFingerprint_IsNotAuthorized()
    {
        AiApproval approval = Pending();
        approval.Approve(Approver, Now, approval.Version);

        Assert.False(approval.Authorizes(
            "0000000000000000000000000000000000000000000000000000000000000000", Now));
    }

    [Fact]
    public void ARejectedApproval_AuthorizesNothing()
    {
        AiApproval approval = Pending();
        approval.Reject(Approver, "Wrong person.", Now, approval.Version);

        Assert.Equal(ApprovalDecision.Rejected, approval.Decision);
        Assert.False(approval.Authorizes(Fingerprint, Now));
    }

    /// <summary>An approval that lapsed authorizes nothing, even though it was granted.</summary>
    [Fact]
    public void AnApprovalPastItsWindow_AuthorizesNothing()
    {
        AiApproval approval = Pending();
        approval.Approve(Approver, Now, approval.Version);

        Assert.False(approval.Authorizes(Fingerprint, Now.AddMinutes(31)));
    }

    /// <summary>The boundary is inclusive. A decision at the last second stands.</summary>
    [Fact]
    public void AnApprovalAtTheExactExpiry_StillAuthorizes()
    {
        AiApproval approval = Pending();
        approval.Approve(Approver, Now, approval.Version);

        Assert.True(approval.Authorizes(Fingerprint, approval.ExpiresAt));
    }

    [Fact]
    public void AnExpiredApproval_CannotBeApproved()
    {
        AiApproval approval = Pending();
        approval.Expire(Now.AddMinutes(31), approval.Version);

        Assert.Equal(ApprovalDecision.Expired, approval.Decision);
        Assert.Throws<DomainException>(() =>
            approval.Approve(Approver, Now.AddMinutes(32), approval.Version));
    }

    /// <summary>Expiry has a moment and no person, because nobody made it.</summary>
    [Fact]
    public void ExpiryRecordsNoDecider()
    {
        AiApproval approval = Pending();
        approval.Expire(Now.AddMinutes(31), approval.Version);

        Assert.Null(approval.DecidedBy);
        Assert.NotNull(approval.DecidedAt);
    }

    /// <summary>A decision is made once.</summary>
    [Fact]
    public void ADecidedApproval_CannotBeDecidedAgain()
    {
        AiApproval approval = Pending();
        approval.Approve(Approver, Now, approval.Version);

        Assert.Throws<DomainException>(() =>
            approval.Reject(Approver, "Changed my mind.", Now, approval.Version));
    }

    /// <summary>
    /// Two people deciding the same approval is a conflict, not a race.
    /// </summary>
    /// <remarks>
    /// The version is checked before the state, so the second decider is told the
    /// row moved rather than being told the approval is in the wrong state — which
    /// is the difference between "somebody beat you to it" and "you are confused".
    /// </remarks>
    [Fact]
    public void AStaleVersion_IsRefused()
    {
        AiApproval approval = Pending();
        approval.Approve(Approver, Now, approval.Version);

        Assert.Throws<ConcurrencyConflictException>(() =>
            approval.Reject(Approver, null, Now, 1));
    }

    [Fact]
    public void AnApprovalWithNoWindow_IsRefused()
    {
        Assert.Throws<DomainException>(() => AiApproval.Request(
            Org, Run, Request, Fingerprint, Approver, Now, TimeSpan.Zero));
    }

    private static AiApproval Pending() => AiApproval.Request(
        Org, Run, Request, Fingerprint, Approver, Now, TimeSpan.FromMinutes(30));
}
