using AgencyOS.Client.Presentation;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Documents;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Representation;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// A row announces the business value a sighted operator reads from it.
/// </summary>
/// <remarks>
/// <para>
/// F-02. <c>RowLabel</c> selected from three vocabularies — names, states and
/// related names — and had no entry for a value of any kind. Every quantitative
/// row in the product therefore announced <em>what it is</em> and <em>what state
/// it is in</em>, and never <em>how much</em>. A screen-reader operator could scan
/// the agreed terms of a negotiation and hear <c>Fee</c>, <c>Term</c>,
/// <c>Territory</c> without a single number, and scan receivables without an
/// amount. For a sighted operator the number is column one.
/// </para>
/// <para>
/// These execute the shipped formatter against the shipped response types. They
/// assert <strong>role and value together</strong>: every figure in a fixture is
/// distinct, so a test cannot pass by finding the right currency attached to the
/// wrong sum, and swapping two money fields fails.
/// </para>
/// </remarks>
public sealed class ValueParityTests
{
    private static MoneyResponse Money(decimal amount, string currency = "USD") =>
        new(amount, currency);

    // ------------------------------------------------------------------ terms

    private static OfferTermResponse Term(string display, string value) =>
        new("GuaranteedCompensation", display, "Money", true, 185000m, "USD",
            null, null, null, null, null, null, value, 1, null);

    /// <summary>
    /// A term announces the value the row shows, because it is the same field.
    /// </summary>
    /// <remarks>
    /// The visible column binds <c>DisplayValue</c> and so does the announcement.
    /// The server formats it once, per the term's own kind, so money, percentages,
    /// dates and counts all arrive correct without the formatter knowing about any
    /// of them — and the two channels cannot drift, because there is one field.
    /// </remarks>
    [Fact]
    public void AMonetaryTermAnnouncesItsValue()
    {
        string spoken = RowLabel.For(Term("Fee", "185,000.00 USD"));

        Assert.Contains("Fee", spoken, StringComparison.Ordinal);
        Assert.Contains("185,000.00 USD", spoken, StringComparison.Ordinal);
    }

    /// <summary>A non-money term announces its own kind of value, unchanged.</summary>
    [Theory]
    [InlineData("Commission", "10%")]
    [InlineData("Term length", "24 months")]
    [InlineData("Exclusivity", "Yes")]
    [InlineData("Start date", "2027-03-01")]
    public void ANonMonetaryTermAnnouncesItsValue(string label, string value)
    {
        string spoken = RowLabel.For(Term(label, value));

        Assert.Contains(label, spoken, StringComparison.Ordinal);
        Assert.Contains(value, spoken, StringComparison.Ordinal);
    }

