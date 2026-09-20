using AgencyOS.Client.Presentation;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// What a negotiation's page may say about its paper.
/// </summary>
/// <remarks>
/// The blind-handoff retest of build 78 found the Deals page showing a green
/// success banner reading "No contract has been drafted, signed or executed, and
/// AgencyOS does not track that yet" on a deal whose contract had been executed.
/// The sentence was true when M7 shipped and false from the moment M8 added
/// contracts.
/// </remarks>
public sealed class ContractStandingTests
{
    private const string TheOldLie = "No contract has been drafted, signed or executed";

    /// <summary>The defect itself: an executed contract cannot read as absent.</summary>
    [Fact]
    public void AnExecutedContractIsNeverReportedAsAbsent()
    {
        ContractStanding standing = ContractStandingFor.Of("TermsAgreed", ["Executed"]);

        Assert.True(standing.Show);
        Assert.Equal(StandingSeverity.Success, standing.Severity);
        Assert.Contains("executed", standing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No contract", standing.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No arrangement of contracts produces the sentence that was wrong.
    /// </summary>
    /// <remarks>
    /// The brief asked specifically that zero, draft and executed cannot produce the
    /// same false success message. Stated as a property over every state this build
    /// knows, so a state added later is covered by construction.
    /// </remarks>
    [Theory]
    [InlineData("Draft")]
    [InlineData("UnderReview")]
    [InlineData("ApprovedForExecution")]
    [InlineData("PartiallyExecuted")]
    [InlineData("Executed")]
    [InlineData("Terminated")]
    [InlineData("Superseded")]
    [InlineData("Abandoned")]
    public void NoContractStateEverClaimsThereIsNoContract(string status)
    {
        ContractStanding standing = ContractStandingFor.Of("TermsAgreed", [status]);

        Assert.DoesNotContain(TheOldLie, standing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "does not track", standing.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Three cases that must not look alike.</summary>
    [Fact]
    public void NoneDraftAndExecutedAreThreeDifferentMessages()
    {
        ContractStanding none = ContractStandingFor.Of("TermsAgreed", []);
        ContractStanding draft = ContractStandingFor.Of("TermsAgreed", ["Draft"]);
        ContractStanding executed = ContractStandingFor.Of("TermsAgreed", ["Executed"]);

        Assert.NotEqual(none.Message, draft.Message);
        Assert.NotEqual(draft.Message, executed.Message);
        Assert.NotEqual(none.Message, executed.Message);
    }

    /// <summary>An absent contract is stated, not celebrated.</summary>
    /// <remarks>
    /// The old banner rendered a missing contract as Success. The absence of an
    /// error is not an achievement, and green is what made the sentence persuasive.
    /// </remarks>
    [Fact]
    public void AMissingContractIsNotASuccess()
    {
        ContractStanding standing = ContractStandingFor.Of("TermsAgreed", []);

        Assert.True(standing.Show);
        Assert.Equal(StandingSeverity.Informational, standing.Severity);
        Assert.Contains("No contract has been recorded", standing.Message, StringComparison.Ordinal);
    }

    /// <summary>Before terms are agreed, nobody expects paper, so nothing is said.</summary>
    [Theory]
    [InlineData("Draft")]
    [InlineData("Negotiating")]
    [InlineData("NoDeal")]
    [InlineData("Cancelled")]
    public void NothingIsSaidAboutPaperBeforeTermsAreAgreed(string dealStatus)
    {
        Assert.False(ContractStandingFor.Of(dealStatus, []).Show);
    }

    /// <summary>A live instrument outranks one that ended without finishing.</summary>
    /// <remarks>
    /// An amendment is a separate contract (ADR-0022), so a deal can carry more than
    /// one. The banner reports the furthest one along, and an abandoned draft beside
    /// a live one must not be what the operator is told about.
    /// </remarks>
    [Fact]
    public void TheFurthestInstrumentIsTheOneReported()
    {
        ContractStanding standing =
            ContractStandingFor.Of("TermsAgreed", ["Abandoned", "Executed", "Draft"]);

        Assert.Equal(StandingSeverity.Success, standing.Severity);
        Assert.Contains("executed", standing.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An instrument that ended without finishing is not green.</summary>
    [Theory]
    [InlineData("Terminated")]
    [InlineData("Superseded")]
    [InlineData("Abandoned")]
    public void AnInstrumentThatEndedIsNotASuccess(string status)
    {
        Assert.Equal(
            StandingSeverity.Warning, ContractStandingFor.Of("TermsAgreed", [status]).Severity);
    }

    /// <summary>A state this build does not know is not guessed at.</summary>
    [Fact]
    public void AnUnknownStateSaysNothing()
    {
        Assert.False(ContractStandingFor.Of("TermsAgreed", ["SomethingLater"]).Show);
    }
}
