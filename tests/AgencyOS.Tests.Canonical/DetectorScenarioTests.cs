using System.Text.Json;
using AgencyOS.Canonical.Detector.Detection;
using AgencyOS.Canonical.Detector.Git;
using Xunit;

namespace AgencyOS.Tests.Canonical;

/// <summary>
/// Each scenario from the detector's specification, run against a fixture repository.
/// </summary>
public sealed class DetectorScenarioTests
{
    internal const string CurrentState = "docs/control-room/CURRENT-STATE.md";
    internal const string DeltaLedger = "docs/control-room/CANONICAL-DELTAS.md";

    /// <summary>A small repository shaped like AgencyOS, with one published delta.</summary>
    internal static (GitFixture Fixture, string Baseline) Baseline(string deltaStatus = "PUBLISHED")
    {
        GitFixture fixture = new GitFixture()
            .Write(CurrentState,
                "# AgencyOS current canonical state\n\n**Status:** CURRENT\n\n## Latest published delta\n\n`DELTA-20260101-001`: PUBLISHED.\n")
            .Write(DeltaLedger,
                $"# AgencyOS canonical deltas\n\n## Template\n\n## DELTA-YYYYMMDD-NNN\n\nStatus:\n\n---\n\n# Entries\n\n## DELTA-20260101-001\n\nStatus: {deltaStatus}\n\nScope: fixture\n")
            .Write("docs/control-room/DECISIONS.md", "# Decisions\n")
            .Write("docs/00_VISION.md", "Vision.\n")
            .Write("docs/releases/ALPHA-build-1.md", "Release.\n")
            .Write("docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md", "Closure.\n")
            .Write("docs/adr/ADR-0001.md", "ADR.\n")
            .Write("src/AgencyOS.Domain/Deal.cs", "class Deal {}\n")
            .Write("tests/AgencyOS.Tests.Unit/DealTests.cs", "class DealTests {}\n");

        return (fixture, fixture.Commit("baseline"));
    }

    private static JsonElement Flags(JsonElement packet) => packet.GetProperty("flags");

