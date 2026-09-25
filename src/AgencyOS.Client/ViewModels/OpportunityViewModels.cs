using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Client.Presentation;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// The pipeline as a dense list: every pursuit, with what it is waiting on.
/// </summary>
/// <remarks>
/// A list rather than a board by default. A board shows stage and hides everything
/// else, and the questions an agent actually has - what is overdue, what has had no
/// reply - are answered by columns a board has nowhere to put.
/// </remarks>
public sealed class OpportunityListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status = "Active";
    private string? _kind;
    private bool _awaitingResponse;
    private string _search = string.Empty;

    public OpportunityListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<OpportunitySummaryResponse> Opportunities { get; } = [];

    /// <summary>Restrict to one status. Defaults to what is being worked.</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string? Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    /// <summary>Show only pursuits with a reply overdue.</summary>
    public bool AwaitingResponse
    {
        get => _awaitingResponse;
        set => Set(ref _awaitingResponse, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    public override bool IsEmpty => _loaded && Opportunities.Count == 0;

    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>How many listed pursuits have a reply overdue.</summary>
    public int Waiting => Opportunities.Count(x => x.AwaitingResponseCount > 0);

    /// <summary>How many have an action past its date.</summary>
    public int Overdue
    {
        get
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

            return Opportunities.Count(x => x.NextActionOn is { } due && due < today);
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<OpportunitySummaryResponse> opportunities = await _api
                .ListOpportunitiesAsync(
                    Status,
                    Kind,
                    ownerUserId: null,
                    AwaitingResponse,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    cancellationToken: token)
                .ConfigureAwait(true);

            Opportunities.Clear();

            foreach (OpportunitySummaryResponse opportunity in opportunities)
            {
                Opportunities.Add(opportunity);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Waiting));
            OnPropertyChanged(nameof(Overdue));
        }, cancellationToken);
}

/// <summary>
/// One pursuit's working surface: subjects, targets, what has gone out, what happened.
/// </summary>
/// <remarks>
/// Backed by one server read rather than assembled from several calls, so the
/// screen shows one coherent answer rather than a set of reads that can disagree.
/// </remarks>
public sealed class OpportunityDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private OpportunityDetailResponse? _opportunity;
    private bool _loaded;

    public OpportunityDetailViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public OpportunityDetailResponse? Opportunity
    {
        get => _opportunity;
        private set => Set(ref _opportunity, value);
    }

    public ObservableCollection<OpportunitySubjectResponse> Subjects { get; } = [];

    public ObservableCollection<OpportunityTargetResponse> Targets { get; } = [];

    public ObservableCollection<SubmissionResponse> Submissions { get; } = [];

    public ObservableCollection<PitchResponse> Pitches { get; } = [];

    public ObservableCollection<OpportunityTaskResponse> Tasks { get; } = [];

    public ObservableCollection<OpportunityHistoryEntryResponse> History { get; } = [];

    public override bool IsEmpty => _loaded && Opportunity is null;

    /// <summary>Whether the caller may read the pursuit's strategy.</summary>
    /// <remarks>
    /// Derived from the value being present rather than from a separate flag. The
    /// API returns the field absent, and absent is deliberately indistinguishable
    /// from empty, so this is true only when there is something to show.
    /// </remarks>
    public bool HasStrategy => !string.IsNullOrWhiteSpace(Opportunity?.StrategyNotes);

    /// <summary>Targets that can still move.</summary>
    public IReadOnlyList<OpportunityTargetResponse> OpenTargets =>
        [.. Targets.Where(x => x.IsOpen)];

    /// <summary>Targets with an action past its date.</summary>
    public IReadOnlyList<OpportunityTargetResponse> Overdue
    {
        get
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

            return [.. Targets.Where(x => x.IsOpen && x.NextActionOn is { } due && due < today)];
        }
    }

    /// <summary>A plain sentence about where the pursuit stands.</summary>
    public string Standing
    {
        get
        {
            if (Opportunity is not { } detail)
            {
                return string.Empty;
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{detail.Opportunity.Status} - {OpenTargets.Count} of {Targets.Count} targets open, {detail.Opportunity.SubmissionCount} submitted, {detail.Opportunity.AwaitingResponseCount} awaiting a reply");
        }
    }

    public Task LoadAsync(Guid opportunityId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            OpportunityDetailResponse detail = await _api
                .GetOpportunityAsync(opportunityId, token)
                .ConfigureAwait(true);

            Opportunity = detail;

            Replace(Subjects, detail.Subjects);
            Replace(Targets, detail.Targets);
            Replace(Submissions, detail.Submissions);
            Replace(Pitches, detail.Pitches);
            Replace(Tasks, detail.OpenTasks);

            IReadOnlyList<OpportunityHistoryEntryResponse> history = await _api
                .GetOpportunityHistoryAsync(opportunityId, token)
                .ConfigureAwait(true);

            Replace(History, history);

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasStrategy));
            OnPropertyChanged(nameof(OpenTargets));
            OnPropertyChanged(nameof(Overdue));
            OnPropertyChanged(nameof(Standing));
            OnPropertyChanged(nameof(CanActivate));
        }, cancellationToken);

    /// <summary>
    /// Gets a value indicating whether the loaded pursuit is a Draft the operator can activate.
    /// </summary>
    /// <remarks>
    /// A pursuit is created as a Draft, and only an Active one accepts market
    /// activity - moving a target, opening a negotiation. The fresh final-candidate
    /// RC at <c>2d2b46a</c> stopped because nothing in the Windows client took that
    /// step. This offers exactly Draft to Active and nothing else; any other
    /// lifecycle change is not what the operator was blocked on.
    /// </remarks>
    public bool CanActivate => Opportunity is { Opportunity.Status: "Draft" };

    /// <summary>
    /// Activates the loaded Draft pursuit, then reloads what the server now holds.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Whether the activation was sent.</returns>
    /// <remarks>
    /// The existing status command, with the version the operator was shown and a key
    /// made before the one attempt; the server decides whether the change is legal.
    /// A refusal - a version conflict included - is thrown to the caller unchanged,
    /// and nothing here pretends the pursuit is Active: the loaded state stays as the
    /// server last described it. There is no retry with a newer version.
    /// </remarks>
    public async Task<bool> ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (Opportunity is not { } detail || !CanActivate)
        {
            return false;
        }

        OpportunitySummaryResponse pursuit = detail.Opportunity;

        await _api
            .ChangeOpportunityStatusAsync(
                pursuit.Id,
                new ChangeOpportunityStatusRequest("Active", pursuit.Version),
                Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture),
                cancellationToken)
            .ConfigureAwait(true);

        await LoadAsync(pursuit.Id, cancellationToken).ConfigureAwait(true);

        OnPropertyChanged(nameof(CanActivate));

        return true;
    }

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
/// The board view: open targets grouped by how far along they are.
/// </summary>
/// <remarks>
/// Offered alongside the dense list, never instead of it. It answers one question
/// well - where is everything - and is the wrong shape for every other question an
/// agent has.
/// </remarks>
public sealed class PipelineViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;

    public PipelineViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<PipelineColumnResponse> Columns { get; } = [];

    public override bool IsEmpty => _loaded && Columns.All(x => x.Targets.Count == 0);

    /// <summary>How many open targets the pipeline holds in total.</summary>
    public int TargetCount => Columns.Sum(x => x.Targets.Count);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<PipelineColumnResponse> columns = await _api
                .GetPipelineAsync(ownerUserId: null, token)
                .ConfigureAwait(true);

            Columns.Clear();

            foreach (PipelineColumnResponse column in columns)
            {
                Columns.Add(column);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(TargetCount));
        }, cancellationToken);
}

