namespace AgencyOS.Windows.Platform.Activation;

/// <summary>
/// A canonical AgencyOS object a link can name.
/// </summary>
/// <remarks>
/// A closed set. An open one would let a link name anything and push the
/// question of what is addressable into whatever handles the link, which is how
/// a deep link becomes a way to probe for records.
/// </remarks>
public enum ActivationRouteKind
{
    Person = 1,
    Company,
    Deal,
    Contract,
    ResearchCase,
    AgentRun,
    AiApproval,
}

/// <summary>
/// Why a link could not be turned into a destination.
/// </summary>
/// <remarks>
/// Distinguished because they mean different things to the person who clicked.
/// A link this build does not understand may be from a newer one; a malformed
/// identifier is a broken link; a foreign scheme was never ours.
/// </remarks>
public enum ActivationFailure
{
    /// <summary>Nothing was wrong.</summary>
    None = 0,

    /// <summary>Not an <c>agencyos:</c> link at all.</summary>
    ForeignScheme,

    /// <summary>The scheme is ours and the route is not one this build knows.</summary>
    UnknownRoute,

    /// <summary>The route is known and the identifier is not a well-formed one.</summary>
    MalformedIdentifier,

    /// <summary>Nothing to parse.</summary>
    Empty,
}

/// <summary>
/// What a link resolved to, or why it did not.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A route is a request, not a permission.</strong> Holding this value
/// means somebody typed or clicked a well-formed link; it says nothing about
/// whether the caller may see the record, whether the record exists, or whether
/// it belongs to their organization. Every one of those is asked of the server
/// after parsing and before anything is shown (ADR-0033).
/// </para>
/// <para>
/// Parsing is separated from navigating for exactly that reason. A parser that
/// could open a page would be a parser that decides what a user may see.
/// </para>
/// </remarks>
public sealed record ActivationRoute
{
    private ActivationRoute()
    {
    }

    /// <summary>Whether the link named something this build can navigate to.</summary>
    public bool IsResolved => Failure == ActivationFailure.None;

    /// <summary>What kind of record, when resolved.</summary>
    public ActivationRouteKind Kind { get; private init; }

    /// <summary>Which record, when resolved.</summary>
    public Guid Id { get; private init; }

    /// <summary>Why not, when unresolved.</summary>
    public ActivationFailure Failure { get; private init; }

    /// <summary>
    /// The workspace this route opens.
    /// </summary>
    /// <remarks>
    /// A tag from <c>AgencyOsWorkspaces</c>, so activation and the command
    /// registry navigate the same way. Nothing addresses a workspace positionally
    /// anywhere in the client (ADR-0032).
    /// </remarks>
    public string Workspace => Kind switch
    {
        ActivationRouteKind.Person => "talent",
        ActivationRouteKind.Company => "companies",
        ActivationRouteKind.Deal => "deals",
        ActivationRouteKind.Contract => "contracts",
        ActivationRouteKind.ResearchCase => "intelligence",
        ActivationRouteKind.AgentRun => "ai",
        ActivationRouteKind.AiApproval => "ai",
        _ => "command-center",
    };

    internal static ActivationRoute Resolved(ActivationRouteKind kind, Guid id) =>
        new() { Kind = kind, Id = id };

    internal static ActivationRoute Refused(ActivationFailure failure) =>
        new() { Failure = failure };
}
