using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Client.Presentation;
using AgencyOS.Contracts.Finance;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// Renders money the way the rest of M9 reasons about it.
/// </summary>
/// <remarks>
/// <para>
/// Every figure carries its currency, always, and two figures in different
/// currencies are never added. A screen that showed a single "total outstanding"
/// over a mixed book would be showing a number that does not exist: AgencyOS holds
/// no exchange rate, and inventing one at the presentation layer would be the
/// worst place of all to invent it (ADR-0023).
/// </para>
/// <para>
/// The formatting is deliberately not <c>ToString("C")</c>. That would render a
/// euro amount with a dollar sign on a US-locale machine, which is exactly the
/// confusion the currency code exists to prevent.
/// </para>
/// </remarks>
public static class MoneyFormatting
{
    /// <summary>Formats an amount with its currency code beside it.</summary>
    public static string Format(MoneyResponse? money) =>
        money is null
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{money.Amount:N2} {money.Currency}");

    /// <summary>Formats a nullable amount, saying so when there is not one.</summary>
    /// <remarks>
    /// "Not yet known" rather than a zero. A contingent bonus nobody can value is
    /// not worth nothing; it is worth an amount nobody knows, and showing zero
    /// would put a false figure into the reader's head (ADR-0023).
    /// </remarks>
    public static string FormatOrUnknown(MoneyResponse? money) =>
        money is null ? "Not yet known" : Format(money);

    /// <summary>Formats a per-currency breakdown as separate figures.</summary>
    public static string FormatTotals(IEnumerable<CurrencyTotalResponse>? totals)
    {
        if (totals is null)
        {
            return string.Empty;
        }

        string[] parts = [.. totals.Select(t => Format(t.Total))];

        return parts.Length == 0 ? "Nothing outstanding" : string.Join("   ", parts);
    }

    /// <summary>Groups amounts by currency without ever adding two of them.</summary>
    public static IReadOnlyList<CurrencyTotalResponse> GroupByCurrency(
        IEnumerable<MoneyResponse> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        return
        [
            .. amounts
                .GroupBy(x => x.Currency, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new CurrencyTotalResponse(
                    g.Key,
                    new MoneyResponse(g.Sum(x => x.Amount), g.Key),
                    g.Count())),
        ];
    }
}

/// <summary>
/// The receivable list: what the agency expects to collect, and what has arrived.
/// </summary>
/// <remarks>
/// A dense table with the arithmetic on every row - original, allocated, adjusted,
/// outstanding - rather than a status chip. A finance desk asks "how much of this
/// is left", and a chip saying "Partially paid" answers a different question.
/// </remarks>
public sealed class ReceivableListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status;
    private string? _beneficiary;
    private string? _currency;
    private bool _overdueOnly;
    private bool _unreconciledOnly;
    private string _search = string.Empty;

    public ReceivableListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<ReceivableResponse> Receivables { get; } = [];

    /// <summary>Open, PartiallyPaid, Paid, Cancelled or WrittenOff.</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <summary>Client or Agency. Whose money it becomes when it arrives.</summary>
    public string? Beneficiary
    {
        get => _beneficiary;
        set => Set(ref _beneficiary, value);
    }

    /// <summary>One currency at a time, because two are never added.</summary>
    public string? Currency
    {
        get => _currency;
        set => Set(ref _currency, value);
    }

    /// <summary>
    /// Show only rows past a resolvable date with something still owed.
    /// </summary>
    /// <remarks>
    /// A receivable with no due date never appears here. The contract did not say
    /// when, and a system that treated silence as "immediately" would manufacture
    /// an arrears position nobody agreed to (ADR-0023).
    /// </remarks>
    public bool OverdueOnly
    {
        get => _overdueOnly;
        set => Set(ref _overdueOnly, value);
    }

    /// <summary>Show only rows whose arithmetic does not yet explain itself.</summary>
    public bool UnreconciledOnly
    {
        get => _unreconciledOnly;
        set => Set(ref _unreconciledOnly, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    public override bool IsEmpty => _loaded && Receivables.Count == 0;

    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>How many listed rows are past a resolvable due date.</summary>
    public int Overdue => Receivables.Count(x => x.IsOverdue);

    /// <summary>How many are held for a client rather than earned by the agency.</summary>
    public int ClientMoney =>
        Receivables.Count(x => string.Equals(x.Beneficiary, "Client", StringComparison.Ordinal));

    /// <summary>
    /// What is outstanding, per currency.
    /// </summary>
    /// <remarks>
    /// A list rather than a figure. There is no honest single number here unless
    /// the whole list happens to be in one currency, and the surface should not
    /// pretend otherwise (ADR-0023).
    /// </remarks>
    public IReadOnlyList<CurrencyTotalResponse> OutstandingByCurrency =>
        MoneyFormatting.GroupByCurrency(Receivables.Select(x => x.Outstanding));

    public string OutstandingSummary => MoneyFormatting.FormatTotals(OutstandingByCurrency);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<ReceivableResponse> receivables = await _api
                .ListReceivablesAsync(
                    Status,
                    contractId: null,
                    payerPartyId: null,
                    clientPersonId: null,
                    Beneficiary,
                    OverdueOnly,
                    UnreconciledOnly,
                    dueAfter: null,
                    dueBefore: null,
                    Currency,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    limit: null,
                    token)
                .ConfigureAwait(true);

            Receivables.Clear();

            foreach (ReceivableResponse receivable in receivables)
            {
                Receivables.Add(receivable);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Overdue));
            OnPropertyChanged(nameof(ClientMoney));
            OnPropertyChanged(nameof(OutstandingByCurrency));
            OnPropertyChanged(nameof(OutstandingSummary));
        }, cancellationToken);
}

