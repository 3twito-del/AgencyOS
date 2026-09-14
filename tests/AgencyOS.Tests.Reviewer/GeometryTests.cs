using AgencyOS.Reviewer.Runtime;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// The arithmetic the layout pass reasons with, and the names it reasons about.
/// </summary>
/// <remarks>
/// <para>
/// Repair Wave 002 settles two findings by measuring rectangles rather than by
/// looking at screenshots, which only works if the measuring is right. It also
/// found that it had not been: the harness trimmed a UI Automation pattern name
/// with the wrong suffix, so every question of the form "does this control
/// support Invoke" answered no, and three accessibility checks reported zero
/// problems because they could not run at all.
/// </para>
/// <para>
/// A check that cannot fire is worse than no check, because its zero is read as
/// an answer. These tests are here so that particular silence cannot return.
/// </para>
/// </remarks>
public sealed class GeometryTests
{
    /// <summary>
    /// A pattern is known by the name the harness asks for it by.
    /// </summary>
    /// <remarks>
    /// The exact strings UI Automation reports, so the test fails if the trimming
    /// is changed to something that happens to look plausible.
    /// </remarks>
    [Theory]
    [InlineData("InvokePatternIdentifiers.Pattern", "Invoke")]
    [InlineData("TogglePatternIdentifiers.Pattern", "Toggle")]
    [InlineData("ValuePatternIdentifiers.Pattern", "Value")]
    [InlineData("SelectionItemPatternIdentifiers.Pattern", "SelectionItem")]
    [InlineData("ExpandCollapsePatternIdentifiers.Pattern", "ExpandCollapse")]
    public void APatternIsNamedTheWayTheHarnessAsksForIt(string reported, string expected) =>
        Assert.Equal(expected, UiaTree.ShortName(reported));

    /// <summary>An unfamiliar name is passed through rather than mangled.</summary>
    [Fact]
    public void AnUnfamiliarPatternNameSurvives() =>
        Assert.Equal("Something", UiaTree.ShortName("Something"));

    /// <summary>A stored rectangle reads back as the rectangle that was stored.</summary>
    [Fact]
    public void ARectangleReadsBackAsItself()
    {
        Rectangle? parsed = Rectangle.Parse("59,891,318,24");

        Assert.NotNull(parsed);
        Assert.Equal(59, parsed.Value.Left);
        Assert.Equal(891, parsed.Value.Top);
        Assert.Equal(377, parsed.Value.Right);
        Assert.Equal(915, parsed.Value.Bottom);
        Assert.Equal("59,891,318,24", parsed.Value.ToString());
    }

    /// <summary>A control with no rectangle is not a rectangle at the origin.</summary>
    /// <remarks>
    /// A scrolled-out control reports no bounds, and reading that as 0,0,0,0 would
    /// place it in the top-left corner of the screen — inside every window, and so
    /// counted as reachable.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("59,891,318")]
    [InlineData("left,top,width,height")]
    public void AnUnreadableRectangleIsNoRectangle(string? bounds) =>
        Assert.Null(Rectangle.Parse(bounds));

    /// <summary>The footer's band is the band its three captions cover together.</summary>
    [Fact]
    public void AUnionCoversEveryRectangleInIt()
    {
        Rectangle? union = Rectangle.Union(
        [
            Rectangle.Parse("71,927,294,24"),
            Rectangle.Parse("71,954,294,24"),
            Rectangle.Parse("71,981,294,24"),
        ]);

        Assert.NotNull(union);
        Assert.Equal(927, union.Value.Top);
        Assert.Equal(1005, union.Value.Bottom);
        Assert.Equal(78, union.Value.Height);
    }

    /// <summary>Nothing at all unions to nothing at all.</summary>
    [Fact]
    public void AUnionOfNothingIsNothing() =>
        Assert.Null(Rectangle.Union([null, null]));

    /// <summary>
    /// The measurement that decides AOS-R001-013.
    /// </summary>
    /// <remarks>
    /// The real numbers from both runs. Before the repair the footer stood 156
    /// pixels tall and the last destination was clipped to 21 pixels of its 54;
    /// after it, the footer is 78 and the destination is whole. Neither state has
    /// the footer's rectangle over the destination's — which is why this is
    /// measured rather than asserted from the screenshot, and why the finding's
    /// own wording about an overlap needed correcting.
    /// </remarks>
    [Fact]
    public void AFooterOverThePaneIsMeasuredInPixels()
    {
        Rectangle destination = Rectangle.Parse("59,891,318,54")!.Value;
        Rectangle footer = Rectangle.Parse("71,927,294,78")!.Value;

        Assert.Equal(18, Rectangle.VerticalOverlap(destination, footer));

        // A footer that begins below the destination covers none of it.
        Assert.Equal(0, Rectangle.VerticalOverlap(
            Rectangle.Parse("59,816,318,21")!.Value,
            Rectangle.Parse("71,849,294,156")!.Value));
    }

    /// <summary>Two things in different columns do not cover each other.</summary>
    /// <remarks>
    /// The pane and the content share every row of the window and overlap in none
    /// of them. Comparing tops and bottoms alone would report the whole shell as
    /// occluded.
    /// </remarks>
    [Fact]
    public void ColumnsThatShareRowsDoNotCoverEachOther() =>
        Assert.Equal(0, Rectangle.VerticalOverlap(
            Rectangle.Parse("59,153,318,762")!.Value,
            Rectangle.Parse("400,153,1200,762")!.Value));

    /// <summary>
    /// The measurement that decides AOS-R001-007.
    /// </summary>
    /// <remarks>
    /// A control laid out past the window's right edge still reports a rectangle,
    /// and that rectangle is simply not on the window. This is the shape of the
    /// detail pane at 900x700 before the repair.
    /// </remarks>
    [Fact]
    public void AControlPastTheWindowEdgeIsNotOnTheWindow()
    {
        Rectangle window = Rectangle.Parse("41,41,900,701")!.Value;

        Assert.False(window.Intersects(Rectangle.Parse("1000,300,240,60")!.Value));
        Assert.True(window.Intersects(Rectangle.Parse("800,300,240,60")!.Value));

        // Touching the edge is not being on the window.
        Assert.False(window.Intersects(Rectangle.Parse("941,300,240,60")!.Value));
    }

    /// <summary>A destination counts as visible only when all of it is.</summary>
    /// <remarks>
    /// The pre-repair pane ended mid-row: 21 pixels of a 54-pixel destination, its
    /// label cut in half. Counting that as visible is how "eleven destinations are
    /// shown" becomes "twelve".
    /// </remarks>
    [Fact]
    public void APartlyShownDestinationIsNotShown()
    {
        Rectangle region = Rectangle.Parse("53,153,330,684")!.Value;

        Assert.True(region.Contains(Rectangle.Parse("59,756,318,54")!.Value));
        Assert.False(region.Contains(Rectangle.Parse("59,816,318,54")!.Value));
    }
}
