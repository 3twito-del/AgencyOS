using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Deals;

/// <summary>
/// The offer state machine, enumerated rather than sampled.
/// </summary>
/// <remarks>
/// All 49 status/transition pairs, against a table written here rather than read
/// from the kernel. The illegal ones carry the weight: an accepted offer that
/// could be rejected, or a recorded one that could go back to draft, would let the
/// system rewrite what the parties did (ADR-0021).
/// </remarks>
public sealed class OfferTransitionTests
{
    private static readonly Dictionary<(OfferStatus From, OfferTransition Trigger), OfferStatus> Legal = new()
    {
        [(OfferStatus.Draft, OfferTransition.Opened)] = OfferStatus.Open,
        [(OfferStatus.Draft, OfferTransition.Withdrawn)] = OfferStatus.Withdrawn,

        [(OfferStatus.Open, OfferTransition.Accepted)] = OfferStatus.Accepted,
        [(OfferStatus.Open, OfferTransition.Rejected)] = OfferStatus.Rejected,
        [(OfferStatus.Open, OfferTransition.Withdrawn)] = OfferStatus.Withdrawn,
        [(OfferStatus.Open, OfferTransition.Expired)] = OfferStatus.Expired,
        [(OfferStatus.Open, OfferTransition.AnsweredByCounter)] = OfferStatus.Superseded,

        [(OfferStatus.Accepted, OfferTransition.UnwoundByReopen)] = OfferStatus.Superseded,
    };

