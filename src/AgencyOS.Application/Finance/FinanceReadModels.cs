using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Finance;

/// <summary>
/// An amount and the currency it is in.
/// </summary>
/// <remarks>
/// Every monetary value on every read model is one of these. There is no bare
/// decimal anywhere in the finance projections, because a figure that travels
/// without its currency is one somebody will assume is dollars (ADR-0023).
/// </remarks>
public sealed record MoneyModel(decimal Amount, string Currency)
{
    /// <summary>Nothing, in a stated currency.</summary>
    public static MoneyModel Zero(string currency) => new(0m, currency);
}

/// <summary>A sum an operative contract says is payable.</summary>
/// <param name="AmountKind">Fixed, Formula, Contingent or Unknown.</param>
/// <param name="Amount">
/// What is due, when that is known. <c>null</c> is a real answer: a contingent
/// bonus and an unvalued participation are obligations with no figure.
/// </param>
/// <param name="DueOn">The due date, once it is a date anybody can work out.</param>
/// <param name="DueUnresolvedReason">Why it is not a date, when it is not.</param>
/// <param name="IsQuantified">Whether a collectible figure exists at all.</param>
public sealed record MonetaryObligationModel(
    MonetaryObligationId Id,
    ContractId ContractId,
    string ContractTitle,
    ContractVersionId ContractVersionId,
    ObligationId? SourceObligationId,
    ContractTermCode? SourceTermCode,
    Guid PayerPartyId,
    string PayerDisplayName,
    Guid PayeePartyId,
    string PayeeDisplayName,
    ObligationCategory Category,
    ObligationAmountKind AmountKind,
    MoneyModel? Amount,
    int? Quantity,
    MoneyModel? UnitAmount,
    string? Condition,
    DateOnly? DueOn,
    string? DueUnresolvedReason,
    string? DueDescription,
    MonetaryObligationStatus Status,
    bool IsQuantified,
    bool HasReceivable,
    string? Description,
    string? Notes,
    int Version);

/// <summary>A sum the agency expects to collect.</summary>
/// <param name="Beneficiary">
/// Client or Agency. The single most consequential field: money collected against
/// a client receivable is held and owed onward, not earned.
/// </param>
/// <param name="Outstanding">What is still owed. Derived from allocations and adjustments.</param>
/// <param name="Status">Where it stands. Derived, never set.</param>
/// <param name="IsOverdue">Past a resolvable date with something owed. Derived.</param>
public sealed record ReceivableModel(
    ReceivableId Id,
    MonetaryObligationId MonetaryObligationId,
    ContractId ContractId,
    string ContractTitle,
    Guid PayerPartyId,
    string PayerDisplayName,
    ReceivableBeneficiary Beneficiary,
    Guid? ClientPersonId,
    string? ClientDisplayName,
    MoneyModel OriginalAmount,
    MoneyModel Allocated,
    MoneyModel Adjusted,
    MoneyModel Outstanding,
    DateOnly? DueOn,
    ReceivableStatus Status,
    bool IsOverdue,
    string? Reference,
    string? ClosureReason,
    string? Notes,
    DateTimeOffset CreatedAt,
    int Version);

/// <summary>One receivable billed on an invoice.</summary>
public sealed record InvoiceLineModel(
    Guid Id,
    ReceivableId ReceivableId,
    MoneyModel Amount,
    string Description,
    int Sequence);

/// <summary>A billing instrument, when one is used.</summary>
/// <param name="HoldsDocument">
/// Whether AgencyOS holds the invoice document. Always false: M9 records that an
/// invoice exists and where it lives, and sends nothing.
/// </param>
public sealed record InvoiceModel(
    InvoiceId Id,
    string? Reference,
    ContractId ContractId,
    string ContractTitle,
    Guid DebtorPartyId,
    string DebtorDisplayName,
    InvoiceStatus Status,
    DateOnly? IssuedOn,
    DateOnly? DueOn,
    MoneyModel Total,
    MoneyModel Outstanding,
    bool IsOverdue,
    bool HoldsDocument,
    string? ExternalReference,
    string? VoidReason,
    string? Notes,
    IReadOnlyList<InvoiceLineModel> Lines,
    DateTimeOffset CreatedAt,
    int Version);

/// <summary>How much of a payment went to which receivable.</summary>
public sealed record PaymentAllocationModel(
    PaymentAllocationId Id,
    PaymentId PaymentId,
    ReceivableId ReceivableId,
    string? ReceivableReference,
    string ContractTitle,
    MoneyModel Amount,
    bool IsApplied,
    DateTimeOffset AppliedAt,
    string? AppliedByDisplayName,
    DateTimeOffset? ReversedAt,
    string? ReversalReason);