    [Fact]
    public void IdenticalShasAreNoRelevantChange()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, baseline));

        Assert.Equal("NO_RELEVANT_CHANGE", packet.GetProperty("result").GetString());
        Assert.Equal("NO_GIT_CHANGE", packet.GetProperty("change_kind").GetString());
        Assert.Equal("MACHINE_VERIFIABLE_FACT_ONLY", packet.GetProperty("authority").GetProperty("classification").GetString());
        Assert.False(packet.TryGetProperty("observation", out _));
        Assert.False(packet.TryGetProperty("candidate", out _));
        Assert.DoesNotContain("AGENCYOS CANONICAL ALERT", GitFixture.Alert(fixture.Detect(baseline, baseline)), StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryDocsChangeIsAGitChangeWithNoCanonicalRelevance()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write("docs/00_VISION.md", "Vision, revised.\n").Commit("docs: vision");

        object outcome = fixture.Detect(baseline, observed);
        JsonElement packet = GitFixture.Parse(outcome);

        Assert.Equal("NO_RELEVANT_CHANGE", packet.GetProperty("result").GetString());
        Assert.Equal("GIT_CHANGE_NOT_CANONICAL_RELEVANT", packet.GetProperty("change_kind").GetString());
        Assert.Equal("YES", Flags(packet).GetProperty("all_changed_paths_under_docs").GetString());
        Assert.Equal("NO", packet.GetProperty("product_code_changed").GetString());
        Assert.Equal(1, packet.GetProperty("changed_paths").GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("AGENCYOS CANONICAL ALERT", GitFixture.Alert(outcome), StringComparison.Ordinal);
    }

    [Fact]
    public void ControlRoomChangeRequiresControlRoomAdjudication()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(CurrentState, "# AgencyOS current canonical state\n\n**Status:** CURRENT\n\nEdited.\n").Commit("docs: edit current state");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed));
        JsonElement authority = packet.GetProperty("authority");

        Assert.Equal("REVIEW_REQUIRED", packet.GetProperty("result").GetString());
        Assert.Equal("YES", packet.GetProperty("canonical_artifact_changed").GetString());
        Assert.Equal("CONTROL_ROOM_ADJUDICATION_REQUIRED", authority.GetProperty("classification").GetString());
        Assert.Equal("CONTROL_ROOM", authority.GetProperty("recommended_next_authority").GetString());
        Assert.False(authority.GetProperty("owner_authority_may_apply").GetBoolean());
        Assert.Contains(packet.GetProperty("conflicts").EnumerateArray(),
            x => x.GetProperty("kind").GetString() == "CURRENT_STATE_CHANGED_WITHOUT_LEDGER_CHANGE");
        Assert.Equal("OBSERVED", packet.GetProperty("observation").GetProperty("lifecycle_state").GetString());
        Assert.Equal("NOT_ALLOCATED", packet.GetProperty("observation").GetProperty("delta_id").GetString());
        Assert.Matches("^OBS-[0-9a-f]{12}-[0-9a-f]{12}$", packet.GetProperty("observation").GetProperty("observation_id").GetString());
        Assert.False(packet.TryGetProperty("candidate", out _));
    }

    [Fact]
    public void ProductSourceChangeIsReportedAndMayNeedOwnerAuthority()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write("src/AgencyOS.Domain/Deal.cs", "class Deal { int x; }\n").Commit("change deal");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed));
        JsonElement authority = packet.GetProperty("authority");

        Assert.Equal("REVIEW_REQUIRED", packet.GetProperty("result").GetString());
        Assert.Equal("YES", packet.GetProperty("product_code_changed").GetString());
        Assert.Equal("YES", Flags(packet).GetProperty("domain_source_changed").GetString());
        Assert.Equal("NO", Flags(packet).GetProperty("all_changed_paths_under_docs").GetString());
        Assert.True(authority.GetProperty("owner_authority_may_apply").GetBoolean());
        Assert.Contains(authority.GetProperty("owner_triggers").EnumerateArray(),
            x => x.GetProperty("trigger").GetString() == "domain source changed"
                && x.GetProperty("paths")[0].GetString() == "src/AgencyOS.Domain/Deal.cs");
    }

    [Fact]
    public void TestsOnlyChangeIsNotProductCode()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write("tests/AgencyOS.Tests.Unit/DealTests.cs", "class DealTests { }\n").Commit("tests");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed));

        Assert.Equal("REVIEW_REQUIRED", packet.GetProperty("result").GetString());
        Assert.Equal("NO", packet.GetProperty("product_code_changed").GetString());
        Assert.Equal("YES", Flags(packet).GetProperty("tests_changed").GetString());
        Assert.False(packet.GetProperty("authority").GetProperty("owner_authority_may_apply").GetBoolean());
    }

    [Fact]
    public void ReleaseClosureAndAdrChangesAreFlagged()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture
            .Write("docs/releases/ALPHA-build-1.md", "Release, edited.\n")
            .Write("docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md", "Closure, edited.\n")
            .Write("docs/adr/ADR-0001.md", "ADR, edited.\n")
            .Commit("records");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed));
        JsonElement flags = Flags(packet);
        JsonElement triggers = packet.GetProperty("authority").GetProperty("owner_triggers");

        Assert.Equal("REVIEW_REQUIRED", packet.GetProperty("result").GetString());
        Assert.Equal("YES", flags.GetProperty("release_record_changed").GetString());
        Assert.Equal("YES", flags.GetProperty("closure_record_changed").GetString());
        Assert.Equal("YES", flags.GetProperty("adr_changed").GetString());
        Assert.Equal("YES", flags.GetProperty("all_changed_paths_under_docs").GetString());
        Assert.Contains(triggers.EnumerateArray(), x => x.GetProperty("trigger").GetString() == "closure record changed");
        Assert.Contains(triggers.EnumerateArray(), x => x.GetProperty("trigger").GetString() == "release record changed");
    }

    [Fact]
    public void MixedDocsAndProductChangeKeepsBothFacts()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture
            .Write("docs/00_VISION.md", "Vision, revised.\n")
            .Write("src/AgencyOS.Api/Endpoints.cs", "class Endpoints {}\n")
            .Commit("mixed");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed));
        JsonElement[] paths = [.. packet.GetProperty("changed_paths").GetProperty("items").EnumerateArray()];

        Assert.Equal("REVIEW_REQUIRED", packet.GetProperty("result").GetString());
        Assert.Equal("YES", packet.GetProperty("product_code_changed").GetString());
        Assert.Equal("NO", Flags(packet).GetProperty("all_changed_paths_under_docs").GetString());
        Assert.Equal(["docs/00_VISION.md", "src/AgencyOS.Api/Endpoints.cs"], paths.Select(x => x.GetProperty("path").GetString()));
        Assert.False(paths[0].GetProperty("canonical_relevant").GetBoolean());
        Assert.True(paths[1].GetProperty("canonical_relevant").GetBoolean());
        Assert.Equal("added", paths[1].GetProperty("change").GetString());
    }

    [Fact]
    public void NonDescendantObservedCommitIsAnExplicitFailure()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string later = fixture.Write("docs/00_VISION.md", "Later.\n").Commit("later");

        JsonElement packet = GitFixture.Parse(fixture.Detect(later, baseline));

        Assert.Equal("DETECTOR_FAILURE", packet.GetProperty("result").GetString());
        Assert.Equal("OBSERVED_NOT_DESCENDANT", packet.GetProperty("failure_class").GetString());
        Assert.Equal("YES", packet.GetProperty("facts").GetProperty("baseline_descends_from_observed").GetString());
        Assert.False(packet.GetProperty("repository_mutated").GetBoolean());
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000", true, "UNKNOWN_BASELINE_COMMIT")]
    [InlineData("0000000000000000000000000000000000000000", false, "UNKNOWN_OBSERVED_COMMIT")]
    [InlineData("abc123", true, "INVALID_INPUT")]
    [InlineData("abc123", false, "INVALID_INPUT")]
    public void UnknownOrMalformedCommitsAreExplicitFailures(string sha, bool asBaseline, string failureClass)
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;

        JsonElement packet = GitFixture.Parse(asBaseline ? fixture.Detect(sha, baseline) : fixture.Detect(baseline, sha));

        Assert.Equal("DETECTOR_FAILURE", packet.GetProperty("result").GetString());
        Assert.Equal(failureClass, packet.GetProperty("failure_class").GetString());
    }

    [Fact]
    public void LocalHeadNeedsACleanTreeAndResolvesWhenClean()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(CurrentState, "Changed.\n").Commit("edit");

        JsonElement clean = GitFixture.Parse(fixture.Detect(baseline, "local-head"));
        Assert.Equal(observed, clean.GetProperty("range").GetProperty("observed_sha").GetString());
        Assert.Equal("LOCAL_HEAD", clean.GetProperty("range").GetProperty("observed_resolved_from").GetString());

        fixture.Write("docs/uncommitted.md", "Not committed.\n");
        JsonElement dirty = GitFixture.Parse(fixture.Detect(baseline, "local-head"));

        Assert.Equal("DIRTY_TREE_FOR_LOCAL_HEAD", dirty.GetProperty("failure_class").GetString());
    }

    [Fact]
    public void AGitDirectoryIsRequired()
    {
        string directory = Path.Combine(Path.GetTempPath(), "agencyos-detector-tests", "not-a-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            object outcome = new Detector(new GitCli(directory)).Detect(
                new DetectorRequest("main", new string('a', 40), new string('b', 40), "2026-01-01T12:00:00Z"));

            Assert.Equal("UNSUPPORTED_REPOSITORY_STATE", GitFixture.Parse(outcome).GetProperty("failure_class").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnavailableEvidenceStaysUnknown()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(CurrentState, "Changed.\n").Commit("edit");
        FailingGit reader = new(new GitCli(fixture.Root), "for-each-ref", "cat-file");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed, reader));
        JsonElement tags = packet.GetProperty("tags_at_range_commits");
        JsonElement before = packet.GetProperty("repository_evidence").GetProperty("baseline");

        Assert.Equal("UNKNOWN", tags.GetProperty("state").GetString());
        Assert.False(tags.TryGetProperty("items", out _));
        Assert.Equal("UNKNOWN", before.GetProperty("current_state_blob_sha256").GetString());
        Assert.Equal("UNKNOWN", before.GetProperty("ledger_statuses").GetProperty("state").GetString());
        Assert.Equal("UNKNOWN", packet.GetProperty("ci_state").GetString());
        Assert.Equal("UNKNOWN", packet.GetProperty("tag_movement").GetString());
        Assert.Contains(packet.GetProperty("evidence_limitations").EnumerateArray(),
            x => x.GetString()!.Contains("tags at commits in the range are UNKNOWN", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnreadableDiffIsAFailureNotAGuess()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(CurrentState, "Changed.\n").Commit("edit");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed, new FailingGit(new GitCli(fixture.Root), "diff")));

        Assert.Equal("DETECTOR_FAILURE", packet.GetProperty("result").GetString());
        Assert.Equal("READ_FAILURE", packet.GetProperty("failure_class").GetString());
    }

    [Fact]
    public void ABackwardLedgerStatusIsAMechanicalConflict()
    {
        (GitFixture fixture, string baseline) = Baseline("PUBLISHED");
        using GitFixture scope = fixture;
        string observed = fixture
            .Write(DeltaLedger, "# AgencyOS canonical deltas\n\n# Entries\n\n## DELTA-20260101-001\n\nStatus: ACCEPTED\n\nScope: fixture\n")
            .Commit("regress");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed));

        Assert.Contains(packet.GetProperty("conflicts").EnumerateArray(),
            x => x.GetProperty("kind").GetString() == "LEDGER_STATUS_NOT_FORWARD");
        Assert.Equal("OBSERVED", packet.GetProperty("observation").GetProperty("lifecycle_state").GetString());
    }

    [Fact]
    public void AlertCarriesEveryProtocolField()
    {
        (GitFixture fixture, string baseline) = Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write("src/AgencyOS.Domain/Deal.cs", "class Deal { int y; }\n").Commit("deal");

        string[] lines = GitFixture.Alert(fixture.Detect(baseline, observed)).Split('\n');

        Assert.Equal("AGENCYOS CANONICAL ALERT", lines[0]);

        foreach (string field in new[]
        {
            "Delta / observation:", "Trigger:", "Current canonical state:", "Candidate change:",
            "Evidence anchors:", "Conflicts:", "Authority required:", "Product code changed:",
            "Recommended Control Room action:",
        })
        {
            Assert.Single(lines, x => x.StartsWith(field, StringComparison.Ordinal));
        }

        Assert.Contains("Product code changed: YES", lines);
        Assert.Contains(lines, x => x.StartsWith("Delta / observation: OBS-", StringComparison.Ordinal)
            && x.Contains("Delta ID: NOT_ALLOCATED", StringComparison.Ordinal)
            && x.Contains("lifecycle state: OBSERVED", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitShasGiveByteIdenticalOutput()
    {
        (GitFixture first, string firstBaseline) = Baseline();
        (GitFixture second, string secondBaseline) = Baseline();
        using GitFixture a = first;
        using GitFixture b = second;
        string firstObserved = first.Write(CurrentState, "Changed.\n").Commit("edit");
        string secondObserved = second.Write(CurrentState, "Changed.\n").Commit("edit");

        Assert.Equal(firstBaseline, secondBaseline);
        Assert.Equal(firstObserved, secondObserved);
        Assert.Equal(GitFixture.Json(first.Detect(firstBaseline, firstObserved)), GitFixture.Json(first.Detect(firstBaseline, firstObserved)));
        Assert.Equal(GitFixture.Json(first.Detect(firstBaseline, firstObserved)), GitFixture.Json(second.Detect(secondBaseline, secondObserved)));
    }

    /// <summary>Makes chosen git subcommands fail, as an unavailable source would.</summary>
    internal sealed class FailingGit(IGitReader inner, params string[] failing) : IGitReader
    {
        public GitResult Run(params string[] arguments) =>
            failing.Contains(arguments[0])
                ? new GitResult(128, [], "unavailable in this test")
                : inner.Run(arguments);
    }
}
