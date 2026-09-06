using AgencyOS.Domain.Releases;
using Xunit;

namespace AgencyOS.Tests.Unit.Releases;

/// <summary>
/// Precedence rules for client versions.
/// </summary>
/// <remarks>
/// These decide whether a build is below the minimum supported version, which
/// decides whether it may write canonical data. A wrong comparison here does not
/// produce a visible bug; it silently admits a client that should have been
/// refused. Hence the table.
/// </remarks>
public sealed class ClientVersionTests
{
    [Theory]
    [InlineData("0.1.0")]
    [InlineData("1.0.0")]
    [InlineData("10.20.30")]
    [InlineData("0.1.0-alpha")]
    [InlineData("0.1.0-alpha.13")]
    [InlineData("1.0.0-rc.1")]
    public void Parse_AcceptsWellFormedVersions(string value)
    {
        Assert.True(ClientVersion.TryParse(value, out ClientVersion? parsed));
        Assert.Equal(value, parsed.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.x")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-alpha..1")]
    [InlineData("v1.0.0")]
    public void Parse_RejectsMalformedVersions(string? value)
    {
        Assert.False(ClientVersion.TryParse(value, out _));
    }

    /// <summary>Build metadata is ignored for precedence, per SemVer 2.0.0.</summary>
    [Fact]
    public void Parse_IgnoresBuildMetadata()
    {
        ClientVersion withMetadata = ClientVersion.Parse("1.2.3+20260907.1");
        ClientVersion without = ClientVersion.Parse("1.2.3");

        Assert.Equal(without, withMetadata);
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("0.9.9", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2")]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.10")]
    [InlineData("1.0.0-alpha.1", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    public void Compare_OrdersByPrecedence(string lower, string higher)
    {
        ClientVersion left = ClientVersion.Parse(lower);
        ClientVersion right = ClientVersion.Parse(higher);

        Assert.True(left < right, $"{lower} should precede {higher}.");
        Assert.True(right > left);
        Assert.True(left <= right);
        Assert.NotEqual(left, right);
    }

    /// <summary>
    /// Numeric prerelease identifiers compare numerically, not as text. The case
    /// that matters: alpha.10 must outrank alpha.9, which string ordering gets
    /// backwards.
    /// </summary>
    [Fact]
    public void Compare_TreatsNumericPrereleaseIdentifiersAsNumbers()
    {
        Assert.True(ClientVersion.Parse("0.1.0-alpha.9") < ClientVersion.Parse("0.1.0-alpha.10"));
    }

    /// <summary>Numeric identifiers rank below alphanumeric ones.</summary>
    [Fact]
    public void Compare_RanksNumericIdentifiersBelowAlphanumeric()
    {
        Assert.True(ClientVersion.Parse("1.0.0-1") < ClientVersion.Parse("1.0.0-alpha"));
    }

    [Fact]
    public void Compare_TreatsEquivalentVersionsAsEqual()
    {
        Assert.Equal(ClientVersion.Parse("1.2.3"), ClientVersion.Parse("1.2.3"));
        Assert.True(ClientVersion.Parse("1.2.3") >= ClientVersion.Parse("1.2.3"));
        Assert.True(ClientVersion.Parse("1.2.3") <= ClientVersion.Parse("1.2.3"));
    }

    [Fact]
    public void IsPrerelease_DistinguishesPrereleaseBuilds()
    {
        Assert.True(ClientVersion.Parse("0.1.0-alpha.13").IsPrerelease);
        Assert.False(ClientVersion.Parse("0.1.0").IsPrerelease);
    }
}
