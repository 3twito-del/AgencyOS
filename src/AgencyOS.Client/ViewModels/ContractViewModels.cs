using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Contracts.Legal;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// The contract list: every instrument, and what each is waiting on.
/// </summary>
/// <remarks>
/// A dense table rather than a board, for the reason the deal list gives: a board
/// shows status and hides the rest, and the questions a legal desk actually has -
/// who still has to sign, what differs from what was agreed, what is due this week
/// - are ones a board has nowhere to put.
/// </remarks>
public sealed class ContractListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status;
    private string? _kind;
    private bool _awaitingSignature;
    private bool _effectiveOnly;
    private bool _differencesOnly;
    private string _search = string.Empty;

    public ContractListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<ContractSummaryResponse> Contracts { get; } = [];

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

    /// <summary>Show only instruments with a required signature outstanding.</summary>
    public bool AwaitingSignature
    {
        get => _awaitingSignature;
        set => Set(ref _awaitingSignature, value);
    }

    /// <summary>Show only instruments in force today.</summary>
    public bool EffectiveOnly
    {
        get => _effectiveOnly;
        set => Set(ref _effectiveOnly, value);
    }

    /// <summary>
    /// Show only instruments whose newest draft differs from what was agreed.
    /// </summary>
    /// <remarks>
    /// A filter on a count, not on a judgement. It says the paper and the handshake
    /// are not identical, which is a reason to read the paper, not a finding that
    /// anything is wrong (ADR-0022).
    /// </remarks>
    public bool DifferencesOnly
    {
        get => _differencesOnly;
        set => Set(ref _differencesOnly, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    public override bool IsEmpty => _loaded && Contracts.Count == 0;

    /// <summary>How many listed instruments still need a signature.</summary>
    public int Unsigned => Contracts.Count(x => x.OutstandingSignatureCount > 0);

    /// <summary>How many are in force today.</summary>
    public int Effective => Contracts.Count(x => x.IsEffective);

    /// <summary>How many carry at least one difference from what was agreed.</summary>
    public int WithDifferences => Contracts.Count(x => x.UnresolvedDifferenceCount > 0);

    /// <summary>Instruments with a legal date inside a week.</summary>
    /// <remarks>
    /// Counts only contracts whose next deadline actually resolved to a date. One
    /// whose every clause hangs off an event that has not happened is not counted,
    /// because it genuinely has no next date (ADR-0022).
    /// </remarks>
    public int DueThisWeek
    {
        get
        {
            DateOnly horizon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

            return Contracts.Count(x => x.NextDeadlineOn is { } due && due <= horizon);
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<ContractSummaryResponse> contracts = await _api
                .ListContractsAsync(
                    Status,
                    Kind,
                    dealId: null,
                    AwaitingSignature,
                    EffectiveOnly,
                    DifferencesOnly,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    token)
                .ConfigureAwait(true);

            Contracts.Clear();

            foreach (ContractSummaryResponse contract in contracts)
            {
                Contracts.Add(contract);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Unsigned));
            OnPropertyChanged(nameof(Effective));
            OnPropertyChanged(nameof(WithDifferences));
            OnPropertyChanged(nameof(DueThisWeek));
        }, cancellationToken);
}