/// <summary>
/// The invoice list.
/// </summary>
/// <remarks>
/// Every verb on this screen is <em>record</em>, <em>issue</em> or <em>void</em>.
/// None of them is <em>send</em>: AgencyOS holds no invoice document and has no
/// transport, so a button offering to send one would be describing something the
/// build cannot do (ADR-0023).
/// </remarks>
public sealed class InvoiceListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _status;
    private string? _currency;
    private bool _overdueOnly;
    private string _search = string.Empty;

    public InvoiceListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<InvoiceResponse> Invoices { get; } = [];

    /// <summary>Draft, Issued or Void. Nothing here says anything about payment.</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string? Currency
    {
        get => _currency;
        set => Set(ref _currency, value);
    }

    public bool OverdueOnly
    {
        get => _overdueOnly;
        set => Set(ref _overdueOnly, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    public override bool IsEmpty => _loaded && Invoices.Count == 0;

    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    public int Issued =>
        Invoices.Count(x => string.Equals(x.Status, "Issued", StringComparison.Ordinal));

    public int Overdue => Invoices.Count(x => x.IsOverdue);

    /// <summary>
    /// Whether AgencyOS holds any of these documents.
    /// </summary>
    /// <remarks>
    /// Always false, and shown rather than hidden. An operator who assumed the
    /// system had the PDF would eventually go looking for it during an argument
    /// about what was billed.
    /// </remarks>
    public bool HoldsAnyDocument => Invoices.Any(x => x.HoldsDocument);

    public IReadOnlyList<CurrencyTotalResponse> OutstandingByCurrency =>
        MoneyFormatting.GroupByCurrency(Invoices.Select(x => x.Outstanding));

    public string OutstandingSummary => MoneyFormatting.FormatTotals(OutstandingByCurrency);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<InvoiceResponse> invoices = await _api
                .ListInvoicesAsync(
                    Status,
                    contractId: null,
                    debtorPartyId: null,
                    OverdueOnly,
                    dueAfter: null,
                    dueBefore: null,
                    Currency,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    limit: null,
                    token)
                .ConfigureAwait(true);

            Invoices.Clear();

            foreach (InvoiceResponse invoice in invoices)
            {
                Invoices.Add(invoice);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Issued));
            OnPropertyChanged(nameof(Overdue));
            OnPropertyChanged(nameof(HoldsAnyDocument));
            OnPropertyChanged(nameof(OutstandingByCurrency));
            OnPropertyChanged(nameof(OutstandingSummary));
        }, cancellationToken);
}

