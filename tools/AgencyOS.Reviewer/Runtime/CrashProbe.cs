using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using AgencyOS.Reviewer.Surface;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>One step of a crash probe and whether the client survived it.</summary>
/// <param name="Step">The step as written in the case.</param>
/// <param name="Detail">What happened.</param>
/// <param name="Alive">Whether the client process was still running afterwards.</param>
/// <param name="ElapsedSeconds">Seconds since the client was launched.</param>
public sealed record CrashProbeStep(string Step, string Detail, bool Alive, double ElapsedSeconds);

/// <summary>What one opener did to the process that ran it.</summary>
/// <param name="DialogId">The dialog the opener should bring up.</param>
/// <param name="Workspace">Where the opener lives.</param>
/// <param name="ProcessId">The client process, for event-log correlation.</param>
/// <param name="LaunchedUtc">When that process was started.</param>
/// <param name="WindowSize">The window size the probe ran at.</param>
/// <param name="Verdict">
/// <c>CRASH_REPRODUCED</c>, <c>EXITED_BEFORE_OPENER</c>, <c>DIALOG_OPENED</c>,
/// <c>DID_NOT_APPEAR</c> or <c>NOT_INVOKED</c>.
/// </param>
/// <param name="CrashedDuring">The step during or after which the process was gone.</param>
/// <param name="ExitCode">The exit code in hexadecimal, when the process ended.</param>
/// <param name="ExitedUtc">When it ended.</param>
/// <param name="DialogTitle">The dialog that appeared, when one did.</param>
/// <param name="FocusBefore">Focus immediately before the opener.</param>
/// <param name="FocusAfter">Focus once the dialog was showing.</param>
/// <param name="FocusAfterClose">Focus once the dialog was dismissed.</param>
/// <param name="Steps">Every step, in order.</param>
/// <param name="Screenshots">Evidence, relative to the run directory.</param>
/// <param name="Trees">Automation snapshots, relative to the run directory.</param>
/// <param name="Inspection">
/// The dialog pass's own observation of the opened dialog, when the source
/// inventory was available to identify it.
/// </param>
public sealed record CrashProbeResult(
    string DialogId,
    string Workspace,
    int ProcessId,
    DateTime LaunchedUtc,
    string WindowSize,
    string Verdict,
    string? CrashedDuring,
    string? ExitCode,
    DateTime? ExitedUtc,
    string? DialogTitle,
    string? FocusBefore,
    string? FocusAfter,
    string? FocusAfterClose,
    IReadOnlyList<CrashProbeStep> Steps,
    IReadOnlyList<string> Screenshots,
    IReadOnlyList<string> Trees,
    DialogObservation? Inspection);

