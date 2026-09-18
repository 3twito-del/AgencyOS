using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That a message appears under a title which names what actually failed.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-012</c>. A create the server refused was written into the list's
/// error bar, whose title is "Could not load people" — and on Companies, "Could
/// not load companies". Nothing had failed to load. The sentence underneath was
/// right, and the heading above it named a different failure.
/// </para>
/// <para>
/// The invariant is about where a message is shown, not what it says: a bar
/// titled for a load in markup carries load failures and nothing else. That is
/// the property the defect broke, and it is checkable without a UI thread.
/// </para>
/// </remarks>
public sealed class RefusalDestinationTests
{
    /// <summary>A bar titled for a load is written to only while loading.</summary>
    /// <remarks>
    /// The title is fixed in markup, so every other use of the bar is a
    /// mislabelled one. Companies also reported a single company that could not
    /// be read as the whole directory failing; that is the same defect and is
    /// covered here.
    /// </remarks>
    [Fact]
    public void ABarTitledForALoadReportsOnlyALoad()
    {
        List<string> offenders = [];

        foreach (string markup in Markup())
        {
            string code = markup + ".cs";

            if (!File.Exists(code))
            {
                continue;
            }

            string text = File.ReadAllText(code);

            foreach (string bar in LoadTitledBars(markup))
            {
                foreach (Match written in Regex.Matches(text, $@"\b{bar}\.Message\s*="))
                {
                    string method = EnclosingMethod(text, written.Index);

                    if (!IsLoadPath(text, method, []))
                    {
                        offenders.Add($"{Path.GetFileName(code)}: {bar} written in {method}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A message is shown under a title that names a different failure "
                + "(AOS-R002-012): " + string.Join("; ", offenders));
    }

    /// <summary>A bar with no title in markup is given one where it is used.</summary>
    /// <remarks>
    /// The repair adds a bar deliberately left untitled, because the title
    /// belongs to whichever command was refused. An untitled bar that is never
    /// given a title would be a heading-less error, which is the same defect
    /// from the other side.
    /// </remarks>
    [Fact]
    public void ABarWithNoTitleInMarkupIsTitledWhereItIsShown()
    {
        List<string> offenders = [];

        foreach (string markup in Markup())
        {
            string code = markup + ".cs";

            if (!File.Exists(code))
            {
                continue;
            }

            string text = File.ReadAllText(code);

            foreach (string bar in UntitledBars(markup))
            {
                if (Regex.IsMatch(text, $@"\b{bar}\.Message\s*=")
                    && !Regex.IsMatch(text, $@"\b{bar}\.Title\s*="))
                {
                    offenders.Add($"{Path.GetFileName(code)}: {bar}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A message is shown with no title at all: " + string.Join("; ", offenders));
    }

    /// <summary>The two surfaces the finding named say what was refused.</summary>
    [Theory]
    [InlineData("PeoplePage", "Could not create the person")]
    [InlineData("CompaniesPage", "Could not create the company")]
    public void ARefusedCreateNamesTheCreate(string page, string title)
    {
        string text = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages", page + ".xaml.cs"));

        Assert.Contains($"Refused(\"{title}\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ListError.Message = ex.", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule separates the two shapes.
    /// </summary>
    /// <remarks>
    /// A positive and negative control, so a later edit that makes the rule
    /// vacuous fails here rather than passing quietly.
    /// </remarks>
    [Theory]
    [InlineData("private void LoadListAsync() { ListError.Message = x; }", false)]
    [InlineData("private void RenderList() { ListError.Message = x; }", false)]
    [InlineData("private void CreatePersonAsync() { ListError.Message = x; }", true)]
    [InlineData(
        "private void LoadAsync() { Unconfigured(); }\n"
            + "private void Unconfigured() { ListError.Message = x; }",
        false)]
    [InlineData(
        "private void CreateAsync() { Unconfigured(); }\n"
            + "private void Unconfigured() { ListError.Message = x; }",
        true)]
    public void TheRuleTellsTheTwoShapesApart(string source, bool flagged)
    {
        Match written = Regex.Match(source, @"\bListError\.Message\s*=");
        string method = EnclosingMethod(source, written.Index);

        Assert.Equal(flagged, !IsLoadPath(source, method, []));
    }

    /// <summary>Whether a method only ever runs while the page is loading.</summary>
    /// <remarks>
    /// The name is the usual answer, but a load can delegate: Command Center
    /// reports an unconfigured API from a helper, and that helper is called from
    /// nowhere else. Reading only the name would have called that a mislabelled
    /// refusal, which it is not — so a method every one of whose callers is on
    /// the load path is on it too.
    /// </remarks>
    private static bool IsLoadPath(string text, string method, HashSet<string> seen)
    {
        if (method.Contains("Load", StringComparison.Ordinal)
            || method.Contains("Render", StringComparison.Ordinal))
        {
            return true;
        }

        if (!seen.Add(method))
        {
            return false;
        }

        List<string> callers = Regex
            .Matches(text, $@"(?<![\w.]){method}\s*\(")
            .Where(x => !IsDeclaration(text, x.Index))
            .Select(x => EnclosingMethod(text, x.Index))
            .ToList();

        return callers.Count > 0 && callers.TrueForAll(x => IsLoadPath(text, x, seen));
    }

    /// <summary>Bars whose markup title already names a load failure.</summary>
    private static IEnumerable<string> LoadTitledBars(string markup) =>
        Bars(markup)
            .Where(x => x.Title.StartsWith("Could not load", StringComparison.Ordinal))
            .Select(x => x.Name);

    /// <summary>Bars markup leaves untitled, to be titled by their caller.</summary>
    private static IEnumerable<string> UntitledBars(string markup) =>
        Bars(markup).Where(x => x.Title.Length == 0).Select(x => x.Name);

    /// <summary>Every named InfoBar in a page, with the title markup gives it.</summary>
    private static IEnumerable<(string Name, string Title)> Bars(string markup)
    {
        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        return XElement.Load(markup)
            .Descendants()
            .Where(x => x.Name.LocalName == "InfoBar" && x.Attribute(name) is not null)
            .Select(x => (x.Attribute(name)!.Value, x.Attribute("Title")?.Value ?? string.Empty));
    }

    /// <summary>Whether a name at this position is being declared, not called.</summary>
    private static bool IsDeclaration(string text, int index)
    {
        int start = text.LastIndexOf('\n', Math.Max(index - 1, 0)) + 1;
        string before = text[start..index];

        // A modifier opens the line and nothing has been opened since, so the
        // name that follows is being declared rather than called.
        return Regex.IsMatch(
                before,
                @"^\s*(private|public|protected|internal|static|async|override|partial|sealed)\b")
            && !before.Contains('{', StringComparison.Ordinal)
            && !before.Contains('(', StringComparison.Ordinal);
    }

    /// <summary>The method a position sits in, by the nearest signature above it.</summary>
    private static string EnclosingMethod(string text, int index)
    {
        Match? found = Regex
            .Matches(
                text[..index],
                @"(?:private|public|protected|internal)[\w\s<>?,\[\]\.]*?\s(?<name>\w+)\s*\(")
            .LastOrDefault();

        return found is null ? "«file»" : found.Groups["name"].Value;
    }

    private static IEnumerable<string> Markup() =>
        Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages"),
            "*.xaml",
            SearchOption.AllDirectories);

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
