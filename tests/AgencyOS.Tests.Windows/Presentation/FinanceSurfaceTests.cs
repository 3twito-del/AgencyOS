using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That the Finance page composes its money statements the way they were tested.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This proves reachability, not meaning.</strong> What a money summary is
/// allowed to say is decided in <c>MoneyAuthorityTests</c>, which drives the real
/// view models through failure, empty, data and retry and composes the page's four
/// operator-visible outputs together. A source string cannot show that a screen
/// contradicts itself.
/// </para>
/// <para>
/// What it does show is that the page still routes every money statement through
/// the tested primitive. Without it the unit test is a mirror of a page that could
/// quietly stop matching — which is the failure mode catalogued as F-07.
/// </para>
/// </remarks>
public sealed class FinanceSurfaceTests
{
    /// <summary>Every summary that states a count, a total or money.</summary>
    public static TheoryData<string> MoneySummaries =>
    [
        "SummaryText",
        "ReceivableSummary",
        "InvoiceSummary",
        "PaymentSummary",
        "CommissionSummary",
        "LedgerSummary",
    ];

    /// <summary>
    /// No money summary is assigned a sentence the page built unconditionally.
    /// </summary>
    [Theory]
    [MemberData(nameof(MoneySummaries))]
    public void EveryMoneySummaryAsksWhetherItsPopulationIsKnown(string summary)
    {
        string page = Page();

        Assert.Contains(
            summary + ".Text = SummaryAuthority.Of(",
            page,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            summary + ".Text = string.Create(",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The empty notice waits on the same authority the totals do.
    /// </summary>
    /// <remarks>
    /// It asserts an absence, so it is subject to the same rule. A book that loaded
    /// empty and then failed to refresh is still empty and no longer known, and the
    /// notice would otherwise open beside the error bar — which is the contradiction
    /// the whole-surface case caught.
    /// </remarks>
    [Fact]
    public void TheEmptyNoticeWaitsOnTheSameAuthority()
    {
        Assert.Contains(
            "ReceivableEmpty.IsOpen = SummaryAuthority.Knows(_receivables)",
            Page(),
            StringComparison.Ordinal);
    }

    /// <summary>Every page whose operational counts were gated, and the control.</summary>
    public static TheoryData<string, string> OperationalCounts => new()
    {
        { "PipelinePage.xaml.cs", "SummaryText" },
        { "ContractsPage.xaml.cs", "SummaryText" },
        { "DealsPage.xaml.cs", "SummaryText" },
    };

    /// <summary>
    /// Every deadline count asks whether its slate is known.
    /// </summary>
    /// <remarks>
    /// The HIGH instances of F-01: a failed load said "0 overdue" and an operator
    /// stopped looking for late work. What they are allowed to say is asserted in
    /// <c>OperationalAuthorityTests</c>, against the real view models.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OperationalCounts))]
    public void EveryDeadlineCountAsksWhetherItsSlateIsKnown(string page, string control)
    {
        string source = Read(page);

        Assert.Contains(control + ".Text = SummaryAuthority.Of(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(control + ".Text = string.Create(", source, StringComparison.Ordinal);
        Assert.Contains("ListEmpty.IsOpen = SummaryAuthority.Knows(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The desk does not claim quiet before it has read.
    /// </summary>
    /// <remarks>
    /// A boolean rather than a total, so it uses the shared authority test with its
    /// own wording rather than <c>Of</c>'s "Totals unavailable."
    /// </remarks>
    [Fact]
    public void TheDeskClaimsQuietOnlyWhenItHasRead()
    {
        string source = Read("CommunicationsPage.xaml.cs");

        Assert.Contains("!SummaryAuthority.Knows(_desk)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SummaryText.Text = _desk.NeedsAttention", source, StringComparison.Ordinal);
    }

    private static string Read(string page) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", page));

    private static string Page() => Read("FinancePage.xaml.cs");

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