/// <summary>
/// One contract's working surface: the drafts, the parties, the obligations.
/// </summary>
public sealed class ContractDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private ContractDetailResponse? _contract;
    private bool _loaded;

    public ContractDetailViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ContractDetailResponse? Contract
    {
        get => _contract;
        private set => Set(ref _contract, value);
    }

    /// <summary>The drafting history, newest first.</summary>
    public ObservableCollection<ContractVersionResponse> Versions { get; } = [];

    public ObservableCollection<ContractPartyResponse> Parties { get; } = [];

    public ObservableCollection<RightsGrantResponse> RightsGrants { get; } = [];

    public ObservableCollection<ContractOptionResponse> Options { get; } = [];

    public ObservableCollection<ObligationResponse> Obligations { get; } = [];

    public ObservableCollection<NoticeRequirementResponse> NoticeRequirements { get; } = [];

    public ObservableCollection<NoticeRecordResponse> RecordedNotices { get; } = [];

    public ObservableCollection<ContractRelationshipResponse> Relationships { get; } = [];

    public ObservableCollection<ContractTaskResponse> Tasks { get; } = [];

    public ObservableCollection<ContractHistoryEntryResponse> History { get; } = [];

    public override bool IsEmpty => _loaded && Contract is null;

    /// <summary>Whether the caller may read what counsel thinks.</summary>
    /// <remarks>
    /// Derived from the value being present rather than from a flag. The API
    /// returns privileged fields absent, and absent is deliberately
    /// indistinguishable from empty, so this is true only when there is something
    /// to show (ADR-0022).
    /// </remarks>
    public bool HasLegalAnalysis => !string.IsNullOrWhiteSpace(Contract?.LegalAnalysis);

    /// <summary>Whether the caller may read the agency's strategy on the paper.</summary>
    public bool HasStrategy => !string.IsNullOrWhiteSpace(Contract?.StrategyNotes);

    /// <summary>
    /// Whether the caller can see what the drafts actually say.
    /// </summary>
    /// <remarks>
    /// Inferred from a version carrying a term. Without <c>contracts.terms.read</c>
    /// those rows are absent, so a screen that offered a reconciliation the server
    /// would refuse can simply not offer it.
    /// </remarks>
    public bool HasTerms => Versions.Any(version => version.Terms.Count > 0);

    /// <summary>Whether the caller can see the figures inside those terms.</summary>
    public bool HasEconomics =>
        Versions.Any(version => version.Terms.Any(term => term.IsEconomic));

    /// <summary>The newest drafting version, when there is one.</summary>
    public ContractVersionResponse? LatestVersion =>
        Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();

    /// <summary>The parties whose signature the agreement still needs.</summary>
    public IReadOnlyList<ContractPartyResponse> OutstandingSignatories =>
        [.. Parties.Where(x => x.IsRequiredSignatory && !x.HasSigned)];

    /// <summary>Whether a further drafting version can still be recorded.</summary>
    public bool AcceptsNewVersions =>
        Contract?.Contract.Status is "Draft" or "UnderReview" or "ApprovedForExecution";

    /// <summary>Whether a signature can still be recorded.</summary>
    public bool AcceptsSignatures =>
        Contract?.Contract.Status is "ApprovedForExecution" or "PartiallyExecuted";

    /// <summary>Obligations past their date and still outstanding.</summary>
    /// <remarks>
    /// Past due, never breached. The screen says a date went by; whether that is a
    /// breach is a legal determination somebody records separately (ADR-0022).
    /// </remarks>
    public IReadOnlyList<ObligationResponse> PastDue =>
        [.. Obligations.Where(x => x.IsPastDue)];

    /// <summary>
    /// Deadlines this build could not work out.
    /// </summary>
    /// <remarks>
    /// Surfaced deliberately rather than hidden. A clause measured from a delivery
    /// that has not happened, or counted in business days AgencyOS has no calendar
    /// for, has no date - and a lawyer needs to know which clauses those are far
    /// more than they need a blank column (ADR-0022).
    /// </remarks>
    public IReadOnlyList<string> UnresolvedDeadlines =>
    [
        .. Options
            .Where(x => x.DeadlineOn is null && x.DeadlineUnresolvedReason is not null)
            .Select(x => $"{x.Subject}: {x.DeadlineUnresolvedReason}"),

        .. Obligations
            .Where(x => x.DueOn is null && x.DueUnresolvedReason is not null)
            .Select(x => $"{x.Description}: {x.DueUnresolvedReason}"),
    ];

    /// <summary>
    /// A plain sentence about where the instrument stands.
    /// </summary>
    /// <remarks>
    /// Separates the four dates the milestone exists to keep apart: it says
    /// "executed", "effective" and "terminated" only when the contract carries each
    /// date, and never infers one from another (ADR-0022).
    /// </remarks>
    public string Standing
    {
        get
        {
            if (Contract is not { } detail)
            {
                return string.Empty;
            }

            ContractSummaryResponse contract = detail.Contract;

            string state = contract.Status switch
            {
                "Draft" => "being drafted",
                "UnderReview" => "under review",
                "ApprovedForExecution" => "approved, awaiting signature",
                "PartiallyExecuted" => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{contract.OutstandingSignatureCount} signature(s) outstanding"),
                "Executed" when contract.ExecutedOn is { } executed => string.Create(
                    CultureInfo.InvariantCulture,
                    $"fully executed {executed:yyyy-MM-dd}"),
                "Executed" => "fully executed",
                "Abandoned" => "abandoned",
                "Superseded" => "superseded by another agreement",
                "Terminated" when contract.TerminatedOn is { } ended => string.Create(
                    CultureInfo.InvariantCulture,
                    $"terminated {ended:yyyy-MM-dd}"),
                "Terminated" => "terminated",
                _ => contract.Status,
            };

            string effect = contract.EffectiveOn is { } from
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $", effective from {from:yyyy-MM-dd}")
                : ", no effective date recorded";

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{contract.Kind} with {contract.CounterpartyDisplayName} - {state}{effect}");
        }
    }

    /// <summary>
    /// What the newest draft did to what was agreed, as a sentence.
    /// </summary>
    /// <remarks>
    /// A count and nothing more. It never says a difference is unfavourable,
    /// material or a risk: those are legal judgements about a document AgencyOS has
    /// not read (ADR-0022).
    /// </remarks>
    public string ReconciliationStanding
    {
        get
        {
            if (Contract?.Contract.UnresolvedDifferenceCount is not { } differences)
            {
                return "No drafting version recorded yet.";
            }

            return differences == 0
                ? "The latest draft records the terms that were agreed."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{differences} term(s) differ between the agreed terms and the latest draft.");
        }
    }

    /// <summary>
    /// Whether AgencyOS holds the documents themselves.
    /// </summary>
    /// <remarks>
    /// Always false in this build, and read off the server's own answer rather than
    /// assumed, so the screen tells the truth about what it has: a reference to a
    /// file somebody else stores (ADR-0022).
    /// </remarks>
    public bool HoldsDocuments => Versions.Any(x => x.HoldsDocument);

    public Task LoadAsync(Guid contractId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            ContractDetailResponse detail = await _api
                .GetContractAsync(contractId, token)
                .ConfigureAwait(true);

            Contract = detail;

            Replace(Versions, [.. detail.Versions.OrderByDescending(x => x.VersionNumber)]);
            Replace(Parties, detail.Parties);
            Replace(RightsGrants, detail.RightsGrants);
            Replace(Options, detail.Options);
            Replace(Obligations, detail.Obligations);
            Replace(NoticeRequirements, detail.NoticeRequirements);
            Replace(RecordedNotices, detail.RecordedNotices);
            Replace(Relationships, detail.Relationships);
            Replace(Tasks, detail.OpenTasks);

            IReadOnlyList<ContractHistoryEntryResponse> history = await _api
                .GetContractHistoryAsync(contractId, token)
                .ConfigureAwait(true);

            Replace(History, history);

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasLegalAnalysis));
            OnPropertyChanged(nameof(HasStrategy));
            OnPropertyChanged(nameof(HasTerms));
            OnPropertyChanged(nameof(HasEconomics));
            OnPropertyChanged(nameof(LatestVersion));
            OnPropertyChanged(nameof(OutstandingSignatories));
            OnPropertyChanged(nameof(AcceptsNewVersions));
            OnPropertyChanged(nameof(AcceptsSignatures));
            OnPropertyChanged(nameof(PastDue));
            OnPropertyChanged(nameof(UnresolvedDeadlines));
            OnPropertyChanged(nameof(Standing));
            OnPropertyChanged(nameof(ReconciliationStanding));
            OnPropertyChanged(nameof(HoldsDocuments));
        }, cancellationToken);

    internal static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();

        foreach (T item in source)
        {
            target.Add(item);
        }
    }
}

