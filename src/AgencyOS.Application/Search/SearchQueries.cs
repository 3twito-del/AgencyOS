using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Search;

/// <summary>What a search hit is.</summary>
public enum SearchEntityType
{
    Person = 1,
    Company = 2,
    Task = 3,

    // Added in M4. Credits and materials carry titles worth finding: "who was in
    // that pilot" and "where is the latest draft" are real questions.
    Credit = 4,
    Material = 5,

    // Added in M5. The slate is the other half of what an agency looks things up
    // in: "what was that book we optioned" and "which package had the director
    // attached" are asked as often as questions about people.
    Project = 6,
    SourceProperty = 7,
    Package = 8,

    // Added in M6. "What are we doing about that studio" is asked constantly, and
    // without this the only way to find a pursuit is to remember whose it is.
    Opportunity = 9,

    // Added in M7. "What are we negotiating with them" is asked as often as
    // anything else, and a deal that could only be reached through its pursuit
    // would be one nobody finds.
    Deal = 10,

    // Added in M8. "Where is the Northgate paper" is asked constantly, and a
    // contract reachable only through its deal is one nobody finds.
    Contract = 11,

    // Added in M9. Reference only - no amount, no balance, no rate is indexed
    // anywhere, so a hit reveals that an invoice exists and nothing about what it
    // is worth. Gated by finance.read (ADR-0023).
    Invoice = 12,
}

/// <summary>Why a row matched, so a ranked list can explain itself.</summary>
/// <remarks>
/// Surfaced to the user. A ranked list nobody can account for is the kind of
/// number <c>docs/03_ROADMAP.md</c> warns against: when "Sara Klein" outranks
/// "Sarah" for the query <c>sarah</c>, the answer should be visible rather than
/// inferred from the order.
/// </remarks>
public enum SearchMatchKind
{
    /// <summary>The primary name is exactly the query, ignoring case.</summary>
    Exact = 1,

    /// <summary>The primary name starts with the query.</summary>
    Prefix = 2,

    /// <summary>A full-text lexeme matched, including a trailing prefix term.</summary>
    FullText = 3,

    /// <summary>Trigram similarity matched: a typo, or a name spelled differently.</summary>
    Similar = 4,
}

/// <param name="Type">What kind of record this is.</param>
/// <param name="Id">Identifier of the record.</param>
/// <param name="Title">Primary line: a person's display name, a company's name, a task's title.</param>
/// <param name="Subtitle">Secondary line, when there is useful context.</param>
/// <param name="Status">Lifecycle status, so archived hits are visibly archived.</param>
/// <param name="Score">Relevance in 0..1. Comparable across types by construction.</param>
/// <param name="MatchedOn">Why it matched.</param>
public sealed record SearchHitModel(
    SearchEntityType Type,
    Guid Id,
    string Title,
    string? Subtitle,
    string Status,
    double Score,
    SearchMatchKind MatchedOn);

/// <param name="Hits">The page of results.</param>
/// <param name="HasMore">Whether a further page exists.</param>
public sealed record SearchResultModel(IReadOnlyList<SearchHitModel> Hits, bool HasMore);

/// <summary>
/// Ranked, tenant-scoped search across the M3 record types.
/// </summary>
/// <remarks>
/// Implemented in infrastructure against PostgreSQL full text and trigram
/// indexes. Deliberately unauthorized: <see cref="SearchService"/> decides which
/// types the caller may see, so there is one place to look for that rule.
/// </remarks>
public interface ISearchQueries
{
    Task<SearchResultModel> SearchAsync(
        OrganizationId organizationId,
        string query,
        IReadOnlySet<SearchEntityType> types,
        bool includeArchived,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