    /// <summary>A contract term is the same shape and gets the same treatment.</summary>
    [Fact]
    public void AContractTermAnnouncesItsValue()
    {
        ContractTermResponse term = new(
            "GuaranteedCompensation", "Fee", "Money", true, true, 185000m, "USD",
            null, null, null, null, null, null, "185,000.00 USD", "7.2", 1, null);

        string spoken = RowLabel.For(term);

        Assert.Contains("Fee", spoken, StringComparison.Ordinal);
        Assert.Contains("185,000.00 USD", spoken, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ money

    private static ReceivableResponse Receivable(
        string currency = "USD",
        decimal original = 240000m,
        decimal allocated = 90000m,
        decimal outstanding = 150000m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Halloway / Corvid Ash",
            Guid.NewGuid(), "Corvid Ash Pictures", "Client", Guid.NewGuid(), "Perrin Halloway",
            Money(original, currency), Money(allocated, currency), Money(0m, currency),
            Money(outstanding, currency), new DateOnly(2026, 8, 1), "Open", false,
            "CIN-0001", null, null, DateTimeOffset.UtcNow, 1);

    /// <summary>
    /// A receivable announces its money, and each figure keeps its own name.
    /// </summary>
    /// <remarks>
    /// Three distinct amounts, so a formatter that read the wrong field, or that
    /// attached the right currency to the wrong sum, fails here rather than
    /// passing on the presence of the word USD.
    /// </remarks>
    [Fact]
    public void AReceivableAnnouncesEachFigureWithItsOwnName()
    {
        string spoken = RowLabel.For(Receivable());

        Assert.Contains("240,000.00 USD original amount", spoken, StringComparison.Ordinal);
        Assert.Contains("90,000.00 USD allocated", spoken, StringComparison.Ordinal);
        Assert.Contains("150,000.00 USD outstanding", spoken, StringComparison.Ordinal);
    }

    /// <summary>Swapping two figures changes what the row says.</summary>
    /// <remarks>
    /// The assertion that makes the one above mean something. A row announcing
    /// three bare sums would satisfy a currency check while telling an operator
    /// that the settled amount is outstanding.
    /// </remarks>
    [Fact]
    public void SwappingTwoFiguresIsVisibleInWhatTheRowSays()
    {
        string right = RowLabel.For(Receivable(allocated: 90000m, outstanding: 150000m));
        string swapped = RowLabel.For(Receivable(allocated: 150000m, outstanding: 90000m));

        Assert.NotEqual(right, swapped);
        Assert.Contains("90,000.00 USD allocated", right, StringComparison.Ordinal);
        Assert.Contains("150,000.00 USD allocated", swapped, StringComparison.Ordinal);
    }

    /// <summary>A payment announces its own amount bare, and the rest by name.</summary>
    [Fact]
    public void APaymentAnnouncesItsAmount()
    {
        PaymentResponse payment = new(
            Guid.NewGuid(), "Incoming", Guid.NewGuid(), "Corvid Ash Pictures", null, null,
            Money(450000m), Money(200000m), Money(250000m), new DateOnly(2026, 7, 10),
            DateTimeOffset.UtcNow, "BankTransfer", "WIRE-88213", null, "Recorded",
            null, null, null, null, null, [], 1);

        string spoken = RowLabel.For(payment);

        Assert.Contains("450,000.00 USD", spoken, StringComparison.Ordinal);
        Assert.Contains("200,000.00 USD allocated", spoken, StringComparison.Ordinal);
        Assert.Contains("250,000.00 USD unapplied", spoken, StringComparison.Ordinal);
        Assert.Contains("Recorded", spoken, StringComparison.Ordinal);
    }

    /// <summary>An invoice announces its total and what is still owed on it.</summary>
    [Fact]
    public void AnInvoiceAnnouncesItsTotalAndOutstanding()
    {
        InvoiceResponse invoice = new(
            Guid.NewGuid(), "GH-2026-0041", Guid.NewGuid(), "Halloway / Corvid Ash",
            Guid.NewGuid(), "Corvid Ash Pictures", "Issued", new DateOnly(2026, 7, 1),
            new DateOnly(2026, 8, 1), Money(450000m), Money(120000m), false, false,
            null, null, null, [], DateTimeOffset.UtcNow, 1);

        string spoken = RowLabel.For(invoice);

        Assert.Contains("450,000.00 USD total", spoken, StringComparison.Ordinal);
        Assert.Contains("120,000.00 USD outstanding", spoken, StringComparison.Ordinal);
    }

    // ------------------------------------------------------- currency and null

    /// <summary>Currency is never dropped, whatever it is.</summary>
    [Theory]
    [InlineData("USD")]
    [InlineData("GBP")]
    [InlineData("EUR")]
    public void CurrencyIsNeverDropped(string currency)
    {
        string spoken = RowLabel.For(Receivable(currency));

        Assert.Contains($"240,000.00 {currency}", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("240000.0000", spoken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two rows in different currencies are distinguishable from the row alone.
    /// </summary>
    /// <remarks>
    /// The adversarial case Reality Closure recorded live: a Finance screen where
    /// the summary read <c>Outstanding: 140,000.00 GBP   1,255,000.00 USD</c> and
    /// the rows beneath it read <c>240000.0000</c>. Identical numbers in different
    /// currencies were identical rows.
    /// </remarks>
    [Fact]
    public void TwoCurrenciesAreDistinguishableFromTheRowAlone()
    {
        string dollars = RowLabel.For(Receivable("USD"));
        string pounds = RowLabel.For(Receivable("GBP"));

        Assert.NotEqual(dollars, pounds);
        Assert.Contains("240,000.00 USD", dollars, StringComparison.Ordinal);
        Assert.Contains("240,000.00 GBP", pounds, StringComparison.Ordinal);
    }

    /// <summary>
    /// An amount nobody has recorded is not announced as zero.
    /// </summary>
    /// <remarks>
    /// The same epistemic rule F-01 established for counts, applied to a figure. A
    /// contingent bonus nobody can value yet is not worth nothing, and saying
    /// <c>0.00 USD</c> would put a number in the operator's head that no one wrote
    /// down.
    /// </remarks>
    [Fact]
    public void AnAbsentAmountIsNotAnnouncedAsZero()
    {
        MonetaryObligationResponse unvalued = new(
            Guid.NewGuid(), Guid.NewGuid(), "Halloway / Corvid Ash", Guid.NewGuid(),
            null, null, Guid.NewGuid(), "Corvid Ash Pictures", Guid.NewGuid(), "Perrin Halloway",
            "Bonus", "Contingent", null, null, null, "If the picture is released",
            null, null, null, "Expected", false, false, "Theatrical release bonus", null, 1);

        string spoken = RowLabel.For(unvalued);

        Assert.Contains("Theatrical release bonus", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("0.00", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("USD", spoken, StringComparison.Ordinal);
    }

    /// <summary>A recorded zero is a business result and is announced.</summary>
    [Fact]
    public void ARecordedZeroIsAnnounced()
    {
        string spoken = RowLabel.For(Receivable(allocated: 0m));

        Assert.Contains("0.00 USD allocated", spoken, StringComparison.Ordinal);
    }

    // ----------------------------------------------------- rows with no value

    /// <summary>
    /// Rows that announced only their own type name now say which row they are.
    /// </summary>
    /// <remarks>
    /// F-08, folded into F-02 by the register: same formatter, same repair. Every
    /// row in these lists announced identical words, so a screen-reader operator
    /// heard a column of <c>Document version</c> and could not tell one from
    /// another.
    /// </remarks>
    [Fact]
    public void ADocumentVersionSaysWhichVersionItIs()
    {
        DocumentVersionResponse version = new(
            Guid.NewGuid(), 3, "engagement-letter-v3.pdf", "application/pdf", 20480,
            "sha256:abc", "Upload", null, DateTimeOffset.UtcNow, "Review Owner", null,
            "Extracted", null, "Clean");

        string spoken = RowLabel.For(version);

        Assert.Contains("engagement-letter-v3.pdf", spoken, StringComparison.Ordinal);
        Assert.NotEqual("Document version", spoken);
    }

    /// <summary>A representation scope says which area it covers.</summary>
    [Fact]
    public void ARepresentationScopeSaysWhichAreaItCovers()
    {
        RepresentationScopeResponse scope = new("Film", new DateOnly(2026, 1, 5), null);

        string spoken = RowLabel.For(scope);

        Assert.Contains("Film", spoken, StringComparison.Ordinal);
        Assert.NotEqual("Representation scope", spoken);
    }

    // --------------------------------------------------------------- bounds

    /// <summary>
    /// A money row still fits inside the bound that makes a row scannable.
    /// </summary>
    /// <remarks>
    /// Four figures, a name, a counterparty and two states is the widest row in
    /// the product. If it exceeded the limit the tail would be cut, and the tail
    /// is where the operative balance is.
    /// </remarks>
    [Fact]
    public void TheWidestMoneyRowStillFits()
    {
        string spoken = RowLabel.For(Receivable());

        Assert.True(spoken.Length <= 160, $"{spoken.Length} characters: {spoken}");
        Assert.DoesNotContain("…", spoken, StringComparison.Ordinal);
        Assert.Contains("150,000.00 USD outstanding", spoken, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rows repaired in earlier waves still say what they said.
    /// </summary>
    /// <remarks>
    /// The value vocabulary is additive and must not displace the role attribution
    /// build 83 established, nor the states the census found correct.
    /// </remarks>
    [Fact]
    public void EarlierRepairsAreUndisturbed()
    {
        string spoken = RowLabel.For(Receivable());

        Assert.StartsWith("CIN-0001", spoken, StringComparison.Ordinal);
        Assert.Contains("Open", spoken, StringComparison.Ordinal);
    }
}
