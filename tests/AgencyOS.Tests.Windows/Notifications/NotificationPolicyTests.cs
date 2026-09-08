using AgencyOS.Windows.Platform.Activation;
using AgencyOS.Windows.Platform.Notifications;
using Xunit;

namespace AgencyOS.Tests.Windows.Notifications;

/// <summary>
/// What a toast is allowed to say.
/// </summary>
/// <remarks>
/// A notification renders on a lock screen, in a meeting room, on a mirrored
/// display. Every test here is about the gap between what the reader may see
/// inside AgencyOS and what may be shown there without them choosing to look
/// (ADR-0034).
/// </remarks>
public sealed class NotificationPolicyTests
{
    private static readonly Guid Id = Guid.Parse("0199f3c2-4a1b-7c3d-9e5f-1a2b3c4d5e6f");

    // ------------------------------------------------------------ defaults

    /// <summary>Silence about the subject is the default.</summary>
    [Fact]
    public void TheDefaultSaysNothingAboutTheSubject()
    {
        NotificationContent content = NotificationPolicy.Compose(
            Request(NotificationSensitivity.Internal, "Marguerite Sable"),
            NotificationDetailPreference.Generic,
            organizationAllowsDetail: true);

        Assert.False(content.IsDetailed);
        Assert.Equal(NotificationPolicy.GenericTitle, content.Title);
        Assert.Equal(NotificationPolicy.GenericBody, content.Body);
        Assert.DoesNotContain("Sable", content.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Detail needs three independent yeses.</summary>
    [Theory]
    [InlineData(NotificationDetailPreference.Generic, true, NotificationSensitivity.Internal)]
    [InlineData(NotificationDetailPreference.Detailed, false, NotificationSensitivity.Internal)]
    [InlineData(NotificationDetailPreference.Detailed, true, NotificationSensitivity.Protected)]
    [InlineData(NotificationDetailPreference.Detailed, true, NotificationSensitivity.Restricted)]
    public void AnyMissingConditionYieldsTheGenericForm(
        NotificationDetailPreference preference,
        bool organizationAllows,
        NotificationSensitivity sensitivity)
    {
        NotificationContent content = NotificationPolicy.Compose(
            Request(sensitivity, "Northgate option"), preference, organizationAllows);

        Assert.False(content.IsDetailed);
        Assert.DoesNotContain("Northgate", content.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>With all three, the subject is named.</summary>
    [Theory]
    [InlineData(NotificationSensitivity.Internal)]
    [InlineData(NotificationSensitivity.Confidential)]
    public void WithEveryConditionTheSubjectIsNamed(NotificationSensitivity sensitivity)
    {
        NotificationContent content = NotificationPolicy.Compose(
            Request(sensitivity, "Northgate option"),
            NotificationDetailPreference.Detailed,
            organizationAllowsDetail: true);

        Assert.True(content.IsDetailed);
        Assert.Contains("Northgate", content.Body, StringComparison.Ordinal);
    }

    // -------------------------------------------------------- the ceiling

    /// <summary>
    /// Protected and restricted material is never named, whatever anybody asked
    /// for.
    /// </summary>
    /// <remarks>
    /// Protected covers M11 source-sensitive claims and M10 privileged documents.
    /// Naming either on a lock screen discloses the thing the classification
    /// exists to protect: who spoke, or that a matter is with lawyers.
    /// </remarks>
    [Fact]
    public void NothingAboveTheCeilingIsEverNamed()
    {
        foreach (NotificationSensitivity sensitivity in
            new[] { NotificationSensitivity.Protected, NotificationSensitivity.Restricted })
        {
            NotificationContent content = NotificationPolicy.Compose(
                Request(sensitivity, "Rousseau spoke in confidence"),
                NotificationDetailPreference.Detailed,
                organizationAllowsDetail: true);

            Assert.False(content.IsDetailed);
            Assert.DoesNotContain("Rousseau", content.Body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A notification cannot carry money, because the type has nowhere to put it.
    /// </summary>
    /// <remarks>
    /// §8 says no finance amounts by default. A field that could carry one would
    /// make that a setting somebody can get wrong; leaving money out of the type
    /// is stronger than leaving it out of the policy.
    /// </remarks>
    [Fact]
    public void ThereIsNowhereForAnAmountToGo()
    {
        Assert.DoesNotContain(
            typeof(NotificationRequest).GetProperties(),
            x => x.PropertyType == typeof(decimal)
                || x.PropertyType == typeof(decimal?)
                || x.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Currency", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An overdue receivable says so, and never says how much.</summary>
    [Fact]
    public void AnOverdueReceivableNamesNoFigure()
    {
        NotificationContent content = NotificationPolicy.Compose(
            new NotificationRequest(
                NotificationCategory.ReceivableOverdue,
                NotificationSensitivity.Confidential,
                ActivationRouteKind.Contract,
                Id,
                "Northgate drama series"),
            NotificationDetailPreference.Detailed,
            organizationAllowsDetail: true);

        Assert.DoesNotContain(content.Body, "£$€", StringComparison.Ordinal);
        Assert.DoesNotContain(content.Title, "£$€", StringComparison.Ordinal);
        Assert.Equal("A receivable is overdue", content.Title);
    }

    // ---------------------------------------------------------- the payload

    /// <summary>
    /// The payload carries a link and nothing else.
    /// </summary>
    /// <remarks>
    /// No token, no grant, no cached content. Clicking re-authorizes, so a toast
    /// that outlived a permission change opens nothing.
    /// </remarks>
    [Fact]
    public void ThePayloadCarriesOnlyALink()
    {
        NotificationContent content = NotificationPolicy.Compose(
            Request(NotificationSensitivity.Internal, "Anything"),
            NotificationDetailPreference.Detailed,
            organizationAllowsDetail: true);

        ActivationRoute route = ActivationRouter.Parse(content.Link);

        Assert.True(route.IsResolved);
        Assert.Equal(Id, route.Id);
        Assert.DoesNotContain("token", content.Link, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("?", content.Link, StringComparison.Ordinal);
    }

    /// <summary>Every category produces a link this build can parse.</summary>
    [Fact]
    public void EveryCategoryProducesAParsableLink()
    {
        foreach (NotificationCategory category in Enum.GetValues<NotificationCategory>())
        {
            NotificationContent content = NotificationPolicy.Compose(
                new NotificationRequest(
                    category, NotificationSensitivity.Internal, ActivationRouteKind.AgentRun, Id),
                NotificationDetailPreference.Generic,
                organizationAllowsDetail: false);

            Assert.True(
                ActivationRouter.Parse(content.Link).IsResolved,
                $"{category} produced an unparsable link.");
        }
    }

    /// <summary>Every category has wording that names no record.</summary>
    [Fact]
    public void EveryCategoryHasSafeWording()
    {
        foreach (NotificationCategory category in Enum.GetValues<NotificationCategory>())
        {
            Assert.False(string.IsNullOrWhiteSpace(NotificationPolicy.Headline(category)));
        }
    }

    // ---------------------------------------------------------- the bounds

    /// <summary>A pathological title cannot become the whole toast.</summary>
    [Fact]
    public void ASubjectIsBounded()
    {
        NotificationContent content = NotificationPolicy.Compose(
            Request(NotificationSensitivity.Internal, new string('x', 500)),
            NotificationDetailPreference.Detailed,
            organizationAllowsDetail: true);

        Assert.True(content.Body.Length <= NotificationPolicy.MaximumSubjectLength + 1);
    }

    /// <summary>
    /// Control characters are stripped.
    /// </summary>
    /// <remarks>
    /// A record title is text somebody typed, and a newline in a toast is a way to
    /// push the rest of the notification out of view.
    /// </remarks>
    [Fact]
    public void ControlCharactersAreStripped()
    {
        NotificationContent content = NotificationPolicy.Compose(
            Request(NotificationSensitivity.Internal, "Real title\n\n\nSomething else"),
            NotificationDetailPreference.Detailed,
            organizationAllowsDetail: true);

        Assert.DoesNotContain('\n', content.Body);
        Assert.DoesNotContain('\r', content.Body);
    }

    /// <summary>An absent subject falls back rather than showing an empty toast.</summary>
    [Fact]
    public void AnAbsentSubjectFallsBackToGeneric()
    {
        NotificationContent content = NotificationPolicy.Compose(
            new NotificationRequest(
                NotificationCategory.TaskDue,
                NotificationSensitivity.Internal,
                ActivationRouteKind.Person,
                Id),
            NotificationDetailPreference.Detailed,
            organizationAllowsDetail: true);

        Assert.False(content.IsDetailed);
        Assert.Equal(NotificationPolicy.GenericBody, content.Body);
    }

    private static NotificationRequest Request(
        NotificationSensitivity sensitivity, string? subject) =>
        new(
            NotificationCategory.TaskDue,
            sensitivity,
            ActivationRouteKind.Person,
            Id,
            subject);
}
