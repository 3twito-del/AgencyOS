using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Dialogs;

/// <summary>
/// That a handler the XAML parser runs mid-construction survives meeting a
/// half-built dialog.
/// </summary>
/// <remarks>
/// <para>
/// This is the rule <c>AOS-R002-019</c> was. Four dialogs declared a control's
/// starting state in markup — a slider's <c>Value</c>, a combo box item's
/// <c>IsSelected</c> — <em>and</em> wired the event that state raises. The parser
/// therefore called the handler while <c>InitializeComponent</c> was still
/// running, and the handler reached for a named control declared further down the
/// document, or for a field the constructor assigns afterwards. Neither existed
/// yet. The <c>NullReferenceException</c> came back as a
/// <c>XamlParseException</c>, left the dialog's constructor, left the page's
/// opener, and was discarded by the fire-and-forget dispatch that runs it — so an
/// operator clicked a button and nothing happened: no dialog, no refusal, no
/// error bar, nothing at all.
/// </para>
/// <para>
/// The repository already had the answer. Ten other dialogs in the same position
/// open the handler with an early return when the control it needs is still null,
/// and the constructor calls the same method again once the tree is whole. This
/// test is that convention written down: where markup can make a handler run
/// during load, the handler must be guarded.
/// </para>
/// <para>
/// It is a structural test over shipped markup and code-behind, because the
/// failure is silent by construction and only a build can make it stop being
/// silent. It fails against the product baseline <c>6e9b66f</c> with exactly the
/// four dialogs the finding named.
/// </para>
/// </remarks>
public sealed class LoadTimeHandlerTests
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// Events a control raises because the markup gave it a starting state, and
    /// where that state is written.
    /// </summary>
    /// <remarks>
    /// Two shapes. Most are an attribute on the control itself — a slider with a
    /// <c>Value</c>, a switch with <c>IsOn</c>. A selector is different: its
    /// starting selection is declared by a child item, so the trigger is
    /// <c>IsSelected</c> on a child rather than anything on the selector.
    /// </remarks>
    private static readonly (string Event, string? OwnAttribute, string? ChildAttribute)[] Triggers =
    [
        ("ValueChanged", "Value", null),
        ("SelectionChanged", null, "IsSelected"),
        ("Toggled", "IsOn", null),
        ("Checked", "IsChecked", null),
        ("Unchecked", "IsChecked", null),
        ("TextChanged", "Text", null),
        ("DateChanged", "Date", null),
    ];

    public static TheoryData<string> Surfaces
    {
        get
        {
            TheoryData<string> data = [];

            foreach (string file in Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows"),
                "*.xaml",
                SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(RepositoryRoot, file);

                if (relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || !File.Exists(Path.Combine(RepositoryRoot, relative + ".cs")))
                {
                    continue;
                }

                data.Add(relative);
            }

            return data;
        }
    }

    /// <summary>
    /// A handler the markup can run during load reaches nothing that does not
    /// exist yet, or refuses to run until it does.
    /// </summary>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public void LoadTimeHandlersGuardWhatTheParserHasNotBuiltYet(string relativePath)
    {
        string markupPath = Path.Combine(RepositoryRoot, relativePath);
        string codePath = markupPath + ".cs";

        string markup = File.ReadAllText(markupPath);
        string code = File.ReadAllText(codePath);

        List<string> order = DeclarationOrder(markup);
        IReadOnlyDictionary<string, string> methods = Methods(code);
        IReadOnlyCollection<string> fields = Fields(code);

        List<string> unguarded = [];

        foreach (XElement element in XElement.Load(markupPath).DescendantsAndSelf())
        {
            foreach ((string name, string? own, string? child) in Triggers)
            {
                if (element.Attribute(name)?.Value is not { Length: > 0 } handler)
                {
                    continue;
                }

                bool fires =
                    (own is not null && element.Attribute(own) is not null)
                    || (child is not null
                        && element.Elements().Any(x => x.Attribute(child)?.Value == "True"));

                if (!fires)
                {
                    continue;
                }

                string owner = element.Attribute(Xaml + "Name")?.Value ?? string.Empty;

                int position = order.IndexOf(owner);

                // Everything the handler can reach that the parser may not have
                // built when it runs: a name declared later in this document, or
                // a field the constructor assigns after InitializeComponent.
                string body = Reachable(handler, methods, []);

                List<string> exposed =
                [
                    .. position >= 0
                        ? order.Skip(position + 1).Where(x => Mentions(body, x))
                        : [],
                    .. fields.Where(x => Mentions(body, x)).Order(StringComparer.Ordinal),
                ];

                if (exposed.Count == 0 || exposed.Any(x => Guards(body, x)))
                {
                    continue;
                }

                unguarded.Add(
                    $"{owner}.{name} runs {handler} during load, which reaches "
                        + string.Join(", ", exposed)
                        + " without checking whether it exists yet");
            }
        }

        Assert.True(
            unguarded.Count == 0,
            $"{relativePath} can throw inside InitializeComponent, which the opener "
                + $"discards in silence (AOS-R002-019): {string.Join("; ", unguarded)}");
    }

    /// <summary>The <c>x:Name</c>s of one document, in the order it declares them.</summary>
    /// <remarks>
    /// Source order, because that is the order the parser creates the elements in
    /// and therefore the order in which the generated fields stop being null.
    /// </remarks>
    private static List<string> DeclarationOrder(string markup) =>
    [
        .. Regex.Matches(markup, "x:Name=\"([^\"]+)\"", RegexOptions.CultureInvariant)
            .Select(x => x.Groups[1].Value),
    ];

    /// <summary>Every method the code-behind declares, by name, with its body.</summary>
    private static IReadOnlyDictionary<string, string> Methods(string code)
    {
        Dictionary<string, string> methods = new(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(
            code,
            @"(?:private|public|internal|protected)\s+(?:static\s+)?[\w\.<>\?\[\]]+\s+(\w+)\s*\([^)]*\)\s*(=>|\{)",
            RegexOptions.CultureInvariant))
        {
            string name = match.Groups[1].Value;
            int start = match.Index + match.Length;

            string body = match.Groups[2].Value == "=>"
                ? code[start..code.IndexOf(';', start)]
                : Block(code, start);

            methods[name] = methods.TryGetValue(name, out string? existing)
                ? existing + body
                : body;
        }

        return methods;
    }

    /// <summary>The text of a block whose opening brace has already been read.</summary>
    private static string Block(string code, int start)
    {
        int depth = 1;
        int index = start;

        while (depth > 0 && index < code.Length)
        {
            depth += code[index] switch { '{' => 1, '}' => -1, _ => 0 };
            index++;
        }

        return code[start..Math.Max(start, index - 1)];
    }

    /// <summary>Every instance field, which is to say everything a constructor assigns.</summary>
    private static IReadOnlyCollection<string> Fields(string code) =>
    [
        .. Regex.Matches(
                code,
                @"^\s*private\s+(?:readonly\s+)?[\w\.<>\?\[\], ]+?\s(_\w+)\s*[;=]",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(x => x.Groups[1].Value)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>One method's body and every method in this class it calls.</summary>
    private static string Reachable(
        string method,
        IReadOnlyDictionary<string, string> methods,
        HashSet<string> seen)
    {
        if (!seen.Add(method) || !methods.TryGetValue(method, out string? body))
        {
            return string.Empty;
        }

        string text = body;

        foreach (string called in Regex.Matches(body, @"\b(\w+)\s*\(", RegexOptions.CultureInvariant)
            .Select(x => x.Groups[1].Value)
            .Distinct(StringComparer.Ordinal))
        {
            text += Reachable(called, methods, seen);
        }

        return text;
    }

    private static bool Mentions(string body, string identifier) =>
        Regex.IsMatch(body, @"\b" + Regex.Escape(identifier) + @"\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether the handler declines to run until the thing exists.
    /// </summary>
    /// <remarks>
    /// The shape the repository already uses: <c>if (X is null) return;</c>, on
    /// its own or among others. One such guard covers the handler, because the
    /// point is that it returns before touching anything rather than which name
    /// it happened to name.
    /// </remarks>
    private static bool Guards(string body, string identifier) =>
        Regex.IsMatch(
            body,
            @"\b" + Regex.Escape(identifier) + @"\s+is\s+null\b",
            RegexOptions.CultureInvariant);

    /// <summary>Walks up from the test binary to the repository root.</summary>
    private static string RepositoryRoot { get; } = Find();

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
