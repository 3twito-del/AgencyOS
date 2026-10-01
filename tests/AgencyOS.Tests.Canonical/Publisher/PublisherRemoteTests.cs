using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>
/// Remote movement at each push, a seal that tries to change a byte the whitelist does
/// not name, and a remote that no longer holds the seal on final readback. Every remote
/// state here is real: another clone pushes to the same bare origin.
/// </summary>
public sealed class PublisherRemoteTests
{
    [Fact]
    public void RemoteMovementBeforeTheSemanticBasisPushIsRemoteBasisMismatch()
    {
        using PublisherFixture fixture = new();
        InterferingGit moved = new InterferingGit(fixture.PublisherGit()).Before("push", 1, () => fixture.ForeignPush());

        Receipt receipt = fixture.Publisher(moved).Stage(new PayloadBuilder(fixture).Build());

        Assert.True(receipt.FailureClass == FailureClasses.RemoteBasisMismatch, receipt.Render());
        Assert.Equal(Results.FailedRemoteVerification, receipt.Result);
        Assert.NotEqual(fixture.RemoteHead(), fixture.OperatorHead());
        Assert.False(fixture.ExistsAt(fixture.RemoteHead(), PublisherFlowTests.Pending));
    }

    [Fact]
    public void RemoteMovementBeforeTheSealPushIsRemoteMovedBeforeSeal()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        InterferingGit moved = new InterferingGit(fixture.PublisherGit()).Before("push", 2, () => fixture.ForeignPush());

        Receipt receipt = fixture.Publisher(moved).Authorize(new AuthorizationBuilder(fixture, basis).Build());

        Assert.True(receipt.FailureClass == FailureClasses.RemoteMovedBeforeSeal, receipt.Render());
        Assert.Equal(Results.FailedRemoteVerification, receipt.Result);
        string remote = fixture.RemoteHead();
        Assert.Contains("- Record: SA-1", fixture.Text(remote, PublisherFixture.Deltas), StringComparison.Ordinal);
        Assert.DoesNotContain("Status: PUBLISHED\n\nDetected: 2026-01-02", fixture.Text(remote, PublisherFixture.Deltas), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/notes.md", "Notes, quietly edited during the seal.\n")]
    [InlineData(PublisherFixture.CurrentState, "# Not the bound state\n")]
    [InlineData("docs/extra.md", "A new file.\n")]
    public void ASealThatChangesAnUnwhitelistedByteStopsBeforeCommitting(string path, string text)
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        PublisherHooks tamper = new() { AfterSealMaterialised = root => File.WriteAllText(Path.Combine(root, path), text) };
        string beforeSeal = string.Empty;

        Receipt receipt = fixture.Publisher(hooks: tamper).Authorize(new AuthorizationBuilder(fixture, basis).Build());
        beforeSeal = fixture.RemoteHead();

        Assert.True(receipt.FailureClass == FailureClasses.SubstantiveMutationDuringSeal, receipt.Render());
        Assert.Equal(Results.FailedSealValidation, receipt.Result);
        Assert.Equal(beforeSeal, fixture.OperatorHead());
        Assert.Equal(basis, fixture.Parent(beforeSeal));
        Assert.NotEqual(string.Empty, fixture.Status());
    }

    [Fact]
    public void ARemoteThatNoLongerHoldsTheSealFailsFinalReadback()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        InterferingGit moved = new InterferingGit(fixture.PublisherGit()).After("push", 2, () => fixture.ForeignPush());

        Receipt receipt = fixture.Publisher(moved).Authorize(new AuthorizationBuilder(fixture, basis).Build());

        Assert.True(receipt.FailureClass == FailureClasses.FinalReadbackMismatch, receipt.Render());
        Assert.Equal(Results.FailedRemoteVerification, receipt.Result);
        Assert.Equal(Receipt.None, receipt.SealingSha);
    }
}
