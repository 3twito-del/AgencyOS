using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Contracts.Deals;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// The negotiation list: every deal, and what each is waiting on.
/// </summary>
/// <remarks>
/// A dense table rather than a board by default, for the reason the M6 pipeline
/// gives: a board shows status and hides the rest, and the questions a negotiator
/// actually has - who owes whom an answer, what lapses this week - are ones a
/// board has nowhere to put.
/// </remarks>
public sealed class DealListViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status;
    private string? _kind;
    private bool _awaitingResponse;
    private bool _termsAgreedOnly;
    private string _search = string.Empty;
    private bool _openOnly = true;

    public DealListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<DealSummaryResponse> Deals { get; } = [];

    /// <summary>
    /// Restrict to one status. Absent by default, which shows live work.
    /// </summary>
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

    /// <summary>Show only negotiations with an offer awaiting an answer.</summary>
    public bool AwaitingResponse
    {
        get => _awaitingResponse;
        set => Set(ref _awaitingResponse, value);
    }

    /// <summary>Show only negotiations whose commercial terms are settled.</summary>
    public bool TermsAgreedOnly
    {
        get => _termsAgreedOnly;
        set => Set(ref _termsAgreedOnly, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    public override bool IsEmpty => _loaded && Deals.Count == 0;

    /// <summary>How many listed negotiations have an offer on the table.</summary>
    public int Awaiting => Deals.Count(x => x.HasOpenOffer);

    /// <summary>How many have agreed terms.</summary>
    /// <remarks>
    /// Agreed terms, not signed contracts. The count says the commercial
    /// conversation finished and nothing about whether anything was papered.
    /// </remarks>
    public int TermsAgreed => Deals.Count(x => x.Status == "TermsAgreed");

    /// <summary>Standing offers whose stated lapse is within a week.</summary>
    public int ExpiringSoon
    {
        get
        {
            DateTimeOffset horizon = DateTimeOffset.UtcNow.AddDays(7);

            return Deals.Count(x =>
                x.HasOpenOffer && x.OpenOfferExpiresAt is { } expiry && expiry <= horizon);
        }
    }

    /// <summary>
    /// Whether the list shows live work when no status is chosen.
    /// </summary>
    /// <remarks>
    /// The default Deals workspace asks the server for live negotiations rather
    /// than for one status. It used to ask for <c>Negotiating</c>, so a deal that
    /// reached <c>TermsAgreed</c> — agreed and waiting to be papered — vanished from
    /// the workspace named after it.
    /// </remarks>
    public bool OpenOnly
    {
        get => _openOnly;
        set => Set(ref _openOnly, value);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            // Either a status the operator chose, or live work. Never both: asking
            // for one status and for live work at the same time would answer a
            // question nobody asked, and an explicit choice is an explicit choice.
            bool openOnly = OpenOnly && string.IsNullOrWhiteSpace(Status);

            IReadOnlyList<DealSummaryResponse> deals = await _api
                .ListDealsAsync(
                    Status,
                    Kind,
                    ownerUserId: null,
                    AwaitingResponse,
                    TermsAgreedOnly,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    openOnly,
                    token)
                .ConfigureAwait(true);

            Deals.Clear();

            foreach (DealSummaryResponse deal in deals)
            {
                Deals.Add(deal);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Awaiting));
            OnPropertyChanged(nameof(TermsAgreed));
            OnPropertyChanged(nameof(ExpiringSoon));
        }, cancellationToken);
}

