using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using AgencyOS.Client.Commands;
using AgencyOS.Reviewer.Surface;
using System.IO;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// Opens dialogs from the real user interface and operates them.
/// </summary>
/// <remarks>
/// <para>
/// Audit 001 inventoried 61 dialogs and opened none of them. A class in source
/// proves nothing about what a person meets, and constructing one directly from a
/// test proves less: it skips the precondition, the button, the enabling rule and
/// the page state that decide whether anybody can get there at all.
/// </para>
/// <para>
/// So every dialog here is reached the way an operator reaches it — navigate,
/// satisfy the precondition, click the control the markup wires to the handler —
/// and then actually worked: focus, tab order, buttons, validation, Escape,
/// Cancel, and whether focus comes back. Nothing is invoked that would mutate
/// unless the audit chose to, and what was chosen is recorded.
/// </para>
/// </remarks>
internal sealed class DialogPass
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The list a guard reads, as the handler names it.</summary>
    private static readonly Regex GuardedList = new(
        @"(?<list>[A-Za-z_][A-Za-z0-9_]*)\.SelectedItem",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private readonly ReviewApp _app;
    private readonly string _runDirectory;

    internal DialogPass(ReviewApp app, string runDirectory)
    {
        _app = app;
        _runDirectory = runDirectory;
    }

    /// <summary>Opens one dialog and records everything about it.</summary>
    /// <param name="dialog">What the source says about it.</param>
    /// <returns>What actually happened.</returns>
    internal DialogObservation Operate(DialogRecord dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        string directory = Path.Combine(_runDirectory, "evidence", "dialog." + dialog.DialogId);

        Directory.CreateDirectory(directory);

        if (dialog.Openings.Count == 0)
        {
            return DialogObservation.NotAttempted(
                dialog.DialogId,
                "UNREACHABLE_NOTHING_CONSTRUCTS",
                "No page constructs this dialog, so there is no path to follow.");
        }

        List<string> tried = [];

        // Every declared path, because one dialog class is opened from several
        // pages and only one of them may be the page that has the record selected.
        foreach (DialogOpening opening in dialog.Openings)
        {
            DialogObservation attempt;

            try
            {
                attempt = Attempt(dialog, opening, directory);
            }
            catch (ElementNotAvailableException)
            {
                // Two different things, and they were being reported as one.
                // The process register is the authority on which.
                bool running = _app.IsRunning;

                return DialogObservation.NotAttempted(
                    dialog.DialogId,
                    running ? "WINDOW_UNREACHABLE" : "APPLICATION_CLOSED",
                    running
                        ? "the window stopped answering the automation interface "
                            + "while this dialog was being attempted from "
                            + (opening.Page ?? "?") + "; the process is still running"
                        : "the application exited while this dialog was being "
                            + "attempted from " + (opening.Page ?? "?")
                            + ExitDescription());
            }

            if (attempt.Outcome == "OPENED")
            {
                return attempt;
            }

            tried.Add((opening.Page ?? "?") + ": " + attempt.Detail);
        }

        return DialogObservation.NotAttempted(
            dialog.DialogId,
            "DID_NOT_APPEAR",
            string.Join(" | ", tried));
    }

    /// <summary>Describes a dialog that something else has already opened.</summary>
    /// <param name="dialog">What the source says about it.</param>
    /// <param name="focusBefore">Where focus was before the opener ran.</param>
    /// <param name="how">How it was opened.</param>
    /// <param name="detail">What the opener reported.</param>
    /// <returns>The same observation the pass records, Escape and focus included.</returns>
    /// <remarks>
    /// Repair Wave 003A.1. The pass selects the first row of one list, and when
    /// that row is not one the opener's guard accepts, the product correctly
    /// refuses and the dialog is never inspected. A probe that walks an explicit
    /// path to the opener hands the open dialog here, so it is measured by exactly
    /// the checks every other dialog was measured by.
    /// </remarks>
    internal DialogObservation Describe(
        DialogRecord dialog, UiaNode? focusBefore, string how, string detail)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        string directory = Path.Combine(_runDirectory, "evidence", "dialog." + dialog.DialogId);

        Directory.CreateDirectory(directory);

        _app.Refresh();

        UiaNode afterOpen = _app.Snapshot();
        UiaNode? modal = Modal(afterOpen, dialog);

        List<string> screenshots = [];
        string shot = Path.Combine(directory, "01-open.png");

        if (_app.Capture(shot))
        {
            screenshots.Add(Relative(shot));
        }

        string treePath = Path.Combine(directory, "ui-tree.json");

        File.WriteAllText(treePath, JsonSerializer.Serialize(afterOpen, Json));

        return modal is null
            ? DialogObservation.NotAttempted(dialog.DialogId, "DID_NOT_APPEAR", detail)
            : Inspect(dialog, modal, afterOpen, focusBefore, how, detail, directory, screenshots, treePath);
    }

    /// <summary>The exit code and time of a client that has gone, for the record.</summary>
    /// <remarks>
    /// Repair Wave 003A.1: sixteen closures were recorded without one, and only one
    /// of them left a crash in the event log.
    /// </remarks>
    private string ExitDescription() =>
        _app.Exit is { } exit
            ? "; exit code 0x" + exit.Code.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)
                + " at " + exit.ExitedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                + ", pid " + _app.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>Opens a dialog and leaves it open.</summary>
    /// <param name="dialog">The dialog to reach.</param>
    /// <returns>How it was opened, and the modal itself if one appeared.</returns>
    /// <remarks>
    /// For probes that need to operate a dialog rather than describe it. The
    /// caller owns closing it.
    /// </remarks>
    internal (bool Opened, string How, string Detail, UiaNode? Modal) OpenFor(
        DialogRecord dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        foreach (DialogOpening opening in dialog.Openings)
        {
            if (opening.Workspace is null)
            {
                continue;
            }

            _app.Focus();
            _app.Keys.Press(ReviewKey.Escape);
            Thread.Sleep(250);
            _app.Resize(1600, 1000);
            Thread.Sleep(400);
            _app.Refresh();

            if (!_app.Navigate(WorkspaceLabel(opening.Workspace)).Succeeded)
            {
                continue;
            }

            Thread.Sleep(1400);
            _app.Refresh();

            SelectARowInEveryList();

            Thread.Sleep(1200);
            _app.Refresh();

            SatisfyGuards(opening);

            Thread.Sleep(500);
            _app.Refresh();

            string directory = Path.Combine(
                _runDirectory, "evidence", "validation." + dialog.DialogId);

            Directory.CreateDirectory(directory);

            (bool invoked, string how, string detail) = Open(dialog, opening, directory);

            if (!invoked)
            {
                (invoked, how, detail) = OpenViaTabs(dialog, opening, directory);
            }

            Thread.Sleep(900);
            _app.Refresh();

            if (Modal(_app.Snapshot(), dialog) is { } modal)
            {
                return (true, how, detail, modal);
            }

            if (invoked)
            {
                _app.Keys.Press(ReviewKey.Escape);
                Thread.Sleep(300);
            }
        }

        return (false, "DID_NOT_APPEAR", "no declared opening produced a modal", null);
    }

    /// <summary>One attempt, down one declared path.</summary>
    private DialogObservation Attempt(
        DialogRecord dialog, DialogOpening opening, string directory)
    {
        if (opening.Workspace is null)
        {
            return DialogObservation.NotAttempted(
                dialog.DialogId, "BLOCKED", "The opening page is not a workspace.");
        }

        // A known starting point, so that a failure to open is evidence about the
        // dialog rather than about where the previous probe left the application.
        _app.Focus();
        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(250);
        _app.Resize(1600, 1000);
        Thread.Sleep(400);
        _app.Refresh();

        string workspaceLabel = WorkspaceLabel(opening.Workspace);
        ReviewStep navigated = _app.Navigate(workspaceLabel);

        if (!navigated.Succeeded)
        {
            return DialogObservation.NotAttempted(
                dialog.DialogId, "BLOCKED",
                "could not reach " + workspaceLabel + ": " + navigated.Detail);
        }

        Thread.Sleep(1400);
        _app.Refresh();

        // Most openers are disabled until a record is selected, and which list
        // holds that record differs per page, so every list gets its first row
        // selected. This is what the markup means by IsEnabled="False".
        int selected = SelectARowInEveryList();

        Thread.Sleep(1200);
        _app.Refresh();

        // Now the detail exists, so the list the guard names can be found.
        IReadOnlyList<string> guards = SatisfyGuards(opening);

        Thread.Sleep(500);
        _app.Refresh();

        UiaNode? focusBefore = _app.FocusedNode();

        (bool invoked, string how, string detail) = Open(dialog, opening, directory);

        // A dialog behind a tab: open each tab and try again. Tabs are where the
        // contract, obligation, offer and intelligence records live, and both the
        // button and the command need one selected before the page's handler will
        // build the dialog.
        //
        // This used to run only when the opener was a button, which is why
        // Phase A reported eleven tab-hosted palette dialogs as blocked by
        // missing fixture state. They were blocked by the harness never opening
        // their tab.
        Thread.Sleep(900);
        _app.Refresh();

        // Not "could it be invoked" but "did a dialog arrive". The palette runs
        // whatever it is handed and reports success either way, so for every
        // palette-opened dialog this used to skip the tab walk entirely and try
        // only whichever tab happened to be showing.
        if (Modal(_app.Snapshot(), dialog) is null)
        {
            (invoked, how, detail) = OpenViaTabs(dialog, opening, directory);

            Thread.Sleep(900);
            _app.Refresh();
        }

        UiaNode afterOpen = _app.Snapshot();
        UiaNode? modal = Modal(afterOpen, dialog);

        List<string> screenshots = [];
        string shot = Path.Combine(directory, "01-open.png");

        if (_app.Capture(shot))
        {
            screenshots.Add(Relative(shot));
        }

        string treePath = Path.Combine(directory, "ui-tree.json");

        File.WriteAllText(treePath, JsonSerializer.Serialize(afterOpen, Json));

        if (modal is null)
        {
            string rows = selected.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " row(s) selected first"
                + (guards.Count > 0
                    ? "; before any tab was chosen: " + string.Join("; ", guards)
                    : string.Empty);

            return new DialogObservation(
                dialog.DialogId,
                invoked ? "DID_NOT_APPEAR" : "COULD_NOT_INVOKE",
                how,
                detail + "; " + rows,
                screenshots,
                Relative(treePath),
                null, null, [], [], [], [], false, "not attempted", [], false, false,
                false, false, 0, [], "NOT_ATTEMPTED", null);
        }

        return Inspect(dialog, modal, afterOpen, focusBefore, how, detail, directory, screenshots, treePath);
    }

    /// <summary>Opens each tab in turn and retries the opener.</summary>
    private (bool Invoked, string How, string Detail) OpenViaTabs(
        DialogRecord dialog, DialogOpening opening, string directory)
    {
        string[] outer = TabNames();

        if (outer.Length == 0)
        {
            return (false, "NONE", "the page has no tabs");
        }

        List<string> tried = [];

        foreach (string tab in outer)
        {
            if (!SelectTabByName(tab))
            {
                continue;
            }

            (bool opened, string how, string detail) = TryHere(dialog, opening, directory, tab);

            if (opened)
            {
                return (true, how, detail);
            }

            // What the guard found *on this tab*. Recording only the tab's name
            // lost it, and the caller then reported the guard reading taken
            // before the walk began - so "PredictionList: not on the page" was
            // said about the Desk tab while the Predictions tab went unmentioned.
            tried.Add(detail);

            // Inner tabs belong to the detail this outer tab just revealed, so
            // they only exist now and are replaced by the next outer selection.
            foreach (string inner in TabNames().Except(outer, StringComparer.Ordinal))
            {
                if (!SelectTabByName(inner))
                {
                    continue;
                }

                (opened, how, detail) = TryHere(
                    dialog, opening, directory, tab + " > " + inner);

                if (opened)
                {
                    return (true, how, detail);
                }

                tried.Add(detail);
            }
        }

        return (false, "NONE",
            "no tab made the opener available. " + string.Join(" | ", tried));
    }

    /// <summary>Satisfies the guard on the current tab, then tries the opener.</summary>
    /// <remarks>
    /// The opener is only pressed once the guard has something selected, because
    /// pressing it beforehand is indistinguishable from the dialog not existing
    /// and costs a second and a half either way.
    /// </remarks>
    private (bool Opened, string How, string Detail) TryHere(
        DialogRecord dialog, DialogOpening opening, string directory, string where)
    {
        Thread.Sleep(700);
        _app.Refresh();

        SelectARowInEveryList();

        // Long enough for the detail to arrive. Several handlers guard on a
        // loaded detail object rather than on the selection - _thesis?.Thesis,
        // _signal?.Signal - so pressing the opener before the fetch returns
        // looks exactly like nothing being selected.
        Thread.Sleep(1400);
        _app.Refresh();

        IReadOnlyList<string> guards = SatisfyGuards(opening);

        Thread.Sleep(400);
        _app.Refresh();

        // A guard that names lists and found none of them here usually means this
        // is not the tab, and skipping the opener keeps a two-level walk
        // affordable. It is only sound for a palette-only opener.
        //
        // A dialog with a button is different: the guard names are read out of
        // the handler by a regex over a window of source, which picks up the
        // neighbouring methods' guards too. RecordSignatureDialog acquired
        // OptionList that way - it guards on outstanding signatories - and the
        // skip then refused to press SignatureButton on the Parties tab, where
        // the button is. Clicking a button that is disabled costs nothing and
        // reports itself, so for those the opener is always tried.
        bool hasButton = opening.Control is { Length: > 0 }
            || opening.ControlLabel is { Length: > 0 };

        if (!hasButton
            && guards.Count > 0
            && !guards.Any(x => x.Contains("selected '", StringComparison.Ordinal)))
        {
            return (false, "NONE", where + ": " + string.Join("; ", guards));
        }

        (bool invoked, string how, string detail) = Open(dialog, opening, directory);

        // Invoked is not appeared. A disabled button refuses, so for button
        // openers the two coincide; the palette always runs the command it was
        // given, so a palette opener reported success on the first tab and the
        // loop stopped there - which is why Phase B left nineteen tab-hosted
        // dialogs blocked and blamed the fixture. The dialog itself is the only
        // evidence that this was the right tab.
        if (invoked)
        {
            Thread.Sleep(900);
            _app.Refresh();

            if (Modal(_app.Snapshot(), dialog) is not null)
            {
                return (true, how, detail + " (on " + where + ")");
            }

            _app.Keys.Press(ReviewKey.Escape);
            Thread.Sleep(250);
        }

        // What the opener reported, not a generic sentence. "Ran but nothing
        // appeared" is the same message whether a button was disabled, a button
        // was missing and the palette was used instead, or the command genuinely
        // produced no dialog - and those are three different findings.
        return (false, "NONE", where + ": " + detail + " — and nothing appeared");
    }

    /// <summary>Every tab currently in the strip, by name.</summary>
    private string[] TabNames()
    {
        try
        {
            AutomationElementCollection tabs = _app.Window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

            return
            [
                .. tabs.Cast<AutomationElement>()
                    .Select(x => x.Current.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal),
            ];
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    /// <summary>Selects one tab and confirms the tree agrees it is selected.</summary>
    private bool SelectTabByName(string name)
    {
        try
        {
            AutomationElement? tab = _app.Window.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty, ControlType.TabItem),
                    new PropertyCondition(AutomationElement.NameProperty, name)));

            if (tab is null
                || !tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                || pattern is not SelectionItemPattern selection)
            {
                return false;
            }

            selection.Select();
            Thread.Sleep(600);

            return selection.Current.IsSelected;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // A tab that is being rebuilt underneath the walk answers
            // E_UNEXPECTED rather than going away politely. Repair Wave 003B met
            // this while re-opening eleven dialogs in one pass: it ended the run
            // and cost the eight dialogs that had not been reached yet. Not
            // finding the tab is an answer; losing the run is not.
            return false;
        }
    }

    /// <summary>Works the dialog once it is on screen.</summary>
    private DialogObservation Inspect(
        DialogRecord dialog,
        UiaNode modal,
        UiaNode afterOpen,
        UiaNode? focusBefore,
        string how,
        string detail,
        string directory,
        List<string> screenshots,
        string treePath)
    {
        UiaNode[] inside = [.. modal.Flatten()];
        UiaNode? focused = _app.FocusedNode();

        bool focusInside = focused is not null
            && inside.Any(x => Same(x, focused));

        // Everything the dialog offers, whether or not the source predicted it.
        IReadOnlyList<string> buttons =
        [
            .. inside.Where(x => x.ControlType == "Button" && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => x.Name! + (x.IsEnabled ? string.Empty : " (disabled)"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        IReadOnlyList<string> fields =
        [
            .. inside.Where(x => x.ControlType is "Edit" or "ComboBox" or "CheckBox" or "Slider")
                .Select(x => x.ControlType + " " + (x.AutomationId is { Length: > 0 }
                    ? x.AutomationId
                    : x.Name is { Length: > 0 } ? "\"" + x.Name + "\"" : "«unnamed»"))
                .Order(StringComparer.Ordinal),
        ];

        // Tab order inside the modal, and whether it stays inside.
        (IReadOnlyList<string> order, bool escapes) = TabOrder(inside);

        // And back again. A trap that only holds in one direction is still a
        // trap, and it is the direction an operator uses to fix a typo.
        (IReadOnlyList<string> reverse, bool reverseEscapes) = TabOrder(inside, back: true);

        IReadOnlyList<AccessibilityObservation> accessibility = Detectors.Accessibility(modal);
        IReadOnlyList<RowSpeechObservation> speech = Detectors.RowSpeech(modal);

        // Not measurable this way, and recorded as such rather than guessed. WinUI
        // puts every ContentDialog behind two visible popup windows - Audit 002
        // measured exactly two on all thirty it opened, and no node anywhere
        // carries a ContentDialog class name - so "is there an unexpected second
        // modal" cannot be answered by counting them. It stays a manual check.
        const int extraModals = -1;

        // Escape, then look for the dialog again and see where focus went.
        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(900);
        _app.Refresh();

        UiaNode afterEscape = _app.Snapshot();
        bool closed = Modal(afterEscape, dialog) is null;
        UiaNode? focusAfter = _app.FocusedNode();

        // Where focus went, classified, rather than a bare true/false. "Back to
        // the opener" and "somewhere on the page" are both acceptable; "nowhere"
        // and "still inside the dismissed dialog" are not, and lumping them
        // together says nothing anybody can act on.
        string focusAfterClose =
            focusAfter is null ? "NONE"
            : inside.Any(x => Same(x, focusAfter)) ? "STILL_IN_DISMISSED_DIALOG"
            : focusBefore is not null && Same(focusAfter, focusBefore) ? "RESTORED_TO_OPENER"
            : "ELSEWHERE_ON_PAGE";

        bool focusRestored = focusAfterClose is "RESTORED_TO_OPENER" or "ELSEWHERE_ON_PAGE";

        string closedShot = Path.Combine(directory, "02-after-escape.png");

        if (_app.Capture(closedShot))
        {
            screenshots.Add(Relative(closedShot));
        }

        return new DialogObservation(
            dialog.DialogId,
            "OPENED",
            how,
            detail,
            screenshots,
            Relative(treePath),
            modal.Name,
            focused?.Describe(),
            buttons,
            fields,
            order,
            reverse,
            reverseEscapes,
            dialog.DefaultButton ?? "none declared",
            accessibility,
            focusInside,
            escapes,
            closed,
            focusRestored,
            extraModals,
            speech,
            focusAfterClose,
            focusAfter?.Describe());
    }

    /// <summary>Presses Tab around the modal and says where focus went.</summary>
    /// <remarks>
    /// Bounded, and the question is containment rather than completeness: a modal
    /// whose Tab order leaves it puts the keyboard somewhere the user cannot see
    /// they are.
    /// </remarks>
    private (IReadOnlyList<string> Order, bool Escapes) TabOrder(
        IReadOnlyList<UiaNode> inside, bool back = false)
    {
        List<string> order = [];
        bool escaped = false;

        for (int i = 0; i < 14; i++)
        {
            if (!_app.Keys.Press(
                ReviewKey.Tab, back ? ReviewModifiers.Shift : ReviewModifiers.None))
            {
                break;
            }

            Thread.Sleep(120);

            UiaNode? focused = _app.FocusedNode();

            if (focused is null)
            {
                order.Add("«no focused element»");

                continue;
            }

            order.Add(focused.Describe());

            if (!inside.Any(x => Same(x, focused)))
            {
                escaped = true;
            }
        }

        return (order, escaped);
    }

    /// <summary>Clicks the control the markup wires to the handler, or runs the command.</summary>
    private (bool Invoked, string How, string Detail) Open(
        DialogRecord dialog, DialogOpening opening, string directory)
    {
        List<string> reasons = [];

        if (opening.ControlLabel is { Length: > 0 } label)
        {
            (bool clicked, string why) = Click(label);

            if (clicked)
            {
                return (true, "BUTTON", "clicked \"" + label + "\"");
            }

            reasons.Add("\"" + label + "\" " + why);
        }

        if (opening.Control is { Length: > 0 } id)
        {
            (bool clicked, string why) = ClickById(id);

            if (clicked)
            {
                return (true, "BUTTON", "invoked " + id);
            }

            reasons.Add(id + " " + why);
        }

        if (opening.CommandId is { Length: > 0 } command)
        {
            (bool ran, string why) = RunCommand(command, directory);

            if (ran)
            {
                return (true, "PALETTE", "ran " + command + " from the palette");
            }

            reasons.Add("palette: " + why);
        }

        return (false, reasons.Count == 0 ? "NONE" : "BUTTON",
            reasons.Count == 0 ? "nothing reaches this dialog" : string.Join("; ", reasons));
    }

    private (bool Clicked, string Why) Click(string label) => Invoke(_app.FindByName(label));

    private (bool Clicked, string Why) ClickById(string automationId) =>
        Invoke(_app.Find(automationId));

    /// <summary>
    /// Presses a control, and says why it could not be pressed when it could not.
    /// </summary>
    /// <remarks>
    /// "Not found" and "found but disabled" are different facts. The first may be
    /// a dialog nobody can reach; the second is a precondition the audit did not
    /// satisfy, and reporting them alike would turn an unmet precondition into a
    /// defect.
    /// </remarks>
    private static (bool Clicked, string Why) Invoke(AutomationElement? element)
    {
        if (element is null)
        {
            return (false, "was not on the page");
        }

        try
        {
            if (!element.Current.IsEnabled)
            {
                return (false, "was present but disabled");
            }

            if (element.Current.IsOffscreen)
            {
                return (false, "was present but not on screen");
            }

            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern)
                && pattern is InvokePattern invoker)
            {
                invoker.Invoke();

                return (true, "clicked");
            }

            return (false, "could not be invoked");
        }
        catch (ElementNotAvailableException)
        {
            return (false, "left the tree while being pressed");
        }
        catch (InvalidOperationException)
        {
            return (false, "refused to be invoked");
        }
    }

    /// <summary>
    /// Opens the palette, types a command's label and takes the first result.
    /// </summary>
    /// <remarks>
    /// Seventeen dialogs have no button anywhere and are reachable only this way,
    /// so whether the palette can actually run them is itself an audit question.
    /// Every step records what it saw, because "the palette did not run it" is
    /// worth nothing without saying what the palette showed instead.
    /// </remarks>
    private (bool Ran, string Why) RunCommand(string commandId, string directory)
    {
        if (!_app.Keys.PressGesture('P', ReviewModifiers.Control))
        {
            return (false, "the palette keystroke was refused");
        }

        Thread.Sleep(800);

        // The palette filters on the command's label, so the label is what to
        // type. Guessing a term from the identifier's last segment - "add",
        // "record", "resolve" - matches a dozen commands and runs whichever
        // happens to rank first, which is not evidence about this dialog.
        _app.Refresh();

        if (_app.Snapshot().Flatten().All(x => x.AutomationId != "PaletteQuery"))
        {
            return (false, "the palette did not open");
        }

        if (CommandRegistry.Default.Find(commandId) is not { } command)
        {
            return (false, "the registry has no command with that identifier");
        }

        if (!_app.Keys.Type(command.Label))
        {
            return (false, "the label could not be typed");
        }

        Thread.Sleep(900);

        // Confirm the palette narrowed to this command before committing, so a
        // miss is recorded as a miss rather than as having run something else.
        (bool shows, string first) = PaletteShows(commandId);

        if (!shows)
        {
            string shot = Path.Combine(directory, "00-palette.png");

            _app.Capture(shot);

            _app.Keys.Press(ReviewKey.Escape);
            Thread.Sleep(300);

            return (false, "typing \"" + command.Label + "\" left " + first + " at the top");
        }

        _app.Keys.Press(ReviewKey.Enter);
        Thread.Sleep(500);

        return (true, "ran from the palette");
    }

    /// <summary>Whether the palette's first result is the command that was typed.</summary>
    private (bool Shows, string First) PaletteShows(string commandId)
    {
        _app.Refresh();

        UiaNode? results = _app.Snapshot().Flatten()
            .FirstOrDefault(x => x.AutomationId == "PaletteResults");

        if (results is null)
        {
            return (false, "no result list");
        }

        UiaNode[] items =
        [
            .. results.Flatten().Where(x => x.ControlType == "ListItem" && !x.IsOffscreen),
        ];

        if (items.Length == 0)
        {
            return (false, "no results");
        }

        string first = items[0].Name ?? "«unnamed»";

        // By identifier, not by label. A palette row announces itself as the
        // record it binds - "PaletteCommand { Id = go.people, Title = Go to
        // People, ... }" - so the identifier is right there and is exact.
        // Matching the label with Contains could not tell "Connect mailbox" from
        // "Disconnect mailbox", and would have confirmed the wrong command and
        // pressed Enter on it.
        bool byIdentifier =
            first.Contains("Id = " + commandId + ",", StringComparison.Ordinal)
            || first.Contains("Id = " + commandId + " ", StringComparison.Ordinal);

        return (
            byIdentifier || MatchesUniqueLabel(commandId, first),
            "\"" + first + "\" (" + items.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + " shown)");
    }

    /// <summary>
    /// Whether the row announced exactly this command's label, and no other
    /// command shares it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added by Repair Wave 003A, and narrow on purpose. A palette row does not
    /// always announce the bound record: in some runs it announces the plain
    /// label instead, which is <c>AOS-R002-018</c>'s territory and not this
    /// wave's. When it does, the identifier match above refuses a command that is
    /// sitting correctly and alone at the top of the list, and the pass records
    /// "nothing appeared" for a command it never ran — which is the one thing an
    /// opener audit must not confuse.
    /// </para>
    /// <para>
    /// Exact equality, and only when the label is unique across the registry, so
    /// this cannot do what matching with <c>Contains</c> would have done. One
    /// label is shared by two commands today, and this rule declines both.
    /// </para>
    /// </remarks>
    internal static bool MatchesUniqueLabel(string commandId, string announced)
    {
        if (CommandRegistry.Default.Find(commandId) is not { } command
            || !string.Equals(announced, command.Label, StringComparison.Ordinal))
        {
            return false;
        }

        return CommandRegistry.Default.Commands
            .Count(x => string.Equals(x.Label, command.Label, StringComparison.Ordinal)) == 1;
    }

    /// <summary>
    /// Selects the first row of every list on the page.
    /// </summary>
    /// <remarks>
    /// Which list holds the record an opener needs differs per page — a project's
    /// roles, a contract's obligations, a deal's offers — so every list gets a
    /// selection rather than guessing which one matters.
    /// </remarks>
    /// <returns>How many rows were selected.</returns>
    private int SelectARowInEveryList()
    {
        try
        {
            // Inside the page, not the window. The navigation pane's destinations
            // are list items and so is the settings item, and selecting one of
            // those leaves the workspace before anything else happens.
            AutomationElement root = ContentHost() ?? _app.Window;

            AutomationElementCollection lists = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));

            int selected = 0;

            foreach (AutomationElement? list in lists)
            {
                if (list is null || list.Current.IsOffscreen)
                {
                    continue;
                }

                // A TabView's strip is a list, and its rows are the tabs. Choosing
                // one here undoes the tab the walk just selected: Finance was left
                // on Receivables after visiting all seven of its tabs, and the
                // Payments tab was then reported as not having a PaymentList.
                if (string.Equals(
                    list.Current.AutomationId, "TabListView", StringComparison.Ordinal))
                {
                    continue;
                }

                // Descendants, not Children. A WinUI ListView virtualizes its rows
                // under a ScrollViewer and an ItemsPresenter, so the rows are
                // never direct children and a Children-scoped search finds
                // nothing on exactly the dense lists this pass needs.
                AutomationElement? row = list.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.IsSelectionItemPatternAvailableProperty, true));

                if (row is null)
                {
                    continue;
                }

                // The navigation destinations are list items too, and selecting one
                // would leave the workspace instead of choosing a record on it.
                string name = row.Current.Name ?? string.Empty;

                if (AgencyOsWorkspaces.All.Any(x =>
                    string.Equals(x.Label, name, StringComparison.Ordinal)))
                {
                    continue;
                }

                // And a tab that reached this far by some other route.
                if (Equals(row.Current.ControlType, ControlType.TabItem))
                {
                    continue;
                }

                try
                {
                    if (row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                        && pattern is SelectionItemPattern selection)
                    {
                        selection.Select();
                        selected++;

                        // One list, not every list. Selecting in a second list on
                        // the same tab replaces the detail the first selection
                        // loaded, and the handler guards on that detail - which is
                        // why a tab probe that selected one row opened a dialog
                        // this pass could not.
                        Thread.Sleep(900);

                        return selected;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // The list rebuilt itself while being selected; the next one
                    // is still worth trying.
                }
                catch (InvalidOperationException)
                {
                }
            }

            return selected;
        }
        catch (ElementNotAvailableException)
        {
            return 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>
    /// The shell's content host, which is everything except the navigation pane.
    /// </summary>
    /// <remarks>
    /// Named in the markup by Repair Wave 002. Falling back to the whole window
    /// when it cannot be found is deliberate: a pass that silently searched
    /// nothing would report every dialog as unopenable.
    /// </remarks>
    /// <summary>
    /// Selects a row in each list the handler's guard names.
    /// </summary>
    /// <param name="opening">The declared path, whose preconditions are read.</param>
    /// <returns>What happened to each named list, in the handler's own terms.</returns>
    /// <remarks>
    /// <para>
    /// Selecting a row in every list is enough for a page whose command reads
    /// the page's own list. It is not enough for a command that reads a list
    /// inside a detail, because that list does not exist until the detail has
    /// loaded, and by then the sweep has been and gone.
    /// </para>
    /// <para>
    /// So this runs second, after the detail has had time to arrive, and it
    /// looks only where the guard says to look. A list that is absent and a list
    /// that is empty are different answers and are reported as such.
    /// </para>
    /// </remarks>
    private IReadOnlyList<string> SatisfyGuards(DialogOpening opening)
    {
        string[] required =
        [
            .. opening.Preconditions
                .SelectMany(x => GuardedList.Matches(x).Cast<Match>())
                .Select(x => x.Groups["list"].Value)
                .Distinct(StringComparer.Ordinal),
        ];

        if (required.Length == 0)
        {
            return [];
        }

        List<string> report = [];

        foreach (string listName in required)
        {
            report.Add(listName + ": " + SelectFirstRow(listName));
            Thread.Sleep(600);
            _app.Refresh();
        }

        return report;
    }

    /// <summary>Selects the first row of one named list.</summary>
    private string SelectFirstRow(string automationId)
    {
        try
        {
            AutomationElement root = ContentHost() ?? _app.Window;

            AutomationElement? list = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

            if (list is null)
            {
                return "not on the page";
            }

            AutomationElementCollection rows = list.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty, ControlType.ListItem));

            if (rows.Count == 0)
            {
                return "on the page but empty";
            }

            if (rows[0] is not { } first
                || !first.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                || pattern is not SelectionItemPattern selection)
            {
                return "has rows that cannot be selected";
            }

            selection.Select();
            Thread.Sleep(700);

            return selection.Current.IsSelected
                ? "selected '" + (first.Current.Name ?? "?") + "'"
                : "Select() returned but IsSelected is false";
        }
        catch (ElementNotAvailableException)
        {
            return "left the tree while being selected";
        }
        catch (InvalidOperationException failure)
        {
            return "refused: " + failure.Message;
        }
    }

    private AutomationElement? ContentHost()
    {
        try
        {
            return _app.Window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "ContentHost"));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>Finds the dialog in the tree, by title or by being a dialog at all.</summary>
    private static UiaNode? Modal(UiaNode tree, DialogRecord dialog)
    {
        UiaNode[] roots = [.. tree.Flatten().Where(IsDialogRoot)];

        if (roots.Length == 0)
        {
            // Some dialogs surface only as the popup window that hosts them.
            roots =
            [
                .. tree.Flatten().Where(x =>
                    x.ControlType == "Window" && x.ClassName == "Popup" && !x.IsOffscreen),
            ];
        }

        return Detectors.DialogRoot(roots, dialog.Title);
    }

    /// <summary>
    /// Whether a node is the root of a dialog.
    /// </summary>
    /// <remarks>
    /// The <c>ContentDialog</c> only. WinUI also hosts it inside a <c>Popup</c>
    /// window, so accepting both counted every dialog twice and reported thirty
    /// unexpected second modals where there were none.
    /// </remarks>
    private static bool IsDialogRoot(UiaNode node) =>
        node.ClassName?.Contains("ContentDialog", StringComparison.Ordinal) == true;

    /// <summary>
    /// Whether two snapshots describe the same control.
    /// </summary>
    /// <remarks>
    /// Identity, not geometry. Comparing rectangles as well made every focus
    /// answer depend on whether the control had moved a pixel between the tree
    /// snapshot and the focus read, and the first pass consequently reported that
    /// focus was restored on none of thirty dialogs — which was a fact about the
    /// comparison, not about the product.
    /// </remarks>
    private static bool Same(UiaNode first, UiaNode second) =>
        string.Equals(first.ControlType, second.ControlType, StringComparison.Ordinal)
        && string.Equals(first.AutomationId, second.AutomationId, StringComparison.Ordinal)
        && string.Equals(first.Name, second.Name, StringComparison.Ordinal);

    private static string WorkspaceLabel(string tag)
    {
        AgencyOsWorkspace? workspace = AgencyOsWorkspaces.All.FirstOrDefault(x =>
            string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase));

        if (workspace is not null)
        {
            return workspace.Label;
        }

        // The page's own name, when it is not the workspace tag: SyncPage is
        // "sync", CommandCenterPage is "commandcenter".
        return AgencyOsWorkspaces.All
            .FirstOrDefault(x => string.Equals(
                x.Tag.Replace("-", string.Empty, StringComparison.Ordinal),
                tag, StringComparison.OrdinalIgnoreCase))
            ?.Label ?? tag;
    }

    private string Relative(string path) =>
        Path.GetRelativePath(_runDirectory, path).Replace('\\', '/');
}

