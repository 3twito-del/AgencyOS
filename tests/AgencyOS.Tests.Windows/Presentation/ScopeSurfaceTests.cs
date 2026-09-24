using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Pins that the Command Center renders WindowScope's sentence and its
// captions. The sentence itself is executed in ScopeDisclosureTests.

/// <summary>
/// That the Command Center actually says which population each figure counts.
/// </summary>
/// <remarks>
/// <para>
/// F-05. What the page states is decided in <c>ScopeDisclosureTests</c>, which
/// executes the shipped sentence; these prove the page still reaches it and that
/// the surrounding labels still mean what the sentence says they mean — the F-07
/// failure applied to a disclosure, where a correct explanation is rendered by
/// nothing.
/// </para>
/// <para>
/// Nothing here changes a population. The three windows keep their headings and
/// the headline keeps its tenant-wide figure; what is asserted is that neither
/// can quietly start reading as the other.
/// </para>
/// </remarks>
public sealed class ScopeSurfaceTests
{
    /// <summary>
    /// The headline caption says the figure is a total, not the rows below.
    /// </summary>
    /// <remarks>
    /// Two words, and the whole misreading turns on them: "open tasks" above
    /// three lists is taken to mean the tasks in those lists.
    /// </remarks>
    [Fact]
    public void TheHeadlineCaptionSaysItIsATotal()
    {
        string markup = Page("CommandCenterPage");

        Assert.Contains("open tasks in total", markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">open tasks<", markup, StringComparison.Ordinal);
    }

    /// <summary>The scope sentence has somewhere to appear.</summary>
    [Fact]
    public void TheScopeSentenceIsOnThePage()
    {
        XElement scope = Assert.Single(
            XDocument.Parse(Page("CommandCenterPage")).Descendants(),
            x => (string?)x.Attribute(Name) == "ScopeText");

        // Wrapped, because a sentence clipped mid-clause explains nothing.
        Assert.Equal("Wrap", (string?)scope.Attribute("TextWrapping"));
    }

    /// <summary>
    /// The page reaches the tested sentence rather than composing its own.
    /// </summary>
    /// <remarks>
    /// A second copy in code-behind would be a second thing to disagree with the
    /// tests, and business meaning does not belong there.
    /// </remarks>
    [Fact]
    public void ThePageReadsTheTestedSentence()
    {
        string source = File.ReadAllText(
            Path.Combine(Pages, "CommandCenterPage.xaml.cs"));

        Assert.Contains("WindowScope.For(", source, StringComparison.Ordinal);

        // Both channels, from the one sentence.
        Assert.Contains("AutomationProperties.SetName(ScopeText, scope)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three windows keep their own names.
    /// </summary>
    /// <remarks>
    /// The repair must not blur them into one another: their separateness is what
    /// makes them windows rather than a single truncated list.
    /// </remarks>
    [Theory]
    [InlineData("OverdueList", "Overdue")]
    [InlineData("DueSoonList", "Due soon")]
    [InlineData("UnscheduledList", "Unscheduled")]
    public void EachWindowKeepsItsOwnName(string list, string announced)
    {
        XElement rows = Assert.Single(
            XDocument.Parse(Page("CommandCenterPage")).Descendants(),
            x => (string?)x.Attribute(Name) == list);

        Assert.Equal(announced, (string?)rows.Attribute("AutomationProperties.Name"));
    }

    /// <summary>
    /// The page can still be acted on.
    /// </summary>
    /// <remarks>
    /// A disclosure repair that cost the operator the ability to complete a task
    /// from here would have traded one defect for a worse one.
    /// </remarks>
    [Fact]
    public void TheWorkCanStillBeActedOn()
    {
        string markup = Page("CommandCenterPage");

        Assert.Contains("Complete selected task", markup, StringComparison.Ordinal);
        Assert.Contains("OnCompleteClick", markup, StringComparison.Ordinal);
    }

    private static XNamespace X => "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XName Name => X + "Name";

    private static string Pages =>
        Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages");

    private static string Page(string page) =>
        File.ReadAllText(Path.Combine(Pages, $"{page}.xaml"));

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
