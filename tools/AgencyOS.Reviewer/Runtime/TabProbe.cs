using System.Globalization;
using System.Text.Json;
using System.Windows.Automation;
using AgencyOS.Client.Commands;
using System.IO;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// Establishes whether a tab can be opened and a row selected on it.
/// </summary>
/// <remarks>
/// <para>
/// Audit 002 Phase B left twelve dialogs blocked and could not say whether the
/// cause was the product or the harness (<c>AOS-R002-009</c>). Every one of them
/// lives on a tabbed page and guards on a loaded detail or a selected row, and
/// the finding asked for one observation to settle it: open the tab, select the
/// record, run the command, and look.
/// </para>
/// <para>
/// This is that observation, made in steps so the answer says <em>which</em> step
/// failed rather than that the dialog did not appear. Each step reports what the
/// automation tree showed, so a negative result is evidence rather than an
/// absence.
/// </para>
/// </remarks>
internal sealed class TabProbe
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ReviewApp _app;
    private readonly string _runDirectory;

    internal TabProbe(ReviewApp app, string runDirectory)
    {
        _app = app;
        _runDirectory = runDirectory;
    }

    /// <summary>Walks one workspace's tab and reports each step.</summary>
    /// <param name="workspaceLabel">Which workspace.</param>
    /// <param name="tabName">Which tab on it.</param>
    /// <param name="commandId">The command to run once a row is selected.</param>
    /// <returns>What each step showed.</returns>
    internal TabProbeResult Probe(string workspaceLabel, string tabName, string commandId)
    {
        string directory = Path.Combine(
            _runDirectory, "evidence", "tabprobe." + tabName.ToLowerInvariant());

        Directory.CreateDirectory(directory);

        List<string> steps = [];

        _app.Focus();
        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(250);
        _app.Resize(1600, 1000);
        Thread.Sleep(500);
        _app.Refresh();

        ReviewStep navigated = _app.Navigate(workspaceLabel);

        steps.Add("navigate to " + workspaceLabel + ": "
            + (navigated.Succeeded ? "ok" : navigated.Detail));

        Thread.Sleep(1600);
        _app.Refresh();

        // 1. Is the tab there, and does selecting it take?
        (bool tabSelected, string tabDetail) = SelectTab(tabName);

        steps.Add("select the " + tabName + " tab: " + tabDetail);

        Thread.Sleep(1600);
        _app.Refresh();

        string afterTab = Path.Combine(directory, "01-tab.png");

        _app.Capture(afterTab);

        UiaNode tree = _app.Snapshot();

        File.WriteAllText(
            Path.Combine(directory, "01-tab.json"), JsonSerializer.Serialize(tree, Json));

        // 2. Did the tab's content arrive?
        // Rows on the page, not in the shell. The navigation pane's destinations
        // are list items and so is the settings item; the first version of this
        // probe selected the settings item and navigated away before the command
        // ran, which made every tab look empty of the thing it was holding.
        UiaNode? content = tree.Flatten()
            .FirstOrDefault(x => x.AutomationId == "ContentHost");

        UiaNode[] rows =
        [
            .. (content ?? tree).Flatten().Where(x =>
                x.ControlType == "ListItem"
                && !string.IsNullOrWhiteSpace(x.Name)
                && !AgencyOsWorkspaces.All.Any(w =>
                    string.Equals(w.Label, x.Name, StringComparison.Ordinal))),
        ];

        steps.Add("rows on the tab: " + rows.Length.ToString(CultureInfo.InvariantCulture)
            + (rows.Length > 0 ? " (first: " + rows[0].Name + ")" : string.Empty));

        // 3. Select one, and confirm the tree says it is selected.
        (bool rowSelected, string rowDetail) = SelectRow(rows.FirstOrDefault()?.Name);

        steps.Add("select a row: " + rowDetail);

        Thread.Sleep(1600);
        _app.Refresh();

        string afterRow = Path.Combine(directory, "02-row.png");

        _app.Capture(afterRow);

        // 4. Run the command and see whether a dialog appears.
        bool ran = RunCommand(commandId);

        steps.Add("run " + commandId + ": " + (ran ? "the palette ran it" : "the palette did not"));

        Thread.Sleep(1200);
        _app.Refresh();

        UiaNode after = _app.Snapshot();

        File.WriteAllText(
            Path.Combine(directory, "03-after.json"), JsonSerializer.Serialize(after, Json));

        string afterCommand = Path.Combine(directory, "03-after.png");

        _app.Capture(afterCommand);

        UiaNode? dialog = after.Flatten().FirstOrDefault(x =>
            x.ControlType == "Window" && x.ClassName == "Popup" && !x.IsOffscreen);

        steps.Add("a dialog appeared: " + (dialog is not null ? "yes — " + (dialog.Name ?? "?") : "no"));

        return new TabProbeResult(
            workspaceLabel,
            tabName,
            commandId,
            tabSelected,
            rows.Length,
            rowSelected,
            ran,
            dialog is not null,
            steps,
            [Relative(afterTab), Relative(afterRow), Relative(afterCommand)]);
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

                // Ask the tree whether it took, rather than assuming.
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
                return (false, "'" + name + "' was not findable");
            }

            if (!row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                || pattern is not SelectionItemPattern selection)
            {
                return (false, "the row exposes no selection pattern");
            }

            selection.Select();
            Thread.Sleep(900);

            bool selected = selection.Current.IsSelected;

            return (selected, selected
                ? "'" + name + "' is selected"
                : "Select() returned but IsSelected is false");
        }
        catch (ElementNotAvailableException)
        {
            return (false, "the row left the tree");
        }
        catch (InvalidOperationException failure)
        {
            return (false, "refused: " + failure.Message);
        }
    }

    private bool RunCommand(string commandId)
    {
        if (CommandRegistry.Default.Find(commandId) is not { } command)
        {
            return false;
        }

        if (!_app.Keys.PressGesture('P', ReviewModifiers.Control))
        {
            return false;
        }

        Thread.Sleep(900);

        if (!_app.Keys.Type(command.Label))
        {
            return false;
        }

        Thread.Sleep(900);
        _app.Keys.Press(ReviewKey.Enter);
        Thread.Sleep(700);

        return true;
    }

    private string Relative(string path) =>
        Path.GetRelativePath(_runDirectory, path).Replace('\\', '/');
}

/// <summary>What the tab probe established.</summary>
/// <param name="Workspace">Which workspace.</param>
/// <param name="Tab">Which tab.</param>
/// <param name="CommandId">The command that was run.</param>
/// <param name="TabSelected">Whether the tab actually became the selected one.</param>
/// <param name="RowsOnTab">How many rows the tab's content showed.</param>
/// <param name="RowSelected">Whether a row actually became selected.</param>
/// <param name="CommandRan">Whether the palette ran the command.</param>
/// <param name="DialogAppeared">Whether a dialog followed.</param>
/// <param name="Steps">What each step reported, in order.</param>
/// <param name="Screenshots">Captures, relative to the run directory.</param>
public sealed record TabProbeResult(
    string Workspace,
    string Tab,
    string CommandId,
    bool TabSelected,
    int RowsOnTab,
    bool RowSelected,
    bool CommandRan,
    bool DialogAppeared,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Screenshots);