/// <summary>
/// What a drafting version did to what was agreed.
/// </summary>
/// <remarks>
/// Answers "what did the lawyers do to our deal". It carries no opinion about
/// whether a change is welcome: whether a term is acceptable is a legal question
/// about a document AgencyOS has not read, and answering it would be answering it
/// wrongly some of the time in a place nobody checks (ADR-0022).
/// </remarks>
public sealed class ReconciliationViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private ReconciliationResponse? _reconciliation;
    private bool _loaded;
    private bool _differencesOnly = true;

    public ReconciliationViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<ReconciliationLineResponse> Lines { get; } = [];

    public ReconciliationResponse? Reconciliation
    {
        get => _reconciliation;
        private set => Set(ref _reconciliation, value);
    }

    /// <summary>Hide the terms the draft records exactly as agreed.</summary>
    /// <remarks>
    /// On by default. A faithful line is the expected case, and a reader scanning
    /// forty of them to find the two that moved is a reader who stops scanning.
    /// </remarks>
    public bool DifferencesOnly
    {
        get => _differencesOnly;
        set
        {
            if (Set(ref _differencesOnly, value))
            {
                Apply();
            }
        }
    }

    public override bool IsEmpty => _loaded && Lines.Count == 0;

    /// <summary>Whether the draft says exactly what was negotiated.</summary>
    public bool IsFaithful => Reconciliation?.IsFaithful ?? false;

    /// <summary>How many terms are not a plain match.</summary>
    public int DifferenceCount => Reconciliation?.DifferenceCount ?? 0;

    /// <summary>
    /// A factual sentence about the comparison.
    /// </summary>
    /// <remarks>
    /// Counts by outcome, and no adjective. "Two terms changed, one is missing" is
    /// something a lawyer acts on; "one material adverse change" is a claim the
    /// system is not entitled to make.
    /// </remarks>
    public string Summary
    {
        get
        {
            if (Reconciliation is null)
            {
                return string.Empty;
            }

            if (IsFaithful)
            {
                return "The draft records the terms that were agreed.";
            }

            int changed = Count("Changed");
            int missing = Count("MissingFromContract");
            int added = Count("AddedInContract");
            int incomparable = Count("NotComparable");

            List<string> parts = [];

            if (changed > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{changed} changed"));
            }

            if (missing > 0)
            {
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture, $"{missing} not carried into the draft"));
            }

            if (added > 0)
            {
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture, $"{added} added by the draft"));
            }

            if (incomparable > 0)
            {
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture, $"{incomparable} not comparable"));
            }

            return string.Join(", ", parts) + ".";
        }
    }

    public Task LoadAsync(
        Guid contractId,
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            ReconciliationResponse result = await _api
                .ReconcileContractVersionAsync(contractId, versionId, token)
                .ConfigureAwait(true);

            Reconciliation = result;

            Apply();

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsFaithful));
            OnPropertyChanged(nameof(DifferenceCount));
            OnPropertyChanged(nameof(Summary));
        }, cancellationToken);

    private int Count(string result) =>
        Reconciliation?.Lines.Count(x => x.Result == result) ?? 0;

    private void Apply()
    {
        IReadOnlyList<ReconciliationLineResponse> lines = Reconciliation?.Lines ?? [];

        ContractDetailViewModel.Replace(
            Lines,
            DifferencesOnly ? [.. lines.Where(x => x.Result != "Matched")] : [.. lines]);

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// The legal work queue: what has to be looked at, and by when.
/// </summary>
/// <remarks>
/// Counts, dates and lists. No risk score and no reading of what a clause means:
/// whether a difference matters is a legal judgement, and what a contract is worth
/// is M9's subject (ADR-0022).
/// </remarks>
public sealed class LegalCommandCenterViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private ContractCommandCenterResponse? _centre;
    private bool _loaded;

    public LegalCommandCenterViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ContractCommandCenterResponse? Centre
    {
        get => _centre;
        private set => Set(ref _centre, value);
    }

    public ObservableCollection<ContractSummaryResponse> AwaitingSignature { get; } = [];

    public ObservableCollection<ContractSummaryResponse> UnderReview { get; } = [];

    public ObservableCollection<ContractSummaryResponse> WithDifferences { get; } = [];

    public ObservableCollection<LegalDeadlineResponse> UpcomingDeadlines { get; } = [];

    public ObservableCollection<ObligationResponse> OverdueObligations { get; } = [];

    public ObservableCollection<ContractOptionResponse> OptionsPastDeadline { get; } = [];

    /// <summary>Negotiations whose terms are agreed and which nobody has papered.</summary>
    public ObservableCollection<DealWithoutContractResponse> Unpapered { get; } = [];

    public ObservableCollection<ContractTaskResponse> OverdueTasks { get; } = [];

    public override bool IsEmpty =>
        _loaded
        && AwaitingSignature.Count == 0
        && UnderReview.Count == 0
        && WithDifferences.Count == 0
        && UpcomingDeadlines.Count == 0
        && OverdueObligations.Count == 0
        && OptionsPastDeadline.Count == 0
        && Unpapered.Count == 0
        && OverdueTasks.Count == 0;

    /// <summary>
    /// The one line a legal desk reads first.
    /// </summary>
    /// <remarks>
    /// Every clause is a count of rows the server derived. Nothing here is a
    /// judgement, and "past due" appears rather than "breached" because those are
    /// different facts (ADR-0022).
    /// </remarks>
    public string Headline
    {
        get
        {
            if (Centre is not { } centre)
            {
                return string.Empty;
            }

            List<string> parts =
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{centre.AwaitingSignatureCount} awaiting signature"),
                string.Create(
                    CultureInfo.InvariantCulture, $"{centre.UnderReviewCount} under review"),
                string.Create(
                    CultureInfo.InvariantCulture, $"{centre.EffectiveCount} in force"),
            ];

            if (centre.OverdueObligations.Count > 0)
            {
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{centre.OverdueObligations.Count} obligation(s) past due"));
            }

            if (centre.TermsAgreedWithoutContract.Count > 0)
            {
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{centre.TermsAgreedWithoutContract.Count} agreed deal(s) not yet papered"));
            }

            return string.Join(", ", parts) + ".";
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            ContractCommandCenterResponse centre = await _api
                .GetLegalCommandCenterAsync(token)
                .ConfigureAwait(true);

            Centre = centre;

            ContractDetailViewModel.Replace(AwaitingSignature, centre.AwaitingSignature);
            ContractDetailViewModel.Replace(UnderReview, centre.UnderReview);
            ContractDetailViewModel.Replace(WithDifferences, centre.WithUnresolvedDifferences);
            ContractDetailViewModel.Replace(UpcomingDeadlines, centre.UpcomingDeadlines);
            ContractDetailViewModel.Replace(OverdueObligations, centre.OverdueObligations);
            ContractDetailViewModel.Replace(OptionsPastDeadline, centre.OptionsPastDeadline);
            ContractDetailViewModel.Replace(Unpapered, centre.TermsAgreedWithoutContract);
            ContractDetailViewModel.Replace(OverdueTasks, centre.OverdueTasks);

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Headline));
        }, cancellationToken);
}

