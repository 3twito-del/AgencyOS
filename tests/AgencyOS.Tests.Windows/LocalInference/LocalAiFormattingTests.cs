using AgencyOS.Client.ViewModels;
using Xunit;

namespace AgencyOS.Tests.Windows.LocalInference;

/// <summary>
/// What the screen is allowed to claim about where a model ran.
/// </summary>
/// <remarks>
/// A person deciding what to ask an assistant about will act on this wording. A
/// screen implying "private mode" while inference happened in somebody else's
/// datacentre would be making a claim about where an agency's material went
/// (§42, ADR-0035).
/// </remarks>
public sealed class LocalAiFormattingTests
{
    /// <summary>
    /// Residency wording says where, and claims nothing else.
    /// </summary>
    /// <remarks>
    /// In particular it never says private, secure or safe. Residency is a fact
    /// about a machine, not a privacy guarantee — the same distinction the domain
    /// draws by keeping Restricted unreachable at every residency.
    /// </remarks>
    [Theory]
    [InlineData("DeviceLocal")]
    [InlineData("OrganizationControlled")]
    [InlineData("ExternalCloud")]
    [InlineData(null)]
    public void ResidencyWordingClaimsOnlyLocation(string? residency)
    {
        foreach (string wording in
            new[] { LocalAiFormatting.Residency(residency), LocalAiFormatting.ResidencyBadge(residency) })
        {
            Assert.NotEmpty(wording);

            foreach (string overclaim in new[] { "private", "secure", "safe", "encrypted" })
            {
                Assert.DoesNotContain(overclaim, wording, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void DeviceLocalSaysItRunsOnThisDevice()
    {
        Assert.Contains(
            "this device",
            LocalAiFormatting.Residency("DeviceLocal"),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "external",
            LocalAiFormatting.Residency("ExternalCloud"),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Only a ready device offers a local run.
    /// </summary>
    /// <remarks>
    /// Unknown is not treated as yes. The probe returns it when it could not ask,
    /// and offering a run on that basis would fail at the model instead of at the
    /// screen.
    /// </remarks>
    [Theory]
    [InlineData("Ready", true)]
    [InlineData("NotReady", false)]
    [InlineData("NotSupported", false)]
    [InlineData("Unknown", false)]
    [InlineData(null, false)]
    public void OnlyAReadyDeviceOffersALocalRun(string? readiness, bool expected)
    {
        Assert.Equal(expected, LocalAiFormatting.CanRunLocally(readiness));
    }

    /// <summary>
    /// The unavailable wording is factual and offers to install nothing.
    /// </summary>
    /// <remarks>
    /// This is the normal case on almost every machine, including all of the ones
    /// AgencyOS has been developed on. Downloading a model because a screen was
    /// opened is not a decision a UI makes on somebody's behalf (§31, §F).
    /// </remarks>
    [Theory]
    [InlineData("NotReady")]
    [InlineData("NotSupported")]
    [InlineData("Unknown")]
    public void UnavailableWordingOffersNoInstall(string readiness)
    {
        string wording = LocalAiFormatting.Readiness(readiness);

        Assert.NotEmpty(wording);

        // Offers, not the words themselves. "AgencyOS will not download one for
        // you" contains "download" and is the opposite of an offer, so matching on
        // the bare word would forbid the sentence that makes the point.
        foreach (string offer in new[]
        {
            "click here", "install now", "get it now", "download it", "download now",
        })
        {
            Assert.DoesNotContain(offer, wording, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A device that could run a model is told AgencyOS will not fetch one.
    /// </summary>
    /// <remarks>
    /// The one case where somebody might reasonably expect the app to act. Saying
    /// plainly that it will not is better than silence, which reads as an
    /// omission (§31).
    /// </remarks>
    [Fact]
    public void ADeviceThatCouldRunOneIsToldAgencyOsWillNotFetchIt()
    {
        Assert.Contains(
            "will not download",
            LocalAiFormatting.Readiness("NotReady"),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The unsupported case names the real reason.</summary>
    [Fact]
    public void UnsupportedNamesTheRealReason()
    {
        string wording = LocalAiFormatting.Readiness("NotSupported");

        Assert.Contains("Copilot+", wording, StringComparison.Ordinal);
        Assert.Contains("neural processor", wording, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No local failure suggests trying the cloud instead.
    /// </summary>
    /// <remarks>
    /// The wording carries the no-fallback rule to the one place a person would
    /// otherwise reach for it. Crossing a residency boundary is a policy decision,
    /// not a retry, and offering it as one here would teach people they are the
    /// same thing (§E).
    /// </remarks>
    [Theory]
    [InlineData("LocalProviderUnavailable")]
    [InlineData("LocalModelNotReady")]
    [InlineData("ProviderTimeout")]
    [InlineData("Cancelled")]
    [InlineData("Unexpected")]
    public void NoLocalFailureSuggestsTheCloud(string failure)
    {
        string wording = LocalAiFormatting.LocalFailure(failure);

        Assert.NotEmpty(wording);

        foreach (string suggestion in new[] { "cloud", "online model", "try the provider" })
        {
            Assert.DoesNotContain(suggestion, wording, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>An unavailable device says nothing was sent anywhere else.</summary>
    /// <remarks>
    /// The most useful sentence on that screen. Somebody whose local run failed
    /// wants to know whether their material went somewhere, and the answer is no.
    /// </remarks>
    [Theory]
    [InlineData("LocalProviderUnavailable")]
    [InlineData("LocalModelNotReady")]
    public void AnUnavailableDeviceSaysNothingWasSent(string failure)
    {
        Assert.Contains(
            "nothing was sent anywhere else",
            LocalAiFormatting.LocalFailure(failure),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every server refusal has wording a person can act on.</summary>
    [Theory]
    [InlineData("Expired")]
    [InlineData("Consumed")]
    [InlineData("Invalidated")]
    [InlineData("ContextMismatch")]
    [InlineData("RunNotWaiting")]
    [InlineData("PermissionRevoked")]
    [InlineData("EmptyResult")]
    [InlineData("NotFound")]
    public void EveryRefusalHasWording(string refusal)
    {
        Assert.NotEmpty(LocalAiFormatting.Refusal(refusal));
    }

    /// <summary>
    /// A changed-context refusal explains itself without blaming the person.
    /// </summary>
    /// <remarks>
    /// The one refusal that would otherwise read as a bug. The records moved while
    /// the model was working, which is nobody's mistake, and the answer is to ask
    /// again rather than to wonder what went wrong.
    /// </remarks>
    [Fact]
    public void AChangedContextRefusalExplainsItself()
    {
        string wording = LocalAiFormatting.Refusal("ContextMismatch");

        Assert.Contains("changed", wording, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask again", wording, StringComparison.OrdinalIgnoreCase);
    }
}
