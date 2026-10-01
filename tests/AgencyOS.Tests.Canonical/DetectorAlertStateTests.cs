using System.Text.Json;
using Xunit;

namespace AgencyOS.Tests.Canonical;

/// <summary>
/// What the Canonical Alert calls current, and how it asks about staging (H3D-003 and the
/// staging question, both found by the first real canonical dogfood).
/// </summary>
/// <remarks>
/// A range can itself publish a new CURRENT-STATE.md. The alert's "Current canonical
/// state:" must then quote the state at the end of the range: the baseline's copy is
/// superseded by the range, not current. The JSON packet keeps both snapshots.
/// </remarks>
public sealed class DetectorAlertStateTests
{
    private const string CurrentState = DetectorScenarioTests.CurrentState;
    private const string DeltaLedger = DetectorScenarioTests.DeltaLedger;
    private const string OldLatest = "`DELTA-20260101-001`: PUBLISHED.";
    private const string NewLatest = "`DELTA-20260102-001`: PUBLISHED.";
    private const string StagedNext = "docs/control-room/pending/DELTA-20260102-001/CURRENT-STATE.next.md";
    private const string StagedPayload = "docs/control-room/pending/DELTA-20260102-001/PUBLICATION-PAYLOAD.json";

    private static string State(string latest) =>
        $"# AgencyOS current canonical state\n\n**Status:** CURRENT\n\n## Latest published delta\n\n{latest}\n";

    private static string Ledger(string second) =>
        "# AgencyOS canonical deltas\n\n## Template\n\n## DELTA-YYYYMMDD-NNN\n\nStatus:\n\n---\n\n# Entries\n\n" +
        $"## DELTA-20260101-001\n\nStatus: PUBLISHED\n\nScope: fixture\n\n## DELTA-20260102-001\n\nStatus: {second}\n\nScope: fixture\n";

    private static string CurrentLine(string alert) =>
        Assert.Single(alert.Split('\n'), x => x.StartsWith("Current canonical state:", StringComparison.Ordinal));

    private static JsonElement Evidence(JsonElement packet, string side) =>
        packet.GetProperty("repository_evidence").GetProperty(side);

    private static string StagingQuestion(string paths) =>
        $"Do the staging changes at {paths} match the expected lifecycle of an accepted transition?";

    private static IEnumerable<string> Questions(JsonElement packet) =>
        packet.GetProperty("semantic_questions").EnumerateArray().Select(x => x.GetString()!);

    [Fact]
    public void WhenCurrentStateIsUnchangedTheAlertQuotesTheSameState()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write("docs/control-room/DECISIONS.md", "# Decisions\n\nEdited.\n").Commit("decisions");

        object outcome = fixture.Detect(baseline, observed);
        JsonElement packet = GitFixture.Parse(outcome);
        string line = CurrentLine(GitFixture.Alert(outcome));
        string blob = Evidence(packet, "observed").GetProperty("current_state_blob_sha256").GetString()!;

        Assert.Equal(Evidence(packet, "baseline").GetProperty("current_state_blob_sha256").GetString(), blob);
        Assert.Equal(Evidence(packet, "baseline").GetProperty("current_state_latest_delta_line").GetString(),
            Evidence(packet, "observed").GetProperty("current_state_latest_delta_line").GetString());
        Assert.Contains(blob[..16], line, StringComparison.Ordinal);
        Assert.Contains(OldLatest, line, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenCurrentStateChangesTheAlertQuotesTheNewStateAsCurrent()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(CurrentState, State(NewLatest)).Commit("advance current state");

        object outcome = fixture.Detect(baseline, observed);
        JsonElement packet = GitFixture.Parse(outcome);
        string line = CurrentLine(GitFixture.Alert(outcome));

        Assert.Contains($"at {observed[..12]} ", line, StringComparison.Ordinal);
        Assert.DoesNotContain(baseline[..12], line, StringComparison.Ordinal);
        Assert.Contains(Evidence(packet, "observed").GetProperty("current_state_blob_sha256").GetString()![..16], line, StringComparison.Ordinal);
        Assert.DoesNotContain(Evidence(packet, "baseline").GetProperty("current_state_blob_sha256").GetString()![..16], line, StringComparison.Ordinal);
        Assert.Contains(NewLatest, line, StringComparison.Ordinal);
        Assert.DoesNotContain(OldLatest, line, StringComparison.Ordinal);

        // The packet still carries both sides.
        Assert.Equal(OldLatest, Evidence(packet, "baseline").GetProperty("current_state_latest_delta_line").GetString());
        Assert.Equal(NewLatest, Evidence(packet, "observed").GetProperty("current_state_latest_delta_line").GetString());
    }