/// <summary>
/// Rights recorded across the agency's contracts.
/// </summary>
/// <remarks>
/// A record of what instruments say was granted. It is not a chain of title, not a
/// clearance and not a finding that any grantor held what they purported to grant,
/// and the wording throughout says so (ADR-0022).
/// </remarks>
public sealed class RightsViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private bool _currentOnly = true;
    private Guid? _projectId;

    public RightsViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<RightsGrantResponse> Grants { get; } = [];

    /// <summary>Show only grants that have not been superseded or ended.</summary>
    public bool CurrentOnly
    {
        get => _currentOnly;
        set => Set(ref _currentOnly, value);
    }

    public Guid? ProjectId
    {
        get => _projectId;
        set => Set(ref _projectId, value);
    }

    public override bool IsEmpty => _loaded && Grants.Count == 0;

    /// <summary>How many listed grants are running today.</summary>
    public int Running => Grants.Count(x => x.IsCurrent);

    /// <summary>
    /// How many grants state a period this build could not structure.
    /// </summary>
    /// <remarks>
    /// Surfaced rather than hidden. A grant whose period is unstated does not
    /// silently become perpetual, and the number of them is worth a lawyer's
    /// attention (ADR-0022).
    /// </remarks>
    public int UnstatedPeriods => Grants.Count(x => x.PeriodKind == "Unstated");

    public Task LoadAsync(Guid? contractId = null, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<RightsGrantResponse> grants = await _api
                .ListRightsGrantsAsync(contractId, ProjectId, CurrentOnly, token)
                .ConfigureAwait(true);

            ContractDetailViewModel.Replace(Grants, grants);

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Running));
            OnPropertyChanged(nameof(UnstatedPeriods));
        }, cancellationToken);
}

