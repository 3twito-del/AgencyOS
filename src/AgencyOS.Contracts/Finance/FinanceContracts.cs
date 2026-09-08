namespace AgencyOS.Contracts.Finance;

// ------------------------------------------------------------------- requests

/// <summary>
/// An amount and the currency it is in.
/// </summary>
/// <remarks>
/// Every monetary value in this contract is one of these. There is no bare number
/// anywhere: a figure that travels without its currency is one somebody will assume
/// is dollars, and AgencyOS holds no exchange rate that could correct the
/// assumption later (ADR-0023).
/// </remarks>
/// <param name="Amount">The figure. A decimal, never a float.</param>
/// <param name="Currency">The ISO 4217 alphabetic code.</param>
public sealed record MoneyRequest(decimal Amount, string Currency);

/// <summary>
/// When a payment falls due, as the contract expresses it.
/// </summary>
/// <remarks>
/// The M8 deadline shape, reused unchanged. <c>BusinessDays</c> is accepted and
/// stored faithfully and then declines to produce a date, because AgencyOS holds no
/// holiday calendar (ADR-0022, ADR-0023).
/// </remarks>
/// <param name="Kind">
/// Absolute for a stated date, Relative for an offset from an event, or
/// Unstructured for a clause this build cannot compute a date from. The three are
/// kept apart because contracts genuinely say three different things.
/// </param>
/// <param name="Description">
/// The clause's own wording. Required for an unstructured rule, so a deadline
/// nothing can be computed from still tells a reader what is required.
/// </param>
public sealed record DueRuleRequest(
    string Kind,
    DateOnly? On = null,
    string? Anchor = null,
    int? Offset = null,
    string? Unit = null,
    bool Before = false,
    string? Basis = null,
    string? Description = null);

/// <summary>
/// Records a sum an operative contract says is payable.
/// </summary>
/// <remarks>
/// The contract must be <strong>operative</strong>: executed, or with a recorded
/// effective date, and not abandoned or superseded. Agreed commercial terms are not
/// a collectible legal amount, and M9 will not turn them into one (ADR-0023).
/// </remarks>
/// <param name="ContractVersionId">The version the obligation was read out of.</param>
/// <param name="PayerPartyId">Who owes it. A party to the contract.</param>
/// <param name="PayeePartyId">Who is owed it.</param>
/// <param name="Category">
/// Compensation, Instalment, Episodic, SigningPayment, Bonus, Deferred,
/// OptionPayment, Expense, Participation or Other.
/// </param>
/// <param name="AmountKind">
/// Fixed, Formula, Contingent or Unknown. <c>Unknown</c> is a real answer: a
/// participation nobody can value is an obligation with no figure, and recording it
/// as zero would put a false number into every total that touches it.
/// </param>
/// <param name="Due">When it falls due.</param>
/// <param name="Amount">The sum, for a fixed obligation.</param>
/// <param name="Quantity">How many units, for a formula obligation.</param>
/// <param name="UnitAmount">What one unit pays.</param>
/// <param name="Unit">Episode, Week, Day and so on.</param>
/// <param name="Condition">What a contingent obligation waits on, as written.</param>
/// <param name="AnchorDate">The anchor date, when it is already known.</param>
/// <param name="SourceObligationId">The M8 obligation it implements, when it implements one.</param>
/// <param name="SourceTermCode">The contract term that states the figure.</param>
/// <param name="Description">What the payment is for.</param>
/// <param name="Notes">Context.</param>
public sealed record RecordMonetaryObligationRequest(
    Guid ContractVersionId,
    Guid PayerPartyId,
    Guid PayeePartyId,
    string Category,
    string AmountKind,
    DueRuleRequest Due,
    MoneyRequest? Amount = null,
    int? Quantity = null,
    MoneyRequest? UnitAmount = null,
    string? Unit = null,
    string? Condition = null,
    DateOnly? AnchorDate = null,
    Guid? SourceObligationId = null,
    string? SourceTermCode = null,
    string? Description = null,
    string? Notes = null);

/// <summary>Fixes the amount of an obligation recorded without one.</summary>
public sealed record QuantifyObligationRequest(
    MoneyRequest Amount,
    int ExpectedVersion,
    string? Reason = null);

