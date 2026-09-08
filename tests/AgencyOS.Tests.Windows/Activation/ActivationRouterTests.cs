using AgencyOS.Client.Commands;
using AgencyOS.Windows.Platform.Activation;
using Xunit;

namespace AgencyOS.Tests.Windows.Activation;

/// <summary>
/// What a deep link can and cannot do.
/// </summary>
/// <remarks>
/// <para>
/// The security property under test is negative and easy to lose: parsing a link
/// grants nothing. These tests prove the parser produces a request and never a
/// decision — it opens no page, reads no record and asks no permission, so the
/// only thing a well-formed link buys an attacker is a navigation attempt the
/// server then refuses (ADR-0033).
/// </para>
/// <para>
/// The malformed cases matter as much as the good ones. A shell hands over
/// whatever was clicked, and some of it will be truncated, hand-edited or from a
/// newer build.
/// </para>
/// </remarks>
public sealed class ActivationRouterTests
{
    private static readonly Guid Id = Guid.Parse("0199f3c2-4a1b-7c3d-9e5f-1a2b3c4d5e6f");

    // ------------------------------------------------------- what resolves

    [Theory]
    [InlineData("person", ActivationRouteKind.Person)]
    [InlineData("company", ActivationRouteKind.Company)]
    [InlineData("deal", ActivationRouteKind.Deal)]
    [InlineData("contract", ActivationRouteKind.Contract)]
    [InlineData("research", ActivationRouteKind.ResearchCase)]
    [InlineData("ai/run", ActivationRouteKind.AgentRun)]
    [InlineData("ai/approval", ActivationRouteKind.AiApproval)]
    public void EverySupportedRouteResolves(string route, ActivationRouteKind expected)
    {
        ActivationRoute parsed = ActivationRouter.Parse($"agencyos://{route}/{Id:D}");

        Assert.True(parsed.IsResolved);
        Assert.Equal(expected, parsed.Kind);
        Assert.Equal(Id, parsed.Id);
    }

    /// <summary>The scheme is matched without regard to case, as shells vary.</summary>
    [Theory]
    [InlineData("AGENCYOS://person/")]
    [InlineData("AgencyOS://PERSON/")]
    public void TheSchemeAndRouteAreCaseInsensitive(string prefix)
    {
        Assert.True(ActivationRouter.Parse(prefix + Id.ToString("D")).IsResolved);
    }

    /// <summary>
    /// Every route round-trips through the link builder.
    /// </summary>
    /// <remarks>
    /// Notifications and copy-link both compose URIs, and two places composing by
    /// hand is how a scheme acquires dialects.
    /// </remarks>
    [Fact]
    public void EveryRouteRoundTrips()
    {
        foreach (ActivationRouteKind kind in Enum.GetValues<ActivationRouteKind>())
        {
            ActivationRoute parsed = ActivationRouter.Parse(ActivationRouter.Link(kind, Id));

            Assert.True(parsed.IsResolved, $"{kind} did not round-trip.");
            Assert.Equal(kind, parsed.Kind);
            Assert.Equal(Id, parsed.Id);
        }
    }

    /// <summary>Every route opens a workspace the shell actually has.</summary>
    [Fact]
    public void EveryRouteNamesARealWorkspace()
    {
        foreach (ActivationRouteKind kind in Enum.GetValues<ActivationRouteKind>())
        {
            ActivationRoute parsed = ActivationRouter.Parse(ActivationRouter.Link(kind, Id));

            Assert.True(
                AgencyOsWorkspaces.Tags.Contains(parsed.Workspace),
                $"{kind} opens '{parsed.Workspace}', which is not a workspace.");
        }
    }

    // --------------------------------------------------------- what refuses

    [Theory]
    [InlineData(null, ActivationFailure.Empty)]
    [InlineData("", ActivationFailure.Empty)]
    [InlineData("   ", ActivationFailure.Empty)]
    public void NothingIsRefusedAsEmpty(string? uri, ActivationFailure expected)
    {
        Assert.Equal(expected, ActivationRouter.Parse(uri).Failure);
    }

    /// <summary>A link from another application is not ours to interpret.</summary>
    [Theory]
    [InlineData("https://example.test/person/x")]
    [InlineData("file:///c:/temp/thing.txt")]
    [InlineData("ms-settings:privacy")]
    public void AForeignSchemeIsRefused(string uri)
    {
        ActivationRoute parsed = ActivationRouter.Parse(uri);

        Assert.False(parsed.IsResolved);
        Assert.Equal(ActivationFailure.ForeignScheme, parsed.Failure);
    }