/// <summary>
/// The payment list: money that actually moved.
/// </summary>
/// <remarks>
/// The column that matters is <em>unapplied</em>. Cash sitting against no
/// receivable is the finance desk's real work queue, and a list that showed only
/// the amount received would hide it.
/// </remarks>
public sealed class PaymentListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _direction;
    private string? _status;
    private string? _currency;
    private bool _unappliedOnly;
    private string _search = string.Empty;

    public PaymentListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<PaymentResponse> Payments { get; } = [];

    public string? Direction
    {
        get => _direction;
        set => Set(ref _direction, value);
    }

    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string? Currency
    {
        get => _currency;
        set => Set(ref _currency, value);
    }

    /// <summary>Show only payments with cash still to apply.</summary>
    public bool UnappliedOnly
    {
        get => _unappliedOnly;
        set => Set(ref _unappliedOnly, value);
    }

    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    public override bool IsEmpty => _loaded && Payments.Count == 0;

    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>How many listed payments still have money nobody has explained.</summary>
    public int WithUnappliedCash => Payments.Count(x => x.Unapplied.Amount > 0m);

    public int Reversed =>
        Payments.Count(x => string.Equals(x.Status, "Reversed", StringComparison.Ordinal));

    public IReadOnlyList<CurrencyTotalResponse> UnappliedByCurrency =>
        MoneyFormatting.GroupByCurrency(
            Payments.Where(x => x.Unapplied.Amount > 0m).Select(x => x.Unapplied));

    public string UnappliedSummary => MoneyFormatting.FormatTotals(UnappliedByCurrency);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<PaymentResponse> payments = await _api
                .ListPaymentsAsync(
                    Direction,
                    Status,
                    payerPartyId: null,
                    UnappliedOnly,
                    recordedAfter: null,
                    recordedBefore: null,
                    Currency,
                    string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                    limit: null,
                    token)
                .ConfigureAwait(true);

            Payments.Clear();

            foreach (PaymentResponse payment in payments)
            {
                Payments.Add(payment);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(WithUnappliedCash));
            OnPropertyChanged(nameof(Reversed));
            OnPropertyChanged(nameof(UnappliedByCurrency));
            OnPropertyChanged(nameof(UnappliedSummary));
        }, cancellationToken);
}

/// <summary>One line of a payment being drafted.</summary>
/// <remarks>
/// The receivable is chosen, never inferred. There is no "apply to the oldest" and
/// no "apply to the closest match": both would be the system deciding what a payer
/// meant, and getting it wrong quietly (ADR-0023).
/// </remarks>
public sealed class PaymentAllocationDraft : ViewModelBase
{
    private Guid _receivableId;
    private decimal _amount;
    private string _description = string.Empty;

    public Guid ReceivableId
    {
        get => _receivableId;
        set => Set(ref _receivableId, value);
    }

    public decimal Amount
    {
        get => _amount;
        set => Set(ref _amount, value);
    }

    /// <summary>What the row is, for the person reading the dialog.</summary>
    public string Description
    {
        get => _description;
        set => Set(ref _description, value ?? string.Empty);
    }

    public override bool IsEmpty => _receivableId == Guid.Empty;
}

