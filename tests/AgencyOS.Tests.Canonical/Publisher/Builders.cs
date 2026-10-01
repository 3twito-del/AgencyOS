using AgencyOS.Canonical.Publisher.Json;
using AgencyOS.Canonical.Publisher.Ledger;

namespace AgencyOS.Tests.Canonical.Publisher;

/// <summary>
/// Builds an Accepted Publication Payload the way the Control Room would hand one over:
/// exact accepted text, and the digests computed from it.
/// </summary>
internal sealed class PayloadBuilder
{
    public const string DeltaId = "DELTA-20260102-001";

    public static readonly string[] MandatoryForbidden =
    [
        "src/", "tests/", ".github/", "scripts/", "docs/control-room/CANONICAL-STATE-PROTOCOL.md",
        "docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md", "docs/releases/", "docs/adr/",
    ];

    public PayloadBuilder(PublisherFixture fixture, string? start = null)
    {
        ExpectedStart = start ?? fixture.RemoteHead();
        Prior = PublisherFixture.Sha256(fixture.Blob(ExpectedStart, PublisherFixture.CurrentState));
    }

    public string Contract { get; set; } = "agencyos-canonical-publisher/v1.1";
    public string Mode { get; set; } = "PUBLISH_NEW_DELTA";
    /// <summary>Members added to the payload root, to prove an unknown member is refused.</summary>
    public Dictionary<string, object> Extra { get; } = [];
    public string Delta { get; set; } = DeltaId;
    public string Branch { get; set; } = PublisherFixture.Branch;
    public string ExpectedStart { get; set; }
    public List<string> Classes { get; set; } = ["CONTROL_ROOM"];
    public List<string> References { get; set; } = ["Adjudication of " + DeltaId];
    public bool OwnerApplies { get; set; }
    public List<string> Governing { get; set; } = [];
    public string Prior { get; set; }
    public string NextText { get; set; } = NextState();
    public string? NextSha { get; set; }
    public List<(string Section, string Claim)> Changes { get; set; } = [("Latest published delta", "`DELTA-20260102-001`: PUBLISHED.")];
    public List<(string Section, string Claim)> Unchanged { get; set; } = [("Release identity", "Release: ALPHA 0.1.0 build 97")];
    public string Kind { get; set; } = "new";
    public string EntryText { get; set; } = Entry();
    public string? Substantive { get; set; }
    public List<(string Id, string Text)> NewDecisions { get; set; } = [];
    public List<(string Id, string Status)> StatusChanges { get; set; } = [];
    public List<(string Id, string ContentBearing)> Receipts { get; set; } = [];
    public List<(string Path, string Before, string After)> Provenance { get; set; } = [];
    public List<(string Path, string Before, string After)> Corrections { get; set; } = [];
    public List<string> Allowed { get; set; } = [PublisherFixture.Deltas, PublisherFixture.CurrentState, PublisherFixture.Decisions];
    public List<string> Forbidden { get; set; } = [.. MandatoryForbidden];
    public bool ProductChange { get; set; }
    public bool SchemaChange { get; set; }
    public bool ApiChange { get; set; }
    public bool DomainExpansion { get; set; }

    public static string NextState(string latest = "`DELTA-20260102-001`: PUBLISHED. The receipt is in CANONICAL-DELTAS.md.") =>
        PublisherFixture.BaseCurrentState.Replace("`DELTA-20260101-001`: PUBLISHED.", latest, StringComparison.Ordinal);

    public static string Entry(string scope = "the fixture transition", string seal = "Seal authorizations: Pending", string id = DeltaId) =>
        $"## {id}\n\nStatus: ACCEPTED\n\nDetected: 2026-01-02T00:00:00Z\n\nPublished: Pending\n\nSources: fixture\n\n" +
        "Prior claim: The latest published delta is DELTA-20260101-001.\n\nCandidate/new claim: The fixture transition is published.\n\n" +
        $"Scope: {scope}\n\nEvidence: fixture evidence\n\nConflicts: None\n\nAuthority required: CONTROL_ROOM\n\n" +
        $"Adjudication: The Control Room accepted the fixture transition.\n\n{seal}\n\nSupersedes: None\n\nUnchanged: Release identity.\n\n" +
        "Open: None\n\nForbidden implications:\n- begin NG-4;\n\nPublication receipt: Pending\n";

    public static string Decision(string id, string authority = "CONTROL_ROOM", string supersedes = "None", string date = "2026-01-02") =>
        $"## {id}\n\nStatus: ACTIVE\n\nDate: {date}\n\nAuthority: {authority}\n\nQuestion: A new fixture question?\n\n" +
        $"Decision: A new fixture decision.\n\nScope: fixture\n\nEvidence / provenance: fixture\n\nConsequences: fixture\n\nSupersedes: {supersedes}\n\n" +
        "Unchanged: None\n\nOpen: None\n\nRecorded by: fixture\n\nPublication receipt: Pending\n";

