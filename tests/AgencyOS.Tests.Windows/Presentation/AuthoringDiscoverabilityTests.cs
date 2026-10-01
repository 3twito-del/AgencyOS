using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgencyOS.Client.Commands;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: The registry and its labels are executed. The launcher's
// construction and the page's command cases are read from IntelligencePage's code-
// behind, which cannot be constructed here.

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
    /// The launcher's code builds its menu from the registry, and the page answers
    /// every Intelligence authoring command the registry holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The completeness mechanism. The launcher is built from the registry, so this
    /// asserts the registry is what the launcher reads — a fourteenth command added
    /// later is surfaced by construction, and this fails if that ever stops being
    /// true.
    /// </para>
    /// <para>
    /// Half executed, half read (F-07). The set of authoring commands is taken from
    /// the real registry; that the page builds its menu from it and has a case for
    /// each is read from the page's source, because page code-behind cannot be
    /// constructed here. It was named "reachable", which is a navigation claim this
    /// cannot make; it is named for the two facts it checks.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLauncherIsBuiltFromTheRegistryAndThePageAnswersEveryAuthoringCommand()
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

    /// <summary>
    /// Each menu entry's text and accessible name are set from its command's label.
    /// </summary>
    /// <remarks>
    /// The same property <c>AOS-R002-018</c> is about, one surface along: an entry
    /// whose accessible name were the definition would be unusable to somebody
    /// listening rather than looking. This is the wiring, read from source. What
    /// the label actually says is executed in
    /// <see cref="EveryIntelligenceAuthoringLabelIsATitleAndNotAnIdentifier"/>;
    /// together they are the claim this test used to make alone (F-07).
    /// </remarks>
    [Fact]
    public void EachMenuEntryIsWrittenAndNamedFromItsCommandLabel()
    {
        string page = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "IntelligencePage.xaml.cs"));

        Assert.Contains("Text = command.Label", page, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(item, command.Label)", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every Intelligence authoring label is a title a person reads, not an identifier.
    /// </summary>
    /// <remarks>
    /// Executed against the real registry. A label that were empty, equal to the
    /// command's id, dotted like one, or the record's own <c>ToString()</c> would be
    /// announced exactly as written, because the entry's accessible name is the
    /// label.
    /// </remarks>
    [Fact]
    public void EveryIntelligenceAuthoringLabelIsATitleAndNotAnIdentifier()
    {
        List<CommandDefinition> commands = [.. Authoring()];

        Assert.NotEmpty(commands);

        foreach (CommandDefinition command in commands)
        {
            string label = command.Label;

            Assert.False(string.IsNullOrWhiteSpace(label), $"{command.Id} has no label.");
            Assert.NotEqual(command.Id, label);
            Assert.DoesNotContain(".", label.TrimEnd('.', '…'), StringComparison.Ordinal);
            Assert.DoesNotContain("CommandDefinition", label, StringComparison.Ordinal);
            Assert.Contains(' ', label);
            Assert.True(char.IsUpper(label[0]), $"'{label}' does not read as a title.");
        }
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