    [Fact]
    public void APublicationRangeDoesNotLabelTheSupersededStateAsCurrent()
    {
        (GitFixture fixture, string _) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string staged = fixture
            .Write(DeltaLedger, Ledger("ACCEPTED"))
            .Write(StagedNext, State(NewLatest))
            .Write(StagedPayload, "{}\n")
            .Commit("stage DELTA-20260102-001");
        string sealedCommit = fixture
            .Write(DeltaLedger, Ledger("PUBLISHED"))
            .Write(CurrentState, State(NewLatest))
            .Delete(StagedNext)
            .Delete(StagedPayload)
            .Commit("publish DELTA-20260102-001");

        object outcome = fixture.Detect(staged, sealedCommit);
        JsonElement packet = GitFixture.Parse(outcome);
        string alert = GitFixture.Alert(outcome);
        string line = CurrentLine(alert);

        Assert.Contains(NewLatest, line, StringComparison.Ordinal);
        Assert.DoesNotContain(OldLatest, line, StringComparison.Ordinal);
        Assert.Contains($"at {sealedCommit[..12]} ", line, StringComparison.Ordinal);
        Assert.Equal("PUBLISHED", Evidence(packet, "observed").GetProperty("ledger_statuses").GetProperty("items")[1].GetProperty("status").GetString());
        Assert.Equal(0, Evidence(packet, "observed").GetProperty("staging_entries").GetProperty("items").GetArrayLength());
        Assert.Contains(StagingQuestion($"{StagedNext}, {StagedPayload}"), Questions(packet));
        Assert.Contains($"  - {StagingQuestion($"{StagedNext}, {StagedPayload}")}", alert.Split('\n'));
    }

    [Fact]
    public void QuotedRepositoryStateIsNotTheDetectorLifecycle()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(CurrentState, State(NewLatest)).Commit("advance current state");

        string[] lines = GitFixture.Alert(fixture.Detect(baseline, observed)).Split('\n');
        string line = CurrentLine(string.Join('\n', lines));

        Assert.StartsWith("Current canonical state: quoted from the repository at the end of the range: CURRENT-STATE.md at ", line, StringComparison.Ordinal);
        Assert.DoesNotContain("OBSERVED", line, StringComparison.Ordinal);
        Assert.DoesNotContain("lifecycle", line, StringComparison.Ordinal);
        Assert.Contains("status line \"**Status:** CURRENT\"", line, StringComparison.Ordinal);
        Assert.Single(lines, x => x.Contains("lifecycle state: OBSERVED", StringComparison.Ordinal));
        Assert.StartsWith("Delta / observation:", Assert.Single(lines, x => x.Contains("OBSERVED", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void AddedStagingIsAskedAboutWithoutAssumingItsDirection()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(StagedNext, State(NewLatest)).Commit("stage");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed));

        Assert.Contains(StagingQuestion(StagedNext), Questions(packet));
        Assert.DoesNotContain(Questions(packet), x => x.Contains("belong to an accepted transition", StringComparison.Ordinal));
    }

    [Fact]
    public void DeletedStagingIsAskedAboutWithoutAssumingItStillExists()
    {
        (GitFixture fixture, string _) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string staged = fixture.Write(StagedNext, State(NewLatest)).Commit("stage");
        string removed = fixture.Delete(StagedNext).Commit("remove staging");

        JsonElement packet = GitFixture.Parse(fixture.Detect(staged, removed));

        Assert.Equal("deleted", Assert.Single(packet.GetProperty("changed_paths").GetProperty("items").EnumerateArray()).GetProperty("change").GetString());
        Assert.Contains(StagingQuestion(StagedNext), Questions(packet));
        Assert.DoesNotContain(Questions(packet), x => x.Contains("belong to an accepted transition", StringComparison.Ordinal));
    }
}
