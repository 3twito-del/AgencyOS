using System.Collections.ObjectModel;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Client.Presentation;

namespace AgencyOS.Client.ViewModels;

/// <summary>The people directory.</summary>
public sealed class PeopleListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;
    private string _searchText = string.Empty;
    private PersonSummaryResponse? _selected;

    public PeopleListViewModel(IAgencyOsApi api) => _api = api;

    public ObservableCollection<PersonSummaryResponse> People { get; } = [];

    public override bool IsEmpty => !IsLoading && !HasError && People.Count == 0;

    public string SearchText
    {
        get => _searchText;
        set => Set(ref _searchText, value);
    }

    public PersonSummaryResponse? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<PersonSummaryResponse> people =
                await _api.ListPeopleAsync(SearchText, token).ConfigureAwait(true);

            People.Clear();

            foreach (PersonSummaryResponse person in people)
            {
                People.Add(person);
            }
        }, cancellationToken);
}

/// <summary>One person: their record, their relationships, and their history.</summary>
public sealed class PersonDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;
    private PersonDetailResponse? _person;

    public PersonDetailViewModel(IAgencyOsApi api) => _api = api;

    public PersonDetailResponse? Person
    {
        get => _person;
        private set
        {
            if (Set(ref _person, value))
            {
                OnPropertyChanged(nameof(Relationships));
            }
        }
    }

    public ObservableCollection<TimelineEntryResponse> Timeline { get; } = [];

    public IReadOnlyList<RelationshipResponse> Relationships =>
        _person?.Relationships ?? [];

    public override bool IsEmpty => !IsLoading && !HasError && _person is null;

    public Task LoadAsync(Guid personId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            Person = await _api.GetPersonAsync(personId, token).ConfigureAwait(true);

            IReadOnlyList<TimelineEntryResponse> timeline =
                await _api.GetPersonTimelineAsync(personId, token).ConfigureAwait(true);

            Timeline.Clear();

            foreach (TimelineEntryResponse entry in timeline)
            {
                Timeline.Add(entry);
            }
        }, cancellationToken);

    /// <summary>Ends a relationship and reloads, so the screen never shows stale state.</summary>
    public Task EndRelationshipAsync(Guid relationshipId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api.EndRelationshipAsync(relationshipId, token).ConfigureAwait(true);

            if (_person is { } person)
            {
                Person = await _api.GetPersonAsync(person.Person.Id, token).ConfigureAwait(true);
            }
        }, cancellationToken);
}

/// <summary>The company directory.</summary>
public sealed class CompanyListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;
    private string _searchText = string.Empty;
    private CompanySummaryResponse? _selected;

    public CompanyListViewModel(IAgencyOsApi api) => _api = api;

    public ObservableCollection<CompanySummaryResponse> Companies { get; } = [];

    public override bool IsEmpty => !IsLoading && !HasError && Companies.Count == 0;

    public string SearchText
    {
        get => _searchText;
        set => Set(ref _searchText, value);
    }

    public CompanySummaryResponse? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<CompanySummaryResponse> companies =
                await _api.ListCompaniesAsync(SearchText, token).ConfigureAwait(true);

            Companies.Clear();

            foreach (CompanySummaryResponse company in companies)
            {
                Companies.Add(company);
            }
        }, cancellationToken);
}