/// <summary>What happened when the audit opened one dialog.</summary>
/// <param name="DialogId">Which dialog.</param>
/// <param name="Outcome">OPENED, DID_NOT_APPEAR, COULD_NOT_INVOKE, BLOCKED or UNREACHABLE_*.</param>
/// <param name="How">BUTTON, PALETTE or NONE.</param>
/// <param name="Detail">What the pass did, in words.</param>
/// <param name="Screenshots">Captures, relative to the run directory.</param>
/// <param name="TreePath">The automation snapshot taken with the dialog open.</param>
/// <param name="Title">The name the dialog announces.</param>
/// <param name="InitialFocus">Where focus was when it opened.</param>
/// <param name="Buttons">Every button it offers, and whether each is enabled.</param>
/// <param name="Fields">Every input it offers.</param>
/// <param name="TabOrder">Where Tab went, in order.</param>
/// <param name="ReverseTabOrder">Where Shift+Tab went, in order.</param>
/// <param name="ReverseTabEscaped">Whether going backwards left the dialog.</param>
/// <param name="DefaultAction">The button Enter commits, as the markup declares it.</param>
/// <param name="Accessibility">What the corrected detectors found inside it.</param>
/// <param name="FocusEnteredDialog">Whether opening it put focus inside.</param>
/// <param name="FocusEscapedDialog">Whether tabbing left the modal.</param>
/// <param name="ClosedOnEscape">Whether Escape dismissed it.</param>
/// <param name="FocusRestored">Whether focus came back where it started.</param>
/// <param name="ExtraModals">Dialogs open beyond the expected one, or -1 when not measurable.</param>
/// <param name="RowSpeech">What any list inside it would announce.</param>
/// <param name="FocusAfterClose">Where focus went once the dialog was dismissed.</param>
/// <param name="FocusLandedOn">Which control that was.</param>
public sealed record DialogObservation(
    string DialogId,
    string Outcome,
    string How,
    string Detail,
    IReadOnlyList<string> Screenshots,
    string? TreePath,
    string? Title,
    string? InitialFocus,
    IReadOnlyList<string> Buttons,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> TabOrder,
    IReadOnlyList<string> ReverseTabOrder,
    bool ReverseTabEscaped,
    string DefaultAction,
    IReadOnlyList<AccessibilityObservation> Accessibility,
    bool FocusEnteredDialog,
    bool FocusEscapedDialog,
    bool ClosedOnEscape,
    bool FocusRestored,
    int ExtraModals,
    IReadOnlyList<RowSpeechObservation> RowSpeech,
    string FocusAfterClose,
    string? FocusLandedOn)
{
    /// <summary>Records a dialog the pass did not get to, and why.</summary>
    /// <param name="id">Which dialog.</param>
    /// <param name="outcome">Why not.</param>
    /// <param name="detail">In words.</param>
    /// <returns>An observation that claims nothing.</returns>
    public static DialogObservation NotAttempted(string id, string outcome, string detail) =>
        new(id, outcome, "NONE", detail, [], null, null, null, [], [], [], [], false,
            "not attempted", [], false, false, false, false, 0, [], "NOT_ATTEMPTED", null);
}
