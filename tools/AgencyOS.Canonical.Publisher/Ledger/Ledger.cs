using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AgencyOS.Canonical.Publisher.Ledger;

/// <summary>A ledger that cannot be parsed under the contract's rules.</summary>
internal sealed class LedgerFormatException(string message) : Exception(message);

/// <summary>The closed field vocabulary of one ledger.</summary>
internal sealed partial class LedgerKind
{
    private LedgerKind(string headingPrefix, Regex id, string[] fields, string[] required, string[] excluded)
    {
        HeadingPrefix = headingPrefix;
        IdPattern = id;
        FieldNames = [.. fields.OrderByDescending(x => x.Length).ThenBy(x => x, StringComparer.Ordinal)];
        Required = required;
        Excluded = new HashSet<string>(excluded, StringComparer.Ordinal);
    }

    public string HeadingPrefix { get; }

    public Regex IdPattern { get; }

    /// <summary>Longest first, so that <c>Open / unresolved questions</c> wins over <c>Open</c>.</summary>
    public IReadOnlyList<string> FieldNames { get; }

    public IReadOnlyList<string> Required { get; }

    /// <summary>The blocks the publication-invariant digest leaves out (contract section 5.4).</summary>
    public IReadOnlySet<string> Excluded { get; }

    public const string Status = "Status";
    public const string Adjudication = "Adjudication";
    public const string SealAuthorizations = "Seal authorizations";
    public const string Published = "Published";
    public const string PublicationReceipt = "Publication receipt";
    public const string Supersedes = "Supersedes";
    public const string ForbiddenImplications = "Forbidden implications";

    /// <summary>
    /// CANONICAL-DELTAS.md: the substantive fields, the lifecycle fields, and the
    /// spellings its template and entries use.
    /// </summary>
    public static readonly LedgerKind Deltas = new(
        "## DELTA-",
        DeltaId(),
        [
            "Status", "Detected", "Published", "Sources", "Prior claim", "Candidate/new claim",
            "Claimed transition", "Scope", "Evidence", "Conflicts", "Authority required", "Adjudication",
            "Seal authorizations", "What changes if accepted", "Supersedes", "Unchanged", "Open",
            "Open / unresolved questions", "Forbidden implications", "Publication receipt",
        ],
        [
            "Status", "Detected", "Published", "Sources", "Prior claim", "Candidate/new claim", "Scope",
            "Evidence", "Conflicts", "Authority required", "Adjudication", "Supersedes", "Unchanged",
            "Publication receipt",
        ],
        ["Status", "Seal authorizations", "Published", "Publication receipt"]);

    /// <summary>DECISIONS.md: the thirteen fields of its template.</summary>
    public static readonly LedgerKind Decisions = new(
        "## DECISION-",
        DecisionId(),
        [
            "Status", "Date", "Authority", "Question", "Decision", "Scope", "Evidence / provenance",
            "Consequences", "Supersedes", "Unchanged", "Open", "Recorded by", "Publication receipt",
        ],
        [
            "Status", "Date", "Authority", "Question", "Decision", "Scope", "Evidence / provenance",
            "Consequences", "Supersedes", "Unchanged", "Open", "Recorded by", "Publication receipt",
        ],
        []);

    [GeneratedRegex("^DELTA-[0-9]{8}-[0-9]{3}$")]
    public static partial Regex DeltaId();

    [GeneratedRegex("^DECISION-[0-9]{8}-[0-9]{3}$")]
    public static partial Regex DecisionId();
}

/// <summary>One field: its header line and every line that belongs to it.</summary>
internal sealed record FieldBlock(string Name, int Start, int End)
{
    public int Length => End - Start;
}

/// <summary>
/// One entry. Its text runs from its heading to its last non-blank line; the blank lines
/// that separate entries belong to no entry (contract section 5.4, rule 1).
/// </summary>
internal sealed class LedgerEntry
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public LedgerEntry(string id, int firstLine, IReadOnlyList<string> lines, IReadOnlyList<FieldBlock> fields, LedgerKind kind)
    {
        Id = id;
        FirstLine = firstLine;
        Lines = lines;
        Fields = fields;
        Kind = kind;
    }

    public string Id { get; }

    /// <summary>The heading's line index in the ledger file.</summary>
    public int FirstLine { get; }

    public int LastLine => FirstLine + Lines.Count - 1;

    public IReadOnlyList<string> Lines { get; }

    public IReadOnlyList<FieldBlock> Fields { get; }

    public LedgerKind Kind { get; }

    public string Text => string.Join('\n', Lines) + "\n";

    public FieldBlock? Field(string name) => Fields.FirstOrDefault(x => x.Name == name);

    public IReadOnlyList<string> BlockLines(FieldBlock block) => [.. Lines.Skip(block.Start).Take(block.Length)];

    /// <summary>The text after <c>Name:</c> on the field's header line, trimmed.</summary>
    public string? HeaderValue(string name)
    {
        FieldBlock? block = Field(name);
        return block is null ? null : Lines[block.Start][(name.Length + 1)..].Trim();
    }

    /// <summary>The block's content: its header value and every following non-blank line.</summary>
    public string BlockContent(string name)
    {
        FieldBlock? block = Field(name);

        if (block is null)
        {
            return string.Empty;
        }

        IEnumerable<string> content = new[] { Lines[block.Start][(name.Length + 1)..].Trim() }
            .Concat(BlockLines(block).Skip(1).Where(x => x.Trim().Length != 0));
        return string.Join('\n', content.Where(x => x.Length != 0));
    }

    /// <summary>
    /// The publication-invariant digest (contract section 5.4): SHA-256 over every line
    /// of the entry, heading included, except the excluded lifecycle blocks, each line
    /// LF-terminated, as UTF-8.
    /// </summary>
    public string InvariantDigest()
    {
        StringBuilder retained = new();
        HashSet<int> excluded = [];

        foreach (FieldBlock block in Fields.Where(x => Kind.Excluded.Contains(x.Name)))
        {
            for (int i = block.Start; i < block.End; i++)
            {
                excluded.Add(i);
            }
        }

        for (int i = 0; i < Lines.Count; i++)
        {
            if (!excluded.Contains(i))
            {
                retained.Append(Lines[i]).Append('\n');
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(retained.ToString())));
    }
}

