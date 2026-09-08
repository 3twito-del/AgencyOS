using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Abstractions;

/// <summary>
/// Repositories for the M9 finance model.
/// </summary>
/// <remarks>
/// Every lookup takes the tenant explicitly, as M2 through M8 do. The database
/// enforces the same containment through composite foreign keys, so a cross-tenant
/// read is impossible to write by accident rather than merely refused after the
/// fact (ADR-0011).
/// </remarks>
public interface IMonetaryObligationRepository
{
    Task<MonetaryObligation?> FindAsync(
        OrganizationId organizationId,
        MonetaryObligationId id,
        CancellationToken cancellationToken = default);

    /// <summary>Every quantified obligation on a contract, for commission calculation.</summary>
    Task<IReadOnlyList<MonetaryObligation>> ListForContractAsync(
        OrganizationId organizationId,
        ContractId contractId,
        CancellationToken cancellationToken = default);

    void Add(MonetaryObligation obligation);
}

public interface IReceivableRepository
{
    Task<Receivable?> FindAsync(
        OrganizationId organizationId,
        ReceivableId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads several receivables at once, for an allocation across many.
    /// </summary>
    /// <remarks>
    /// One query rather than one per line, because a payment settling six invoices
    /// is ordinary and six round trips inside a transaction that holds row locks is
    /// how a concurrent allocation turns into a deadlock.
    /// </remarks>
    Task<IReadOnlyList<Receivable>> FindManyAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<ReceivableId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Every receivable raised from an obligation.</summary>
    Task<IReadOnlyList<Receivable>> ListForObligationAsync(
        OrganizationId organizationId,
        MonetaryObligationId obligationId,
        CancellationToken cancellationToken = default);

    void Add(Receivable receivable);

    void AddAdjustment(PaymentAdjustment adjustment);

    Task<PaymentAdjustment?> FindAdjustmentAsync(
        OrganizationId organizationId,
        PaymentAdjustmentId id,
        CancellationToken cancellationToken = default);

    /// <summary>The applied adjustments against a receivable, for reconciliation.</summary>
    Task<IReadOnlyList<PaymentAdjustment>> ListAdjustmentsAsync(
        OrganizationId organizationId,
        ReceivableId receivableId,
        CancellationToken cancellationToken = default);
}

public interface IInvoiceRepository
{
    Task<Invoice?> FindAsync(
        OrganizationId organizationId,
        InvoiceId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether an invoice number is already in use in this tenant.
    /// </summary>
    /// <remarks>
    /// Checked here so the caller gets a sentence rather than a constraint
    /// violation; the unique index is what settles a race between two people
    /// numbering at once.
    /// </remarks>
    Task<bool> ReferenceExistsAsync(
        OrganizationId organizationId,
        string reference,
        InvoiceId? excluding,
        CancellationToken cancellationToken = default);

    void Add(Invoice invoice);
}

public interface IPaymentRepository
{
    /// <summary>Loads a payment with the allocations its commands reason about.</summary>
    Task<Payment?> FindAsync(
        OrganizationId organizationId,
        PaymentId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Payments already recorded with the same external reference.
    /// </summary>
    /// <remarks>
    /// A duplicate-entry warning, not a uniqueness rule. Two genuinely different
    /// payments can carry the same remittance text, so a match is reported to the
    /// caller and never used to refuse the record (ADR-0023).
    /// </remarks>
    Task<IReadOnlyList<Payment>> FindByExternalReferenceAsync(
        OrganizationId organizationId,
        string externalReference,
        CancellationToken cancellationToken = default);

    /// <summary>Every applied allocation against a receivable, for its running total.</summary>
    Task<IReadOnlyList<PaymentAllocation>> ListAllocationsForReceivableAsync(
        OrganizationId organizationId,
        ReceivableId receivableId,
        CancellationToken cancellationToken = default);

    void Add(Payment payment);
}

public interface ICommissionRepository
{
    Task<CommissionRule?> FindRuleAsync(
        OrganizationId organizationId,
        CommissionRuleId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every rule that could govern a client's transaction.
    /// </summary>
    /// <remarks>
    /// Loaded whole and narrowed by the kernel, because which rule governs is a
    /// question about dates and specificity that belongs in one place.
    /// </remarks>
    Task<IReadOnlyList<CommissionRule>> ListRulesForClientAsync(
        OrganizationId organizationId,
        Guid clientPersonId,
        CancellationToken cancellationToken = default);

    Task<CommissionEntitlement?> FindEntitlementAsync(
        OrganizationId organizationId,
        CommissionEntitlementId id,
        CancellationToken cancellationToken = default);

    /// <summary>Entitlements already calculated against an obligation.</summary>
    Task<IReadOnlyList<CommissionEntitlement>> ListEntitlementsForObligationAsync(
        OrganizationId organizationId,
        MonetaryObligationId obligationId,
        CancellationToken cancellationToken = default);

    void AddRule(CommissionRule rule);

    void AddEntitlement(CommissionEntitlement entitlement);
}

public interface ILedgerRepository
{
    /// <summary>The organization's accounts, seeded on first use.</summary>
    Task<IReadOnlyList<Account>> ListAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    Task<JournalEntry?> FindEntryAsync(
        OrganizationId organizationId,
        JournalEntryId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The posted entry an allocation produced, so a reversal undoes that one.
    /// </summary>
    /// <remarks>
    /// A reversal must mirror the entry that actually posted. Rebuilding the lines
    /// from the source and posting them again would put a second copy of the
    /// original into the ledger before undoing it, and the pair would net to
    /// nothing while the account totals had moved twice (ADR-0023).
    /// </remarks>
    Task<JournalEntry?> FindEntryForAllocationAsync(
        OrganizationId organizationId,
        PaymentAllocationId allocationId,
        CancellationToken cancellationToken = default);

    /// <summary>The posted entry a receivable produced, for a given source.</summary>
    Task<JournalEntry?> FindEntryForReceivableAsync(
        OrganizationId organizationId,
        ReceivableId receivableId,
        JournalSource source,
        CancellationToken cancellationToken = default);

    void AddAccounts(IEnumerable<Account> accounts);

    void AddEntry(JournalEntry entry);
}

/// <summary>Records the curated financial history.</summary>
/// <remarks>
/// Separate from both the audit trail and the ledger, because the three answer
/// different questions and merging any two would make the answer to one of them
/// worse (ADR-0012, ADR-0023).
/// </remarks>
public interface IFinanceEventRepository
{
    void Add(FinanceEvent entry);
}

/// <summary>Links ordinary tasks to the finance work they chase.</summary>
public interface IFinanceTaskLinkRepository
{
    void Add(FinanceTaskLink link);
}