/// <summary>Records that an obligation will not fall due after all.</summary>
public sealed record ReleaseObligationRequest(string Reason, int ExpectedVersion);

/// <summary>
/// Raises a receivable from a quantified obligation.
/// </summary>
/// <param name="Beneficiary">
/// Client or Agency. The most consequential field in the milestone: money collected
/// against a client receivable is held and owed onward, not earned.
/// </param>
/// <param name="Amount">Defaults to the obligation's own figure.</param>
/// <param name="DueOn">Defaults to the obligation's resolved due date.</param>
/// <param name="ClientPersonId">Required for a client receivable.</param>
/// <param name="RepresentationId">The representation it arises under.</param>
/// <param name="Reference">The agency's own handle for it.</param>
/// <param name="Notes">Context.</param>
public sealed record RaiseReceivableRequest(
    string Beneficiary,
    MoneyRequest? Amount = null,
    DateOnly? DueOn = null,
    Guid? ClientPersonId = null,
    Guid? RepresentationId = null,
    string? Reference = null,
    string? Notes = null);

/// <summary>
/// Gives up on collecting what remains.
/// </summary>
/// <remarks>
/// A financial act with a reason and a posting, not data cleanup. The original
/// amount stays exactly what it was (ADR-0023).
/// </remarks>
public sealed record WriteOffReceivableRequest(string Reason, int ExpectedVersion);

/// <summary>Withdraws a receivable raised in error, before anything was collected.</summary>
public sealed record CancelReceivableRequest(string Reason, int ExpectedVersion);

/// <param name="ReceivableId">What is being billed.</param>
/// <param name="Amount">How much of it. Never more than is outstanding.</param>
public sealed record InvoiceLineRequest(Guid ReceivableId, MoneyRequest Amount, string Description);

/// <summary>
/// Records an invoice against receivables that already exist.
/// </summary>
/// <remarks>
/// <strong>Not every receivable needs one.</strong> A payer settling on a schedule
/// the contract sets may never receive an invoice, so this is an optional
/// instrument that points at what is owed rather than a required stage before the
/// money.
/// </remarks>
/// <param name="Reference">
/// The operator's own invoice number. AgencyOS assigns none, because numbering
/// carries statutory weight that varies by jurisdiction and the system is in no
/// position to claim compliance with any of them. Unique per organization.
/// </param>
public sealed record RecordInvoiceRequest(
    Guid DebtorPartyId,
    string Currency,
    IReadOnlyList<InvoiceLineRequest> Lines,
    string? Reference = null,
    DateOnly? DueOn = null,
    string? ExternalReference = null,
    string? Notes = null);

/// <summary>
/// Records that an invoice was issued.
/// </summary>
/// <remarks>
/// A fact somebody entered, not an act AgencyOS performed. The verb is deliberately
/// not "send": there is no transport, no email and no attachment, and delivery is
/// M10's subject (ADR-0023).
/// </remarks>
public sealed record IssueInvoiceRequest(
    DateOnly IssuedOn,
    int ExpectedVersion,
    string? Reference = null);

/// <summary>Withdraws an issued invoice, keeping the record.</summary>
public sealed record VoidInvoiceRequest(string Reason, int ExpectedVersion);

/// <param name="ReceivableId">What the money is for.</param>
/// <param name="Amount">How much of the payment goes to it.</param>
public sealed record AllocationRequest(Guid ReceivableId, MoneyRequest Amount, string? Notes = null);