/// <summary>
/// Obligations across the agency's contracts.
/// </summary>
/// <remarks>
/// Past due and breached are separate columns and never the same one. A date going
/// by is a fact the server derived; a breach is a determination somebody recorded
/// with a reason attached (ADR-0022).
/// </remarks>
public sealed class ObligationListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private bool _outstandingOnly = true;
    private bool _overdueOnly;
    private string? _status;

    public ObligationListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<ObligationResponse> Obligations { get; } = [];

    public bool OutstandingOnly
    {
        get => _outstandingOnly;
        set => Set(ref _outstandingOnly, value);
    }

    /// <summary>Show only obligations whose date has passed.</summary>
    public bool OverdueOnly
    {
        get => _overdueOnly;
        set => Set(ref _overdueOnly, value);
    }

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public override bool IsEmpty => _loaded && Obligations.Count == 0;

    /// <summary>How many listed obligations are past their date.</summary>
    public int PastDue => Obligations.Count(x => x.IsPastDue);

    /// <summary>How many have a breach recorded against them.</summary>
    /// <remarks>Deliberately a different number from <see cref="PastDue"/>.</remarks>
    public int Breached => Obligations.Count(x => x.Status == "Breached");

    /// <summary>
    /// How many have a due date this build could not work out.
    /// </summary>
    /// <remarks>
    /// The honest gap, shown rather than hidden: these are duties that exist and
    /// whose date nobody can compute yet, and they are exactly what a work queue
    /// would otherwise lose (ADR-0022).
    /// </remarks>
    public int Unresolved => Obligations.Count(x => x.DueOn is null);

    public Task LoadAsync(Guid? contractId = null, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<ObligationResponse> obligations = await _api
                .ListObligationsAsync(contractId, Status, OutstandingOnly, OverdueOnly, token)
                .ConfigureAwait(true);

            ContractDetailViewModel.Replace(Obligations, obligations);

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(PastDue));
            OnPropertyChanged(nameof(Breached));
            OnPropertyChanged(nameof(Unresolved));
        }, cancellationToken);
}

