using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Deals;

/// <summary>
/// The deal state machine, enumerated rather than sampled.
/// </summary>
/// <remarks>
/// Every one of the 25 status/transition pairs is asserted individually against a
/// table written here rather than read from the implementation, so the test can
/// disagree with the code. Sampling the interesting transitions would prove the
/// legal ones work and say nothing about the illegal ones, and it is the illegal
/// ones that matter (ADR-0021).
/// </remarks>
public sealed class DealTransitionTests
{
    /// <summary>The complete table, stated independently of the rules kernel.</summary>
    private static readonly Dictionary<(DealStatus From, DealTransition Trigger), DealStatus> Legal = new()
    {
        [(DealStatus.Draft, DealTransition.OfferRecorded)] = DealStatus.Negotiating,
        [(DealStatus.Draft, DealTransition.Cancelled)] = DealStatus.Cancelled,

        [(DealStatus.Negotiating, DealTransition.OfferRecorded)] = DealStatus.Negotiating,
        [(DealStatus.Negotiating, DealTransition.OfferAccepted)] = DealStatus.TermsAgreed,
        [(DealStatus.Negotiating, DealTransition.ClosedNoDeal)] = DealStatus.NoDeal,
        [(DealStatus.Negotiating, DealTransition.Cancelled)] = DealStatus.Cancelled,

        [(DealStatus.TermsAgreed, DealTransition.NegotiationReopened)] = DealStatus.Negotiating,

        [(DealStatus.NoDeal, DealTransition.NegotiationReopened)] = DealStatus.Negotiating,
    };

