using System.Globalization;
using System.Text.Json;
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
            DialogObservation attempt = Attempt(dialog, opening, directory);

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

        Thread.Sleep(700);
        _app.Refresh();

        UiaNode? focusBefore = _app.FocusedNode();

        (bool invoked, string how, string detail) = Open(dialog, opening, directory);

        // A dialog behind a tab: open each tab and try again. Tabs are where the
        // contract, obligation and offer records live, and the opener sits with
        // them.
        if (!invoked && opening.ControlLabel is { Length: > 0 })
        {
            (invoked, how, detail) = OpenViaTabs(dialog, opening, directory);
        }

        Thread.Sleep(900);
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

        if (modal is null)
        {
            string rows = selected.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " row(s) selected first";

            return new DialogObservation(
                dialog.DialogId,
                invoked ? "DID_NOT_APPEAR" : "COULD_NOT_INVOKE",
                how,
                detail + "; " + rows,
                screenshots,
                Relative(treePath),
                null, null, [], [], [], [], false, false, false, false, 0, [],
                "NOT_ATTEMPTED", null);
        }

        return Inspect(dialog, modal, afterOpen, focusBefore, how, detail, directory, screenshots, treePath);
    }

    /// <summary>Opens each tab in turn and retries the opener.</summary>
    private (bool Invoked, string How, string Detail) OpenViaTabs(
        DialogRecord dialog, DialogOpening opening, string directory)
    {
        AutomationElementCollection tabs;

        try
        {
            tabs = _app.Window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
        }
        catch (ElementNotAvailableException)
        {
            return (false, "NONE", "the tab strip could not be read");
        }

        foreach (AutomationElement? tab in tabs)
        {
            if (tab is null)
            {
                continue;
            }

            try
            {
                if (!tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                    || pattern is not SelectionItemPattern selection)
                {
                    continue;
                }

                selection.Select();
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            Thread.Sleep(700);
            _app.Refresh();

            SelectARowInEveryList();
            Thread.Sleep(500);
            _app.Refresh();

            (bool invoked, string how, string detail) = Open(dialog, opening, directory);

            if (invoked)
            {
                return (true, how, detail + " (after opening the "
                    + (tab.Current.Name ?? "?") + " tab)");
            }
        }

        return (false, "NONE", "no tab made the opener available");
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
    private (IReadOnlyList<string> Order, bool Escapes) TabOrder(IReadOnlyList<UiaNode> inside)
    {
        List<string> order = [];
        bool escaped = false;

        for (int i = 0; i < 14; i++)
        {
            if (!_app.Keys.Press(ReviewKey.Tab))
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
        (bool shows, string first) = PaletteShows(command.Label);

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
    private (bool Shows, string First) PaletteShows(string label)
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

        return (
            first.Contains(label, StringComparison.OrdinalIgnoreCase),
            "\"" + first + "\" (" + items.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + " shown)");
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
            AutomationElementCollection lists = _app.Window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));

            int selected = 0;

            foreach (AutomationElement? list in lists)
            {
                if (list is null || list.Current.IsOffscreen)
                {
                    continue;
                }

                AutomationElement? row = list.FindFirst(
                    TreeScope.Children,
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

                try
                {
                    if (row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                        && pattern is SelectionItemPattern selection)
                    {
                        selection.Select();
                        selected++;
                        Thread.Sleep(250);
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

        if (dialog.Title is { Length: > 0 } title)
        {
            UiaNode? titled = roots.FirstOrDefault(x =>
                string.Equals(x.Name, title, StringComparison.Ordinal));

            if (titled is not null)
            {
                return titled;
            }
        }

        return roots.FirstOrDefault();
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
        new(id, outcome, "NONE", detail, [], null, null, null, [], [], [], [],
            false, false, false, false, 0, [], "NOT_ATTEMPTED", null);
}
