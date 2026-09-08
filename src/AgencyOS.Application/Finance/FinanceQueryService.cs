using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Finance;

/// <summary>
/// Authorizes every read of the finance model.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Finance refuses rather than redacts</strong>, and that is the one place
/// M9 departs from the pattern M7 and M8 established. Removing rows from a list of
/// contract terms leaves a shorter list, which is honest. Removing rows from an
/// arithmetic report leaves a <em>wrong answer presented as a right one</em>: a
/// balance with three allocations hidden is not a partial view of the balance, it
/// is a different number, and somebody will act on it (ADR-0023).
/// </para>
/// <para>
/// So every method here authorizes and then returns everything, or authorizes and
/// returns nothing at all. There is no partially-visible receivable, no
/// half-redacted ledger and no commission figure with the rate removed.
/// </para>
/// <para>
/// The grants are disjoint from the commercial ones by design. Holding
/// <c>deals.economics.read</c> - which lets somebody see what a deal pays - confers
/// no finance permission whatsoever. Knowing what was negotiated and knowing what
/// the agency has collected are different disclosures with different readerships.
/// </para>
/// </remarks>
public sealed class FinanceQueryService
{
    private const int MaximumLimit = 200;
    private const int DefaultLimit = 50;
    private const int HistoryLimit = 200;

    private readonly IFinanceQueries _queries;
    private readonly TenantGuard _guard;
    private readonly IClock _clock;

    public FinanceQueryService(IFinanceQueries queries, TenantGuard guard, IClock clock)
    {
        _queries = queries;
        _guard = guard;
        _clock = clock;
    }

    // ------------------------------------------------------------ obligations

