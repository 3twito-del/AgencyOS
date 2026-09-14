using System.Xml.Linq;
using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Windows.Layout;

/// <summary>
/// That the shell still behaves when the window is small.
/// </summary>
/// <remarks>
/// <para>
/// Two findings, both about rectangles. At 900x700 the navigation pane stayed at
/// its full width and the detail pane — every action on the surface — was outside
/// the window with nothing to say it was missing (<c>AOS-R001-007</c>). And the
/// connection footer wrapped to 180 pixels of the pane, so the destination list
/// it shared the pane with showed eleven of seventeen destinations
/// (<c>AOS-R001-013</c>).
/// </para>
/// <para>
/// The authoritative evidence for both is measured at run time by the review
/// harness, which resizes a real window and reads the rectangles back. This is the
/// structural half: it pins the four markup decisions that produced those
/// rectangles, so a later edit that undoes one fails here rather than at the next
/// audit. It cannot prove the layout is usable, and does not claim to.
/// </para>
/// </remarks>
public sealed class ShellLayoutTests
{
    /// <summary>
    /// The navigation pane is allowed to collapse.
    /// </summary>
    /// <remarks>
    /// <c>Left</c> pins the pane at every size. On a 900x700 window that spent a
    /// third of the width on navigation and pushed the detail pane off screen.
    /// <c>Auto</c> is the adaptive behaviour NavigationView was built with.
    /// </remarks>
    [Fact]
    public void TheNavigationPaneIsNotPinnedAtEverySize()
    {
        XElement navigation = Element("NavigationView");

        Assert.NotEqual("Left", navigation.Attribute("PaneDisplayMode")?.Value);
    }

    /// <summary>
    /// The page can be scrolled to when it does not fit.
    /// </summary>
    /// <remarks>
    /// Both axes. The detail pane left the window sideways at 900x700, and Sync's
    /// "Discard selected change" left it downwards at 1280x720.
    /// </remarks>
    [Fact]
    public void ThePageCanBeScrolledToWhenItDoesNotFit()
    {
        XElement host = Shell()
            .Descendants()
            .Single(x => x.Name.LocalName == "ScrollViewer"
                && x.Elements().Any(child => child.Name.LocalName == "Frame"));

        Assert.NotEqual("Disabled", host.Attribute("HorizontalScrollMode")?.Value);
        Assert.NotEqual("Disabled", host.Attribute("VerticalScrollMode")?.Value);

        // The page is given the host's size rather than being left to take the
        // unlimited width a ScrollViewer offers. Without this a star-sized column
        // stretches past the window at every size, which is how the first attempt
        // at this repair pushed Command Center's actions off a 1600x1000 desktop.
        Assert.NotNull(host.Attribute("SizeChanged"));
    }

    /// <summary>
    /// The connection footer stays one predictable height.
    /// </summary>
    /// <remarks>
    /// Three wrapping captions holding a URL and a tenant identifier grew to 180
    /// pixels, and the pane gives the destination list whatever the footer leaves.
    /// Trimming instead of wrapping halved it, and the full text is still on the
    /// element for anybody who hovers.
    /// </remarks>
    [Fact]
    public void TheConnectionFooterDoesNotGrowIntoTheDestinationList()
    {
        XElement footer = Element("NavigationView.PaneFooter");

        List<XElement> captions =
        [
            .. footer.Descendants().Where(x => x.Name.LocalName == "TextBlock"),
        ];

        Assert.NotEmpty(captions);

        foreach (XElement caption in captions)
        {
            string name = caption.Attribute(Xaml + "Name")?.Value ?? "(anonymous)";

            Assert.True(
                caption.Attribute("TextWrapping")?.Value == "NoWrap",
                $"The footer caption '{name}' wraps, so the footer's height depends on "
                    + "how long a server address happens to be.");

            Assert.True(
                caption.Attribute("TextTrimming") is not null,
                $"The footer caption '{name}' would be cut off with no indication.");
        }
    }

    /// <summary>
    /// The footer is opaque.
    /// </summary>
    /// <remarks>
    /// The destination list scrolls underneath it. A transparent footer shows the
    /// rows passing behind the text, which is what made the audit describe the
    /// footer as drawing over the selected destination.
    /// </remarks>
    [Fact]
    public void TheConnectionFooterIsOpaque()
    {
        XElement panel = Element("NavigationView.PaneFooter").Elements().Single();

        Assert.NotNull(panel.Attribute("Background"));
    }

    /// <summary>
    /// A page fills the shell, and stops shrinking at the floor.
    /// </summary>
    /// <remarks>
    /// The rule the shell applies on every resize, tested where it lives rather
    /// than through a window.
    /// </remarks>
    [Theory]
    [InlineData(1100, 640, 1100)]
    [InlineData(640, 640, 640)]
    [InlineData(520, 640, 640)]
    public void APageFillsTheShellUntilTheShellIsTooSmall(
        double available, double minimum, double expected) =>
        Assert.Equal(expected, ContentExtent.For(available, minimum));

    /// <summary>Before the first layout pass the page decides for itself.</summary>
    /// <remarks>
    /// A zero-width host is a host that has not been measured. Laying a page out
    /// at the floor because of it would make every page briefly scrollable on
    /// startup.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnmeasuredShellLeavesThePageToItself(double available) =>
        Assert.True(double.IsNaN(ContentExtent.For(available, 640)));

    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Element(string localName) =>
        Shell().DescendantsAndSelf().Single(x => x.Name.LocalName == localName);

    private static XElement Shell() =>
        XElement.Load(Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "MainWindow.xaml"));

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
