using AgencyOS.Domain.Common;
using AgencyOS.Domain.Releases;
using Xunit;

namespace AgencyOS.Tests.Unit.Releases;

/// <summary>
/// The decision table from <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>.
/// </summary>
/// <remarks>
/// Severity order is the substance of these tests: revocation outranks contract
/// incompatibility, which outranks being behind. A client that is both revoked
/// and merely old must be told it is revoked.
/// </remarks>
public sealed class ReleasePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CurrentClient_IsUnrestricted()
    {
        ReleaseDecision decision = Evaluate("0.3.0", contract: 1);

        Assert.Equal(UpdatePolicy.None, decision.Policy);
        Assert.False(decision.BlocksProtectedMutations);
    }

    [Fact]
    public void SupportedButOlderClient_GetsTheConfiguredNonBlockingPolicy()
    {
        ReleaseDecision decision = Evaluate("0.2.0", contract: 1);

        Assert.Equal(UpdatePolicy.Recommended, decision.Policy);
        Assert.False(decision.BlocksProtectedMutations);
    }

    [Fact]
    public void BehindPolicy_IsConfigurablePerRing()
    {
        ReleasePolicy policy = Policy(behindPolicy: UpdatePolicy.Available);

        ReleaseDecision decision = policy.Evaluate(Client("0.2.0", 1), Now);

        Assert.Equal(UpdatePolicy.Available, decision.Policy);
        Assert.False(decision.BlocksProtectedMutations);
    }

    [Fact]
    public void ClientBelowMinimum_MustUpdateAndIsBlocked()
    {
        ReleaseDecision decision = Evaluate("0.1.9", contract: 1);

        Assert.Equal(UpdatePolicy.Mandatory, decision.Policy);
        Assert.True(decision.BlocksProtectedMutations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void ClientOutsideContractRange_MustUpdateAndIsBlocked(int contract)
    {
        ReleaseDecision decision = Evaluate("0.3.0", contract);

        Assert.Equal(UpdatePolicy.Mandatory, decision.Policy);
        Assert.True(decision.BlocksProtectedMutations);
    }

    [Fact]
    public void RevokedVersion_IsRefused()
    {
        ReleasePolicy policy = Policy(revoked: ["0.2.5"]);

        ReleaseDecision decision = policy.Evaluate(Client("0.2.5", 1), Now);

        Assert.Equal(UpdatePolicy.Revoked, decision.Policy);
        Assert.True(decision.BlocksProtectedMutations);
    }

    [Fact]
    public void KillSwitch_RefusesEveryClientOnTheRing()
    {
        ReleasePolicy policy = Policy();
        policy.EngageKillSwitch(Now);

        ReleaseDecision decision = policy.Evaluate(Client("0.3.0", 1), Now);

        Assert.Equal(UpdatePolicy.Revoked, decision.Policy);
        Assert.True(decision.BlocksProtectedMutations);
        Assert.True(decision.KillSwitch);
    }

    /// <summary>Revocation outranks every other consideration.</summary>
    [Fact]
    public void RevocationOutranksContractIncompatibility()
    {
        ReleasePolicy policy = Policy(revoked: ["0.2.5"]);

        ReleaseDecision decision = policy.Evaluate(Client("0.2.5", contract: 99), Now);

        Assert.Equal(UpdatePolicy.Revoked, decision.Policy);
    }

    /// <summary>Contract incompatibility outranks merely being behind.</summary>
    [Fact]
    public void ContractIncompatibilityOutranksBeingBehind()
    {
        ReleaseDecision decision = Evaluate("0.2.0", contract: 99);

        Assert.Equal(UpdatePolicy.Mandatory, decision.Policy);
        Assert.Contains("contract", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateDeadlineInTheFuture_DoesNotYetBlock()
    {
        ReleasePolicy policy = Policy(mandatoryAfter: Now.AddDays(7));

        ReleaseDecision decision = policy.Evaluate(Client("0.2.0", 1), Now);

        Assert.Equal(UpdatePolicy.Recommended, decision.Policy);
        Assert.False(decision.BlocksProtectedMutations);
    }

    [Fact]
    public void UpdateDeadlineInThePast_Blocks()
    {
        ReleasePolicy policy = Policy(mandatoryAfter: Now.AddDays(-1));

        ReleaseDecision decision = policy.Evaluate(Client("0.2.0", 1), Now);

        Assert.Equal(UpdatePolicy.Mandatory, decision.Policy);
        Assert.True(decision.BlocksProtectedMutations);
    }

    /// <summary>A deadline never affects a client that is already current.</summary>
    [Fact]
    public void UpdateDeadline_DoesNotAffectACurrentClient()
    {
        ReleasePolicy policy = Policy(mandatoryAfter: Now.AddDays(-1));

        ReleaseDecision decision = policy.Evaluate(Client("0.3.0", 1), Now);

        Assert.Equal(UpdatePolicy.None, decision.Policy);
        Assert.False(decision.BlocksProtectedMutations);
    }

    [Fact]
    public void NoPolicy_FailsClosed()
    {
        ReleaseDecision decision = ReleaseDecision.NoPolicy("no row");

        Assert.Equal(UpdatePolicy.Mandatory, decision.Policy);
        Assert.True(decision.BlocksProtectedMutations);
    }

    [Fact]
    public void RevokeVersion_IsIdempotent()
    {
        ReleasePolicy policy = Policy();

        policy.RevokeVersion("0.2.5", Now);
        policy.RevokeVersion("0.2.5", Now);

        Assert.Single(policy.RevokedVersions);
    }

    // A blocking state cannot be configured as the answer for a merely-old client:
    // that would make "behind" indistinguishable from "refused".
    [Theory]
    [InlineData(UpdatePolicy.Mandatory)]
    [InlineData(UpdatePolicy.Revoked)]
    public void BehindPolicy_RejectsBlockingStates(UpdatePolicy behind)
    {
        Assert.Throws<DomainException>(() => Policy(behindPolicy: behind));
    }

    [Fact]
    public void Create_RejectsAMinimumAboveTheLatestVersion()
    {
        Assert.Throws<DomainException>(() => ReleasePolicy.Create(
            "windows-x64",
            ReleaseRing.Alpha,
            latestVersion: "0.2.0",
            minimumSupportedVersion: "0.3.0",
            apiContractMinimum: 1,
            apiContractMaximum: 1,
            now: Now));
    }

    [Fact]
    public void Create_RejectsAnInvertedContractRange()
    {
        Assert.Throws<DomainException>(() => ReleasePolicy.Create(
            "windows-x64",
            ReleaseRing.Alpha,
            latestVersion: "0.3.0",
            minimumSupportedVersion: "0.2.0",
            apiContractMinimum: 5,
            apiContractMaximum: 2,
            now: Now));
    }

    private static ReleaseDecision Evaluate(string version, int contract) =>
        Policy().Evaluate(Client(version, contract), Now);

    private static ClientIdentity Client(string version, int contract) => new(
        "windows-x64",
        ReleaseRing.Alpha,
        ClientVersion.Parse(version),
        contract);

    private static ReleasePolicy Policy(
        UpdatePolicy behindPolicy = UpdatePolicy.Recommended,
        IReadOnlyList<string>? revoked = null,
        DateTimeOffset? mandatoryAfter = null)
    {
        return ReleasePolicy.Create(
            platform: "windows-x64",
            ring: ReleaseRing.Alpha,
            latestVersion: "0.3.0",
            minimumSupportedVersion: "0.2.0",
            apiContractMinimum: 1,
            apiContractMaximum: 1,
            now: Now,
            behindPolicy: behindPolicy,
            mandatoryAfterUtc: mandatoryAfter,
            revokedVersions: revoked);
    }
}
