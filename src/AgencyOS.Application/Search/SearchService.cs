using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Search;

/// <summary>
/// Authorizes a search, then delegates to the ranked projection.
/// </summary>
/// <remarks>
/// <para>
/// Search is the easiest place in a product to leak a record: one box that reads
/// everything. The rule here is that a search result may only contain what the
/// caller could have read directly, so the requested types are intersected with
/// the types the caller holds read permission for, in this tenant, before any
/// query runs.
/// </para>
/// <para>
/// A type the caller cannot read is dropped rather than refused. Refusing would
/// tell the caller that the type exists and that somebody has records in it;
/// dropping returns the same answer they would get by searching only what they
/// can see. When nothing survives, the search is refused outright, because at
/// that point there is no result to return that is not a lie about emptiness.
/// </para>
/// </remarks>
public sealed class SearchService
{
    /// <summary>Largest page search will return.</summary>
    private const int MaximumTake = 50;

    private const int DefaultTake = 25;

    /// <summary>Longest query accepted, before ranking cost stops being bounded.</summary>
    private const int MaximumQueryLength = 256;

    private static readonly IReadOnlyDictionary<SearchEntityType, string> RequiredPermissions =
        new Dictionary<SearchEntityType, string>
        {
            [SearchEntityType.Person] = Permission.PeopleRead,
            [SearchEntityType.Company] = Permission.CompaniesRead,
            [SearchEntityType.Task] = Permission.TasksRead,

            // Credits and materials are facets of a talent record, so they are
            // gated by the same permission that governs the record itself.
            [SearchEntityType.Credit] = Permission.TalentRead,
            [SearchEntityType.Material] = Permission.TalentRead,

            // Projects and the properties they derive from are gated together; a
            // package is gated by its own permission, because a caller who may see
            // the slate is not automatically entitled to what the agency is
            // quietly assembling on it.
            [SearchEntityType.Project] = Permission.ProjectsRead,
            [SearchEntityType.SourceProperty] = Permission.ProjectsRead,
            [SearchEntityType.Package] = Permission.PackagesRead,

            // Gated by opportunity access. Strategy is not searchable at all, so
            // there is nothing here a reader of the pipeline should not see.
            [SearchEntityType.Opportunity] = Permission.OpportunitiesRead,
        };

    private readonly ISearchQueries _queries;
    private readonly TenantGuard _guard;

    public SearchService(ISearchQueries queries, TenantGuard guard)
    {
        _queries = queries;
        _guard = guard;
    }

    /// <summary>Searches the tenant, restricted to what the caller may read.</summary>
    /// <param name="organizationId">Tenant to search. Never crossed.</param>
    /// <param name="query">Raw user text.</param>
    /// <param name="requestedTypes">Types to search, or null for everything the caller may read.</param>
    /// <param name="includeArchived">Whether archived records take part.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to return.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<SearchResultModel> SearchAsync(
        OrganizationId organizationId,
        string? query,
        IReadOnlySet<SearchEntityType>? requestedTypes = null,
        bool includeArchived = false,
        int skip = 0,
        int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        string normalized = Normalize(query);

        HashSet<SearchEntityType> candidates = requestedTypes is null || requestedTypes.Count == 0
            ? [.. RequiredPermissions.Keys]
            : [.. requestedTypes];

        HashSet<SearchEntityType> permitted = [];

        foreach (SearchEntityType type in candidates)
        {
            if (!RequiredPermissions.TryGetValue(type, out string? permission))
            {
                continue;
            }

            if (await _guard.HasPermissionAsync(permission, organizationId, cancellationToken).ConfigureAwait(false))
            {
                permitted.Add(type);
            }
        }

        if (permitted.Count == 0)
        {
            // Nothing survived. Establishing the caller is authenticated and then
            // refusing is the honest answer; an empty result would claim the tenant
            // holds no matching records, which is not what was determined.
            throw new PermissionDeniedException(
                RequiredPermissions[candidates.FirstOrDefault(RequiredPermissions.ContainsKey, SearchEntityType.Person)]);
        }

        if (normalized.Length == 0)
        {
            // An empty query is a legitimate UI state, not an error: the search box
            // is open and nothing is typed yet. Returning nothing beats returning
            // the whole tenant.
            return new SearchResultModel([], HasMore: false);
        }

        return await _queries
            .SearchAsync(
                organizationId,
                normalized,
                permitted,
                includeArchived,
                Math.Max(skip, 0),
                Math.Clamp(take, 1, MaximumTake),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Normalize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        string trimmed = query.Trim();

        return trimmed.Length > MaximumQueryLength ? trimmed[..MaximumQueryLength] : trimmed;
    }
}
