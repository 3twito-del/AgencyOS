using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Releases;
using Xunit;

namespace AgencyOS.Tests.Windows.Updates;

/// <summary>
/// What a person is told about their build, and what it must not soften.
/// </summary>
/// <remarks>
/// The five states were settled in M0 and are enforced server-side. M13 adds no
/// policy: these tests are about the screen telling the truth about what the
/// server is going to do next (ADR-0034).
/// </remarks>
public sealed class UpdateFormattingTests
{
    [Theory]
    [InlineData("None")]
    [InlineData("Available")]
    [InlineData("Recommended")]
    [InlineData("Mandatory")]
    [InlineData("Revoked")]
    public void EveryStateHasAHeadlineAndAConsequence(string policy)
    {
        Assert.NotEmpty(UpdateFormatting.Headline(policy));
        Assert.NotEmpty(UpdateFormatting.Consequence(policy));
    }

    /// <summary>
    /// The non-blocking states say so.
    /// </summary>
    /// <remarks>
    /// Dramatising Available into a warning trains people to ignore the ones that
    /// matter, which is how a Mandatory notice ends up dismissed on reflex.
    /// </remarks>
    [Theory]
    [InlineData("Available")]
    [InlineData("Recommended")]
    public void NonBlockingStatesSayWorkContinues(string policy)
    {
        Assert.Contains(
            "keep working",
            UpdateFormatting.Consequence(policy),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The blocking states say what will be refused, and what will not.
    /// </summary>
    /// <remarks>
    /// Reading stays available under both, which is the part people most need to
    /// know: a withdrawn build is not a locked-out one, and saying so prevents a
    /// panic that a vaguer message would cause.
    /// </remarks>
    [Theory]
    [InlineData("Mandatory")]
    [InlineData("Revoked")]
    public void BlockingStatesSayWhatIsRefused(string policy)
    {
        string consequence = UpdateFormatting.Consequence(policy);

        Assert.Contains("refused", consequence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reading is unaffected", consequence, StringComparison.Ordinal);
        Assert.Equal("Blocking", UpdateFormatting.Severity(policy));
    }

    [Theory]
    [InlineData("None", "Informational")]
    [InlineData("Available", "Informational")]
    [InlineData("Recommended", "Advisory")]
    [InlineData("Mandatory", "Blocking")]
    [InlineData("Revoked", "Blocking")]
    public void SeverityCollapsesToWhatMatters(string policy, string expected)
    {
        Assert.Equal(expected, UpdateFormatting.Severity(policy));
    }

    /// <summary>
    /// Blocking is read from the server, never re-derived from the policy name.
    /// </summary>
    /// <remarks>
    /// The client does not decide this and must not appear to. If the two ever
    /// disagreed the server would be right and the screen would be lying, so the
    /// screen reads the server's own answer — even when it looks wrong.
    /// </remarks>
    [Fact]
    public void BlockingIsReadFromTheServer()
    {
        Assert.True(UpdateFormatting.BlocksWork(
            Handshake("Available", blocks: true)));

        Assert.False(UpdateFormatting.BlocksWork(
            Handshake("Mandatory", blocks: false)));
    }

    /// <summary>A mandatory update names the version that unblocks work.</summary>
    [Fact]
    public void MandatoryNamesTheMinimumVersion()
    {
        string guidance = UpdateFormatting.Guidance(
            Handshake("Mandatory", blocks: true, minimum: "0.4.0"));

        Assert.Contains("0.4.0", guidance, StringComparison.Ordinal);
        Assert.Contains("continue making changes", guidance, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A withdrawn build points at the designated rollback, when there is one.
    /// </summary>
    /// <remarks>
    /// The rollback target is the release authority's own instruction, and it is
    /// preferred over the latest version: a build is withdrawn for a reason, and
    /// the authority may want people on something specific rather than newest.
    /// </remarks>
    [Fact]
    public void RevokedPrefersTheDesignatedRollback()
    {
        string guidance = UpdateFormatting.Guidance(
            Handshake("Revoked", blocks: true, latest: "0.5.0", rollback: "0.3.2"));

        Assert.Contains("0.3.2", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("0.5.0", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void RevokedFallsBackToTheLatestVersion()
    {
        Assert.Contains(
            "0.5.0",
            UpdateFormatting.Guidance(Handshake("Revoked", blocks: true, latest: "0.5.0")),
            StringComparison.Ordinal);
    }

    /// <summary>A current build is told to do nothing.</summary>
    [Fact]
    public void ACurrentBuildIsToldNothing()
    {
        Assert.Empty(UpdateFormatting.Guidance(Handshake("None", blocks: false)));
        Assert.Equal("Nothing to do.", UpdateFormatting.Consequence("None"));
    }

    /// <summary>
    /// An unreachable authority is its own state, not an assumed "fine".
    /// </summary>
    /// <remarks>
    /// A build that treated silence as permission would keep working through
    /// exactly the outage during which a kill switch was flipped.
    /// </remarks>
    [Fact]
    public void AnUnreachableAuthorityIsItsOwnState()
    {
        Assert.Contains(
            "could not reach",
            UpdateFormatting.Consequence(null),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "unaffected",
            UpdateFormatting.Guidance(null),
            StringComparison.OrdinalIgnoreCase);

        // And it does not claim the build is blocked, because it does not know.
        Assert.False(UpdateFormatting.BlocksWork(null));
    }

    /// <summary>
    /// Nothing here offers to override, defer or dismiss a policy.
    /// </summary>
    /// <remarks>
    /// Asserted on the wording because the API has no such method: the risk is a
    /// screen that implies one exists, which would misrepresent what the server
    /// will do and what the user can do about it (§T).
    /// </remarks>
    [Theory]
    [InlineData("Mandatory")]
    [InlineData("Revoked")]
    public void NothingOffersAnOverride(string policy)
    {
        string wording =
            UpdateFormatting.Headline(policy) + UpdateFormatting.Consequence(policy);

        foreach (string override_ in new[]
        {
            "continue anyway", "ignore", "dismiss", "remind me later", "skip this",
        })
        {
            Assert.DoesNotContain(override_, wording, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HandshakeResponse Handshake(
        string policy,
        bool blocks,
        string? latest = null,
        string? minimum = null,
        string? rollback = null) =>
        new(
            "windows-x64",
            "alpha",
            policy,
            "test",
            blocks,
            latest,
            minimum,
            new ApiContractRange(1, 13),
            SecurityEpoch: 1,
            MandatoryAfterUtc: null,
            KillSwitch: false,
            RollbackTarget: rollback,
            Artifact: null);
}
