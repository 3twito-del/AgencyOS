namespace AgencyOS.Contracts.Search;

/// <summary>
/// One search result.
/// </summary>
/// <remarks>
/// Typed and identity-bearing, never an untyped blob: the client opens the record
/// straight from a hit, so the hit has to say exactly what it points at.
/// </remarks>
/// <param name="Type">Person, Company or Task.</param>
/// <param name="Id">Identifier of the matched record.</param>
/// <param name="Title">Primary label - a person's display name, a company name, a task title.</param>
/// <param name="Subtitle">Supporting context such as title and company.</param>
/// <param name="Status">Lifecycle status of the record.</param>
/// <param name="Score">
/// Relevance, higher is better. Comparable within one response only; it is not a
/// stable quantity across queries and should never be shown as a percentage.
/// </param>
/// <param name="MatchedOn">
/// Why it matched: <c>Exact</c>, <c>Prefix</c>, <c>FullText</c> or <c>Similar</c>.
/// Present so a ranked list can account for itself; an order nobody can explain
/// is the kind of number this system does not ship.
/// </param>
public sealed record SearchHit(
    string Type,
    Guid Id,
    string Title,
    string? Subtitle,
    string Status,
    double Score,
    string MatchedOn);

/// <summary>
/// One page of search results.
/// </summary>
/// <remarks>
/// An empty or whitespace query returns no hits rather than the whole tenant. An
/// empty search box is a legitimate state, and answering it with everything is
/// both useless and expensive.
/// </remarks>
/// <param name="Query">The normalized query that was executed.</param>
/// <param name="Hits">Results, most relevant first.</param>
/// <param name="Skip">Results skipped.</param>
/// <param name="Take">Maximum requested.</param>
/// <param name="HasMore">Whether more results exist beyond this page.</param>
public sealed record SearchResponse(
    string Query,
    IReadOnlyList<SearchHit> Hits,
    int Skip,
    int Take,
    bool HasMore);
