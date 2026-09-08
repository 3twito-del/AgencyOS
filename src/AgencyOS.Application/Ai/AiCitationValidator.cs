using System.Text.RegularExpressions;

namespace AgencyOS.Application.Ai;

/// <summary>
/// Checks that what a brief points at is something the run actually had.
/// </summary>
/// <remarks>
/// <para>
/// A model can produce a well-formed identifier for a record that does not exist,
/// belongs to another tenant, or was withheld from this reader. Fluent prose is
/// not evidence that a citation is real, and a brief whose references cannot be
/// opened is worse than one with none — it looks checkable (§38, §39).
/// </para>
/// <para>
/// Invalid citations are removed rather than reported to the reader. Saying "the
/// model cited an object you may not see" would confirm the object exists, which
/// is precisely the disclosure the classification prevented in the first place
/// (§6).
/// </para>
/// </remarks>
public static partial class AiCitationValidator
{
    /// <summary>
    /// Removes every citation that does not resolve to supplied context.
    /// </summary>
    /// <remarks>
    /// Matches the citation form AgencyOS asks for in its prompts. Anything shaped
    /// like a citation that does not resolve is stripped; anything not shaped like
    /// one is left alone, because a model writing about "the 2024 agreement" in
    /// prose is not making a machine-checkable claim.
    /// </remarks>
    public static string Strip(string text, IReadOnlyList<AiCitationReference> allowed)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(allowed);

        HashSet<string> permitted = new(StringComparer.OrdinalIgnoreCase);

        foreach (AiCitationReference reference in allowed)
        {
            permitted.Add(Key(reference.Kind, reference.Id));
        }

        return CitationPattern().Replace(
            text,
            match =>
            {
                string kind = match.Groups["kind"].Value;

                return Guid.TryParse(match.Groups["id"].Value, out Guid id)
                    && permitted.Contains(Key(kind, id))
                        ? match.Value
                        : string.Empty;
            });
    }

    /// <summary>Whether every citation in the text resolves.</summary>
    /// <remarks>
    /// Used by tests rather than by the runtime, which strips rather than rejects:
    /// a brief with one bad reference is still worth reading without it.
    /// </remarks>
    public static bool AllResolve(string text, IReadOnlyList<AiCitationReference> allowed)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(allowed);

        HashSet<string> permitted = new(StringComparer.OrdinalIgnoreCase);

        foreach (AiCitationReference reference in allowed)
        {
            permitted.Add(Key(reference.Kind, reference.Id));
        }

        foreach (Match match in CitationPattern().Matches(text))
        {
            if (!Guid.TryParse(match.Groups["id"].Value, out Guid id)
                || !permitted.Contains(Key(match.Groups["kind"].Value, id)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Every citation the text contains, whether or not it resolves.</summary>
    public static IReadOnlyList<AiCitationReference> Extract(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<AiCitationReference> found = [];

        foreach (Match match in CitationPattern().Matches(text))
        {
            if (Guid.TryParse(match.Groups["id"].Value, out Guid id))
            {
                found.Add(new AiCitationReference(match.Groups["kind"].Value, id));
            }
        }

        return found;
    }

    private static string Key(string kind, Guid id) => $"{kind}:{id:D}";

    /// <summary>
    /// The citation form: <c>[cite:Kind:uuid]</c>.
    /// </summary>
    /// <remarks>
    /// A fixed marker rather than a heuristic. Asking the model for a specific
    /// shape means a citation is either recognizable and checkable or it is prose,
    /// with no third category that looks like a reference and cannot be verified.
    /// </remarks>
    [GeneratedRegex(
        @"\[cite:(?<kind>[A-Za-z]+):(?<id>[0-9a-fA-F-]{36})\]",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CitationPattern();
}
