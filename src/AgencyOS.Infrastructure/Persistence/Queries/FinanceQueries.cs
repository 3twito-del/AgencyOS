using System.Globalization;
using AgencyOS.Application.Finance;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Tasks;
using AgencyOS.Finance.Rules;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projections for the finance model.
/// </summary>
/// <remarks>
/// <para>
/// Every figure that could drift is computed here rather than stored: what a
/// receivable still owes, where it stands, what a payment has left to apply, what
/// commission has actually been earned, what an account holds, and whether a
/// receivable's arithmetic explains itself. There is no balance column to go stale
/// and no status column to disagree with the rows beneath it (ADR-0023).
/// </para>
/// <para>
/// Balances are grouped by currency and never summed across them. Adding dollars
/// to euros produces a number that means nothing, and AgencyOS holds no rate that
/// would make it mean something.
/// </para>
/// <para>
/// Deliberately unauthorized. <see cref="FinanceQueryService"/> applies the
/// tenant-scoped checks, and finance refuses rather than redacts, so there is no
/// per-row filtering here at all.
/// </para>
/// </remarks>
internal sealed class FinanceQueries : IFinanceQueries
{
    private readonly AgencyOsDbContext _context;
    private readonly Application.Abstractions.IClock _clock;

    public FinanceQueries(AgencyOsDbContext context, Application.Abstractions.IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

    // ------------------------------------------------------------ obligations

    public async Task<IReadOnlyList<MonetaryObligationModel>> ListObligationsAsync(
        OrganizationId organizationId,
        ContractId? contractId,
        bool unbilledOnly,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<MonetaryObligation> query = _context.MonetaryObligations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (contractId is { } contract)
        {
            query = query.Where(x => x.ContractId == contract);
        }

        if (unbilledOnly)
        {
            query = query.Where(x => x.Status == MonetaryObligationStatus.Expected);
        }

        List<MonetaryObligation> obligations = await query
            .OrderBy(x => x.ResolvedDueOn ?? DateOnly.MaxValue)
            .ThenByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (obligations.Count == 0)
        {
            return [];
        }

        FinanceContext context = await LoadContextAsync(
            organizationId, [.. obligations.Select(x => x.ContractId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        MonetaryObligationId[] ids = [.. obligations.Select(x => x.Id)];

        HashSet<MonetaryObligationId> billed =
        [
            .. await _context.Receivables
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId
                    && ids.Contains(x.MonetaryObligationId))
                .Select(x => x.MonetaryObligationId)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
        ];

        return [.. obligations.Select(x => ToModel(x, context, billed.Contains(x.Id)))];
    }

    public async Task<MonetaryObligationModel?> GetObligationAsync(
        OrganizationId organizationId,
        MonetaryObligationId id,
        CancellationToken cancellationToken = default)
    {
        MonetaryObligation? obligation = await _context.MonetaryObligations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (obligation is null)
        {
            return null;
        }

        FinanceContext context = await LoadContextAsync(
            organizationId, [obligation.ContractId], cancellationToken).ConfigureAwait(false);

        bool billed = await _context.Receivables
            .AsNoTracking()
            .AnyAsync(
                x => x.OrganizationId == organizationId && x.MonetaryObligationId == id,
                cancellationToken)
            .ConfigureAwait(false);

        return ToModel(obligation, context, billed);
    }

    // ------------------------------------------------------------ receivables

    public async Task<IReadOnlyList<ReceivableModel>> ListReceivablesAsync(
        OrganizationId organizationId,
        ReceivableFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Receivable> query = _context.Receivables
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (filter.ContractId is { } contract)
        {
            query = query.Where(x => x.ContractId == contract);
        }

        if (filter.PayerPartyId is { } payer)
        {
            query = query.Where(x => x.PayerPartyId == payer);
        }

        if (filter.ClientPersonId is { } client)
        {
            query = query.Where(x => x.ClientPersonId == client);
        }

        if (filter.Beneficiary is { } beneficiary)
        {
            query = query.Where(x => x.Beneficiary == beneficiary);
        }

        if (filter.DueAfter is { } after)
        {
            query = query.Where(x => x.DueOn != null && x.DueOn >= after);
        }

        if (filter.DueBefore is { } before)
        {
            query = query.Where(x => x.DueOn != null && x.DueOn <= before);
        }

        if (!string.IsNullOrWhiteSpace(filter.Currency))
        {
            query = query.Where(x => x.CurrencyCodeValue == filter.Currency);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string search = filter.Search.Trim();

            query = query.Where(x =>
                x.Reference != null && EF.Functions.ILike(x.Reference, $"%{search}%"));
        }

        List<Receivable> receivables = await query
            .OrderBy(x => x.DueOn ?? DateOnly.MaxValue)
            .ThenByDescending(x => x.CreatedAt)
            .Take(limit * 3)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (receivables.Count == 0)
        {
            return [];
        }

        FinanceContext context = await LoadContextAsync(
            organizationId, [.. receivables.Select(x => x.ContractId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<ReceivableModel> models = receivables.Select(x => ToModel(x, context));

        // Status and overdue are arithmetic, so they narrow after projection rather
        // than in SQL that would have to hardcode a clock.
        if (filter.Status is { } status)
        {
            models = models.Where(x => x.Status == status);
        }

        if (filter.OverdueOnly)
        {
            models = models.Where(x => x.IsOverdue);
        }

        if (filter.UnreconciledOnly)
        {
            models = models.Where(x => x.Outstanding.Amount > 0m && x.Allocated.Amount > 0m);
        }

        return [.. models.Take(limit)];
    }

    public async Task<ReceivableModel?> GetReceivableAsync(
        OrganizationId organizationId,
        ReceivableId id,
        CancellationToken cancellationToken = default)
    {
        Receivable? receivable = await _context.Receivables
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (receivable is null)
        {
            return null;
        }

        FinanceContext context = await LoadContextAsync(
            organizationId, [receivable.ContractId], cancellationToken).ConfigureAwait(false);

        return ToModel(receivable, context);
    }

    // --------------------------------------------------------------- invoices

    public async Task<IReadOnlyList<InvoiceModel>> ListInvoicesAsync(
        OrganizationId organizationId,
        InvoiceFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Invoice> query = _context.Invoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.OrganizationId == organizationId);

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.ContractId is { } contract)
        {
            query = query.Where(x => x.ContractId == contract);
        }

        if (filter.DebtorPartyId is { } debtor)
        {
            query = query.Where(x => x.DebtorPartyId == debtor);
        }

        if (filter.DueAfter is { } after)
        {
            query = query.Where(x => x.DueOn != null && x.DueOn >= after);
        }

        if (filter.DueBefore is { } before)
        {
            query = query.Where(x => x.DueOn != null && x.DueOn <= before);
        }

        if (!string.IsNullOrWhiteSpace(filter.Currency))
        {
            query = query.Where(x => x.CurrencyCodeValue == filter.Currency);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string search = filter.Search.Trim();

            query = query.Where(x =>
                x.Reference != null && EF.Functions.ILike(x.Reference, $"%{search}%"));
        }

        List<Invoice> invoices = await query
            .OrderByDescending(x => x.IssuedOn ?? DateOnly.MinValue)
            .ThenByDescending(x => x.CreatedAt)
            .Take(limit * 2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (invoices.Count == 0)
        {
            return [];
        }

        FinanceContext context = await LoadContextAsync(
            organizationId, [.. invoices.Select(x => x.ContractId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        Dictionary<ReceivableId, Receivable> billed = await LoadReceivablesAsync(
            organizationId,
            [.. invoices.SelectMany(x => x.Lines).Select(x => x.ReceivableId).Distinct()],
            cancellationToken).ConfigureAwait(false);

        IEnumerable<InvoiceModel> models = invoices.Select(x => ToModel(x, context, billed));

        if (filter.OverdueOnly)
        {
            models = models.Where(x => x.IsOverdue);
        }

        return [.. models.Take(limit)];
    }

    public async Task<InvoiceModel?> GetInvoiceAsync(
        OrganizationId organizationId,
        InvoiceId id,
        CancellationToken cancellationToken = default)
    {
        Invoice? invoice = await _context.Invoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (invoice is null)
        {
            return null;
        }

        FinanceContext context = await LoadContextAsync(
            organizationId, [invoice.ContractId], cancellationToken).ConfigureAwait(false);

        Dictionary<ReceivableId, Receivable> billed = await LoadReceivablesAsync(
            organizationId,
            [.. invoice.Lines.Select(x => x.ReceivableId)],
            cancellationToken).ConfigureAwait(false);

        return ToModel(invoice, context, billed);
    }

    // --------------------------------------------------------------- payments

    public async Task<IReadOnlyList<PaymentModel>> ListPaymentsAsync(
        OrganizationId organizationId,
        PaymentFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Payment> query = _context.Payments
            .AsNoTracking()
            .Include(x => x.Allocations)
            .Where(x => x.OrganizationId == organizationId);

        if (filter.Direction is { } direction)
        {
            query = query.Where(x => x.Direction == direction);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.PayerPartyId is { } payer)
        {
            query = query.Where(x => x.PayerPartyId == payer);
        }

        if (filter.RecordedAfter is { } after)
        {
            query = query.Where(x => x.ReceivedOn >= after);
        }

        if (filter.RecordedBefore is { } before)
        {
            query = query.Where(x => x.ReceivedOn <= before);
        }

        if (!string.IsNullOrWhiteSpace(filter.Currency))
        {
            query = query.Where(x => x.CurrencyCodeValue == filter.Currency);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string search = filter.Search.Trim();

            query = query.Where(x =>
                (x.ExternalReference != null && EF.Functions.ILike(x.ExternalReference, $"%{search}%"))
                || (x.PayerName != null && EF.Functions.ILike(x.PayerName, $"%{search}%")));
        }

        List<Payment> payments = await query
            .OrderByDescending(x => x.ReceivedOn)
            .ThenByDescending(x => x.RecordedAt)
            .Take(limit * 2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (payments.Count == 0)
        {
            return [];
        }

        PaymentDetail detail = await LoadPaymentDetailAsync(
            organizationId, payments, cancellationToken).ConfigureAwait(false);

        IEnumerable<PaymentModel> models = payments.Select(x => ToModel(x, detail));

        // Unapplied is arithmetic over allocation rows, so it narrows after
        // projection rather than in SQL.
        if (filter.UnappliedOnly)
        {
            models = models.Where(x => x.Unapplied.Amount > 0m && x.Status == PaymentStatus.Recorded);
        }

        return [.. models.Take(limit)];
    }

    public async Task<PaymentModel?> GetPaymentAsync(
        OrganizationId organizationId,
        PaymentId id,
        CancellationToken cancellationToken = default)
    {
        Payment? payment = await _context.Payments
            .AsNoTracking()
            .Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return null;
        }

        PaymentDetail detail = await LoadPaymentDetailAsync(
            organizationId, [payment], cancellationToken).ConfigureAwait(false);

        return ToModel(payment, detail);
    }

    // ------------------------------------------------------------ commissions

    public async Task<IReadOnlyList<CommissionRuleModel>> ListCommissionRulesAsync(
        OrganizationId organizationId,
        Guid? clientPersonId,
        ContractId? contractId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<CommissionRule> query = _context.CommissionRules
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (clientPersonId is { } client)
        {
            query = query.Where(x => x.ClientPersonId == client);
        }

        if (contractId is { } contract)
        {
            query = query.Where(x => x.ContractId == contract);
        }

        List<CommissionRule> rules = await query
            .OrderByDescending(x => x.EffectiveFrom)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rules.Count == 0)
        {
            return [];
        }

        FinanceContext context = await LoadContextAsync(
            organizationId,
            [.. rules.Where(x => x.ContractId is not null).Select(x => x.ContractId!.Value)],
            cancellationToken).ConfigureAwait(false);

        DateOnly today = Today;

        return
        [
            .. rules.Select(rule => new CommissionRuleModel(
                rule.Id,
                rule.RepresentationId,
                rule.ClientPersonId,
                context.PersonName(rule.ClientPersonId),
                rule.ContractId,
                rule.ContractId is { } id ? context.ContractTitle(id) : null,
                rule.Basis,
                rule.RatePercent,
                rule.FixedAmountValue is { } fixedAmount && rule.CurrencyCodeValue is { } currency
                    ? new MoneyModel(fixedAmount, currency)
                    : null,
                rule.TermCode,
                rule.EffectiveFrom,
                rule.EffectiveTo,
                rule.GovernsOn(today),
                rule.Provenance,
                rule.Notes,
                rule.Version)),
        ];
    }

    public async Task<IReadOnlyList<CommissionEntitlementModel>> ListCommissionsAsync(
        OrganizationId organizationId,
        CommissionFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<CommissionEntitlement> query = _context.CommissionEntitlements
            .AsNoTracking()
            .Include(x => x.Adjustments)
            .Where(x => x.OrganizationId == organizationId);

        if (filter.ClientPersonId is { } client)
        {
            query = query.Where(x => x.ClientPersonId == client);
        }

        if (filter.ContractId is { } contract)
        {
            query = query.Where(x => x.ContractId == contract);
        }

        if (filter.RepresentationId is { } representation)
        {
            query = query.Where(x => x.RepresentationId == representation);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Currency))
        {
            query = query.Where(x => x.CurrencyCodeValue == filter.Currency);
        }

        List<CommissionEntitlement> entitlements = await query
            .OrderByDescending(x => x.CalculatedAt)
            .Take(limit * 2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entitlements.Count == 0)
        {
            return [];
        }

        CommissionDetail detail = await LoadCommissionDetailAsync(
            organizationId, entitlements, cancellationToken).ConfigureAwait(false);

        IEnumerable<CommissionEntitlementModel> models =
            entitlements.Select(x => ToModel(x, detail));

        if (filter.OutstandingOnly)
        {
            models = models.Where(x => x.Outstanding.Amount > 0m);
        }

        return [.. models.Take(limit)];
    }

    public async Task<CommissionEntitlementModel?> GetCommissionAsync(
        OrganizationId organizationId,
        CommissionEntitlementId id,
        CancellationToken cancellationToken = default)
    {
        CommissionEntitlement? entitlement = await _context.CommissionEntitlements
            .AsNoTracking()
            .Include(x => x.Adjustments)
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entitlement is null)
        {
            return null;
        }

        CommissionDetail detail = await LoadCommissionDetailAsync(
            organizationId, [entitlement], cancellationToken).ConfigureAwait(false);

        return ToModel(entitlement, detail);
    }

    // ----------------------------------------------------------------- ledger

    public async Task<IReadOnlyList<AccountModel>> ListAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        List<Account> accounts = await _context.Accounts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. accounts.Select(x => new AccountModel(
                x.Id, x.Kind, x.Category, x.Code, x.Name, x.Description)),
        ];
    }

    public async Task<IReadOnlyList<JournalEntryModel>> ListJournalEntriesAsync(
        OrganizationId organizationId,
        JournalFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<JournalEntry> query = _context.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.OrganizationId == organizationId);

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Source is { } source)
        {
            query = query.Where(x => x.Source == source);
        }

        if (filter.AccountId is { } account)
        {
            query = query.Where(x => x.Lines.Any(line => line.AccountId == account));
        }

        if (filter.PostedAfter is { } after)
        {
            query = query.Where(x => x.PostingDate >= after);
        }

        if (filter.PostedBefore is { } before)
        {
            query = query.Where(x => x.PostingDate <= before);
        }

        if (!string.IsNullOrWhiteSpace(filter.Currency))
        {
            query = query.Where(x => x.CurrencyCodeValue == filter.Currency);
        }

        List<JournalEntry> entries = await query
            .OrderByDescending(x => x.PostingDate)
            .ThenByDescending(x => x.RecordedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0)
        {
            return [];
        }

        Dictionary<AccountId, Account> accounts = await LoadAccountsAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        return [.. entries.Select(entry => ToModel(entry, accounts, users))];
    }

    public async Task<JournalEntryModel?> GetJournalEntryAsync(
        OrganizationId organizationId,
        JournalEntryId id,
        CancellationToken cancellationToken = default)
    {
        JournalEntry? entry = await _context.JournalEntries
            .AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return null;
        }

        Dictionary<AccountId, Account> accounts = await LoadAccountsAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        return ToModel(entry, accounts, users);
    }

