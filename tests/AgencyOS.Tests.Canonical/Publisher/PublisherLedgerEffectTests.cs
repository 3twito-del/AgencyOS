using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>Decision receipts, status changes and provenance prose, exactly as the whitelist allows.</summary>
public sealed class PublisherLedgerEffectTests
{
    private static Receipt Publish(PublisherFixture fixture, PayloadBuilder payload, string authority = "CONTROL_ROOM")
    {
        string basis = PublisherFlowTests.StageDefault(fixture, payload);
        return fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, basis) { Authority = authority }.Build());
    }

    [Fact]
    public void ANewDecisionsReceiptNamesTheSemanticBasisThatFirstHeldIt()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture)
        {
            NewDecisions = [("DECISION-20260102-001", PayloadBuilder.Decision("DECISION-20260102-001"))],
            Receipts = [("DECISION-20260102-001", "SEMANTIC_BASIS")],
        };

        Receipt receipt = Publish(fixture, payload);

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        string basis = receipt.SemanticBasisSha;
        Assert.Contains($"Publication receipt: {basis} on origin/main; remote readback verified {receipt.BasisReadbackUtc}",
            fixture.Text(fixture.RemoteHead(), PublisherFixture.Decisions), StringComparison.Ordinal);
        Assert.Contains($"DECISION-20260102-001: {basis}", receipt.DecisionReceiptsAffected);
    }

    [Fact]
    public void AnEarlierDecisionsReceiptNamesItsOwnContentBearingCommit()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture) { Receipts = [("DECISION-20260101-002", fixture.PendingDecisionCommit)] };

        Receipt receipt = Publish(fixture, payload);

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        string decisions = fixture.Text(fixture.RemoteHead(), PublisherFixture.Decisions);
        Assert.Contains($"Publication receipt: {fixture.PendingDecisionCommit} on origin/main; remote readback verified {receipt.BasisReadbackUtc}", decisions, StringComparison.Ordinal);
    }

    [Fact]
    public void AContentBearingCommitThatIsNotTheFirstToHoldTheDecisionIsRefused()
    {
        using PublisherFixture fixture = new();
        string later = fixture.ForeignPush();
        fixture.ResetOperatorToRemote();
        PayloadBuilder payload = new(fixture) { Receipts = [("DECISION-20260101-002", later)] };

        Receipt receipt = fixture.Publisher().Stage(payload.Build());

        Assert.True(receipt.FailureClass == FailureClasses.PreconditionDrift, receipt.Render());
    }

    [Fact]
    public void ASupersedingOwnerDecisionMovesTheEarlierStatusAtTheSeal()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture)
        {
            Classes = ["CONTROL_ROOM", "OWNER"],
            NewDecisions = [("DECISION-20260102-001", PayloadBuilder.Decision("DECISION-20260102-001", "OWNER", "DECISION-20260101-001"))],
            StatusChanges = [("DECISION-20260101-001", "SUPERSEDED by DECISION-20260102-001")],
            Receipts = [("DECISION-20260102-001", "SEMANTIC_BASIS")],
        };

        Receipt receipt = Publish(fixture, payload, authority: "OWNER");

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        Assert.Contains("Status: SUPERSEDED by DECISION-20260102-001", fixture.Text(fixture.RemoteHead(), PublisherFixture.Decisions), StringComparison.Ordinal);
        Assert.Contains("Status: ACTIVE", fixture.Text(receipt.SemanticBasisSha, PublisherFixture.Decisions), StringComparison.Ordinal);
    }

    [Fact]
    public void AControlRoomDecisionCannotSupersedeAnOwnerDecision()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture)
        {
            NewDecisions = [("DECISION-20260102-001", PayloadBuilder.Decision("DECISION-20260102-001", "CONTROL_ROOM", "DECISION-20260101-001"))],
            StatusChanges = [("DECISION-20260101-001", "SUPERSEDED by DECISION-20260102-001")],
        };

        Receipt receipt = fixture.Publisher().Stage(payload.Build());

        Assert.True(receipt.FailureClass == FailureClasses.AuthorityMissing, receipt.Render());
    }

    [Fact]
    public void ProvenanceProseIsReplacedExactlyAtTheSeal()
    {
        using PublisherFixture fixture = new();
        const string Before = "**Migration status.** Decisions are migrated under the protocol.";
        const string After = "**Migration status.** Decisions are migrated under the protocol; the fixture delta is published.";
        PayloadBuilder payload = new(fixture) { Provenance = [(PublisherFixture.Decisions, Before, After)] };

        Receipt receipt = Publish(fixture, payload);

        Assert.True(receipt.Result == Results.PublishedVerified, receipt.Render());
        string decisions = fixture.Text(fixture.RemoteHead(), PublisherFixture.Decisions);
        Assert.Contains(After, decisions, StringComparison.Ordinal);
        Assert.Equal(PublisherFixture.BaseDecisions.Replace(Before, After, StringComparison.Ordinal) + "\n" + PublisherFixture.PendingDecision, decisions);
    }
}
