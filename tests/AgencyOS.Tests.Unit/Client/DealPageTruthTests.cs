using AgencyOS.Client.Presentation;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Deals;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Legal;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// That nothing a Deal page says about paper can contradict anything else it says.
/// </summary>
/// <remarks>
/// <para>
/// The build-79 blind handoff found the Deal page asserting two things at once,
/// twenty pixels apart: a subtitle reading "terms agreed, no contract recorded" and
/// a green panel reading "Contract executed". The panel was the build-79 repair and
/// was right; the subtitle was older, inferred contract absence from deal status
/// alone, and had never been looked at.
/// </para>
/// <para>
/// So this tests the <em>composite</em> — every operator-visible sentence about
/// this negotiation, together — rather than the string that was changed. Testing
/// one control is what let the contradiction survive a repair that was verified
/// live.
/// </para>
/// <para>
/// <strong>The real view model, not a copy of it (F-07).</strong> This used to
/// compose the screen from a hand-written mirror of the deal's state line, pinned
/// to the real one by reading its source, and sampled eleven contract
/// arrangements. It now loads <see cref="DealDetailViewModel"/> against a fake
/// server and reads the two properties the page renders, for every deal status the
/// domain has and every <em>set</em> of contract statuses — all 256. The standing
/// depends only on which statuses are present, not on how many contracts hold
/// them, so the power set is the whole state space and "never" is a claim this
/// test can actually make.
/// </para>
/// <para>
/// What this does not reach is the page itself: that the Deals page renders these
/// two properties and adds no paper sentence of its own is a markup fact, held by
/// <c>DealPaperTruthTests</c> in the Windows suite.
/// </para>
/// </remarks>
public sealed class DealPageTruthTests
{
    /// <summary>Words that assert there is no paper.</summary>
    private static readonly string[] ClaimsOfAbsence =
    [
        "no contract",
        "not papered",
        "contract absent",
        "does not track",
    ];

    /// <summary>Words that assert there is paper.</summary>
    private static readonly string[] ClaimsOfPresence =
    [
        "contract executed",
        "partially executed",
        "approved for signature",
        "under review",
        "in drafting",
    ];

    /// <summary>Every deal status the domain has, with and without an offer open.</summary>
    public static TheoryData<string, bool> Deals()
    {
        TheoryData<string, bool> data = [];

        foreach (string status in Enum.GetNames<DealStatus>())
        {
            data.Add(status, false);
            data.Add(status, true);
        }

        return data;
    }

    /// <summary>
    /// The paper sentences, for every arrangement of paper a deal can have.
    /// </summary>
    /// <remarks>
    /// The invariant is not "the message is right". It is that the page never says
    /// both, whatever the contract is doing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Deals))]
    public async Task TheDealPagesPaperSentencesNeverAssertBothPresenceAndAbsence(
        string dealStatus, bool openOffer)
    {
        foreach (string[] contracts in Arrangements())
        {
            string screen = await ScreenAsync(dealStatus, openOffer, contracts);

            bool absence = ClaimsOfAbsence.Any(
                x => screen.Contains(x, StringComparison.OrdinalIgnoreCase));

            bool presence = ClaimsOfPresence.Any(
                x => screen.Contains(x, StringComparison.OrdinalIgnoreCase));

            Assert.False(
                absence && presence,
                $"A {dealStatus} deal with [{string.Join(", ", contracts)}] says both: {screen}");
        }
    }

    /// <summary>
    /// While any contract is recorded against the deal, nothing on it says there is none.
    /// </summary>
    /// <remarks>
    /// The build-78 falsehood, stated as the operator met it: a deal whose contract
    /// had been executed, on a page announcing that no contract had been drafted.
    /// Every non-empty set of contract statuses, whatever state it is in — an
    /// abandoned draft is still a contract that was recorded.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Deals))]
    public async Task WhileAnyContractIsRecordedThePageNeverSaysThereIsNone(
        string dealStatus, bool openOffer)
    {
        foreach (string[] contracts in Arrangements().Where(x => x.Length > 0))
        {
            string screen = await ScreenAsync(dealStatus, openOffer, contracts);

            Assert.False(
                ClaimsOfAbsence.Any(x => screen.Contains(x, StringComparison.OrdinalIgnoreCase)),
                $"A {dealStatus} deal with [{string.Join(", ", contracts)}] says there is none: {screen}");
        }
    }

