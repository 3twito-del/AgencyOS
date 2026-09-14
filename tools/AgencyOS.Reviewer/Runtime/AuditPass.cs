using System.Globalization;
using System.Text.Json;
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
            nodes.Count(Interactive),
            VisibleText(nodes),
            OpenNotices(nodes),
            Accessibility(nodes),
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
                matched));
        }

        return probes;
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

        List<AccessibilityObservation> accessibility = [.. Accessibility(nodes)];

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
            nodes.Count(Interactive),
            VisibleText(nodes),
            OpenNotices(nodes),
            accessibility,
            null,
            Clipped(nodes));
    }

    /// <summary>Captures one surface at a smaller window, for the layout pass.</summary>
    internal SurfaceEvidence ProbeNarrowWindow(string workspaceLabel, int width, int height)
    {
        string surfaceId = "layout.narrow." + workspaceLabel.Replace(' ', '-').ToLowerInvariant();
        string directory = Path.Combine(_runDirectory, "evidence", surfaceId);

        Directory.CreateDirectory(directory);

        _app.Focus();
        _app.Navigate(workspaceLabel);
        Thread.Sleep(500);
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
            nodes.Count(Interactive),
            VisibleText(nodes),
            OpenNotices(nodes),
            [],
            null,
            Clipped(nodes));
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

    /// <summary>
    /// Accessibility problems visible in a running automation tree.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than the static XAML suite, and complementary to it.
    /// The static suite proves the markup declares a name; this proves the running
    /// control exposes one, which is a different claim - a template, a converter or
    /// a runtime-populated item can lose it.
    /// </remarks>
    private static IReadOnlyList<AccessibilityObservation> Accessibility(IReadOnlyList<UiaNode> nodes)
    {
        List<AccessibilityObservation> observations = [];

        foreach (UiaNode node in nodes)
        {
            bool actionable = node.Patterns.Any(x =>
                x is "Invoke" or "Toggle" or "ExpandCollapse" or "SelectionItem" or "Value");

            bool named = !string.IsNullOrWhiteSpace(node.Name);

            if (actionable && !named && node.IsEnabled && !node.IsOffscreen)
            {
                observations.Add(new AccessibilityObservation(
                    "actionable-control-without-accessible-name",
                    node.Describe(),
                    "Supports " + string.Join('/', node.Patterns)
                        + " and exposes no name, so a screen reader announces only its type."));
            }

            if (actionable && !node.IsKeyboardFocusable && node.IsEnabled && !node.IsOffscreen
                && node.ControlType is not ("ListItem" or "DataItem" or "TreeItem" or "MenuItem"))
            {
                observations.Add(new AccessibilityObservation(
                    "actionable-control-not-focusable",
                    node.Describe(),
                    "Can be invoked and cannot be reached by keyboard."));
            }

            if (node.ControlType == "Text"
                && node.IsKeyboardFocusable
                && node.Patterns.Count == 0)
            {
                observations.Add(new AccessibilityObservation(
                    "decorative-element-is-focusable",
                    node.Describe(),
                    "A text element with no pattern takes a Tab stop."));
            }

            if (node.ControlType is "Edit" or "ComboBox" && !named && !node.IsOffscreen)
            {
                observations.Add(new AccessibilityObservation(
                    "input-without-label",
                    node.Describe(),
                    "An input with no accessible name cannot be described to its user."));
            }
        }

        // Two controls that announce identically on one surface are two controls a
        // screen-reader user cannot tell apart.
        foreach (IGrouping<string, UiaNode> group in nodes
            .Where(x => !string.IsNullOrWhiteSpace(x.Name)
                && x.Patterns.Contains("Invoke")
                && !x.IsOffscreen)
            .GroupBy(x => x.Name!, StringComparer.Ordinal)
            .Where(x => x.Count() > 1))
        {
            observations.Add(new AccessibilityObservation(
                "duplicate-accessible-name",
                group.Key,
                group.Count().ToString(CultureInfo.InvariantCulture)
                    + " invokable controls on this surface announce the same name."));
        }

        return observations;
    }

    private static IReadOnlyList<string> Clipped(IReadOnlyList<UiaNode> nodes) =>
    [
        .. nodes
            .Where(x => x.IsOffscreen && x.IsEnabled && !string.IsNullOrWhiteSpace(x.Name)
                && x.Patterns.Any(p => p is "Invoke" or "Value" or "Toggle"))
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

    private static bool Interactive(UiaNode node) =>
        node.IsKeyboardFocusable
        || node.Patterns.Any(x => x is "Invoke" or "Toggle" or "Value" or "SelectionItem" or "ExpandCollapse");

    private string Relative(string path) =>
        Path.GetRelativePath(_runDirectory, path).Replace('\\', '/');
}