/// <summary>
/// Records that money moved.
/// </summary>
/// <remarks>
/// <para>
/// A payment is a fact somebody observed. Its amount, currency and received date
/// are immutable once recorded; a typo is corrected by reversing it and recording
/// the right one, so both survive with their own dates and actors (ADR-0023).
/// </para>
/// <para>
/// Anything not allocated stays <strong>unapplied</strong> and is reported back.
/// There is no auto-matching: assigning a residual to whichever receivable looks
/// closest would be the system guessing at somebody's intent and acting on it.
/// </para>
/// </remarks>
/// <param name="Direction">Incoming or Outgoing.</param>
/// <param name="ReceivedOn">The day it moved, as reported. Freely backdated.</param>
/// <param name="Method">BankTransfer, Cheque, Card, Cash, Offset or Other.</param>
/// <param name="ExternalReference">
/// The payer's or bank's own identifier. Not assumed unique: two genuinely
/// different payments can carry the same remittance text, so a match is reported as
/// a possible duplicate and never used to refuse the record.
/// </param>
/// <param name="SourceSystem">Where the reference came from. A future import fills this seam.</param>
/// <param name="Allocations">What it is for, if the caller already knows.</param>
public sealed record RecordPaymentRequest(
    string Direction,
    MoneyRequest Amount,
    DateOnly ReceivedOn,
    string Method,
    Guid? PayerPartyId = null,
    string? PayerName = null,
    Guid? PayeePartyId = null,
    string? PayeeName = null,
    string? ExternalReference = null,
    string? SourceSystem = null,
    string? Notes = null,
    IReadOnlyList<AllocationRequest>? Allocations = null);

/// <summary>Applies unapplied cash to receivables.</summary>
public sealed record AllocatePaymentRequest(
    IReadOnlyList<AllocationRequest> Allocations,
    int ExpectedVersion);

/// <summary>Returns an allocation's money to unapplied, keeping the line as history.</summary>
public sealed record ReverseAllocationRequest(
    Guid AllocationId,
    string Reason,
    int ExpectedVersion);

/// <summary>Undoes a payment recorded in error, with a reversing payment.</summary>
public sealed record ReversePaymentRequest(string Reason, int ExpectedVersion);

/// <summary>
/// Records a deduction that reduces what will ever arrive.
/// </summary>
/// <remarks>
/// A fact somebody entered, never an inference. A gap with no adjustment against it
/// stays a gap: attributing it to withholding would produce a reconciled receivable
/// and a wrong tax record, and nobody would look at either again. AgencyOS
/// implements no tax engine and infers no liability (ADR-0023).
/// </remarks>
/// <param name="Kind">
/// Withholding, BankFee, WireFee, AgreedReduction, WriteOff or Other.
/// </param>
public sealed record RecordAdjustmentRequest(
    string Kind,
    MoneyRequest Amount,
    string Description,
    DateOnly OccurredOn,
    int ExpectedVersion,
    Guid? PaymentId = null,
    string? ExternalReference = null);

/// <summary>
/// Records the rule that decides what the agency is entitled to.
/// </summary>
/// <remarks>
/// Effective-dated, because a rate is a term of a relationship and relationships
/// are renegotiated. There is no default rate anywhere in AgencyOS: a transaction
/// with no governing rule produces no entitlement and says so (ADR-0023).
/// </remarks>
/// <param name="Basis">GrossCompensation, SpecificTerm or FixedAmount.</param>
/// <param name="RatePercent">The percentage, entered as a percentage: ten per cent is 10.</param>
/// <param name="FixedAmount">The sum, for a fixed rule.</param>
/// <param name="TermCode">The term a specific-term rule applies to.</param>
/// <param name="ContractId">The contract it is confined to, or null to govern broadly.</param>
public sealed record CreateCommissionRuleRequest(
    Guid RepresentationId,
    Guid ClientPersonId,
    string Basis,
    DateOnly EffectiveFrom,
    decimal? RatePercent = null,
    MoneyRequest? FixedAmount = null,
    string? TermCode = null,
    Guid? ContractId = null,
    DateOnly? EffectiveTo = null,
    string? Provenance = null,
    string? Notes = null);

/// <summary>Ends a rule from a date, which is the only way one stops governing.</summary>
public sealed record EndCommissionRuleRequest(DateOnly EndsOn, int ExpectedVersion);

/// <summary>
/// Works out what the agency is entitled to against an obligation.
/// </summary>
/// <remarks>
/// The governing date defaults to the day the obligation falls due, not to today.
/// Recalculating a 2027 commission in 2029 must give the 2027 answer, because the
/// 2027 rule is what the parties were operating under (ADR-0023).
/// </remarks>
public sealed record CalculateCommissionRequest(
    Guid ClientPersonId,
    Guid RepresentationId,
    DateOnly? GoverningOn = null,
    Guid? ClientReceivableId = null,
    string? Notes = null);

