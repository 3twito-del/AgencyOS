using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using AgencyOS.Client.Commands;
using System.IO;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// Drives the running client and writes down what it saw.
/// </summary>
/// <remarks>
/// <para>
/// Observes and never repairs. Every method here reads, navigates, presses a key
/// the product itself declares, or takes a picture. Nothing writes to the product
/// or its database except through the client's own controls, and Run 001 does not
/// press a control that mutates anything.
/// </para>
/// <para>
/// Every negative result is recorded as a result rather than thrown. A surface
/// that cannot be reached is the single most interesting thing an audit can find,
/// and an exception would end the pass instead of reporting it.
/// </para>
/// </remarks>
internal sealed class AuditPass
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
    };

    private readonly ReviewApp _app;
    private readonly string _runDirectory;

    internal AuditPass(ReviewApp app, string runDirectory)
    {
        _app = app;
        _runDirectory = runDirectory;
    }

    /// <summary>Walks every workspace, capturing evidence for each.</summary>
    internal IReadOnlyList<SurfaceEvidence> WalkWorkspaces()
    {
        List<SurfaceEvidence> evidence = [];

        foreach (AgencyOsWorkspace workspace in AgencyOsWorkspaces.All)
        {
            evidence.Add(Visit(workspace));
        }

        return evidence;
    }

    private SurfaceEvidence Visit(AgencyOsWorkspace workspace)
    {
        string surfaceId = "workspace." + workspace.Tag;
        string directory = Path.Combine(_runDirectory, "evidence", surfaceId);

        Directory.CreateDirectory(directory);

        _app.Focus();

        ReviewStep navigated = _app.Navigate(workspace.Label);

        if (!navigated.Succeeded)
        {
            return new SurfaceEvidence(
                surfaceId, false, navigated.Detail, [], null, 0, 0, [], [], [], null, []);
        }

        // Content frames populate asynchronously; a capture taken immediately
        // photographs the previous page and would be evidence of nothing.
        Thread.Sleep(1200);
        _app.Refresh();

        List<string> screenshots = [];
        string shot = Path.Combine(directory, "01-loaded.png");

        if (_app.Capture(shot))
        {
            screenshots.Add(Relative(shot));
        }

        UiaNode tree = _app.Snapshot();
        string treePath = Path.Combine(directory, "ui-tree.json");

        File.WriteAllText(treePath, JsonSerializer.Serialize(tree, Json));

        UiaNode[] nodes = [.. tree.Flatten()];

        KeyboardPass? keyboard = RunKeyboardPass(workspace.Label);

        // The keyboard pass moves focus, which changes the picture. A second
        // capture after it shows where focus ended up, which is what the focus
        // findings cite.
        string focusShot = Path.Combine(directory, "02-after-keyboard.png");

        if (_app.Capture(focusShot))
        {
            screenshots.Add(Relative(focusShot));
        }

        return new SurfaceEvidence(
            surfaceId,
            true,
            navigated.Detail,
            screenshots,
            Relative(treePath),
            nodes.Length,
            nodes.Count(Detectors.Interactive),
            VisibleText(nodes),
            OpenNotices(nodes),
            Detectors.Accessibility(tree),
            keyboard,
            Clipped(nodes));
    }

    /// <summary>
    /// Presses every global gesture the registry declares and records where it went.
    /// </summary>
    /// <remarks>
    /// This is the one check that closes the M13 loop end to end. The registry
    /// proves the gesture is unique, the window installs it from the registry, and
    /// a unit test proves both — but nothing until now pressed the key on a running
    /// build and looked at which workspace the shell actually selected.
    /// </remarks>
    internal IReadOnlyList<GestureProbe> ProbeGestures()
    {
        List<GestureProbe> probes = [];

        foreach (CommandDefinition command in CommandRegistry.Default.GlobalGestures)
        {
            // Start somewhere known, so an unchanged selection is evidence that
            // nothing happened rather than that it was already there.
            _app.Focus();
            _app.Navigate("Command Center");
            Thread.Sleep(400);

            bool sent = SendGesture(command.Gesture);

            Thread.Sleep(900);
            _app.Refresh();

            string? selection = SelectedNavigationItem();

            string? expected = command.Action == CommandActionKind.Navigate
                ? AgencyOsWorkspaces.Find(command.Workspace!)?.Label
                : command.Workspace is { } workspace
                    ? AgencyOsWorkspaces.Find(workspace)?.Label
                    : null;

            bool matched = expected is null
                ? selection is not null
                : string.Equals(selection, expected, StringComparison.Ordinal);

            probes.Add(new GestureProbe(
                command.Id,
                command.Gesture.Display,
                expected,
                selection,
                sent,
                matched,
                GestureVerdict(sent, matched, expected, selection)));
        }

        return probes;
    }

    /// <summary>
    /// Runs the corrected detectors over every workspace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Audit 001R exists because three of Audit 001's five accessibility checks
    /// could never fire, so their zeroes across seventeen workspaces were not
    /// evidence of anything. This re-walks all seventeen with the corrected
    /// detectors and records how much was looked at, not only what was found — a
    /// count of problems with no denominator is how the original silence went
    /// unnoticed.
    /// </para>
    /// <para>
    /// It observes and repairs nothing. No control is invoked; the pass navigates,
    /// photographs, and reads the tree.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<AccessibilitySurvey> SurveyWorkspaces(int width, int height)
    {
        List<AccessibilitySurvey> surveys = [];

        _app.Focus();
        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(200);
        _app.Resize(width, height);
        Thread.Sleep(700);
        _app.Refresh();

        foreach (AgencyOsWorkspace workspace in AgencyOsWorkspaces.All)
        {
            string surfaceId = "workspace." + workspace.Tag;
            string directory = Path.Combine(_runDirectory, "evidence", surfaceId);

            Directory.CreateDirectory(directory);

            _app.Focus();

            ReviewStep navigated = _app.Navigate(workspace.Label);

            if (!navigated.Succeeded)
            {
                surveys.Add(new AccessibilitySurvey(
                    surfaceId, workspace.Label, false, navigated.Detail, null, null, 0, 0, [], []));

                continue;
            }

            // Content frames populate asynchronously; a tree read immediately
            // describes the previous page and would be evidence of nothing.
            Thread.Sleep(1200);
            _app.Refresh();

            string? screenshot = null;
            string shot = Path.Combine(directory, "01-loaded.png");

            if (_app.Capture(shot))
            {
                screenshot = Relative(shot);
            }

            UiaNode tree = _app.Snapshot();
            string treePath = Path.Combine(directory, "ui-tree.json");

            File.WriteAllText(treePath, JsonSerializer.Serialize(tree, Json));

            UiaNode[] reviewable = [.. Detectors.Reviewable(tree)];

            surveys.Add(new AccessibilitySurvey(
                surfaceId,
                workspace.Label,
                true,
                "Observed at " + _app.Size() + ".",
                screenshot,
                Relative(treePath),
                reviewable.Length,
                reviewable.Count(Detectors.Interactive),
                Detectors.Accessibility(tree),
                Detectors.RowSpeech(tree)));
        }

        return surveys;
    }

    /// <summary>Opens the two shell overlays and records what they do.</summary>
    internal IReadOnlyList<SurfaceEvidence> ProbeOverlays()
    {
        List<SurfaceEvidence> evidence =
        [
            ProbeOverlay(
                "overlay.command-palette",
                () => _app.Keys.PressGesture('P', ReviewModifiers.Control),
                "PaletteQuery"),
            ProbeOverlay(
                "overlay.global-search",
                () => _app.Keys.PressGesture('K', ReviewModifiers.Control),
                "SearchQuery"),
        ];

        return evidence;
    }

    private SurfaceEvidence ProbeOverlay(string surfaceId, Func<bool> open, string expectedFocusId)
    {
        string directory = Path.Combine(_runDirectory, "evidence", surfaceId);

        Directory.CreateDirectory(directory);

        _app.Focus();
        _app.Navigate("Command Center");
        Thread.Sleep(300);

        UiaNode? before = _app.FocusedNode();

        if (!open())
        {
            return new SurfaceEvidence(
                surfaceId, false, "The keystroke could not be delivered.", [], null, 0, 0, [], [], [], null, []);
        }

        Thread.Sleep(700);
        _app.Refresh();

        List<string> screenshots = [];
        string opened = Path.Combine(directory, "01-open.png");

        if (_app.Capture(opened))
        {
            screenshots.Add(Relative(opened));
        }

        UiaNode tree = _app.Snapshot();
        string treePath = Path.Combine(directory, "ui-tree.json");

        File.WriteAllText(treePath, JsonSerializer.Serialize(tree, Json));

        UiaNode[] nodes = [.. tree.Flatten()];
        UiaNode? focused = _app.FocusedNode();

        List<AccessibilityObservation> accessibility = [.. Detectors.Accessibility(tree)];

        if (focused?.AutomationId is not { } focusId
            || !string.Equals(focusId, expectedFocusId, StringComparison.Ordinal))
        {
            accessibility.Add(new AccessibilityObservation(
                "focus-not-moved-into-overlay",
                focused?.Describe() ?? "«nothing focused»",
                "Opening the overlay should put focus in " + expectedFocusId
                    + "; focus is on " + (focused?.Describe() ?? "nothing") + "."));
        }

        // Escape, then check focus came back somewhere sensible.
        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(600);
        _app.Refresh();

        UiaNode? after = _app.FocusedNode();

        string closed = Path.Combine(directory, "02-after-escape.png");

        if (_app.Capture(closed))
        {
            screenshots.Add(Relative(closed));
        }

        string detail = string.Create(
            CultureInfo.InvariantCulture,
            $"focus before: {before?.Describe() ?? "none"}; on open: {focused?.Describe() ?? "none"}; "
                + $"after Escape: {after?.Describe() ?? "none"}");

        return new SurfaceEvidence(
            surfaceId,
            true,
            detail,
            screenshots,
            Relative(treePath),
            nodes.Length,
            nodes.Count(Detectors.Interactive),
            VisibleText(nodes),
            OpenNotices(nodes),
            accessibility,
            null,
            Clipped(nodes));
    }

    /// <summary>Captures one surface at a smaller window, for the layout pass.</summary>
    /// <remarks>
    /// The window is returned to desktop size before navigating. Below the
    /// navigation breakpoint the pane compacts and its destinations leave the
    /// automation tree, so navigating while already narrow silently lands on
    /// whichever workspace was open and photographs the wrong page.
    /// </remarks>
    internal SurfaceEvidence ProbeNarrowWindow(string workspaceLabel, int width, int height)
    {
        string surfaceId = "layout.narrow." + workspaceLabel.Replace(' ', '-').ToLowerInvariant();
        string directory = Path.Combine(_runDirectory, "evidence", surfaceId);

        Directory.CreateDirectory(directory);

        Settle(workspaceLabel);
        _app.Resize(width, height);
        Thread.Sleep(900);
        _app.Refresh();

        List<string> screenshots = [];
        string shot = Path.Combine(directory, "01-narrow.png");

        if (_app.Capture(shot))
        {
            screenshots.Add(Relative(shot));
        }

        UiaNode tree = _app.Snapshot();
        string treePath = Path.Combine(directory, "ui-tree.json");

        File.WriteAllText(treePath, JsonSerializer.Serialize(tree, Json));

        UiaNode[] nodes = [.. tree.Flatten()];

        return new SurfaceEvidence(
            surfaceId,
            true,
            "Window resized to " + width.ToString(CultureInfo.InvariantCulture) + "x"
                + height.ToString(CultureInfo.InvariantCulture) + "; actual " + _app.Size() + ".",
            screenshots,
            Relative(treePath),
            nodes.Length,
            nodes.Count(Detectors.Interactive),
            VisibleText(nodes),
            OpenNotices(nodes),
            [],
            null,
            Clipped(nodes));
    }

    /// <summary>
    /// Measures one workspace at one window size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repair Wave 002 has to settle two claims about rectangles: that the detail
    /// pane leaves the window at 900x700 (<c>AOS-R001-007</c>), and that the
    /// connection footer takes the space the destination list needs
    /// (<c>AOS-R001-013</c>). Both are measured here rather than judged from a
    /// picture, because a screenshot shows what a layout looked like and a
    /// rectangle says whether somebody could reach the control.
    /// </para>
    /// <para>
    /// The screenshot is still written. It is what a person reads when the numbers
    /// say something surprising.
    /// </para>
    /// </remarks>
    internal LayoutProbe ProbeLayout(string workspaceLabel, int width, int height)
    {
        string size = width.ToString(CultureInfo.InvariantCulture) + "x"
            + height.ToString(CultureInfo.InvariantCulture);
        string surfaceId = "layout." + size + "."
            + workspaceLabel.Replace(' ', '-').ToLowerInvariant();
        string directory = Path.Combine(_runDirectory, "evidence", surfaceId);

        Directory.CreateDirectory(directory);

        Settle(workspaceLabel);
        _app.Resize(width, height);

        // Layout below a breakpoint settles in more than one pass: the pane
        // collapses, the content reflows, and a capture taken too early shows an
        // intermediate state that never appeared on anybody's screen.
        Thread.Sleep(1400);
        _app.Refresh();

        string? screenshot = null;
        string shot = Path.Combine(directory, "01-layout.png");

        if (_app.Capture(shot))
        {
            screenshot = Relative(shot);
        }

        UiaNode tree = _app.Snapshot();
        string treePath = Path.Combine(directory, "ui-tree.json");

        File.WriteAllText(treePath, JsonSerializer.Serialize(tree, Json));

        UiaNode[] nodes = [.. tree.Flatten()];
        Rectangle? window = Rectangle.Parse(tree.Bounds);

        Rectangle? footer = Rectangle.Union(nodes
            .Where(x => x.AutomationId is "SyncText" or "ConnectionText" or "BuildText")
            .Select(x => Rectangle.Parse(x.Bounds)));

        UiaNode? paneScroller = nodes
            .FirstOrDefault(x => x.AutomationId == "MenuItemsScrollViewer" && !x.IsOffscreen);

        Rectangle? paneRegion = Rectangle.Parse(paneScroller?.Bounds);

        List<DestinationPlacement> destinations = [];

        foreach (AgencyOsWorkspace workspace in AgencyOsWorkspaces.All)
        {
            UiaNode? item = nodes.FirstOrDefault(x =>
                x.ControlType == "ListItem"
                && string.Equals(x.Name, workspace.Label, StringComparison.Ordinal));

            Rectangle? bounds = Rectangle.Parse(item?.Bounds);

            destinations.Add(new DestinationPlacement(
                workspace.Label,
                item?.Bounds,
                item?.IsOffscreen ?? true,
                bounds is not null && paneRegion is not null
                    && paneRegion.Value.Contains(bounds.Value),
                string.Equals(workspace.Label, workspaceLabel, StringComparison.Ordinal),
                bounds is not null && footer is not null
                    ? (int)Rectangle.VerticalOverlap(bounds.Value, footer.Value)
                    : 0));
        }

        IReadOnlyList<ControlReachability> controls = Reachability(nodes, window);
        bool paneExpanded = destinations.Any(x => x.Bounds is not null);

        return new LayoutProbe(
            surfaceId,
            workspaceLabel,
            size,
            _app.Size(),
            tree.Bounds,
            screenshot,
            Relative(treePath),
            paneExpanded,
            paneScroller?.Bounds,
            footer?.ToString(),
            footer is null ? 0 : (int)footer.Value.Height,
            destinations,
            ContentScrolls(nodes),
            [.. controls
                .Where(x => x.Verdict == ReachabilityVerdict.ClippedUnreachable)
                .Select(x => x.Control)],
            nodes.Count(Detectors.Interactive),
            controls,
            Worst(controls));
    }

    /// <summary>
    /// The worst thing that happened to any action on the surface.
    /// </summary>
    /// <remarks>
    /// A surface is only as reachable as its least reachable action. Averaging or
    /// counting would let one unreachable button disappear into forty that were
    /// fine, which is the failure mode the whole pass is here to avoid.
    /// </remarks>
    private static string Worst(IReadOnlyList<ControlReachability> controls)
    {
        if (controls.Count == 0)
        {
            return ReachabilityVerdict.Inconclusive;
        }

        foreach (string verdict in (string[])
        [
            ReachabilityVerdict.ClippedUnreachable,
            ReachabilityVerdict.Inconclusive,
            ReachabilityVerdict.ScrollReachable,
        ])
        {
            if (controls.Any(x => x.Verdict == verdict))
            {
                return verdict;
            }
        }

        return ReachabilityVerdict.Reachable;
    }

    /// <summary>
    /// Whether every declared destination can actually be opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The measurement <c>AOS-R001-013</c> turns on. Four of seventeen
    /// destinations sit below the fold of the navigation pane at every size the
    /// shell expands it at, and the finding is about whether that makes them
    /// unreachable or merely undiscoverable. Those are different defects with
    /// different repairs, and a screenshot cannot tell them apart.
    /// </para>
    /// <para>
    /// So each destination is selected, and the pass records whether the page
    /// changed and whether the pane brought the destination onto the window. A
    /// destination that opens and scrolls itself into view is discoverable-by-use
    /// even when it is not visible at rest.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<DestinationReachability> ProbeDestinations(int width, int height)
    {
        List<DestinationReachability> verdicts = [];

        _app.Focus();
        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(200);
        _app.Resize(width, height);
        Thread.Sleep(900);
        _app.Refresh();

        foreach (AgencyOsWorkspace workspace in AgencyOsWorkspaces.All)
        {
            UiaNode opening = _app.Snapshot();
            Rectangle? window = Rectangle.Parse(opening.Bounds);

            UiaNode? before = Item(opening, workspace.Label);

            ReviewStep navigated = _app.Navigate(workspace.Label);

            // Generously, because the question is whether the pane ever brings the
            // destination into view, not whether it does so within some deadline.
            Thread.Sleep(1500);
            _app.Refresh();

            bool selected = navigated.Succeeded
                && string.Equals(SelectedNavigationItem(), workspace.Label, StringComparison.Ordinal);

            UiaNode? after = Item(_app.Snapshot(), workspace.Label);
            bool broughtIntoView = OnWindow(after, window);

            // Selecting a destination is how a user gets there; asking it to scroll
            // itself is how the pass tells "the pane does not follow the selection"
            // apart from "the destination cannot be shown at all". The first is a
            // discoverability complaint, the second is a defect.
            if (!broughtIntoView && selected)
            {
                AskToScrollIntoView(workspace.Label);
                _app.Refresh();

                after = Item(_app.Snapshot(), workspace.Label);
                broughtIntoView = OnWindow(after, window);
            }

            verdicts.Add(new DestinationReachability(
                workspace.Label,
                Verdict(before, selected, broughtIntoView, navigated.Succeeded),
                before?.Bounds,
                after?.Bounds,
                selected,
                broughtIntoView));
        }

        return verdicts;
    }

    private static UiaNode? Item(UiaNode tree, string label) =>
        tree.Flatten().FirstOrDefault(x =>
            x.ControlType == "ListItem"
            && string.Equals(x.Name, label, StringComparison.Ordinal));

    private static bool OnWindow(UiaNode? node, Rectangle? window) =>
        node is { IsOffscreen: false }
        && window is { } viewport
        && Rectangle.Parse(node.Bounds) is { } bounds
        && viewport.Intersects(bounds);

    private static string Verdict(
        UiaNode? before, bool selected, bool broughtIntoView, bool reached)
    {
        if (!reached)
        {
            // The pane is compact and its destinations live in a flyout this pass
            // does not open. That is the shell's own answer to a small window, not
            // a destination anybody has lost.
            return before is null
                ? ReachabilityVerdict.CompactedByDesign
                : ReachabilityVerdict.Inconclusive;
        }

        if (!selected)
        {
            return ReachabilityVerdict.Inconclusive;
        }

        if (before is { IsOffscreen: false } && before.Bounds is not null)
        {
            return ReachabilityVerdict.Reachable;
        }

        return broughtIntoView
            ? ReachabilityVerdict.ScrollReachable
            : ReachabilityVerdict.ClippedUnreachable;
    }

    /// <summary>
    /// Whether the content host can be scrolled sideways rather than clipping.
    /// </summary>
    /// <remarks>
    /// The scroll bar is off screen whenever there is nothing to scroll to, so its
    /// presence in the tree - not its visibility - is what says the content is
    /// reachable at all.
    /// </remarks>
    private static bool ContentScrolls(IReadOnlyList<UiaNode> nodes) =>
        nodes.Any(x => x.ControlType == "ScrollBar" && x.AutomationId == "HorizontalScrollBar");

    /// <summary>
    /// Named actions a user cannot get to at this size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Off screen" and "cannot be reached" are different claims, and
    /// <c>AOS-R001-007</c> is about the second. A control scrolled out of a
    /// viewport reports no rectangle and is off screen, and a user reaches it by
    /// scrolling; a control laid out past the window edge reports a rectangle
    /// that is not on the window, and no amount of scrolling helps.
    /// </para>
    /// <para>
    /// So a candidate is asked to scroll itself into view before it is reported.
    /// Anything that comes back onto the window was reachable, which is the
    /// question the finding actually asks. This is the only place the layout pass
    /// touches the running application, and scrolling is not a mutation.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Every named action on the surface, and whether a user could get to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A candidate that is not on the window is asked to scroll itself into view
    /// before any verdict is recorded, because "off screen" and "cannot be
    /// reached" are different claims and only the second is a defect. This is the
    /// only place the layout pass touches the running application, and scrolling
    /// is not a mutation.
    /// </para>
    /// <para>
    /// Controls already on the window are recorded too. A pass that only lists
    /// problems cannot say how much it looked at, and that is exactly the
    /// ambiguity this whole rebaseline exists to remove.
    /// </para>
    /// </remarks>
    private IReadOnlyList<ControlReachability> Reachability(
        IReadOnlyList<UiaNode> nodes, Rectangle? window)
    {
        List<ControlReachability> verdicts = [];

        if (window is not { } viewport)
        {
            return verdicts;
        }

        foreach (UiaNode node in nodes
            .Where(Detectors.Operable)
            .DistinctBy(x => x.Describe(), StringComparer.Ordinal)
            .OrderBy(x => x.Describe(), StringComparer.Ordinal))
        {
            Rectangle? bounds = Rectangle.Parse(node.Bounds);

            if (bounds is { } shown && viewport.Intersects(shown) && !node.IsOffscreen)
            {
                verdicts.Add(new ControlReachability(
                    node.Describe(), node.Bounds, ReachabilityVerdict.Reachable, false, null));

                continue;
            }

            (bool scrollable, Rectangle? arrived) = AskToScrollIntoView(node.Name!);

            verdicts.Add(new ControlReachability(
                node.Describe(),
                node.Bounds,
                Detectors.Reachability(bounds, node.IsOffscreen, viewport, scrollable, arrived),
                scrollable,
                arrived?.ToString()));
        }

        return verdicts;
    }

    /// <summary>Asks a control to bring itself into view, and says where it landed.</summary>
    private (bool Scrollable, Rectangle? Arrived) AskToScrollIntoView(string name)
    {
        AutomationElement? element = _app.FindByName(name);

        if (element is null)
        {
            return (false, null);
        }

        bool scrollable = false;

        try
        {
            if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out object? pattern)
                && pattern is ScrollItemPattern scroller)
            {
                scrollable = true;
                scroller.ScrollIntoView();

                // Long enough for the scroll to finish. A rectangle read mid-scroll
                // measures a control part-way past the viewport edge - the first
                // pass recorded a 48-pixel button as two pixels tall - and a
                // measurement of an animation is not a measurement of a layout.
                Thread.Sleep(900);
            }

            Rect rectangle = element.Current.BoundingRectangle;

            if (rectangle.IsEmpty || double.IsInfinity(rectangle.Width))
            {
                return (scrollable, null);
            }

            return (scrollable, new Rectangle(
                rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height));
        }
        catch (ElementNotAvailableException)
        {
            return (scrollable, null);
        }
        catch (InvalidOperationException)
        {
            // A control that refuses to scroll itself has not been shown to be
            // reachable, which is the answer this method exists to give.
            return (scrollable, null);
        }
    }


    /// <summary>
    /// Puts the window somewhere known and opens the workspace under test.
    /// </summary>
    /// <remarks>
    /// Escape first, because an overlay left open by an earlier probe photographs
    /// itself instead of the page. Desktop size second, because the navigation
    /// pane's destinations are not in the automation tree while it is compact, so
    /// navigating from a narrow window silently does nothing.
    /// </remarks>
    private void Settle(string workspaceLabel)
    {
        _app.Focus();
        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(200);
        _app.Resize(1600, 1000);
        Thread.Sleep(600);
        _app.Refresh();

        ReviewStep navigated = _app.Navigate(workspaceLabel);

        if (!navigated.Succeeded)
        {
            throw new InvalidOperationException(
                "The layout pass could not open '" + workspaceLabel + "': " + navigated.Detail);
        }

        Thread.Sleep(700);
    }

    /// <summary>
    /// What pressing one declared gesture amounted to.
    /// </summary>
    /// <remarks>
    /// Every pass starts from Command Center, so an unchanged selection is
    /// evidence that nothing happened rather than that it was already there. The
    /// distinction between a no-op and a wrong target matters: one is a command
    /// that did not run, the other is a command that ran and went somewhere else.
    /// </remarks>
    private static string GestureVerdict(
        bool sent, bool matched, string? expected, string? selection)
    {
        if (!sent)
        {
            return "BLOCKED_BY_STATE";
        }

        // A command with no declared destination cannot be judged by which
        // workspace is selected afterwards, and calling it landed because
        // something was selected would be counting a question this pass never
        // asked. Ctrl+K and F5 are the two; the overlay probe judges Ctrl+K
        // properly, by looking for the overlay.
        if (expected is null)
        {
            return "CONTEXT_DEPENDENT";
        }

        if (matched)
        {
            return "LANDED";
        }

        return string.Equals(selection, "Command Center", StringComparison.Ordinal)
            ? "NO_OP"
            : "WRONG_TARGET";
    }

    private bool SendGesture(CommandGesture gesture)
    {
        ReviewModifiers modifiers = ReviewModifiers.None;

        if (gesture.Modifiers.HasFlag(CommandModifiers.Control))
        {
            modifiers |= ReviewModifiers.Control;
        }

        if (gesture.Modifiers.HasFlag(CommandModifiers.Shift))
        {
            modifiers |= ReviewModifiers.Shift;
        }

        if (gesture.Modifiers.HasFlag(CommandModifiers.Alt))
        {
            modifiers |= ReviewModifiers.Alt;
        }

        return gesture.Key switch
        {
            >= CommandKey.F1 and <= CommandKey.F12 =>
                _app.Keys.PressFunction(gesture.Key - CommandKey.F1 + 1, modifiers),
            >= CommandKey.D0 and <= CommandKey.D9 =>
                _app.Keys.PressGesture((char)('0' + (gesture.Key - CommandKey.D0)), modifiers),
            >= CommandKey.A and <= CommandKey.Z =>
                _app.Keys.PressGesture((char)('A' + (gesture.Key - CommandKey.A)), modifiers),
            _ => false,
        };
    }

    private string? SelectedNavigationItem()
    {
        try
        {
            AutomationElement? list = _app.Window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "NavigationView"));

            AutomationElement root = list ?? _app.Window;

            AutomationElementCollection candidates = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsSelectionItemPatternAvailableProperty, true));

            foreach (AutomationElement? candidate in candidates)
            {
                if (candidate is null)
                {
                    continue;
                }

                if (candidate.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                    && pattern is SelectionItemPattern item
                    && item.Current.IsSelected)
                {
                    string name = candidate.Current.Name ?? string.Empty;

                    if (AgencyOsWorkspaces.All.Any(x =>
                        string.Equals(x.Label, name, StringComparison.Ordinal)))
                    {
                        return name;
                    }
                }
            }

            return null;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tabs through a surface and records where focus went.
    /// </summary>
    /// <remarks>
    /// Bounded at forty stops. The question is whether the keyboard can traverse
    /// the surface and get out again, not whether it can visit every row of a
    /// virtualized list, and an unbounded walk on a dense workspace would never
    /// finish.
    /// </remarks>
    private KeyboardPass? RunKeyboardPass(string workspaceLabel)
    {
        if (!_app.Focus())
        {
            return null;
        }

        // Start from the navigation item so the traversal begins at a known place
        // on every workspace and the orders are comparable.
        AutomationElement? start = _app.FindByName(workspaceLabel);

        try
        {
            start?.SetFocus();
        }
        catch (InvalidOperationException)
        {
            // Some containers refuse programmatic focus; the pass still runs from
            // wherever focus is, and the first stop records where that was.
        }

        Thread.Sleep(250);

        List<FocusStop> stops = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        string? previous = null;
        int repeats = 0;

        for (int i = 1; i <= 40; i++)
        {
            if (!_app.Keys.Press(ReviewKey.Tab))
            {
                break;
            }

            UiaNode? focused = _app.FocusedNode();

            if (focused is null)
            {
                stops.Add(new FocusStop(i, "«no focused element»", null, false));
                continue;
            }

            string description = focused.Describe();

            stops.Add(new FocusStop(i, description, focused.Bounds, focused.IsOffscreen));
            seen.Add(description);

            if (string.Equals(description, previous, StringComparison.Ordinal))
            {
                repeats++;
            }
            else
            {
                repeats = 0;
            }

            previous = description;

            // Focus that has not moved in four presses is not traversing.
            if (repeats >= 3)
            {
                return new KeyboardPass(
                    stops,
                    seen.Count,
                    true,
                    stops.Count(x => x.Offscreen),
                    stops.Count(x => x.Control.Contains("«unnamed»", StringComparison.Ordinal)));
            }
        }

        return new KeyboardPass(
            stops,
            seen.Count,
            false,
            stops.Count(x => x.Offscreen),
            stops.Count(x => x.Control.Contains("«unnamed»", StringComparison.Ordinal)));
    }

    private static IReadOnlyList<string> Clipped(IReadOnlyList<UiaNode> nodes) =>
    [
        .. nodes
            .Where(x => x.IsOffscreen && Detectors.Operable(x))
            .Select(x => x.Describe())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(40),
    ];

    private static IReadOnlyList<string> VisibleText(IReadOnlyList<UiaNode> nodes) =>
    [
        .. nodes
            .Where(x => !x.IsOffscreen && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => x.Name!)
            .Where(x => x.Length is > 1 and < 200)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private static IReadOnlyList<string> OpenNotices(IReadOnlyList<UiaNode> nodes) =>
    [
        .. nodes
            .Where(x => !x.IsOffscreen
                && (x.ClassName?.Contains("InfoBar", StringComparison.Ordinal) == true
                    || x.ControlType == "ProgressBar"))
            .Select(x => x.ControlType + ": " + (x.Name ?? x.ClassName ?? "?"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private string Relative(string path) =>
        Path.GetRelativePath(_runDirectory, path).Replace('\\', '/');
}
