using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
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

/// <summary>A client signed on the prospects surface.</summary>
/// <param name="PersonId">The person now represented.</param>
/// <param name="DisplayName">Their name, as the conversion returned it.</param>
public sealed record SignedClient(Guid PersonId, string DisplayName);

/// <summary>What a surface says about a signed client's talent profile.</summary>
/// <param name="Title">The heading.</param>
/// <param name="Message">The sentence the operator reads.</param>
/// <param name="IsError">Whether an attempt was refused or failed.</param>
/// <param name="IsSuccess">Whether an attempt left a profile in place.</param>
public sealed record ProfileNotice(string Title, string Message, bool IsError = false, bool IsSuccess = false);

/// <summary>Whether a signed client has a talent profile, as far as a surface knows.</summary>
/// <remarks>
/// Five meanings, kept apart because the surface says each differently: a read not
/// yet started, a read running and a read that failed are all "not established", and
/// a surface that says one of them while another is true is asserting what it has not
/// seen. Absent is only ever what the authoritative talent read answered.
/// </remarks>
public enum TalentProfileState
{
    /// <summary>A signed client is known; no read has established anything yet.</summary>
    NotChecked,

    /// <summary>The talent read is in progress.</summary>
    Checking,

    /// <summary>The talent read answered that this person has no profile.</summary>
    Absent,

    /// <summary>The person has a talent profile.</summary>
    Present,

