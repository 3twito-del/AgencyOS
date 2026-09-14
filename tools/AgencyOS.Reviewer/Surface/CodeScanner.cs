using System.Text.RegularExpressions;
using System.IO;

namespace AgencyOS.Reviewer.Surface;

/// <summary>
/// What a page's code-behind says about itself.
/// </summary>
/// <param name="RelativePath">Repository-relative path of the code-behind.</param>
/// <param name="TypeName">The page or dialog type.</param>
/// <param name="DispatchedCommands">Command identifiers the file answers.</param>
/// <param name="OpenedDialogs">Dialog types the file constructs.</param>
/// <param name="ApiCalls">API client members the file calls.</param>
/// <param name="EventHandlers">Click and selection handlers the file declares.</param>
public sealed record CodeBehindFacts(
    string RelativePath,
    string TypeName,
    IReadOnlyList<string> DispatchedCommands,
    IReadOnlyList<string> OpenedDialogs,
    IReadOnlyList<string> ApiCalls,
    IReadOnlyList<string> EventHandlers);

/// <summary>
/// Reads the Windows client's code-behind for facts about reachability.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately textual rather than a Roslyn workspace. The three questions this
/// answers - which command identifiers a page answers, which dialog types it
/// constructs, and which client calls it makes - are all visible in the literal
/// text, and a compiler-accurate parse would not change any of the answers while
/// making the harness depend on loading the whole solution.
/// </para>
/// <para>
/// The one place that would mislead is a command identifier assembled at run time
/// rather than written as a literal. <see cref="DynamicDispatchSuspects"/> reports
/// those separately instead of pretending the file dispatches nothing.
/// </para>
/// </remarks>
public static class CodeScanner
{
    private static readonly Regex CaseLiteral =
        new("""case\s+"([^"]+)"\s*:""", RegexOptions.Compiled);

    private static readonly Regex EqualityLiteral =
        new("""commandId\s*==\s*"([^"]+)"|"([^"]+)"\s*==\s*commandId""", RegexOptions.Compiled);

    // Two shapes, because the client writes both. An explicitly typed
    // construction, and the target-typed form the pages actually prefer:
    // "CreateDealDialog dialog = new() { XamlRoot = XamlRoot };". Matching only
    // the first would report every dialog in the product as unreachable, which is
    // the most confidently wrong answer this scanner could give.
    private static readonly Regex DialogConstruction = new(
        @"new\s+([A-Z][A-Za-z0-9_]*Dialog)\s*[({]"
            + @"|([A-Z][A-Za-z0-9_]*Dialog)\s+[a-zA-Z_][A-Za-z0-9_]*\s*=\s*new\s*[({]",
        RegexOptions.Compiled);

    private static readonly Regex ApiCall =
        new(@"_api\.([A-Za-z0-9_]+)\s*\(|api\.([A-Za-z0-9_]+)Async\s*\(", RegexOptions.Compiled);

    private static readonly Regex HandlerDeclaration =
        new(@"private\s+(?:async\s+)?void\s+(On[A-Za-z0-9_]+)\s*\(", RegexOptions.Compiled);

    /// <summary>Reads every page and dialog code-behind.</summary>
    public static IReadOnlyList<CodeBehindFacts> Scan(SourceIndex source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<CodeBehindFacts> facts = [];

        foreach ((string path, string text) in source.Under("src/AgencyOS.Windows/", ".xaml.cs"))
        {
            string typeName = Path.GetFileName(path).Replace(".xaml.cs", string.Empty, StringComparison.Ordinal);

            facts.Add(new CodeBehindFacts(
                path,
                typeName,
                ReadDispatchedCommands(text),
                Distinct(DialogConstruction.Matches(text)
                    .Select(x => x.Groups[1].Success ? x.Groups[1].Value : x.Groups[2].Value)),
                Distinct(ApiCall.Matches(text)
                    .Select(x => x.Groups[1].Success ? x.Groups[1].Value : x.Groups[2].Value + "Async")),
                Distinct(HandlerDeclaration.Matches(text).Select(x => x.Groups[1].Value))));
        }

        return facts;
    }

    /// <summary>
    /// Files that dispatch on a command identifier they did not write literally.
    /// </summary>
    /// <remarks>
    /// Reported rather than silently missed. A page that switched on a computed
    /// identifier would make every "this command is never dispatched" finding
    /// unsound, so the harness says when it cannot see the whole picture.
    /// </remarks>
    public static IReadOnlyList<string> DynamicDispatchSuspects(SourceIndex source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<string> suspects = [];

        foreach ((string path, string text) in source.Under("src/AgencyOS.Windows/", ".xaml.cs"))
        {
            string body = ExecuteBody(text);

            if (body.Length == 0)
            {
                continue;
            }

            // A switch arm or comparison whose right-hand side is not a literal.
            if (Regex.IsMatch(body, @"case\s+[A-Za-z_]", RegexOptions.None, TimeSpan.FromSeconds(2))
                || Regex.IsMatch(body, @"commandId\s*\.\s*StartsWith", RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                suspects.Add(path);
            }
        }

        return suspects;
    }

    /// <summary>Command identifiers a code-behind answers, read from its Execute body.</summary>
    private static IReadOnlyList<string> ReadDispatchedCommands(string text)
    {
        string body = ExecuteBody(text);

        if (body.Length == 0)
        {
            return [];
        }

        List<string> commands = [.. CaseLiteral.Matches(body).Select(x => x.Groups[1].Value)];

        foreach (Match match in EqualityLiteral.Matches(body))
        {
            string value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;

            if (value.Length > 0)
            {
                commands.Add(value);
            }
        }

        return Distinct(commands);
    }

    /// <summary>
    /// The body of <c>Execute(string commandId)</c>, by brace matching.
    /// </summary>
    /// <remarks>
    /// Brace matching rather than a regex over the whole file, because a page
    /// contains other switches over other strings - tab names, status values - and
    /// counting those as dispatched commands would hide exactly the dead-command
    /// defect this scan exists to find.
    /// </remarks>
    internal static string ExecuteBody(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int start = text.IndexOf("void Execute(string commandId)", StringComparison.Ordinal);

        if (start < 0)
        {
            return string.Empty;
        }

        int open = text.IndexOf('{', start);

        if (open < 0)
        {
            return string.Empty;
        }

        int depth = 0;

        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return text[open..(i + 1)];
                }
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values) =>
        [.. values.Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)];
}