/// <summary>Money that actually moved.</summary>
/// <param name="ReceivedOn">The day it moved, as reported. Freely backdated.</param>
/// <param name="RecordedAt">When AgencyOS was told.</param>
/// <param name="Unapplied">What has not been applied to anything. Derived, never discarded.</param>
public sealed record PaymentModel(
    PaymentId Id,
    PaymentDirection Direction,
    Guid? PayerPartyId,
    string PayerDisplayName,
    Guid? PayeePartyId,
    string? PayeeDisplayName,
    MoneyModel Amount,
    MoneyModel Allocated,
    MoneyModel Unapplied,
    DateOnly ReceivedOn,
    DateTimeOffset RecordedAt,
    PaymentMethod Method,
    string? ExternalReference,
    string? SourceSystem,
    PaymentStatus Status,
    PaymentId? ReversedByPaymentId,
    PaymentId? ReversalOfPaymentId,
    string? ReversalReason,
    string? RecordedByDisplayName,
    string? Notes,
    IReadOnlyList<PaymentAllocationModel> Allocations,
    int Version);

/// <summary>Money that will never arrive, and why somebody says so.</summary>
public sealed record PaymentAdjustmentModel(
    PaymentAdjustmentId Id,
    ReceivableId ReceivableId,
    PaymentId? PaymentId,
    PaymentAdjustmentKind Kind,
    MoneyModel Amount,
    string Description,
    string? ExternalReference,
    DateOnly OccurredOn,
    bool IsApplied,
    string? RecordedByDisplayName);

/// <summary>The rule that decides what the agency is entitled to.</summary>
public sealed record CommissionRuleModel(
    CommissionRuleId Id,
    Guid RepresentationId,
    Guid ClientPersonId,
    string ClientDisplayName,
    ContractId? ContractId,
    string? ContractTitle,
    CommissionBasisKind Basis,
    decimal? RatePercent,
    MoneyModel? FixedAmount,
    ContractTermCode? TermCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsInForceToday,
    string? Provenance,
    string? Notes,
    int Version);

/// <summary>A change to what the agency is owed.</summary>
public sealed record CommissionAdjustmentModel(
    Guid Id,
    CommissionAdjustmentKind Kind,
    MoneyModel Amount,
    string Reason,
    DateTimeOffset RecordedAt,
    string? RecordedByDisplayName);

/// <summary>
/// What the agency is entitled to, and what it has actually earned.
/// </summary>
/// <remarks>
/// Three different numbers, kept apart on purpose. <paramref name="Entitled"/> is
/// what the rule gives against the whole obligation; <paramref name="Collected"/>
/// is the same rate against what has actually arrived; <paramref name="Outstanding"/>
/// is what is left. An agency entitled to a hundred thousand against a contract
/// that has paid four hundred of a million has collected forty (ADR-0023).
/// </remarks>
public sealed record CommissionEntitlementModel(
    CommissionEntitlementId Id,
    MonetaryObligationId MonetaryObligationId,
    ContractId ContractId,
    string ContractTitle,
    Guid ClientPersonId,
    string ClientDisplayName,
    Guid RepresentationId,
    CommissionRuleId CommissionRuleId,
    ReceivableId? ClientReceivableId,
    CommissionBasisKind Basis,
    decimal? RatePercentSnapshot,
    MoneyModel BasisAmount,
    MoneyModel Entitled,
    MoneyModel Collected,
    MoneyModel Adjusted,
    MoneyModel Outstanding,
    DateOnly GoverningOn,
    CommissionEntitlementStatus Status,
    IReadOnlyList<CommissionAdjustmentModel> Adjustments,
    DateTimeOffset CalculatedAt,
    string? CalculatedByDisplayName,
    string? Notes,
    int Version);

/// <summary>One account in the organization's ledger.</summary>
public sealed record AccountModel(
    AccountId Id,
    SystemAccount Kind,
    LedgerAccountCategory Category,
    string Code,
    string Name,
    string? Description);

/// <summary>One line of a journal entry.</summary>
public sealed record JournalLineModel(
    Guid Id,
    AccountId AccountId,
    string AccountCode,
    string AccountName,
    JournalSide Side,
    MoneyModel Amount,
    int Sequence,
    string? Memo);

