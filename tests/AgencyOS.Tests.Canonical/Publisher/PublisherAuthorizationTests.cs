using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>
/// The seal authorization gate: the binding rule, remote movement around it, the
/// append-only history, and replacement bases.
/// </summary>
public sealed class PublisherAuthorizationTests
{
    private static void AssertInvalid(PublisherFixture fixture, Receipt receipt, string basis)
    {
        Assert.True(receipt.FailureClass == FailureClasses.SealAuthorizationInvalid, receipt.Render());
        Assert.Equal(Results.StoppedAuthority, receipt.Result);
        Assert.Equal(basis, fixture.RemoteHead());
        Assert.DoesNotContain("- Record: SA-1", fixture.Text(fixture.RemoteHead(), PublisherFixture.Deltas), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("delta")]
    [InlineData("basis")]
    [InlineData("current")]
    [InlineData("payload")]
    [InlineData("authority")]
    [InlineData("reference")]
    public void AnAuthorizationThatDoesNotBindTheBasisIsRefused(string defect)
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        AuthorizationBuilder authorization = new(fixture, basis);

        switch (defect)
        {
            case "delta": authorization.Delta = "DELTA-20260102-009"; break;
            case "basis": authorization.Basis = fixture.Start; break;
            case "current": authorization.Next = new string('1', 64); break;
            case "payload": authorization.Payload = new string('2', 64); break;
            case "authority": authorization.Authority = "OWNER"; break;
            default: authorization.Reference = "the chat on Tuesday"; break;
        }

        AssertInvalid(fixture, fixture.Publisher().Authorize(authorization.Build()), basis);
    }