/// <summary>
/// Records that money moved, and says what is left over before anything is written.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of this screen is the third figure. A dialog that took an
/// amount and a receivable and said "saved" would let somebody record eighty
/// thousand against a hundred-thousand receivable and walk away believing the
/// account was settled. This one shows received, allocated and unapplied together,
/// before the commit, so the residual is a decision rather than a discovery
/// (ADR-0023).
/// </para>
/// <para>
/// The arithmetic here is a <strong>preview</strong>. The server recomputes every
/// figure in the transaction that writes it, and its answer wins: this is what the
/// operator is about to ask for, not what will have happened.
/// </para>
/// </remarks>
public sealed class RecordPaymentViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private decimal _amount;
    private string _currency = string.Empty;
    private string _direction = "Incoming";
    private string _method = "BankTransfer";
    private DateOnly _receivedOn = DateOnly.FromDateTime(DateTime.UtcNow);
    private Guid? _payerPartyId;
    private string? _payerName;
    private string? _externalReference;
    private string? _notes;
    private RecordPaymentResponse? _result;

    public RecordPaymentViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;

        Allocations.CollectionChanged += (_, _) => RecomputePreview();
    }

    public ObservableCollection<PaymentAllocationDraft> Allocations { get; } = [];

    public decimal Amount
    {
        get => _amount;
        set
        {
            if (Set(ref _amount, value))
            {
                RecomputePreview();
            }
        }
    }

    /// <summary>The ISO 4217 code. Required: there is no default currency.</summary>
    public string Currency
    {
        get => _currency;
        set => Set(ref _currency, (value ?? string.Empty).Trim().ToUpperInvariant());
    }

    public string Direction
    {
        get => _direction;
        set => Set(ref _direction, value ?? "Incoming");
    }

    public string Method
    {
        get => _method;
        set => Set(ref _method, value ?? "BankTransfer");
    }

    /// <summary>The day the money moved, as reported. Freely backdated.</summary>
    public DateOnly ReceivedOn
    {
        get => _receivedOn;
        set => Set(ref _receivedOn, value);
    }

    public Guid? PayerPartyId
    {
        get => _payerPartyId;
        set => Set(ref _payerPartyId, value);
    }

    /// <summary>Who paid, when they are not a party AgencyOS knows.</summary>
    public string? PayerName
    {
        get => _payerName;
        set => Set(ref _payerName, value);
    }

    /// <summary>
    /// The payer's or bank's own identifier.
    /// </summary>
    /// <remarks>
    /// Not assumed unique. Two genuinely different payments can carry the same
    /// remittance text, so a match is reported back as a possible duplicate and
    /// never used to refuse the record.
    /// </remarks>
    public string? ExternalReference
    {
        get => _externalReference;
        set => Set(ref _externalReference, value);
    }

    public string? Notes
    {
        get => _notes;
        set => Set(ref _notes, value);
    }

    /// <summary>What the server said, once the payment was recorded.</summary>
    public RecordPaymentResponse? Result
    {
        get => _result;
        private set
        {
            if (Set(ref _result, value))
            {
                OnPropertyChanged(nameof(HasPossibleDuplicates));
                OnPropertyChanged(nameof(RecordedUnapplied));
            }
        }
    }

    public override bool IsEmpty => Allocations.Count == 0;

    /// <summary>What the draft allocations come to. A preview, not a commitment.</summary>
    public decimal AllocatedPreview => Allocations.Sum(x => x.Amount);

    /// <summary>What would be left unapplied. Never silently assigned to anything.</summary>
    public decimal UnappliedPreview => _amount - AllocatedPreview;

    /// <summary>Whether the draft allocates more than arrived.</summary>
    public bool IsOverAllocated => AllocatedPreview > _amount;

    public string AmountDisplay =>
        MoneyFormatting.Format(new MoneyResponse(_amount, _currency));

    public string AllocatedDisplay =>
        MoneyFormatting.Format(new MoneyResponse(AllocatedPreview, _currency));

    public string UnappliedDisplay =>
        MoneyFormatting.Format(new MoneyResponse(UnappliedPreview, _currency));

    /// <summary>
    /// The sentence the operator reads before committing.
    /// </summary>
    /// <remarks>
    /// Deliberately explicit that leftover cash stays leftover. "Unapplied" on its
    /// own reads like a formatting state; saying what happens to it makes it a
    /// choice somebody made.
    /// </remarks>
    public string PreviewSummary =>
        IsOverAllocated
            ? $"Allocations come to {AllocatedDisplay}, which is more than the {AmountDisplay} received."
            : UnappliedPreview == 0m
                ? $"All {AmountDisplay} is allocated."
                : $"{AllocatedDisplay} of {AmountDisplay} allocated. "
                    + $"{UnappliedDisplay} stays unapplied until somebody says what it is for.";

    /// <summary>Whether the form is complete enough to send.</summary>
    public bool CanRecord =>
        _amount > 0m
        && _currency.Length == 3
        && !IsOverAllocated
        && Allocations.All(x => x.ReceivableId != Guid.Empty && x.Amount > 0m);

    /// <summary>Payments already carrying the same external reference.</summary>
    public bool HasPossibleDuplicates => _result is { PossibleDuplicates.Count: > 0 };

    /// <summary>What the server actually left unapplied.</summary>
    public string RecordedUnapplied =>
        _result is null ? string.Empty : MoneyFormatting.Format(_result.Unapplied);

    public PaymentAllocationDraft AddAllocation()
    {
        PaymentAllocationDraft draft = new();
        draft.PropertyChanged += (_, _) => RecomputePreview();
        Allocations.Add(draft);

        return draft;
    }

    public void RemoveAllocation(PaymentAllocationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Allocations.Remove(draft);
    }

    /// <summary>
    /// Records the payment.
    /// </summary>
    /// <remarks>
    /// The idempotency key is minted by the caller and passed through unchanged, so
    /// a retry after a lost response is recognizable to the server as the same
    /// command rather than a second payment.
    /// </remarks>
    public Task RecordAsync(string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            RecordPaymentRequest request = new(
                Direction,
                new MoneyRequest(_amount, _currency),
                ReceivedOn,
                Method,
                PayerPartyId,
                PayerName,
                PayeePartyId: null,
                PayeeName: null,
                ExternalReference,
                SourceSystem: null,
                Notes,
                [
                    .. Allocations.Select(x => new AllocationRequest(
                        x.ReceivableId, new MoneyRequest(x.Amount, _currency))),
                ]);

            Result = await _api.RecordPaymentAsync(request, idempotencyKey, token).ConfigureAwait(true);
        }, cancellationToken);

    private void RecomputePreview()
    {
        OnPropertyChanged(nameof(AllocatedPreview));
        OnPropertyChanged(nameof(UnappliedPreview));
        OnPropertyChanged(nameof(IsOverAllocated));
        OnPropertyChanged(nameof(AmountDisplay));
        OnPropertyChanged(nameof(AllocatedDisplay));
        OnPropertyChanged(nameof(UnappliedDisplay));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(CanRecord));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// What the agency is entitled to, and what it has actually earned.
