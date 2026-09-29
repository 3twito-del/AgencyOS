using System.Globalization;
using System.Text.RegularExpressions;

namespace AgencyOS.Canonical.Publisher.Ledger;

/// <summary>One seal authorization record (contract section 5.2).</summary>
internal sealed record SealRecord(
    string Record,
    string Scope,
    string Authority,
    string Delta,
    string SemanticBasis,
    string NextSha256,
    string PayloadSha256,
    string Authorized,
    string Reference,
    string RecordedBy)
{
    public static readonly string[] Keys =
    [
        "Scope", "Authority", "Delta", "Semantic basis", "CURRENT-STATE.next.md blob SHA-256",
        "PUBLICATION-PAYLOAD.json blob SHA-256", "Authorized", "Reference", "Recorded by",
    ];

    /// <summary>The record's lines, in the exact syntax of section 5.2.</summary>
    public IReadOnlyList<string> Lines =>
    [
        "- Record: " + Record,
        "  Scope: " + Scope,
        "  Authority: " + Authority,
        "  Delta: " + Delta,
        "  Semantic basis: " + SemanticBasis,
        "  CURRENT-STATE.next.md blob SHA-256: " + NextSha256,
        "  PUBLICATION-PAYLOAD.json blob SHA-256: " + PayloadSha256,
        "  Authorized: " + Authorized,
        "  Reference: " + Reference,
        "  Recorded by: " + RecordedBy,
    ];
}

/// <summary>
/// The ledger rules the publisher checks mechanically, and the only edits it makes to
/// ledger text. Every edit is a pure function of the old text.
/// </summary>
internal static partial class LedgerRules
{
    public const string Pending = "Pending";

    /// <summary>
    /// Structural rules every delta entry must meet. The lifecycle blocks the digest
    /// excludes must have their strict shape, so that no substantive text can be hidden
    /// inside one of them and escape the publication-invariant digest.
    /// </summary>
    public static void ValidateDelta(LedgerEntry entry, bool requireSealAuthorizations)
    {
        foreach (string field in LedgerKind.Deltas.Required)
        {
            if (entry.Field(field) is null)
            {
                throw new LedgerFormatException($"{entry.Id} has no '{field}' field.");
            }
        }

        if (entry.Field("Open") is null && entry.Field("Open / unresolved questions") is null)
        {
            throw new LedgerFormatException($"{entry.Id} has no 'Open' field.");
        }

        if (requireSealAuthorizations && entry.Field(LedgerKind.SealAuthorizations) is null)
        {
            throw new LedgerFormatException($"{entry.Id} has no 'Seal authorizations' field.");
        }

        foreach (string field in new[] { LedgerKind.Status, LedgerKind.Published, LedgerKind.PublicationReceipt })
        {
            RequireSingleLine(entry, field);
        }

        if (entry.Field(LedgerKind.SealAuthorizations) is not null)
        {
            SealRecords(entry);
        }
    }

    public static void ValidateDecision(LedgerEntry entry)
    {
        foreach (string field in LedgerKind.Decisions.Required)
        {
            if (entry.Field(field) is null)
            {
                throw new LedgerFormatException($"{entry.Id} has no '{field}' field.");
            }
        }

        RequireSingleLine(entry, LedgerKind.Status);
        RequireSingleLine(entry, LedgerKind.PublicationReceipt);
        RequireSingleLine(entry, "Date");
        RequireSingleLine(entry, "Authority");
    }

    /// <summary>The rules a decision entry added by a transition must meet (DECISIONS.md).</summary>
    public static void ValidateNewDecision(LedgerEntry entry)
    {
        ValidateDecision(entry);

        if (entry.HeaderValue(LedgerKind.Status) != "ACTIVE")
        {
            throw new LedgerFormatException($"{entry.Id} is added with a status other than ACTIVE.");
        }

        if (entry.HeaderValue("Authority") is not ("OWNER" or "CONTROL_ROOM"))
        {
            throw new LedgerFormatException($"{entry.Id} names an authority other than OWNER or CONTROL_ROOM.");
        }

        if (entry.HeaderValue(LedgerKind.PublicationReceipt) != Pending)
        {
            throw new LedgerFormatException($"{entry.Id} is added with a publication receipt other than Pending.");
        }

        string date = entry.Id.Substring("DECISION-".Length, 8);
        string expected = string.Create(CultureInfo.InvariantCulture, $"{date[..4]}-{date[4..6]}-{date[6..]}");

        if (entry.HeaderValue("Date") != expected)
        {
            throw new LedgerFormatException($"{entry.Id} is dated {entry.HeaderValue("Date")}, not {expected} as its ID requires.");
        }
    }

    private static void RequireSingleLine(LedgerEntry entry, string field)
    {
        FieldBlock? block = entry.Field(field);

        if (block is not null && entry.BlockLines(block).Skip(1).Any(x => x.Trim().Length != 0))
        {
            throw new LedgerFormatException($"{entry.Id}: the '{field}' block holds text beyond its header line.");
        }
    }

