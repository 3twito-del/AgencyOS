using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That no money row shows a number without saying what it is denominated in.
/// </summary>
/// <remarks>
/// <para>
/// F-11. Every Finance row bound <c>Something.Amount</c> — the raw decimal out of
/// <c>MoneyResponse</c>, discarding <c>Currency</c>. Reality Closure recorded it
/// live: a receivable row reading <c>240000.0000 | 90000.0000 | 150000.0000</c>
/// two lines beneath a summary reading <c>Outstanding: 140,000.00 GBP
/// 1,255,000.00 USD</c>. The summaries had always used <c>MoneyFormatting</c>;
/// the rows never did.
/// </para>
/// <para>
/// <strong>This proves reachability, not meaning.</strong> What a row announces is
/// decided in <c>ValueParityTests</c>, which executes the shipped formatter
/// against the shipped response types. This shows the page still routes its money
/// through the tested converter, which is the F-07 failure applied to a binding.
/// </para>
/// </remarks>
public sealed class MoneySurfaceTests
{
    /// <summary>
    /// No row binds a bare amount, except where its row states the currency once.
    /// </summary>
    /// <remarks>
    /// The ledger's account rows carry a <c>Currency</c> column of their own, so
    /// all three of their figures are already denominated and repeating the code
    /// on each would be noise rather than repair. Every other money row had
    /// nothing to say which currency it was in.
    /// </remarks>
    [Fact]
    public void NoMoneyRowBindsABareAmountWithoutStatingItsCurrency()
    {
        string markup = Page("FinancePage");
        XDocument document = XDocument.Parse(markup);

        List<string> bare = [];

        foreach (XElement list in document.Descendants()
            .Where(x => x.Name.LocalName == "ListView"))
        {
            string name = (string?)list.Attribute(Name) ?? "«unnamed»";
            string inner = list.ToString();

            if (!Regex.IsMatch(inner, @"Binding \w+\.Amount\}"))
            {
                continue;
            }

            // A row that states its currency once is already complete.
            if (Regex.IsMatch(inner, @"Binding Currency\}"))
            {
                continue;
            }

            bare.Add(name);
        }

        Assert.True(
            bare.Count == 0,
            "These rows show a number with nothing to say what it is denominated in: "
                + string.Join(", ", bare));
    }

    /// <summary>Every repaired figure goes through the tested converter.</summary>
    [Theory]
    [InlineData("ReceivableList", 3)]
    [InlineData("InvoiceList", 2)]
    [InlineData("PaymentList", 3)]
    [InlineData("CommissionList", 3)]
    [InlineData("JournalList", 2)]
    [InlineData("ReconcileList", 1)]
    public void EveryRepairedFigureGoesThroughTheMoneyConverter(string list, int figures)
    {
        XElement rows = Assert.Single(
            XDocument.Parse(Page("FinancePage")).Descendants(),
            x => (string?)x.Attribute(Name) == list);

        int converted = Regex.Matches(
            rows.ToString(),
            @"Converter=\{StaticResource Money\}").Count;

        Assert.Equal(figures, converted);
    }

    /// <summary>
    /// The ledger's account rows are deliberately left as they were.
    /// </summary>
    /// <remarks>
    /// Recorded so a later sweep does not "fix" them into saying the currency four
    /// times in one row.
    /// </remarks>
    [Fact]
    public void TheLedgerAccountRowStatesItsCurrencyOnce()
    {
        XElement rows = Assert.Single(
            XDocument.Parse(Page("FinancePage")).Descendants(),
            x => (string?)x.Attribute(Name) == "BalanceList");

        string inner = rows.ToString();

        Assert.Contains("Binding Currency}", inner, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(inner, @"Binding \w+\.Amount\}").Count);
        Assert.DoesNotContain("StaticResource Money", inner, StringComparison.Ordinal);
    }

    /// <summary>
    /// A term row shows the value the server wrote, which is what it announces.
    /// </summary>
    /// <remarks>
    /// The visible column and <c>RowLabel</c> read one field, so the two channels
    /// cannot drift into formatting the same term differently.
    /// </remarks>
    [Theory]
    [InlineData("DealsPage")]
    [InlineData("ContractsPage")]
    public void ATermRowShowsTheValueItAnnounces(string page)
    {
        XElement terms = Assert.Single(
            XDocument.Parse(Page(page)).Descendants(),
            x => (string?)x.Attribute(Name) == "TermList");

        string inner = terms.ToString();

        Assert.Contains("Binding DisplayValue}", inner, StringComparison.Ordinal);
        Assert.Contains("StaticResource RowLabel", inner, StringComparison.Ordinal);
    }

    private static XNamespace X => "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XName Name => X + "Name";

    private static string Page(string page) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", $"{page}.xaml"));

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
