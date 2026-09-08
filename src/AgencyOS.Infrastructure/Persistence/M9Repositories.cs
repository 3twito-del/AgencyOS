using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>Loads and stores what contracts say is payable.</summary>
/// <remarks>
/// Every query filters on the tenant as well as the identifier, exactly as M2
/// through M8 do (ADR-0011).
/// </remarks>
public sealed class MonetaryObligationRepository : IMonetaryObligationRepository
{
    private readonly AgencyOsDbContext _context;

    public MonetaryObligationRepository(AgencyOsDbContext context) => _context = context;

    public Task<MonetaryObligation?> FindAsync(
        OrganizationId organizationId,
        MonetaryObligationId id,
        CancellationToken cancellationToken = default) =>
        _context.MonetaryObligations.FirstOrDefaultAsync(
            obligation => obligation.OrganizationId == organizationId && obligation.Id == id,
            cancellationToken);

    public async Task<IReadOnlyList<MonetaryObligation>> ListForContractAsync(
        OrganizationId organizationId,
        ContractId contractId,
        CancellationToken cancellationToken = default)
    {
        List<MonetaryObligation> obligations = await _context.MonetaryObligations
            .Where(x => x.OrganizationId == organizationId && x.ContractId == contractId)
            .OrderBy(x => x.ResolvedDueOn ?? DateOnly.MaxValue)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return obligations;
    }

    public void Add(MonetaryObligation obligation) => _context.MonetaryObligations.Add(obligation);
}

/// <summary>Loads and stores what the agency expects to collect.</summary>
public sealed class ReceivableRepository : IReceivableRepository
{
    private readonly AgencyOsDbContext _context;

    public ReceivableRepository(AgencyOsDbContext context) => _context = context;

    public Task<Receivable?> FindAsync(
        OrganizationId organizationId,
        ReceivableId id,
        CancellationToken cancellationToken = default) =>
        _context.Receivables.FirstOrDefaultAsync(
            receivable => receivable.OrganizationId == organizationId && receivable.Id == id,
            cancellationToken);

    /// <summary>
    /// Loads several at once, in a deterministic order.
    /// </summary>
    /// <remarks>
    /// Ordered by identifier so two concurrent allocations touching the same pair of
    /// receivables take their row locks in the same sequence. Unordered loads are
    /// how two transactions that would each have succeeded deadlock instead
    /// (ADR-0023).
    /// </remarks>
    public async Task<IReadOnlyList<Receivable>> FindManyAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<ReceivableId> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        ReceivableId[] wanted = [.. ids];

        List<Receivable> receivables = await _context.Receivables
            .Where(x => x.OrganizationId == organizationId && wanted.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return receivables;
    }

    public async Task<IReadOnlyList<Receivable>> ListForObligationAsync(
        OrganizationId organizationId,
        MonetaryObligationId obligationId,
        CancellationToken cancellationToken = default)
    {
        List<Receivable> receivables = await _context.Receivables
            .Where(x => x.OrganizationId == organizationId
                && x.MonetaryObligationId == obligationId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return receivables;
    }

    public void Add(Receivable receivable) => _context.Receivables.Add(receivable);

    public void AddAdjustment(PaymentAdjustment adjustment) =>
        _context.PaymentAdjustments.Add(adjustment);

    public Task<PaymentAdjustment?> FindAdjustmentAsync(
        OrganizationId organizationId,
        PaymentAdjustmentId id,
        CancellationToken cancellationToken = default) =>
        _context.PaymentAdjustments.FirstOrDefaultAsync(
            adjustment => adjustment.OrganizationId == organizationId && adjustment.Id == id,
            cancellationToken);

    public async Task<IReadOnlyList<PaymentAdjustment>> ListAdjustmentsAsync(
        OrganizationId organizationId,
        ReceivableId receivableId,
        CancellationToken cancellationToken = default)
    {
        List<PaymentAdjustment> adjustments = await _context.PaymentAdjustments
            .Where(x => x.OrganizationId == organizationId && x.ReceivableId == receivableId)
            .OrderBy(x => x.OccurredOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return adjustments;
    }
}

/// <summary>Loads and stores billing instruments.</summary>
public sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly AgencyOsDbContext _context;

    public InvoiceRepository(AgencyOsDbContext context) => _context = context;

    public Task<Invoice?> FindAsync(
        OrganizationId organizationId,
        InvoiceId id,
        CancellationToken cancellationToken = default) =>
        _context.Invoices
            .Include(invoice => invoice.Lines)
            .FirstOrDefaultAsync(
                invoice => invoice.OrganizationId == organizationId && invoice.Id == id,
                cancellationToken);

    public Task<bool> ReferenceExistsAsync(
        OrganizationId organizationId,
        string reference,
        InvoiceId? excluding,
        CancellationToken cancellationToken = default) =>
        _context.Invoices.AnyAsync(
            invoice => invoice.OrganizationId == organizationId
                && invoice.Reference == reference
                && (excluding == null || invoice.Id != excluding),
            cancellationToken);

    public void Add(Invoice invoice) => _context.Invoices.Add(invoice);
}

/// <summary>Loads and stores money that moved.</summary>
public sealed class PaymentRepository : IPaymentRepository
{
    private readonly AgencyOsDbContext _context;

