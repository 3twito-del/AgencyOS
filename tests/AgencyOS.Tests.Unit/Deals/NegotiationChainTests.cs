using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Deals;
using Xunit;

namespace AgencyOS.Tests.Unit.Deals;

/// <summary>
/// The negotiation chain: one thread, in order, with one answer at a time.
/// </summary>
/// <remarks>
/// M7 keeps a single chronological thread per deal. Branching is not silently
/// tolerated - two standing offers is a refusal rather than a shape the reader has
/// to interpret - and the last check here is the one that matters most: a deal
/// claiming agreed terms with no accepted offer behind it (ADR-0021).
/// </remarks>
public sealed class NegotiationChainTests
{
    [Fact]
    public void AnEmptyThread_IsValidForADraftDeal()
    {
        Assert.Null(DealRules.DescribeChainProblem((int)DealStatus.Draft, []));
    }

    [Fact]
    public void AStandingOffer_IsValidWhileNegotiating()
    {
        OfferNode[] chain = [Node(1, OfferStatus.Open, OfferDirection.Inbound)];

        Assert.Null(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, chain));
    }

    [Fact]
    public void AFullThread_ValidatesEndToEnd()
    {
        OfferNode inbound = Node(1, OfferStatus.Superseded, OfferDirection.Inbound);
        OfferNode counter = Node(2, OfferStatus.Superseded, OfferDirection.Outbound, inbound.OfferId);
        OfferNode revised = Node(3, OfferStatus.Superseded, OfferDirection.Inbound, counter.OfferId);
        OfferNode accepted = Node(4, OfferStatus.Accepted, OfferDirection.Outbound, revised.OfferId);

        OfferNode[] chain = [inbound, counter, revised, accepted];

        Assert.Null(DealRules.DescribeChainProblem((int)DealStatus.TermsAgreed, chain));
        Assert.Equal(accepted.OfferId, DealRules.AcceptedOffer(chain));
        Assert.Equal(accepted.OfferId, DealRules.LatestOffer(chain));
        Assert.Equal(Guid.Empty, DealRules.StandingOffer(chain));
    }

    /// <summary>
    /// The invariant the milestone exists to protect, checked from both sides.
    /// </summary>
    [Fact]
    public void AgreedTermsAndAnAcceptedOffer_MustAgree()
    {
        OfferNode accepted = Node(1, OfferStatus.Accepted, OfferDirection.Inbound);
        OfferNode open = Node(1, OfferStatus.Open, OfferDirection.Inbound);

        // An accepted offer with the deal still negotiating.
        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, [accepted]));

        // Agreed terms with nothing accepted.
        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.TermsAgreed, [open]));

        // And both correct arrangements.
        Assert.Null(DealRules.DescribeChainProblem((int)DealStatus.TermsAgreed, [accepted]));
        Assert.Null(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, [open]));
    }

    [Fact]
    public void TwoStandingOffers_AreRefused()
    {
        OfferNode[] chain =
        [
            Node(1, OfferStatus.Open, OfferDirection.Inbound),
            Node(2, OfferStatus.Open, OfferDirection.Outbound),
        ];

        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, chain));
    }

    [Fact]
    public void TwoAcceptedOffers_AreRefused()
    {
        OfferNode[] chain =
        [
            Node(1, OfferStatus.Accepted, OfferDirection.Inbound),
            Node(2, OfferStatus.Accepted, OfferDirection.Outbound),
        ];

        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.TermsAgreed, chain));
    }

    [Fact]
    public void AnOfferCannotAnswerItself()
    {
        Guid id = Guid.CreateVersion7();

        OfferNode self = new()
        {
            OfferId = id,
            Sequence = 1,
            State = (int)OfferStatus.Open,
            Direction = (int)OfferDirection.Inbound,
            RespondsToOfferId = id,
        };

        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, [self]));
    }

    [Fact]
    public void AnOfferCannotAnswerSomethingOutsideTheDeal()
    {
        OfferNode orphan = Node(1, OfferStatus.Open, OfferDirection.Outbound, Guid.CreateVersion7());

        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, [orphan]));
    }

    /// <summary>
    /// An answer comes after what it answers. Order is the canonical sequence, so
    /// this catches a thread assembled out of order rather than one whose
    /// identifiers happen to sort oddly.
    /// </summary>
    [Fact]
    public void AnOfferCannotAnswerSomethingThatCameAfterIt()
    {
        OfferNode later = Node(2, OfferStatus.Superseded, OfferDirection.Inbound);
        OfferNode earlier = Node(1, OfferStatus.Open, OfferDirection.Outbound, later.OfferId);

        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, [later, earlier]));
    }

    [Fact]
    public void TwoOffersCannotShareAPosition()
    {
        OfferNode[] chain =
        [
            Node(1, OfferStatus.Superseded, OfferDirection.Inbound),
            Node(1, OfferStatus.Open, OfferDirection.Outbound),
        ];

        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, chain));
    }

    /// <summary>
    /// Countering an accepted offer would change what was agreed without anybody
    /// reopening the negotiation.
    /// </summary>
    [Fact]
    public void AnAcceptedOfferCannotBeAnswered()
    {
        OfferNode accepted = Node(1, OfferStatus.Accepted, OfferDirection.Inbound);
        OfferNode counter = Node(2, OfferStatus.Open, OfferDirection.Outbound, accepted.OfferId);

        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.TermsAgreed, [accepted, counter]));

        Assert.False(DealRules.MayBeAnswered(accepted));
        Assert.True(DealRules.MayBeAnswered(Node(1, OfferStatus.Open, OfferDirection.Inbound)));
    }

    [Theory]
    [InlineData(OfferStatus.Draft)]
    [InlineData(OfferStatus.Accepted)]
    [InlineData(OfferStatus.Rejected)]
    [InlineData(OfferStatus.Withdrawn)]
    [InlineData(OfferStatus.Expired)]
    [InlineData(OfferStatus.Superseded)]
    public void OnlyAStandingOfferMayBeAnswered(OfferStatus status)
    {
        Assert.False(DealRules.MayBeAnswered(Node(1, status, OfferDirection.Inbound)));
    }

    [Fact]
    public void AnUnknownStateOrDirection_IsRefused()
    {
        OfferNode badState = new()
        {
            OfferId = Guid.CreateVersion7(),
            Sequence = 1,
            State = 99,
            Direction = (int)OfferDirection.Inbound,
            RespondsToOfferId = null,
        };

        OfferNode badDirection = new()
        {
            OfferId = Guid.CreateVersion7(),
            Sequence = 1,
            State = (int)OfferStatus.Open,
            Direction = 99,
            RespondsToOfferId = null,
        };

        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, [badState]));
        Assert.NotNull(DealRules.DescribeChainProblem((int)DealStatus.Negotiating, [badDirection]));
    }

    [Fact]
    public void TheNextPositionFollowsTheHighestSoFar()
    {
        Assert.Equal(1, DealRules.NextSequence([]));

        OfferNode[] chain =
        [
            Node(1, OfferStatus.Superseded, OfferDirection.Inbound),
            Node(2, OfferStatus.Open, OfferDirection.Outbound),
        ];

        Assert.Equal(3, DealRules.NextSequence(chain));
    }

    /// <summary>
    /// Position, not identity, decides the order. Version 7 GUIDs happen to sort
    /// by creation time, and a thread that leaned on that would reorder silently
    /// the day the identifier scheme changed.
    /// </summary>
    [Fact]
    public void TheThreadIsOrderedByPositionRatherThanIdentity()
    {
        OfferNode second = Node(2, OfferStatus.Open, OfferDirection.Outbound);
        OfferNode first = Node(1, OfferStatus.Superseded, OfferDirection.Inbound);

        // Deliberately handed to the kernel in the wrong order.
        Assert.Equal(second.OfferId, DealRules.LatestOffer([second, first]));
        Assert.Equal(second.OfferId, DealRules.StandingOffer([second, first]));
    }

    // ------------------------------------------------------------- properties

    /// <summary>
    /// Any thread built the way the application builds one validates.
    /// </summary>
    /// <remarks>
    /// Generated systematically over length and ending, so every reachable shape
    /// of a well-formed negotiation is covered rather than sampled.
    /// </remarks>
    [Theory]
    [MemberData(nameof(WellFormedThreads))]
    public void AnyWellFormedThread_Validates(int length, OfferStatus ending)
    {
        (DealStatus deal, OfferNode[] chain) = BuildThread(length, ending);

        Assert.Null(DealRules.DescribeChainProblem((int)deal, chain));
        Assert.True(DealRules.IsChainValid((int)deal, chain));
    }

    /// <summary>
    /// At most one offer is standing and at most one is accepted, in every
    /// well-formed thread.
    /// </summary>
    [Theory]
    [MemberData(nameof(WellFormedThreads))]
    public void AtMostOneOfferIsStandingAndAtMostOneIsAccepted(int length, OfferStatus ending)
    {
        (_, OfferNode[] chain) = BuildThread(length, ending);

        Assert.True(chain.Count(node => node.State == (int)OfferStatus.Open) <= 1);
        Assert.True(chain.Count(node => node.State == (int)OfferStatus.Accepted) <= 1);
    }

    public static TheoryData<int, OfferStatus> WellFormedThreads()
    {
        TheoryData<int, OfferStatus> data = [];

        foreach (OfferStatus ending in new[]
        {
            OfferStatus.Open,
            OfferStatus.Accepted,
            OfferStatus.Rejected,
            OfferStatus.Withdrawn,
            OfferStatus.Expired,
        })
        {
            for (int length = 1; length <= 6; length++)
            {
                data.Add(length, ending);
            }
        }

        return data;
    }

    /// <summary>
    /// A thread of the given length whose last offer ends the given way, with
    /// every earlier offer superseded by the one that answered it.
    /// </summary>
    private static (DealStatus Deal, OfferNode[] Chain) BuildThread(int length, OfferStatus ending)
    {
        List<OfferNode> chain = [];

        for (int position = 1; position <= length; position++)
        {
            bool last = position == length;

            OfferDirection direction =
                position % 2 == 1 ? OfferDirection.Inbound : OfferDirection.Outbound;

            chain.Add(Node(
                position,
                last ? ending : OfferStatus.Superseded,
                direction,
                position == 1 ? null : chain[^1].OfferId));
        }

        DealStatus deal = ending == OfferStatus.Accepted
            ? DealStatus.TermsAgreed
            : DealStatus.Negotiating;

        return (deal, [.. chain]);
    }

    private static OfferNode Node(
        int sequence,
        OfferStatus status,
        OfferDirection direction,
        Guid? respondsTo = null) =>
        new()
        {
            OfferId = Guid.CreateVersion7(),
            Sequence = sequence,
            State = (int)status,
            Direction = (int)direction,
            RespondsToOfferId = respondsTo,
        };
}
