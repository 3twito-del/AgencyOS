using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That every counter closed by Reality Closure still asks before it claims.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This proves reachability, not meaning.</strong> What a counter is
/// allowed to say is decided in <c>MoneyAuthorityTests</c>,
/// <c>OperationalAuthorityTests</c> and <c>ResidualAuthorityTests</c>, which drive
/// the real view models through failure, empty, data and retry and compose each
/// page's operator-visible outputs together. A source string cannot show that a
/// screen contradicts itself.
/// </para>
/// <para>
/// What it does show is that no page has quietly stopped routing through the
/// tested primitive — the failure catalogued as F-07, where a green test mirrors a
/// page that has moved on without it. F-01 ran to twenty-eight sites across five
/// waves; this is the guard that stops a twenty-ninth appearing unnoticed.
/// </para>
/// </remarks>
public sealed class AuthoritySurfaceTests
{
    /// <summary>Every page counter the census found, and the wave that closed it.</summary>
    /// <remarks>
    /// Finance has its own pinned list in <c>FinanceSurfaceTests</c>; the six money
    /// summaries are not repeated here.
    /// </remarks>
    public static TheoryData<string, string> Counters => new()
    {
        // wave 2 — deadline and attention claims
        { "PipelinePage", "SummaryText" },
        { "ContractsPage", "SummaryText" },
        { "DealsPage", "SummaryText" },

        // wave 6 — the plain counts
        { "DocumentsPage", "SummaryText" },
        { "ProjectsPage", "SummaryText" },
        { "TalentPage", "CountText" },
        { "CommunicationsPage", "MessageSummary" },
        { "CommunicationsPage", "OutboundSummary" },
        { "CommunicationsPage", "DeskSummary" },
        { "AiPage", "SummaryText" },
    };

    /// <summary>
    /// No counter is assigned a sentence the page built unconditionally.
    /// </summary>
    [Theory]
    [MemberData(nameof(Counters))]
    public void EveryCounterAsksWhetherItsPopulationIsKnown(string page, string counter)
    {
        string source = Page(page);

        Assert.Contains(
            counter + ".Text = SummaryAuthority.Of(",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            counter + ".Text = string.Create(",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>Every absence notice waits on the same authority its counter does.</summary>
    /// <remarks>
    /// A notice asserts something too — "there are none" — and a population that
    /// loaded empty and then failed to refresh is still empty and no longer known.
    /// </remarks>
    [Theory]
    [InlineData("DocumentsPage", "ListEmpty")]
    [InlineData("ProjectsPage", "ListEmpty")]
    [InlineData("TalentPage", "ListEmpty")]
    [InlineData("CommunicationsPage", "MessageEmpty")]
    [InlineData("CommunicationsPage", "OutboundEmpty")]
    [InlineData("CommunicationsPage", "DeskClear")]
    public void EveryAbsenceNoticeWaitsOnTheSameAuthority(string page, string notice)
    {
        string source = Page(page);

        Assert.Contains(
            notice + ".IsOpen = SummaryAuthority.Knows(",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The Command Center headlines show a figure only when they have one.
    /// </summary>
    /// <remarks>
    /// Three bare numbers in fixed slots, where <c>Totals unavailable.</c> does not
    /// fit. They take the dash instead, and say in words what the dash means —
    /// otherwise the seen and the spoken channels would disagree about whether the
    /// product knows the answer.
    /// </remarks>
    [Theory]
    [InlineData("OpenTasksText", "open tasks in total")]
    [InlineData("PeopleCountText", "people")]
    [InlineData("CompanyCountText", "companies")]
    public void EveryHeadlineShowsAFigureOnlyWhenItHasOne(string slot, string caption)
    {
        string source = Page("CommandCenterPage");

        Assert.Contains($"Headline({slot}, \"{caption}\"", source, StringComparison.Ordinal);

        // The old shape wrote the number straight in, with no question asked.
        Assert.DoesNotContain(
            slot + ".Text = _viewModel.",
            source,
            StringComparison.Ordinal);

        Assert.Contains("SummaryAuthority.Figure(", source, StringComparison.Ordinal);
        Assert.Contains("SummaryAuthority.Spoken(", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The desk's counts and the desk's sentence come from one authority test.
    /// </summary>
    /// <remarks>
    /// They answer the same question from the same projection. Wave 2 gated the
    /// sentence and left the counts, so the panel could disclaim and assert at once.
    /// </remarks>
    [Fact]
    public void TheDeskCountsAndSentenceShareOneAuthority()
    {
        string source = Page("CommunicationsPage");

        Assert.Contains("DeskSummary.Text = SummaryAuthority.Of(", source, StringComparison.Ordinal);
        Assert.Contains("SummaryAuthority.Knows(_desk) && !_desk.NeedsAttention", source, StringComparison.Ordinal);
        Assert.Contains("!SummaryAuthority.Knows(_desk)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A membership count stops claiming the membership when its load fails.
    /// </summary>
    /// <remarks>
    /// Organization has no list view model — it holds a local collection inside a
    /// try — so it cannot use the primitive, but it answers the same question. Only
    /// the load clears the count; a refused action says nothing about who the
    /// members are.
    /// </remarks>
    [Fact]
    public void AFailedMembershipLoadStopsClaimingTheMembership()
    {
        string source = Page("OrganizationPage");

        Assert.Contains(
            "SummaryText.Text = SummaryAuthority.Unavailable;",
            source,
            StringComparison.Ordinal);
    }

    private static string Page(string page) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", $"{page}.xaml.cs"));

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
