using System.Windows.Automation;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// Whether a named control can actually be operated, and why not when it cannot.
/// </summary>
/// <remarks>
/// <para>
/// Audit 002 left <c>RecordSignatureDialog</c> unopened and called it a harness
/// limitation: the button existed in the automation tree and was offscreen, and
/// the pass did not bring it into view. That was the right classification and an
/// incomplete diagnosis — "offscreen" has more than one cause, and only one of
/// them is the harness's fault.
/// </para>
/// <para>
/// A control clipped inside something that scrolls is reachable, and a pass that
/// does not scroll is failing to look. A control clipped by a container that
/// cannot scroll is not reachable by anybody, and reporting that as a harness
/// limitation hides a product problem behind the harness's own shortcoming. This
/// separates them and names which one it found.
/// </para>
/// </remarks>
/// <param name="Verdict">One of the five states below.</param>
/// <param name="Detail">What the tree showed.</param>
/// <param name="Bounds">The control's screen rectangle, when it has one.</param>
/// <param name="ScrollAttempted">Whether a scroll was tried.</param>
/// <param name="ScrollPattern">Which pattern was used, when one was.</param>
public sealed record ReachResult(
    string Verdict,
    string Detail,
    string? Bounds,
    bool ScrollAttempted,
    string? ScrollPattern);

/// <summary>
/// Brings one named control into view, deterministically or not at all.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a traversal engine. It takes a control the caller already
/// knows the name of, asks the tree where it is, and — only when something in its
/// ancestry can actually scroll — asks that thing to show it. It discovers
/// nothing, walks nothing, and scrolls nothing it was not pointed at.
/// </para>
/// <para>
/// Readiness is read from the tree rather than waited for. The verdict after a
/// scroll is the tree's own answer to "does this control have a rectangle now",
/// which is a fact, not an interval.
/// </para>
/// </remarks>
internal sealed class TargetReach
{
    /// <summary>The control is not in the tree at all.</summary>
    internal const string Missing = "TARGET_MISSING";

    /// <summary>Present, and refusing input.</summary>
    internal const string Disabled = "TARGET_DISABLED";

    /// <summary>Present, enabled, and on screen.</summary>
    internal const string Onscreen = "TARGET_ONSCREEN";

    /// <summary>Clipped, but something in its ancestry can scroll it into view.</summary>
    internal const string OffscreenScrollable = "TARGET_OFFSCREEN_SCROLLABLE";

    /// <summary>Clipped, and nothing can scroll it. Nobody can reach this.</summary>
    internal const string OffscreenNotScrollable = "TARGET_OFFSCREEN_NOT_SCROLLABLE";

    private readonly ReviewApp _app;

    internal TargetReach(ReviewApp app) => _app = app;

    /// <summary>Classifies a named control without touching it.</summary>
    /// <param name="automationId">The control's automation identifier.</param>
    /// <returns>What the tree says about it.</returns>
    internal ReachResult Inspect(string automationId)
    {
        AutomationElement? element = _app.Find(automationId);

        if (element is null)
        {
            return new ReachResult(Missing, "no control with that identifier", null, false, null);
        }

        return Classify(element, scrolled: false, pattern: null);
    }