/// <summary>
/// One target's workspace: what has gone to them, and what they said.
/// </summary>
public sealed class OpportunityTargetViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private OpportunityTargetResponse? _target;
    private bool _loaded;

    public OpportunityTargetViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public OpportunityTargetResponse? Target
    {
        get => _target;
        private set => Set(ref _target, value);
    }

    public ObservableCollection<SubmissionResponse> Submissions { get; } = [];

    public override bool IsEmpty => _loaded && Target is null;

    /// <summary>Submissions that have had no reply since the date one was expected.</summary>
    /// <remarks>
    /// Derived from the submission rows the server returned. Nothing anywhere is
    /// written to represent a buyer's silence.
    /// </remarks>
    public IReadOnlyList<SubmissionResponse> AwaitingResponse =>
        [.. Submissions.Where(x => x.IsAwaitingResponse)];

    /// <summary>A plain sentence about where this target stands.</summary>
    public string Standing
    {
        get
        {
            if (Target is not { } target)
            {
                return string.Empty;
            }

            string waiting = target.AwaitingResponseSince is { } since
                ? string.Create(CultureInfo.InvariantCulture, $", awaiting a reply since {since:yyyy-MM-dd}")
                : string.Empty;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{target.Stage} - {target.SubmissionCount} submitted, {target.PitchCount} pitched{waiting}");
        }
    }

    public Task LoadAsync(Guid targetId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            OpportunityTargetResponse target = await _api
                .GetOpportunityTargetAsync(targetId, token)
                .ConfigureAwait(true);

            Target = target;

            IReadOnlyList<SubmissionResponse> submissions = await _api
                .ListSubmissionsAsync(opportunityId: null, targetId, token)
                .ConfigureAwait(true);

            Submissions.Clear();

            foreach (SubmissionResponse submission in submissions)
            {
                Submissions.Add(submission);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(AwaitingResponse));
            OnPropertyChanged(nameof(Standing));
        }, cancellationToken);
}