    public async Task<IReadOnlyList<MonetaryObligationModel>> ListObligationsAsync(
        OrganizationId organizationId,
        ContractId? contractId = null,
        bool unbilledOnly = false,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.FinanceRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListObligationsAsync(
                organizationId, contractId, unbilledOnly, Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MonetaryObligationModel?> GetObligationAsync(
        OrganizationId organizationId,
        MonetaryObligationId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.FinanceRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.GetObligationAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------ receivables

    public async Task<IReadOnlyList<ReceivableModel>> ListReceivablesAsync(
        OrganizationId organizationId,
        ReceivableFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.FinanceRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListReceivablesAsync(
                organizationId, filter ?? new ReceivableFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ReceivableModel?> GetReceivableAsync(
        OrganizationId organizationId,
        ReceivableId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.FinanceRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.GetReceivableAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);
    }

    // --------------------------------------------------------------- invoices

    public async Task<IReadOnlyList<InvoiceModel>> ListInvoicesAsync(
        OrganizationId organizationId,
        InvoiceFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.FinanceRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListInvoicesAsync(
                organizationId, filter ?? new InvoiceFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<InvoiceModel?> GetInvoiceAsync(
        OrganizationId organizationId,
        InvoiceId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.FinanceRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.GetInvoiceAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);
    }

    // --------------------------------------------------------------- payments

    /// <summary>
    /// Payments and how they were applied.
    /// </summary>
    /// <remarks>
    /// Behind its own grant. Knowing that a hundred thousand is owed and knowing
    /// that eighty of it arrived last Tuesday are different disclosures, and the
    /// second names bank movements.
    /// </remarks>
    public async Task<IReadOnlyList<PaymentModel>> ListPaymentsAsync(
        OrganizationId organizationId,
        PaymentFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.FinancePaymentsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListPaymentsAsync(
                organizationId, filter ?? new PaymentFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PaymentModel?> GetPaymentAsync(
        OrganizationId organizationId,
        PaymentId id,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.FinancePaymentsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.GetPaymentAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------ commissions

    /// <summary>
    /// What the agency earns from a client.
    /// </summary>
    /// <remarks>
    /// The most sensitive number in the relationship, behind its own grant rather
    /// than folded into general finance access.
    /// </remarks>
    public async Task<IReadOnlyList<CommissionRuleModel>> ListCommissionRulesAsync(
        OrganizationId organizationId,
        Guid? clientPersonId = null,
        ContractId? contractId = null,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.FinanceCommissionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListCommissionRulesAsync(organizationId, clientPersonId, contractId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CommissionEntitlementModel>> ListCommissionsAsync(
        OrganizationId organizationId,
        CommissionFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.FinanceCommissionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListCommissionsAsync(
                organizationId, filter ?? new CommissionFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CommissionEntitlementModel?> GetCommissionAsync(
        OrganizationId organizationId,
        CommissionEntitlementId id,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.FinanceCommissionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.GetCommissionAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);
    }

    // ----------------------------------------------------------------- ledger

    public async Task<IReadOnlyList<AccountModel>> ListAccountsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.FinanceLedgerRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.ListAccountsAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JournalEntryModel>> ListJournalEntriesAsync(
        OrganizationId organizationId,
        JournalFilter filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.FinanceLedgerRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .ListJournalEntriesAsync(
                organizationId, filter ?? new JournalFilter(), Clamp(limit), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JournalEntryModel?> GetJournalEntryAsync(
        OrganizationId organizationId,
        JournalEntryId id,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.FinanceLedgerRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.GetJournalEntryAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// What each account holds, per currency.
    /// </summary>
    /// <remarks>
    /// Never summed across currencies. Adding dollars to euros produces a number
    /// that means nothing, and AgencyOS holds no rate that would make it mean
    /// something (ADR-0023).
    /// </remarks>
    public async Task<IReadOnlyList<AccountBalanceModel>> GetBalancesAsync(
        OrganizationId organizationId,
        string? currency = null,
        CancellationToken cancellationToken = default)
    {
        await _guard
            .AuthorizeAsync(Permission.FinanceLedgerRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.GetBalancesAsync(organizationId, currency, cancellationToken)
            .ConfigureAwait(false);
    }

    // --------------------------------------------------------- reconciliation

    /// <summary>
    /// What arrived against what was expected.
    /// </summary>
    /// <remarks>
    /// Requires <c>finance.payments.read</c> as well as <c>finance.read</c>, and
    /// refuses without either. The whole content of a reconciliation is the
    /// relationship between an expectation and a set of bank movements; producing
    /// one with the movements removed would report a variance that is not the
    /// variance.
    /// </remarks>
    public async Task<ReconciliationModel?> ReconcileAsync(
        OrganizationId organizationId,
        ReceivableId id,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.FinanceRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        await _guard
            .AuthorizeAsync(Permission.FinancePaymentsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries.ReconcileAsync(organizationId, id, cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- history

    /// <summary>
    /// The curated financial history.
    /// </summary>
    /// <remarks>
    /// One business act, one entry, however many rows it wrote. Never raw audit
    /// rows, which answer a security question in a security vocabulary (ADR-0012).
    /// </remarks>
    public async Task<IReadOnlyList<FinanceHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        ContractId? contractId = null,
        ReceivableId? receivableId = null,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.FinanceRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return await _queries
            .GetHistoryAsync(organizationId, contractId, receivableId, HistoryLimit, cancellationToken)
            .ConfigureAwait(false);
    }

    // --------------------------------------------------------- command centre

    /// <summary>
    /// What the finance desk has to look at.
    /// </summary>
    /// <remarks>
    /// Requires both <c>finance.read</c> and <c>finance.payments.read</c>, because
    /// half of it is about money that has arrived. A partial command centre would
    /// be a work queue with items silently missing, which is worse than none.
    /// </remarks>
    public async Task<FinanceCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        await _guard.AuthorizeAsync(Permission.FinanceRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        await _guard
            .AuthorizeAsync(Permission.FinancePaymentsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        FinanceCommandCenterModel centre = await _queries
            .GetCommandCenterAsync(organizationId, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        // Commission is the one section behind a further grant. A desk that can
        // chase receivables but may not see what the agency earns gets the rest of
        // the queue with that section empty rather than a refusal for the whole
        // page - the only place M9 removes rather than refuses, and it removes a
        // whole section rather than rows inside an arithmetic.
        bool mayReadCommission = await _guard
            .HasPermissionAsync(Permission.FinanceCommissionsRead, organizationId, cancellationToken)
            .ConfigureAwait(false);

        return mayReadCommission
            ? centre
            : centre with
            {
                UncollectedCommission = [],
                CommissionOutstandingByCurrency = [],
            };
    }

    private static int Clamp(int? limit) =>
        limit is not { } value ? DefaultLimit : Math.Clamp(value, 1, MaximumLimit);
}
