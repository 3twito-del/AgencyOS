using System.Text.Json;
using Xunit;

namespace AgencyOS.Tests.Canonical;

/// <summary>
/// The detector's own lifecycle claim is only ever <c>OBSERVED</c>. It may quote what
/// the repository says, but only under <c>repository_evidence</c>, and quoting is kept
/// apart from anything the detector asserts itself.
/// </summary>
public sealed class OutputBoundaryTests
{
    /// <summary>The protocol states the detector must never claim for its own output.</summary>
    private static readonly string[] LaterStates = ["PENDING_ADJUDICATION", "ACCEPTED", "PUBLISHED"];

    /// <summary>
    /// Fields the detector writes in its own words. Everything else is either a fixed
    /// enumeration, a SHA, or text quoted from the repository (commit subjects, paths,
    /// tag names, and everything under <c>repository_evidence</c>).
    /// </summary>
    private static readonly string[] AuthoredRoots =
    [
        "$.result", "$.change_kind", "$.observation", "$.authority.classification",
        "$.authority.recommended_next_authority", "$.recommended_control_room_action",
        "$.evidence_limitations", "$.semantic_questions", "$.machine_verifiable_observations",
        "$.conflicts", "$.failure_class", "$.detail",
    ];

    /// <summary>Outputs across every kind of result, with a ledger that holds all three later states.</summary>
    private static List<object> AllOutcomes(GitFixture fixture, string baseline)
    {
        List<object> outcomes = [fixture.Detect(baseline, baseline)];

        string docs = fixture.Write("docs/00_VISION.md", "Edited.\n").Commit("docs");
        outcomes.Add(fixture.Detect(baseline, docs));

        string ledger = fixture
            .Write(DetectorScenarioTests.DeltaLedger,
                "# Deltas\n\n# Entries\n\n## DELTA-20260101-001\n\nStatus: ACCEPTED\n\n## DELTA-20260101-002\n\nStatus: ACCEPTED\n\n## DELTA-20260101-003\n\nStatus: PENDING_ADJUDICATION\n")
            .Commit("ledger");
        outcomes.Add(fixture.Detect(baseline, ledger));

        string product = fixture.Write("src/AgencyOS.Domain/Deal.cs", "class Deal { }\n").Commit("product");
        outcomes.Add(fixture.Detect(baseline, product));
        outcomes.Add(fixture.Detect(product, baseline));

        return outcomes;
    }

    [Fact]
    public void ARelevantResultIsAnObservationWithNoDeltaId()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(DetectorScenarioTests.CurrentState, "Changed.\n").Commit("edit");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, observed));
        JsonElement observation = packet.GetProperty("observation");

        Assert.Equal("OBSERVED", observation.GetProperty("lifecycle_state").GetString());
        Assert.Equal("NOT_ALLOCATED", observation.GetProperty("delta_id").GetString());
        Assert.Matches("^OBS-[0-9a-f]{12}-[0-9a-f]{12}$", observation.GetProperty("observation_id").GetString());
        Assert.False(observation.TryGetProperty("status", out _));
        Assert.False(packet.TryGetProperty("candidate", out _));
    }

    [Fact]
    public void TheDetectorNeverClaimsALaterLifecycleState()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;

        foreach (object outcome in AllOutcomes(fixture, baseline))
        {
            JsonElement packet = GitFixture.Parse(outcome);

            Assert.Contains(packet.GetProperty("result").GetString(),
                new[] { "NO_RELEVANT_CHANGE", "REVIEW_REQUIRED", "DETECTOR_FAILURE" });

            List<string> violations = [];
            Walk(packet, "$", violations);
            Assert.Empty(violations);
        }
    }

    [Fact]
    public void QuotedRepositoryStatusesStayQuoted()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string ledger = fixture
            .Write(DetectorScenarioTests.DeltaLedger,
                "# Deltas\n\n# Entries\n\n## DELTA-20260101-001\n\nStatus: PUBLISHED\n\n## DELTA-20260101-002\n\nStatus: ACCEPTED\n\n## DELTA-20260101-003\n\nStatus: PENDING_ADJUDICATION\n")
            .Commit("ledger");

        JsonElement packet = GitFixture.Parse(fixture.Detect(baseline, ledger));
        string[] quoted = [.. packet.GetProperty("repository_evidence").GetProperty("observed")
            .GetProperty("ledger_statuses").GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("status").GetString()!)];

        Assert.Equal(["PUBLISHED", "ACCEPTED", "PENDING_ADJUDICATION"], quoted);
        Assert.Equal("OBSERVED", packet.GetProperty("observation").GetProperty("lifecycle_state").GetString());
    }

    [Fact]
    public void TheAlertReportsAnObservationNotACandidate()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        int alerts = 0;

        foreach (object outcome in AllOutcomes(fixture, baseline))
        {
            string[] lines = GitFixture.Alert(outcome).Split('\n');

            if (lines[0] != "AGENCYOS CANONICAL ALERT")
            {
                continue;
            }

            alerts++;
            string first = lines.First(x => x.Length != 0 && x != lines[0]);

            Assert.StartsWith("Delta / observation: OBS-", first, StringComparison.Ordinal);
            Assert.Contains("Delta ID: NOT_ALLOCATED", first, StringComparison.Ordinal);
            Assert.Contains("lifecycle state: OBSERVED", first, StringComparison.Ordinal);
            Assert.DoesNotContain(LaterStates, x => first.Contains(x, StringComparison.Ordinal));
        }

        Assert.True(alerts >= 2);
    }

    /// <summary>
    /// A later lifecycle state may appear only as quoted repository text. In any field the
    /// detector writes in its own words, it may not appear at all, even inside a sentence.
    /// </summary>
    private static void Walk(JsonElement element, string path, List<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Walk(property.Value, path + "." + property.Name, violations);
                }

                break;

            case JsonValueKind.Array:
                int i = 0;

                foreach (JsonElement item in element.EnumerateArray())
                {
                    Walk(item, $"{path}[{i++}]", violations);
                }

                break;

            case JsonValueKind.String:
                string value = element.GetString()!;
                bool quoted = path.StartsWith("$.repository_evidence.", StringComparison.Ordinal);
                bool authored = AuthoredRoots.Any(root => path == root || path.StartsWith(root + ".", StringComparison.Ordinal) || path.StartsWith(root + "[", StringComparison.Ordinal));

                if (!quoted && LaterStates.Contains(value))
                {
                    violations.Add($"{path} = {value}");
                }

                if (authored && LaterStates.Any(x => value.Contains(x, StringComparison.Ordinal)))
                {
                    violations.Add($"{path} contains a later lifecycle state: {value}");
                }

                break;
        }
    }
}
