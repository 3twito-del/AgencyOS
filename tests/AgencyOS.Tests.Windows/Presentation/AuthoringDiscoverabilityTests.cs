using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgencyOS.Client.Commands;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That an ordinary authoring capability can be found without knowing the palette.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-003</c>, decided by the owner: the command palette stays a valid
/// accelerator, but it must not be the only way an operator discovers a normal
/// authoring workflow. The Intelligence workspace had **one** click handler in the
/// whole page, and it was for something else — thirteen authoring dialogs reachable
/// only by typing into the palette.
/// </para>
/// <para>
/// The rule is not "every dialog needs a button". It is that every ordinary
/// authoring command has both a palette route and a discoverable in-workspace one.
/// </para>
/// </remarks>
public sealed class AuthoringDiscoverabilityTests
{
    /// <summary>
    /// Every Intelligence authoring command is reachable from the workspace.
    /// </summary>
    /// <remarks>
    /// The completeness mechanism. The launcher is built from the registry, so this
    /// asserts the registry is what the launcher reads — a fourteenth command added
    /// later is surfaced by construction, and this fails if that ever stops being
    /// true.
    /// </remarks>
    [Fact]
    public void EveryIntelligenceAuthoringCommandIsReachableFromTheWorkspace()
    {
        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "IntelligencePage.xaml.cs"));

        // The launcher reads the registry rather than a written-out list.
        Assert.Contains("CommandRegistry.Default.Commands", page, StringComparison.Ordinal);
        Assert.Contains("\"intelligence\"", page, StringComparison.Ordinal);
        Assert.Contains("CommandActionKind.Invoke", page, StringComparison.Ordinal);
        Assert.Contains("AuthorMenu.Items.Add(item)", page, StringComparison.Ordinal);

        // Authoring only: a menu called New does not offer to open a tab.
        Assert.Contains("!x.Id.StartsWith(\"go.\"", page, StringComparison.Ordinal);

        // And every one of them is answered by the page it launches from.
        List<string> unanswered = [];

        foreach (CommandDefinition command in Authoring())
        {
            if (!page.Contains($"case \"{command.Id}\":", StringComparison.Ordinal))
            {
                unanswered.Add(command.Id);
            }
        }

        Assert.True(
            unanswered.Count == 0,
            "The launcher would offer commands the workspace does not answer: "
                + string.Join(", ", unanswered));
    }

    /// <summary>There is a visible launcher, and it says what it is.</summary>
    [Fact]
    public void TheWorkspaceHasAVisibleNamedLauncher()
    {
        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        XElement launcher = Assert.Single(
            XElement.Load(Path.Combine(
                RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "IntelligencePage.xaml"))
                .Descendants(),
            x => x.Attribute(name)?.Value == "AuthorButton");

        Assert.Equal("DropDownButton", launcher.Name.LocalName);
        Assert.False(string.IsNullOrWhiteSpace(launcher.Attribute("Content")?.Value));
        Assert.False(string.IsNullOrWhiteSpace(
            launcher.Attribute("AutomationProperties.Name")?.Value));
    }

    /// <summary>The menu announces titles, never command records.</summary>
    /// <remarks>
    /// The same property <c>AOS-R002-018</c> is about, one surface along: an entry
    /// whose accessible name were the definition would be unusable to somebody
    /// listening rather than looking.
    /// </remarks>
    [Fact]
    public void TheMenuAnnouncesHumanTitles()
    {
        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "IntelligencePage.xaml.cs"));

        Assert.Contains("Text = command.Label", page, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(item, command.Label)", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The launcher dispatches the same command, not a second implementation.
    /// </summary>
    /// <remarks>
    /// Two code paths to one capability is how they drift. Both the palette and the
    /// launcher go through <c>Execute</c> with the same identifier.
    /// </remarks>
    [Fact]
    public void TheLauncherDispatchesTheSameCommand()
    {
        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "IntelligencePage.xaml.cs"));

        Assert.Contains("item.Click += (_, _) => Execute(id);", page, StringComparison.Ordinal);
    }

    /// <summary>The Finance invoice command has a button like its siblings.</summary>
    /// <remarks>
    /// The one non-Intelligence case: every other authoring command on that page
    /// had a control and this one had only the palette.
    /// </remarks>
    [Fact]
    public void RecordingAnInvoiceHasAButton()
    {
        XName name = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        XElement button = Assert.Single(
            XElement.Load(Path.Combine(
                RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "FinancePage.xaml")).Descendants(),
            x => x.Attribute(name)?.Value == "RecordInvoiceButton");

        Assert.Equal("Record invoice", button.Attribute("Content")?.Value);
        Assert.Equal("OnRecordInvoiceClick", button.Attribute("Click")?.Value);
    }

    // A product-wide "every answered command has a visible opener" rule was
    // attempted here and withdrawn: following a click handler to the command it
    // dispatches needs real call-graph analysis, and the approximation reported
    // pages that plainly have buttons. A guard that cries wolf is worse than none,
    // and the completeness this decision asked for is the Intelligence one above.

    private static IEnumerable<CommandDefinition> Authoring() =>
        CommandRegistry.Default.Commands.Where(x =>
            x.Action == CommandActionKind.Invoke
            && string.Equals(x.Workspace, "intelligence", StringComparison.Ordinal)
            && !x.Id.StartsWith("go.", StringComparison.Ordinal));

    private static string RepositoryRoot { get; } = Find();

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
