using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

// SOURCE-PROOF: Structural guards on the People and Contracts pages for the C10
// blind failures. The operator claims are executed against the real view models in
// PeopleSearchAbsenceTests and ReconciliationReachTests (AgencyOS.Tests.Unit).

/// <summary>
/// Structural guards for BF-01 and BF-02: which view-model answer each surface reads.
/// </summary>
/// <remarks>
/// These read source, and prove only that a page binds the decision the unit tests
/// execute. That is the gap C10 exposed in both places: the decision the page read
/// was not the one that could tell the states apart.
/// </remarks>
public sealed class C10BlindFailureSurfaceTests
{
    private static readonly XName XamlName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

    /// <summary>The People list has a no-match statement distinct from the empty directory.</summary>
    [Fact]
    public void ThePeopleListStatesANoMatchSearchApartFromAnEmptyDirectory()
    {
        XElement empty = Bar("PeoplePage.xaml", "ListEmpty");
        XElement noMatch = Bar("PeoplePage.xaml", "ListNoMatches");

        Assert.Equal("No people yet", empty.Attribute("Title")?.Value);
        Assert.Equal("No people match this search", noMatch.Attribute("Title")?.Value);

        string said = noMatch.Attribute("Title")!.Value + " " + noMatch.Attribute("Message")!.Value;

        Assert.DoesNotContain("yet", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("first person", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Create", said, StringComparison.Ordinal);
    }

    /// <summary>Each People absence statement opens from its own view-model answer.</summary>
    [Fact]
    public void ThePeopleListOpensEachAbsenceStatementFromItsOwnAnswer()
    {
        string code = Code("PeoplePage.xaml.cs");

        Assert.Contains("ListEmpty.IsOpen = _list.IsEmpty;", code, StringComparison.Ordinal);
        Assert.Contains("ListNoMatches.IsOpen = _list.HasNoMatches;", code, StringComparison.Ordinal);
    }

    /// <summary>The Reconcile gate reads the view model's decision, not visible term rows.</summary>
    [Fact]
    public void TheReconcileGateReadsTheViewModelsDecision()
    {
        string gate = Assert.Single(
            Code("ContractsPage.xaml.cs").Split('\n'),
            x => x.Contains("ReconcileButton.IsEnabled", StringComparison.Ordinal)).Trim();

        Assert.Equal("ReconcileButton.IsEnabled = loaded && _detail.CanReconcile;", gate);
    }

    /// <summary>
    /// The command that opens the draft comparison says, before it is invoked, that it
    /// only compares and changes nothing.
    /// </summary>
    /// <remarks>
    /// The affected blind rerun on 967e8e4 reached the enabled command, labelled
    /// "Reconcile", and declined to press it under a read-only instruction: the word
    /// reads as an act that records something, and nothing on the command said
    /// otherwise. The comparison it opens is a read (BF-02).
    /// </remarks>
    [Fact]
    public void TheDraftComparisonCommandSaysItOnlyCompares()
    {
        XElement command = Bar("ContractsPage.xaml", "ReconcileButton");

        Assert.Equal("Review differences", command.Attribute("Label")?.Value);
        Assert.Equal("OnReconcileClick", command.Attribute("Click")?.Value);

        string help = command.Attribute("AutomationProperties.HelpText")?.Value ?? string.Empty;

        Assert.Equal(ReadOnlyComparison, help);
        Assert.Equal(help, command.Attribute("ToolTipService.ToolTip")?.Value);
    }

    /// <summary>The comparison surface says the same before and after it is used.</summary>
    [Fact]
    public void TheComparisonSurfaceSaysItChangesNothing()
    {
        XElement caption = Bar("ContractsPage.xaml", "ReconcileCaption");

        Assert.Equal(ComparisonPrompt, caption.Attribute("Text")?.Value);
        Assert.Contains($"? \"{ComparisonPrompt}\"", Code("ContractsPage.xaml.cs"), StringComparison.Ordinal);

        // The caption gives way to the result once a comparison is shown; the tab's
        // standing note stays, so the statement is on screen after activation too.
        XElement note = Bar("ContractsPage.xaml", "ComparisonNote");

        Assert.Equal("True", note.Attribute("IsOpen")?.Value);
        Assert.Contains("read-only comparison and does not change the contract", note.Attribute("Message")?.Value, StringComparison.Ordinal);
    }

    private const string ReadOnlyComparison =
        "Read-only comparison. Shows how this draft differs from the agreed terms. Does not change the contract.";

    private const string ComparisonPrompt =
        "Review differences to compare the draft with the agreed terms. This comparison does not change the contract.";

    private static XElement Bar(string page, string name) =>
        Assert.Single(
            XElement.Load(Path.Combine(Pages, page)).Descendants(),
            x => x.Attribute(XamlName)?.Value == name);

    private static string Code(string file) => File.ReadAllText(Path.Combine(Pages, file));

    private static string Pages => Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages");

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