/// <summary>
/// Records a change to what the agency is owed.
/// </summary>
/// <remarks>
/// An adjustment row, never an edit to the calculated figure. "We calculated a
/// hundred thousand and then waived ten" is a different story from "we calculated
/// ninety thousand", and only the first is true.
/// </remarks>
/// <param name="Kind">RateCorrection, Settlement, Waiver, BasisCorrection or Other.</param>
public sealed record AdjustCommissionRequest(
    string Kind,
    MoneyRequest Amount,
    string Reason,
    int ExpectedVersion);

/// <param name="Account">
/// Cash, AccountsReceivable, ClientFundsPayable, CommissionRevenue, UnappliedCash,
/// WriteOffExpense, DeductionExpense or Suspense.
/// </param>
/// <param name="Side">Debit or Credit. The amount is always positive.</param>
public sealed record JournalLineRequest(
    string Account,
    string Side,
    MoneyRequest Amount,
    string? Memo = null);

/// <summary>
/// Posts a balanced entry somebody wrote by hand.
/// </summary>
/// <remarks>
/// The narrowest surface in the milestone, behind <c>finance.ledger.post</c>. Every
/// other posting in AgencyOS is a consequence of a business act made by the system
/// in the same transaction; this is the one route by which a person writes an entry
/// directly (ADR-0023).
/// </remarks>
/// <param name="PostingDate">
/// The date the entry belongs to for accounting. Defaults to the economic date.
/// </param>
public sealed record PostJournalEntryRequest(
    string Memo,
    string Currency,
    DateOnly OccurredOn,
    IReadOnlyList<JournalLineRequest> Lines,
    DateOnly? PostingDate = null);

/// <summary>Posts the entry that undoes a posted one.</summary>
public sealed record ReverseJournalEntryRequest(string Reason, DateOnly? PostingDate = null);

// ------------------------------------------------------------------ responses

/// <summary>An amount and the currency it is in.</summary>
public sealed record MoneyResponse(decimal Amount, string Currency);

/// <summary>A sum an operative contract says is payable.</summary>
/// <param name="Amount">
/// What is due, when that is known. Absent is a real answer, not missing data.
/// </param>
/// <param name="DueOn">The due date, once it is a date anybody can work out.</param>
/// <param name="DueUnresolvedReason">Why it is not a date, when it is not.</param>
/// <param name="IsQuantified">Whether a collectible figure exists at all.</param>
/// <param name="HasReceivable">Whether anything has been raised from it yet.</param>
public sealed record MonetaryObligationResponse(
    Guid Id,
    Guid ContractId,
    string ContractTitle,
    Guid ContractVersionId,
    Guid? SourceObligationId,
    string? SourceTermCode,
    Guid PayerPartyId,
    string PayerDisplayName,
    Guid PayeePartyId,
    string PayeeDisplayName,
    string Category,
    string AmountKind,
    MoneyResponse? Amount,
    int? Quantity,
    MoneyResponse? UnitAmount,
    string? Condition,
    DateOnly? DueOn,
    string? DueUnresolvedReason,
    string? DueDescription,
    string Status,
    bool IsQuantified,
    bool HasReceivable,
    string? Description,
    string? Notes,
    int Version);

/// <summary>A sum the agency expects to collect.</summary>
/// <param name="Beneficiary">Client or Agency. Decides whose money it becomes.</param>
/// <param name="Outstanding">What is still owed. Derived from allocations and adjustments.</param>
/// <param name="Status">Open, PartiallyPaid, Paid, Cancelled or WrittenOff. Derived.</param>
/// <param name="IsOverdue">
/// Past a resolvable date with something owed. A receivable with no due date is
/// never overdue: the contract did not say when.
/// </param>
public sealed record ReceivableResponse(
    Guid Id,
    Guid MonetaryObligationId,
    Guid ContractId,
    string ContractTitle,
    Guid PayerPartyId,
    string PayerDisplayName,
    string Beneficiary,
    Guid? ClientPersonId,
    string? ClientDisplayName,
    MoneyResponse OriginalAmount,
    MoneyResponse Allocated,
    MoneyResponse Adjusted,
    MoneyResponse Outstanding,
    DateOnly? DueOn,
    string Status,
    bool IsOverdue,
    string? Reference,
    string? ClosureReason,
    string? Notes,
    DateTimeOffset CreatedAt,
    int Version);

