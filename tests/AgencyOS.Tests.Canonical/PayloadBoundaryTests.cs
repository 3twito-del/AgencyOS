using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgencyOS.Canonical.Detector.Output;
using Xunit;

namespace AgencyOS.Tests.Canonical;

// SOURCE-PROOF: Reads docs/control-room/CANONICAL-PUBLISHER-CONTRACT.md, the published
// normative contract, because its section 3.2 table is the authority for what an
// Accepted Publication Payload must contain. Deriving the keys from that text, rather
// than restating them here, keeps this test bound to the contract actually in force.

/// <summary>
/// Detector output cannot be consumed as an Accepted Publication Payload
/// (CANONICAL-PUBLISHER-CONTRACT.md section 3).
/// </summary>
/// <remarks>
/// Between detector evidence and a payload stands a Control Room adjudication, and
/// nothing in the detector crosses it. This is asserted three ways: the packet names a
/// different contract; it lacks the payload's required fields even if that contract
/// name were forged; and the detector assembly exposes no type or member that makes,
/// takes or names a payload or a publisher.
/// </remarks>
public sealed partial class PayloadBoundaryTests
{
    private const string PublisherContract = "agencyos-canonical-publisher/v1";

    private static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("The repository root could not be located.");
        }
    }

    /// <summary>The payload keys in the contract's section 3.2 table, as dotted paths.</summary>
    private static List<string> PayloadKeys()
    {
        string contract = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "control-room", "CANONICAL-PUBLISHER-CONTRACT.md"));
        int start = contract.IndexOf("### 3.2 Fields", StringComparison.Ordinal);
        int end = contract.IndexOf("## 4.", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The contract's section 3.2 field table was not found.");

        List<string> keys = [];

        foreach (Match row in TableKey().Matches(contract[start..end]))
        {
            foreach (Match key in Backticked().Matches(row.Groups[1].Value))
            {
                keys.Add(key.Groups[1].Value);
            }
        }

        Assert.Contains("mode", keys);
        Assert.Contains("authority.adjudication_references", keys);
        return keys;
    }

    private static string DetectorPacket()
    {
        (GitFixture fixture, string baseline) = DetectorScenarioTests.Baseline();
        using GitFixture scope = fixture;
        string observed = fixture.Write(DetectorScenarioTests.CurrentState, "Changed.\n").Commit("edit");
        return GitFixture.Json(fixture.Detect(baseline, observed));
    }

    /// <summary>
    /// The structural minimum any payload must meet: the publisher's contract name and
    /// every field the contract lists. It is not a publisher; it only refuses.
    /// </summary>
    private static bool IsStructurallyAPayload(JsonNode packet, IEnumerable<string> keys) =>
        packet["contract"]?.GetValue<string>() == PublisherContract
        && keys.All(key => Resolve(packet, key) is not null);

    private static JsonNode? Resolve(JsonNode node, string dotted)
    {
        JsonNode? current = node;

        foreach (string part in dotted.Split('.'))
        {
            current = current is System.Text.Json.Nodes.JsonObject map && map.TryGetPropertyValue(part, out JsonNode? next) ? next : null;
        }

        return current;
    }

    [Fact]
    public void ThePacketNamesTheDetectorContractNotThePublisher()
    {
        JsonNode packet = JsonNode.Parse(DetectorPacket())!;

        Assert.Equal(PacketWriter.ContractId, packet["contract"]!.GetValue<string>());
        Assert.NotEqual(PublisherContract, PacketWriter.ContractId);
        Assert.False(IsStructurallyAPayload(packet, PayloadKeys()));
    }

    [Fact]
    public void EvenAForgedContractNameDoesNotMakeAPayload()
    {
        JsonNode packet = JsonNode.Parse(DetectorPacket())!;
        packet["contract"] = PublisherContract;
        List<string> keys = PayloadKeys();

        List<string> missing = [.. keys.Where(key => key != "contract" && Resolve(packet, key) is null)];

        Assert.False(IsStructurallyAPayload(packet, keys));
        Assert.Contains("mode", missing);
        Assert.Contains("expected_start_sha", missing);
        Assert.Contains("authority.adjudication_references", missing);
        Assert.Contains("current_state.next_sha256", missing);
        Assert.Contains("paths.allowed", missing);
    }

    [Fact]
    public void ThePacketIsAnObservationNotACandidateDelta()
    {
        using JsonDocument packet = JsonDocument.Parse(DetectorPacket());
        JsonElement observation = packet.RootElement.GetProperty("observation");

        Assert.Equal("OBSERVED", observation.GetProperty("lifecycle_state").GetString());
        Assert.Equal("NOT_ALLOCATED", observation.GetProperty("delta_id").GetString());
        Assert.False(packet.RootElement.TryGetProperty("candidate", out _));
        Assert.False(packet.RootElement.TryGetProperty("delta_id", out _));
    }

    [Fact]
    public void TheDetectorAssemblyHasNoPathToAPayloadOrThePublisher()
    {
        Assembly detector = typeof(PacketWriter).Assembly;

        Assert.DoesNotContain(detector.GetTypes(), x =>
            x.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase)
            || x.Name.Contains("Publisher", StringComparison.OrdinalIgnoreCase)
            || x.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(m => m.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase)
                    || m.Name.Contains("Publish", StringComparison.OrdinalIgnoreCase)));

        Assert.Empty(detector.GetExportedTypes());
        Assert.DoesNotContain(detector.GetReferencedAssemblies(), x => x.Name!.StartsWith("AgencyOS", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^\| (`[^|]+`(?:, `[^|]+`)*) \|", RegexOptions.Multiline)]
    private static partial Regex TableKey();

    [GeneratedRegex("`([a-z_.0-9]+)`")]
    private static partial Regex Backticked();
}
