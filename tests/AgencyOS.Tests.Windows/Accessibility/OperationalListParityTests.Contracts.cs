using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using Xunit;

namespace AgencyOS.Tests.Windows.Accessibility;

/// <summary>
/// The two accessibility findings of the release candidate at <c>ca6ee24</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A long contract title took the row's compact facts with it.</strong> The
/// Contracts list showed a long title, "Counterparty: A24", "Executed" and "Long form";
/// its announcement ended "…first negotiation on sequels, Counterparty:…". The row
/// was unprofiled, so the inferred label shortened the whole sentence and the title,
/// coming first, spent the budget. Owner decision D5: the 160-character target does
/// not outrank a primary fact. The Projects workspace lists the same summaries through
/// the same mechanism, showing title, counterparty and status.
/// </para>
/// <para>
/// <strong>A payment's amount had no role.</strong> Narrator said "240,000.00 GBP"
/// beside "90,000.00 GBP allocated" and "150,000.00 GBP unapplied": the other two
/// money facts said what they were, the first did not.
/// </para>
/// <para>
/// These run each row through its own template's announcement and help text. Live
/// proof is for the next release candidate.
/// </para>
/// </remarks>
public sealed partial class OperationalListParityTests
{
    /// <summary>The live title, long enough to spend the whole budget by itself.</summary>
    private const string LongContractTitle =
        "Autumn slate lead role - long-form performer services agreement with A24 including rider, "
            + "loan-out undertaking and first negotiation on sequels";

    private static ContractSummaryResponse ContractSummary(string title = LongContractTitle) =>
        new(
            Guid.CreateVersion7(),
            title,
            "LEGAL-REF-7731",
            "LongForm",
            "Executed",
            Guid.CreateVersion7(),
            "Hidden deal name",
            Guid.CreateVersion7(),
            "Hidden subject",
            "A24",
            Guid.CreateVersion7(),
            "Hidden owner",
            new DateOnly(2026, 9, 22),
            null,
            null,
            false,
            3,
            2,
            "Hidden version label",
            Guid.CreateVersion7(),
            0,
            2,
            0,
            0,
            1,
            0,
            new DateOnly(2026, 10, 15),
            "Hidden deadline",
            1,
            new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero),
            9);

    private static string ContractSaid(string template, ContractSummaryResponse row) =>
        InEnglish(() => Announce(Find(template == "ContractList" ? "Pages/ContractsPage.xaml" : "Pages/ProjectsPage.xaml", template), row));

    private static void AssertNothingHiddenIsSaid(string said)
    {
        foreach (string hidden in (string[])
            [
                "LEGAL-REF-7731", "Hidden deal name", "Hidden subject", "Hidden owner", "Hidden version label",
                "Hidden deadline", "2026-09-22", "2026-10-15",
            ])
        {
            Assert.DoesNotContain(hidden, said, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The Contracts list keeps counterparty, status and kind whole under a long title,
    /// which yields and is offered whole on the same row.
    /// </summary>
    [Fact]
    public void ALongContractTitleCostsTheContractsRowNoCompactFact()
    {
        ContractSummaryResponse row = ContractSummary();
        string said = ContractSaid("ContractList", row);
        string[] segments = said.Split(", ");

        Assert.Contains("Counterparty: A24", segments);
        Assert.Contains("Executed", segments);
        Assert.Contains("Long form", segments);
        Fragment(said, "Contract: ", LongContractTitle);
        Assert.DoesNotContain(LongContractTitle, said, StringComparison.Ordinal);

        // The shape, produced by the mechanism: only the title yields.
        Assert.Equal(
            "Contract: Autumn slate lead role - long-form performer services agreement with A24 including rider, "
                + "loan-out…, Counterparty: A24, Executed, Long form",
            said);
        Assert.Equal(149, said.Length);

        // The whole title, on the same row.
        Assert.Equal(LongContractTitle, HelpText(Template(Find("Pages/ContractsPage.xaml", "ContractList")), row));

        AssertNothingHiddenIsSaid(said);
    }

    /// <summary>
    /// The Projects workspace's contract list is protected the same way, and says only
    /// what it shows: no kind.
    /// </summary>
    [Fact]
    public void ALongContractTitleCostsTheProjectContractRowNoCompactFact()
    {
        ContractSummaryResponse row = ContractSummary();
        string said = ContractSaid("ProjectContractList", row);
        string[] segments = said.Split(", ");

        Assert.Contains("Counterparty: A24", segments);
        Assert.Contains("Executed", segments);
        Fragment(said, "Contract: ", LongContractTitle);
        Assert.DoesNotContain(LongContractTitle, said, StringComparison.Ordinal);

        // That template shows no kind, so the row does not say one.
        Assert.DoesNotContain("Long form", said, StringComparison.Ordinal);
        Assert.DoesNotContain("LongForm", said, StringComparison.Ordinal);

        Assert.Equal(
            "Contract: Autumn slate lead role - long-form performer services agreement with A24 including rider, "
                + "loan-out undertaking and first…, Counterparty: A24, Executed",
            said);
        Assert.Equal(160, said.Length);

        Assert.Equal(LongContractTitle, HelpText(Template(Find("Pages/ProjectsPage.xaml", "ProjectContractList")), row));

        AssertNothingHiddenIsSaid(said);
    }

    /// <summary>A contract whose title fits is said whole, within 160.</summary>
    [Fact]
    public void AShortContractsRowTitleIsSaidWhole()
    {
        string said = ContractSaid("ContractList", ContractSummary("Northgate - writer agreement"));

        Assert.Equal("Contract: Northgate - writer agreement, Counterparty: A24, Executed, Long form", said);
    }

    /// <summary>
    /// Every money fact on a payment says what it is: the amount, what is allocated,
    /// what is unapplied, each with its currency.
    /// </summary>
    [Fact]
    public void EveryPaymentAmountSaysItsRole()
    {
        string said = PaymentSaid(Payment());

        Assert.True(SaysInRole(said, "240,000.00 GBP", "amount"), said);
        Assert.True(SaysInRole(said, "90,000.00 GBP", "allocated"), said);
        Assert.True(SaysInRole(said, "150,000.00 GBP", "unapplied"), said);

        // No money fact is said bare.
        Assert.DoesNotContain(
            said.Split(", "),
            x => x.EndsWith(" GBP", StringComparison.Ordinal));
    }

    /// <summary>Swapping any two payment amounts fails: each is bound to its role.</summary>
    [Fact]
    public void SwappingAnyTwoPaymentAmountsFails()
    {
        PaymentResponse row = Payment();
        string said = PaymentSaid(row);
        (string Money, string Role)[] facts = [("240,000.00 GBP", "amount"), ("90,000.00 GBP", "allocated"), ("150,000.00 GBP", "unapplied")];

        Assert.All(facts, x => Assert.True(SaysInRole(said, x.Money, x.Role), said));

        PaymentResponse swapped = row with { Amount = row.Allocated, Allocated = row.Amount };
        string swappedSaid = PaymentSaid(swapped);

        Assert.False(SaysInRole(swappedSaid, "240,000.00 GBP", "amount"), swappedSaid);
        Assert.True(SaysInRole(swappedSaid, "240,000.00 GBP", "allocated"), swappedSaid);
    }
}