    public PaymentRepository(AgencyOsDbContext context) => _context = context;

    public Task<Payment?> FindAsync(
        OrganizationId organizationId,
        PaymentId id,
        CancellationToken cancellationToken = default) =>
        _context.Payments
            .Include(payment => payment.Allocations)
            .FirstOrDefaultAsync(
                payment => payment.OrganizationId == organizationId && payment.Id == id,
                cancellationToken);

    /// <summary>
    /// Payments already carrying the same external reference.
    /// </summary>
    /// <remarks>
    /// A warning, never a rule. Two genuinely different payments can share a
    /// remittance text, so this reports matches and the caller decides (ADR-0023).
    /// </remarks>
    public async Task<IReadOnlyList<Payment>> FindByExternalReferenceAsync(
        OrganizationId organizationId,
        string externalReference,
        CancellationToken cancellationToken = default)
    {
        List<Payment> payments = await _context.Payments
            .Where(x => x.OrganizationId == organizationId
                && x.ExternalReference == externalReference
                && x.Status == PaymentStatus.Recorded)
            .OrderByDescending(x => x.RecordedAt)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return payments;
    }

    public async Task<IReadOnlyList<PaymentAllocation>> ListAllocationsForReceivableAsync(
        OrganizationId organizationId,
        ReceivableId receivableId,
        CancellationToken cancellationToken = default)
    {
        List<PaymentAllocation> allocations = await _context.PaymentAllocations
            .Where(x => x.OrganizationId == organizationId && x.ReceivableId == receivableId)
            .OrderBy(x => x.AppliedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return allocations;
    }

    public void Add(Payment payment) => _context.Payments.Add(payment);
}

/// <summary>Loads and stores commission rules and what they entitle the agency to.</summary>
public sealed class CommissionRepository : ICommissionRepository
{
    private readonly AgencyOsDbContext _context;

    public CommissionRepository(AgencyOsDbContext context) => _context = context;

    public Task<CommissionRule?> FindRuleAsync(
        OrganizationId organizationId,
        CommissionRuleId id,
        CancellationToken cancellationToken = default) =>
        _context.CommissionRules.FirstOrDefaultAsync(
            rule => rule.OrganizationId == organizationId && rule.Id == id,
            cancellationToken);