    /// <summary>
    /// Parses the <c>Seal authorizations</c> block. It is either exactly
    /// <c>Seal authorizations: Pending</c>, or <c>Seal authorizations:</c> followed by one
    /// or more records in the exact section 5.2 syntax, numbered SA-1, SA-2, ….
    /// </summary>
    public static IReadOnlyList<SealRecord> SealRecords(LedgerEntry entry)
    {
        FieldBlock block = entry.Field(LedgerKind.SealAuthorizations)
            ?? throw new LedgerFormatException($"{entry.Id} has no 'Seal authorizations' field.");
        List<string> lines = [.. entry.BlockLines(block)];

        while (lines.Count > 1 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines[0] == "Seal authorizations: Pending" && lines.Count == 1)
        {
            return [];
        }

        if (lines[0] != "Seal authorizations:" || (lines.Count - 1) % 10 != 0 || lines.Count == 1)
        {
            throw new LedgerFormatException($"{entry.Id}: the 'Seal authorizations' block is not Pending and not a list of records.");
        }

        List<SealRecord> records = [];

        for (int r = 0; r < (lines.Count - 1) / 10; r++)
        {
            string[] record = [.. lines.Skip(1 + (r * 10)).Take(10)];
            string expectedId = "SA-" + (r + 1).ToString(CultureInfo.InvariantCulture);

            if (record[0] != "- Record: " + expectedId)
            {
                throw new LedgerFormatException($"{entry.Id}: record {r + 1} is not '- Record: {expectedId}'.");
            }

            string[] values = new string[SealRecord.Keys.Length];

            for (int k = 0; k < SealRecord.Keys.Length; k++)
            {
                string prefix = "  " + SealRecord.Keys[k] + ": ";

                if (!record[k + 1].StartsWith(prefix, StringComparison.Ordinal) || record[k + 1].Length == prefix.Length)
                {
                    throw new LedgerFormatException($"{entry.Id}: {expectedId} line {k + 2} is not '{prefix.Trim()} <value>'.");
                }

                values[k] = record[k + 1][prefix.Length..];
            }

            records.Add(new SealRecord(expectedId, values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]));
        }

        return records;
    }

    /// <summary>Appends a new entry at the end of the ledger, after one blank line.</summary>
    public static string AppendEntry(string ledgerText, LedgerKind kind, string entryText)
    {
        ParsedLedger ledger = ParsedLedger.Parse(ledgerText, kind);

        if (ledger.EntriesHeading < 0)
        {
            throw new LedgerFormatException("The ledger has no '# Entries' heading to append under.");
        }

        if (ledger.ContentAfterEntries)
        {
            throw new LedgerFormatException("The ledger has content after its entries, so there is no unambiguous end to append at.");
        }

        return ledgerText + "\n" + entryText;
    }

    /// <summary>Replaces one entry's text, keeping every other byte of the file.</summary>
    public static string ReplaceEntry(string ledgerText, LedgerKind kind, string id, string entryText)
    {
        ParsedLedger ledger = ParsedLedger.Parse(ledgerText, kind);
        LedgerEntry entry = ledger.Entry(id) ?? throw new LedgerFormatException($"The ledger has no entry {id} to replace.");
        List<string> lines = [.. ledger.Lines];
        lines.RemoveRange(entry.FirstLine, entry.Lines.Count);
        lines.InsertRange(entry.FirstLine, entryText[..^1].Split('\n'));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    /// Changes one lifecycle header line from its expected value to a new one. Nothing else
    /// in the entry or the file changes.
    /// </summary>
    public static string SetHeader(string ledgerText, LedgerKind kind, string id, string field, string expected, string value)
    {
        ParsedLedger ledger = ParsedLedger.Parse(ledgerText, kind);
        LedgerEntry entry = ledger.Entry(id) ?? throw new LedgerFormatException($"The ledger has no entry {id}.");
        FieldBlock block = entry.Field(field) ?? throw new LedgerFormatException($"{id} has no '{field}' field.");

        if (entry.HeaderValue(field) != expected)
        {
            throw new LedgerFormatException($"{id}: '{field}' is '{entry.HeaderValue(field)}', not '{expected}'.");
        }

        List<string> lines = [.. ledger.Lines];
        lines[entry.FirstLine + block.Start] = field + ": " + value;
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    /// Appends one record to a delta's authorization history. The first record replaces
    /// <c>Pending</c>; a later one goes after the last record. Blank lines that follow the
    /// block stay where they were.
    /// </summary>
    public static string AppendSealRecord(string ledgerText, string id, SealRecord record)
    {
        ParsedLedger ledger = ParsedLedger.Parse(ledgerText, LedgerKind.Deltas);
        LedgerEntry entry = ledger.Entry(id) ?? throw new LedgerFormatException($"The ledger has no entry {id}.");
        IReadOnlyList<SealRecord> existing = SealRecords(entry);
        FieldBlock block = entry.Field(LedgerKind.SealAuthorizations)!;
        int blockStart = entry.FirstLine + block.Start;
        int contentEnd = blockStart;

        while (contentEnd + 1 < entry.FirstLine + block.End && ledger.Lines[contentEnd + 1].Trim().Length != 0)
        {
            contentEnd++;
        }

        List<string> lines = [.. ledger.Lines];

        if (existing.Count == 0)
        {
            lines[blockStart] = "Seal authorizations:";
            lines.InsertRange(blockStart + 1, record.Lines);
        }
        else
        {
            lines.InsertRange(contentEnd + 1, record.Lines);
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Every DELTA- and DECISION- identifier in a text.</summary>
    public static IReadOnlyList<string> CitedIds(string text) =>
        [.. CitedId().Matches(text).Select(x => x.Value).Distinct(StringComparer.Ordinal)];

    [GeneratedRegex("(?<![A-Z0-9-])(?:DELTA|DECISION)-[0-9]{8}-[0-9]{3}(?![0-9])")]
    private static partial Regex CitedId();
}