/// <summary>
/// Options across the agency's contracts.
/// </summary>
/// <remarks>
/// Past deadline and expired are separate, for the same reason past due and
/// breached are. An option sitting past its date and still Available is precisely
/// what a work queue exists to surface: nobody has dealt with it (ADR-0022).
/// </remarks>
public sealed class OptionListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private bool _exercisableOnly;
    private bool _pastDeadlineOnly;
    private string? _status = "Available";

    public OptionListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<ContractOptionResponse> Options { get; } = [];

    /// <summary>Show only options that could be exercised today.</summary>
    public bool ExercisableOnly
    {
        get => _exercisableOnly;
        set => Set(ref _exercisableOnly, value);
    }

    /// <summary>Show only options whose stated deadline has passed unresolved.</summary>
    public bool PastDeadlineOnly
    {
        get => _pastDeadlineOnly;
        set => Set(ref _pastDeadlineOnly, value);
    }

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public override bool IsEmpty => _loaded && Options.Count == 0;

    /// <summary>How many listed options could be exercised today.</summary>
    public int Exercisable => Options.Count(x => x.IsExercisable);

    /// <summary>How many are past their stated deadline and still unresolved.</summary>
    public int PastDeadline => Options.Count(x => x.IsPastDeadline);

    /// <summary>How many have a deadline this build could not work out.</summary>
    public int Unresolved => Options.Count(x => x.DeadlineOn is null);

    public Task LoadAsync(Guid? contractId = null, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<ContractOptionResponse> options = await _api
                .ListContractOptionsAsync(
                    contractId, Status, ExercisableOnly, PastDeadlineOnly, token)
                .ConfigureAwait(true);

            ContractDetailViewModel.Replace(Options, options);

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Exercisable));
            OnPropertyChanged(nameof(PastDeadline));
            OnPropertyChanged(nameof(Unresolved));
        }, cancellationToken);
}