/// <summary>One accounting event, in balanced lines.</summary>
/// <param name="OccurredOn">When the economic event happened.</param>
/// <param name="PostingDate">The date the entry belongs to for accounting.</param>
/// <param name="RecordedAt">When AgencyOS was told. Three dates, never merged.</param>
public sealed record JournalEntryModel(
    JournalEntryId Id,
    JournalEntryStatus Status,
    JournalSource Source,
    string Memo,
    string Currency,
    MoneyModel Debits,
    MoneyModel Credits,
    bool IsBalanced,
    DateOnly OccurredOn,
    DateOnly PostingDate,
    DateTimeOffset RecordedAt,
    DateTimeOffset? PostedAt,
    string? PostedByDisplayName,
    ReceivableId? ReceivableId,
    PaymentId? PaymentId,
    CommissionEntitlementId? CommissionEntitlementId,
    JournalEntryId? ReversalOfEntryId,
    JournalEntryId? ReversedByEntryId,
    string? ReversalReason,
    IReadOnlyList<JournalLineModel> Lines,
    int Version);

/// <summary>What an account holds, by currency.</summary>
/// <remarks>
/// Per currency and never summed across them. Adding dollars to euros produces a
/// number that means nothing, and AgencyOS holds no rate that would make it mean
/// something (ADR-0023).
/// </remarks>
public sealed record AccountBalanceModel(
    AccountId AccountId,
    SystemAccount Kind,
    string Code,
    string Name,
    LedgerAccountCategory Category,
    string Currency,
    MoneyModel Debits,
    MoneyModel Credits,
    MoneyModel Balance);

/// <summary>
/// What arrived against what was expected.
/// </summary>
/// <param name="Outcome">NotStarted, Reconciled, Shortfall or Excess. Facts, not blame.</param>
/// <param name="Variance">The size of the gap.</param>
/// <param name="Explanation">
/// The arithmetic in a sentence. It never says what the variance was, because the
/// system does not know.
/// </param>
public sealed record ReconciliationModel(
    ReceivableId ReceivableId,
    string? Reference,
    ContractId ContractId,
    string ContractTitle,
    MoneyModel Expected,
    MoneyModel Allocated,
    MoneyModel Deductions,
    MoneyModel WrittenOff,
    MoneyModel Variance,
    int VarianceDirection,
    string Outcome,
    string Explanation,
    IReadOnlyList<PaymentAdjustmentModel> Adjustments,
    IReadOnlyList<PaymentAllocationModel> Allocations);

/// <summary>An outstanding task linked to finance work.</summary>
public sealed record FinanceTaskModel(
    Guid Id,
    string Title,
    string State,
    string Priority,
    DateTimeOffset? DueAt,
    ReceivableId? ReceivableId,
    InvoiceId? InvoiceId,
    PaymentId? PaymentId);

/// <summary>One entry in the curated financial history.</summary>
/// <remarks>
/// One business act, one entry, however many rows it wrote. Never raw audit rows,
/// which answer a security question in a security vocabulary (ADR-0012).
/// </remarks>
public sealed record FinanceHistoryEntryModel(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    MoneyModel? Amount,
    string? Detail,
    string? ActorDisplayName);

/// <summary>A total, by currency.</summary>
/// <remarks>
/// The only shape an aggregate takes in M9. Every figure the finance surfaces show
/// is grouped by currency, because there is no honest way to add two.
/// </remarks>
public sealed record CurrencyTotalModel(string Currency, MoneyModel Total, int Count);

/// <summary>
/// What the finance desk has to look at.
/// </summary>
/// <remarks>
/// Counts, dates and lists of real rows. No cash forecast, no revenue prediction,
/// no valuation, no deal-quality score: every one of those would be a claim about
/// the future, and M9 records what happened (ADR-0023).
/// </remarks>
public sealed record FinanceCommandCenterModel(
    IReadOnlyList<ReceivableModel> Overdue,
    IReadOnlyList<ReceivableModel> DueSoon,
    IReadOnlyList<PaymentModel> UnappliedPayments,
    IReadOnlyList<ReconciliationModel> UnreconciledVariances,
    IReadOnlyList<CommissionEntitlementModel> UncollectedCommission,
    IReadOnlyList<MonetaryObligationModel> UnbilledObligations,
    IReadOnlyList<FinanceTaskModel> OverdueTasks,
    IReadOnlyList<CurrencyTotalModel> OutstandingByCurrency,
    IReadOnlyList<CurrencyTotalModel> UnappliedByCurrency,
    IReadOnlyList<CurrencyTotalModel> CommissionOutstandingByCurrency,
    int OverdueCount,
    int UnappliedCount);

