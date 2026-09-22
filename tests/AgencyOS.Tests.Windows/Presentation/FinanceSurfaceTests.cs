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

    private static string Page() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", "FinancePage.xaml.cs"));

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