    /// <summary>A read was attempted and failed or was refused; existence is unknown.</summary>
    Unavailable,
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
    private SignedClient? _signed;
    private TalentProfileState _profileState;
    private int _profileRead;
    private string? _profileMessage;
    private bool _profileFailed;

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
                // A converted pursuit is a signed client, who may still need a
                // talent profile. Any other selection is not; clearing the
                // selection keeps whoever was just signed, because a refreshed list
                // drops the converted row while the operator is still looking at
                // what signing them meant.
                if (value is { Stage: "Converted" })
                {
                    SetSignedClient(new SignedClient(value.PersonId, value.DisplayName));
                }
                else if (value is not null)
                {
                    SetSignedClient(null);
                }

                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(CanAdvance));
            }
        }
    }

    /// <summary>The client signed here, or the converted pursuit selected, if any.</summary>
    public SignedClient? Signed => _signed;

    /// <summary>Whether <see cref="Signed"/> has a talent profile, as far as this surface knows.</summary>
    public TalentProfileState ProfileState => _profileState;

    /// <summary>What the last attempt to create a talent profile came to, or null.</summary>
    public string? ProfileMessage => _profileMessage;

    /// <summary>Whether the last attempt to create a talent profile was refused or failed.</summary>
    public bool ProfileFailed => _profileFailed;

    /// <summary>
    /// Gets a value indicating whether the signed client can be given a talent profile here.
    /// </summary>
    /// <remarks>
    /// Signing a client creates a representation, not a talent profile; they are
    /// separate, and the Talent roster and talent pursuits are built from profiles
    /// (owner decision C). So the next step after signing is a deliberate one. It is not
    /// offered before or while the talent read runs, so it cannot race that read; it is
    /// offered once the read says there is no profile, and where the read could not
    /// answer - the server refuses a second profile, so offering it cannot make one.
    /// </remarks>
    public bool CanCreateTalentProfile =>
        _signed is not null
        && _profileState is TalentProfileState.Absent or TalentProfileState.Unavailable;

    /// <summary>
    /// What the operator is told about the signed client's talent profile, or null
    /// when nobody has been signed here.
    /// </summary>
    /// <remarks>
    /// Absent is said only when the talent read answered that there is no profile. A
    /// read not yet started, one still running and one that failed are each said as
    /// what they are, and none of them as missing.
    /// </remarks>
    public ProfileNotice? ProfileNotice => (_signed, _profileMessage, _profileState) switch
    {
        (null, _, _) => null,
        (_, { } outcome, _) when _profileFailed => new ProfileNotice("Talent profile not created", outcome, IsError: true),
        (_, { } outcome, _) => new ProfileNotice("Talent profile", outcome, IsSuccess: true),
        ({ } client, null, TalentProfileState.Absent) => new ProfileNotice(
            "Next: create a talent profile",
            $"{client.DisplayName} is a client, but does not appear in Talent or in talent pursuits until they have a talent profile."),
        ({ } client, null, TalentProfileState.Present) => new ProfileNotice(
            "Talent profile",
            $"{client.DisplayName} has a talent profile and appears in Talent."),
        ({ } client, null, TalentProfileState.Checking) => new ProfileNotice(
            "Talent profile",
            $"Checking whether {client.DisplayName} already has a talent profile."),
        ({ } client, null, TalentProfileState.Unavailable) => new ProfileNotice(
            "Talent profile",
            $"Whether {client.DisplayName} has a talent profile could not be checked. Create one if they need to appear in Talent."),
        ({ } client, null, _) => new ProfileNotice(
            "Talent profile",
            $"{client.DisplayName} is a client. Whether they have a talent profile has not been checked yet."),
    };

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

            // Signing does not create a talent profile. Say whether one exists, so
            // the operator is told the next step rather than left to find it.
            SetSignedClient(new SignedClient(prospect.PersonId, representation.DisplayName));
            await ReadProfileStateAsync(token).ConfigureAwait(true);

            return representation;
        }, cancellationToken);
    }

    /// <summary>Reads whether the signed client already has a talent profile.</summary>
    /// <remarks>
    /// The talent read is the authority on that question: it answers not-found for
    /// a person with no profile. While it runs the state is Checking; a refusal or any
    /// other failure makes it Unavailable, never Absent - a surface that could not look
    /// must not claim there is nothing.
    /// </remarks>
    public Task CheckTalentProfileAsync(CancellationToken cancellationToken = default) =>
        ReadProfileStateAsync(cancellationToken);

    /// <summary>
    /// Creates the signed client's talent profile, as its own deliberate step.
    /// </summary>
    /// <param name="careerStage">The career stage the operator chose; Unknown is honest.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Whether a profile now exists for the signed client.</returns>
    /// <remarks>
    /// Uses the existing talent-profile command, which the server authorizes on its
    /// own permission. A refusal or failure leaves the representation exactly as it
    /// was and creates nothing on this side; the operator is told which happened.
    /// </remarks>
    public async Task<bool> CreateTalentProfileAsync(
        string careerStage,
        CancellationToken cancellationToken = default)
    {
        if (_signed is not { } client || !CanCreateTalentProfile)
        {
            return false;
        }

        string key = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        string? message;
        bool failed;

        try
        {
            await _api
                .CreateTalentProfileAsync(
                    new CreateTalentProfileRequest(client.PersonId, careerStage),
                    key,
                    cancellationToken)
                .ConfigureAwait(true);

            _profileState = TalentProfileState.Present;
            message = $"Talent profile created. {client.DisplayName} now appears in Talent.";
            failed = false;
        }
        catch (AgencyOsApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            _profileState = TalentProfileState.Present;
            message = $"{client.DisplayName} already has a talent profile, so none was created.";
            failed = false;
        }
        catch (AgencyOsApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            message = $"You do not have permission to create talent profiles. {client.DisplayName} is still a client.";
            failed = true;
        }
        catch (AgencyOsApiException ex)
        {
            message = $"The talent profile was not created. {ex.Detail ?? ex.Message} {client.DisplayName} is still a client.";
            failed = true;
        }
        catch (HttpRequestException ex)
        {
            message = $"Cannot reach the AgencyOS server, so the talent profile was not created. {ex.Message}";
            failed = true;
        }

        if (_profileState == TalentProfileState.Present)
        {
            // Established by the command itself; no earlier read may overwrite it.
            _profileRead++;
        }

        _profileMessage = message;
        _profileFailed = failed;
        RaiseProfileChanged();

        return _profileState == TalentProfileState.Present;
    }

    private async Task ReadProfileStateAsync(CancellationToken cancellationToken)
    {
        if (_signed is not { } client)
        {
            return;
        }

        // Only the latest read for the current client may say anything: an earlier
        // read, or one for a client the operator has since moved away from, is ignored.
        int read = ++_profileRead;

        _profileState = TalentProfileState.Checking;
        RaiseProfileChanged();

        TalentProfileState state;

        try
        {
            await _api.GetTalentAsync(client.PersonId, cancellationToken).ConfigureAwait(true);
            state = TalentProfileState.Present;
        }
        catch (AgencyOsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            state = TalentProfileState.Absent;
        }
        catch (Exception ex) when (ex is AgencyOsApiException or HttpRequestException)
        {
            state = TalentProfileState.Unavailable;
        }

        if (read == _profileRead && _signed == client)
        {
            _profileState = state;
            RaiseProfileChanged();
        }
    }

    private void SetSignedClient(SignedClient? client)
    {
        if (_signed == client)
        {
            return;
        }

        _signed = client;
        _profileRead++;
        _profileState = TalentProfileState.NotChecked;
        _profileMessage = null;
        _profileFailed = false;
        RaiseProfileChanged();
    }

    private void RaiseProfileChanged()
    {
        OnPropertyChanged(nameof(Signed));
        OnPropertyChanged(nameof(ProfileState));
        OnPropertyChanged(nameof(ProfileMessage));
        OnPropertyChanged(nameof(ProfileFailed));
        OnPropertyChanged(nameof(CanCreateTalentProfile));
        OnPropertyChanged(nameof(ProfileNotice));
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