/// </summary>
/// <remarks>
/// Three columns that are never collapsed: entitled, collected, outstanding. An
/// agency entitled to a hundred thousand against a contract that has paid four
/// hundred of a million has collected forty, and a screen that showed only the
/// first number would be reporting revenue that has not arrived (ADR-0023).
/// </remarks>
public sealed class CommissionListViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private Guid? _clientPersonId;
    private string? _status;
    private string? _currency;
    private bool _outstandingOnly;

    public CommissionListViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<CommissionEntitlementResponse> Commissions { get; } = [];

    public ObservableCollection<CommissionRuleResponse> Rules { get; } = [];

    public Guid? ClientPersonId
    {
        get => _clientPersonId;
        set => Set(ref _clientPersonId, value);
    }

    /// <summary>Calculated, Superseded or Cancelled.</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string? Currency
    {
        get => _currency;
        set => Set(ref _currency, value);
    }

    /// <summary>Show only entitlements with something still to collect.</summary>
    public bool OutstandingOnly
    {
        get => _outstandingOnly;
        set => Set(ref _outstandingOnly, value);
    }

    public override bool IsEmpty => _loaded && Commissions.Count == 0;

    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>What has been earned, per currency.</summary>
    public IReadOnlyList<CurrencyTotalResponse> CollectedByCurrency =>
        MoneyFormatting.GroupByCurrency(Commissions.Select(x => x.Collected));

    /// <summary>What is entitled but not yet collected, per currency.</summary>
    public IReadOnlyList<CurrencyTotalResponse> OutstandingByCurrency =>
        MoneyFormatting.GroupByCurrency(Commissions.Select(x => x.Outstanding));

    public string CollectedSummary => MoneyFormatting.FormatTotals(CollectedByCurrency);

    public string OutstandingSummary => MoneyFormatting.FormatTotals(OutstandingByCurrency);

    /// <summary>How many rules are in force today.</summary>
    public int RulesInForce => Rules.Count(x => x.IsInForceToday);

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<CommissionEntitlementResponse> commissions = await _api
                .ListCommissionsAsync(
                    ClientPersonId,
                    contractId: null,
                    representationId: null,
                    Status,
                    OutstandingOnly,
                    Currency,
                    limit: null,
                    token)
                .ConfigureAwait(true);

            IReadOnlyList<CommissionRuleResponse> rules = await _api
                .ListCommissionRulesAsync(ClientPersonId, contractId: null, token)
                .ConfigureAwait(true);

            Commissions.Clear();

            foreach (CommissionEntitlementResponse commission in commissions)
            {
                Commissions.Add(commission);
            }

            Rules.Clear();

            foreach (CommissionRuleResponse rule in rules)
            {
                Rules.Add(rule);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CollectedByCurrency));
            OnPropertyChanged(nameof(OutstandingByCurrency));
            OnPropertyChanged(nameof(CollectedSummary));
            OnPropertyChanged(nameof(OutstandingSummary));
            OnPropertyChanged(nameof(RulesInForce));
        }, cancellationToken);
}

