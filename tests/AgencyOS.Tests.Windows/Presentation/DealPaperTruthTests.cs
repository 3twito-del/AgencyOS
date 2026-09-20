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
