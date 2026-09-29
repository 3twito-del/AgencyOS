using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>
/// Descriptive corrections (contract section 7.3). The Control Room classifies; the
/// publisher only checks mechanically that nothing semantic moved.
/// </summary>
public sealed class PublisherCorrectionTests
{
    private const string Before = "NG-4 is NEXT and not authorized.";
    private const string After = "NG-4 is NEXT; it is not authorized.";

    private static PayloadBuilder Correction(PublisherFixture fixture, string path = PublisherFixture.CurrentState, string before = Before, string after = After)
    {
        PayloadBuilder payload = new(fixture)
        {
            Mode = "DESCRIPTIVE_CORRECTION",
            References = ["docs/control-room/CANONICAL-DELTAS.md@" + fixture.RemoteHead()],
            Corrections = [(path, before, after)],
            Changes = [],
            Unchanged = [],
        };

        string current = fixture.Text(fixture.RemoteHead(), PublisherFixture.CurrentState);
        payload.NextSha = PublisherFixture.Sha256(path == PublisherFixture.CurrentState ? current.Replace(before, after, StringComparison.Ordinal) : current);
        return payload;
    }

    [Fact]
    public void ADescriptiveCorrectionIsCommittedPushedAndReadBack()
    {
        using PublisherFixture fixture = new();

        Receipt receipt = fixture.Publisher().Correct(Correction(fixture).Build());

        Assert.True(receipt.Result == Results.CorrectionVerified, receipt.Render());
        Assert.Contains(After, fixture.Text(fixture.RemoteHead(), PublisherFixture.CurrentState), StringComparison.Ordinal);
        Assert.Equal(fixture.Start, fixture.Parent(fixture.RemoteHead()));
        Assert.False(fixture.ExistsAt(fixture.RemoteHead(), "docs/control-room/pending"));
    }

    [Fact]
    public void ACorrectionThatTouchesALedgerEntryIsNotDescriptive()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = Correction(fixture, PublisherFixture.Deltas, "Candidate/new claim: the fixture baseline", "Candidate/new claim: a different baseline");

        Receipt receipt = fixture.Publisher().Correct(payload.Build());

        Assert.True(receipt.FailureClass == FailureClasses.SemanticChangeRequiresNewDelta, receipt.Render());
        Assert.Equal(fixture.Start, fixture.RemoteHead());
    }

    [Fact]
    public void ACorrectionPayloadIsRefusedByStage()
    {
        using PublisherFixture fixture = new();

        Assert.Equal(FailureClasses.PreconditionDrift, fixture.Publisher().Stage(Correction(fixture).Build()).FailureClass);
    }

    [Fact]
    public void ACorrectionWhileATransitionIsPendingInvalidatesItsBasis()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);

        Receipt correction = fixture.Publisher().Correct(Correction(fixture).Build());

        Assert.True(correction.Result == Results.CorrectionVerified, correction.Render());
        Assert.Contains(correction.UnresolvedWarnings, x => x.Contains("invalidates that basis", StringComparison.Ordinal));
        Assert.Equal(basis, fixture.Parent(fixture.RemoteHead()));

        Receipt stale = fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, basis).Build());

        Assert.True(stale.FailureClass == FailureClasses.RemoteBasisMismatch, stale.Render());
    }
}