/// <summary>
/// The ledger: accounts, balances and the entries behind them.
/// </summary>
/// <remarks>
/// Balances are shown per currency and per account, exactly as the server computes
/// them from posted lines. Nothing here is a stored total, so nothing here can
/// disagree with the entries beneath it (ADR-0023).
/// </remarks>
public sealed class LedgerViewModel : ViewModelBase, IAuthoritativePopulation
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;
    private string? _currency;
    private string? _status;
    private string? _source;

    public LedgerViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<AccountBalanceResponse> Balances { get; } = [];

    public ObservableCollection<JournalEntryResponse> Entries { get; } = [];

    public string? Currency
    {
        get => _currency;
        set => Set(ref _currency, value);
    }

    /// <summary>Draft, Posted or Reversed.</summary>
    public string? Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    /// <summary>Which act wrote the entry, or ManualAdjustment for one somebody typed.</summary>
    public string? Source
    {
        get => _source;
        set => Set(ref _source, value);
    }

    public override bool IsEmpty => _loaded && Entries.Count == 0;

    /// <summary>Whether a load has ever completed. See <see cref="IAuthoritativePopulation"/>.</summary>
    public bool HasLoaded => _loaded;

    /// <summary>
    /// Whether every listed entry balances.
    /// </summary>
    /// <remarks>
    /// Shown, not assumed. The database refuses an unbalanced entry through a
    /// deferred constraint trigger, so this should always be true - and a screen
    /// that displays it is a screen where a false answer would be noticed.
    /// </remarks>
    public bool AllBalanced => Entries.All(x => x.IsBalanced);

    /// <summary>Entries somebody wrote by hand rather than the system deriving.</summary>
    public int ManualEntries =>
        Entries.Count(x => string.Equals(x.Source, "ManualAdjustment", StringComparison.Ordinal));

    public int Reversed =>
        Entries.Count(x => string.Equals(x.Status, "Reversed", StringComparison.Ordinal));

    /// <summary>The currencies the ledger actually holds.</summary>
    public IReadOnlyList<string> Currencies =>
        [.. Balances.Select(x => x.Currency).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)];

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<AccountBalanceResponse> balances = await _api
                .GetLedgerBalancesAsync(Currency, token)
                .ConfigureAwait(true);

            IReadOnlyList<JournalEntryResponse> entries = await _api
                .ListJournalEntriesAsync(
                    Status,
                    Source,
                    accountId: null,
                    postedAfter: null,
                    postedBefore: null,
                    Currency,
                    limit: null,
                    token)
                .ConfigureAwait(true);

            Balances.Clear();

            foreach (AccountBalanceResponse balance in balances)
            {
                Balances.Add(balance);
            }

            Entries.Clear();

            foreach (JournalEntryResponse entry in entries)
            {
                Entries.Add(entry);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(AllBalanced));
            OnPropertyChanged(nameof(ManualEntries));
            OnPropertyChanged(nameof(Reversed));
            OnPropertyChanged(nameof(Currencies));
        }, cancellationToken);
}

/// <summary>
/// What arrived against what was expected, for one receivable.
/// </summary>
/// <remarks>
/// The screen states the arithmetic and stops. Whether a shortfall is withholding,
/// a bank charge, a dispute or a mistake is something a person finds out, and a
/// surface that guessed would produce a reconciled receivable and a wrong record
/// nobody would look at again (ADR-0023).
/// </remarks>
public sealed class ReceivableReconciliationViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private ReceivableReconciliationResponse? _reconciliation;

    public ReceivableReconciliationViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ReceivableReconciliationResponse? Reconciliation
    {
        get => _reconciliation;
        private set
        {
            if (Set(ref _reconciliation, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(Outcome));
                OnPropertyChanged(nameof(Explanation));
                OnPropertyChanged(nameof(VarianceDisplay));
                OnPropertyChanged(nameof(IsReconciled));
                OnPropertyChanged(nameof(HasUnexplainedVariance));
            }
        }
    }

    public override bool IsEmpty => _reconciliation is null;

    /// <summary>NotStarted, Reconciled, Shortfall or Excess.</summary>
    public string Outcome => _reconciliation?.Outcome ?? string.Empty;

    public string Explanation => _reconciliation?.Explanation ?? string.Empty;

    public string VarianceDisplay => MoneyFormatting.Format(_reconciliation?.Variance);

    public bool IsReconciled =>
        string.Equals(Outcome, "Reconciled", StringComparison.Ordinal);

    /// <summary>
    /// Whether there is a gap nobody has recorded a reason for.
    /// </summary>
    /// <remarks>
    /// The honest state, and one the UI leaves visible rather than resolving. A gap
    /// with no adjustment against it is exactly that: unexplained. Attributing it
    /// to tax would produce a reconciled row and a fabricated tax fact (ADR-0023).
    /// </remarks>
    public bool HasUnexplainedVariance =>
        _reconciliation is { } model
        && model.Variance.Amount != 0m
        && model.Adjustments.Count == 0;

    public Task LoadAsync(Guid receivableId, CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            Reconciliation = await _api
                .ReconcileReceivableAsync(receivableId, token)
                .ConfigureAwait(true);
        }, cancellationToken);
}