    /// <summary>The deal's own state line never mentions paper, whatever its status.</summary>
    /// <remarks>
    /// The line that caused build 79. Contract truth has one source on this page,
    /// <see cref="ContractStanding"/>, and this is the proof that the deal's own
    /// line is not a second one — read from the real property, for every status.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Deals))]
    public async Task TheDealsOwnStateLineNeverMentionsAContract(string dealStatus, bool openOffer)
    {
        DealDetailViewModel page = await LoadAsync(dealStatus, openOffer, ["Executed"]);

        Assert.False(string.IsNullOrWhiteSpace(page.Standing));
        Assert.DoesNotContain("contract", page.Standing, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An executed contract is never described as absent.</summary>
    [Fact]
    public async Task AnExecutedContractIsNeverCalledAbsent()
    {
        string screen = await ScreenAsync("TermsAgreed", false, ["Executed"]);

        Assert.DoesNotContain("no contract", screen, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("executed", screen, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A drafted contract is not described as absent either.</summary>
    [Fact]
    public async Task ADraftContractIsNeverCalledAbsent()
    {
        string screen = await ScreenAsync("TermsAgreed", false, ["Draft"]);

        Assert.DoesNotContain("no contract", screen, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drafted", screen, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>With no contract at all, the page may say so exactly once.</summary>
    [Fact]
    public async Task WithNoContractTheAbsenceIsStatedOnce()
    {
        DealDetailViewModel page = await LoadAsync("TermsAgreed", false, []);

        Assert.True(page.ContractStanding.Show);
        Assert.Equal(StandingSeverity.Informational, page.ContractStanding.Severity);

        // The control that keeps the two invariants above honest: the absence
        // vocabulary does recognise the one absence sentence the page can say, so
        // their passing is not the detector failing to see anything.
        Assert.Contains(
            ClaimsOfAbsence,
            x => page.ContractStanding.Message.Contains(x, StringComparison.OrdinalIgnoreCase));

        // The deal's own line says nothing about paper, so the standing is the only
        // place an operator is told, and there is nothing for it to disagree with.
        Assert.DoesNotContain("contract", page.Standing, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every set of contract statuses a deal can carry: the power set, all 256.
    /// </summary>
    private static IEnumerable<string[]> Arrangements()
    {
        string[] statuses = Enum.GetNames<ContractStatus>();

        for (int mask = 0; mask < 1 << statuses.Length; mask++)
        {
            yield return [.. statuses.Where((_, i) => (mask & (1 << i)) != 0)];
        }
    }

    /// <summary>
    /// Everything about paper that the page puts in front of an operator.
    /// </summary>
    /// <remarks>
    /// The deal's own state line plus the contract standing, read from the view
    /// model the page binds. If a third sentence is ever added, it belongs here,
    /// and this test is where it will be caught saying something the others do not.
    /// </remarks>
    private static async Task<string> ScreenAsync(
        string dealStatus, bool openOffer, IReadOnlyCollection<string> contracts)
    {
        DealDetailViewModel page = await LoadAsync(dealStatus, openOffer, contracts);

        ContractStanding standing = page.ContractStanding;

        return standing.Show
            ? $"{page.Standing} | {standing.Title} | {standing.Message}"
            : page.Standing;
    }

    private static async Task<DealDetailViewModel> LoadAsync(
        string dealStatus, bool openOffer, IReadOnlyCollection<string> contracts)
    {
        FakeAgencyOsApi api = new();

        DealSummaryResponse deal = FakeAgencyOsApi.Deal(
            "Autumn slate - lead role", status: dealStatus, hasOpenOffer: openOffer);

        api.Deals.Add(deal);

        foreach (string status in contracts)
        {
            api.Contracts.Add(FakeAgencyOsApi.Contract("Long form " + status, status: status));
        }

        DealDetailViewModel page = new(api);

        await page.LoadAsync(deal.Id);

        // A page that failed to load says nothing, which would pass every
        // assertion here for the wrong reason.
        Assert.Null(page.ErrorMessage);
        Assert.NotNull(page.Deal);

        return page;
    }
}