/// <summary>One receivable billed on an invoice.</summary>
public sealed record InvoiceLineResponse(
    Guid Id,
    Guid ReceivableId,
    MoneyResponse Amount,
    string Description,
    int Sequence);

/// <summary>A billing instrument.</summary>
/// <param name="HoldsDocument">
/// Whether AgencyOS holds the invoice document. Always <c>false</c>: M9 records that
/// an invoice exists and where it lives, and sends nothing. M10 owns documents and
/// communications.
/// </param>
public sealed record InvoiceResponse(
    Guid Id,
    string? Reference,
    Guid ContractId,
    string ContractTitle,
    Guid DebtorPartyId,
    string DebtorDisplayName,
    string Status,
    DateOnly? IssuedOn,
    DateOnly? DueOn,
    MoneyResponse Total,
    MoneyResponse Outstanding,
    bool IsOverdue,
    bool HoldsDocument,
    string? ExternalReference,
    string? VoidReason,
    string? Notes,
    IReadOnlyList<InvoiceLineResponse> Lines,
    DateTimeOffset CreatedAt,
    int Version);

/// <summary>How much of a payment went to which receivable.</summary>
public sealed record PaymentAllocationResponse(
    Guid Id,
    Guid PaymentId,
    Guid ReceivableId,
    string? ReceivableReference,
    string ContractTitle,
    MoneyResponse Amount,
    bool IsApplied,
    DateTimeOffset AppliedAt,
    string? AppliedByDisplayName,
    DateTimeOffset? ReversedAt,
    string? ReversalReason);

/// <summary>Money that actually moved.</summary>
/// <param name="ReceivedOn">The day it moved, as reported.</param>
/// <param name="RecordedAt">When AgencyOS was told. Two dates, never merged.</param>
/// <param name="Unapplied">What has not been applied to anything. Derived, never discarded.</param>
public sealed record PaymentResponse(
    Guid Id,
    string Direction,
    Guid? PayerPartyId,
    string PayerDisplayName,
    Guid? PayeePartyId,
    string? PayeeDisplayName,
    MoneyResponse Amount,
    MoneyResponse Allocated,
    MoneyResponse Unapplied,
    DateOnly ReceivedOn,
    DateTimeOffset RecordedAt,
    string Method,
    string? ExternalReference,
    string? SourceSystem,
    string Status,
    Guid? ReversedByPaymentId,
    Guid? ReversalOfPaymentId,
    string? ReversalReason,
    string? RecordedByDisplayName,
    string? Notes,
    IReadOnlyList<PaymentAllocationResponse> Allocations,
    int Version);

/// <summary>Money that will never arrive, and why somebody says so.</summary>
public sealed record PaymentAdjustmentResponse(
    Guid Id,
    Guid ReceivableId,
    Guid? PaymentId,
    string Kind,
    MoneyResponse Amount,
    string Description,
    string? ExternalReference,
    DateOnly OccurredOn,
    bool IsApplied,
    string? RecordedByDisplayName);

/// <summary>The rule that decides what the agency is entitled to.</summary>
public sealed record CommissionRuleResponse(
    Guid Id,
    Guid RepresentationId,
    Guid ClientPersonId,
    string ClientDisplayName,
    Guid? ContractId,
    string? ContractTitle,
    string Basis,
    decimal? RatePercent,
    MoneyResponse? FixedAmount,
    string? TermCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsInForceToday,
    string? Provenance,
    string? Notes,
    int Version);

/// <summary>A change to what the agency is owed.</summary>
public sealed record CommissionAdjustmentResponse(
    Guid Id,
    string Kind,
    MoneyResponse Amount,
    string Reason,
    DateTimeOffset RecordedAt,
    string? RecordedByDisplayName);

