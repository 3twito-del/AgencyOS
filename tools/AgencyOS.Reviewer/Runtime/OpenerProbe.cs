using System.Globalization;
using System.Text.Json;
using System.Windows.Automation;
using AgencyOS.Client.Commands;
using System.IO;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>What one opener invocation amounted to, for an operator watching it.</summary>
/// <remarks>
/// <para>
/// The outcome vocabulary is the point. Audit 002 found four openers that were
/// invoked and produced <em>nothing</em> — no dialog, no refusal, no notice, no
/// focus change — and the ordinary dialog pass could only say the dialog did not
/// appear, which is also what a correct refusal looks like from the outside.
/// </para>
/// <para>
/// So this separates them. <c>INVOKED_NO_OBSERVABLE_OUTCOME</c> is a distinct
/// verdict from <c>REFUSED_WITH_FEEDBACK</c>, and only the first is a defect.
/// </para>
/// </remarks>
/// <param name="DialogId">The dialog the opener is supposed to bring up.</param>
/// <param name="Workspace">Which workspace the opener lives in.</param>
/// <param name="Tab">Which tab on it.</param>
/// <param name="CommandId">The registry command that runs the opener.</param>
/// <param name="Control">The control clicked, when a button was used instead.</param>
/// <param name="RowsOnTab">How many rows the tab held.</param>
/// <param name="RowSelected">Whether a row was selected before invoking.</param>
/// <param name="Invoked">Whether the opener was actually invoked.</param>
/// <param name="How">BUTTON, PALETTE or NONE.</param>
/// <param name="Outcome">The verdict, from the vocabulary above.</param>
/// <param name="DialogTitle">The dialog that appeared, when one did.</param>
/// <param name="NoticesBefore">Open notices before the invocation.</param>
/// <param name="NoticesAfter">Open notices after it.</param>
/// <param name="FocusBefore">Where focus was before.</param>
/// <param name="FocusAfter">Where focus was after.</param>
/// <param name="Steps">Every step, in order, with what the tree showed.</param>
/// <param name="Screenshots">Captured evidence, relative to the run directory.</param>
/// <param name="Trees">Automation snapshots, relative to the run directory.</param>
public sealed record OpenerProbeResult(
    string DialogId,
    string Workspace,
    string Tab,
    string CommandId,
    string? Control,
    int RowsOnTab,
    bool RowSelected,
    bool Invoked,
    string How,
    string Outcome,
    string? DialogTitle,
    IReadOnlyList<string> NoticesBefore,
    IReadOnlyList<string> NoticesAfter,
    string? FocusBefore,
    string? FocusAfter,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Screenshots,
    IReadOnlyList<string> Trees);

