using AgencyOS.Client.Presentation;
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

    /// <summary>
    /// The whole screen, for every arrangement of paper a deal can have.
    /// </summary>
    /// <remarks>
    /// The invariant is not "the message is right". It is that the page never says
    /// both, whatever the contract is doing.
    /// </remarks>
    [Theory]
    [InlineData("TermsAgreed")]
    [InlineData("Negotiating")]
    [InlineData("Draft")]
    [InlineData("NoDeal")]
    [InlineData("Cancelled")]
    public void ThePageNeverAssertsBothPresenceAndAbsence(string dealStatus)
    {
        foreach (string[] contracts in Arrangements())
        {
            string screen = Screen(dealStatus, contracts);

            bool absence = ClaimsOfAbsence.Any(
                x => screen.Contains(x, StringComparison.OrdinalIgnoreCase));

            bool presence = ClaimsOfPresence.Any(
                x => screen.Contains(x, StringComparison.OrdinalIgnoreCase));

            Assert.False(
                absence && presence,
                $"A {dealStatus} deal with [{string.Join(", ", contracts)}] says both: {screen}");
        }
    }

    /// <summary>An executed contract is never described as absent.</summary>
    [Fact]
    public void AnExecutedContractIsNeverCalledAbsent()
    {
        string screen = Screen("TermsAgreed", ["Executed"]);

        Assert.DoesNotContain("no contract", screen, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("executed", screen, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A drafted contract is not described as absent either.</summary>
    [Fact]
    public void ADraftContractIsNeverCalledAbsent()
    {
        string screen = Screen("TermsAgreed", ["Draft"]);

        Assert.DoesNotContain("no contract", screen, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drafted", screen, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>With no contract at all, the page may say so exactly once.</summary>
    [Fact]
    public void WithNoContractTheAbsenceIsStatedOnce()
    {
        ContractStanding standing = ContractStandingFor.Of("TermsAgreed", []);

        Assert.True(standing.Show);
        Assert.Equal(StandingSeverity.Informational, standing.Severity);

        // The deal's own line says nothing about paper, so the standing is the only
        // place an operator is told, and there is nothing for it to disagree with.
        Assert.DoesNotContain(
            "contract", DealLine("TermsAgreed"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The deal's own wording never mentions paper, whatever its status.</summary>
    [Theory]
    [InlineData("TermsAgreed")]
    [InlineData("Negotiating")]
    [InlineData("Draft")]
    [InlineData("NoDeal")]
    [InlineData("Cancelled")]
    public void TheDealLineNeverMentionsContracts(string dealStatus)
    {
        Assert.DoesNotContain("contract", DealLine(dealStatus), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every arrangement of contracts a deal can carry.</summary>
    private static IEnumerable<string[]> Arrangements() =>
    [
        [],
        ["Draft"],
        ["UnderReview"],
        ["ApprovedForExecution"],
        ["PartiallyExecuted"],
        ["Executed"],
        ["Abandoned"],
        ["Terminated"],
        ["Superseded"],
        ["Abandoned", "Executed"],
        ["Draft", "Superseded"],
    ];

    /// <summary>
    /// Everything about paper that the page puts in front of an operator.
    /// </summary>
    /// <remarks>
    /// The deal's own state line plus the contract standing. If a third sentence is
    /// ever added, it belongs here, and this test is where it will be caught saying
    /// something the others do not.
    /// </remarks>
    private static string Screen(string dealStatus, IReadOnlyCollection<string> contracts)
    {
        ContractStanding standing = ContractStandingFor.Of(dealStatus, contracts);

        return standing.Show
            ? $"{DealLine(dealStatus)} | {standing.Title} | {standing.Message}"
            : DealLine(dealStatus);
    }

    /// <summary>
    /// The deal's own state wording, as <c>DealDetailViewModel.Standing</c> builds it.
    /// </summary>
    /// <remarks>
    /// Mirrored rather than invoked, because constructing the view model needs an
    /// API client and this is a statement about wording. <c>DealsPageTruthTests</c>
    /// in the Windows suite pins that the real property still matches.
    /// </remarks>
    private static string DealLine(string dealStatus) => dealStatus switch
    {
        "TermsAgreed" => "terms agreed",
        "Negotiating" => "in negotiation",
        "NoDeal" => "closed without agreement",
        "Cancelled" => "cancelled",
        _ => "being set up",
    };
}
