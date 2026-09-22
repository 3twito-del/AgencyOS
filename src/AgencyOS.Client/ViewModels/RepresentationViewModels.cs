using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Contracts.Representation;
using AgencyOS.Client.Presentation;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// The talent and client list.
/// </summary>
/// <remarks>
/// One list with filters rather than separate Clients and Talent screens. Being a
/// client is a state a person is in, not a different kind of record, and two
/// screens would make a former client look like a different person from the one
/// the agency signed.
/// </remarks>
public sealed class TalentListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private bool _clientsOnly;
    private bool _formerClientsOnly;
    private string? _discipline;
    private string _search = string.Empty;

    public TalentListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<TalentSummaryResponse> Talent { get; } = [];

    /// <summary>Show only people the agency currently represents.</summary>
    public bool ClientsOnly
    {
        get => _clientsOnly;
        set
        {
            if (Set(ref _clientsOnly, value) && value)
            {
                // The two filters are contradictory; asking for both would return
                // nothing and look like a bug rather than a contradiction.
                Set(ref _formerClientsOnly, false, nameof(FormerClientsOnly));
            }
        }
    }

    /// <summary>Show only people the agency used to represent.</summary>
    public bool FormerClientsOnly
    {
        get => _formerClientsOnly;
        set
        {
            if (Set(ref _formerClientsOnly, value) && value)
            {
                Set(ref _clientsOnly, false, nameof(ClientsOnly));
            }
        }
    }

    public string? Discipline
    {
        get => _discipline;
        set => Set(ref _discipline, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    public override bool IsEmpty => _loaded && Talent.Count == 0;
    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>How many of the listed people are currently clients.</summary>
    public int ClientCount => Talent.Count(x => x.IsClient);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<TalentSummaryResponse> talent = await _api
                .ListTalentAsync(
                    ClientsOnly,
                    FormerClientsOnly,
                    Discipline,
                    scope: null,
                    leadUserId: null,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    token)
                .ConfigureAwait(true);

            Talent.Clear();

            foreach (TalentSummaryResponse entry in talent)
            {
                Talent.Add(entry);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ClientCount));
        }, cancellationToken);
}

/// <summary>
/// A client's working surface: representation, team, credits, materials, history.
/// </summary>
/// <remarks>
/// Backed by the server's client overview rather than assembled from several
/// calls, so what the screen shows is one coherent answer rather than a set of
/// reads that may disagree with each other.
/// </remarks>
public sealed class ClientOverviewViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private ClientOverviewResponse? _overview;

    public ClientOverviewViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<CreditResponse> Credits { get; } = [];

    public ObservableCollection<MaterialResponse> Materials { get; } = [];

    public ObservableCollection<RepresentationHistoryEntryResponse> History { get; } = [];

    public ObservableCollection<RepresentationTeamMemberResponse> Team { get; } = [];

    public ClientOverviewResponse? Overview
    {
        get => _overview;
        private set => Set(ref _overview, value);
    }

    public override bool IsEmpty => Overview is null;

    public string DisplayName => Overview?.Talent.Talent.DisplayName ?? string.Empty;

    /// <summary>Gets a value indicating whether the agency represents them right now.</summary>
    public bool IsClient => Overview?.Talent.Talent.IsClient ?? false;

    /// <summary>
    /// One line stating exactly where the relationship stands.
    /// </summary>
    /// <remarks>
    /// Says what the agency represents them for and who owns the relationship,
    /// because "Active" alone tells somebody nothing they can act on.
    /// </remarks>
    public string StatusLine
    {
        get
        {
            if (Overview is not { } overview)
            {
                return string.Empty;
            }

            TalentSummaryResponse talent = overview.Talent.Talent;

            string status = talent.RepresentationStatus switch
            {
                null => "Not represented",
                "Active" => "Client",
                string other => other,
            };

            string scopes = talent.Scopes.Count == 0
                ? "no recorded scope"
                : string.Join(", ", talent.Scopes);

            string lead = talent.LeadDisplayName is { Length: > 0 } name
                ? $"led by {name}"
                : "no lead assigned";

            return string.Create(CultureInfo.InvariantCulture, $"{status} - {scopes} - {lead}.");
        }
    }

    /// <summary>Gets a value indicating whether internal positioning was withheld.</summary>
    /// <remarks>
    /// The UI never claims a note is absent when it was merely redacted, and never
    /// claims one exists when it does not: it simply does not show the field. This
    /// property exists so the screen can stay quiet rather than guess.
    /// </remarks>
    public bool HasPositioningNotes => !string.IsNullOrWhiteSpace(Overview?.Talent.PositioningNotes);

    public Task LoadAsync(Guid personId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            ClientOverviewResponse overview = await _api
                .GetClientOverviewAsync(personId, token)
                .ConfigureAwait(true);

            Overview = overview;

            Replace(Credits, overview.Credits);
            Replace(Materials, overview.Materials);
            Replace(History, overview.RecentHistory);
            Replace(Team, overview.Representation?.Team.Where(x => x.EndsOn is null).ToArray() ?? []);

            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(IsClient));
            OnPropertyChanged(nameof(StatusLine));
            OnPropertyChanged(nameof(HasPositioningNotes));
            OnPropertyChanged(nameof(IsEmpty));
        }, cancellationToken);

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();

        foreach (T item in source)
        {
            target.Add(item);
        }
    }
}

