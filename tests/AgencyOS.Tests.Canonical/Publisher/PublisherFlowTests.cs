using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>The full lifecycle against a real bare remote, and the basis-only stop.</summary>
public sealed class PublisherFlowTests
{
    internal const string Pending = "docs/control-room/pending/" + PayloadBuilder.DeltaId;
    internal const string StagedNext = Pending + "/CURRENT-STATE.next.md";
    internal const string StagedPayload = Pending + "/PUBLICATION-PAYLOAD.json";

    /// <summary>Stages the default payload and returns the semantic basis.</summary>
    internal static string StageDefault(PublisherFixture fixture, PayloadBuilder? builder = null)
    {
        Receipt receipt = fixture.Publisher().Stage((builder ?? new PayloadBuilder(fixture)).Build());
        Assert.True(receipt.Result == Results.BasisVerifiedAwaitingSeal, receipt.Render());
        return receipt.SemanticBasisSha;
    }

    [Fact]
    public void AnAcceptedTransitionIsStagedHeldAuthorizedSealedAndReadBack()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture);

        Receipt staged = fixture.Publisher().Stage(payload.Build());

        Assert.True(staged.Result == Results.BasisVerifiedAwaitingSeal, staged.Render());
        string basis = staged.SemanticBasisSha;
        Assert.Equal(basis, fixture.RemoteHead());
        Assert.Equal(fixture.Start, fixture.Parent(basis));
        Assert.Equal(PublisherFixture.BaseCurrentState, fixture.Text(basis, PublisherFixture.CurrentState));
        Assert.Equal(payload.NextText, fixture.Text(basis, StagedNext));
        Assert.Contains("Status: ACCEPTED", fixture.Text(basis, PublisherFixture.Deltas), StringComparison.Ordinal);

        Receipt sealedReceipt = fixture.Publisher().Authorize(new AuthorizationBuilder(fixture, basis).Build());

        Assert.True(sealedReceipt.Result == Results.PublishedVerified, sealedReceipt.Render());
        string seal = fixture.RemoteHead();
        string finalBasis = fixture.Parent(seal);
        Assert.Equal(basis, fixture.Parent(finalBasis));
        Assert.Equal(seal, sealedReceipt.SealingSha);
        Assert.Equal(finalBasis, sealedReceipt.FinalPublicationBasisSha);
        Assert.Equal(payload.NextText, fixture.Text(seal, PublisherFixture.CurrentState));
        Assert.False(fixture.ExistsAt(seal, Pending));

        string deltas = fixture.Text(seal, PublisherFixture.Deltas);
        Assert.Contains("Status: PUBLISHED", deltas[deltas.IndexOf("## " + PayloadBuilder.DeltaId, StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains($"Publication receipt: {finalBasis} on origin/main; remote readback verified {sealedReceipt.BasisReadbackUtc}", deltas, StringComparison.Ordinal);
        Assert.Contains($"Published: {sealedReceipt.BasisReadbackUtc}", deltas, StringComparison.Ordinal);
        Assert.Contains("- Record: SA-1\n  Scope: AUTHORIZE_SEAL_ONLY\n  Authority: CONTROL_ROOM\n  Delta: DELTA-20260102-001\n  Semantic basis: " + basis, deltas, StringComparison.Ordinal);
        Assert.Contains("AGENCYOS PUBLISHER RECEIPT", sealedReceipt.Render(), StringComparison.Ordinal);
        Assert.Equal("NO", sealedReceipt.ProductCodeChanged);
        Assert.Equal(string.Empty, fixture.Status());
    }
}
