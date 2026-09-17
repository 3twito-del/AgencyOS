using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Layout;

/// <summary>
/// That no list is wrapped in a scroll viewer of its own.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-022</c>. Eleven detail tabs held a <c>ListView</c> as the whole
/// content of a <c>ScrollViewer</c>. A list already scrolls itself; wrapped
/// again, an <em>empty</em> list with padding alternated between two heights on
/// every layout pass — its own scroll bar appearing and disappearing — until WinUI
/// raised <c>LayoutCycleException</c> (<c>0x802B0014</c>). Nothing handles that, so
/// the framework ended the process with <c>0xC000027B</c>. Opening Attachments on
/// any project did it, because that list is never filled.
/// </para>
/// <para>
/// Measured, not inferred. In a temporary diagnostic build, with the tabs still
/// empty: the list with its padding removed survived; the list inside a
/// <c>StackPanel</c> survived; the list with the outer viewer removed survived; the
/// list inside a one-child <c>Grid</c> still crashed. The repair removes the outer
/// viewer, which is the one of those that is also correct on its own terms — a
/// list measured at infinite height cannot virtualize its rows.
/// </para>
/// <para>
/// The crash itself needs a live WinUI window and cannot be raised in a hosted
/// test. This pins the markup shape that produced it; the process-level evidence
/// is the review harness's <c>crash-probe</c> run, kept under
/// <c>artifacts/reviewer/run-repair-003a1/</c>.
/// </para>
/// </remarks>
public sealed class SelfScrollingListTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Controls that bring their own scroll viewer in their template.</summary>
    private static readonly string[] SelfScrolling = ["ListView", "GridView"];

    /// <summary>No markup in the client wraps a list in a second scroll viewer.</summary>
    [Fact]
    public void NoListIsWrappedInAScrollViewerOfItsOwn()
    {
        List<string> found = [];

        foreach (string file in Markup())
        {
            found.AddRange(Violations(XElement.Load(file))
                .Select(x => Path.GetFileName(file) + ":" + x));
        }

        Assert.True(
            found.Count == 0,
            "A list is the whole content of a ScrollViewer. Empty and padded, that is a "
                + "layout cycle and the client exits (AOS-R002-022): "
                + string.Join(", ", found));
    }

    /// <summary>The eleven lists the repair changed scroll themselves.</summary>
    [Theory]
    [InlineData("ProjectsPage.xaml", "AttachmentList")]
    [InlineData("ProjectsPage.xaml", "MaterialList")]
    [InlineData("ProjectsPage.xaml", "PackageList")]
    [InlineData("ProjectsPage.xaml", "HistoryList")]
    [InlineData("PipelinePage.xaml", "PitchList")]
    [InlineData("PipelinePage.xaml", "SubjectList")]
    [InlineData("PipelinePage.xaml", "TaskList")]
    [InlineData("PipelinePage.xaml", "HistoryList")]
    [InlineData("DealsPage.xaml", "TaskList")]
    [InlineData("DealsPage.xaml", "HistoryList")]
    [InlineData("ContractsPage.xaml", "HistoryList")]
    public void TheRepairedListIsTheTabsOwnContent(string page, string list)
    {
        XElement element = Page(page)
            .Descendants(Presentation + "ListView")
            .Single(x => (string?)x.Attribute(Xaml + "Name") == list);

        Assert.Equal("TabViewItem", element.Parent?.Name.LocalName);
    }

    /// <summary>The rule separates the shapes that crashed from the ones that did not.</summary>
    /// <remarks>
    /// Each case is one of the diagnostic variants, run live against an empty list.
    /// </remarks>
    [Theory]
    [InlineData("<ScrollViewer><ListView Padding='4' /></ScrollViewer>", true)]
    [InlineData("<ScrollViewer><GridView Padding='4' /></ScrollViewer>", true)]
    [InlineData("<ScrollViewer><Grid><ListView Padding='4' /></Grid></ScrollViewer>", true)]
    [InlineData("<ScrollViewer><StackPanel><ListView Padding='4' /></StackPanel></ScrollViewer>", false)]
    [InlineData("<ScrollViewer><Grid><TextBlock /><ListView Grid.Row='1' /></Grid></ScrollViewer>", false)]
    [InlineData("<TabViewItem><ListView Padding='4' /></TabViewItem>", false)]
    [InlineData("<ScrollViewer><ScrollViewer.Resources /><StackPanel /></ScrollViewer>", false)]
    public void TheRuleTellsTheShapesApart(string markup, bool flagged)
    {
        XElement root = XElement.Parse(
            "<Page xmlns='" + Presentation.NamespaceName + "'>" + markup + "</Page>");

        Assert.Equal(flagged, Violations(root).Any());
    }

    /// <summary>
    /// Lists that sit under a scroll viewer in a shape that survived are left alone.
    /// </summary>
    /// <remarks>
    /// Forty-three lists sit inside a <c>StackPanel</c> under a viewer, and two are
    /// one row of a many-row page grid. The first shape survived empty in the
    /// diagnostic build; the second is how Organization and Sync are laid out, and
    /// both pages were opened empty without the client exiting.
    /// </remarks>
    [Theory]
    [InlineData("ProjectsPage.xaml", "RoleList")]
    [InlineData("PipelinePage.xaml", "TargetList")]
    [InlineData("ContractsPage.xaml", "TaskList")]
    [InlineData("OrganizationPage.xaml", "MemberList")]
    [InlineData("SyncPage.xaml", "QueueList")]
    public void ASurvivingShapeIsNotFlagged(string page, string list)
    {
        XElement root = Page(page);

        Assert.Contains(
            root.Descendants(Presentation + "ListView"),
            x => (string?)x.Attribute(Xaml + "Name") == list
                && x.Ancestors(Presentation + "ScrollViewer").Any());

        Assert.DoesNotContain(Violations(root), x => x == list);
    }

    /// <summary>
    /// The names of lists that are a scroll viewer's whole content, directly or as
    /// the only child of a <c>Grid</c>.
    /// </summary>
    private static IEnumerable<string> Violations(XElement root)
    {
        foreach (XElement viewer in root.DescendantsAndSelf(Presentation + "ScrollViewer"))
        {
            XElement? content = Only(viewer);

            if (content is not null && content.Name.LocalName == "Grid")
            {
                content = Only(content);
            }

            if (content is not null && SelfScrolling.Contains(content.Name.LocalName))
            {
                yield return (string?)content.Attribute(Xaml + "Name") ?? content.Name.LocalName;
            }
        }
    }

    /// <summary>
    /// The single content child of an element, ignoring property elements such as
    /// <c>ScrollViewer.Resources</c>.
    /// </summary>
    private static XElement? Only(XElement parent)
    {
        XElement[] children =
        [
            .. parent.Elements().Where(x => !x.Name.LocalName.Contains('.', StringComparison.Ordinal)),
        ];

        return children.Length == 1 ? children[0] : null;
    }

    private static IEnumerable<string> Markup() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows"), "*.xaml", SearchOption.AllDirectories)
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static XElement Page(string name) =>
        XElement.Load(Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages", name));

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