    /// <summary>
    /// Brings a named control into view when something can, and says what happened.
    /// </summary>
    /// <param name="automationId">The control's automation identifier.</param>
    /// <returns>The verdict after the attempt.</returns>
    internal ReachResult Reveal(string automationId)
    {
        AutomationElement? element = _app.Find(automationId);

        if (element is null)
        {
            return new ReachResult(Missing, "no control with that identifier", null, false, null);
        }

        ReachResult before = Classify(element, scrolled: false, pattern: null);

        if (before.Verdict is Onscreen or Disabled)
        {
            return before;
        }

        // The control's own pattern first. ScrollItem exists precisely for this and
        // asks the nearest scrolling container to show this item, which is both
        // narrower and more reliable than computing an offset.
        if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out object? item)
            && item is ScrollItemPattern scrollItem)
        {
            try
            {
                scrollItem.ScrollIntoView();
                Thread.Sleep(350);
                _app.Refresh();

                AutomationElement? after = _app.Find(automationId);

                if (after is not null)
                {
                    return Classify(after, scrolled: true, pattern: nameof(ScrollItemPattern));
                }
            }
            catch (InvalidOperationException)
            {
                // The container declined. Falls through to the ancestor attempt,
                // and to the honest verdict if that declines too.
            }
            catch (ElementNotAvailableException)
            {
                return new ReachResult(
                    Missing, "the control left the tree while being revealed", null, true, null);
            }
        }

        if (ScrollableAncestor(element) is { } scrollable)
        {
            try
            {
                scrollable.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, 100d);
                Thread.Sleep(350);
                _app.Refresh();

                AutomationElement? after = _app.Find(automationId);

                if (after is not null)
                {
                    return Classify(after, scrolled: true, pattern: nameof(ScrollPattern));
                }
            }
            catch (InvalidOperationException)
            {
                // Same again: a container that says it scrolls and then will not.
            }
            catch (ElementNotAvailableException)
            {
                return new ReachResult(
                    Missing, "the control left the tree while being revealed", null, true, null);
            }
        }

        return before with
        {
            Verdict = OffscreenNotScrollable,
            Detail = before.Detail + "; nothing in its ancestry could scroll it into view",
            ScrollAttempted = true,
        };
    }

    /// <summary>
    /// The nearest ancestor that both claims to scroll and is able to.
    /// </summary>
    /// <remarks>
    /// <c>HorizontallyScrollable</c> and <c>VerticallyScrollable</c> are the
    /// container's own statement that there is something out of view. A
    /// <c>ScrollViewer</c> whose content fits reports neither, and asking it to
    /// scroll would do nothing while looking like it had.
    /// </remarks>
    private static ScrollPattern? ScrollableAncestor(AutomationElement element)
    {
        try
        {
            TreeWalker walker = TreeWalker.ControlViewWalker;

            for (AutomationElement? current = walker.GetParent(element);
                current is not null;
                current = walker.GetParent(current))
            {
                if (!current.TryGetCurrentPattern(ScrollPattern.Pattern, out object? pattern)
                    || pattern is not ScrollPattern scroll)
                {
                    continue;
                }

                if (scroll.Current.HorizontallyScrollable || scroll.Current.VerticallyScrollable)
                {
                    return scroll;
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// The verdict for one element as the tree currently reports it.
    /// </summary>
    /// <remarks>
    /// A clipped control reports <c>IsOffscreen</c>, and WinUI gives it no bounding
    /// rectangle at all when it is clipped rather than merely scrolled past. Both
    /// are treated as not on screen; the rectangle is recorded because its absence
    /// is itself evidence about which of the two happened.
    /// </remarks>
    private static ReachResult Classify(AutomationElement element, bool scrolled, string? pattern)
    {
        try
        {
            AutomationElement.AutomationElementInformation current = element.Current;

            System.Windows.Rect rect = current.BoundingRectangle;

            string? bounds = rect.IsEmpty || double.IsInfinity(rect.Width)
                ? null
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{rect.Left:F0},{rect.Top:F0},{rect.Width:F0},{rect.Height:F0}");

            if (!current.IsEnabled)
            {
                return new ReachResult(
                    Disabled, "present and disabled", bounds, scrolled, pattern);
            }

            if (current.IsOffscreen || bounds is null)
            {
                return new ReachResult(
                    scrolled ? OffscreenNotScrollable : OffscreenScrollable,
                    bounds is null
                        ? "present with no bounding rectangle, so it is clipped rather than scrolled past"
                        : "present and reported offscreen",
                    bounds,
                    scrolled,
                    pattern);
            }

            return new ReachResult(Onscreen, "present and on screen", bounds, scrolled, pattern);
        }
        catch (ElementNotAvailableException)
        {
            return new ReachResult(Missing, "the control left the tree", null, scrolled, pattern);
        }
    }
}
