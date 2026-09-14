using System.Globalization;
using System.Text;

namespace AgencyOS.Client.Presentation;

/// <summary>
/// Turns a domain token into the words a person reads.
/// </summary>
/// <remarks>
/// <para>
/// AgencyOS names its domain values in PascalCase — <c>TalentEmployment</c>,
/// <c>ProjectLicense</c>, <c>SourceSensitive</c> — and Audit 001 found those
/// tokens reaching the screen unchanged, two inches below a filter that spelled
/// the same value out in full (<c>AOS-R001-012</c>). "Internal implementation
/// terminology exposed to users" is the defect; "Talent employment" is the fix.
/// </para>
/// <para>
/// <strong>This is presentation only.</strong> No domain enum is renamed, no
/// contract value changes, and nothing here is ever parsed back: the identity of
/// a value stays the token the server sent. The label is derived on the way to a
/// screen and thrown away.
/// </para>
/// <para>
/// <strong>It does not simplify meaning.</strong> The product keeps distinctions
/// that matter — status is not stage, recorded is not sent, a source is not the
/// truth, an obligation is not a task, a prediction is not a fact — and this
/// splits words without touching any of them. Where a token is already the term
/// the industry uses, <see cref="Known"/> leaves it alone rather than inventing a
/// friendlier one.
/// </para>
/// </remarks>
public static class DisplayLabel
{
    /// <summary>
    /// Tokens that are already how the business writes them.
    /// </summary>
    /// <remarks>
    /// Splitting these would be worse than leaving them: an agency writes "AI" and
    /// "P&amp;L", not "A I". Entries are exceptions to the rule below, not a
    /// glossary of everything the domain contains.
    /// </remarks>
    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
    {
        ["AI"] = "AI",
        ["Ai"] = "AI",
        ["ProjectLicense"] = "Project licence",
        ["NoDeal"] = "No deal",
        ["UnknownOutcome"] = "Unknown outcome",
        ["MoreMaterialRequested"] = "More material requested",
        ["MeetingRequested"] = "Meeting requested",
    };

    /// <summary>
    /// Writes a token as a sentence-case phrase.
    /// </summary>
    /// <remarks>
    /// Sentence case rather than title case, because these appear inside prose and
    /// in table cells beside ordinary words. "Talent employment", not "Talent
    /// Employment".
    /// </remarks>
    /// <param name="token">A PascalCase domain token, or anything else.</param>
    /// <returns>The phrase to show, or the input unchanged when it is not a token.</returns>
    public static string For(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        string trimmed = token.Trim();

        if (Known.TryGetValue(trimmed, out string? known))
        {
            return known;
        }

        // Anything already written for people - it has a space, or it is not the
        // shape of an identifier - is left exactly as it is. This runs over
        // display strings as well as tokens, and rewriting a sentence would be a
        // bug rather than a formatting choice.
        if (trimmed.Contains(' ', StringComparison.Ordinal) || !IsToken(trimmed))
        {
            return trimmed;
        }

        StringBuilder text = new(trimmed.Length + 8);

        for (int index = 0; index < trimmed.Length; index++)
        {
            char current = trimmed[index];

            bool boundary =
                index > 0
                && char.IsUpper(current)
                && (!char.IsUpper(trimmed[index - 1])

                    // The end of an acronym: the P of "PdfDocument" starts a word,
                    // the D of "PDFDocument" does not, but its successor does.
                    || (index + 1 < trimmed.Length && char.IsLower(trimmed[index + 1])));

            if (boundary)
            {
                text.Append(' ');
                text.Append(char.ToLower(current, CultureInfo.InvariantCulture));

                continue;
            }

            // Digits begin a word too: "Level2" reads as "Level 2".
            if (index > 0 && char.IsDigit(current) && !char.IsDigit(trimmed[index - 1]))
            {
                text.Append(' ');
                text.Append(current);

                continue;
            }

            text.Append(index == 0 ? char.ToUpper(current, CultureInfo.InvariantCulture) : current);
        }

        return text.ToString();
    }

    /// <summary>Whether a string is shaped like a domain token rather than prose.</summary>
    private static bool IsToken(string value)
    {
        if (!char.IsLetter(value[0]))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
