using AgencyOS.Client.Presentation;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.PeopleSlice;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// What a row says, and what a domain token reads as.
/// </summary>
/// <remarks>
/// The deterministic half of Repair Wave 002. Both formatters are pure functions
/// over data, so the rules they encode can be pinned without a window, a database
/// or a screen reader — which is what keeps <c>AOS-R001-003</c> and
/// <c>AOS-R001-012</c> from coming back quietly.
/// </remarks>
public sealed class PresentationLabelTests
{
    // ------------------------------------------------------- AOS-R001-012

    [Theory]
    [InlineData("TalentEmployment", "Talent employment")]
    [InlineData("ProjectSale", "Project sale")]
    [InlineData("TermsAgreed", "Terms agreed")]
    [InlineData("SourceSensitive", "Source sensitive")]
    [InlineData("MoreMaterialRequested", "More material requested")]
    [InlineData("BrandEntertainment", "Brand entertainment")]
    public void APascalCaseTokenReadsAsWords(string token, string expected) =>
        Assert.Equal(expected, DisplayLabel.For(token));

    /// <summary>
    /// A single word is already a label.
    /// </summary>
    /// <remarks>
    /// Most domain values are one word — Draft, Negotiating, Open, High — and the
    /// converter runs over all of them. Leaving them untouched is what makes it
    /// safe to apply everywhere rather than only where a value happens to be
    /// compound.
    /// </remarks>
    [Theory]
    [InlineData("Draft")]
    [InlineData("Negotiating")]
    [InlineData("Open")]
    [InlineData("High")]
    public void ASingleWordIsUnchanged(string token) =>
        Assert.Equal(token, DisplayLabel.For(token));

    /// <summary>
    /// Prose is left alone.
    /// </summary>
    /// <remarks>
    /// The converter is applied to bindings that usually carry a token and
    /// sometimes carry a sentence a person wrote. Rewriting the sentence would be
    /// the bug.
    /// </remarks>
    [Theory]
    [InlineData("Autumn slate — lead role")]
    [InlineData("Recorded, not sent")]
    [InlineData("")]
    public void WrittenTextIsUnchanged(string text) =>
        Assert.Equal(text, DisplayLabel.For(text));

    /// <summary>
    /// The terms the business already writes its own way survive.
    /// </summary>
    /// <remarks>
    /// Splitting on case alone would produce "A I" and "Project license". An
    /// agency writes AI, and writes licence.
    /// </remarks>
    [Theory]
    [InlineData("AI", "AI")]
    [InlineData("ProjectLicense", "Project licence")]
    [InlineData("NoDeal", "No deal")]
    public void EstablishedTermsAreKept(string token, string expected) =>
        Assert.Equal(expected, DisplayLabel.For(token));

    /// <summary>
    /// Distinctions the product depends on are not flattened.
    /// </summary>
    /// <remarks>
    /// Status is not stage, an obligation is not a task, a prediction is not a
    /// fact. This formatter splits words and changes nothing else, and this is the
    /// test that says so out loud.
    /// </remarks>
    [Fact]
    public void MeaningIsNotSimplifiedAway()
    {
        Assert.NotEqual(DisplayLabel.For("Status"), DisplayLabel.For("Stage"));
        Assert.NotEqual(DisplayLabel.For("Obligation"), DisplayLabel.For("Task"));
        Assert.NotEqual(DisplayLabel.For("Prediction"), DisplayLabel.For("Fact"));
        Assert.NotEqual(DisplayLabel.For("Recorded"), DisplayLabel.For("Sent"));
        Assert.NotEqual(DisplayLabel.For("Source"), DisplayLabel.For("Truth"));
    }

    // ------------------------------------------------------- AOS-R001-003