/// <summary>
/// Walks to one opener a step at a time and watches whether the client survives.
/// </summary>
/// <remarks>
/// <para>
/// Reproduction support for <c>AOS-R002-022</c>, added by Repair Wave 003A.1 and
/// scoped to it. The dialog pass selects a row in every list and then presses the
/// opener, and captures nothing until afterwards, so when the process went away it
/// could not say whether the opener or a row selection had taken it down. This
/// takes an explicit path, checks the process after every step, photographs the
/// window immediately before the opener, and records the exit code.
/// </para>
/// <para>
/// It observes the crash. It does not catch, retry or recover from one: a probe
/// that restarted the client and carried on would be hiding the thing it exists to
/// record.
/// </para>
/// </remarks>
internal sealed class CrashProbe
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ReviewApp _app;
    private readonly string _runDirectory;
    private readonly Stopwatch _clock;
    private readonly Func<string, DialogRecord?> _records;

    internal CrashProbe(
        ReviewApp app, string runDirectory, Stopwatch clock, Func<string, DialogRecord?> records)
    {
        _app = app;
        _runDirectory = runDirectory;
        _clock = clock;
        _records = records;
    }

    /// <summary>Follows the path and invokes the opener.</summary>
    /// <param name="dialogId">The dialog expected.</param>
    /// <param name="workspace">The workspace to start from.</param>
    /// <param name="path">
    /// Steps: <c>tab:Name</c>; <c>row:ListId</c>, <c>row:ListId:index</c> or
    /// <c>pick:ListId:name prefix</c>; <c>open:ButtonId</c> or
    /// <c>open:palette=command.id</c>, which hands the dialog to the dialog pass;
    /// <c>openkeep:ButtonId</c>, which leaves it open; <c>click:ButtonId</c>;
    /// <c>key:Escape</c> or <c>key:Alt+Down</c>; <c>focus:ControlId</c>;
    /// <c>type:text</c>; <c>read:ControlId</c>; <c>wait:milliseconds</c>;
    /// <c>shot:name</c>; <c>scroll:ListId</c>.
    /// </param>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    /// <param name="watch">How long to watch the process after the opener.</param>
    /// <param name="launchedUtc">When the client was started.</param>
    /// <returns>What happened to the process.</returns>
    internal CrashProbeResult Run(
        string dialogId,
        string workspace,
        IReadOnlyList<string> path,
        int width,
        int height,
        TimeSpan watch,
        DateTime launchedUtc)
    {
        string directory = Path.Combine(_runDirectory, "evidence", "crash." + dialogId);

        Directory.CreateDirectory(directory);

        List<CrashProbeStep> steps = [];
        List<string> shots = [];
        List<string> trees = [];

        string? focusBefore = null;
        string? focusAfter = null;
        string? focusAfterClose = null;
        string? title = null;
        DialogObservation? inspection = null;
        bool invoked = false;
        bool opened = false;
        string? lastStep = null;

        _app.Focus();
        _app.Resize(width, height);
        Thread.Sleep(500);
        _app.Refresh();

        IEnumerable<string> all = ["nav:" + workspace, .. path];

        foreach (string step in all)
        {
            lastStep = step;

            string detail;

            try
            {
                if (step.StartsWith("open:", StringComparison.Ordinal)
                    || step.StartsWith("openkeep:", StringComparison.Ordinal))
                {
                    bool keep = step.StartsWith("openkeep:", StringComparison.Ordinal);
                    UiaNode? before = _app.FocusedNode();

                    focusBefore = before?.Describe();

                    Evidence(directory, "01-before", shots, trees);

                    string opener = step[(step.IndexOf(':', StringComparison.Ordinal) + 1)..];

                    (invoked, detail) = opener.StartsWith("palette=", StringComparison.Ordinal)
                        ? new OpenerProbe(_app, _runDirectory)
                            .RunFromPalette(opener["palette=".Length..], directory)
                        : Press(opener);

                    if (invoked)
                    {
                        (opened, title, detail) = WatchOpener(dialogId, watch, detail, steps, step);

                        if (opened && _app.IsRunning && keep)
                        {
                            focusAfter = _app.FocusedNode()?.Describe();

                            Evidence(directory, "02-open", shots, trees);

                            detail += "; left open";
                        }
                        else if (opened && _app.IsRunning && _records(dialogId) is { } record)
                        {
                            focusAfter = _app.FocusedNode()?.Describe();
                            inspection = new DialogPass(_app, _runDirectory)
                                .Describe(record, before, "BUTTON", detail);
                            focusAfterClose = inspection.FocusLandedOn;

                            detail += "; inspected by the dialog pass: " + inspection.Outcome
                                + ", focus-in=" + inspection.FocusEnteredDialog
                                + ", tab-escapes=" + inspection.FocusEscapedDialog
                                + ", shift-tab-escapes=" + inspection.ReverseTabEscaped
                                + ", escape-closed=" + inspection.ClosedOnEscape
                                + ", focus-after-close=" + inspection.FocusAfterClose
                                + ", accessibility=" + inspection.Accessibility.Count
                                    .ToString(CultureInfo.InvariantCulture);
                        }
                        else if (opened && _app.IsRunning)
                        {
                            focusAfter = _app.FocusedNode()?.Describe();

                            Evidence(directory, "02-open", shots, trees);

                            _app.Keys.Press(ReviewKey.Escape);
                            Thread.Sleep(1200);

                            if (_app.IsRunning)
                            {
                                _app.Refresh();
                                focusAfterClose = _app.FocusedNode()?.Describe();

                                Evidence(directory, "03-closed", shots, trees);
                            }

                            detail += "; Escape pressed";
                        }
                        else if (!opened && _app.IsRunning)
                        {
                            Evidence(directory, "02-after", shots, trees);
                        }
                    }
                }
                else if (step.StartsWith("shot:", StringComparison.Ordinal))
                {
                    Evidence(directory, step["shot:".Length..], shots, trees);
                    detail = "captured";
                }
                else
                {
                    detail = Walk(step);
                    Thread.Sleep(step.StartsWith("key:", StringComparison.Ordinal)
                        || step.StartsWith("type:", StringComparison.Ordinal) ? 600 : 1300);

                    if (_app.IsRunning)
                    {
                        _app.Refresh();
                    }
                }
            }
            catch (Exception failure) when (failure is ElementNotAvailableException
                or COMException or InvalidOperationException)
            {
                detail = "automation failed: " + failure.GetType().Name + ": " + failure.Message;
            }

            bool alive = _app.IsRunning;

            steps.Add(new CrashProbeStep(step, detail, alive, Elapsed()));

            if (!alive)
            {
                break;
            }
        }

        (int Code, DateTime ExitedUtc)? exit = _app.Exit;

        string verdict = Verdict(exit is not null, invoked, opened);

        return new CrashProbeResult(
            dialogId,
            workspace,
            _app.ProcessId,
            launchedUtc,
            width.ToString(CultureInfo.InvariantCulture) + "x"
                + height.ToString(CultureInfo.InvariantCulture),
            verdict,
            exit is not null ? lastStep : null,
            exit is { } ended
                ? "0x" + ended.Code.ToString("X8", CultureInfo.InvariantCulture)
                : null,
            exit?.ExitedUtc,
            title,
            focusBefore,
            focusAfter,
            focusAfterClose,
            steps,
            shots,
            trees,
            inspection);
    }

    /// <summary>What the run amounted to.</summary>
    /// <param name="exited">Whether the client process was gone at the end.</param>
    /// <param name="invoked">Whether the opener was invoked.</param>
    /// <param name="opened">Whether a dialog was seen and the process outlived the watch.</param>
    /// <returns>The verdict.</returns>
    /// <remarks>
    /// An exit outranks everything. A dialog that appeared and was followed by the
    /// process going away is a crash, not an opened dialog; and an exit before the
    /// opener ran is its own verdict, because that is the case the dialog pass could
    /// not tell apart from a dialog crash and the reason this probe exists.
    /// </remarks>
    internal static string Verdict(bool exited, bool invoked, bool opened) =>
        exited
            ? invoked ? "CRASH_REPRODUCED" : "EXITED_BEFORE_OPENER"
            : opened ? "DIALOG_OPENED"
            : invoked ? "DID_NOT_APPEAR"
            : "NOT_INVOKED";

    /// <summary>
    /// Watches the process after the opener, second by second, for the whole window.
    /// </summary>
    /// <remarks>
    /// A dialog appearing is not the end of it. A layout failure can take the
    /// process down after the first frame, so the watch continues after the dialog
    /// is seen and the dialog is only reported as opened if the process outlived
    /// the window.
    /// </remarks>
    private (bool Opened, string? Title, string Detail) WatchOpener(
        string dialogId,
        TimeSpan watch,
        string invokedDetail,
        List<CrashProbeStep> steps,
        string step)
    {
        Stopwatch watched = Stopwatch.StartNew();
        string? title = null;
        double? seenAt = null;

        while (watched.Elapsed < watch)
        {
            Thread.Sleep(250);

            if (!_app.IsRunning)
            {
                return (false, title, invokedDetail
                    + "; the process was gone "
                    + watched.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)
                    + " s after the opener"
                    + (seenAt is { } at
                        ? ", " + (watched.Elapsed.TotalSeconds - at).ToString("0.00", CultureInfo.InvariantCulture)
                            + " s after the dialog was seen"
                        : ", and no dialog had been seen"));
            }

            if (title is null && Modal() is { } found)
            {
                title = found;
                seenAt = watched.Elapsed.TotalSeconds;

                steps.Add(new CrashProbeStep(
                    step + " (watch)",
                    "a dialog titled '" + found + "' is in the tree, expecting " + dialogId,
                    true,
                    Elapsed()));
            }
        }

        return title is not null
            ? (true, title, invokedDetail + "; the dialog was seen after "
                + seenAt!.Value.ToString("0.00", CultureInfo.InvariantCulture)
                + " s and the process was alive for the whole "
                + watch.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s watch")
            : (false, null, invokedDetail + "; the process stayed alive and no dialog appeared");
    }

    /// <summary>The title of an open content dialog, when there is one.</summary>
    private string? Modal()
    {
        try
        {
            AutomationElement? popup = _app.Window.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                    new PropertyCondition(AutomationElement.ClassNameProperty, "Popup")));

            return popup is null || popup.Current.IsOffscreen ? null : popup.Current.Name;
        }
        catch (Exception failure) when (failure is ElementNotAvailableException
            or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private (bool Invoked, string Detail) Press(string automationId)
    {
        AutomationElement? button = _app.Find(automationId);

        if (button is null)
        {
            return (false, "no control with automation id '" + automationId + "'");
        }

        if (!button.Current.IsEnabled)
        {
            return (false, "'" + automationId + "' is disabled");
        }

        if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern)
            || pattern is not InvokePattern invoke)
        {
            return (false, "'" + automationId + "' exposes no Invoke pattern");
        }

        invoke.Invoke();

        return (true, "invoked '" + automationId + "' ('" + button.Current.Name + "')");
    }

    private string Walk(string step)
    {
        string[] parts = step.Split(':');

        switch (parts[0])
        {
            case "nav":
                return _app.Navigate(parts[1]).Detail;

            case "wait":
                Thread.Sleep(int.Parse(parts[1], CultureInfo.InvariantCulture));
                return "waited";

            case "tab":
                return SelectTab(parts[1]);

            case "scroll":
                return Scroll(parts[1]);

            case "click":
                return Press(parts[1]).Detail;

            case "key":
                return PressKey(parts[1]);

            case "focus":
                return FocusOn(parts[1]);

            case "type":
                return _app.Keys.Type(step["type:".Length..])
                    ? "typed '" + step["type:".Length..] + "'"
                    : "the keystrokes were refused";

            case "read":
                return Read(parts[1]);

            case "see":
                return See(step["see:".Length..]);

            case "describedby":
                return DescribedBy(parts[1]);

            case "set":
                return Set(step["set:".Length..]);

            // Every name inside a container, for lists a reader can reach and a
            // harness cannot select. Reality Closure could not verify the term
            // rows live for exactly this reason: both term lists are
            // SelectionMode="None", and read: reports an announced name through
            // the selection pattern. The names are what a screen reader is given,
            // so this asks for them directly.
            case "names":
                return Names(parts[1]);

            // Where focus is, without moving it. A refusal that the operator has to
            // go looking for is most of what AOS-R002-010 was about.
            case "focused":
                return "focus is on " + (_app.FocusedNode()?.Describe() ?? "nothing");

            case "palette":
                return OpenPalette(step["palette:".Length..]);

            case "row":
                return SelectRow(
                    parts[1],
                    parts.Length > 2 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0,
                    null);

            case "pick":
                return SelectRow(parts[1], 0, parts[2]);

            default:
                return "unknown step";
        }
    }

    /// <summary>Opens the command palette and types a query, leaving it open.</summary>
    /// <remarks>
    /// Repair Wave 003D needs to read what a palette row announces
    /// (<c>AOS-R002-018</c>), which means the palette open and populated rather
    /// than a command already run. The gesture is the product's own: Ctrl+P.
    /// </remarks>
    private string OpenPalette(string query)
    {
        _app.Focus();
        Thread.Sleep(300);

        if (!_app.Keys.PressGesture('P', ReviewModifiers.Control))
        {
            return "the palette keystroke was refused";
        }

        Thread.Sleep(900);
        _app.Refresh();

        if (_app.Snapshot().Flatten().All(x => x.AutomationId != "PaletteQuery"))
        {
            return "the palette did not open";
        }

        if (query.Length > 0 && !_app.Keys.Type(query))
        {
            return "the query could not be typed";
        }

        Thread.Sleep(900);
        _app.Refresh();

        return "the palette is open on '" + query + "'";
    }

    private string PressKey(string chord)
    {
        string[] parts = chord.Split('+');
        ReviewModifiers modifiers = ReviewModifiers.None;

        foreach (string modifier in parts[..^1])
        {
            modifiers |= Enum.Parse<ReviewModifiers>(modifier);
        }

        return _app.Keys.Press(Enum.Parse<ReviewKey>(parts[^1]), modifiers)
            ? "pressed " + chord
            : "the keystroke " + chord + " was refused";
    }

    private string FocusOn(string automationId)
    {
        AutomationElement? element = _app.Find(automationId);

        if (element is null)
        {
            return "no control with automation id '" + automationId + "'";
        }

        element.SetFocus();
        Thread.Sleep(300);

        return "focused '" + automationId + "'; focus is on "
            + (_app.FocusedNode()?.Describe() ?? "nothing");
    }

    /// <summary>
    /// Replaces what a control holds, as <c>id=value</c>.
    /// </summary>
    /// <remarks>
    /// Correcting an entry means replacing it, and the keyboard vocabulary here has
    /// no select-all. Setting the value through the pattern is also what an operator
    /// editing the field does as far as the product is concerned: it raises the same
    /// change notification, which is what retires the refusal.
    /// </remarks>
    private string Set(string assignment)
    {
        int split = assignment.IndexOf('=', StringComparison.Ordinal);

        if (split <= 0)
        {
            return "a set step reads 'set:automationId=value'";
        }

        string automationId = assignment[..split];
        string value = assignment[(split + 1)..];

        AutomationElement? element = _app.Find(automationId);

        if (element is null)
        {
            return "no control with automation id '" + automationId + "'";
        }

        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern)
            || pattern is not ValuePattern editable)
        {
            return "'" + automationId + "' holds no value";
        }

        editable.SetValue(value);
        Thread.Sleep(400);

        return "'" + automationId + "' is now '" + value + "'";
    }

    /// <summary>
    /// What a control says describes it, read from UI Automation itself.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R002-011</c> is about a relationship, not about text being visible, so
    /// the only evidence worth having is the relationship as an assistive technology
    /// would read it. <c>DescribedBy</c> is property 30105; the managed client has no
    /// name for it, so it is looked up by id.
    /// </remarks>
    private string DescribedBy(string automationId)
    {
        AutomationElement? element = _app.Find(automationId);

        if (element is null)
        {
            return "no control with automation id '" + automationId + "'";
        }

        AutomationProperty property = AutomationProperty.LookupById(30105);

        if (property is null)
        {
            // Said precisely, because "cannot read it" and "it is not set" are very
            // different findings. System.Windows.Automation never registered
            // DescribedBy; LabeledBy, two ids away, proves the lookup itself works.
            object? labelled = element.GetCurrentPropertyValue(AutomationElement.LabeledByProperty);

            return "this UI Automation client has no DescribedBy property (30105), so the "
                + "association cannot be read from here; the client does expose LabeledBy, "
                + "which reads as "
                + (labelled is AutomationElement label ? "'" + label.Current.Name + "'" : "nothing")
                + ". Whether the description is spoken is a screen-reader gate, not this one.";
        }

        object? value = element.GetCurrentPropertyValue(property);

        if (value is not object[] { Length: > 0 } described)
        {
            return "'" + automationId + "' is described by nothing";
        }

        IEnumerable<string> said = described
            .OfType<AutomationElement>()
            .Select(x => Text(x));

        return "'" + automationId + "' is described by [" + string.Join(" | ", said) + "]";
    }

    /// <summary>Everything an element says, itself and beneath it.</summary>
    private static string Text(AutomationElement element)
    {
        string[] beneath = element
            .FindAll(TreeScope.Descendants, new PropertyCondition(
                AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>()
            .Select(x => x.Current.Name)
            .Where(x => x is { Length: > 0 })
            .ToArray();

        return beneath.Length > 0
            ? string.Join(" — ", beneath)
            : element.Current.Name;
    }

    /// <summary>
    /// Whether something named is actually on the window, and where.
    /// </summary>
    /// <remarks>
    /// By name rather than by automation id, because the navigation pane's
    /// destinations carry neither an id nor an x:Name, and adding one to measure
    /// them would be changing the product to suit the harness. Reports what
    /// <c>AOS-R001-013</c> and <c>AOS-R002-021</c> were measured with: whether
    /// the element is offscreen, and its rectangle.
    /// </remarks>
    /// <summary>What every row inside a container announces.</summary>
    /// <remarks>
    /// Names only, deduplicated and in tree order. A list whose rows all announce
    /// the same words shows one entry here, which is the defect
    /// <c>10-ACCESSIBILITY-PARITY.md</c> recorded for document versions and
    /// representation scopes.
    /// </remarks>
    private string Names(string automationId)
    {
        _app.Refresh();

        UiaNode? container = _app.Snapshot().Flatten()
            .FirstOrDefault(x => string.Equals(x.AutomationId, automationId, StringComparison.Ordinal));

        if (container is null)
        {
            return "no control with automation id '" + automationId + "'";
        }

        List<string> names = [];

        foreach (UiaNode node in container.Flatten())
        {
            if (node.Name is { Length: > 0 } name
                && !string.Equals(name, container.Name, StringComparison.Ordinal)
                && !names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        return names.Count == 0
            ? "'" + automationId + "' contains nothing that announces a name"
            : "announces=[" + string.Join(" § ", names) + "]";
    }

    private string See(string name)
    {
        _app.Refresh();

        UiaNode? found = _app.Snapshot().Flatten()
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));

        if (found is null)
        {
            return "nothing named '" + name + "' is in the tree";
        }

        return "'" + name + "' is " + (found.IsOffscreen ? "OFFSCREEN" : "on screen")
            + " at " + (found.Bounds ?? "«no rectangle»");
    }

    /// <summary>What a control shows: its value, or the item it has selected.</summary>
    private string Read(string automationId)
    {
        AutomationElement? element = _app.Find(automationId);

        if (element is null)
        {
            return "no control with automation id '" + automationId + "'";
        }

        List<string> said = ["name='" + element.Current.Name + "'"];

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? value)
            && value is ValuePattern text)
        {
            said.Add("value='" + text.Current.Value + "'");
        }

        if (element.TryGetCurrentPattern(SelectionPattern.Pattern, out object? selection)
            && selection is SelectionPattern chosen)
        {
            said.Add("selected=[" + string.Join(", ", chosen.Current.GetSelection()
                .Select(x => "'" + x.Current.Name + "'")) + "]");
        }

        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? expand)
            && expand is ExpandCollapsePattern state)
        {
            said.Add("state=" + state.Current.ExpandCollapseState);
        }

        // An InfoBar announces nothing itself: its heading and its sentence are
        // separate children, and reading only the bar would have said the
        // refusal was blank when it was not (AOS-R002-012).
        string[] beneath = element
            .FindAll(TreeScope.Descendants, new PropertyCondition(
                AutomationElement.ControlTypeProperty, ControlType.Text))
            .Cast<AutomationElement>()
            .Select(x => x.Current.Name)
            .Where(x => x is { Length: > 0 })
            .ToArray();

        if (beneath.Length > 0)
        {
            said.Add("says=[" + string.Join(" | ", beneath) + "]");
        }

        return string.Join(" ", said);
    }

    /// <summary>
    /// Says who scrolls a list, scrolls it to the end, and says whether its last
    /// row became visible.
    /// </summary>
    /// <remarks>
    /// The repair for <c>AOS-R002-022</c> changes what wraps a list, and a list
    /// that stops crashing but can no longer be scrolled is a different defect.
    /// This is the measurement that tells them apart.
    /// </remarks>
    private string Scroll(string listId)
    {
        AutomationElement? list = _app.Find(listId);

        if (list is null)
        {
            return "no list with automation id '" + listId + "'";
        }

        List<string> said = [];
        TreeWalker walker = TreeWalker.ControlViewWalker;
        AutomationElement? scroller = null;

        for (AutomationElement? node = list; node is not null; node = walker.GetParent(node))
        {
            if (node.TryGetCurrentPattern(ScrollPattern.Pattern, out object? found)
                && found is ScrollPattern candidate)
            {
                said.Add((node.Current.AutomationId is { Length: > 0 } id ? id : node.Current.ClassName)
                    + " vertically-scrollable=" + candidate.Current.VerticallyScrollable
                    + " view=" + candidate.Current.VerticalViewSize.ToString("0.#", CultureInfo.InvariantCulture) + "%");

                if (scroller is null && candidate.Current.VerticallyScrollable)
                {
                    scroller = node;
                }
            }

            if (said.Count >= 3)
            {
                break;
            }
        }

        AutomationElementCollection rows = list.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));

        string last = rows.Count == 0 ? "no rows" : "last row offscreen="
            + rows[rows.Count - 1].Current.IsOffscreen;

        if (scroller is not null
            && scroller.GetCurrentPattern(ScrollPattern.Pattern) is ScrollPattern scroll)
        {
            scroll.SetScrollPercent(ScrollPattern.NoScroll, 100);
            Thread.Sleep(700);

            rows = list.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));

            last += "; scrolled "
                + (scroller.Current.AutomationId is { Length: > 0 } sid ? sid : scroller.Current.ClassName)
                + " to 100%; last row offscreen=" + rows[rows.Count - 1].Current.IsOffscreen
                + " (" + rows[rows.Count - 1].Current.Name + ")";
        }
        else
        {
            last += "; nothing above the list can scroll it";
        }

        return rows.Count.ToString(CultureInfo.InvariantCulture) + " row(s); "
            + string.Join(" | ", said) + "; " + last;
    }

    private string SelectTab(string name)
    {
        AutomationElement? tab = _app.Window.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
                new PropertyCondition(AutomationElement.NameProperty, name)));

        if (tab is null
            || !tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
            || pattern is not SelectionItemPattern selection)
        {
            return "no selectable tab named '" + name + "'";
        }

        selection.Select();

        return "selected tab '" + name + "'";
    }

    /// <summary>Selects a row by position, or the first whose name starts with a prefix.</summary>
    /// <remarks>
    /// By name as well as by position, because the fixture's lists are ordered by
    /// when a record last changed, and a probe that writes moves its own rows.
    /// </remarks>
    private string SelectRow(string listId, int index, string? prefix)
    {
        AutomationElement? list = _app.Find(listId);

        if (list is null)
        {
            return "no list with automation id '" + listId + "'";
        }

        AutomationElement[] rows =
        [
            .. list.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>(),
        ];

        if (prefix is not null)
        {
            index = Array.FindIndex(
                rows, x => x.Current.Name.StartsWith(prefix, StringComparison.Ordinal));

            if (index < 0)
            {
                return "no row of '" + listId + "' starts with '" + prefix + "'";
            }
        }

        if (rows.Length <= index)
        {
            return "'" + listId + "' has " + rows.Length.ToString(CultureInfo.InvariantCulture)
                + " row(s); row " + index.ToString(CultureInfo.InvariantCulture) + " does not exist";
        }

        AutomationElement row = rows[index];

        if (!row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
            || pattern is not SelectionItemPattern selection)
        {
            return "row " + index.ToString(CultureInfo.InvariantCulture) + " of '" + listId
                + "' exposes no selection pattern";
        }

        selection.Select();

        return "selected row " + index.ToString(CultureInfo.InvariantCulture) + " of '" + listId
            + "' of " + rows.Length.ToString(CultureInfo.InvariantCulture)
            + ": '" + row.Current.Name + "'";
    }

    private void Evidence(string directory, string name, List<string> shots, List<string> trees)
    {
        _app.Refresh();

        string shot = Path.Combine(directory, name + ".png");

        if (_app.Capture(shot))
        {
            shots.Add(Relative(shot));
        }

        string tree = Path.Combine(directory, name + ".json");

        File.WriteAllText(tree, JsonSerializer.Serialize(_app.Snapshot(), Json));
        trees.Add(Relative(tree));
    }

    private double Elapsed() => Math.Round(_clock.Elapsed.TotalSeconds, 2);

    private string Relative(string path) =>
        Path.GetRelativePath(_runDirectory, path).Replace('\\', '/');
}
