using AgencyOS.Canonical.Publisher.Ledger;
using Xunit;

namespace AgencyOS.Tests.Canonical.Publisher;

// SOURCE-PROOF: Reads docs/control-room/CANONICAL-DELTAS.md and DECISIONS.md, the published
// canonical ledgers, because the parser must handle the real historical entries - above all
// DELTA-20260928-001, which predates Seal authorizations - exactly as they stand.

/// <summary>
/// The publication-invariant parser (contract section 5.4), implemented once and shared by
/// every phase.
/// </summary>
public sealed class LedgerParserTests
{
    private static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("The repository root could not be located.");
        }
    }

    private static string Published(string name) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "control-room", name)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static LedgerEntry Delta001() =>
        ParsedLedger.Parse(Published("CANONICAL-DELTAS.md"), LedgerKind.Deltas).Entry("DELTA-20260928-001")!;

    private static string Digest(string entryText) =>
        ParsedLedger.Parse("# Entries\n\n" + entryText, LedgerKind.Deltas).Entries[0].InvariantDigest();

    [Fact]
    public void TheRealLedgerParsesOnlyAfterEntriesAndNeverReadsTheTemplate()
    {
        ParsedLedger deltas = ParsedLedger.Parse(Published("CANONICAL-DELTAS.md"), LedgerKind.Deltas);
        ParsedLedger decisions = ParsedLedger.Parse(Published("DECISIONS.md"), LedgerKind.Decisions);

        Assert.Equal(["DELTA-20260928-001"], deltas.Entries.Select(x => x.Id));
        Assert.Equal(4, decisions.Entries.Count);

        foreach (LedgerEntry entry in deltas.Entries)
        {
            LedgerRules.ValidateDelta(entry, requireSealAuthorizations: false);
        }

        foreach (LedgerEntry entry in decisions.Entries)
        {
            LedgerRules.ValidateDecision(entry);
        }

        Assert.Null(Delta001().Field(LedgerKind.SealAuthorizations));
    }

    [Fact]
    public void TheHistoricalDeltaDigestIsPinned()
    {
        // Computed independently (Python, from the committed blob) and by this parser; they agree.
        Assert.Equal("397d9ce4220567d86b0cc9fdb0ed601a11e4d33c61f5f4ac916e37d5ac9902fa", Delta001().InvariantDigest());
    }

    [Fact]
    public void LifecycleMovementNeverChangesTheDigest()
    {
        string entry = Delta001().Text;
        string digest = Digest(entry);
        string pending = entry.Replace("\n\nSupersedes:", "\n\nSeal authorizations: Pending\n\nSupersedes:", StringComparison.Ordinal);
        SealRecord record = new("SA-1", "AUTHORIZE_SEAL_ONLY", "CONTROL_ROOM", "DELTA-20260928-001", new string('a', 40),
            new string('b', 64), new string('c', 64), "2026-01-01T00:00:00Z", "Adjudication of DELTA-20260928-001", "fixture");
        string one = LedgerRules.AppendSealRecord("# Entries\n\n" + pending, "DELTA-20260928-001", record)["# Entries\n\n".Length..];
        string two = LedgerRules.AppendSealRecord("# Entries\n\n" + one, "DELTA-20260928-001", record with { Record = "SA-2" })["# Entries\n\n".Length..];
        string moved = two.Replace("Status: PUBLISHED", "Status: ACCEPTED", StringComparison.Ordinal);

        Assert.Equal(digest, Digest(pending));
        Assert.Equal(digest, Digest(one));
        Assert.Equal(digest, Digest(two));
        Assert.Equal(digest, Digest(moved));
        Assert.Equal(2, LedgerRules.SealRecords(ParsedLedger.Parse("# Entries\n\n" + two, LedgerKind.Deltas).Entries[0]).Count);
    }

    [Fact]
    public void ASubstantiveEditChangesTheDigest()
    {
        string entry = Delta001().Text;

        Assert.NotEqual(Digest(entry), Digest(entry.Replace("Scope: Canonical-state migration only.", "Scope: Everything.", StringComparison.Ordinal)));
        Assert.NotEqual(Digest(entry), Digest(entry.Replace("## DELTA-20260928-001", "## DELTA-20260928-002", StringComparison.Ordinal)));
    }

    [Fact]
    public void NestedRecordFieldsAreNeverTopLevelFields()
    {
        string entry = PayloadBuilder.Entry(seal:
            "Seal authorizations:\n- Record: SA-1\n  Scope: AUTHORIZE_SEAL_ONLY\n  Authority: CONTROL_ROOM\n  Delta: DELTA-20260102-001\n" +
            "  Semantic basis: " + new string('a', 40) + "\n  CURRENT-STATE.next.md blob SHA-256: " + new string('b', 64) +
            "\n  PUBLICATION-PAYLOAD.json blob SHA-256: " + new string('c', 64) + "\n  Authorized: 2026-01-01T00:00:00Z\n" +
            "  Reference: Adjudication of DELTA-20260102-001\n  Recorded by: fixture");
        LedgerEntry parsed = ParsedLedger.Parse("# Entries\n\n" + entry, LedgerKind.Deltas).Entries[0];

        Assert.Equal("the fixture transition", parsed.HeaderValue("Scope"));
        Assert.Single(parsed.Fields, x => x.Name == "Scope");
        Assert.Equal(Digest(PayloadBuilder.Entry()), parsed.InvariantDigest());
    }

    [Fact]
    public void AppendingAnEntryLeavesTheEarlierEntryByteIdentical()
    {
        string ledger = Published("CANONICAL-DELTAS.md");
        string appended = LedgerRules.AppendEntry(ledger, LedgerKind.Deltas, PayloadBuilder.Entry());
        ParsedLedger after = ParsedLedger.Parse(appended, LedgerKind.Deltas);

        Assert.Equal(Delta001().Text, after.Entry("DELTA-20260928-001")!.Text);
        Assert.Equal(Delta001().InvariantDigest(), after.Entry("DELTA-20260928-001")!.InvariantDigest());
        Assert.Equal(PayloadBuilder.Entry(), after.Entry(PayloadBuilder.DeltaId)!.Text);
    }

    /// <summary>A synthetic ledger with two entries and a chosen run of separator blank lines between them.</summary>
    private static string TwoEntries(int separators, string first) =>
        "# Entries\n\n" + first + new string('\n', separators) + "\n" + PayloadBuilder.Entry(id: "DELTA-20260102-002");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void SeparatorBlankLinesBelongToNoEntry(int separators)
    {
        string first = PayloadBuilder.Entry();
        LedgerEntry parsed = ParsedLedger.Parse(TwoEntries(separators, first), LedgerKind.Deltas).Entry(PayloadBuilder.DeltaId)!;

        Assert.Equal(first, parsed.Text);
        Assert.Equal(Digest(first), parsed.InvariantDigest());
    }

    [Fact]
    public void AppendingALaterEntryLeavesEveryEarlierDigestUnchanged()
    {
        string ledger = TwoEntries(1, PayloadBuilder.Entry());
        ParsedLedger before = ParsedLedger.Parse(ledger, LedgerKind.Deltas);
        ParsedLedger after = ParsedLedger.Parse(LedgerRules.AppendEntry(ledger, LedgerKind.Deltas, PayloadBuilder.Entry(id: "DELTA-20260102-003")), LedgerKind.Deltas);

        foreach (LedgerEntry entry in before.Entries)
        {
            Assert.Equal(entry.InvariantDigest(), after.Entry(entry.Id)!.InvariantDigest());
        }
    }

    [Fact]
    public void AnInternalBlankLineInSubstantiveContentChangesTheDigest()
    {
        string entry = PayloadBuilder.Entry();
        string spaced = entry.Replace("Scope: the fixture transition\n", "Scope: the fixture transition\n\n", StringComparison.Ordinal);

        Assert.NotEqual(Digest(entry), Digest(spaced));
        Assert.Equal(spaced, ParsedLedger.Parse("# Entries\n\n" + spaced, LedgerKind.Deltas).Entries[0].Text);
    }

    /// <summary>
    /// The heading is part of the entry and lies in no excluded block, so it is digested.
    /// This is exactly why the Phase-3A validation script's 206d087d… value was not the
    /// normative digest: that script left out "## DELTA-20260928-001\n" (22 bytes), and
    /// hashed 4,991 bytes where the contract hashes 5,013. The reference below shares no code
    /// with the production parser.
    /// </summary>
    [Fact]
    public void TheHeadingIsDigestedAndOmittingItIsNotTheNormativeAlgorithm()
    {
        string[] lines = Delta001().Text[..^1].Split('\n');
        string[] fields =
        [
            "Open / unresolved questions", "What changes if accepted", "Candidate/new claim", "Forbidden implications",
            "Publication receipt", "Seal authorizations", "Authority required", "Claimed transition", "Adjudication",
            "Prior claim", "Supersedes", "Published", "Unchanged", "Conflicts", "Detected", "Evidence", "Sources", "Status",
            "Scope", "Open",
        ];
        HashSet<string> excluded = ["Status", "Seal authorizations", "Published", "Publication receipt"];
        List<string> body = [];
        string? field = null;

        foreach (string line in lines.Skip(1))
        {
            field = fields.FirstOrDefault(x => line.StartsWith(x + ":", StringComparison.Ordinal)) ?? field;

            if (field is null || !excluded.Contains(field))
            {
                body.Add(line);
            }
        }

        byte[] withHeading = System.Text.Encoding.UTF8.GetBytes(string.Concat(new[] { lines[0] }.Concat(body).Select(x => x + "\n")));
        byte[] withoutHeading = System.Text.Encoding.UTF8.GetBytes(string.Concat(body.Select(x => x + "\n")));

        Assert.Equal(5013, withHeading.Length);
        Assert.Equal(4991, withoutHeading.Length);
        Assert.Equal("397d9ce4220567d86b0cc9fdb0ed601a11e4d33c61f5f4ac916e37d5ac9902fa", PublisherFixture.Sha256(withHeading));
        Assert.Equal("206d087d88890bb7f8b1dd2f4d4aed825476763abb4dc9e9bb8bc39cc16ad8cd", PublisherFixture.Sha256(withoutHeading));
        Assert.Equal(PublisherFixture.Sha256(withHeading), Delta001().InvariantDigest());
    }

    [Theory]
    [InlineData("Status: ACCEPTED\n  hidden claim\n")]
    [InlineData("Published: Pending\nhidden claim without a field name\n")]
    public void TextHiddenInAnExcludedBlockIsAFormatError(string replacement)
    {
        string field = replacement[..replacement.IndexOf('\n', StringComparison.Ordinal)] + "\n";
        string entry = PayloadBuilder.Entry().Replace(field, replacement, StringComparison.Ordinal);
        LedgerEntry parsed = ParsedLedger.Parse("# Entries\n\n" + entry, LedgerKind.Deltas).Entries[0];

        Assert.Throws<LedgerFormatException>(() => LedgerRules.ValidateDelta(parsed, requireSealAuthorizations: true));
    }
}
