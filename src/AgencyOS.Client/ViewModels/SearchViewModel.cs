using System.Collections.ObjectModel;
using AgencyOS.Client.Cache;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Search;

namespace AgencyOS.Client.ViewModels;

/// <summary>Where a search result came from.</summary>
public enum SearchSource
{
    /// <summary>Ranked by the server, against canonical data.</summary>
    Server = 1,

    /// <summary>
    /// Matched locally against the cache, because the server was unreachable.
    /// </summary>
    /// <remarks>
    /// Deliberately distinguishable. Offline results are a substring match over a
    /// possibly stale copy, not the server's ranking, and the UI says so rather
    /// than letting the user believe they searched everything.
    /// </remarks>
    Cache = 2,
}

/// <summary>
/// Global search across the tenant.
/// </summary>
/// <remarks>
/// Falls back to the cache when the server cannot be reached, and reports which
/// it used. Silently serving stale local results as though they were the server's
/// would be the kind of quiet lie this system is built to avoid.
/// </remarks>
public sealed class SearchViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;
    private readonly LocalCache? _cache;

    private string _query = string.Empty;
    private bool _includeArchived;
    private bool _hasMore;
    private SearchSource _source = SearchSource.Server;
    private bool _hasSearched;

    public SearchViewModel(IAgencyOsApi api, LocalCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
        _cache = cache;
    }

    public ObservableCollection<SearchHit> Results { get; } = [];

    /// <summary>What the user typed.</summary>
    public string Query
    {
        get => _query;
        set => Set(ref _query, value ?? string.Empty);
    }

    public bool IncludeArchived
    {
        get => _includeArchived;
        set => Set(ref _includeArchived, value);
    }

    /// <summary>Gets a value indicating whether further results exist beyond this page.</summary>
    public bool HasMore
    {
        get => _hasMore;
        private set => Set(ref _hasMore, value);
    }

    /// <summary>Where the results on screen came from.</summary>
    public SearchSource Source
    {
        get => _source;
        private set => Set(ref _source, value);
    }

    /// <summary>Gets a value indicating whether a search has run at least once.</summary>
    public bool HasSearched
    {
        get => _hasSearched;
        private set => Set(ref _hasSearched, value);
    }

    /// <summary>Gets a value indicating whether the last search found nothing.</summary>
    public override bool IsEmpty => HasSearched && Results.Count == 0;

    /// <summary>Gets a value indicating whether results are stale local matches.</summary>
    public bool IsOffline => Source == SearchSource.Cache;

    /// <summary>Runs the search, preferring the server and falling back to the cache.</summary>
    public Task SearchAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            string query = Query.Trim();

            if (query.Length == 0)
            {
                Results.Clear();
                HasMore = false;
                HasSearched = false;
                Source = SearchSource.Server;
                Notify();
                return;
            }

            try
            {
                SearchResponse response = await _api
                    .SearchAsync(query, types: null, IncludeArchived, skip: 0, take: 25, token)
                    .ConfigureAwait(true);

                Results.Clear();

                foreach (SearchHit hit in response.Hits)
                {
                    Results.Add(hit);
                }

                HasMore = response.HasMore;
                Source = SearchSource.Server;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                if (_cache is null)
                {
                    throw;
                }

                SearchCache(query);
                Source = SearchSource.Cache;
            }

            HasSearched = true;
            Notify();
        }, cancellationToken);

    /// <summary>
    /// Matches against the cache when the server is unreachable.
    /// </summary>
    /// <remarks>
    /// A substring match, not the server's ranking. Every hit is scored zero and
    /// marked as such, because inventing a relevance number the server did not
    /// produce would make offline results look authoritative.
    /// </remarks>
    private void SearchCache(string query)
    {
        Results.Clear();

        foreach (PersonSummaryResponse person in _cache!.ReadPeople(query, limit: 25))
        {
            Results.Add(new SearchHit(
                "Person",
                person.Id,
                person.DisplayName,
                person.Title ?? person.Email,
                person.Status,
                Score: 0,
                MatchedOn: "Cache"));
        }

        foreach (CompanySummaryResponse company in _cache.ReadCompanies(limit: 200))
        {
            if (company.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Results.Add(new SearchHit(
                    "Company",
                    company.Id,
                    company.Name,
                    company.LegalName ?? company.Website,
                    company.Status,
                    Score: 0,
                    MatchedOn: "Cache"));
            }
        }

        HasMore = false;
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsOffline));
    }
}
