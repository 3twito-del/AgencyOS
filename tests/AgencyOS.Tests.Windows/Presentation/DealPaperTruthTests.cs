using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// Structural guards on the Deals and Talent pages: what their markup and
/// code-behind contain.
/// </summary>
/// <remarks>
/// <para>
/// The blind-handoff retest of build 78 found a green banner on the Deals page
/// reading "No contract has been drafted, signed or executed, and AgencyOS does not
/// track that yet", on a deal whose contract had been executed. The sentence was a
/// literal in the markup, so the first guard is on the markup: what it said could
/// not be wrong about a particular deal, because it did not depend on one.
/// </para>
/// <para>
/// <strong>These read source; they prove that a binding, a call or a literal is
/// or is not there (F-07).</strong> Several were named for what an operator
/// receives - "never states", "is reachable", "offers the same" - and a string in
/// a file is not evidence of any of that. Each is now named for what it checks,
/// and the operator claims live where they can be executed:
/// <c>DealPageTruthTests</c> loads the real deal view model across every deal
/// status and every set of contract statuses; <c>ContractStandingTests</c> and
/// <c>NextActionTests</c> run the decisions the banners render; and
/// <c>PersonContactReachTests</c> reads the projection the Talent page binds.
/// </para>
/// </remarks>
public sealed class DealPaperTruthTests
{
    /// <summary>The retired "no contract" sentence is not in the Deals markup.</summary>
    /// <remarks>
    /// A markup fact, and the right instrument for one: the old sentence was a
    /// literal. That the page says no such thing while a contract exists, whatever
    /// the paper is doing, is proved against the real view model in
    /// <c>DealPageTruthTests.WhileAnyContractIsRecordedThePageNeverSaysThereIsNone</c>.
    /// </remarks>
    [Fact]
    public void TheRetiredNoContractSentenceIsNotInTheDealsMarkup()
    {
        string markup = File.ReadAllText(PagePath);

        Assert.DoesNotContain(
            "No contract has been drafted", markup, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "does not track that yet", markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The paper banner carries no fixed wording or severity.
    /// </summary>
    /// <remarks>
    /// A hardcoded message is how the last one became false: nothing about it could
    /// change when M8 added contracts. Title, message and severity now come from the
    /// view model, so the only way to say something wrong is to compute it wrongly,
    /// which <c>ContractStandingTests</c> covers.
    /// </remarks>
    [Fact]
    public void ThePaperBannerSaysNothingOfItsOwn()
    {
        XElement bar = Bar("ContractStandingBar");

        Assert.Null(bar.Attribute("Message"));
        Assert.Null(bar.Attribute("Title"));
        Assert.Null(bar.Attribute("Severity"));
    }

    /// <summary>The page's code reads the view model's standing and maps its severities.</summary>
    /// <remarks>
    /// What the standing says is decided in <c>ContractStandingFor</c> and read
    /// through the real view model in <c>DealPageTruthTests</c>; this shows only
    /// that the page's code refers to it.
    /// </remarks>
    [Fact]
    public void TheDealsPageCodeReadsTheStandingAndMapsItsSeverities()
    {
        string code = File.ReadAllText(CodePath);

        Assert.Contains("_detail.ContractStanding", code, StringComparison.Ordinal);
        Assert.Contains("StandingSeverity.Success", code, StringComparison.Ordinal);
        Assert.Contains("StandingSeverity.Warning", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Deals page has a named next-action bar, and its code reads the view
    /// model's next action.
    /// </summary>
    /// <remarks>
    /// The other half of the retest: an open task existed and no surface said it was
    /// the thing to do next. Which task is offered is decided by
    /// <c>NextActionFrom</c> and proved in <c>NextActionTests</c>.
    /// </remarks>
    [Fact]
    public void TheDealsPageHasANamedNextActionBarFedFromTheViewModel()
    {
        XElement bar = Bar("NextActionBar");

        Assert.False(string.IsNullOrWhiteSpace(bar.Attribute("AutomationProperties.Name")?.Value));

        Assert.Equal("Next action", bar.Attribute("Title")?.Value);

        string code = File.ReadAllText(CodePath);

        Assert.Contains("_detail.NextAction", code, StringComparison.Ordinal);
    }

    /// <summary>The shared banner's source has an overdue branch that warns.</summary>
    /// <remarks>
    /// The wording and severity live in the shared banner, so that both surfaces
    /// offering a next action say it the same way. The banner writes onto an
    /// <c>InfoBar</c>, which needs a XAML host this suite does not have, so this
    /// reads its source. Whether a task is overdue is decided by
    /// <c>NextActionFrom</c> and proved in <c>NextActionTests</c>.
    /// </remarks>
    [Fact]
    public void TheSharedBannerSourceHasAnOverdueWarningBranch()
    {
        string banner = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Presentation", "NextActionBanner.cs"));

        Assert.Contains("next.IsOverdue", banner, StringComparison.Ordinal);
        Assert.Contains("InfoBarSeverity.Warning", banner, StringComparison.Ordinal);
        Assert.Contains("Next action, overdue", banner, StringComparison.Ordinal);
    }

    /// <summary>The Deals and Talent pages both render through the shared banner.</summary>
    /// <remarks>
    /// One renderer is what makes the two surfaces word an action the same way, and
    /// that is a source fact. It does not show that both pages are handed the same
    /// action.
    /// </remarks>
    [Fact]
    public void TheDealsAndTalentPagesRenderThroughTheSharedNextActionBanner()
    {
        string talent = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.Contains("NextActionBanner.Apply", talent, StringComparison.Ordinal);
        Assert.Contains("NextActionBanner.Apply", File.ReadAllText(CodePath), StringComparison.Ordinal);
    }


    // TheDealStateWordingSaysNothingAboutPaper sliced the source of
    // DealDetailViewModel.Standing and asserted "contract" was absent from it. The
    // real property is now read for every deal status in
    // DealPageTruthTests.TheDealsOwnStateLineNeverMentionsAContract, which proves
    // the same claim by executing it, so the slice was removed rather than kept as
    // a weaker duplicate (F-07).

    /// <summary>The next action says who owns it.</summary>
    /// <remarks>
    /// Three branches, not two. "Unassigned" is a claim about ownership, so the
    /// renderer must reach it from <c>IsAssigned</c>, which is authoritative, and not
    /// from a missing name — which is how build 80 reported assigned work as
    /// unowned. <c>OwnershipSurfaceTruthTests</c> mirrors this clause to assert the
    /// sentences it produces; this keeps the mirror honest by pinning the real one.
    /// </remarks>
    [Fact]
    public void TheNextActionNamesAnOwnerOrSaysThereIsNone()
    {
        string banner = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Presentation", "NextActionBanner.cs"));

        Assert.Contains("Unassigned", banner, StringComparison.Ordinal);
        Assert.Contains("IsAssigned: true", banner, StringComparison.Ordinal);
        Assert.Contains("Assigned, name unavailable", banner, StringComparison.Ordinal);

        // The falsehood: deciding ownership from the name alone.
        Assert.DoesNotContain(
            "next.Assignee is { } who ? $\" {who}.\" : \" Unassigned.\"",
            banner,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The Talent page lists interactions through the shared template, which renders
    /// detailed notes.
    /// </summary>
    /// <remarks>
    /// The build-80 retest could read a call's summary on the person and never learn
    /// what the call was about: the only template rendering detailed notes was the
    /// org-wide Command Center. One shared template now serves both, so the two
    /// cannot drift apart about what a contact shows. That the notes are in what
    /// the list is bound to is proved against PostgreSQL in
    /// <c>PersonContactReachTests</c>.
    /// </remarks>
    [Fact]
    public void TheTalentPageListsInteractionsThroughTheNotesTemplate()
    {
        string shared = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "App.xaml"));

        Assert.Contains("x:Key=\"InteractionTemplate\"", shared, StringComparison.Ordinal);
        Assert.Contains("DetailedNotes", shared, StringComparison.Ordinal);

        string talent = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml"));

        Assert.Contains("InteractionTemplate", talent, StringComparison.Ordinal);

        string code = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.Contains("InteractionList.ItemsSource", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shared template discloses detailed notes behind an expander, and the
    /// Command Center uses it.
    /// </summary>
    [Fact]
    public void TheSharedTemplateDisclosesNotesAndTheCommandCenterUsesIt()
    {
        // The template moved into App.xaml when the person's own surface began
        // rendering contact too. What matters is unchanged: notes disclose behind an
        // expander, and every surface showing an interaction uses the same one.
        string shared = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "App.xaml"));

        Assert.Contains("DetailedNotes", shared, StringComparison.Ordinal);
        Assert.Contains("<Expander", shared, StringComparison.Ordinal);

        string desk = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "CommandCenterPage.xaml"));

        Assert.Contains("InteractionTemplate", desk, StringComparison.Ordinal);
    }

    /// <summary>The project page's code gives the attachments list an items source.</summary>
    [Fact]
    public void TheProjectsPageCodeGivesTheAttachmentsListASource()
    {
        string code = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "ProjectsPage.xaml.cs"));

        Assert.Contains("AttachmentList.ItemsSource", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Talent page formats representation start dates as ISO and binds none raw.
    /// </summary>
    [Fact]
    public void TheTalentPageFormatsStartDatesAsIsoAndBindsNoneRaw()
    {
        string code = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.DoesNotContain("StartsOn:d}", code, StringComparison.Ordinal);
        Assert.Contains("StartsOn:yyyy-MM-dd}", code, StringComparison.Ordinal);

        string markup = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml"));

        Assert.DoesNotContain("Text=\"{Binding StartsOn}\"", markup, StringComparison.Ordinal);
    }

    private static XElement Bar(string name)
    {
        XName xName = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

        return Assert.Single(
            XElement.Load(PagePath).Descendants(),
            x => x.Attribute(xName)?.Value == name);
    }

    // Computed rather than assigned, because a static field initializer runs in
    // declaration order and these would otherwise be built before RepositoryRoot is.
    private static string PagePath =>
        Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "DealsPage.xaml");

    private static string CodePath =>
        Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "DealsPage.xaml.cs");

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
