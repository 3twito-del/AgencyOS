using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Deals;
using Xunit;

namespace AgencyOS.Tests.Unit.Deals;

/// <summary>
/// The C# to F# boundary, walked in both directions.
/// </summary>
/// <remarks>
/// <para>
/// The rules kernel and the domain each declare the deal and offer vocabularies -
/// the kernel as discriminated unions, the domain as enums - and they agree by
/// convention rather than by compilation. These tests are what turns that
/// convention into a build failure: a state added on either side without the other
/// shows up here immediately, rather than as a transition nothing enforces.
/// </para>
/// <para>
/// This is the specific risk the F# pilot took on, and it is cheap to close
/// (ADR-0021).
/// </para>
/// </remarks>
public sealed class DealRulesBoundaryTests
{
    [Fact]
    public void EveryDealStatus_IsKnownToTheRulesKernel()
    {
        int[] kernel = DealRules.KnownDealStates();

        foreach (DealStatus status in Enum.GetValues<DealStatus>())
        {
            Assert.Contains((int)status, kernel);
        }
    }

    [Fact]
    public void EveryKernelDealState_HasADomainStatus()
    {
        foreach (int code in DealRules.KnownDealStates())
        {
            Assert.True(
                Enum.IsDefined((DealStatus)code),
                $"The kernel knows deal state {code} and the domain does not.");
        }
    }

    [Fact]
    public void TheTwoDealVocabularies_AreTheSameSize()
    {
        Assert.Equal(Enum.GetValues<DealStatus>().Length, DealRules.KnownDealStates().Length);
    }

    [Fact]
    public void EveryDealTransition_IsKnownToTheRulesKernel()
    {
        int[] kernel = DealRules.KnownDealTriggers();

        foreach (DealTransition transition in Enum.GetValues<DealTransition>())
        {
            Assert.Contains((int)transition, kernel);
        }

        Assert.Equal(Enum.GetValues<DealTransition>().Length, kernel.Length);
    }

    [Fact]
    public void EveryOfferStatus_IsKnownToTheRulesKernel()
    {
        int[] kernel = DealRules.KnownOfferStates();

        foreach (OfferStatus status in Enum.GetValues<OfferStatus>())
        {
            Assert.Contains((int)status, kernel);
        }

        Assert.Equal(Enum.GetValues<OfferStatus>().Length, kernel.Length);
    }

    [Fact]
    public void EveryKernelOfferState_HasADomainStatus()
    {
        foreach (int code in DealRules.KnownOfferStates())
        {
            Assert.True(
                Enum.IsDefined((OfferStatus)code),
                $"The kernel knows offer state {code} and the domain does not.");
        }
    }

    [Fact]
    public void EveryOfferTransition_IsKnownToTheRulesKernel()
    {
        int[] kernel = DealRules.KnownOfferTriggers();

        foreach (OfferTransition transition in Enum.GetValues<OfferTransition>())
        {
            Assert.Contains((int)transition, kernel);
        }

        Assert.Equal(Enum.GetValues<OfferTransition>().Length, kernel.Length);
    }

    [Fact]
    public void EveryOfferDirection_IsKnownToTheRulesKernel()
    {
        int[] kernel = DealRules.KnownDirections();

        foreach (OfferDirection direction in Enum.GetValues<OfferDirection>())
        {
            Assert.Contains((int)direction, kernel);
        }

        Assert.Equal(Enum.GetValues<OfferDirection>().Length, kernel.Length);
    }

    /// <summary>
    /// The value kinds are the sharpest of these: a kind the domain offers and the
    /// kernel cannot parse would be a term that validates as anything.
    /// </summary>
    [Fact]
    public void EveryTermValueKind_CanBeParsedByTheRulesKernel()
    {
        int[] kernel = DealRules.KnownValueKinds();

        foreach (TermValueKind kind in Enum.GetValues<TermValueKind>())
        {
            Assert.Contains((int)kind, kernel);
        }

        Assert.Equal(Enum.GetValues<TermValueKind>().Length, kernel.Length);
    }

    /// <summary>Zero is reserved for "no answer" and must never be a real value.</summary>
    [Fact]
    public void ZeroIsNeverAState()
    {
        Assert.DoesNotContain(0, DealRules.KnownDealStates());
        Assert.DoesNotContain(0, DealRules.KnownOfferStates());
        Assert.DoesNotContain(0, DealRules.KnownDealTriggers());
        Assert.DoesNotContain(0, DealRules.KnownOfferTriggers());
        Assert.DoesNotContain(0, DealRules.KnownValueKinds());
        Assert.DoesNotContain(0, DealRules.KnownDirections());
    }

    /// <summary>
    /// A value the kernel does not recognise is refused rather than treated as
    /// permissive, which is the failure mode that matters.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(98)]
    [InlineData(int.MaxValue)]
    public void AnUnknownState_PermitsNothing(int unknown)
    {
        Assert.Equal(0, DealRules.NextDealState(unknown, (int)DealTransition.Cancelled));
        Assert.Equal(0, DealRules.NextOfferState(unknown, (int)OfferTransition.Accepted));
        Assert.False(DealRules.IsOfferEditable(unknown));
        Assert.False(DealRules.DealAcceptsOfferActivity(unknown));
        Assert.Empty(DealRules.ReachableDealStates(unknown));
        Assert.Empty(DealRules.ReachableOfferStates(unknown));
    }

    /// <summary>
    /// The domain's published transition table is the kernel's, not a copy of it.
    /// </summary>
    [Fact]
    public void TheDomainsPublishedTables_ComeFromTheKernel()
    {
        foreach (DealStatus status in Enum.GetValues<DealStatus>())
        {
            IReadOnlySet<DealStatus> reachable = Deal.ReachableFrom(status);

            foreach (DealTransition transition in Enum.GetValues<DealTransition>())
            {
                int next = DealRules.NextDealState((int)status, (int)transition);

                if (next != 0)
                {
                    Assert.Contains((DealStatus)next, reachable);
                }
            }
        }

        foreach (OfferStatus status in Enum.GetValues<OfferStatus>())
        {
            IReadOnlySet<OfferStatus> reachable = Offer.ReachableFrom(status);

            foreach (OfferTransition transition in Enum.GetValues<OfferTransition>())
            {
                int next = DealRules.NextOfferState((int)status, (int)transition);

                if (next != 0)
                {
                    Assert.Contains((OfferStatus)next, reachable);
                }
            }
        }
    }
}