    [Fact]
    public void ResumeAtTheHoldNeverSealsWithoutAnAuthorization()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);

        Receipt receipt = fixture.Publisher().Resume(PublisherFixture.Branch, PayloadBuilder.DeltaId);

        Assert.Equal(Results.BasisVerifiedAwaitingSeal, receipt.Result);
        Assert.Equal(basis, fixture.RemoteHead());
    }

    [Fact]
    public void AMachineFactOnlyTransitionStillNeedsAControlRoomSealAuthorization()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture) { Classes = ["MACHINE_VERIFIABLE_FACT"], References = [] };
        string basis = PublisherFlowTests.StageDefault(fixture, payload);

        Assert.Equal(Results.BasisVerifiedAwaitingSeal, fixture.Publisher().Resume(PublisherFixture.Branch, PayloadBuilder.DeltaId).Result);
        AssertInvalid(fixture, fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, basis) { Authority = "OWNER" }.Build()), basis);

        Receipt receipt = fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, basis).Build());

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        Assert.Contains("CONTROL_ROOM", receipt.BindingSealAuthorization, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnerRequiredTransitionNeedsAnOwnerSealAuthorization()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture)
        {
            Classes = ["CONTROL_ROOM", "OWNER"],
            OwnerApplies = true,
            Governing = ["DECISION-20260101-001"],
        };
        string basis = PublisherFlowTests.StageDefault(fixture, payload);

        AssertInvalid(fixture, fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, basis).Build()), basis);

        Receipt receipt = fixture.Publisher().Authorize(
            new AuthorizationBuilder(fixture, basis) { Authority = "OWNER", Reference = "DECISION-20260101-001" }.Build());

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        Assert.StartsWith("SA-1; OWNER;", receipt.BindingSealAuthorization, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizeCannotRunWithoutAStagedBasis()
    {
        using PublisherFixture fixture = new();
        AuthorizationBuilder authorization = new(fixture.Start, new string('1', 64), new string('2', 64));

        Receipt receipt = fixture.Publisher().Authorize(authorization.Build());

        Assert.NotEqual(Results.PublishedVerified, receipt.Result);
        Assert.Equal(fixture.Start, fixture.RemoteHead());
    }

    [Fact]
    public void ResumeClassifiesARemoteThatMovedPastAnUnauthorizedBasis()
    {
        using PublisherFixture fixture = new();
        PublisherFlowTests.StageDefault(fixture);
        fixture.ForeignPush();
        fixture.ResetOperatorToRemote();

        Receipt receipt = fixture.Publisher().Resume(PublisherFixture.Branch, PayloadBuilder.DeltaId);

        Assert.True(receipt.FailureClass == FailureClasses.RemoteBasisMismatch, receipt.Render());
    }

    [Fact]
    public void ResumeClassifiesARemoteThatMovedPastTheFinalBasisBeforeTheSeal()
    {
        using PublisherFixture fixture = new();
        InvalidatedFirstAuthorization(fixture);

        Receipt receipt = fixture.Publisher().Resume(PublisherFixture.Branch, PayloadBuilder.DeltaId);

        Assert.True(receipt.FailureClass == FailureClasses.RemoteMovedBeforeSeal, receipt.Render());
    }

    [Fact]
    public void RemoteMovementBetweenBasisAndAuthorizationInvalidatesIt()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        string foreign = fixture.ForeignPush();

        Receipt receipt = fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, basis).Build());

        Assert.True(receipt.FailureClass == FailureClasses.RemoteBasisMismatch, receipt.Render());
        Assert.Equal(Results.FailedRemoteVerification, receipt.Result);
        Assert.Equal(foreign, fixture.RemoteHead());
    }

    [Fact]
    public void AReplacementBasisWithTheSameSemanticsIsAuthorizedAnew()
    {
        using PublisherFixture fixture = new();
        string first = PublisherFlowTests.StageDefault(fixture);

        // A pre-seal correction to staging prose: same delta, new semantic basis on top.
        PayloadBuilder replacement = new(fixture, first)
        {
            Mode = "ADVANCE_EXISTING_DELTA",
            Kind = "existing",
            NextText = PayloadBuilder.NextState("`DELTA-20260102-001`: PUBLISHED. Its receipt is in CANONICAL-DELTAS.md."),
        };
        string second = PublisherFlowTests.StageDefault(fixture, replacement);

        Assert.Equal(first, fixture.Parent(second));
        AssertInvalid(fixture, fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, first).Build()), second);

        Receipt receipt = fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, second).Build());

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        Assert.Equal(replacement.NextText, fixture.Text(fixture.RemoteHead(), PublisherFixture.CurrentState));
    }

    [Fact]
    public void AReplacementBasisThatChangesTheSemanticClaimNeedsANewDelta()
    {
        using PublisherFixture fixture = new();
        string first = PublisherFlowTests.StageDefault(fixture);
        PayloadBuilder replacement = new(fixture, first)
        {
            Mode = "ADVANCE_EXISTING_DELTA",
            Kind = "existing",
            EntryText = PayloadBuilder.Entry(scope: "a wider scope than was adjudicated"),
        };

        Receipt receipt = fixture.Publisher().Stage(replacement.Build());

        Assert.True(receipt.FailureClass == FailureClasses.SemanticChangeRequiresNewDelta, receipt.Render());
        Assert.Equal(first, fixture.RemoteHead());
    }

    [Fact]
    public void AReplacementBasisCannotRewriteTheAuthorizationHistory()
    {
        using PublisherFixture fixture = new();
        (string history, string invalidated) = InvalidatedFirstAuthorization(fixture);
        PayloadBuilder replacement = new(fixture, fixture.RemoteHead())
        {
            Mode = "ADVANCE_EXISTING_DELTA",
            Kind = "existing",
            EntryText = PayloadBuilder.Entry(seal: history.Replace("Authority: CONTROL_ROOM", "Authority: OWNER", StringComparison.Ordinal)),
        };

        Receipt receipt = fixture.Publisher().Stage(replacement.Build());

        Assert.True(receipt.FailureClass == FailureClasses.SealAuthorizationInvalid, receipt.Render());
        Assert.NotEqual(invalidated, fixture.OperatorHead());
    }

    [Fact]
    public void AuthorizationsAreAppendOnlyAcrossReplacementBases()
    {
        using PublisherFixture fixture = new();
        (string history, _) = InvalidatedFirstAuthorization(fixture);
        PayloadBuilder replacement = new(fixture, fixture.RemoteHead())
        {
            Mode = "ADVANCE_EXISTING_DELTA",
            Kind = "existing",
            EntryText = PayloadBuilder.Entry(seal: history),
        };
        string second = PublisherFlowTests.StageDefault(fixture, replacement);

        Receipt receipt = fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, second) { Authorized = "2026-01-02T02:00:00Z" }.Build());

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        string deltas = fixture.Text(fixture.RemoteHead(), PublisherFixture.Deltas);
        Assert.Contains(history, deltas, StringComparison.Ordinal);
        Assert.Contains("- Record: SA-2\n  Scope: AUTHORIZE_SEAL_ONLY\n  Authority: CONTROL_ROOM\n  Delta: DELTA-20260102-001\n  Semantic basis: " + second, deltas, StringComparison.Ordinal);
        Assert.StartsWith("SA-2;", receipt.BindingSealAuthorization, StringComparison.Ordinal);
        Assert.Single(receipt.EarlierAuthorizationRecords);
    }

    /// <summary>
    /// SA-1 is recorded and pushed, then the remote moves before the seal: SA-1 stays in
    /// the ledger and no longer binds. Returns SA-1's block text and the unpushed seal.
    /// </summary>
    internal static (string History, string UnpushedSeal) InvalidatedFirstAuthorization(PublisherFixture fixture)
    {
        string basis = PublisherFlowTests.StageDefault(fixture);
        InterferingGit moved = new InterferingGit(fixture.PublisherGit()).Before("push", 2, () => fixture.ForeignPush());

        Receipt receipt = fixture.Publisher(moved).Authorize(new AuthorizationBuilder(fixture, basis).Build());

        Assert.True(receipt.FailureClass == FailureClasses.RemoteMovedBeforeSeal, receipt.Render());
        string unpushed = fixture.OperatorHead();
        fixture.ResetOperatorToRemote();
        string deltas = fixture.Text(fixture.RemoteHead(), PublisherFixture.Deltas);
        int start = deltas.IndexOf("Seal authorizations:\n- Record: SA-1", StringComparison.Ordinal);
        int end = deltas.IndexOf("\n\nSupersedes:", start, StringComparison.Ordinal);
        return (deltas[start..end], unpushed);
    }
}