    public static TheoryData<DealStatus, DealTransition> EveryPair()
    {
        TheoryData<DealStatus, DealTransition> data = [];

        foreach (DealStatus status in Enum.GetValues<DealStatus>())
        {
            foreach (DealTransition transition in Enum.GetValues<DealTransition>())
            {
                data.Add(status, transition);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void EveryTransition_MatchesThePublishedTable(DealStatus from, DealTransition trigger)
    {
        bool expected = Legal.ContainsKey((from, trigger));

        Assert.Equal(expected, Deal.Permits(from, trigger));

        if (expected)
        {
            Assert.Contains(Legal[(from, trigger)], Deal.ReachableFrom(from));
        }
    }

    [Fact]
    public void TheTableIsCompletelyEnumerated()
    {
        int pairs = Enum.GetValues<DealStatus>().Length * Enum.GetValues<DealTransition>().Length;

        Assert.Equal(25, pairs);
        Assert.Equal(8, Legal.Count);
    }

    /// <summary>
    /// Cancelling is final. Calling something off and then carrying on is a new
    /// negotiation, and recording it as the same one would erase that it stopped.
    /// </summary>
    [Fact]
    public void OnlyCancelled_IsTerminal()
    {
        foreach (DealStatus status in Enum.GetValues<DealStatus>())
        {
            bool terminal = status == DealStatus.Cancelled;

            Assert.Equal(terminal, Deal.TerminalStatuses.Contains(status));
            Assert.Equal(terminal, Deal.ReachableFrom(status).Count == 0);
        }
    }

    /// <summary>
    /// The invariant the whole milestone rests on: nothing but accepting an offer
    /// reaches TermsAgreed.
    /// </summary>
    [Fact]
    public void TermsAgreed_IsReachableOnlyByAcceptingAnOffer()
    {
        IEnumerable<DealTransition> routes = Legal
            .Where(entry => entry.Value == DealStatus.TermsAgreed)
            .Select(entry => entry.Key.Trigger);

        Assert.Equal(DealTransition.OfferAccepted, Assert.Single(routes));
    }

    /// <summary>
    /// And nothing a caller can ask for reaches it either, which is the half a
    /// transition table alone would not cover.
    /// </summary>
    [Theory]
    [InlineData(DealTransition.ClosedNoDeal, true)]
    [InlineData(DealTransition.Cancelled, true)]
    [InlineData(DealTransition.NegotiationReopened, true)]
    [InlineData(DealTransition.OfferRecorded, false)]
    [InlineData(DealTransition.OfferAccepted, false)]
    public void OnlyConsequenceFreeTransitions_AreCallerRequestable(
        DealTransition transition,
        bool requestable)
    {
        Assert.Equal(
            requestable,
            AgencyOS.Deals.Rules.DealRules.IsCallerRequestableDealTrigger((int)transition));
    }

    [Fact]
    public void OfferActivity_IsAcceptedOnlyWhileTheDealIsLive()
    {
        Assert.True(AgencyOS.Deals.Rules.DealRules.DealAcceptsOfferActivity((int)DealStatus.Draft));
        Assert.True(AgencyOS.Deals.Rules.DealRules.DealAcceptsOfferActivity((int)DealStatus.Negotiating));

        Assert.False(AgencyOS.Deals.Rules.DealRules.DealAcceptsOfferActivity((int)DealStatus.TermsAgreed));
        Assert.False(AgencyOS.Deals.Rules.DealRules.DealAcceptsOfferActivity((int)DealStatus.NoDeal));
        Assert.False(AgencyOS.Deals.Rules.DealRules.DealAcceptsOfferActivity((int)DealStatus.Cancelled));
    }

    /// <summary>
    /// The status vocabulary stops before contract language, and this asserts it
    /// keeps stopping there. M8 owns execution; a status here that named it would
    /// be the agency claiming a document exists.
    /// </summary>
    [Fact]
    public void TheStatusVocabulary_StopsBeforeExecution()
    {
        string[] names = [.. Enum.GetNames<DealStatus>()];

        Assert.DoesNotContain("Signed", names);
        Assert.DoesNotContain("Executed", names);
        Assert.DoesNotContain("Paid", names);
        Assert.DoesNotContain("Commissioned", names);
        Assert.DoesNotContain("Invoiced", names);
        Assert.DoesNotContain("Closed", names);

        Assert.Contains("TermsAgreed", names);
    }
}

/// <summary>The deal aggregate: anchoring, metadata and closure.</summary>
public sealed class DealTests
{
    private static readonly OrganizationId Tenant = new(Guid.CreateVersion7());
    private static readonly UserId Actor = new(Guid.CreateVersion7());
    private static readonly OpportunityId Pursuit = OpportunityId.New();
    private static readonly OpportunityTargetId Target = OpportunityTargetId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewDeal_OpensAsADraftAndRecordsThat()
    {
        Deal deal = Open();

        Assert.Equal(DealStatus.Draft, deal.Status);
        Assert.Equal(Pursuit, deal.OpportunityId);
        Assert.Equal(Target, deal.OpportunityTargetId);
        Assert.Equal(1, deal.Version);
        Assert.False(deal.HasAgreedTerms);
        Assert.True(deal.AcceptsOfferActivity);

        DealEvent opened = Assert.Single(deal.Events);
        Assert.Equal(DealEventKind.Opened, opened.Kind);
        Assert.Equal(DealStatus.Draft, opened.ToStatus);
        Assert.Null(opened.FromStatus);
    }

    /// <summary>
    /// A deal may only be opened once a target has actually said something. M6 put
    /// Advanced in the pipeline as this boundary, and Interested precedes it.
    /// </summary>
    [Theory]
    [InlineData(OpportunityTargetStage.Identified, false)]
    [InlineData(OpportunityTargetStage.Approved, false)]
    [InlineData(OpportunityTargetStage.Contacted, false)]
    [InlineData(OpportunityTargetStage.Engaged, false)]
    [InlineData(OpportunityTargetStage.Interested, true)]
    [InlineData(OpportunityTargetStage.Advanced, true)]
    [InlineData(OpportunityTargetStage.Passed, false)]
    [InlineData(OpportunityTargetStage.Withdrawn, false)]
    [InlineData(OpportunityTargetStage.Exhausted, false)]
    public void OnlyAnAdvancedTarget_CanOpenANegotiation(OpportunityTargetStage stage, bool openable)
    {
        Assert.Equal(openable, Deal.OpenableFromStages.Contains(stage));
    }

    [Fact]
    public void RecordingTheFirstOffer_MovesTheDealIntoNegotiation()
    {
        Deal deal = Open();

        deal.NoteOfferRecorded(Now, Actor);

        Assert.Equal(DealStatus.Negotiating, deal.Status);
        Assert.Equal(2, deal.Events.Count);
    }

    /// <summary>
    /// A second offer changes nothing about the status, and writes no event saying
    /// so. A timeline full of "still negotiating" rows is a timeline nobody reads.
    /// </summary>
    [Fact]
    public void RecordingAFurtherOffer_AddsNoStatusEvent()
    {
        Deal deal = Open();

        deal.NoteOfferRecorded(Now, Actor);
        int events = deal.Events.Count;

        deal.NoteOfferRecorded(Now, Actor);

        Assert.Equal(DealStatus.Negotiating, deal.Status);
        Assert.Equal(events, deal.Events.Count);
    }

    [Fact]
    public void AcceptingAnOffer_AgreesTheTerms()
    {
        Deal deal = Negotiating();

        deal.NoteOfferAccepted(Now, Actor);

        Assert.Equal(DealStatus.TermsAgreed, deal.Status);
        Assert.True(deal.HasAgreedTerms);

        // Terms agreed is not a licence to keep negotiating over the top of it.
        Assert.False(deal.AcceptsOfferActivity);
    }

    [Fact]
    public void ReopeningAgreedTerms_ReturnsToNegotiation()
    {
        Deal deal = Negotiating();
        deal.NoteOfferAccepted(Now, Actor);

        deal.Reopen(Now, Actor, deal.Version, "The studio came back on the backend.");

        Assert.Equal(DealStatus.Negotiating, deal.Status);
        Assert.True(deal.AcceptsOfferActivity);
    }

    [Fact]
    public void ClosingWithoutAgreement_EndsTheNegotiationAndCanBeReopened()
    {
        Deal deal = Negotiating();

        deal.CloseWithoutAgreement(new DateOnly(2026, 9, 20), Now, Actor, deal.Version, "They passed.");

        Assert.Equal(DealStatus.NoDeal, deal.Status);
        Assert.Equal(new DateOnly(2026, 9, 20), deal.ClosedOn);

        deal.Reopen(Now, Actor, deal.Version);

        Assert.Equal(DealStatus.Negotiating, deal.Status);
        Assert.Null(deal.ClosedOn);
    }

    [Fact]
    public void ACancelledDeal_AcceptsNothing()
    {
        Deal deal = Negotiating();

        deal.Cancel(new DateOnly(2026, 9, 20), Now, Actor, deal.Version, "Opened in error.");

        Assert.Equal(DealStatus.Cancelled, deal.Status);
        Assert.True(deal.IsTerminal);
        Assert.Throws<DomainException>(() => deal.Reopen(Now, Actor, deal.Version));
    }

    [Fact]
    public void EndingBeforeItOpened_IsRefused()
    {
        Deal deal = Negotiating();

        Assert.Throws<DomainException>(() =>
            deal.CloseWithoutAgreement(new DateOnly(2025, 1, 1), Now, Actor, deal.Version));
    }

    [Fact]
    public void AStaleChange_IsRefused()
    {
        Deal deal = Negotiating();

        Assert.Throws<ConcurrencyConflictException>(() =>
            deal.CloseWithoutAgreement(new DateOnly(2026, 9, 20), Now, Actor, deal.Version - 1));
    }

    /// <summary>
    /// The kind chooses which terms are meaningful, so changing it under recorded
    /// offers would invalidate terms that can no longer be corrected.
    /// </summary>
    [Fact]
    public void ChangingTheKind_IsRefusedOnceOffersExist()
    {
        Deal deal = Open();

        deal.UpdateMetadata(
            "The Undertow - Northgate",
            DealKind.Writing,
            Actor,
            Now,
            Actor,
            deal.Version);

        Assert.Equal(DealKind.Writing, deal.Kind);

        deal.NoteOfferRecorded(Now, Actor);

        Assert.Throws<DomainException>(() =>
            deal.UpdateMetadata(
                "The Undertow - Northgate",
                DealKind.Directing,
                Actor,
                Now,
                Actor,
                deal.Version));
    }

    [Fact]
    public void MetadataUpdates_LeaveTheStatusAlone()
    {
        Deal deal = Negotiating();

        deal.UpdateMetadata(
            "Renamed",
            deal.Kind,
            Actor,
            Now,
            Actor,
            deal.Version,
            summary: "A writing deal.",
            strategyNotes: "Hold at 700 and trade backend for guarantee.");

        Assert.Equal(DealStatus.Negotiating, deal.Status);
        Assert.Equal("Renamed", deal.Name);
        Assert.Equal("Hold at 700 and trade backend for guarantee.", deal.StrategyNotes);
    }

    /// <summary>Only a live deal blocks another for the same target and kind.</summary>
    [Theory]
    [InlineData(DealStatus.Draft, true)]
    [InlineData(DealStatus.Negotiating, true)]
    [InlineData(DealStatus.TermsAgreed, true)]
    [InlineData(DealStatus.NoDeal, false)]
    [InlineData(DealStatus.Cancelled, false)]
    public void LiveStatuses_AreTheOnesThatBlockADuplicate(DealStatus status, bool live)
    {
        Assert.Equal(live, Deal.LiveStatuses.Contains(status));
    }

    internal static Deal Open() =>
        Deal.Open(
            Tenant,
            Pursuit,
            Target,
            "The Undertow - Northgate Pictures",
            DealKind.ProjectSale,
            Actor,
            new DateOnly(2026, 9, 7),
            Actor,
            Now);

    private static Deal Negotiating()
    {
        Deal deal = Open();
        deal.NoteOfferRecorded(Now, Actor);
        return deal;
    }
}
