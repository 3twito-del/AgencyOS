using AgencyOS.Application.Ai;
using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Ai;

/// <summary>
/// What AgencyOS is willing to transmit, and to whom.
/// </summary>
/// <remarks>
/// <para>
/// Two questions that look alike and are not: whether somebody may read a record,
/// and whether AgencyOS may send it to a model provider. A firm can perfectly
/// reasonably let its analysts read something it will not put in anybody else's
/// datacentre, and every test here is about the second question (§5, §42).
/// </para>
/// <para>
/// The mapping is pessimistic on purpose. Where a classification could reasonably
/// land on two levels it lands on the higher one: being wrong downward breaks
/// somebody's confidence, and being wrong upward makes a brief less useful.
/// </para>
/// </remarks>
public sealed class ModelDataClassificationTests
{
    private static readonly OrganizationId Org = new(Guid.CreateVersion7());
    private static readonly UserId Admin = new(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A source-sensitive signal is protected, not merely confidential.
    /// </summary>
    /// <remarks>
    /// What it protects is a person's identity. The damage from disclosing who
    /// spoke is different in kind from the damage of disclosing a price, so it does
    /// not share a level with commercial confidence (ADR-0030).
    /// </remarks>
    [Theory]
    [InlineData(IntelligenceSensitivity.Internal, ModelDataSensitivity.Internal)]
    [InlineData(IntelligenceSensitivity.Confidential, ModelDataSensitivity.Confidential)]
    [InlineData(IntelligenceSensitivity.SourceSensitive, ModelDataSensitivity.Protected)]
    [InlineData(IntelligenceSensitivity.Restricted, ModelDataSensitivity.Restricted)]
    public void IntelligenceClassificationMapsUpward(
        IntelligenceSensitivity sensitivity, ModelDataSensitivity expected)
    {
        Assert.Equal(expected, ModelDataPolicy.Classify(sensitivity));
    }

    /// <summary>
    /// Privileged is protected, because transmitting it may be the act that waives
    /// the privilege.
    /// </summary>
    /// <remarks>
    /// Whether sending a privileged document to a third-party processor waives
    /// privilege is a question AgencyOS is in no position to answer, so it does not
    /// send the document (ADR-0025).
    /// </remarks>
    [Theory]
    [InlineData(DocumentSensitivity.Internal, ModelDataSensitivity.Internal)]
    [InlineData(DocumentSensitivity.Confidential, ModelDataSensitivity.Confidential)]
    [InlineData(DocumentSensitivity.Financial, ModelDataSensitivity.Confidential)]
    [InlineData(DocumentSensitivity.Privileged, ModelDataSensitivity.Protected)]
    [InlineData(DocumentSensitivity.Restricted, ModelDataSensitivity.Restricted)]
    public void DocumentClassificationMapsUpward(
        DocumentSensitivity sensitivity, ModelDataSensitivity expected)
    {
        Assert.Equal(expected, ModelDataPolicy.Classify(sensitivity));
    }

    /// <summary>
    /// A private mailbox is protected even where the reader is entitled to it.
    /// </summary>
    /// <remarks>
    /// The people in those messages did not choose AgencyOS, let alone a provider.
    /// </remarks>
    [Theory]
    [InlineData(MailboxVisibility.Private, ModelDataSensitivity.Protected)]
    [InlineData(MailboxVisibility.Shared, ModelDataSensitivity.Confidential)]
    public void MailboxVisibilityMapsUpward(
        MailboxVisibility visibility, ModelDataSensitivity expected)
    {
        Assert.Equal(expected, ModelDataPolicy.Classify(visibility));
    }

    /// <summary>
    /// A block is only as transmissible as its most sensitive part.
    /// </summary>
    /// <remarks>
    /// The maximum, never an average. The difference is between refusing a prompt
    /// and sending a confidence buried in ten pages of ordinary material.
    /// </remarks>
    [Fact]
    public void TheStrictestClassificationGoverns()
    {
        Assert.Equal(
            ModelDataSensitivity.Protected,
            ModelDataPolicy.Highest(
            [
                ModelDataSensitivity.Internal,
                ModelDataSensitivity.Protected,
                ModelDataSensitivity.Confidential,
            ]));

        Assert.Equal(
            ModelDataSensitivity.Internal, ModelDataPolicy.Highest([]));
    }

    /// <summary>An organization with no policy row transmits nothing.</summary>
    /// <remarks>
    /// Two ways of saying no — the absent row and the disabled flag — and no way to
    /// accidentally say yes (§43).
    /// </remarks>
    [Fact]
    public void TheDefaultPolicyIsClosed()
    {
        AiProviderPolicy policy = AiProviderPolicy.ClosedDefault(Org, "fake");

        Assert.False(policy.IsEnabled);
        Assert.False(policy.AllowsCanonicalWriteProposals);
        Assert.Equal(ModelDataSensitivity.Internal, policy.MaximumSensitivity);
        Assert.Equal(0, policy.Version);
        Assert.False(policy.Permits(ModelDataSensitivity.Internal));
    }

    /// <summary>
    /// Restricted has no reachable ceiling.
    /// </summary>
    /// <remarks>
    /// Not "requires an administrator" and not "requires a flag". There is no
    /// setting at any level that permits it, which is what makes the classification
    /// mean something rather than being advisory (§5, §43).
    /// </remarks>
    [Fact]
    public void NoCeilingReachesRestricted()
    {
        Assert.Throws<DomainException>(() => AiProviderPolicy.Create(
            Org, "fake", true, ModelDataSensitivity.Restricted, false, Admin, Now));

        AiProviderPolicy policy = Enabled(ModelDataSensitivity.Protected);

        Assert.Throws<DomainException>(() => policy.Update(
            true, ModelDataSensitivity.Restricted, false, Admin, Now, policy.Version));

        Assert.False(policy.Permits(ModelDataSensitivity.Restricted));
    }

    /// <summary>A ceiling permits itself and everything below it.</summary>
    [Theory]
    [InlineData(ModelDataSensitivity.Internal, ModelDataSensitivity.Internal, true)]
    [InlineData(ModelDataSensitivity.Internal, ModelDataSensitivity.Confidential, false)]
    [InlineData(ModelDataSensitivity.Confidential, ModelDataSensitivity.Confidential, true)]
    [InlineData(ModelDataSensitivity.Confidential, ModelDataSensitivity.Protected, false)]
    [InlineData(ModelDataSensitivity.Protected, ModelDataSensitivity.Protected, true)]
    [InlineData(ModelDataSensitivity.Protected, ModelDataSensitivity.Restricted, false)]
    public void ACeilingPermitsItselfAndBelow(
        ModelDataSensitivity ceiling, ModelDataSensitivity material, bool expected)
    {
        Assert.Equal(expected, Enabled(ceiling).Permits(material));
    }

    /// <summary>A disabled policy permits nothing, whatever its ceiling says.</summary>
    [Fact]
    public void ADisabledPolicyPermitsNothing()
    {
        AiProviderPolicy policy = AiProviderPolicy.Create(
            Org, "fake", false, ModelDataSensitivity.Protected, true, Admin, Now);

        Assert.False(policy.Permits(ModelDataSensitivity.Internal));
    }

    /// <summary>
    /// Turning write proposals off removes them from the product, and does not
    /// weaken approval.
    /// </summary>
    /// <remarks>
    /// The two settings answer different questions. A firm may be content for a
    /// model to read and summarize while wanting nothing proposed for approval at
    /// all; a write still needs a person either way (§42).
    /// </remarks>
    [Fact]
    public void ReadingAndProposingAreSeparateSettings()
    {
        AiProviderPolicy policy = AiProviderPolicy.Create(
            Org, "fake", true, ModelDataSensitivity.Protected, false, Admin, Now);

        Assert.True(policy.Permits(ModelDataSensitivity.Protected));
        Assert.False(policy.AllowsCanonicalWriteProposals);
    }

    private static AiProviderPolicy Enabled(ModelDataSensitivity ceiling) =>
        AiProviderPolicy.Create(Org, "fake", true, ceiling, true, Admin, Now);
}