/// <summary>
/// The operational picture: what needs attention now.
/// </summary>
/// <remarks>
/// Backed entirely by the server's Command Center query. Nothing here is computed
/// on the client, so what the user sees is what the domain says.
/// </remarks>
public sealed class CommandCenterViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;
    private CommandCenterResponse? _view;

    public CommandCenterViewModel(IAgencyOsApi api) => _api = api;

    public ObservableCollection<TaskResponse> Overdue { get; } = [];

    public ObservableCollection<TaskResponse> DueSoon { get; } = [];

    public ObservableCollection<TaskResponse> Unscheduled { get; } = [];

    public ObservableCollection<InteractionResponse> RecentInteractions { get; } = [];

    /// <summary>
    /// Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.
    /// </summary>
    /// <remarks>
    /// The projection itself is the discriminator. Until it arrives the three
    /// counts below have no answer, and <c>?? 0</c> used to supply one - a headline
    /// reading <c>0 open tasks</c> over a workspace that had failed to load.
    /// </remarks>
    public bool HasLoaded => _view is not null;

    /// <summary>Open tasks across the tenant. Meaningful only when <see cref="HasLoaded"/>.</summary>
    public int OpenTaskCount => _view?.OpenTaskCount ?? 0;

    /// <summary>People on file. Meaningful only when <see cref="HasLoaded"/>.</summary>
    public int PeopleCount => _view?.PeopleCount ?? 0;

    /// <summary>Companies on file. Meaningful only when <see cref="HasLoaded"/>.</summary>
    public int CompanyCount => _view?.CompanyCount ?? 0;

    public override bool IsEmpty =>
        !IsLoading
        && !HasError
        && Overdue.Count == 0
        && DueSoon.Count == 0
        && Unscheduled.Count == 0
        && RecentInteractions.Count == 0;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            CommandCenterResponse view = await _api.GetCommandCenterAsync(token).ConfigureAwait(true);
            _view = view;

            Replace(Overdue, view.Overdue);
            Replace(DueSoon, view.DueSoon);
            Replace(Unscheduled, view.Unscheduled);
            Replace(RecentInteractions, view.RecentInteractions);

            OnPropertyChanged(nameof(OpenTaskCount));
            OnPropertyChanged(nameof(PeopleCount));
            OnPropertyChanged(nameof(CompanyCount));
        }, cancellationToken);

    /// <summary>Completes a task and refreshes, so the buckets stay truthful.</summary>
    /// <param name="taskId">Task to complete.</param>
    /// <param name="expectedVersion">
    /// The version shown to the user. Sending it is what stops a completion made
    /// from a stale list quietly overwriting somebody else's change.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    public Task CompleteAsync(Guid taskId, int expectedVersion, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await _api
                .CompleteTaskAsync(taskId, new TaskTransitionRequest(expectedVersion), null, token)
                .ConfigureAwait(true);

            CommandCenterResponse view = await _api.GetCommandCenterAsync(token).ConfigureAwait(true);
            _view = view;

            Replace(Overdue, view.Overdue);
            Replace(DueSoon, view.DueSoon);
            Replace(Unscheduled, view.Unscheduled);
            Replace(RecentInteractions, view.RecentInteractions);

            OnPropertyChanged(nameof(OpenTaskCount));
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
/// Recording what happened and, in the same breath, what happens next.
/// </summary>
/// <remarks>
/// The follow-up is part of this screen rather than a separate step, because
/// "met Sarah, send her the screenplay Monday" is one thought. The server records
/// both in one transaction, so the user cannot end up with the memory but not the
/// commitment.
/// </remarks>
public sealed class RecordInteractionViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private string _type = "Meeting";
    private DateTimeOffset _occurredAt = DateTimeOffset.UtcNow;
    private string _summary = string.Empty;
    private string? _detailedNotes;
    private bool _createFollowUp;
    private string _followUpTitle = string.Empty;
    private DateTimeOffset? _followUpDueAt;
    private string _followUpPriority = "Normal";

    public RecordInteractionViewModel(IAgencyOsApi api) => _api = api;

    /// <summary>Parties taking part. At least one is required by the server.</summary>
    public ObservableCollection<PartyRefRequest> Participants { get; } = [];

    public override bool IsEmpty => false;

    public string InteractionType
    {
        get => _type;
        set => Set(ref _type, value);
    }

    public DateTimeOffset OccurredAt
    {
        get => _occurredAt;
        set => Set(ref _occurredAt, value);
    }

    public string Summary
    {
        get => _summary;
        set
        {
            if (Set(ref _summary, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public string? DetailedNotes
    {
        get => _detailedNotes;
        set => Set(ref _detailedNotes, value);
    }

    public bool CreateFollowUp
    {
        get => _createFollowUp;
        set
        {
            if (Set(ref _createFollowUp, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public string FollowUpTitle
    {
        get => _followUpTitle;
        set
        {
            if (Set(ref _followUpTitle, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public DateTimeOffset? FollowUpDueAt
    {
        get => _followUpDueAt;
        set => Set(ref _followUpDueAt, value);
    }

    public string FollowUpPriority
    {
        get => _followUpPriority;
        set => Set(ref _followUpPriority, value);
    }

    /// <summary>
    /// Gets a value indicating whether the form is complete enough to send.
    /// </summary>
    /// <remarks>
    /// A client-side convenience only. The server enforces the same rules, and a
    /// caller that bypasses this screen is refused there.
    /// </remarks>
    public bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_summary)
        && Participants.Count > 0
        && (!_createFollowUp || !string.IsNullOrWhiteSpace(_followUpTitle));

    /// <summary>Adds a participant, ignoring duplicates.</summary>
    public void AddParticipant(PartyRefRequest party)
    {
        ArgumentNullException.ThrowIfNull(party);

        if (Participants.Any(p => p.Kind == party.Kind && p.Id == party.Id))
        {
            return;
        }

        Participants.Add(party);
        OnPropertyChanged(nameof(CanSubmit));
    }

    public void RemoveParticipant(PartyRefRequest party)
    {
        PartyRefRequest? existing = Participants
            .FirstOrDefault(p => p.Kind == party.Kind && p.Id == party.Id);

        if (existing is not null)
        {
            Participants.Remove(existing);
            OnPropertyChanged(nameof(CanSubmit));
        }
    }

    /// <summary>Records the interaction, with its follow-up when one was asked for.</summary>
    /// <returns>The server's answer, or null when the request failed.</returns>
    public async Task<RecordInteractionResponse?> SubmitAsync(CancellationToken cancellationToken = default)
    {
        RecordInteractionResponse? result = null;

        await RunAsync(async token =>
        {
            RecordInteractionRequest request = new(
                InteractionType,
                OccurredAt,
                Summary,
                [.. Participants.Select(p => new InteractionParticipantRequest(p))],
                DetailedNotes,
                CreateFollowUp
                    ? new FollowUpTaskRequest(FollowUpTitle, FollowUpDueAt, FollowUpPriority)
                    : null);

            result = await _api.RecordInteractionAsync(request, null, token).ConfigureAwait(true);
        }, cancellationToken).ConfigureAwait(true);

        return result;
    }

    /// <summary>Clears the form for the next capture.</summary>
    public void Reset()
    {
        Summary = string.Empty;
        DetailedNotes = null;
        CreateFollowUp = false;
        FollowUpTitle = string.Empty;
        FollowUpDueAt = null;
        FollowUpPriority = "Normal";
        OccurredAt = DateTimeOffset.UtcNow;
        Participants.Clear();
        OnPropertyChanged(nameof(CanSubmit));
    }
}
