using System.Diagnostics.CodeAnalysis;

namespace AgencyOS.Windows.Platform.Activation;

/// <summary>
/// Turns an <c>agencyos:</c> link into a destination, and does nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Pure.</strong> It has no dependencies, touches no window, opens no
/// page and asks the server nothing. That is what makes it testable, and it is
/// also the security property: a parser that could navigate would be a parser
/// that decides what a user may see. Navigation happens afterwards, in the shell,
/// and only after an authorized read succeeds (ADR-0033).
/// </para>
/// <para>
/// One router, rather than parsing scattered across pages. A link arriving at a
/// page would mean every page owns a piece of the URI vocabulary, and the shape
/// of what is addressable would be discoverable only by reading all of them.
/// </para>
/// <para>
/// The vocabulary is deliberately small and flat. Routes name a canonical object
/// by identifier and carry nothing else: no filters, no query strings, no
/// commands. A link that could carry an action would be a way to make the client
/// do something without a person choosing it.
/// </para>
/// </remarks>
public static class ActivationRouter
{
    /// <summary>The scheme AgencyOS registers.</summary>
    public const string Scheme = "agencyos";

    /// <summary>
    /// The routes this build understands.
    /// </summary>
    /// <remarks>
    /// Written as the path a link carries, so the table reads the way the link
    /// does. <c>ai/run</c> and <c>ai/approval</c> are two segments because the AI
    /// surface has more than one kind of addressable object and flattening them
    /// would make <c>ai/{id}</c> ambiguous.
    /// </remarks>
    private static readonly Dictionary<string, ActivationRouteKind> Routes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["person"] = ActivationRouteKind.Person,
            ["company"] = ActivationRouteKind.Company,
            ["deal"] = ActivationRouteKind.Deal,
            ["contract"] = ActivationRouteKind.Contract,
            ["research"] = ActivationRouteKind.ResearchCase,
            ["ai/run"] = ActivationRouteKind.AgentRun,
            ["ai/approval"] = ActivationRouteKind.AiApproval,
        };

    /// <summary>Every route path this build understands.</summary>
    public static IReadOnlyCollection<string> KnownRoutes => Routes.Keys;

    /// <summary>
    /// Parses a link, refusing anything it does not recognize.
    /// </summary>
    /// <remarks>
    /// Never throws. A malformed link is an ordinary thing to receive — a shell
    /// hands over whatever was clicked, and some of it will be truncated,
    /// hand-edited or from a newer build — so refusal is a value rather than an
    /// exception.
    /// </remarks>
    public static ActivationRoute Parse(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return ActivationRoute.Refused(ActivationFailure.Empty);
        }

        if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out Uri? parsed))
        {
            return ActivationRoute.Refused(ActivationFailure.UnknownRoute);
        }

        if (!string.Equals(parsed.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return ActivationRoute.Refused(ActivationFailure.ForeignScheme);
        }

        // The host is the first path segment for a scheme like ours, so the route
        // is the host plus whatever path follows it.
        string[] segments =
        [
            .. (parsed.Host + parsed.AbsolutePath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];

        if (segments.Length < 2)
        {
            return ActivationRoute.Refused(ActivationFailure.UnknownRoute);
        }

        // Longest match first, so ai/run wins over a hypothetical ai route rather
        // than depending on dictionary order.
        string twoPart = segments.Length >= 3
            ? $"{segments[0]}/{segments[1]}"
            : string.Empty;

        if (twoPart.Length > 0 && Routes.TryGetValue(twoPart, out ActivationRouteKind nested))
        {
            return Identify(nested, segments[2], segments.Length, expected: 3);
        }

        return Routes.TryGetValue(segments[0], out ActivationRouteKind kind)
            ? Identify(kind, segments[1], segments.Length, expected: 2)
            : ActivationRoute.Refused(ActivationFailure.UnknownRoute);
    }

    /// <summary>Builds the link for a record. The inverse of <see cref="Parse"/>.</summary>
    /// <remarks>
    /// Provided so notifications and copy-link both produce a form this build can
    /// parse. Two places composing URIs by hand is how a scheme acquires dialects.
    /// </remarks>
    public static string Link(ActivationRouteKind kind, Guid id)
    {
        string route = Routes.First(x => x.Value == kind).Key;

        return $"{Scheme}://{route}/{id:D}";
    }

    /// <summary>Whether a string is a link this build would accept.</summary>
    public static bool TryParse(string? uri, [NotNullWhen(true)] out ActivationRoute? route)
    {
        ActivationRoute parsed = Parse(uri);
        route = parsed.IsResolved ? parsed : null;

        return parsed.IsResolved;
    }

    /// <summary>
    /// Validates the identifier and refuses trailing rubbish.
    /// </summary>
    /// <remarks>
    /// Extra segments are refused rather than ignored. A link with more in it than
    /// this build understands may mean something in a newer build, and quietly
    /// dropping the part we do not recognize would navigate somewhere plausible
    /// and wrong.
    /// </remarks>
    private static ActivationRoute Identify(
        ActivationRouteKind kind, string candidate, int actual, int expected)
    {
        if (actual != expected)
        {
            return ActivationRoute.Refused(ActivationFailure.UnknownRoute);
        }

        // Exact form only. Guid.TryParse accepts braces, parentheses and the
        // hyphenless form, and a scheme that accepts four spellings of one
        // identifier has four spellings of every link.
        return Guid.TryParseExact(candidate, "D", out Guid id) && id != Guid.Empty
            ? ActivationRoute.Resolved(kind, id)
            : ActivationRoute.Refused(ActivationFailure.MalformedIdentifier);
    }
}