/// <summary>The optional predicates a receivable list accepts.</summary>
public sealed record ReceivableFilter(
    ReceivableStatus? Status = null,
    ContractId? ContractId = null,
    Guid? PayerPartyId = null,
    Guid? ClientPersonId = null,
    ReceivableBeneficiary? Beneficiary = null,
    bool OverdueOnly = false,
    bool UnreconciledOnly = false,
    DateOnly? DueAfter = null,
    DateOnly? DueBefore = null,
    string? Currency = null,
    string? Search = null);

/// <summary>The optional predicates an invoice list accepts.</summary>
public sealed record InvoiceFilter(
    InvoiceStatus? Status = null,
    ContractId? ContractId = null,
    Guid? DebtorPartyId = null,
    bool OverdueOnly = false,
    DateOnly? DueAfter = null,
    DateOnly? DueBefore = null,
    string? Currency = null,
    string? Search = null);

/// <summary>The optional predicates a payment list accepts.</summary>
public sealed record PaymentFilter(
    PaymentDirection? Direction = null,
    PaymentStatus? Status = null,
    Guid? PayerPartyId = null,
    bool UnappliedOnly = false,
    DateOnly? RecordedAfter = null,
    DateOnly? RecordedBefore = null,
    string? Currency = null,
    string? Search = null);

/// <summary>The optional predicates a commission list accepts.</summary>
public sealed record CommissionFilter(
    Guid? ClientPersonId = null,
    ContractId? ContractId = null,
    Guid? RepresentationId = null,
    CommissionEntitlementStatus? Status = null,
    bool OutstandingOnly = false,
    string? Currency = null);

/// <summary>The optional predicates a journal list accepts.</summary>
public sealed record JournalFilter(
    JournalEntryStatus? Status = null,
    JournalSource? Source = null,
    AccountId? AccountId = null,
    DateOnly? PostedAfter = null,
    DateOnly? PostedBefore = null,
    string? Currency = null);

/// <summary>
/// Read-side projections for the finance model.
/// </summary>
/// <remarks>
/// Every derived fact - a balance, a status, an unapplied residual, a variance, a
/// collected commission - is computed here from the rows that recorded the events.
/// Nothing is stored beside the aggregate, so nothing can drift from it
/// (ADR-0023).
/// </remarks>
public interface IFinanceQueries
{
    Task<IReadOnlyList<MonetaryObligationModel>> ListObligationsAsync(
        OrganizationId organizationId,
        ContractId? contractId,
        bool unbilledOnly,
        int limit,
        CancellationToken cancellationToken = default);

    Task<MonetaryObligationModel?> GetObligationAsync(
        OrganizationId organizationId,
        MonetaryObligationId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReceivableModel>> ListReceivablesAsync(
        OrganizationId organizationId,
        ReceivableFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<ReceivableModel?> GetReceivableAsync(
        OrganizationId organizationId,
        ReceivableId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceModel>> ListInvoicesAsync(
        OrganizationId organizationId,
        InvoiceFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<InvoiceModel?> GetInvoiceAsync(
        OrganizationId organizationId,
        InvoiceId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentModel>> ListPaymentsAsync(
        OrganizationId organizationId,
        PaymentFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<PaymentModel?> GetPaymentAsync(
        OrganizationId organizationId,
        PaymentId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommissionRuleModel>> ListCommissionRulesAsync(
        OrganizationId organizationId,
        Guid? clientPersonId,
        ContractId? contractId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommissionEntitlementModel>> ListCommissionsAsync(
        OrganizationId organizationId,
        CommissionFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CommissionEntitlementModel?> GetCommissionAsync(
        OrganizationId organizationId,
        CommissionEntitlementId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountModel>> ListAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JournalEntryModel>> ListJournalEntriesAsync(
        OrganizationId organizationId,
        JournalFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<JournalEntryModel?> GetJournalEntryAsync(
        OrganizationId organizationId,
        JournalEntryId id,
        CancellationToken cancellationToken = default);

    /// <summary>Account balances, per currency and never summed across them.</summary>
    Task<IReadOnlyList<AccountBalanceModel>> GetBalancesAsync(
        OrganizationId organizationId,
        string? currency,
        CancellationToken cancellationToken = default);

    Task<ReconciliationModel?> ReconcileAsync(
        OrganizationId organizationId,
        ReceivableId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinanceHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        ContractId? contractId,
        ReceivableId? receivableId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<FinanceCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
