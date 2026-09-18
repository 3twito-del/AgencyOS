using AgencyOS.Client.ViewModels;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// That a link is made by choosing a record, not by typing its identifier.
/// </summary>
/// <remarks>
/// <c>AOS-R001-006</c>. <c>LinkRecordDialog</c> asked for a typed identifier for
/// all fourteen kinds a link may point at. Twelve have a list this organization
/// can be asked for; two belong to a parent record and have none of their own.
/// </remarks>
public sealed class LinkTargetTests
{
    /// <summary>All fourteen kinds the database can enforce are still offered.</summary>
    /// <remarks>
    /// The instruction that matters: a kind is not dropped for being awkward to
    /// pick. A link that was legal yesterday stays legal.
    /// </remarks>
    [Fact]
    public void EveryKindTheDatabaseEnforcesIsStillOffered()
    {
        Assert.Equal(14, LinkTargets.All.Count);

        foreach (string kind in new[]
        {
            "Person", "Company", "TalentProfile", "Material", "Project", "Package",
            "Opportunity", "Submission", "Deal", "Offer", "Contract", "ContractVersion",
            "Invoice", "Payment",
        })
        {
            Assert.Contains(kind, LinkTargets.All);
        }
    }

    /// <summary>Twelve can be chosen from a list.</summary>
    [Fact]
    public void TwelveKindsCanBePicked()
    {
        Assert.Equal(12, LinkTargets.Pickable.Count);
        Assert.All(LinkTargets.Pickable, x => Assert.True(LinkTargets.CanPick(x)));
    }

    /// <summary>The two that cannot say why.</summary>
    /// <remarks>
    /// A reason an operator can read, not a silent absence. Both are children of
    /// another record, which is the whole of the gap.
    /// </remarks>
    [Theory]
    [InlineData("Material", "person")]
    [InlineData("ContractVersion", "contract")]
    public void TheTwoThatCannotSayWhy(string kind, string parent)
    {
        Assert.False(LinkTargets.CanPick(kind));

        string reason = Assert.IsType<string>(LinkTargets.WhyNot(kind));

        Assert.Contains(parent, reason, StringComparison.Ordinal);
    }

    /// <summary>A pickable kind has no excuse recorded against it.</summary>
    [Fact]
    public void APickableKindHasNoExcuse() =>
        Assert.All(LinkTargets.Pickable, x => Assert.Null(LinkTargets.WhyNot(x)));

    /// <summary>The two sets are exactly the whole.</summary>
    /// <remarks>
    /// Without this a kind could fall out of both and simply stop being offered.
    /// </remarks>
    [Fact]
    public void NothingFallsBetweenTheTwoSets()
    {
        Assert.Equal(
            LinkTargets.All.Order(StringComparer.Ordinal),
            LinkTargets.Pickable.Concat(LinkTargets.RequiresAParent.Keys).Order(StringComparer.Ordinal));
    }
}