/// <summary>
/// The prospect pipeline, and the conversion workflow.
/// </summary>
/// <remarks>
/// Conversion is the one command here that carries an idempotency key. It creates
/// a representation, so a retry after a lost response must not sign the client
/// twice - which is exactly what the key buys (ADR-0014).
/// </remarks>
public sealed class ProspectsViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private bool _openOnly = true;
    private string? _stage;
    private ProspectResponse? _selected;

    public ProspectsViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<ProspectResponse> Prospects { get; } = [];

    public ProspectResponse? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(CanAdvance));
            }
        }
    }

    /// <summary>Show only pursuits that are still live.</summary>
    public bool OpenOnly
    {
        get => _openOnly;
        set => Set(ref _openOnly, value);
    }

    public string? Stage
    {
        get => _stage;
        set => Set(ref _stage, value);
    }

    public override bool IsEmpty => _loaded && Prospects.Count == 0;

    /// <summary>Gets a value indicating whether the selection can still be converted.</summary>
    public bool CanConvert => Selected is { Stage: "Identified" or "Contacted" or "Courting" };

    /// <summary>Gets a value indicating whether the selection can still move stage.</summary>
    public bool CanAdvance => CanConvert;

    /// <summary>Pursuits needing attention on or before today.</summary>
    public IEnumerable<ProspectResponse> DueNow(DateOnly today) =>
        Prospects.Where(x => x.NextFollowUpOn is { } due && due <= today);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<ProspectResponse> prospects = await _api
                .ListProspectsAsync(OpenOnly, Stage, ownerUserId: null, dueOnOrBefore: null, token)
                .ConfigureAwait(true);

            Prospects.Clear();

            foreach (ProspectResponse prospect in prospects)
            {
                Prospects.Add(prospect);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
        }, cancellationToken);

    /// <summary>Moves the selected pursuit to a new stage.</summary>
    public Task AdvanceAsync(
        ProspectResponse prospect,
        string stage,
        DateOnly occurredOn,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prospect);

        return RunAsync(async token =>
        {
            // The version the user was shown, not one re-read first: re-reading
            // would agree with the server by construction and defeat the check.
            await _api
                .AdvanceProspectAsync(
                    prospect.Id,
                    new AdvanceProspectRequest(stage, occurredOn, prospect.Version, reason),
                    token)
                .ConfigureAwait(true);

            await ReloadAsync(prospect.Id, token).ConfigureAwait(true);
        }, cancellationToken);
    }

    /// <summary>
    /// Converts the selected pursuit into a representation.
    /// </summary>
    /// <remarks>
    /// Carries an idempotency key generated here, before the first attempt, so a
    /// retry after a lost response replays the original answer instead of signing
    /// the client a second time.
    /// </remarks>
    public Task<RepresentationResponse?> ConvertAsync(
        ProspectResponse prospect,
        DateOnly startsOn,
        Guid leadUserId,
        IReadOnlyList<string> scopes,
        bool? isExclusive = null,
        string? territory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prospect);

        return RunWithResultAsync(async token =>
        {
            string key = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);

            RepresentationResponse representation = await _api
                .ConvertProspectAsync(
                    prospect.Id,
                    new ConvertProspectRequest(
                        startsOn,
                        leadUserId,
                        scopes,
                        prospect.Version,
                        isExclusive,
                        territory),
                    key,
                    token)
                .ConfigureAwait(true);

            await LoadAsync(token).ConfigureAwait(true);

            return representation;
        }, cancellationToken);
    }

    private async Task ReloadAsync(Guid prospectId, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(true);

        Selected = Prospects.FirstOrDefault(x => x.Id == prospectId);
    }

    /// <summary>
    /// Runs an operation that produces a value, keeping error state honest.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>RunAsync</c> but returns what the operation produced, so a
    /// conversion can hand back the representation it created without the caller
    /// having to re-read it.
    /// </remarks>
    private async Task<T?> RunWithResultAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
        where T : class
    {
        T? result = null;

        await RunAsync(async token => result = await operation(token).ConfigureAwait(true), cancellationToken)
            .ConfigureAwait(true);

        return result;
    }
}
