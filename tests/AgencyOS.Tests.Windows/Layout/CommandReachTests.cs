using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Layout;

// SOURCE-PROOF: Asserts overflow containers in markup and a bring-into-view call in
// the shell's selection handler. The names say these are markup and source facts,
// not observed scrolling.

/// <summary>
/// That a command on screen can be reached at the window sizes people use.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-021</c>. Five contract commands sat in a horizontal
/// <c>StackPanel</c> with no overflow. At 1600x1000 the last two were enabled,
/// reported <c>IsOffscreen</c>, had no bounding rectangle at all, and had no
/// scrollable ancestor to bring them back — so they could not be clicked and
/// nothing indicated they existed.
/// </para>
/// <para>
/// <c>AOS-R001-013</c> / <c>AOS-R001R-001</c>. Seventeen navigation destinations
/// do not fit the pane at any footer height. Scrolling always worked; the pane
/// simply never followed the selection, so working in one of the four
/// below-the-fold workspaces showed a list that did not contain the page being
/// worked on.
/// </para>
/// </remarks>
public sealed class CommandReachTests
{
    /// <summary>The contract commands live in something that can overflow.</summary>
    [Fact]
    public void TheContractCommandsCanOverflow()
    {
        XElement page = XElement.Load(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "ContractsPage.xaml"));

        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        XElement bar = Assert.Single(
            page.Descendants(),
            x => x.Attribute(name)?.Value == "DetailCommands");

        Assert.Equal("CommandBar", bar.Name.LocalName);

        foreach (string command in Commands)
        {
            Assert.Contains(bar.Descendants(), x => x.Attribute(name)?.Value == command);
        }
    }

    /// <summary>No contract command is left in a row that cannot overflow.</summary>
    /// <remarks>
    /// The defect was the container, not the buttons. A command moved back out of
    /// the bar would be unreachable again at the same size.
    /// </remarks>
    [Theory]
    [InlineData("NewButton")]
    [InlineData("VersionButton")]
    [InlineData("ReconcileButton")]
    [InlineData("SignatureButton")]
    [InlineData("NoticeButton")]
    public void EveryContractCommandIsInTheBar(string command)
    {
        XElement page = XElement.Load(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "ContractsPage.xaml"));

        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        XElement button = Assert.Single(
            page.Descendants(),
            x => x.Attribute(name)?.Value == command);

        Assert.Contains(
            button.Ancestors(),
            x => x.Attribute(name)?.Value == "DetailCommands");
    }

    /// <summary>
    /// The shell's selection handler calls <c>StartBringIntoView</c>.
    /// </summary>
    /// <remarks>
    /// One call, in the one place every selection passes through — including the
    /// keyboard accelerators, which select through the same property. This reads
    /// the shell's source and shows the call is there, after the handler begins; it
    /// does not show the pane scrolling, which needs a running window (F-07).
    /// </remarks>
    [Fact]
    public void TheSelectionHandlerAsksThePaneToBringTheItemIntoView()
    {
        string shell = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "MainWindow.xaml.cs"));

        Assert.Contains("StartBringIntoView()", shell, StringComparison.Ordinal);

        int follows = shell.IndexOf("StartBringIntoView()", StringComparison.Ordinal);
        int handler = shell.IndexOf("OnNavigationSelectionChanged", StringComparison.Ordinal);

        Assert.True(
            handler >= 0 && follows > handler,
            "The pane is brought into view somewhere other than where the selection changes.");
    }

    private static IEnumerable<string> Commands =>
        ["NewButton", "VersionButton", "ReconcileButton", "SignatureButton", "NoticeButton"];

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