/// <summary>
/// Invokes one named opener and says what an operator would have seen.
/// </summary>
/// <remarks>
/// <para>
/// Narrow on purpose. This is reproduction support for <c>AOS-R002-019</c> and
/// nothing else: it takes the four surfaces it is pointed at, drives each the way
/// the product intends, and writes before/after evidence for each. It discovers
/// nothing, traverses nothing and replaces no manual gate.
/// </para>
/// <para>
/// The palette match is deliberately looser than <c>DialogPass</c>'s. That pass
/// requires the result row to announce itself as the bound record, which is how
/// it refuses to press Enter on the wrong command; but the row's accessible name
/// is itself an open finding (<c>AOS-R002-018</c>), and when it announces the
/// plain label instead, the strict match refuses a command that is sitting
/// correctly at the top of a one-result list. Here the label is accepted as well
/// as the identifier, and which of the two matched is recorded, so a run that
/// leaned on the looser rule says so.
/// </para>
/// </remarks>
internal sealed class OpenerProbe
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ReviewApp _app;
    private readonly string _runDirectory;

    internal OpenerProbe(ReviewApp app, string runDirectory)
    {
        _app = app;
        _runDirectory = runDirectory;
    }

    /// <summary>Drives one opener and records what happened.</summary>
    /// <param name="dialogId">The dialog expected to appear.</param>
    /// <param name="workspace">The workspace holding the opener.</param>
    /// <param name="tab">The tab on it.</param>
    /// <param name="commandId">The command the palette would run.</param>
    /// <param name="control">A button to click instead, when the page has one.</param>
    /// <param name="selectRow">Whether to select the tab's first row first.</param>
    /// <returns>What an operator would have seen.</returns>
    internal OpenerProbeResult Probe(
        string dialogId,
        string workspace,
        string tab,
        string commandId,
        string? control,
        bool selectRow)
    {
        string directory = Path.Combine(_runDirectory, "evidence", "opener." + dialogId);

        Directory.CreateDirectory(directory);

        List<string> steps = [];
        List<string> shots = [];
        List<string> trees = [];

        _app.Focus();
        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(250);
        _app.Resize(1600, 1000);
        Thread.Sleep(400);
        _app.Refresh();

        ReviewStep navigated = _app.Navigate(workspace);

        steps.Add("navigate to " + workspace + ": "
            + (navigated.Succeeded ? "ok" : navigated.Detail));

        Thread.Sleep(1500);
        _app.Refresh();

        (bool tabSelected, string tabDetail) = SelectTab(tab);

        steps.Add("select the " + tab + " tab: " + tabDetail);

        Thread.Sleep(1500);
        _app.Refresh();

        UiaNode before = _app.Snapshot();

        UiaNode[] rows = Rows(before);

        steps.Add("rows on the tab: " + rows.Length.ToString(CultureInfo.InvariantCulture)
            + (rows.Length > 0 ? " (first: " + rows[0].Name + ")" : string.Empty));

        bool rowSelected = false;

        if (selectRow)
        {
            (rowSelected, string rowDetail) = SelectRow(rows.FirstOrDefault()?.Name);

            steps.Add("select a row: " + rowDetail);

            Thread.Sleep(1200);
            _app.Refresh();

            before = _app.Snapshot();
        }

        IReadOnlyList<string> noticesBefore = Notices(before);
        string? focusBefore = _app.FocusedNode()?.Describe();

        steps.Add("notices before: " + Join(noticesBefore));
        steps.Add("focus before: " + (focusBefore ?? "nothing"));

        string beforeShot = Path.Combine(directory, "01-before.png");

        _app.Capture(beforeShot);
        shots.Add(Relative(beforeShot));

        string beforeTree = Path.Combine(directory, "01-before.json");

        File.WriteAllText(beforeTree, JsonSerializer.Serialize(before, Json));
        trees.Add(Relative(beforeTree));

        // The opener itself. A button where the page has one, because that is
        // what an operator reaches for; the palette otherwise, because for these
        // pages it is the only path there is.
        bool invoked;
        string how;

        if (control is { Length: > 0 })
        {
            ReviewStep clicked = _app.Invoke(control);

            invoked = clicked.Succeeded;
            how = invoked ? "BUTTON" : "NONE";

            steps.Add("click \"" + control + "\": " + clicked.Detail);
        }
        else
        {
            (invoked, string why) = RunFromPalette(commandId, directory);

            how = invoked ? "PALETTE" : "NONE";

            steps.Add("run " + commandId + ": " + why);
        }

        Thread.Sleep(1500);
        _app.Refresh();

        UiaNode after = _app.Snapshot();

        string afterShot = Path.Combine(directory, "02-after.png");

        _app.Capture(afterShot);
        shots.Add(Relative(afterShot));

        string afterTree = Path.Combine(directory, "02-after.json");

        File.WriteAllText(afterTree, JsonSerializer.Serialize(after, Json));
        trees.Add(Relative(afterTree));

        UiaNode? dialog = after.Flatten().FirstOrDefault(x =>
            x.ControlType == "Window" && x.ClassName == "Popup" && !x.IsOffscreen);

        IReadOnlyList<string> noticesAfter = Notices(after);
        string? focusAfter = _app.FocusedNode()?.Describe();

        steps.Add("a dialog appeared: " + (dialog is not null ? "yes — " + (dialog.Name ?? "?") : "no"));
        steps.Add("notices after: " + Join(noticesAfter));
        steps.Add("focus after: " + (focusAfter ?? "nothing"));

        string outcome = Classify(invoked, control, before, dialog, noticesBefore, noticesAfter);

        steps.Add("outcome: " + outcome);

        // Leave the application as it was found, so the next case starts from a
        // page rather than from this one's dialog.
        if (dialog is not null)
        {
            _app.Keys.Press(ReviewKey.Escape);
            Thread.Sleep(700);
        }

        return new OpenerProbeResult(
            dialogId,
            workspace,
            tab,
            commandId,
            control,
            rows.Length,
            rowSelected,
            invoked,
            how,
            outcome,
            dialog?.Name,
            noticesBefore,
            noticesAfter,
            focusBefore,
            focusAfter,
            steps,
            shots,
            trees);
    }

    /// <summary>
    /// Navigates to a control and says whether an operator could click it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The closure slice's instrument for the two dialogs Audit 002 left unopened
    /// for reasons that were the harness's rather than the product's. It follows a
    /// path of tabs — nested ones included, which is where <c>ParticipantList</c>
    /// lives — selecting the first row at each stop so the next level has something
    /// to render, and then asks <see cref="TargetReach"/> about one named control.
    /// </para>
    /// <para>
    /// It does not invoke the control. Whether the dialog then opens is the
    /// ordinary dialog pass's question, and keeping those separate is what lets a
    /// verdict of "the operator could not have clicked this either" mean something.
    /// </para>
    /// </remarks>
    /// <param name="workspace">The workspace to navigate to.</param>
    /// <param name="tabs">Tabs to select in order, outermost first.</param>
    /// <param name="target">The control's automation identifier.</param>
    /// <param name="width">Window width to measure at.</param>
    /// <param name="height">Window height to measure at.</param>
    /// <returns>The steps taken and the reach verdict.</returns>
    internal OpenerProbeResult Reach(
        string workspace,
        IReadOnlyList<string> tabs,
        string target,
        int width,
        int height)
    {
        string directory = Path.Combine(_runDirectory, "evidence", "reach." + target);

        Directory.CreateDirectory(directory);

        List<string> steps = [];
        List<string> shots = [];
        List<string> trees = [];

        _app.Focus();
        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(250);
        _app.Resize(width, height);
        Thread.Sleep(500);
        _app.Refresh();

        steps.Add("window: " + _app.Size());

        ReviewStep navigated = _app.Navigate(workspace);

        steps.Add("navigate to " + workspace + ": "
            + (navigated.Succeeded ? "ok" : navigated.Detail));

        Thread.Sleep(1500);
        _app.Refresh();

        foreach (string tab in tabs)
        {
            (bool selected, string detail) = SelectTab(tab);

            steps.Add("select the " + tab + " tab: " + detail);

            Thread.Sleep(1300);
            _app.Refresh();

            // A row so the next level has something to show. The detail pane of a
            // list-and-detail page renders nothing until a record is chosen, and a
            // nested tab inside it therefore holds nothing either.
            UiaNode[] rows = Rows(_app.Snapshot());

            if (rows.Length > 0)
            {
                (_, string rowDetail) = SelectRow(rows[0].Name);

                steps.Add("  select a row: " + rowDetail);

                Thread.Sleep(1300);
                _app.Refresh();
            }
            else
            {
                steps.Add("  no rows on that tab");
            }
        }

        string beforeShot = Path.Combine(directory, "01-before.png");

        _app.Capture(beforeShot);
        shots.Add(Relative(beforeShot));

        UiaNode before = _app.Snapshot();

        string beforeTree = Path.Combine(directory, "01-before.json");

        File.WriteAllText(beforeTree, JsonSerializer.Serialize(before, Json));
        trees.Add(Relative(beforeTree));

        TargetReach reach = new(_app);
        ReachResult inspected = reach.Inspect(target);

        steps.Add("before: " + inspected.Verdict + " — " + inspected.Detail
            + " (bounds " + (inspected.Bounds ?? "none") + ")");

        ReachResult revealed = reach.Reveal(target);

        steps.Add("after reveal: " + revealed.Verdict + " — " + revealed.Detail
            + " (bounds " + (revealed.Bounds ?? "none") + ")"
            + (revealed.ScrollPattern is { } used ? " via " + used : string.Empty));

        Thread.Sleep(400);
        _app.Refresh();

        string afterShot = Path.Combine(directory, "02-after.png");

        _app.Capture(afterShot);
        shots.Add(Relative(afterShot));

        UiaNode after = _app.Snapshot();

        string afterTree = Path.Combine(directory, "02-after.json");

        File.WriteAllText(afterTree, JsonSerializer.Serialize(after, Json));
        trees.Add(Relative(afterTree));

        return new OpenerProbeResult(
            target,
            workspace,
            string.Join(">", tabs),
            string.Empty,
            target,
            Rows(before).Length,
            false,
            false,
            "REACH",
            revealed.Verdict,
            null,
            Notices(before),
            Notices(after),
            inspected.Bounds,
            revealed.Bounds,
            steps,
            shots,
            trees);
    }

    /// <summary>
    /// What the invocation amounted to.
    /// </summary>
    /// <remarks>
    /// The order matters. A dialog that appeared is the intended outcome whatever
    /// else changed; a new open notice is a refusal the operator can read; a
    /// disabled or absent control never got as far as being invoked. Only when
    /// the opener ran and none of those is true has the operator been told
    /// nothing, and that verdict is the one this whole probe exists to name.
    /// </remarks>
    internal static string Classify(
        bool invoked,
        string? control,
        UiaNode before,
        UiaNode? dialog,
        IReadOnlyList<string> noticesBefore,
        IReadOnlyList<string> noticesAfter)
    {
        if (dialog is not null)
        {
            return "OPENED";
        }

        if (!invoked)
        {
            if (control is { Length: > 0 })
            {
                UiaNode? node = before.Flatten().FirstOrDefault(x =>
                    string.Equals(x.Name, control, StringComparison.Ordinal));

                return node is null ? "MISSING_OPENER"
                    : node.IsEnabled ? "MISSING_OPENER" : "DISABLED";
            }

            return "MISSING_OPENER";
        }

        string[] appeared = [.. noticesAfter.Except(noticesBefore, StringComparer.Ordinal)];

        if (appeared.Length > 0)
        {
            return appeared.Any(x => x.StartsWith(FailureTitle, StringComparison.Ordinal))
                ? "FAILED_WITH_VISIBLE_ERROR"
                : "REFUSED_WITH_FEEDBACK";
        }

        return "INVOKED_NO_OBSERVABLE_OUTCOME";
    }

    /// <summary>
    /// The title every AgencyOS page gives a failure, as distinct from a notice.
    /// </summary>
    /// <remarks>
    /// The one string this probe reads, and it is read deliberately. UI Automation
    /// does not expose an <c>InfoBar</c>'s severity: the bar arrives as a
    /// <c>StatusBar</c> whose only severity signal is a standard icon whose
    /// accessible name is in the operating system's display language — which on
    /// this machine is Hebrew, and which the harness has already been caught
    /// trusting once. A product string in this repository is a firmer thing to
    /// stand on than a localized one from the shell, and if the copy changes this
    /// degrades to <c>REFUSED_WITH_FEEDBACK</c>, which is still "the product
    /// answered" rather than the silence being hunted.
    /// </remarks>
    private const string FailureTitle = "That did not happen";

    /// <summary>Every notice the page is showing, with its title and message.</summary>
    /// <remarks>
    /// <para>
    /// An <c>InfoBar</c> that is closed is not in the automation tree at all, so
    /// presence is the signal. The text goes in because "a notice appeared" and
    /// "the notice that was already there is still there" are different answers,
    /// and only the first is the product answering this invocation.
    /// </para>
    /// <para>
    /// Read as the tree actually presents it, which took a correction. An open
    /// <c>InfoBar</c> arrives as a <c>StatusBar</c> whose class name is the
    /// <em>fully qualified</em> <c>Microsoft.UI.Xaml.Controls.InfoBar</c>, with no
    /// accessible name of its own; its title and message are child text elements
    /// carrying those automation ids. Matching the short class name and reading
    /// <c>Name</c> found nothing at all, so every notice on every page was
    /// invisible to this probe — which would have reported a legitimate refusal as
    /// the silence this wave exists to catch.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> Notices(UiaNode tree) =>
    [
        .. tree.Flatten()
            .Where(x => x.ClassName is { Length: > 0 } name
                && (name.EndsWith(".InfoBar", StringComparison.Ordinal)
                    || string.Equals(name, "InfoBar", StringComparison.Ordinal))
                && !x.IsOffscreen)
            .Select(Describe)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>One notice, as an operator would read it.</summary>
    private static string Describe(UiaNode bar)
    {
        string? Part(string automationId) => bar.Flatten()
            .FirstOrDefault(x => x.AutomationId == automationId)?.Name;

        string title = Part("Title") ?? bar.Name ?? "«untitled»";
        string message = Part("Message") ?? bar.HelpText ?? bar.Value ?? string.Empty;

        return message.Length > 0 ? title + " — " + message : title;
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join(" | ", values);

    private static UiaNode[] Rows(UiaNode tree)
    {
        UiaNode? content = tree.Flatten().FirstOrDefault(x => x.AutomationId == "ContentHost");

        return
        [
            .. (content ?? tree).Flatten().Where(x =>
                x.ControlType == "ListItem"
                && !string.IsNullOrWhiteSpace(x.Name)
                && !AgencyOsWorkspaces.All.Any(w =>
                    string.Equals(w.Label, x.Name, StringComparison.Ordinal))),
        ];
    }

    private (bool Ran, string Why) RunFromPalette(string commandId, string directory)
    {
        if (CommandRegistry.Default.Find(commandId) is not { } command)
        {
            return (false, "the registry has no command with that identifier");
        }

        // Focus first. A refused keystroke is a safety property of the harness —
        // it will not type into a window that is not the one under review — and
        // the cheapest way to stop provoking it is to claim the foreground before
        // asking for the gesture rather than after.
        _app.Focus();
        Thread.Sleep(300);

        if (!_app.Keys.PressGesture('P', ReviewModifiers.Control))
        {
            return (false, "the palette keystroke was refused");
        }

        Thread.Sleep(900);
        _app.Refresh();

        if (_app.Snapshot().Flatten().All(x => x.AutomationId != "PaletteQuery"))
        {
            return (false, "the palette did not open");
        }

        if (!_app.Keys.Type(command.Label))
        {
            return (false, "the label could not be typed");
        }

        Thread.Sleep(1000);
        _app.Refresh();

        UiaNode? results = _app.Snapshot().Flatten()
            .FirstOrDefault(x => x.AutomationId == "PaletteResults");

        UiaNode[] items =
        [
            .. (results?.Flatten() ?? []).Where(x => x.ControlType == "ListItem" && !x.IsOffscreen),
        ];

        if (items.Length == 0)
        {
            _app.Keys.Press(ReviewKey.Escape);

            return (false, "the palette showed no results for \"" + command.Label + "\"");
        }

        string first = items[0].Name ?? "«unnamed»";

        bool byIdentifier = first.Contains("Id = " + commandId + ",", StringComparison.Ordinal)
            || first.Contains("Id = " + commandId + " ", StringComparison.Ordinal);

        bool byLabel = string.Equals(first, command.Label, StringComparison.Ordinal);

        if (!byIdentifier && !byLabel)
        {
            string shot = Path.Combine(directory, "00-palette.png");

            _app.Capture(shot);
            _app.Keys.Press(ReviewKey.Escape);

            return (false, "the top result was \"" + first + "\", not " + commandId);
        }

        _app.Keys.Press(ReviewKey.Enter);
        Thread.Sleep(600);

        return (true, "ran from the palette, matched by "
            + (byIdentifier ? "identifier" : "label") + " on "
            + items.Length.ToString(CultureInfo.InvariantCulture) + " result(s)");
    }

    private (bool Selected, string Detail) SelectTab(string tabName)
    {
        try
        {
            AutomationElementCollection tabs = _app.Window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));

            foreach (AutomationElement? tab in tabs)
            {
                if (tab is null
                    || !string.Equals(tab.Current.Name, tabName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                    || pattern is not SelectionItemPattern selection)
                {
                    return (false, "the tab exposes no selection pattern");
                }

                selection.Select();
                Thread.Sleep(900);

                bool selected = selection.Current.IsSelected;

                return (selected, selected ? "selected" : "Select() returned but IsSelected is false");
            }

            return (false, "no tab named '" + tabName + "' is in the tree");
        }
        catch (ElementNotAvailableException)
        {
            return (false, "the tab left the tree");
        }
        catch (InvalidOperationException failure)
        {
            return (false, "refused: " + failure.Message);
        }
    }

    private (bool Selected, string Detail) SelectRow(string? name)
    {
        if (name is null)
        {
            return (false, "there was no row to select");
        }

        try
        {
            AutomationElement? row = _app.FindByName(name);

            if (row is null)
            {
                return (false, "'" + name + "' was gone by the time it was selected");
            }

            if (!row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                || pattern is not SelectionItemPattern selection)
            {
                return (false, "'" + name + "' exposes no selection pattern");
            }

            selection.Select();
            Thread.Sleep(700);

            return (selection.Current.IsSelected, selection.Current.IsSelected
                ? "'" + name + "' is selected"
                : "Select() returned but '" + name + "' is not selected");
        }
        catch (ElementNotAvailableException)
        {
            return (false, "'" + name + "' left the tree while being selected");
        }
        catch (InvalidOperationException failure)
        {
            return (false, "refused: " + failure.Message);
        }
    }

    private string Relative(string path) =>
        Path.GetRelativePath(_runDirectory, path).Replace('\\', '/');
}