    public static string Digest(string entryText) =>
        ParsedLedger.Parse("# Entries\n\n" + entryText, LedgerKind.Deltas).Entries[0].InvariantDigest();

    public byte[] Build()
    {
        static List<object> Pairs(IEnumerable<(string, string)> items, string a, string b) =>
            [.. items.Select(x => (object)new JsonMap { [a] = x.Item1, [b] = x.Item2 })];

        static List<object> Triples(IEnumerable<(string, string, string)> items) =>
            [.. items.Select(x => (object)new JsonMap { ["path"] = x.Item1, ["before"] = x.Item2, ["after"] = x.Item3 })];

        bool descriptive = Mode == "DESCRIPTIVE_CORRECTION";

        JsonMap payload = new()
        {
            ["contract"] = Contract,
            ["mode"] = Mode,
            ["delta_id"] = descriptive ? "None" : Delta,
            ["branch"] = Branch,
            ["expected_start_sha"] = ExpectedStart,
            ["authority"] = new JsonMap
            {
                ["classes"] = Classes.Cast<object>().ToList(),
                ["adjudication_references"] = References.Cast<object>().ToList(),
                ["owner_decision_applies"] = OwnerApplies,
                ["governing_decision_ids"] = Governing.Cast<object>().ToList(),
            },
            ["current_state"] = new JsonMap
            {
                ["prior_sha256"] = Prior,
                ["next_sha256"] = NextSha ?? PublisherFixture.Sha256(NextText),
                ["next_text"] = descriptive ? "None" : NextText,
                ["changes"] = Pairs(Changes, "section", "claim"),
                ["unchanged"] = Pairs(Unchanged, "section", "claim"),
            },
            ["delta"] = new JsonMap
            {
                ["kind"] = descriptive ? "None" : Kind,
                ["substantive_sha256"] = descriptive ? "None" : Substantive ?? Digest(EntryText),
                ["entry_text"] = descriptive ? "None" : EntryText,
            },
            ["decisions"] = new JsonMap
            {
                ["new_entries"] = Pairs(NewDecisions, "id", "entry_text"),
                ["status_changes"] = Pairs(StatusChanges, "id", "status"),
                ["receipts_to_seal"] = Pairs(Receipts, "id", "content_bearing"),
            },
            ["provenance_prose"] = Triples(Provenance),
            ["corrections"] = Triples(Corrections),
            ["paths"] = new JsonMap
            {
                ["allowed"] = Allowed.Cast<object>().ToList(),
                ["forbidden"] = Forbidden.Cast<object>().ToList(),
            },
            ["flags"] = new JsonMap
            {
                ["product_code_change"] = ProductChange,
                ["schema_change"] = SchemaChange,
                ["api_change"] = ApiChange,
                ["domain_expansion"] = DomainExpansion,
            },
        };

        foreach (KeyValuePair<string, object> extra in Extra)
        {
            payload[extra.Key] = extra.Value;
        }

        return CanonicalJson.Serialize(payload);
    }
}

/// <summary>Builds the durable seal authorization input for a verified semantic basis.</summary>
internal sealed class AuthorizationBuilder
{
    public AuthorizationBuilder(PublisherFixture fixture, string basis, string delta = PayloadBuilder.DeltaId)
    {
        Delta = delta;
        Basis = basis;
        Next = PublisherFixture.Sha256(fixture.Blob(basis, $"docs/control-room/pending/{delta}/CURRENT-STATE.next.md"));
        Payload = PublisherFixture.Sha256(fixture.Blob(basis, $"docs/control-room/pending/{delta}/PUBLICATION-PAYLOAD.json"));
    }

    /// <summary>An authorization with explicit digests, for a basis that stages nothing.</summary>
    public AuthorizationBuilder(string basis, string next, string payload)
    {
        Delta = PayloadBuilder.DeltaId;
        Basis = basis;
        Next = next;
        Payload = payload;
    }

    public string Delta { get; set; }
    public string Basis { get; set; }
    public string Next { get; set; }
    public string Payload { get; set; }
    public string Authority { get; set; } = "CONTROL_ROOM";
    public string Reference { get; set; } = "Adjudication of " + PayloadBuilder.DeltaId;
    public string Authorized { get; set; } = "2026-01-02T01:00:00Z";

    public byte[] Build() => CanonicalJson.Serialize(new JsonMap
    {
        ["contract"] = "agencyos-canonical-seal-authorization/v1",
        ["scope"] = "AUTHORIZE_SEAL_ONLY",
        ["authority"] = Authority,
        ["delta_id"] = Delta,
        ["semantic_basis_sha"] = Basis,
        ["current_state_next_sha256"] = Next,
        ["payload_sha256"] = Payload,
        ["authorized_utc"] = Authorized,
        ["reference"] = Reference,
        ["recorded_by"] = "Claude (fixture recorder)",
    });
}