/// <summary>
/// What the agency is entitled to, and what it has actually earned.
/// </summary>
/// <remarks>
/// Three different numbers, kept apart on purpose. An agency entitled to a hundred
/// thousand against a contract that has paid four hundred of a million has
/// collected forty thousand, and a surface that reported either as the other would
/// be wrong in a way somebody would act on (ADR-0023).
/// </remarks>
/// <param name="RatePercentSnapshot">
/// The rate as it stood at calculation, never re-read. A later correction to the
/// rule does not restate what was already acted on.
/// </param>
public sealed record CommissionEntitlementResponse(
    Guid Id,
    Guid MonetaryObligationId,
    Guid ContractId,
    string ContractTitle,
    Guid ClientPersonId,
    string ClientDisplayName,
    Guid RepresentationId,
    Guid CommissionRuleId,
    Guid? ClientReceivableId,
    string Basis,
    decimal? RatePercentSnapshot,
    MoneyResponse BasisAmount,
    MoneyResponse Entitled,
    MoneyResponse Collected,
    MoneyResponse Adjusted,
    MoneyResponse Outstanding,
    DateOnly GoverningOn,
    string Status,
    IReadOnlyList<CommissionAdjustmentResponse> Adjustments,
    DateTimeOffset CalculatedAt,
    string? CalculatedByDisplayName,
    string? Notes,
    int Version);

/// <summary>One account in the organization's ledger.</summary>
public sealed record AccountResponse(
    Guid Id,
    string Kind,
    string Category,
    string Code,
    string Name,
    string? Description);

/// <summary>One line of a journal entry.</summary>
/// <param name="Side">Debit or Credit. The amount is always positive.</param>
public sealed record JournalLineResponse(
    Guid Id,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string Side,
    MoneyResponse Amount,
    int Sequence,
    string? Memo);

/// <summary>One accounting event, in balanced lines.</summary>
/// <param name="OccurredOn">When the economic event happened.</param>
/// <param name="PostingDate">The date the entry belongs to for accounting.</param>
/// <param name="RecordedAt">When AgencyOS was told. Three dates, never merged.</param>
public sealed record JournalEntryResponse(
    Guid Id,
    string Status,
    string Source,
    string Memo,
    string Currency,
    MoneyResponse Debits,
    MoneyResponse Credits,
    bool IsBalanced,
    DateOnly OccurredOn,
    DateOnly PostingDate,
    DateTimeOffset RecordedAt,
    DateTimeOffset? PostedAt,
    string? PostedByDisplayName,
    Guid? ReceivableId,
    Guid? PaymentId,
    Guid? CommissionEntitlementId,
    Guid? ReversalOfEntryId,
    Guid? ReversedByEntryId,
    string? ReversalReason,
    IReadOnlyList<JournalLineResponse> Lines,
    int Version);

/// <summary>
/// What an account holds, in one currency.
/// </summary>
/// <remarks>
/// Per currency, never summed across them. Adding dollars to euros produces a
/// number that means nothing, and AgencyOS holds no rate that would make it mean
/// something (ADR-0023).
/// </remarks>
public sealed record AccountBalanceResponse(
    Guid AccountId,
    string Kind,
    string Code,
    string Name,
    string Category,
    string Currency,
    MoneyResponse Debits,
    MoneyResponse Credits,
    MoneyResponse Balance);

/// <summary>
/// What arrived against what was expected.
/// </summary>
/// <param name="Outcome">
/// NotStarted, Reconciled, Shortfall or Excess. Four factual outcomes and no
/// judgement: whether a shortfall is withholding, a bank charge, a dispute or a
/// mistake is something a person finds out.
/// </param>
/// <param name="VarianceDirection">1 short, -1 over, 0 exact.</param>
/// <param name="Explanation">The arithmetic in a sentence, with no guess about why.</param>
public sealed record ReceivableReconciliationResponse(
    Guid ReceivableId,
    string? Reference,
    Guid ContractId,
    string ContractTitle,
    MoneyResponse Expected,
    MoneyResponse Allocated,
    MoneyResponse Deductions,
    MoneyResponse WrittenOff,
    MoneyResponse Variance,
    int VarianceDirection,
    string Outcome,
    string Explanation,
    IReadOnlyList<PaymentAdjustmentResponse> Adjustments,
    IReadOnlyList<PaymentAllocationResponse> Allocations);