    /// <summary>
    /// What each account holds, per currency.
    /// </summary>
    /// <remarks>
    /// Computed from posted lines every time. There is no running balance column,
    /// because a running balance is a number that can disagree with the entries
    /// that produced it, and reconciling the two afterwards is impossible when
    /// nobody knows which is right (ADR-0023).
    /// </remarks>
    public async Task<IReadOnlyList<AccountBalanceModel>> GetBalancesAsync(
        OrganizationId organizationId,
        string? currency,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.JournalLines
            .AsNoTracking()
            .Where(line => line.OrganizationId == organizationId)
            .Join(
                _context.JournalEntries
                    .AsNoTracking()
                    .Where(entry => entry.Status != JournalEntryStatus.Draft),
                line => line.JournalEntryId,
                entry => entry.Id,
                (line, entry) => new { line.AccountId, line.Side, line.AmountValue, line.CurrencyCodeValue })
            .GroupBy(x => new { x.AccountId, x.CurrencyCodeValue, x.Side })
            .Select(group => new
            {
                group.Key.AccountId,
                group.Key.CurrencyCodeValue,
                group.Key.Side,
                Total = group.Sum(x => x.AmountValue),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(currency))
        {
            rows = [.. rows.Where(x => x.CurrencyCodeValue == currency)];
        }

        Dictionary<AccountId, Account> accounts = await LoadAccountsAsync(
            organizationId, cancellationToken).ConfigureAwait(false);

        List<AccountBalanceModel> balances = [];

        foreach (var group in rows.GroupBy(x => new { x.AccountId, x.CurrencyCodeValue }))
        {
            if (!accounts.TryGetValue(group.Key.AccountId, out Account? account))
            {
                continue;
            }

            decimal debits = group.Where(x => x.Side == JournalSide.Debit).Sum(x => x.Total);
            decimal credits = group.Where(x => x.Side == JournalSide.Credit).Sum(x => x.Total);

            // The one place the debit-and-credit convention is applied, and it is
            // the kernel's function rather than a repeat of it here.
            decimal balance =
                FinanceRules.SignedAmount((int)account.Category, (int)JournalSide.Debit, debits)
                + FinanceRules.SignedAmount((int)account.Category, (int)JournalSide.Credit, credits);

            string code = group.Key.CurrencyCodeValue;

            balances.Add(new AccountBalanceModel(
                account.Id,
                account.Kind,
                account.Code,
                account.Name,
                account.Category,
                code,
                new MoneyModel(debits, code),
                new MoneyModel(credits, code),
                new MoneyModel(balance, code)));
        }

        return [.. balances.OrderBy(x => x.Code).ThenBy(x => x.Currency)];
    }

    // --------------------------------------------------------- reconciliation

    public async Task<ReconciliationModel?> ReconcileAsync(
        OrganizationId organizationId,
        ReceivableId id,
        CancellationToken cancellationToken = default)
    {
        Receivable? receivable = await _context.Receivables
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (receivable is null)
        {
            return null;
        }

        List<PaymentAllocation> allocations = await _context.PaymentAllocations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ReceivableId == id)
            .OrderBy(x => x.AppliedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PaymentAdjustment> adjustments = await _context.PaymentAdjustments
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ReceivableId == id)
            .OrderBy(x => x.OccurredOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        FinanceContext context = await LoadContextAsync(
            organizationId, [receivable.ContractId], cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        decimal allocated = allocations.Where(x => x.IsApplied).Sum(x => x.AmountValue);

        // Write-offs are counted separately from ordinary deductions, because
        // "we agreed to take less" and "we gave up" are different explanations.
        decimal deductions = adjustments
            .Where(x => x.IsApplied && x.Kind != PaymentAdjustmentKind.WriteOff)
            .Sum(x => x.AmountValue);

        decimal writtenOff = adjustments
            .Where(x => x.IsApplied && x.Kind == PaymentAdjustmentKind.WriteOff)
            .Sum(x => x.AmountValue);

        string currency = receivable.CurrencyCodeValue;

        ReconciliationInput input = new()
        {
            Expected = receivable.OriginalAmountValue,
            Allocated = allocated,
            Deductions = deductions,
            WrittenOff = writtenOff,
            Currency = currency,
        };

        return new ReconciliationModel(
            receivable.Id,
            receivable.Reference,
            receivable.ContractId,
            context.ContractTitle(receivable.ContractId),
            new MoneyModel(receivable.OriginalAmountValue, currency),
            new MoneyModel(allocated, currency),
            new MoneyModel(deductions, currency),
            new MoneyModel(writtenOff, currency),
            new MoneyModel(FinanceRules.Variance(input), currency),
            FinanceRules.VarianceDirection(input),
            ((ReconciliationOutcome)FinanceRules.ReconciliationOutcome(input)).ToString(),

            // The arithmetic in a sentence, and no guess about what the gap was.
            FinanceRules.DescribeReconciliation(input),

            [.. adjustments.Select(x => ToModel(x, users))],
            [.. allocations.Select(x => ToModel(x, receivable, context, users))]);
    }

    // ---------------------------------------------------------------- history

    public async Task<IReadOnlyList<FinanceHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        ContractId? contractId,
        ReceivableId? receivableId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<FinanceEvent> query = _context.FinanceEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (contractId is { } contract)
        {
            query = query.Where(x => x.ContractId == contract);
        }

        if (receivableId is { } receivable)
        {
            query = query.Where(x => x.ReceivableId == receivable);
        }

        List<FinanceEvent> events = await query
            .OrderByDescending(x => x.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. events.Select(entry => new FinanceHistoryEntryModel(
                entry.OccurredAt,
                entry.Kind.ToString(),
                entry.Summary,
                entry.AmountValue is { } amount && entry.CurrencyCodeValue is { } currency
                    ? new MoneyModel(amount, currency)
                    : null,
                entry.Detail,
                Name(users, entry.RecordedBy))),
        ];
    }

    // --------------------------------------------------------- command centre

    public async Task<FinanceCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        IReadOnlyList<ReceivableModel> overdue = await ListReceivablesAsync(
            organizationId, new ReceivableFilter(OverdueOnly: true), 50, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ReceivableModel> dueSoon = await ListReceivablesAsync(
            organizationId,
            new ReceivableFilter(DueAfter: today, DueBefore: today.AddDays(30)),
            50,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PaymentModel> unapplied = await ListPaymentsAsync(
            organizationId, new PaymentFilter(UnappliedOnly: true), 50, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<CommissionEntitlementModel> commission = await ListCommissionsAsync(
            organizationId,
            new CommissionFilter(
                Status: CommissionEntitlementStatus.Calculated, OutstandingOnly: true),
            50,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<MonetaryObligationModel> unbilled = await ListObligationsAsync(
            organizationId, null, unbilledOnly: true, 50, cancellationToken).ConfigureAwait(false);

        // Only obligations that could actually be billed. One whose amount is
        // contingent or unknown is not waiting for somebody to raise a receivable;
        // it is waiting for the world (ADR-0023).
        unbilled = [.. unbilled.Where(x => x.IsQuantified && !x.HasReceivable)];

        List<ReconciliationModel> variances = [];

        foreach (ReceivableModel candidate in overdue.Concat(dueSoon).Take(25))
        {
            ReconciliationModel? reconciliation = await ReconcileAsync(
                organizationId, candidate.Id, cancellationToken).ConfigureAwait(false);

            if (reconciliation is { Outcome: not nameof(ReconciliationOutcome.Reconciled) }
                && reconciliation.Allocated.Amount > 0m)
            {
                variances.Add(reconciliation);
            }
        }

        IReadOnlyList<FinanceTaskModel> tasks = await LoadOverdueTasksAsync(
            organizationId, now, cancellationToken).ConfigureAwait(false);

        return new FinanceCommandCenterModel(
            overdue,
            dueSoon,
            unapplied,
            variances,
            commission,
            unbilled,
            tasks,

            // Grouped by currency, never summed across them.
            ByCurrency(overdue.Select(x => x.Outstanding)),
            ByCurrency(unapplied.Select(x => x.Unapplied)),
            ByCurrency(commission.Select(x => x.Outstanding)),
            overdue.Count,
            unapplied.Count);
    }

    // ------------------------------------------------------------ projections

    private static MonetaryObligationModel ToModel(
        MonetaryObligation obligation,
        FinanceContext context,
        bool billed) =>
        new(
            obligation.Id,
            obligation.ContractId,
            context.ContractTitle(obligation.ContractId),
            obligation.ContractVersionId,
            obligation.SourceObligationId,
            obligation.SourceTermCode,
            obligation.PayerPartyId,
            context.PartyName(obligation.PayerPartyId),
            obligation.PayeePartyId,
            context.PartyName(obligation.PayeePartyId),
            obligation.Category,
            obligation.AmountKind,
            obligation.Amount is { } amount
                ? new MoneyModel(amount.Amount, amount.Currency.Value)
                : null,
            obligation.Quantity,
            obligation.UnitAmountValue is { } unit && obligation.CurrencyCodeValue is { } currency
                ? new MoneyModel(unit, currency)
                : null,
            obligation.Condition,
            obligation.ResolvedDueOn,

            // Present exactly when the date is not, and it names which of the three
            // honest reasons applies (ADR-0022).
            obligation.ResolvedDueOn is null ? obligation.Due.WhyUnresolved(null) : null,

            obligation.Due.Description,
            obligation.Status,
            obligation.IsQuantified,
            billed,
            obligation.Description,
            obligation.Notes,
            obligation.Version);

    private ReceivableModel ToModel(Receivable receivable, FinanceContext context)
    {
        string currency = receivable.CurrencyCodeValue;

        return new ReceivableModel(
            receivable.Id,
            receivable.MonetaryObligationId,
            receivable.ContractId,
            context.ContractTitle(receivable.ContractId),
            receivable.PayerPartyId,
            context.PartyName(receivable.PayerPartyId),
            receivable.Beneficiary,
            receivable.ClientPersonId,
            receivable.ClientPersonId is { } client ? context.PersonName(client) : null,
            new MoneyModel(receivable.OriginalAmountValue, currency),
            new MoneyModel(receivable.AllocatedAmountValue, currency),
            new MoneyModel(receivable.AdjustedAmountValue, currency),
            new MoneyModel(receivable.Outstanding.Amount, currency),
            receivable.DueOn,
            receivable.Status,
            receivable.IsOverdueOn(Today),
            receivable.Reference,
            receivable.ClosureReason,
            receivable.Notes,
            receivable.CreatedAt,
            receivable.Version);
    }

    private InvoiceModel ToModel(
        Invoice invoice,
        FinanceContext context,
        Dictionary<ReceivableId, Receivable> billed)
    {
        string currency = invoice.CurrencyCodeValue;

        // What the invoice still expects: the outstanding balances of what it bills,
        // capped at what it actually billed on each line.
        decimal outstanding = invoice.Lines.Sum(line =>
            billed.TryGetValue(line.ReceivableId, out Receivable? receivable)
                ? Math.Min(line.AmountValue, receivable.Outstanding.Amount)
                : line.AmountValue);

        return new InvoiceModel(
            invoice.Id,
            invoice.Reference,
            invoice.ContractId,
            context.ContractTitle(invoice.ContractId),
            invoice.DebtorPartyId,
            context.PartyName(invoice.DebtorPartyId),
            invoice.Status,
            invoice.IssuedOn,
            invoice.DueOn,
            new MoneyModel(invoice.Total.Amount, currency),
            new MoneyModel(outstanding, currency),
            invoice.Status == InvoiceStatus.Issued
                && invoice.DueOn is { } due
                && due < Today
                && outstanding > 0m,

            // Stated rather than assumed. M9 records that an invoice exists and
            // where it lives; the document itself is M10's (ADR-0023).
            false,

            invoice.ExternalReference,
            invoice.VoidReason,
            invoice.Notes,
            [
                .. invoice.Lines.OrderBy(line => line.Sequence).Select(line => new InvoiceLineModel(
                    line.Id,
                    line.ReceivableId,
                    new MoneyModel(line.AmountValue, line.CurrencyCodeValue),
                    line.Description,
                    line.Sequence)),
            ],
            invoice.CreatedAt,
            invoice.Version);
    }

    private static PaymentModel ToModel(Payment payment, PaymentDetail detail)
    {
        string currency = payment.CurrencyCodeValue;

        return new PaymentModel(
            payment.Id,
            payment.Direction,
            payment.PayerPartyId,
            payment.PayerPartyId is { } payer
                ? detail.Context.PartyName(payer)
                : payment.PayerName ?? "(unknown)",
            payment.PayeePartyId,
            payment.PayeePartyId is { } payee ? detail.Context.PartyName(payee) : payment.PayeeName,
            new MoneyModel(payment.AmountValue, currency),
            new MoneyModel(payment.Allocated.Amount, currency),
            new MoneyModel(payment.Unapplied.Amount, currency),
            payment.ReceivedOn,
            payment.RecordedAt,
            payment.Method,
            payment.ExternalReference,
            payment.SourceSystem,
            payment.Status,
            payment.ReversedByPaymentId,
            payment.ReversalOfPaymentId,
            payment.ReversalReason,
            Name(detail.Users, payment.RecordedBy),
            payment.Notes,
            [
                .. payment.Allocations
                    .OrderBy(x => x.AppliedAt)
                    .Select(allocation => ToModel(
                        allocation,
                        detail.Receivables.GetValueOrDefault(allocation.ReceivableId),
                        detail.Context,
                        detail.Users)),
            ],
            payment.Version);
    }

    private static PaymentAllocationModel ToModel(
        PaymentAllocation allocation,
        Receivable? receivable,
        FinanceContext context,
        Dictionary<Guid, string> users) =>
        new(
            allocation.Id,
            allocation.PaymentId,
            allocation.ReceivableId,
            receivable?.Reference,
            receivable is null ? "(unknown)" : context.ContractTitle(receivable.ContractId),
            new MoneyModel(allocation.AmountValue, allocation.CurrencyCodeValue),
            allocation.IsApplied,
            allocation.AppliedAt,
            Name(users, allocation.AppliedBy),
            allocation.ReversedAt,
            allocation.ReversalReason);

    private static PaymentAdjustmentModel ToModel(
        PaymentAdjustment adjustment,
        Dictionary<Guid, string> users) =>
        new(
            adjustment.Id,
            adjustment.ReceivableId,
            adjustment.PaymentId,
            adjustment.Kind,
            new MoneyModel(adjustment.AmountValue, adjustment.CurrencyCodeValue),
            adjustment.Description,
            adjustment.ExternalReference,
            adjustment.OccurredOn,
            adjustment.IsApplied,
            Name(users, adjustment.RecordedBy));

    /// <summary>
    /// Projects an entitlement, with what has actually been collected worked out.
    /// </summary>
    /// <remarks>
    /// Collected is the snapshotted rate applied to what has arrived against the
    /// client receivable, not a proportion of the entitlement. Three numbers that
    /// are genuinely three numbers (ADR-0023).
    /// </remarks>
    private static CommissionEntitlementModel ToModel(
        CommissionEntitlement entitlement,
        CommissionDetail detail)
    {
        string currency = entitlement.CurrencyCodeValue;

        decimal collectedBasis = detail.CollectedBasis.GetValueOrDefault(entitlement.Id, 0m);

        Domain.Deals.Money collected =
            entitlement.CollectedAgainst(Domain.Deals.Money.Create(collectedBasis, currency));

        Domain.Deals.Money outstanding = entitlement.OutstandingAgainst(collected);

        return new CommissionEntitlementModel(
            entitlement.Id,
            entitlement.MonetaryObligationId,
            entitlement.ContractId,
            detail.Context.ContractTitle(entitlement.ContractId),
            entitlement.ClientPersonId,
            detail.Context.PersonName(entitlement.ClientPersonId),
            entitlement.RepresentationId,
            entitlement.CommissionRuleId,
            entitlement.ClientReceivableId,
            entitlement.Basis,
            entitlement.RatePercentSnapshot,
            new MoneyModel(entitlement.BasisAmountValue, currency),
            new MoneyModel(entitlement.EntitledAmountValue, currency),
            new MoneyModel(collected.Amount, currency),
            new MoneyModel(entitlement.AdjustedAmount.Amount, currency),
            new MoneyModel(outstanding.Amount, currency),
            entitlement.GoverningOn,
            entitlement.Status,
            [
                .. entitlement.Adjustments.Select(adjustment => new CommissionAdjustmentModel(
                    adjustment.Id,
                    adjustment.Kind,
                    new MoneyModel(adjustment.AmountValue, adjustment.CurrencyCodeValue),
                    adjustment.Reason,
                    adjustment.RecordedAt,
                    Name(detail.Users, adjustment.RecordedBy))),
            ],
            entitlement.CalculatedAt,
            Name(detail.Users, entitlement.CalculatedBy),
            entitlement.Notes,
            entitlement.Version);
    }

    private static JournalEntryModel ToModel(
        JournalEntry entry,
        Dictionary<AccountId, Account> accounts,
        Dictionary<Guid, string> users)
    {
        string currency = entry.CurrencyCodeValue;

        return new JournalEntryModel(
            entry.Id,
            entry.Status,
            entry.Source,
            entry.Memo,
            currency,
            new MoneyModel(entry.Debits.Amount, currency),
            new MoneyModel(entry.Credits.Amount, currency),
            entry.IsBalanced,
            entry.OccurredOn,
            entry.PostingDate,
            entry.RecordedAt,
            entry.PostedAt,
            entry.PostedBy is { } poster ? Name(users, poster) : null,
            entry.ReceivableId,
            entry.PaymentId,
            entry.CommissionEntitlementId,
            entry.ReversalOfEntryId,
            entry.ReversedByEntryId,
            entry.ReversalReason,
            [
                .. entry.Lines.OrderBy(line => line.Sequence).Select(line => new JournalLineModel(
                    line.Id,
                    line.AccountId,
                    accounts.TryGetValue(line.AccountId, out Account? account) ? account.Code : "?",
                    account is null ? "(unknown account)" : account.Name,
                    line.Side,
                    new MoneyModel(line.AmountValue, line.CurrencyCodeValue),
                    line.Sequence,
                    line.Memo)),
            ],
            entry.Version);
    }

    /// <summary>Totals by currency, because there is no honest way to add two.</summary>
    private static IReadOnlyList<CurrencyTotalModel> ByCurrency(IEnumerable<MoneyModel> amounts) =>
    [
        .. amounts
            .GroupBy(x => x.Currency)
            .Select(group => new CurrencyTotalModel(
                group.Key,
                new MoneyModel(group.Sum(x => x.Amount), group.Key),
                group.Count()))
            .OrderBy(x => x.Currency),
    ];

    // ------------------------------------------------------------------ loading

    private static string? Name(Dictionary<Guid, string> users, UserId id) =>
        users.TryGetValue(id.Value, out string? name) ? name : null;

    private Task<Dictionary<Guid, string>> LoadUserNamesAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        _context.Memberships
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { Id = u.Id.Value, u.DisplayName })
            .Distinct()
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);

    private async Task<Dictionary<AccountId, Account>> LoadAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        List<Account> accounts = await _context.Accounts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return accounts.ToDictionary(x => x.Id);
    }

    private async Task<Dictionary<ReceivableId, Receivable>> LoadReceivablesAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<ReceivableId> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        ReceivableId[] wanted = [.. ids];

        List<Receivable> receivables = await _context.Receivables
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && wanted.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return receivables.ToDictionary(x => x.Id);
    }

