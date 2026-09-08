using System.Globalization;
using System.Text;

namespace AgencyOS.Windows.Platform.Diagnostics;

/// <summary>
/// One fact about the installation, safe to paste into a support message.
/// </summary>
/// <param name="Name">What it is called. Stable, so support can ask for one.</param>
/// <param name="Value">
/// The value, already rendered. Never an object, because the moment a diagnostic
/// carries an object somebody serializes it and the serializer decides what is
/// safe (ADR-0034).
/// </param>
public sealed record DiagnosticField(string Name, string Value);

/// <summary>
/// What AgencyOS will say about itself.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Built from an allow-list, never redacted from a dump.</strong> A
/// redactor has to recognize every shape of secret it might meet and is wrong the
/// first time it meets a new one; an allow-list is wrong only about things
/// somebody deliberately added. That asymmetry is the whole design (§V,
/// ADR-0034).
/// </para>
/// <para>
/// Nothing here names a person, a company, a deal, a contract or a document. The
/// summary describes the <em>installation</em> — versions, capability,
/// connectivity — because that is what a support conversation needs and because a
/// diagnostic is pasted into channels AgencyOS does not control.
/// </para>
/// </remarks>
public sealed class DiagnosticSummary
{
    private readonly List<DiagnosticField> _fields = [];

    /// <summary>
    /// The only field names this summary will carry.
    /// </summary>
    /// <remarks>
    /// A test asserts that every field added is one of these, so adding a
    /// diagnostic is a deliberate act that shows up in review rather than a line
    /// somebody slipped into a builder.
    /// </remarks>
    public static IReadOnlySet<string> Permitted { get; } = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "AgencyOS version",
        "Release channel",
        "API contract",
        "Server compatibility",
        "Windows version",
        "Windows App SDK",
        "Architecture",
        "Local AI readiness",
        "Execution providers",
        "Cache schema",
        "Connectivity",
        "Last sync",
    };

    /// <summary>
    /// Words that must never appear in a diagnostic value.
    /// </summary>
    /// <remarks>
    /// A second, cruder net under the allow-list. It cannot catch a leak that uses
    /// none of these words, and it is not relied on to: it exists so that an
    /// obviously wrong value fails a test rather than reaching a support channel.
    /// </remarks>
    public static IReadOnlyList<string> Forbidden { get; } =
    [
        "password", "secret", "apikey", "api-key", "api_key", "bearer",
        "token", "credential", "connectionstring", "authorization",
    ];

    /// <summary>The fields gathered, in the order they were added.</summary>
    public IReadOnlyList<DiagnosticField> Fields => _fields;

    /// <summary>
    /// Adds a permitted field.
    /// </summary>
    /// <remarks>
    /// Refuses an unlisted name outright. A diagnostic builder that silently
    /// dropped an unknown field would make the allow-list advisory, and the
    /// failure would be a missing line nobody noticed.
    /// </remarks>
    public DiagnosticSummary Add(string name, string? value)
    {
        if (!Permitted.Contains(name))
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                name,
                "Diagnostics are built from an allow-list. Add the field to "
                    + $"{nameof(DiagnosticSummary)}.{nameof(Permitted)} deliberately, or do "
                    + "not report it.");
        }

        _fields.Add(new DiagnosticField(name, Clean(value)));

        return this;
    }

    /// <summary>Renders the summary for the clipboard.</summary>
    public string Render()
    {
        StringBuilder builder = new();

        int width = _fields.Count == 0 ? 0 : _fields.Max(x => x.Name.Length);

        foreach (DiagnosticField field in _fields)
        {
            builder
                .Append(field.Name.PadRight(width))
                .Append("  ")
                .AppendLine(field.Value);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Normalizes a value and refuses the obviously unsafe.
    /// </summary>
    /// <remarks>
    /// Newlines are collapsed so one field cannot forge others, and an absent
    /// value reads as "unknown" rather than as blank — a blank line in a support
    /// paste is indistinguishable from a field that was never gathered.
    /// </remarks>
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        string flattened = string.Join(
            ' ',
            value.Split(
                ['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (string forbidden in Forbidden)
        {
            if (flattened.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return "[withheld]";
            }
        }

        return flattened.Length <= 200
            ? flattened
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{flattened[..200]}…");
    }
}
