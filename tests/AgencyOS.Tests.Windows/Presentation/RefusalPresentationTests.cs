using System.Text.RegularExpressions;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That a surface which catches a refusal shows the server's reason, not the
/// problem's title.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-024</c>. <c>AgencyOsApiException.Message</c> is the title —
/// "Invalid request" — and <c>Detail</c> is the sentence the domain wrote:
/// "This target has not been approved yet. Approve it before recording a pitch."
/// Two pages and one dialog showed the first, so the rule that would have let the
/// operator proceed was hidden from them while seven other surfaces showed it.
/// </para>
/// <para>
/// Source is read rather than executed, because page code-behind cannot be
/// constructed off a UI thread. The runtime half is the review harness's refusal
/// evidence.
/// </para>
/// </remarks>
public sealed class RefusalPresentationTests
{
    /// <summary>
    /// No catch of an API refusal in the client's source reads the title without
    /// the detail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scan of every <c>.cs</c> file in the Windows and Client projects, which is
    /// an enumerable population, so "no" is a claim this can make — about source.
    /// It was named "no surface shows", which is a claim about what reaches the
    /// screen; a catch could read the detail and still display something else, and
    /// this would not see it (F-07).
    /// </para>
    /// <para>
    /// The body is read up to the first closing brace, so a catch whose handling
    /// sits after a nested block is only partly read. The positive and negative
    /// controls below pin the shapes it does recognise.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoCatchOfAnApiRefusalReadsTheTitleWithoutTheDetail()
    {
        List<string> offenders = [];

        foreach (string file in Sources())
        {
            string text = File.ReadAllText(file);

            foreach (Match caught in Regex.Matches(
                text,
                @"catch \(AgencyOsApiException (?<name>\w+)\)\s*\{(?<body>[^}]*)\}",
                RegexOptions.Singleline))
            {
                string name = caught.Groups["name"].Value;
                string body = caught.Groups["body"].Value;

                if (body.Contains($"{name}.Message", StringComparison.Ordinal)
                    && !body.Contains($"{name}.Detail", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A refusal is shown by its title rather than the server's reason "
                + "(AOS-R002-024): " + string.Join(", ", offenders));
    }

    /// <summary>The three surfaces the finding and the survey named.</summary>
    [Theory]
    [InlineData("Pages/ProjectsPage.xaml.cs")]
    [InlineData("Pages/PipelinePage.xaml.cs")]
    [InlineData("Dialogs/LinkResearchItemDialog.xaml.cs")]
    public void TheRepairedSurfacesUseTheDetail(string relative)
    {
        string text = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", relative));

        Assert.Contains("failure.Detail ?? failure.Message", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule separates the two shapes.
    /// </summary>
    /// <remarks>
    /// A positive and negative control, so a later edit that makes the rule
    /// vacuous fails here rather than passing quietly.
    /// </remarks>
    [Theory]
    [InlineData("catch (AgencyOsApiException failure) { Show(failure.Message); }", true)]
    [InlineData("catch (AgencyOsApiException failure) { Show(failure.Detail ?? failure.Message); }", false)]
    [InlineData("catch (AgencyOsApiException failure) { Show(failure.Detail); }", false)]
    [InlineData("catch (AgencyOsApiException) { Hide(); }", false)]
    public void TheRuleTellsTheTwoShapesApart(string source, bool flagged)
    {
        bool found = false;

        foreach (Match caught in Regex.Matches(
            source, @"catch \(AgencyOsApiException (?<name>\w+)\)\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline))
        {
            string name = caught.Groups["name"].Value;
            string body = caught.Groups["body"].Value;

            found |= body.Contains($"{name}.Message", StringComparison.Ordinal)
                && !body.Contains($"{name}.Detail", StringComparison.Ordinal);
        }

        Assert.Equal(flagged, found);
    }

    private static IEnumerable<string> Sources() =>
        new[] { "AgencyOS.Windows", "AgencyOS.Client" }
            .Select(project => Path.Combine(RepositoryRoot, "src", project))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

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