    public async Task<IReadOnlyList<CommissionRule>> ListRulesForClientAsync(
        OrganizationId organizationId,
        Guid clientPersonId,
        CancellationToken cancellationToken = default)
    {
        List<CommissionRule> rules = await _context.CommissionRules
            .Where(x => x.OrganizationId == organizationId && x.ClientPersonId == clientPersonId)
            .OrderBy(x => x.EffectiveFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rules;
    }

    public Task<CommissionEntitlement?> FindEntitlementAsync(
        OrganizationId organizationId,
        CommissionEntitlementId id,
        CancellationToken cancellationToken = default) =>
        _context.CommissionEntitlements
            .Include(entitlement => entitlement.Adjustments)
            .FirstOrDefaultAsync(
                entitlement => entitlement.OrganizationId == organizationId && entitlement.Id == id,
                cancellationToken);

    public async Task<IReadOnlyList<CommissionEntitlement>> ListEntitlementsForObligationAsync(
        OrganizationId organizationId,
        MonetaryObligationId obligationId,
        CancellationToken cancellationToken = default)
    {
        List<CommissionEntitlement> entitlements = await _context.CommissionEntitlements
            .Include(x => x.Adjustments)
            .Where(x => x.OrganizationId == organizationId
                && x.MonetaryObligationId == obligationId)
            .OrderBy(x => x.CalculatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entitlements;
    }

    public void AddRule(CommissionRule rule) => _context.CommissionRules.Add(rule);

    public void AddEntitlement(CommissionEntitlement entitlement) =>
        _context.CommissionEntitlements.Add(entitlement);
}

/// <summary>Loads and stores the ledger.</summary>
public sealed class LedgerRepository : ILedgerRepository
{
    private readonly AgencyOsDbContext _context;

    public LedgerRepository(AgencyOsDbContext context) => _context = context;

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        // Reads the tracked set as well as the table, so a chart seeded earlier in
        // the same transaction is visible to the posting that needed it.
        List<Account> accounts = await _context.Accounts
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (accounts.Count > 0)
        {
            return accounts;
        }

        Account[] pending =
        [
            .. _context.ChangeTracker
                .Entries<Account>()
                .Where(entry => entry.Entity.OrganizationId == organizationId)
                .Select(entry => entry.Entity),
        ];

        return pending;
    }

    public Task<JournalEntry?> FindEntryAsync(
        OrganizationId organizationId,
        JournalEntryId id,
        CancellationToken cancellationToken = default) =>
        _context.JournalEntries
            .Include(entry => entry.Lines)
            .FirstOrDefaultAsync(
                entry => entry.OrganizationId == organizationId && entry.Id == id,
                cancellationToken);

    /// <summary>
    /// The posted entry an allocation produced.
    /// </summary>
    /// <remarks>
    /// Looks in the change tracker as well, because an allocation recorded and
    /// reversed in one request would otherwise find nothing: its entry is still
    /// pending.
    /// </remarks>
    public async Task<JournalEntry?> FindEntryForAllocationAsync(
        OrganizationId organizationId,
        PaymentAllocationId allocationId,
        CancellationToken cancellationToken = default)
    {
        JournalEntry? stored = await _context.JournalEntries
            .Include(entry => entry.Lines)
            .Where(entry => entry.OrganizationId == organizationId
                && entry.PaymentAllocationId == allocationId
                && entry.Status == JournalEntryStatus.Posted)
            .OrderBy(entry => entry.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return stored ?? Pending(
            organizationId, entry => entry.PaymentAllocationId == allocationId);
    }

    public async Task<JournalEntry?> FindEntryForReceivableAsync(
        OrganizationId organizationId,
        ReceivableId receivableId,
        JournalSource source,
        CancellationToken cancellationToken = default)
    {
        JournalEntry? stored = await _context.JournalEntries
            .Include(entry => entry.Lines)
            .Where(entry => entry.OrganizationId == organizationId
                && entry.ReceivableId == receivableId
                && entry.Source == source
                && entry.Status == JournalEntryStatus.Posted)
            .OrderBy(entry => entry.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return stored ?? Pending(
            organizationId,
            entry => entry.ReceivableId == receivableId && entry.Source == source);
    }

    public void AddAccounts(IEnumerable<Account> accounts) => _context.Accounts.AddRange(accounts);

    public void AddEntry(JournalEntry entry) => _context.JournalEntries.Add(entry);

    /// <summary>An entry added in this transaction and not yet written.</summary>
    private JournalEntry? Pending(
        OrganizationId organizationId,
        Func<JournalEntry, bool> predicate) =>
        _context.ChangeTracker
            .Entries<JournalEntry>()
            .Select(entry => entry.Entity)
            .Where(entry => entry.OrganizationId == organizationId
                && entry.Status == JournalEntryStatus.Posted)
            .FirstOrDefault(predicate);
}

/// <summary>Records the curated financial history.</summary>
public sealed class FinanceEventRepository : IFinanceEventRepository
{
    private readonly AgencyOsDbContext _context;

    public FinanceEventRepository(AgencyOsDbContext context) => _context = context;

    public void Add(FinanceEvent entry) => _context.FinanceEvents.Add(entry);
}

/// <summary>Records which piece of finance work a task chases.</summary>
public sealed class FinanceTaskLinkRepository : IFinanceTaskLinkRepository
{
    private readonly AgencyOsDbContext _context;

    public FinanceTaskLinkRepository(AgencyOsDbContext context) => _context = context;

    public void Add(FinanceTaskLink link) => _context.FinanceTaskLinks.Add(link);
}