/// <summary>A parsed ledger file.</summary>
internal sealed class ParsedLedger
{
    private ParsedLedger(IReadOnlyList<string> lines, int entriesHeading, IReadOnlyList<LedgerEntry> entries, bool contentAfterEntries, LedgerKind kind)
    {
        Lines = lines;
        EntriesHeading = entriesHeading;
        Entries = entries;
        ContentAfterEntries = contentAfterEntries;
        Kind = kind;
    }

    /// <summary>The file's lines; the file ends with LF, which is not a line.</summary>
    public IReadOnlyList<string> Lines { get; }

    public int EntriesHeading { get; }

    public IReadOnlyList<LedgerEntry> Entries { get; }

    /// <summary>Whether a non-entry, non-blank line follows an entry.</summary>
    public bool ContentAfterEntries { get; }

    public LedgerKind Kind { get; }

    public LedgerEntry? Entry(string id) => Entries.FirstOrDefault(x => x.Id == id);

    public static ParsedLedger Parse(string text, LedgerKind kind)
    {
        if (text.Contains('\r', StringComparison.Ordinal))
        {
            throw new LedgerFormatException("The ledger contains a carriage return; ledgers are LF only.");
        }

        if (text.Length != 0 && !text.EndsWith('\n'))
        {
            throw new LedgerFormatException("The ledger does not end with a line feed.");
        }

        string[] lines = text.Length == 0 ? [] : text[..^1].Split('\n');
        int heading = Array.IndexOf(lines, "# Entries");
        List<LedgerEntry> entries = [];
        bool contentAfter = false;

        if (heading >= 0)
        {
            int i = heading + 1;

            while (i < lines.Length)
            {
                if (!lines[i].StartsWith(kind.HeadingPrefix, StringComparison.Ordinal))
                {
                    if (entries.Count != 0 && lines[i].Trim().Length != 0)
                    {
                        contentAfter = true;
                    }

                    i++;
                    continue;
                }

                int end = i + 1;

                while (end < lines.Length && !(lines[end].StartsWith('#') || lines[end] == "---"))
                {
                    end++;
                }

                int last = end - 1;

                while (last > i && lines[last].Trim().Length == 0)
                {
                    last--;
                }

                entries.Add(ParseEntry(lines, i, last, kind));
                i = end;
            }
        }

        List<string> duplicates = [.. entries.GroupBy(x => x.Id).Where(x => x.Count() > 1).Select(x => x.Key)];

        if (duplicates.Count != 0)
        {
            throw new LedgerFormatException($"The ledger has duplicate entries: {string.Join(", ", duplicates)}.");
        }

        return new ParsedLedger(lines, heading, entries, contentAfter, kind);
    }

    private static LedgerEntry ParseEntry(string[] fileLines, int first, int last, LedgerKind kind)
    {
        string heading = fileLines[first];
        string id = heading[3..].Trim();

        if (!kind.IdPattern.IsMatch(id) || heading != "## " + id)
        {
            throw new LedgerFormatException($"'{heading}' is not a well-formed entry heading.");
        }

        List<string> lines = [.. fileLines.Skip(first).Take(last - first + 1)];
        List<(string Name, int Start)> headers = [];

        for (int k = 1; k < lines.Count; k++)
        {
            string? name = kind.FieldNames.FirstOrDefault(x => lines[k].StartsWith(x + ":", StringComparison.Ordinal));

            if (name is not null)
            {
                headers.Add((name, k));
            }
            else if (headers.Count == 0 && lines[k].Trim().Length != 0)
            {
                throw new LedgerFormatException($"{id} has text before its first field.");
            }
        }

        List<FieldBlock> fields = [];

        for (int h = 0; h < headers.Count; h++)
        {
            int end = h + 1 < headers.Count ? headers[h + 1].Start : lines.Count;
            fields.Add(new FieldBlock(headers[h].Name, headers[h].Start, end));
        }

        List<string> repeated = [.. fields.GroupBy(x => x.Name).Where(x => x.Count() > 1).Select(x => x.Key)];

        if (repeated.Count != 0)
        {
            throw new LedgerFormatException($"{id} repeats the field(s) {string.Join(", ", repeated)}.");
        }

        return new LedgerEntry(id, first, lines, fields, kind);
    }
}
