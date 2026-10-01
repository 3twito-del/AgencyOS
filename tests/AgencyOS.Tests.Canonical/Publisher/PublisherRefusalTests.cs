using System.Text;
using AgencyOS.Canonical.Publisher.Publishing;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>
/// Pre-flight and semantic-basis refusals (P0 and P2). Each stops with the contract's
/// failure class, and none reaches the remote.
/// </summary>
public sealed class PublisherRefusalTests
{
    private static Receipt Stage(PublisherFixture fixture, PayloadBuilder payload) => fixture.Publisher().Stage(payload.Build());

    private static void AssertStopped(PublisherFixture fixture, Receipt receipt, string failureClass, string result)
    {
        Assert.True(receipt.FailureClass == failureClass, receipt.Render());
        Assert.Equal(result, receipt.Result);
        Assert.Equal(fixture.Start, fixture.RemoteHead());
    }

    [Fact]
    public void StageAlwaysStopsAtTheHoldAndCannotSeal()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture);
        string basis = PublisherFlowTests.StageDefault(fixture, payload);
        Assert.Equal(Results.BasisVerifiedAwaitingSeal, fixture.Publisher().Stage(payload.Build()).Result);

        Receipt again = fixture.Publisher().Resume(PublisherFixture.Branch, PayloadBuilder.DeltaId);

        Assert.Equal(Results.BasisVerifiedAwaitingSeal, again.Result);
        Assert.Equal(basis, fixture.RemoteHead());
        Assert.Equal(PublisherFixture.BaseCurrentState, fixture.Text(basis, PublisherFixture.CurrentState));
        Assert.DoesNotContain("Seal authorizations:\n- Record", fixture.Text(basis, PublisherFixture.Deltas), StringComparison.Ordinal);
        Assert.Contains("Status: ACCEPTED\n\nDetected: 2026-01-02", fixture.Text(basis, PublisherFixture.Deltas), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BASIS")]
    [InlineData("SEAL")]
    public void AV11PayloadCarryingStopAfterIsRefusedNotIgnored(string value)
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture);
        builder.Extra["stop_after"] = value;

        AssertStopped(fixture, Stage(fixture, builder), FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Theory]
    [InlineData("CONTROL_ROOM", "docs/adr/ADR-0001.md", FailureClasses.ScopeViolation)]
    [InlineData("OWNER", "docs/adr/ADR-0001.md", FailureClasses.ScopeViolation)]
    [InlineData("OWNER", "docs/releases/ALPHA-0.1.0-build-98.md", FailureClasses.ScopeViolation)]
    [InlineData("OWNER", "scripts/Invoke-AgencyOS.ps1", FailureClasses.ScopeViolation)]
    [InlineData("OWNER", "docs/control-room/CANONICAL-STATE-PROTOCOL.md", FailureClasses.ScopeViolation)]
    [InlineData("OWNER", "src/Product.cs", FailureClasses.ProductBoundaryViolation)]
    public void NoAuthorityOverridesTheHardForbiddenSet(string authority, string path, string failureClass)
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture);

        if (authority == "OWNER")
        {
            builder.Classes = ["CONTROL_ROOM", "OWNER"];
            builder.OwnerApplies = true;
            builder.Governing = ["DECISION-20260101-001"];
        }

        builder.Allowed.Add(path);
        builder.Forbidden.RemoveAll(x => path.StartsWith(x, StringComparison.Ordinal));

        Receipt receipt = Stage(fixture, builder);

        Assert.True(receipt.FailureClass == failureClass, receipt.Render());
        Assert.Equal(fixture.Start, fixture.RemoteHead());
    }

    [Fact]
    public void AMissingAuthorityReferenceIsAuthorityMissing()
    {
        using PublisherFixture fixture = new();
        Receipt receipt = Stage(fixture, new PayloadBuilder(fixture) { References = [] });

        AssertStopped(fixture, receipt, FailureClasses.AuthorityMissing, Results.StoppedAuthority);
        Assert.Equal(string.Empty, fixture.Status());
    }

    [Fact]
    public void AnUnresolvableAuthorityReferenceIsAuthorityMissing()
    {
        using PublisherFixture fixture = new();
        Receipt receipt = Stage(fixture, new PayloadBuilder(fixture) { References = ["the chat on Tuesday"] });

        AssertStopped(fixture, receipt, FailureClasses.AuthorityMissing, Results.StoppedAuthority);
    }

    [Theory]
    [InlineData("pretty")]
    [InlineData("v1")]
    [InlineData("number")]
    [InlineData("unknown-key")]
    public void ANonCanonicalOrWrongVersionPayloadIsPreconditionDrift(string defect)
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture);

        if (defect == "v1")
        {
            builder.Contract = "agencyos-canonical-publisher/v1";
        }

        string json = Encoding.UTF8.GetString(builder.Build());
        byte[] bytes = defect switch
        {
            "pretty" => Encoding.UTF8.GetBytes(json.Replace(",\"", ", \"", StringComparison.Ordinal)),
            "number" => Encoding.UTF8.GetBytes(json.Replace("\"api_change\":false", "\"api_change\":0", StringComparison.Ordinal)),
            "unknown-key" => Encoding.UTF8.GetBytes("{\"a_extra\":\"x\"," + json[1..]),
            _ => Encoding.UTF8.GetBytes(json),
        };

        Receipt receipt = fixture.Publisher().Stage(bytes);

        AssertStopped(fixture, receipt, FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Fact]
    public void ADetectorPacketIsNotAPayload()
    {
        using PublisherFixture fixture = new();
        byte[] packet = Encoding.UTF8.GetBytes("{\"contract\":\"agencyos-canonical-detector/v1\",\"observation\":{\"lifecycle_state\":\"OBSERVED\"},\"result\":\"REVIEW_REQUIRED\"}");

        Receipt receipt = fixture.Publisher().Stage(packet);

        AssertStopped(fixture, receipt, FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Fact]
    public void APriorCurrentStateDigestMismatchIsPreconditionDrift()
    {
        using PublisherFixture fixture = new();
        Receipt receipt = Stage(fixture, new PayloadBuilder(fixture) { Prior = new string('0', 64) });

        AssertStopped(fixture, receipt, FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Theory]
    [InlineData("allowed-forbidden")]
    [InlineData("mandatory-omitted")]
    [InlineData("written-not-allowed")]
    public void AForbiddenOrUnallowedPathIsAScopeViolation(string defect)
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture);

        switch (defect)
        {
            case "allowed-forbidden":
                builder.Allowed.Add("docs/adr/ADR-0001.md");
                break;
            case "mandatory-omitted":
                builder.Forbidden.Remove("docs/releases/");
                break;
            default:
                builder.Allowed.Remove(PublisherFixture.CurrentState);
                break;
        }

        AssertStopped(fixture, Stage(fixture, builder), FailureClasses.ScopeViolation, Results.StoppedScope);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AProductFlagOrPathIsAProductBoundaryViolation(bool flag)
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture) { ProductChange = flag };

        if (!flag)
        {
            builder.Allowed.Add("src/Product.cs");
        }

        AssertStopped(fixture, Stage(fixture, builder), FailureClasses.ProductBoundaryViolation, Results.StoppedScope);
    }

    [Fact]
    public void ASecondPendingDeltaIsRefused()
    {
        using PublisherFixture fixture = new();
        string basis = PublisherFlowTests.StageDefault(fixture);
        PayloadBuilder second = new(fixture, basis)
        {
            Delta = "DELTA-20260102-002",
            References = ["Adjudication of DELTA-20260102-002"],
            EntryText = PayloadBuilder.Entry(id: "DELTA-20260102-002"),
            NextText = PayloadBuilder.NextState("`DELTA-20260102-002`: PUBLISHED."),
            Changes = [("Latest published delta", "`DELTA-20260102-002`: PUBLISHED.")],
        };

        Receipt receipt = fixture.Publisher().Stage(second.Build());

        Assert.True(receipt.FailureClass == FailureClasses.PreconditionDrift, receipt.Render());
        Assert.Contains("Another transition is pending", string.Join(' ', receipt.UnresolvedWarnings), StringComparison.Ordinal);
        Assert.Equal(basis, fixture.RemoteHead());
    }

    [Fact]
    public void ADeltaDigestMismatchStopsLocallyAndKeepsTheEvidence()
    {
        using PublisherFixture fixture = new();
        Receipt receipt = Stage(fixture, new PayloadBuilder(fixture) { Substantive = new string('a', 64) });

        AssertStopped(fixture, receipt, FailureClasses.SemanticChangeRequiresNewDelta, Results.StoppedPrecondition);
        Assert.NotEqual(string.Empty, fixture.Status());
        Assert.Equal(fixture.Start, fixture.OperatorHead());
    }

    [Theory]
    [InlineData(PublisherFixture.Deltas, "Scope: fixture\n\nEvidence: fixture\n\nConflicts: None\n\nAuthority required", "Scope: fixture, rewritten\n\nEvidence: fixture\n\nConflicts: None\n\nAuthority required")]
    [InlineData(PublisherFixture.Decisions, "Decision: The fixture decision.", "Decision: The fixture decision, rewritten.")]
    public void AnEarlierEntryMutatedInALocalBasisIsASemanticChange(string ledger, string before, string after)
    {
        using PublisherFixture fixture = new();
        PayloadBuilder payload = new(fixture);

        // A crashed or careless run left a local semantic basis that also rewrote history.
        InterferingGit crash = new InterferingGit(fixture.PublisherGit()).Before("push", 1, () => throw new SimulatedCrash());
        Assert.Throws<SimulatedCrash>(() => fixture.Publisher(crash).Stage(payload.Build()));
        string honest = fixture.OperatorHead();
        fixture.Git(fixture.Operator, "reset", "-q", "--soft", fixture.Start);
        string text = fixture.Git(fixture.Operator, "show", honest + ":" + ledger);
        fixture.CommitInOperator("a basis that also rewrites history", (ledger, text.Replace(before, after, StringComparison.Ordinal)));

        Receipt receipt = fixture.Publisher().Stage(payload.Build());

        AssertStopped(fixture, receipt, FailureClasses.SemanticChangeRequiresNewDelta, Results.StoppedPrecondition);
        string evidence = string.Join(' ', receipt.UnresolvedWarnings);
        Assert.Contains(ledger == PublisherFixture.Deltas ? "DELTA-20260101-001" : "DECISION-20260101-001", evidence, StringComparison.Ordinal);
        Assert.Contains("rewritten", evidence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-field")]
    [InlineData("not-active")]
    [InlineData("wrong-date")]
    public void AMalformedNewDecisionIsPreconditionDrift(string defect)
    {
        using PublisherFixture fixture = new();
        string decision = PayloadBuilder.Decision("DECISION-20260102-001");
        decision = defect switch
        {
            "missing-field" => decision.Replace("Consequences: fixture\n\n", string.Empty, StringComparison.Ordinal),
            "not-active" => decision.Replace("Status: ACTIVE", "Status: SUPERSEDED by DECISION-20260101-001", StringComparison.Ordinal),
            _ => decision.Replace("Date: 2026-01-02", "Date: 2026-01-03", StringComparison.Ordinal),
        };

        Receipt receipt = Stage(fixture, new PayloadBuilder(fixture) { NewDecisions = [("DECISION-20260102-001", decision)] });

        AssertStopped(fixture, receipt, FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Fact]
    public void AMissingCitedDecisionIsAuthorityMissing()
    {
        using PublisherFixture fixture = new();
        Receipt receipt = Stage(fixture, new PayloadBuilder(fixture) { Governing = ["DECISION-20260101-999"] });

        AssertStopped(fixture, receipt, FailureClasses.AuthorityMissing, Results.StoppedAuthority);
    }

    [Theory]
    [InlineData("expansion-without-owner")]
    [InlineData("owner-without-owner-decision")]
    public void AnOwnerRequiredExpansionWithoutOwnerAuthorityIsRefused(string defect)
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture) { DomainExpansion = true };

        if (defect == "owner-without-owner-decision")
        {
            builder.OwnerApplies = true;
            builder.Classes = ["CONTROL_ROOM", "OWNER"];
            builder.Governing = ["DECISION-20260101-002"];
        }

        AssertStopped(fixture, Stage(fixture, builder), FailureClasses.AuthorityMissing, Results.StoppedAuthority);
    }

    [Fact]
    public void AnOwnerExpansionWithAnOwnerDecisionIsMechanicallyAdmissible()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture)
        {
            DomainExpansion = true,
            OwnerApplies = true,
            Classes = ["CONTROL_ROOM", "OWNER"],
            Governing = ["DECISION-20260101-001"],
        };

        Assert.Equal(Results.BasisVerifiedAwaitingSeal, Stage(fixture, builder).Result);
    }

    [Fact]
    public void ProvenanceProseWhoseBeforeTextIsAbsentIsRefusedBeforeAnyPush()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture) { Provenance = [(PublisherFixture.Decisions, "text that is not there", "after")] };

        AssertStopped(fixture, Stage(fixture, builder), FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Fact]
    public void ProvenanceProseInsideAnEntryIsRefused()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture) { Provenance = [(PublisherFixture.Decisions, "The fixture decision.", "Changed.")] };

        AssertStopped(fixture, Stage(fixture, builder), FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Fact]
    public void SubstantiveTextHiddenInAnExcludedLifecycleBlockIsRefused()
    {
        using PublisherFixture fixture = new();
        string smuggled = PayloadBuilder.Entry().Replace("Status: ACCEPTED\n", "Status: ACCEPTED\n  NG-4 is authorized.\n", StringComparison.Ordinal);

        Receipt receipt = Stage(fixture, new PayloadBuilder(fixture) { EntryText = smuggled });

        AssertStopped(fixture, receipt, FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Fact]
    public void ADeltaEntryThatClaimsPublishedBeforeTheSealIsRefused()
    {
        using PublisherFixture fixture = new();
        string early = PayloadBuilder.Entry().Replace("Status: ACCEPTED", "Status: PUBLISHED", StringComparison.Ordinal);

        AssertStopped(fixture, Stage(fixture, new PayloadBuilder(fixture) { EntryText = early }), FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Fact]
    public void StagedTextThatIntroducesAForbiddenImplicationIsRefused()
    {
        using PublisherFixture fixture = new();
        PayloadBuilder builder = new(fixture) { NextText = PayloadBuilder.NextState() + "\nWe begin NG-4 now.\n" };

        AssertStopped(fixture, Stage(fixture, builder), FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
    }

    [Fact]
    public void ADirtyWorkingTreeIsNeverDiscarded()
    {
        using PublisherFixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.Operator, "docs", "draft.md"), "Uncommitted.\n");

        Receipt receipt = Stage(fixture, new PayloadBuilder(fixture));

        AssertStopped(fixture, receipt, FailureClasses.PreconditionDrift, Results.StoppedPrecondition);
        Assert.True(File.Exists(Path.Combine(fixture.Operator, "docs", "draft.md")));
    }
}