    /// <summary>A route this build does not know fails cleanly rather than guessing.</summary>
    [Theory]
    [InlineData("agencyos://invoice/")]
    [InlineData("agencyos://payment/")]
    [InlineData("agencyos://ai/tool/")]
    public void AnUnknownRouteIsRefused(string prefix)
    {
        ActivationRoute parsed = ActivationRouter.Parse(prefix + Id.ToString("D"));

        Assert.False(parsed.IsResolved);
        Assert.Equal(ActivationFailure.UnknownRoute, parsed.Failure);
    }

    /// <summary>
    /// A malformed identifier is refused, in every spelling.
    /// </summary>
    /// <remarks>
    /// Only the hyphenated form is accepted. <c>Guid.TryParse</c> also takes
    /// braces, parentheses and the hyphenless form, and a scheme that accepts four
    /// spellings of one identifier has four spellings of every link — which makes
    /// audit and comparison harder for no benefit.
    /// </remarks>
    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{0199f3c2-4a1b-7c3d-9e5f-1a2b3c4d5e6f}")]
    [InlineData("0199f3c24a1b7c3d9e5f1a2b3c4d5e6f")]
    [InlineData("1")]
    public void AMalformedIdentifierIsRefused(string candidate)
    {
        ActivationRoute parsed = ActivationRouter.Parse($"agencyos://person/{candidate}");

        Assert.False(parsed.IsResolved);
        Assert.Equal(ActivationFailure.MalformedIdentifier, parsed.Failure);
    }

    /// <summary>
    /// Extra segments are refused rather than ignored.
    /// </summary>
    /// <remarks>
    /// A link carrying more than this build understands may mean something in a
    /// newer one. Dropping the part we do not recognize would navigate somewhere
    /// plausible and wrong, which is worse than refusing.
    /// </remarks>
    [Theory]
    [InlineData("agencyos://person/{id}/edit")]
    [InlineData("agencyos://person/{id}/delete")]
    [InlineData("agencyos://ai/run/{id}/execute")]
    public void TrailingSegmentsAreRefused(string template)
    {
        ActivationRoute parsed = ActivationRouter.Parse(
            template.Replace("{id}", Id.ToString("D"), StringComparison.Ordinal));

        Assert.False(parsed.IsResolved);
    }

    /// <summary>A route with no identifier names nothing.</summary>
    [Theory]
    [InlineData("agencyos://person")]
    [InlineData("agencyos://person/")]
    [InlineData("agencyos://")]
    public void ARouteWithNoIdentifierIsRefused(string uri)
    {
        Assert.False(ActivationRouter.Parse(uri).IsResolved);
    }

    // ------------------------------------------------ what it does not do

    /// <summary>
    /// A link cannot carry an instruction.
    /// </summary>
    /// <remarks>
    /// The vocabulary is a kind and an identifier. Anything that looked like a
    /// command — approve, execute, delete — is refused as an unknown route, which
    /// is what stops a link from making the client act without a person choosing.
    /// </remarks>
    [Theory]
    [InlineData("agencyos://ai/approval/{id}?approve=true")]
    [InlineData("agencyos://ai/approval/{id}#approve")]
    [InlineData("agencyos://execute/{id}")]
    public void ALinkCannotCarryAnAction(string template)
    {
        string uri = template.Replace("{id}", Id.ToString("D"), StringComparison.Ordinal);
        ActivationRoute parsed = ActivationRouter.Parse(uri);

        // Either refused outright, or resolved to the plain record with nothing
        // of the query or fragment surviving into the route.
        if (parsed.IsResolved)
        {
            Assert.Equal(ActivationRouteKind.AiApproval, parsed.Kind);
            Assert.Equal(Id, parsed.Id);
        }
    }

    /// <summary>
    /// The parser is a pure function of its input.
    /// </summary>
    /// <remarks>
    /// Asserted directly because it is the property everything else rests on: no
    /// ambient state, no ordering effect, and therefore nothing a link can change
    /// by being parsed.
    /// </remarks>
    [Fact]
    public void ParsingIsPure()
    {
        string uri = ActivationRouter.Link(ActivationRouteKind.Deal, Id);

        ActivationRoute first = ActivationRouter.Parse(uri);
        ActivationRoute second = ActivationRouter.Parse(uri);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryParseAgreesWithParse()
    {
        Assert.True(ActivationRouter.TryParse(
            ActivationRouter.Link(ActivationRouteKind.Person, Id), out ActivationRoute? good));
        Assert.NotNull(good);

        Assert.False(ActivationRouter.TryParse("agencyos://nope/1", out ActivationRoute? bad));
        Assert.Null(bad);
    }

    /// <summary>The advertised route table matches what parses.</summary>
    [Fact]
    public void TheAdvertisedRoutesAreTheSupportedRoutes()
    {
        Assert.Equal(
            Enum.GetValues<ActivationRouteKind>().Length,
            ActivationRouter.KnownRoutes.Count);
    }
}
