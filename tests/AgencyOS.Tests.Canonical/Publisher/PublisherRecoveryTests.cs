using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>
/// Crash recovery (contract section 10). Each state is produced by really interrupting a
/// run at the point the contract names, and recovery is derived from git alone.
/// </summary>
public sealed class PublisherRecoveryTests
{
    private static Receipt Resume(PublisherFixture fixture) => fixture.Publisher().Resume(PublisherFixture.Branch, PayloadBuilder.DeltaId);

    private static InterferingGit CrashAt(PublisherFixture fixture, int push, bool after) =>
        after
            ? new InterferingGit(fixture.PublisherGit()).After("push", push, () => throw new SimulatedCrash())
            : new InterferingGit(fixture.PublisherGit()).Before("push", push, () => throw new SimulatedCrash());

    [Fact]
    public void R0AcceptedButUnstagedStartsAtP1()
    {
        using PublisherFixture fixture = new();

        Assert.Equal(Results.StoppedPrecondition, Resume(fixture).Result);
        Assert.Equal(Results.BasisVerifiedAwaitingSeal, fixture.Publisher().Stage(new PayloadBuilder(fixture).Build()).Result);
    }

    [Fact]
    public void R1ALocalSemanticBasisIsValidatedPushedAndReadBack()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture);
        Assert.Throws<SimulatedCrash>(() => fixture.Publisher(CrashAt(fixture, 1, after: false)).Stage(payload.Build()));
        string local = fixture.OperatorHead();
        Assert.Equal(fixture.Start, fixture.RemoteHead());

        Receipt receipt = Resume(fixture);

        Assert.True(receipt.Result == Results.BasisVerifiedAwaitingSeal, receipt.Render());
        Assert.Equal(local, fixture.RemoteHead());
    }

    [Fact]
    public void R1AlsoRecoversThroughStageWithTheSamePayload()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture);
        Assert.Throws<SimulatedCrash>(() => fixture.Publisher(CrashAt(fixture, 1, after: false)).Stage(payload.Build()));

        Assert.Equal(Results.BasisVerifiedAwaitingSeal, fixture.Publisher().Stage(payload.Build()).Result);
    }

    [Fact]
    public void R2TheHoldIsReReadAndNeverSealedWithoutAnAuthorization()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);

        Receipt receipt = Resume(fixture);

        Assert.Equal(Results.BasisVerifiedAwaitingSeal, receipt.Result);
        Assert.Equal(basis, fixture.RemoteHead());
        Assert.Equal(basis, fixture.OperatorHead());
    }

    [Fact]
    public void R3ALocalAuthorizationCommitIsPushedAndTheSealCompleted()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        Assert.Throws<SimulatedCrash>(() => fixture.Publisher(CrashAt(fixture, 1, after: false)).Authorize(new AuthorizationBuilder(fixture, basis).Build()));
        Assert.Equal(basis, fixture.RemoteHead());
        Assert.Equal(basis, fixture.LocalParent(fixture.OperatorHead()));

        Receipt receipt = Resume(fixture);

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
    }

    [Fact]
    public void R4AnAuthorizationCommitOnTheRemoteIsBoundAndSealed()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        Assert.Throws<SimulatedCrash>(() => fixture.Publisher(CrashAt(fixture, 1, after: true)).Authorize(new AuthorizationBuilder(fixture, basis).Build()));
        string authorization = fixture.RemoteHead();
        Assert.Equal(basis, fixture.Parent(authorization));

        Receipt receipt = Resume(fixture);

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        Assert.Equal(authorization, fixture.Parent(fixture.RemoteHead()));
    }

    [Fact]
    public void R5ALocalSealIsPushedAndReadBack()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        Assert.Throws<SimulatedCrash>(() => fixture.Publisher(CrashAt(fixture, 2, after: false)).Authorize(new AuthorizationBuilder(fixture, basis).Build()));
        string seal = fixture.OperatorHead();
        Assert.NotEqual(seal, fixture.RemoteHead());

        Receipt receipt = Resume(fixture);

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        Assert.Equal(seal, fixture.RemoteHead());
    }

    [Fact]
    public void R6ASealOnTheRemoteIsReadBackWithoutMutation()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        Assert.Throws<SimulatedCrash>(() => fixture.Publisher(CrashAt(fixture, 2, after: true)).Authorize(new AuthorizationBuilder(fixture, basis).Build()));
        string seal = fixture.RemoteHead();

        Receipt receipt = Resume(fixture);

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        Assert.Equal(seal, fixture.RemoteHead());
        Assert.Equal(seal, receipt.SealingSha);
    }

    [Fact]
    public void R7RerunningAPublishedTransitionChangesNothing()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        Assert.Equal(Results.PublishedVerified, fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, basis).Build()).Result);
        string remote = fixture.RemoteHead();
        string local = fixture.OperatorHead();
        SortedDictionary<string, string> origin = FixtureSnapshot.Take(fixture.Origin);
        SortedDictionary<string, string> tree = Tracked(fixture);

        Receipt receipt = Resume(fixture);

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        Assert.Contains(receipt.UnresolvedWarnings, x => x.StartsWith("Already published", StringComparison.Ordinal));
        Assert.Equal(remote, fixture.RemoteHead());
        Assert.Equal(local, fixture.OperatorHead());
        Assert.Equal(origin, FixtureSnapshot.Take(fixture.Origin));
        Assert.Equal(tree, Tracked(fixture));
    }

    [Fact]
    public void AStateThatMatchesNoRowIsPreconditionDrift()
    {
        using PublisherFixture fixture = new();
        PublisherFlowTests.StageDefault(fixture);
        fixture.CommitInOperator("one local commit", ("docs/notes.md", "Local 1.\n"));
        fixture.CommitInOperator("two local commits", ("docs/notes.md", "Local 2.\n"));

        Receipt receipt = Resume(fixture);

        Assert.True(receipt.FailureClass == FailureClasses.PreconditionDrift, receipt.Render());
    }

    [Fact]
    public void AnAuthorizationCommitInHistoryIsNotInferredAsBindingANewerBasis()
    {
        using PublisherFixture fixture = new();
        (string history, _) = PublisherAuthorizationTests.InvalidatedFirstAuthorization(fixture);
        PayloadBuilder replacement = new(fixture, fixture.RemoteHead())
        {
            Mode = "ADVANCE_EXISTING_DELTA",
            Kind = "existing",
            EntryText = PayloadBuilder.Entry(seal: history),
        };
        string second = PublisherFlowTests.StageDefault(fixture, replacement);

        Receipt receipt = Resume(fixture);

        Assert.Equal(Results.BasisVerifiedAwaitingSeal, receipt.Result);
        Assert.Equal(second, fixture.RemoteHead());
    }

    /// <summary>The operator's working tree, without .git: what a person would see.</summary>
    private static SortedDictionary<string, string> Tracked(PublisherFixture fixture)
    {
        SortedDictionary<string, string> all = FixtureSnapshot.Take(fixture.Operator);
        return new SortedDictionary<string, string>(
            all.Where(x => !x.Key.StartsWith(".git", StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value),
            StringComparer.Ordinal);
    }
}
