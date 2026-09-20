using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That the Deals page cannot go back to asserting a contract does not exist.
/// </summary>
/// <remarks>
/// The blind-handoff retest of build 78 found a green banner on the Deals page
/// reading "No contract has been drafted, signed or executed, and AgencyOS does not
/// track that yet", on a deal whose contract had been executed. The sentence was a
/// literal in the markup, so the guard is on the markup: what it said could not be
/// wrong about a particular deal, because it did not depend on one.
/// </remarks>
public sealed class DealPaperTruthTests
{
    /// <summary>The sentence itself is gone, and cannot come back.</summary>
    [Fact]
    public void ThePageNeverStatesThatNoContractExists()
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

    /// <summary>The page asks the view model what the paper is doing.</summary>
    [Fact]
    public void ThePageRendersTheStandingItIsGiven()
    {
        string code = File.ReadAllText(CodePath);

        Assert.Contains("_detail.ContractStanding", code, StringComparison.Ordinal);
        Assert.Contains("StandingSeverity.Success", code, StringComparison.Ordinal);
        Assert.Contains("StandingSeverity.Warning", code, StringComparison.Ordinal);
    }

    /// <summary>There is a surface for the next action.</summary>
    /// <remarks>
    /// The other half of the retest: an open task existed and no surface said it was
    /// the thing to do next.
    /// </remarks>
    [Fact]
    public void ThePageOffersTheNextAction()
    {
        XElement bar = Bar("NextActionBar");

        Assert.False(string.IsNullOrWhiteSpace(bar.Attribute("AutomationProperties.Name")?.Value));

        Assert.Equal("Next action", bar.Attribute("Title")?.Value);

        string code = File.ReadAllText(CodePath);

        Assert.Contains("_detail.NextAction", code, StringComparison.Ordinal);
    }

    /// <summary>An overdue action is not rendered as ordinary information.</summary>
    /// <remarks>
    /// The wording and severity live in the shared banner, so that both surfaces
    /// offering a next action say it the same way. This checks the one place rather
    /// than each caller.
    /// </remarks>
    [Fact]
    public void AnOverdueActionIsRaised()
    {
        string banner = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Presentation", "NextActionBanner.cs"));

        Assert.Contains("next.IsOverdue", banner, StringComparison.Ordinal);
        Assert.Contains("InfoBarSeverity.Warning", banner, StringComparison.Ordinal);
        Assert.Contains("Next action, overdue", banner, StringComparison.Ordinal);
    }

    /// <summary>Both surfaces that offer an action use the same banner.</summary>
    [Fact]
    public void TheTalentSurfaceOffersTheSameNextAction()
    {
        string talent = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "TalentPage.xaml.cs"));

        Assert.Contains("NextActionBanner.Apply", talent, StringComparison.Ordinal);
        Assert.Contains("NextActionBanner.Apply", File.ReadAllText(CodePath), StringComparison.Ordinal);
    }


    /// <summary>The deal's own state wording never mentions a contract.</summary>
    /// <remarks>
    /// The build-79 blind handoff found this line asserting "no contract recorded"
    /// from deal status alone while the panel beside it said the contract was
    /// executed. Contract truth has one source on this page; this pins that the
    /// deal's line is not a second one.
    /// </remarks>
    [Fact]
    public void TheDealStateWordingSaysNothingAboutPaper()
    {
        string viewModel = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Client", "ViewModels", "DealViewModels.cs"));

        int standing = viewModel.IndexOf("public string Standing", StringComparison.Ordinal);

        Assert.True(standing > 0, "The Standing property was not found.");

        int end = viewModel.IndexOf("public Task LoadAsync", standing, StringComparison.Ordinal);
        string body = viewModel[standing..(end > standing ? end : viewModel.Length)];

        // Comments explain why the wording changed and naturally quote the sentence
        // that was removed. The invariant is about what an operator reads, so the
        // explanation is stripped before asserting on the code.
        string rendered = string.Join(
            Environment.NewLine,
            body.ReplaceLineEndings("\n")
                .Split('\n')
                .Select(line => line.TrimStart())
                .Where(line => !line.StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("contract", rendered, StringComparison.OrdinalIgnoreCase);
    }

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

    /// <summary>Detailed notes reach the person's own surface, not only the desk.</summary>
    /// <remarks>
    /// The build-80 retest could read a call's summary on the person and never learn
    /// what the call was about: the only template rendering detailed notes was the
    /// org-wide Command Center. One shared template now serves both, so the two
    /// cannot drift apart about what a contact shows.
    /// </remarks>
    [Fact]
    public void RecentContactIsOnThePersonSurface()
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

    /// <summary>Detailed notes are reachable from the interaction surface.</summary>
    [Fact]
    public void DetailedNotesAreReachable()
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

    /// <summary>The project attachments list is bound to something.</summary>
    [Fact]
    public void TheAttachmentsTabHasASource()
    {
        string code = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "ProjectsPage.xaml.cs"));

        Assert.Contains("AttachmentList.ItemsSource", code, StringComparison.Ordinal);
    }

    /// <summary>Representation dates are written one way on that surface.</summary>
    [Fact]
    public void RepresentationDatesUseOneFormat()
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
