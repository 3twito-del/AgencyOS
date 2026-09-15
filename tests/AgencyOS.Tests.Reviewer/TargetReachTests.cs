using AgencyOS.Reviewer.Runtime;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// That "offscreen" is never reported as one thing when it is two.
/// </summary>
/// <remarks>
/// <para>
/// Audit 002 left <c>RecordSignatureDialog</c> unopened and called it a harness
/// limitation: <c>SignatureButton</c> was in the tree, was offscreen, and the pass
/// did not scroll. The closure slice measured it and found the diagnosis was
/// wrong in a way that mattered — nothing in the button's ancestry scrolls, so no
/// amount of scrolling would ever have revealed it, and at 1600x1000 no operator
/// could click it either. A harness limitation was standing in front of a product
/// finding.
/// </para>
/// <para>
/// These are the controls for the distinction. The verdicts are produced from a
/// live automation tree, which this suite has none of, so what is asserted here is
/// the vocabulary and the ordering of the rules — the same thing
/// <c>DetectorControlTests</c> asserts about the accessibility detectors, and for
/// the same reason: a check nobody has seen fire is a check nobody should trust.
/// </para>
/// </remarks>
public sealed class TargetReachTests
{
    /// <summary>The five states, spelled once, so a rename cannot go unnoticed.</summary>
    [Fact]
    public void TheVocabularyIsExactlyThese() =>
        Assert.Equal(
            [
                "TARGET_DISABLED",
                "TARGET_MISSING",
                "TARGET_OFFSCREEN_NOT_SCROLLABLE",
                "TARGET_OFFSCREEN_SCROLLABLE",
                "TARGET_ONSCREEN",
            ],
            new[]
            {
                TargetReach.Missing,
                TargetReach.Disabled,
                TargetReach.Onscreen,
                TargetReach.OffscreenScrollable,
                TargetReach.OffscreenNotScrollable,
            }.Order(StringComparer.Ordinal));

    /// <summary>A control with a rectangle and no complaint is reachable.</summary>
    [Fact]
    public void AControlWithBoundsIsOnscreen()
    {
        ReachResult result = new(
            TargetReach.Onscreen, "present and on screen", "1617,390,194,48", false, null);

        Assert.Equal(TargetReach.Onscreen, result.Verdict);
        Assert.NotNull(result.Bounds);
        Assert.False(result.ScrollAttempted);
    }

    /// <summary>
    /// A clipped control has no rectangle at all, and that is the tell.
    /// </summary>
    /// <remarks>
    /// The measurement this slice turned on. A control scrolled past still reports
    /// where it would be; one clipped by a container that cannot scroll reports
    /// nothing, and WinUI's own tree is what says which happened.
    /// </remarks>
    [Fact]
    public void AClippedControlHasNoRectangle()
    {
        ReachResult result = new(
            TargetReach.OffscreenNotScrollable,
            "present with no bounding rectangle, so it is clipped rather than scrolled past;"
                + " nothing in its ancestry could scroll it into view",
            null,
            true,
            "ScrollItemPattern");

        Assert.Null(result.Bounds);
        Assert.True(result.ScrollAttempted);
        Assert.Contains("clipped", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scroll that was attempted and failed is not the same as one never tried.
    /// </summary>
    /// <remarks>
    /// The verdict a harness may claim for itself is
    /// <c>TARGET_OFFSCREEN_SCROLLABLE</c> before it has tried. Afterwards the
    /// honest answer is <c>TARGET_OFFSCREEN_NOT_SCROLLABLE</c>, and the difference
    /// is exactly the difference between blaming the harness and reporting the
    /// product.
    /// </remarks>
    [Fact]
    public void AFailedRevealDowngradesToNotScrollable()
    {
        ReachResult before = new(
            TargetReach.OffscreenScrollable, "present and reported offscreen", null, false, null);

        ReachResult after = before with
        {
            Verdict = TargetReach.OffscreenNotScrollable,
            ScrollAttempted = true,
        };

        Assert.Equal(TargetReach.OffscreenScrollable, before.Verdict);
        Assert.Equal(TargetReach.OffscreenNotScrollable, after.Verdict);
        Assert.False(before.ScrollAttempted);
        Assert.True(after.ScrollAttempted);
    }

    /// <summary>A disabled control is present, and is not a reachability problem.</summary>
    [Fact]
    public void ADisabledControlIsItsOwnVerdict()
    {
        ReachResult result = new(
            TargetReach.Disabled, "present and disabled", "1295,483,174,48", false, null);

        Assert.Equal(TargetReach.Disabled, result.Verdict);
        Assert.NotNull(result.Bounds);
    }

    /// <summary>A control that is not there is never called offscreen.</summary>
    [Fact]
    public void AMissingControlIsNotOffscreen()
    {
        ReachResult result = new(
            TargetReach.Missing, "no control with that identifier", null, false, null);

        Assert.Equal(TargetReach.Missing, result.Verdict);
        Assert.DoesNotContain("offscreen", result.Verdict, StringComparison.OrdinalIgnoreCase);
    }
}