/// <summary>An outstanding task linked to finance work.</summary>
public sealed record FinanceTaskResponse(
    Guid Id,
    string Title,
    string State,
    string Priority,
    DateTimeOffset? DueAt,
    Guid? ReceivableId,
    Guid? InvoiceId,
    Guid? PaymentId);

/// <summary>One entry in the curated financial history.</summary>
public sealed record FinanceHistoryEntryResponse(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    MoneyResponse? Amount,
    string? Detail,
    string? ActorDisplayName);

/// <summary>A total, in one currency.</summary>
public sealed record CurrencyTotalResponse(string Currency, MoneyResponse Total, int Count);

/// <summary>
/// What the finance desk has to look at.
/// </summary>
/// <remarks>
/// Counts, dates and lists of real rows. There is deliberately no cash forecast, no
/// revenue prediction, no valuation and no score: each would be a claim about the
/// future, and M9 records what happened (ADR-0023).
/// </remarks>
/// <param name="UnbilledObligations">
/// Quantified obligations with nothing raised against them. An obligation whose
/// amount is contingent or unknown is not here: it is waiting for the world, not
/// for somebody to raise a receivable.
/// </param>
/// <param name="UncollectedCommission">
/// Absent entirely without <c>finance.commissions.read</c>. The only place M9
/// removes rather than refuses, and it removes a whole section rather than rows
/// inside an arithmetic.
/// </param>
public sealed record FinanceCommandCenterResponse(
    IReadOnlyList<ReceivableResponse> Overdue,
    IReadOnlyList<ReceivableResponse> DueSoon,
    IReadOnlyList<PaymentResponse> UnappliedPayments,
    IReadOnlyList<ReceivableReconciliationResponse> UnreconciledVariances,
    IReadOnlyList<CommissionEntitlementResponse> UncollectedCommission,
    IReadOnlyList<MonetaryObligationResponse> UnbilledObligations,
    IReadOnlyList<FinanceTaskResponse> OverdueTasks,
    IReadOnlyList<CurrencyTotalResponse> OutstandingByCurrency,
    IReadOnlyList<CurrencyTotalResponse> UnappliedByCurrency,
    IReadOnlyList<CurrencyTotalResponse> CommissionOutstandingByCurrency,
    int OverdueCount,
    int UnappliedCount);

/// <param name="ObligationId">The obligation recorded.</param>
public sealed record RecordMonetaryObligationResponse(Guid ObligationId);

/// <param name="ReceivableId">The receivable raised.</param>
public sealed record RaiseReceivableResponse(Guid ReceivableId);

/// <param name="InvoiceId">The invoice recorded.</param>
public sealed record RecordInvoiceResponse(Guid InvoiceId);

/// <summary>What a recorded payment produced.</summary>
/// <param name="Unapplied">
/// What is left over. Never silently assigned to anything: a residual belongs to
/// whoever paid it until somebody says what it is for.
/// </param>
/// <param name="PossibleDuplicates">
/// Payments already carrying the same external reference. A warning, never a
/// refusal: two genuinely different payments can share a remittance text.
/// </param>
public sealed record RecordPaymentResponse(
    Guid PaymentId,
    MoneyResponse Allocated,
    MoneyResponse Unapplied,
    IReadOnlyList<Guid> PossibleDuplicates);

/// <param name="AdjustmentId">The deduction recorded.</param>
public sealed record RecordAdjustmentResponse(Guid AdjustmentId);

/// <param name="CommissionRuleId">The rule created.</param>
public sealed record CreateCommissionRuleResponse(Guid CommissionRuleId);

/// <param name="CommissionEntitlementId">The entitlement calculated.</param>
public sealed record CalculateCommissionResponse(Guid CommissionEntitlementId);

/// <param name="JournalEntryId">The entry posted.</param>
public sealed record PostJournalEntryResponse(Guid JournalEntryId);

/// <param name="ReversalEntryId">The entry that undid it.</param>
public sealed record ReverseJournalEntryResponse(Guid ReversalEntryId);

/// <param name="ReversalPaymentId">The payment that undid it.</param>
public sealed record ReversePaymentResponse(Guid ReversalPaymentId);