/// <summary>
/// What the finance desk has to look at.
/// </summary>
/// <remarks>
/// Counts, dates and real rows. There is no cash forecast, no revenue prediction,
/// no agency valuation and no deal-quality score anywhere on it: each would be a
/// claim about the future, and M9 records what happened (ADR-0023).
/// </remarks>
public sealed class FinanceCommandCenterViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private FinanceCommandCenterResponse? _model;

    public FinanceCommandCenterViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public FinanceCommandCenterResponse? Model
    {
        get => _model;
        private set
        {
            if (Set(ref _model, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(OverdueCount));
                OnPropertyChanged(nameof(UnappliedCount));
                OnPropertyChanged(nameof(OutstandingSummary));
                OnPropertyChanged(nameof(UnappliedSummary));
                OnPropertyChanged(nameof(CommissionOutstandingSummary));
                OnPropertyChanged(nameof(ShowsCommission));
                OnPropertyChanged(nameof(UnexplainedVarianceCount));
                OnPropertyChanged(nameof(UnbilledCount));
            }
        }
    }

    public override bool IsEmpty =>
        _model is null
        || (_model.Overdue.Count == 0
            && _model.DueSoon.Count == 0
            && _model.UnappliedPayments.Count == 0
            && _model.UnreconciledVariances.Count == 0
            && _model.UncollectedCommission.Count == 0
            && _model.UnbilledObligations.Count == 0
            && _model.OverdueTasks.Count == 0);

    public int OverdueCount => _model?.OverdueCount ?? 0;

    public int UnappliedCount => _model?.UnappliedCount ?? 0;

    public int UnexplainedVarianceCount => _model?.UnreconciledVariances.Count ?? 0;

    /// <summary>Quantified obligations with nothing raised against them yet.</summary>
    public string OutstandingSummary =>
        MoneyFormatting.FormatTotals(_model?.OutstandingByCurrency);

    public int UnbilledCount => _model?.UnbilledObligations.Count ?? 0;

    public string UnappliedSummary => MoneyFormatting.FormatTotals(_model?.UnappliedByCurrency);

    public string CommissionOutstandingSummary =>
        MoneyFormatting.FormatTotals(_model?.CommissionOutstandingByCurrency);

    /// <summary>
    /// Whether the commission section is present.
    /// </summary>
    /// <remarks>
    /// A reader without <c>finance.commissions.read</c> gets the rest of the queue
    /// with this section absent rather than a refusal for the whole page - the one
    /// place M9 removes rather than refuses, and it removes a whole section rather
    /// than rows inside an arithmetic (ADR-0023).
    /// </remarks>
    public bool ShowsCommission => _model is { UncollectedCommission.Count: > 0 };

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            Model = await _api.GetFinanceCommandCenterAsync(token).ConfigureAwait(true);
        }, cancellationToken);
}

/// <summary>
/// The financial history of a contract or a receivable.
/// </summary>
/// <remarks>
/// One business act, one entry, however many rows it wrote. Never the raw audit
/// trail, which answers a security question in a security vocabulary and would
/// read to a finance operator as noise (ADR-0012, ADR-0023).
/// </remarks>
public sealed class FinanceHistoryViewModel : ViewModelBase
{
    private readonly IAgencyOsApi _api;

    private bool _loaded;

    public FinanceHistoryViewModel(IAgencyOsApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public ObservableCollection<FinanceHistoryEntryResponse> Entries { get; } = [];

    public override bool IsEmpty => _loaded && Entries.Count == 0;

    public Task LoadAsync(
        Guid? contractId = null,
        Guid? receivableId = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            IReadOnlyList<FinanceHistoryEntryResponse> entries = await _api
                .GetFinanceHistoryAsync(contractId, receivableId, token)
                .ConfigureAwait(true);

            Entries.Clear();

            foreach (FinanceHistoryEntryResponse entry in entries)
            {
                Entries.Add(entry);
            }

            _loaded = true;

            OnPropertyChanged(nameof(IsEmpty));
        }, cancellationToken);
}