/// <summary>
/// One negotiation's working surface: the thread, the agreement, the tasks.
/// </summary>
public sealed class DealDetailViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private DealDetailResponse? _deal;
    private bool _loaded;

    public DealDetailViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public DealDetailResponse? Deal
    {
        get => _deal;
        private set => Set(ref _deal, value);
    }

    /// <summary>The negotiation thread, most recent first.</summary>
    public ObservableCollection<OfferResponse> Offers { get; } = [];

    public ObservableCollection<DealTaskResponse> Tasks { get; } = [];

    public ObservableCollection<DealHistoryEntryResponse> History { get; } = [];

    public override bool IsEmpty => _loaded && Deal is null;

    /// <summary>Whether the caller may read the agency's negotiating strategy.</summary>
    /// <remarks>
    /// Derived from the value being present rather than from a flag. The API
    /// returns the field absent, and absent is deliberately indistinguishable from
    /// empty, so this is true only when there is something to show.
    /// </remarks>
    public bool HasStrategy => !string.IsNullOrWhiteSpace(Deal?.StrategyNotes);

    /// <summary>
    /// Whether the caller can see what this negotiation pays.
    /// </summary>
    /// <remarks>
    /// Inferred from an offer carrying an economic term. Without
    /// <c>deals.economics.read</c> those rows are absent, so a screen that offers
    /// a comparison the server would refuse can simply not offer it.
    /// </remarks>
    public bool HasEconomics =>
        Offers.Any(offer => offer.Terms.Any(term => term.IsEconomic));

    /// <summary>The offer awaiting an answer, when there is one.</summary>
    public OfferResponse? OpenOffer => Deal?.OpenOffer;

    /// <summary>The agreement, when terms are agreed.</summary>
    public OfferResponse? AcceptedOffer => Deal?.AcceptedOffer;

    /// <summary>The offer before the current one, for a default comparison.</summary>
    public OfferResponse? PreviousOffer
    {
        get
        {
            OfferResponse? current = OpenOffer ?? AcceptedOffer ?? Offers.FirstOrDefault();

            return current is null
                ? null
                : Offers
                    .Where(x => x.Sequence < current.Sequence && x.Status != "Draft")
                    .OrderByDescending(x => x.Sequence)
                    .FirstOrDefault();
        }
    }

    /// <summary>Whether the negotiation can still take an offer.</summary>
    public bool AcceptsOffers => Deal?.Deal.Status is "Draft" or "Negotiating";

    /// <summary>
    /// A plain sentence about where the negotiation stands.
    /// </summary>
    /// <remarks>
    /// Says "terms agreed" and never "signed" or "closed". A screen that implied a
    /// contract existed would be a claim the system cannot substantiate.
    /// </remarks>
    public string Standing
    {
        get
        {
            if (Deal is not { } detail)
            {
                return string.Empty;
            }

            string offers = string.Create(
                CultureInfo.InvariantCulture,
                $"{detail.Deal.OfferCount} offer(s)");

            string state = detail.Deal.Status switch
            {
                "TermsAgreed" => "terms agreed, no contract recorded",
                "Negotiating" when detail.Deal.HasOpenOffer => "awaiting an answer",
                "Negotiating" => "in negotiation",
                "NoDeal" => "closed without agreement",
                "Cancelled" => "cancelled",
                _ => "being set up",
            };

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{detail.Deal.Kind} with {detail.Deal.CounterpartyDisplayName} - {offers}, {state}");
        }
    }

    public Task LoadAsync(Guid dealId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            DealDetailResponse detail = await _api
                .GetDealAsync(dealId, token)
                .ConfigureAwait(true);

            Deal = detail;

            Replace(Offers, [.. detail.Offers.OrderByDescending(x => x.Sequence)]);
            Replace(Tasks, detail.OpenTasks);

            IReadOnlyList<DealHistoryEntryResponse> history = await _api
                .GetDealHistoryAsync(dealId, token)
                .ConfigureAwait(true);

            Replace(History, history);

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasStrategy));
            OnPropertyChanged(nameof(HasEconomics));
            OnPropertyChanged(nameof(OpenOffer));
            OnPropertyChanged(nameof(AcceptedOffer));
            OnPropertyChanged(nameof(PreviousOffer));
            OnPropertyChanged(nameof(AcceptsOffers));
            OnPropertyChanged(nameof(Standing));
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
/// A side-by-side comparison of two offers.
/// </summary>
/// <remarks>
/// Answers "what changed". It carries no opinion about whether a change is
/// welcome: higher compensation is good for the client and a longer term usually
/// is not, and which is which depends on the term and the side (ADR-0021).
/// </remarks>
public sealed class OfferComparisonViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private OfferComparisonResponse? _comparison;
    private bool _loaded;
    private bool _materialOnly = true;

    public OfferComparisonViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<TermDifferenceResponse> Differences { get; } = [];

    public OfferComparisonResponse? Comparison
    {
        get => _comparison;
        private set => Set(ref _comparison, value);
    }

    /// <summary>Hide terms that did not move. On by default.</summary>
    public bool MaterialOnly
    {
        get => _materialOnly;
        set
        {
            if (Set(ref _materialOnly, value))
            {
                Render();
            }
        }
    }

    public override bool IsEmpty => _loaded && Differences.Count == 0;

    /// <summary>How many terms actually moved.</summary>
    public int ChangedCount =>
        Comparison?.Differences.Count(x => x.Change != "Unchanged") ?? 0;

    public Task LoadAsync(
        Guid dealId,
        Guid previousOfferId,
        Guid currentOfferId,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            Comparison = await _api
                .CompareOffersAsync(dealId, previousOfferId, currentOfferId, token)
                .ConfigureAwait(true);

            _loaded = true;

            Render();

            OnPropertyChanged(nameof(ChangedCount));
        }, cancellationToken);

    private void Render()
    {
        Differences.Clear();

        if (Comparison is not { } comparison)
        {
            return;
        }

        IEnumerable<TermDifferenceResponse> rows = MaterialOnly
            ? comparison.Differences.Where(x => x.Change != "Unchanged")
            : comparison.Differences;

        foreach (TermDifferenceResponse row in rows)
        {
            Differences.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// The board view: negotiations grouped by where they stand.
/// </summary>
public sealed class DealPipelineViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;

    public DealPipelineViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<DealPipelineColumnResponse> Columns { get; } = [];

    public override bool IsEmpty => _loaded && Columns.All(x => x.Deals.Count == 0);

    public int DealCount => Columns.Sum(x => x.Deals.Count);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<DealPipelineColumnResponse> columns = await _api
                .GetDealPipelineAsync(ownerUserId: null, token)
                .ConfigureAwait(true);

            Columns.Clear();

            foreach (DealPipelineColumnResponse column in columns)
            {
                Columns.Add(column);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(DealCount));
        }, cancellationToken);
}

/// <summary>
/// The supported commercial terms, so a term editor need not hard-code them.
/// </summary>
/// <remarks>
/// Loaded from the server's catalog rather than duplicated here. A client that
/// carried its own copy would drift from the vocabulary the server validates
/// against, and the user would discover it as a refusal (ADR-0021).
/// </remarks>
public sealed class DealTermCatalogViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;

    public DealTermCatalogViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<DealTermDefinitionResponse> Terms { get; } = [];

    public override bool IsEmpty => _loaded && Terms.Count == 0;

    /// <summary>The terms whose values require <c>deals.economics.read</c>.</summary>
    public IReadOnlyList<DealTermDefinitionResponse> Economic =>
        [.. Terms.Where(x => x.IsEconomic)];

    /// <summary>The definition for a code, or null when this build has none.</summary>
    public DealTermDefinitionResponse? Find(string code) =>
        Terms.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.Ordinal));

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<DealTermDefinitionResponse> terms = await _api
                .ListDealTermsAsync(token)
                .ConfigureAwait(true);

            Terms.Clear();

            foreach (DealTermDefinitionResponse term in terms)
            {
                Terms.Add(term);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Economic));
        }, cancellationToken);
}