    public static TheoryData<OfferStatus, OfferTransition> EveryPair()
    {
        TheoryData<OfferStatus, OfferTransition> data = [];

        foreach (OfferStatus status in Enum.GetValues<OfferStatus>())
        {
            foreach (OfferTransition transition in Enum.GetValues<OfferTransition>())
            {
                data.Add(status, transition);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void EveryTransition_MatchesThePublishedTable(OfferStatus from, OfferTransition trigger)
    {
        bool expected = Legal.ContainsKey((from, trigger));

        Assert.Equal(expected, Offer.Permits(from, trigger));

        if (expected)
        {
            Assert.Contains(Legal[(from, trigger)], Offer.ReachableFrom(from));
        }
    }

    [Fact]
    public void TheTableIsCompletelyEnumerated()
    {
        int pairs = Enum.GetValues<OfferStatus>().Length * Enum.GetValues<OfferTransition>().Length;

        Assert.Equal(49, pairs);
        Assert.Equal(8, Legal.Count);
    }

    /// <summary>
    /// An accepted offer can only be superseded, and only by a reopen. It can
    /// never be rejected, withdrawn or expired: each of those would claim somebody
    /// did something after agreement that they did not do.
    /// </summary>
    [Fact]
    public void AnAcceptedOffer_CanOnlyBeUnwoundByReopening()
    {
        foreach (OfferTransition transition in Enum.GetValues<OfferTransition>())
        {
            bool legal = transition == OfferTransition.UnwoundByReopen;

            Assert.Equal(legal, Offer.Permits(OfferStatus.Accepted, transition));
        }
    }

    [Theory]
    [InlineData(OfferStatus.Rejected)]
    [InlineData(OfferStatus.Withdrawn)]
    [InlineData(OfferStatus.Expired)]
    [InlineData(OfferStatus.Superseded)]
    public void FinishedOffers_AcceptNothing(OfferStatus terminal)
    {
        Assert.Empty(Offer.ReachableFrom(terminal));
    }

    /// <summary>Only a draft is editable. This is the immutability rule in one line.</summary>
    [Theory]
    [InlineData(OfferStatus.Draft, true)]
    [InlineData(OfferStatus.Open, false)]
    [InlineData(OfferStatus.Accepted, false)]
    [InlineData(OfferStatus.Rejected, false)]
    [InlineData(OfferStatus.Withdrawn, false)]
    [InlineData(OfferStatus.Expired, false)]
    [InlineData(OfferStatus.Superseded, false)]
    public void OnlyADraft_IsEditable(OfferStatus status, bool editable)
    {
        Assert.Equal(editable, AgencyOS.Deals.Rules.DealRules.IsOfferEditable((int)status));
    }

    /// <summary>Supersession is a consequence, never a request.</summary>
    [Theory]
    [InlineData(OfferTransition.Opened, true)]
    [InlineData(OfferTransition.Accepted, true)]
    [InlineData(OfferTransition.Rejected, true)]
    [InlineData(OfferTransition.Withdrawn, true)]
    [InlineData(OfferTransition.Expired, true)]
    [InlineData(OfferTransition.AnsweredByCounter, false)]
    [InlineData(OfferTransition.UnwoundByReopen, false)]
    public void SupersessionIsNotCallerRequestable(OfferTransition transition, bool requestable)
    {
        Assert.Equal(
            requestable,
            AgencyOS.Deals.Rules.DealRules.IsCallerRequestableOfferTrigger((int)transition));
    }

    /// <summary>
    /// Recording an offer and accepting one move the deal; nothing else does.
    /// </summary>
    [Theory]
    [InlineData(OfferTransition.Opened, DealTransition.OfferRecorded)]
    [InlineData(OfferTransition.Accepted, DealTransition.OfferAccepted)]
    [InlineData(OfferTransition.Rejected, null)]
    [InlineData(OfferTransition.Withdrawn, null)]
    [InlineData(OfferTransition.Expired, null)]
    [InlineData(OfferTransition.AnsweredByCounter, null)]
    [InlineData(OfferTransition.UnwoundByReopen, null)]
    public void OfferActs_ImplyTheRightDealConsequence(
        OfferTransition transition,
        DealTransition? consequence)
    {
        int actual = AgencyOS.Deals.Rules.DealRules.DealConsequenceOf((int)transition);

        Assert.Equal(consequence is { } expected ? (int)expected : 0, actual);
    }

    /// <summary>
    /// The vocabulary carries no offer-shaped deal language and no contract
    /// language. An offer is accepted or it is not; signing is M8.
    /// </summary>
    [Fact]
    public void TheStatusVocabulary_StopsAtAcceptance()
    {
        string[] names = [.. Enum.GetNames<OfferStatus>()];

        Assert.DoesNotContain("Signed", names);
        Assert.DoesNotContain("Executed", names);
        Assert.DoesNotContain("Countersigned", names);
        Assert.DoesNotContain("Paid", names);

        Assert.Contains("Accepted", names);
        Assert.Contains("Superseded", names);
    }
}

/// <summary>The offer aggregate: recording, freezing and answering.</summary>
public sealed class OfferTests
{
    private static readonly OrganizationId Tenant = new(Guid.CreateVersion7());
    private static readonly UserId Actor = new(Guid.CreateVersion7());
    private static readonly DealId Deal = DealId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARecordedOffer_IsStandingAndFrozenImmediately()
    {
        Offer offer = Recorded();

        Assert.Equal(OfferStatus.Open, offer.Status);
        Assert.True(offer.IsStanding);
        Assert.False(offer.IsEditable);
        Assert.Equal(Now, offer.CommunicatedAt);

        OfferEvent recorded = Assert.Single(offer.Events);
        Assert.Equal(OfferTransition.Opened, recorded.Transition);
    }

    /// <summary>
    /// Offers are usually written down after the call, so the communicated time is
    /// freely backdated. That makes the thread a business record rather than an
    /// audit trail, which the audit log remains.
    /// </summary>
    [Fact]
    public void AnOfferCanBeBackdated_ButNotPostdated()
    {
        Offer offer = Draft();
        offer.Open(Now, Actor, offer.Version, Now.AddDays(-3));

        Assert.Equal(Now.AddDays(-3), offer.CommunicatedAt);

        Offer postdated = Draft();

        Assert.Throws<DomainException>(() =>
            postdated.Open(Now, Actor, postdated.Version, Now.AddDays(1)));
    }

    [Fact]
    public void ADraft_AcceptsTermsAndCanBeRecorded()
    {
        Offer offer = Offer.StartDraft(Tenant, Deal, OfferDirection.Outbound, 1, Actor, Now);

        Assert.True(offer.IsEditable);
        Assert.Empty(offer.Events);

        offer.AddTerm(
            DealKind.Writing,
            DealTermCode.GuaranteedCompensation,
            DealTermValue.OfMoney(Money.Create(500_000m, "USD")),
            Now,
            offer.Version);

        offer.Open(Now, Actor, offer.Version);

        Assert.Equal(OfferStatus.Open, offer.Status);
        Assert.False(offer.IsEditable);
    }

    /// <summary>
    /// A proposal that proposes nothing is not a negotiating position, and an
    /// empty snapshot in the chain is one nothing downstream can compare against.
    /// </summary>
    [Fact]
    public void ADraftWithNoTerms_CannotBeRecorded()
    {
        Offer offer = Offer.StartDraft(Tenant, Deal, OfferDirection.Outbound, 1, Actor, Now);

        Assert.Throws<DomainException>(() => offer.Open(Now, Actor, offer.Version));
    }

    /// <summary>
    /// The immutability rule, exercised through every editing path there is.
    /// </summary>
    [Fact]
    public void ARecordedOffersTerms_CannotBeChangedThroughAnyPath()
    {
        Offer offer = Recorded();

        Assert.Throws<DomainException>(() =>
            offer.AddTerm(
                DealKind.Writing,
                DealTermCode.Bonus,
                DealTermValue.OfMoney(Money.Create(50_000m, "USD")),
                Now,
                offer.Version));

        Assert.Throws<DomainException>(() =>
            offer.UpdateTerm(
                DealKind.Writing,
                DealTermCode.GuaranteedCompensation,
                DealTermValue.OfMoney(Money.Create(650_000m, "USD")),
                Now,
                offer.Version));

        Assert.Throws<DomainException>(() =>
            offer.RemoveTerm(DealTermCode.GuaranteedCompensation, Now, offer.Version));

        Assert.Throws<DomainException>(() => offer.UpdateDraft(Now, offer.Version, summary: "Edited"));
    }

    /// <summary>An accepted offer stays exactly what was accepted.</summary>
    [Fact]
    public void AnAcceptedOffer_KeepsTheTermsThatWereAccepted()
    {
        Offer offer = Recorded();

        offer.Accept(Now, Actor, offer.Version);

        Assert.Equal(OfferStatus.Accepted, offer.Status);

        OfferTerm term = Assert.Single(offer.Terms);
        Assert.Equal(500_000m, term.AsMoney!.Value.Amount);

        Assert.Throws<DomainException>(() =>
            offer.UpdateTerm(
                DealKind.Writing,
                DealTermCode.GuaranteedCompensation,
                DealTermValue.OfMoney(Money.Create(1m, "USD")),
                Now,
                offer.Version));
    }

    /// <summary>
    /// Nothing expires because time passed. An offer that never stated a lapse
    /// cannot lapse, and one whose stated date is still ahead has not lapsed yet.
    /// </summary>
    [Fact]
    public void ExpiryIsRecorded_NeverInferred()
    {
        Offer never = Recorded();

        Assert.Throws<DomainException>(() => never.Expire(Now, Actor, never.Version));

        Offer later = Draft(expiresAt: Now.AddDays(7));
        later.Open(Now, Actor, later.Version);

        Assert.Throws<DomainException>(() => later.Expire(Now, Actor, later.Version));

        // Made ten days ago with a week to answer, so the lapse is genuinely behind
        // us rather than a date that precedes the offer itself.
        Offer lapsed = Draft(expiresAt: Now.AddDays(-3));
        lapsed.Open(Now, Actor, lapsed.Version, Now.AddDays(-10));

        lapsed.Expire(Now, Actor, lapsed.Version);

        Assert.Equal(OfferStatus.Expired, lapsed.Status);
    }

    /// <summary>
    /// Checked when the offer is recorded, which is the first moment a draft has a
    /// communicated instant to compare an expiry against.
    /// </summary>
    [Fact]
    public void AnOfferCannotExpireBeforeItWasMade()
    {
        Offer offer = Draft(expiresAt: Now.AddDays(-1));

        Assert.Throws<DomainException>(() => offer.Open(Now, Actor, offer.Version, Now));
    }

    /// <summary>
    /// Rejected, withdrawn and expired mean three different things, and the
    /// difference is who did what. "No response" is none of them.
    /// </summary>
    [Fact]
    public void RejectionWithdrawalAndExpiry_AreDistinctOutcomes()
    {
        Offer rejected = Recorded();
        rejected.Reject(Now, Actor, rejected.Version, "They passed on the number.");
        Assert.Equal(OfferStatus.Rejected, rejected.Status);

        Offer withdrawn = Recorded();
        withdrawn.Withdraw(Now, Actor, withdrawn.Version, "We pulled it.");
        Assert.Equal(OfferStatus.Withdrawn, withdrawn.Status);

        Assert.DoesNotContain("NoResponse", Enum.GetNames<OfferStatus>());
        Assert.DoesNotContain("Ignored", Enum.GetNames<OfferStatus>());
    }

    [Fact]
    public void SupersedingAnOffer_RecordsWhatAnsweredIt()
    {
        Offer offer = Recorded();
        OfferId counter = OfferId.New();

        offer.NoteAnsweredByCounter(Now, Actor, counter);

        Assert.Equal(OfferStatus.Superseded, offer.Status);
        Assert.Contains(counter.ToString(), offer.Events.Last().Reason);
    }

    [Fact]
    public void AStaleAnswer_IsRefused()
    {
        Offer offer = Recorded();

        Assert.Throws<ConcurrencyConflictException>(() =>
            offer.Accept(Now, Actor, offer.Version - 1));
    }

    [Fact]
    public void ASequenceMustBePositive()
    {
        Assert.Throws<DomainException>(() =>
            Offer.StartDraft(Tenant, Deal, OfferDirection.Inbound, 0, Actor, Now));
    }

    /// <summary>
    /// A response from the other side is a counter; one from the same side is a
    /// revision of one's own position. Derived from the two sides, never stored.
    /// </summary>
    [Theory]
    [InlineData(OfferDirection.Inbound, OfferDirection.Outbound, OfferResponseKind.Counter)]
    [InlineData(OfferDirection.Outbound, OfferDirection.Inbound, OfferResponseKind.Counter)]
    [InlineData(OfferDirection.Inbound, OfferDirection.Inbound, OfferResponseKind.Revision)]
    [InlineData(OfferDirection.Outbound, OfferDirection.Outbound, OfferResponseKind.Revision)]
    public void AResponseIsClassifiedBySide(
        OfferDirection answered,
        OfferDirection answering,
        OfferResponseKind expected)
    {
        Assert.Equal(expected, Offer.Classify(answered, answering));
    }

    [Fact]
    public void ATermCodeAppearsAtMostOncePerOffer()
    {
        Offer offer = Offer.StartDraft(Tenant, Deal, OfferDirection.Outbound, 1, Actor, Now);

        offer.AddTerm(
            DealKind.Writing,
            DealTermCode.Fee,
            DealTermValue.OfMoney(Money.Create(100_000m, "USD")),
            Now,
            offer.Version);

        Assert.Throws<DomainException>(() =>
            offer.AddTerm(
                DealKind.Writing,
                DealTermCode.Fee,
                DealTermValue.OfMoney(Money.Create(120_000m, "USD")),
                Now,
                offer.Version));
    }

    [Fact]
    public void ADraftTerm_CanBeChangedAndRemoved()
    {
        Offer offer = Offer.StartDraft(Tenant, Deal, OfferDirection.Outbound, 1, Actor, Now);

        offer.AddTerm(
            DealKind.Writing,
            DealTermCode.Fee,
            DealTermValue.OfMoney(Money.Create(100_000m, "USD")),
            Now,
            offer.Version);

        offer.UpdateTerm(
            DealKind.Writing,
            DealTermCode.Fee,
            DealTermValue.OfMoney(Money.Create(140_000m, "USD")),
            Now,
            offer.Version);

        Assert.Equal(140_000m, Assert.Single(offer.Terms).AsMoney!.Value.Amount);

        offer.RemoveTerm(DealTermCode.Fee, Now, offer.Version);

        Assert.Empty(offer.Terms);
    }

    /// <summary>Abandoning a draft is recorded, not deleted.</summary>
    [Fact]
    public void ADraftCanBeAbandoned()
    {
        Offer offer = Offer.StartDraft(Tenant, Deal, OfferDirection.Outbound, 1, Actor, Now);

        offer.Withdraw(Now, Actor, offer.Version, "Never went out.");

        Assert.Equal(OfferStatus.Withdrawn, offer.Status);
    }

    /// <summary>
    /// A draft carrying one term, ready to be recorded.
    /// </summary>
    /// <remarks>
    /// Every offer starts here. There is no factory that produces an already-frozen
    /// one, because terms may only be added while a draft is editable.
    /// </remarks>
    internal static Offer Draft(DateTimeOffset? expiresAt = null)
    {
        Offer offer = Offer.StartDraft(
            Tenant, Deal, OfferDirection.Inbound, 1, Actor, Now, expiresAt: expiresAt);

        offer.AddTerm(
            DealKind.Writing,
            DealTermCode.GuaranteedCompensation,
            DealTermValue.OfMoney(Money.Create(500_000m, "USD")),
            Now,
            offer.Version);

        return offer;
    }

    internal static Offer Recorded()
    {
        Offer offer = Draft();

        offer.Open(Now, Actor, offer.Version);

        return offer;
    }
}