    /// <summary>
    /// A deal row says what it shows, not what it holds.
    /// </summary>
    /// <remarks>
    /// The row Audit 001 measured at 894 characters and six identifiers. The
    /// assertions are the ones that matter to somebody listening: the name is
    /// there, the state is there, and the record's plumbing is not.
    /// </remarks>
    [Fact]
    public void ADealRowAnnouncesItsNameAndState()
    {
        DealSummaryResponse deal = new(
            Guid.CreateVersion7(), "Autumn slate — lead role", null, "TalentEmployment",
            "Negotiating", Guid.CreateVersion7(), "Autumn slate — lead role",
            Guid.CreateVersion7(), "A24", Guid.CreateVersion7(), null, "The Quiet Coast",
            Guid.CreateVersion7(), "Review Owner", new DateOnly(2026, 9, 14), null,
            1, Guid.CreateVersion7(), "Inbound", DateTimeOffset.UtcNow, true, null, null,
            0, null, DateTimeOffset.UtcNow, 2);

        string spoken = RowLabel.For(deal);

        Assert.Contains("Autumn slate", spoken, StringComparison.Ordinal);
        Assert.Contains("Negotiating", spoken, StringComparison.Ordinal);

        // The token becomes words on the way out.
        Assert.Contains("Talent employment", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("TalentEmployment", spoken, StringComparison.Ordinal);

        AssertNotARecordDump(spoken);
    }

    /// <summary>A person row says the person, not the row's plumbing.</summary>
    [Fact]
    public void APersonRowAnnouncesTheirName()
    {
        PersonSummaryResponse person = new(
            Guid.CreateVersion7(), "Priya Raghunathan", "Literary Agent",
            "priya@review.invalid", null, "Active", Guid.CreateVersion7(),
            "Pinewood Streaming Group", DateTimeOffset.UtcNow, 3);

        string spoken = RowLabel.For(person);

        Assert.StartsWith("Priya Raghunathan", spoken, StringComparison.Ordinal);
        Assert.Contains("Pinewood Streaming Group", spoken, StringComparison.Ordinal);

        AssertNotARecordDump(spoken);
    }

    /// <summary>
    /// A row stays short enough to listen to.
    /// </summary>
    /// <remarks>
    /// The defect was not only that the old name was wrong, but that it was
    /// hundreds of characters. A name nobody can wait through is a name nobody
    /// hears.
    /// </remarks>
    [Fact]
    public void ARowIsShortEnoughToScan()
    {
        PersonSummaryResponse person = new(
            Guid.CreateVersion7(),
            new string('x', 400),
            "Executive Vice President of Global Scripted Content and Strategic Partnerships",
            null, null, "Active", null, null, DateTimeOffset.UtcNow, 1);

        string spoken = RowLabel.For(person);

        Assert.True(
            spoken.Length <= 161,
            $"A row announced {spoken.Length} characters. The defect this closes was a "
                + "row that read out its entire record.");
    }

    /// <summary>A row bound to a plain string says the string.</summary>
    /// <remarks>
    /// Several lists bind strings — agent kinds, tool names — and those were always
    /// correct. The formatter must not make them worse.
    /// </remarks>
    [Fact]
    public void APlainStringRowIsItself() =>
        Assert.Equal("brief-writer", RowLabel.For("brief-writer"));

    /// <summary>Nothing at all is said for nothing at all.</summary>
    [Fact]
    public void ANullRowIsSilent() => Assert.Equal(string.Empty, RowLabel.For(null));

    /// <summary>
    /// An unrecognised record says what kind of thing it is.
    /// </summary>
    /// <remarks>
    /// The fallback has to be honest rather than empty: a row that announces
    /// nothing is as unusable as one that announces everything, and it must still
    /// never be <c>ToString()</c>.
    /// </remarks>
    [Fact]
    public void AnUnrecognisedRowNamesItsKindRatherThanDumpingItself()
    {
        string spoken = RowLabel.For(new Unlabelled(Guid.CreateVersion7(), 7));

        Assert.Equal("Unlabelled", spoken);

        AssertNotARecordDump(spoken);
    }

    private static void AssertNotARecordDump(string spoken)
    {
        Assert.DoesNotContain(" { ", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain(" = ", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("Response", spoken, StringComparison.Ordinal);

        Assert.False(
            System.Text.RegularExpressions.Regex.IsMatch(
                spoken, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}"),
            $"A row announced an identifier: {spoken}");
    }

    private sealed record Unlabelled(Guid Id, int Version);
}