    private async Task<PaymentDetail> LoadPaymentDetailAsync(
        OrganizationId organizationId,
        IReadOnlyList<Payment> payments,
        CancellationToken cancellationToken)
    {
        Dictionary<ReceivableId, Receivable> receivables = await LoadReceivablesAsync(
            organizationId,
            [.. payments.SelectMany(x => x.Allocations).Select(x => x.ReceivableId).Distinct()],
            cancellationToken).ConfigureAwait(false);

        FinanceContext context = await LoadContextAsync(
            organizationId,
            [.. receivables.Values.Select(x => x.ContractId).Distinct()],
            cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return new PaymentDetail(receivables, context, users);
    }

    /// <summary>
    /// Everything a batch of entitlements needs, including what has been collected.
    /// </summary>
    /// <remarks>
    /// The collected basis is the applied allocations against the client receivable
    /// each entitlement is attached to. One query for all of them rather than one
    /// per entitlement.
    /// </remarks>
    private async Task<CommissionDetail> LoadCommissionDetailAsync(
        OrganizationId organizationId,
        IReadOnlyList<CommissionEntitlement> entitlements,
        CancellationToken cancellationToken)
    {
        // An entitlement is calculated against an obligation, and the receivable
        // that collects it is usually raised afterwards. Reading the collected
        // basis only from an explicit link would report nothing collected on every
        // entitlement calculated in the ordinary order, while the ledger already
        // held the revenue - a stored answer contradicting the arithmetic beneath
        // it, which is the failure M9 exists to avoid (ADR-0023).
        MonetaryObligationId[] obligationIds =
            [.. entitlements.Select(x => x.MonetaryObligationId).Distinct()];

        List<ReceivableLink> links = await _context.Receivables
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.Beneficiary == ReceivableBeneficiary.Client
                && obligationIds.Contains(x.MonetaryObligationId))
            .Select(x => new ReceivableLink(x.Id, x.MonetaryObligationId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ReceivableId[] receivableIds =
        [
            .. links
                .Select(x => x.ReceivableId)
                .Concat(entitlements
                    .Where(x => x.ClientReceivableId is not null)
                    .Select(x => x.ClientReceivableId!.Value))
                .Distinct(),
        ];

        Dictionary<ReceivableId, decimal> collected = receivableIds.Length == 0
            ? []
            : (await _context.PaymentAllocations
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId
                        && x.IsApplied
                        && receivableIds.Contains(x.ReceivableId))
                    .GroupBy(x => x.ReceivableId)
                    .Select(group => new
                    {
                        ReceivableId = group.Key,
                        Total = group.Sum(x => x.AmountValue),
                    })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToDictionary(x => x.ReceivableId, x => x.Total);

        // An explicit link narrows to that one receivable. Without one, every
        // client receivable raised from the obligation counts, which is what the
        // posting path already does when it moves earned commission into revenue.
        Dictionary<CommissionEntitlementId, decimal> byEntitlement = entitlements.ToDictionary(
            x => x.Id,
            x => x.ClientReceivableId is { } linked
                ? collected.GetValueOrDefault(linked, 0m)
                : links
                    .Where(link => link.MonetaryObligationId == x.MonetaryObligationId)
                    .Sum(link => collected.GetValueOrDefault(link.ReceivableId, 0m)));

        FinanceContext context = await LoadContextAsync(
            organizationId, [.. entitlements.Select(x => x.ContractId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> users = await LoadUserNamesAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        return new CommissionDetail(byEntitlement, context, users);
    }

    private async Task<IReadOnlyList<FinanceTaskModel>> LoadOverdueTasksAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await _context.FinanceTaskLinks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _context.Tasks.AsNoTracking()
                    .Where(t => t.State == TaskState.Open && t.DueAt != null && t.DueAt < now),
                link => link.TaskItemId,
                task => task.Id,
                (link, task) => new
                {
                    TaskId = task.Id.Value,
                    task.Title,
                    task.State,
                    task.Priority,
                    task.DueAt,
                    link.ReceivableId,
                    link.InvoiceId,
                    link.PaymentId,
                })
            .OrderBy(x => x.DueAt)
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(x => new FinanceTaskModel(
                x.TaskId,
                x.Title,
                x.State.ToString(),
                x.Priority.ToString(),
                x.DueAt,
                x.ReceivableId,
                x.InvoiceId,
                x.PaymentId)),
        ];
    }

    /// <summary>
    /// Contract titles and party names for a batch, loaded once.
    /// </summary>
    /// <remarks>
    /// The party name is read through the M8 contract party rather than copied onto
    /// the finance row, on the M7 and M8 precedent: a copy would be a second answer
    /// that stops agreeing the moment somebody corrects the party.
    /// </remarks>
    private async Task<FinanceContext> LoadContextAsync(
        OrganizationId organizationId,
        IReadOnlyList<ContractId> contractIds,
        CancellationToken cancellationToken)
    {
        ContractId[] ids = [.. contractIds.Distinct()];

        Dictionary<ContractId, string> contracts = ids.Length == 0
            ? []
            : await _context.Contracts
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Title, cancellationToken)
                .ConfigureAwait(false);

        List<ContractParty> parties = ids.Length == 0
            ? []
            : await _context.ContractParties
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && ids.Contains(x.ContractId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        Dictionary<Guid, string> people = await _context.People
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, string> companies = await _context.Companies
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { Id = x.Id.Value, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            .ConfigureAwait(false);

        return new FinanceContext(contracts, parties.ToDictionary(x => x.Id), people, companies);
    }

    /// <summary>Names, resolved once per request.</summary>
    private sealed record FinanceContext(
        Dictionary<ContractId, string> Contracts,
        Dictionary<Guid, ContractParty> Parties,
        Dictionary<Guid, string> People,
        Dictionary<Guid, string> Companies)
    {
        public string ContractTitle(ContractId id) =>
            Contracts.TryGetValue(id, out string? title) ? title : "(unknown)";

        public string PersonName(Guid id) =>
            People.TryGetValue(id, out string? name) ? name : "(unknown)";

        public string PartyName(Guid partyId)
        {
            if (!Parties.TryGetValue(partyId, out ContractParty? party))
            {
                return "(unknown party)";
            }

            if (party.CompanyId is { } company)
            {
                return Companies.TryGetValue(company.Value, out string? name) ? name : "(unknown)";
            }

            if (party.PersonId is { } person)
            {
                return People.TryGetValue(person.Value, out string? name) ? name : "(unknown)";
            }

            return party.ExternalName ?? "(unnamed party)";
        }
    }

    private sealed record PaymentDetail(
        Dictionary<ReceivableId, Receivable> Receivables,
        FinanceContext Context,
        Dictionary<Guid, string> Users);

    /// <summary>A client receivable and the obligation it was raised from.</summary>
    private sealed record ReceivableLink(
        ReceivableId ReceivableId,
        MonetaryObligationId MonetaryObligationId);

    private sealed record CommissionDetail(
        Dictionary<CommissionEntitlementId, decimal> CollectedBasis,
        FinanceContext Context,
        Dictionary<Guid, string> Users);
}
