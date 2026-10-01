using System.Text;
using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>
/// A descriptive correction resolves its authority references exactly as semantic staging
/// does (contract sections 5.2 and 7.3). H3D-001: before this, <c>correct</c> checked only
/// that a reference was present, so any non-empty string passed.
/// </summary>
public sealed class PublisherCorrectionAuthorityTests
{
    private static Receipt Correct(PublisherFixture fixture, params string[] references)
    {
        PayloadBuilder payload = PublisherCorrectionTests.Correction(fixture);
        payload.References = [.. references];
        return fixture.Publisher().Correct(payload.Build());
    }

    private static void AssertRefused(PublisherFixture fixture, Receipt receipt)
    {
        Assert.True(receipt.FailureClass == FailureClasses.AuthorityMissing, receipt.Render());
        Assert.Equal(Results.StoppedAuthority, receipt.Result);
        Assert.Equal(fixture.Start, fixture.RemoteHead());
        Assert.Equal(fixture.Start, fixture.OperatorHead());
        Assert.Equal(string.Empty, fixture.Status());
    }

    private static void AssertAccepted(PublisherFixture fixture, Receipt receipt)
    {
        Assert.True(receipt.Result == Results.CorrectionVerified, receipt.Render());
        Assert.Equal(fixture.Start, fixture.Parent(fixture.RemoteHead()));
        Assert.Contains(PublisherCorrectionTests.After, fixture.Text(fixture.RemoteHead(), PublisherFixture.CurrentState), StringComparison.Ordinal);
    }

    [Fact]
    public void ControlRoomAuthorityWithNoReferenceIsRefused()
    {
        using PublisherFixture fixture = new();

        AssertRefused(fixture, Correct(fixture));
    }

    [Fact]
    public void AnArbitraryNonEmptyReferenceIsRefused()
    {
        using PublisherFixture fixture = new();

        AssertRefused(fixture, Correct(fixture, "The Control Room said so in chat."));
    }

    [Fact]
    public void APinnedPathThatDoesNotResolveIsRefused()
    {
        using PublisherFixture fixture = new();

        AssertRefused(fixture, Correct(fixture, "docs/control-room/CANONICAL-DELTAS.md@" + new string('a', 40)));
    }

    [Fact]
    public void APinnedPathMissingAtAnExistingCommitIsRefused()
    {
        using PublisherFixture fixture = new();

        AssertRefused(fixture, Correct(fixture, "docs/control-room/NO-SUCH-FILE.md@" + fixture.Start));
    }

    [Fact]
    public void ANonexistentDecisionIdIsRefused()
    {
        using PublisherFixture fixture = new();

        AssertRefused(fixture, Correct(fixture, "DECISION-20260101-099"));
    }

    [Fact]
    public void AnAdjudicationOfAMissingDeltaIsRefused()
    {
        using PublisherFixture fixture = new();

        AssertRefused(fixture, Correct(fixture, "Adjudication of DELTA-20260101-099"));
    }

    [Fact]
    public void OneUnresolvedReferenceAmongValidOnesIsRefused()
    {
        using PublisherFixture fixture = new();

        AssertRefused(fixture, Correct(fixture, "DECISION-20260101-001", "DECISION-20260101-099"));
    }

    [Fact]
    public void AnAdjudicationOfADeltaWhoseAdjudicationIsPendingIsRefused()
    {
        using PublisherFixture fixture = new();
        string entry = PayloadBuilder.Entry(id: "DELTA-20260101-002")
            .Replace("Status: ACCEPTED", "Status: PENDING_ADJUDICATION", StringComparison.Ordinal)
            .Replace("Adjudication: The Control Room accepted the fixture transition.", "Adjudication: Pending", StringComparison.Ordinal);
        PushLedger(fixture, PublisherFixture.BaseDeltas + "\n" + entry);

        Receipt receipt = fixture.Publisher().Correct(Payload(fixture, "Adjudication of DELTA-20260101-002"));

        Assert.True(receipt.FailureClass == FailureClasses.AuthorityMissing, receipt.Render());
        Assert.Contains(receipt.UnresolvedWarnings, x => x.Contains("'Adjudication of DELTA-20260101-002'", StringComparison.Ordinal));
        Assert.Equal(fixture.OperatorHead(), fixture.RemoteHead());
        Assert.Equal(string.Empty, fixture.Status());
    }

    [Fact]
    public void AnExistingDecisionIdIsAccepted()
    {
        using PublisherFixture fixture = new();

        AssertAccepted(fixture, Correct(fixture, "DECISION-20260101-001"));
    }

    [Fact]
    public void AResolvingPinnedPathIsAccepted()
    {
        using PublisherFixture fixture = new();

        AssertAccepted(fixture, Correct(fixture, "docs/control-room/CANONICAL-DELTAS.md@" + fixture.Start));
    }

    [Fact]
    public void AnAdjudicationOfADeltaWithAPopulatedAdjudicationIsAccepted()
    {
        using PublisherFixture fixture = new();

        AssertAccepted(fixture, Correct(fixture, "Adjudication of DELTA-20260101-001"));
    }

    private static byte[] Payload(PublisherFixture fixture, string reference)
    {
        PayloadBuilder payload = PublisherCorrectionTests.Correction(fixture);
        payload.References = [reference];
        return payload.Build();
    }

    /// <summary>Another actor publishes a ledger; the operator then follows the remote.</summary>
    private static void PushLedger(PublisherFixture fixture, string ledger)
    {
        fixture.Git(fixture.Other, "pull", "-q", "--ff-only", "origin", PublisherFixture.Branch);
        File.WriteAllBytes(Path.Combine(fixture.Other, PublisherFixture.Deltas), new UTF8Encoding(false).GetBytes(ledger));
        fixture.Git(fixture.Other, "add", "-A");
        fixture.Git(fixture.Other, "commit", "-q", "-m", "add a delta awaiting adjudication");
        fixture.Git(fixture.Other, "push", "-q", "origin", PublisherFixture.Branch);
        fixture.ResetOperatorToRemote();
    }
}
