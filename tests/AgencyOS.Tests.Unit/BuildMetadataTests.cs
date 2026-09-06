using AgencyOS.Contracts;
using Xunit;

namespace AgencyOS.Tests.Unit;

/// <summary>
/// Guards the version metadata contract established in M0.
/// </summary>
/// <remarks>
/// Governing document: <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>. A client that
/// misreports its identity cannot be governed by the release authority, so the
/// metadata pipeline is treated as an invariant rather than a convenience.
/// </remarks>
public sealed class BuildMetadataTests
{
    /// <summary>
    /// <c>build/Version.props</c> and <see cref="ApiContract.Current"/> are two
    /// declarations of one fact. This is the test that stops them diverging.
    /// </summary>
    [Fact]
    public void ApiContractConstant_MatchesBuildMetadata()
    {
        Assert.Equal(ApiContract.Current, BuildInfo.ApiContractVersion);
    }

    [Fact]
    public void ApiContractVersion_IsPositive()
    {
        Assert.True(ApiContract.Current > 0, "API contract versions start at 1.");
    }

    [Fact]
    public void Version_IsStampedAndParseable()
    {
        Assert.NotEqual("unknown", BuildInfo.Version);
        Assert.True(
            Version.TryParse(BuildInfo.Version.Split('-')[0], out _),
            $"Version '{BuildInfo.Version}' is not a parseable version.");
    }

    [Fact]
    public void BuildId_IsStamped()
    {
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.BuildId));
        Assert.NotEqual("unknown", BuildInfo.BuildId);
    }

    [Fact]
    public void GitCommit_IsStamped()
    {
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.GitCommit));
    }

    /// <summary>
    /// The ring set mirrors <c>config/release-channels.yaml</c>. A build that
    /// reports a ring outside that set cannot be placed by the release authority.
    /// </summary>
    [Fact]
    public void Channel_IsADeclaredReleaseRing()
    {
        string[] declaredRings = ["forge", "lab", "nightly", "alpha", "beta", "rc", "stable"];
        Assert.Contains(BuildInfo.Channel, declaredRings);
    }
}
