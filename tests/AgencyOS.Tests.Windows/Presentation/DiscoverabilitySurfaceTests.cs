using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That every repaired return path is actually reachable, and reachable by keyboard.
/// </summary>
/// <remarks>
/// <para>
/// F-17 and the entity-to-intelligence gap. Whether the right records are listed
/// is decided against PostgreSQL in <c>DiscoverabilityTests</c> and in the live
/// journeys; these prove the rows exist, carry a handler, and are invokable
/// rather than merely selectable - the F-07 failure applied to navigation, where
/// a correct destination is reached by nothing.
/// </para>
/// <para>
/// <strong>Invokable is the accessibility claim.</strong> A WinUI list row with
/// <c>IsItemClickEnabled</c> carries the Invoke pattern, so a screen reader
/// announces it as something that can be activated and Enter activates it. A row
/// wired through selection alone would be a mouse-shaped affordance that says
/// nothing about being a way out of the page.
/// </para>
/// </remarks>
public sealed class DiscoverabilitySurfaceTests
{
    /// <summary>
    /// Every list added as a return path is invokable and has a handler.
    /// </summary>
    /// <remarks>
    /// A project's commercial work, the intelligence that names a person or a
    /// company, and the desk's report that research is overdue. Each was a
    /// statement with no way to act on it.
    /// </remarks>
    [Theory]
    [InlineData("ProjectsPage", "PursuitList", "OnPursuitInvoked")]
    [InlineData("ProjectsPage", "ProjectDealList", "OnDealInvoked")]
    [InlineData("ProjectsPage", "ProjectContractList", "OnContractInvoked")]
    [InlineData("PeoplePage", "PersonIntelligenceList", "OnIntelligenceInvoked")]
    [InlineData("CompaniesPage", "CompanyIntelligenceList", "OnIntelligenceInvoked")]
    [InlineData("IntelligencePage", "OverdueResearchList", "OnOverdueResearchInvoked")]
    public void AReturnPathIsInvokableAndHandled(string page, string list, string handler)
    {
        XElement rows = Assert.Single(
            XDocument.Parse(Page(page)).Descendants(),
            x => (string?)x.Attribute(Name) == list);

        Assert.Equal("True", (string?)rows.Attribute("IsItemClickEnabled"));
        Assert.Equal(handler, (string?)rows.Attribute("ItemClick"));
    }

    /// <summary>Every one of them says what it is, when it is read aloud.</summary>
    /// <remarks>
    /// A list that announces nothing is a list a screen-reader operator cannot
    /// tell apart from the one above it, which would make the return path
    /// discoverable only by sight.
    /// </remarks>
    [Theory]
    [InlineData("ProjectsPage", "PursuitList")]
    [InlineData("ProjectsPage", "ProjectDealList")]
    [InlineData("ProjectsPage", "ProjectContractList")]
    [InlineData("PeoplePage", "PersonIntelligenceList")]
    [InlineData("CompaniesPage", "CompanyIntelligenceList")]
    [InlineData("IntelligencePage", "OverdueResearchList")]
    public void AReturnPathAnnouncesWhatItIs(string page, string list)
    {
        XElement rows = Assert.Single(
            XDocument.Parse(Page(page)).Descendants(),
            x => (string?)x.Attribute(Name) == list);

        string? announced = (string?)rows.Attribute("AutomationProperties.Name");

        Assert.False(string.IsNullOrWhiteSpace(announced));
    }

    /// <summary>
    /// A commercial row keeps the role discipline the rest of the product uses.
    /// </summary>
    /// <remarks>
    /// A navigation label asserts a relationship. The counterparty on a deal or a
    /// contract row reached from a project is the same claim it is anywhere else,
    /// so it goes through the same vocabulary rather than being rendered bare
    /// here (wave 8).
    /// </remarks>
    [Theory]
    [InlineData("ProjectDealList")]
    [InlineData("ProjectContractList")]
    public void ACommercialRowAttributesItsCounterparty(string list)
    {
        XElement rows = Assert.Single(
            XDocument.Parse(Page("ProjectsPage")).Descendants(),
            x => (string?)x.Attribute(Name) == list);

        Assert.Contains(
            "ConverterParameter=CounterpartyDisplayName",
            rows.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Pipeline no longer describes its search as name and description only.
    /// </summary>
    /// <remarks>
    /// Placeholder text is a claim about what a box does. It said "Name or
    /// description" while the query had just been taught the people and companies
    /// the pursuit concerns, which would have left an operator not trying the one
    /// thing that now works (F-06).
    /// </remarks>
    [Fact]
    public void ThePipelineSearchSaysItFindsPeople()
    {
        XElement box = Assert.Single(
            XDocument.Parse(Page("PipelinePage")).Descendants(),
            x => (string?)x.Attribute(Name) == "SearchBox");

        string? placeholder = (string?)box.Attribute("PlaceholderText");

        Assert.NotNull(placeholder);
        Assert.Contains("person", placeholder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("description", placeholder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The destinations can be opened on one record.
    /// </summary>
    /// <remarks>
    /// Without this the row would navigate to a workspace and leave the operator
    /// to find the record again, which is the search they were trying to avoid.
    /// </remarks>
    [Theory]
    [InlineData("PipelinePage")]
    [InlineData("DealsPage")]
    [InlineData("ContractsPage")]
    [InlineData("IntelligencePage")]
    public void ADestinationCanBeOpenedOnOneRecord(string page)
    {
        string source = File.ReadAllText(
            Path.Combine(Pages, $"{page}.xaml.cs"));

        Assert.Matches(new Regex(@"class \w+ : Page, IPaletteCommandTarget, IRecordTarget"), source);
        Assert.Contains("SelectRevealed", source, StringComparison.Ordinal);
    }

    private static XNamespace X => "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XName Name => X + "Name";

    private static string Pages =>
        Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages");

    private static string Page(string page) =>
        File.ReadAllText(Path.Combine(Pages, $"{page}.xaml"));

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
