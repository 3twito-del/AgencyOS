using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Finance;

/// <summary>
/// Turns business acts into balanced journal entries.
/// </summary>
/// <remarks>
/// <para>
/// The one place that knows which accounts an event touches. Scattering that
/// knowledge across the handlers would mean a payment posted one way by the
/// payment handler and another by an import, and a ledger with two conventions in
/// it is not a ledger (ADR-0023).
/// </para>
/// <para>
/// <strong>The client-funds rule lives here.</strong> Recognising a client
/// receivable credits <em>client funds payable</em>, not revenue: the agency has
/// not earned a hundred thousand because a studio owes its client that much.
/// Commission moves out of that liability into revenue only when it has actually
/// been collected. Posting a gross receipt as agency revenue would overstate what
/// the agency earned by an order of magnitude, and it is the single easiest
/// mistake for a finance module to make.
/// </para>
/// <para>
/// Every entry produced here is posted immediately, inside the same transaction as
/// the act that caused it. A consequence that could fail separately would leave the
/// books disagreeing with the operational record, and no later job could tell which
/// was right.
/// </para>
/// </remarks>
public sealed class LedgerPosting
{
    private readonly ILedgerRepository _ledger;
    private readonly IClock _clock;

    public LedgerPosting(ILedgerRepository ledger, IClock clock)
    {
        _ledger = ledger;
        _clock = clock;
    }

    /// <summary>
    /// The organization's accounts, seeded on first use.
    /// </summary>
    /// <remarks>
    /// Seeded lazily rather than in the migration, so an organization created
    /// before M9 gets its chart the first time somebody uses finance rather than
    /// needing a backfill. The set is fixed and identical for every tenant.
    /// </remarks>
    public async Task<IReadOnlyDictionary<SystemAccount, Account>> ChartAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Account> accounts = await _ledger
            .ListAccountsAsync(organizationId, cancellationToken)
            .ConfigureAwait(false);

        if (accounts.Count == 0)
        {
            IReadOnlyList<Account> seeded = Account.SeedChart(organizationId, _clock.UtcNow);

            _ledger.AddAccounts(seeded);

            accounts = seeded;
        }

        return accounts.ToDictionary(account => account.Kind);
    }

    /// <summary>
    /// Recognises a receivable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A client receivable: <c>Dr accounts receivable / Cr client funds payable</c>.
    /// The agency records that it is owed the money and, in the same breath, that
    /// the money is not its own.
    /// </para>
    /// <para>
    /// An agency receivable: <c>Dr accounts receivable / Cr commission revenue</c>
    /// is deliberately <em>not</em> what happens. Nothing is credited to revenue
    /// when a receivable is raised, because raising one is an expectation rather
    /// than an earning. An agency receivable credits client funds payable too, and
    /// the transfer to revenue happens on collection.
    /// </para>
    /// </remarks>
    public async Task<JournalEntry> PostReceivableRaisedAsync(
        Receivable receivable,
        UserId actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receivable);

        IReadOnlyDictionary<SystemAccount, Account> chart =
            await ChartAsync(receivable.OrganizationId, cancellationToken).ConfigureAwait(false);

        JournalEntry entry = Start(
            receivable.OrganizationId,
            JournalSource.ReceivableRaised,
            $"Receivable raised: {receivable.Reference ?? receivable.Id.ToString()}",
            receivable.CurrencyCodeValue,
            DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            actor);

        Money amount = receivable.OriginalAmount;

        entry.AddLine(
            chart[SystemAccount.AccountsReceivable].Id, JournalSide.Debit, amount, _clock.UtcNow);

        // Gross is a liability, never revenue. This is the line that keeps the
        // agency's books honest about whose money it is.
        entry.AddLine(
            chart[SystemAccount.ClientFundsPayable].Id, JournalSide.Credit, amount, _clock.UtcNow);

        entry.AttributeTo(receivable: receivable.Id);

        return Post(entry, actor);
    }

    /// <summary>
    /// Records cash arriving, before anybody has said what it is for.
    /// </summary>
    /// <remarks>
    /// <c>Dr cash / Cr unapplied cash</c>. The money is in hand and its purpose is
    /// unknown, which is a real and ordinary state - and one that must not be
    /// silently attributed to whichever receivable looks closest (ADR-0023).
    /// </remarks>
    public async Task<JournalEntry> PostPaymentRecordedAsync(
        Payment payment,
        UserId actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);

        IReadOnlyDictionary<SystemAccount, Account> chart =
            await ChartAsync(payment.OrganizationId, cancellationToken).ConfigureAwait(false);

        JournalEntry entry = Start(
            payment.OrganizationId,
            JournalSource.PaymentRecorded,
            $"Payment received: {payment.ExternalReference ?? payment.Id.ToString()}",
            payment.CurrencyCodeValue,
            payment.ReceivedOn,
            actor);

        Money amount = payment.Amount;

        bool incoming = payment.Direction == PaymentDirection.Incoming;

        entry.AddLine(
            chart[SystemAccount.Cash].Id,
            incoming ? JournalSide.Debit : JournalSide.Credit,
            amount,
            _clock.UtcNow);

        entry.AddLine(
            chart[SystemAccount.UnappliedCash].Id,
            incoming ? JournalSide.Credit : JournalSide.Debit,
            amount,
            _clock.UtcNow);

        entry.AttributeTo(payment: payment.Id);

        return Post(entry, actor);
    }

    /// <summary>
    /// Applies cash to what it turned out to be for.
    /// </summary>
    /// <remarks>
    /// <c>Dr unapplied cash / Cr accounts receivable</c>. The clearing account
    /// empties and the debt reduces; cash does not move again, because it already
    /// arrived.
    /// </remarks>
    public async Task<JournalEntry> PostAllocationAsync(
        Payment payment,
        PaymentAllocation allocation,
        UserId actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(allocation);

        IReadOnlyDictionary<SystemAccount, Account> chart =
            await ChartAsync(payment.OrganizationId, cancellationToken).ConfigureAwait(false);

        JournalEntry entry = Start(
            payment.OrganizationId,
            JournalSource.PaymentAllocated,
            "Payment applied to receivable",
            allocation.CurrencyCodeValue,
            payment.ReceivedOn,
            actor);

        Money amount = allocation.Amount;

        entry.AddLine(
            chart[SystemAccount.UnappliedCash].Id, JournalSide.Debit, amount, _clock.UtcNow);

        entry.AddLine(
            chart[SystemAccount.AccountsReceivable].Id, JournalSide.Credit, amount, _clock.UtcNow);

        entry.AttributeTo(
            receivable: allocation.ReceivableId, payment: payment.Id, allocation: allocation.Id);

        return Post(entry, actor);
    }

    /// <summary>
    /// Moves earned commission out of the client's money and into the agency's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Dr client funds payable / Cr commission revenue</c>. The liability to the
    /// client reduces by exactly what the agency has earned, and revenue is
    /// recognised for the first and only time.
    /// </para>
    /// <para>
    /// Posted on <strong>collection</strong>, not on entitlement. That is an
    /// operational convention rather than a revenue-recognition policy, and
    /// ADR-0023 says so explicitly: AgencyOS has adopted no accounting framework,
    /// and crediting revenue when cash arrives is the conservative choice available
    /// to a system that has not been told which framework applies (ADR-0023).
    /// </para>
    /// </remarks>
    public async Task<JournalEntry> PostCommissionCollectedAsync(
        CommissionEntitlement entitlement,
        Money earned,
        DateOnly occurredOn,
        UserId actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entitlement);

        IReadOnlyDictionary<SystemAccount, Account> chart =
            await ChartAsync(entitlement.OrganizationId, cancellationToken).ConfigureAwait(false);

        JournalEntry entry = Start(
            entitlement.OrganizationId,
            JournalSource.CommissionCollected,
            "Commission earned on collected funds",
            earned.Currency.Value,
            occurredOn,
            actor);

        entry.AddLine(
            chart[SystemAccount.ClientFundsPayable].Id, JournalSide.Debit, earned, _clock.UtcNow);

        entry.AddLine(
            chart[SystemAccount.CommissionRevenue].Id, JournalSide.Credit, earned, _clock.UtcNow);

        entry.AttributeTo(commission: entitlement.Id, receivable: entitlement.ClientReceivableId);

        return Post(entry, actor);
    }

    /// <summary>
    /// Records a deduction that reduces what will ever arrive.
    /// </summary>
    /// <remarks>
    /// <c>Dr deductions / Cr accounts receivable</c>. The receivable reduces because
    /// the money is not coming, and the expense records why. AgencyOS makes no claim
    /// about what a withholding will later be credited against: it has no tax engine
    /// and infers no liability.
    /// </remarks>
    public async Task<JournalEntry> PostAdjustmentAsync(
        PaymentAdjustment adjustment,
        UserId actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        IReadOnlyDictionary<SystemAccount, Account> chart =
            await ChartAsync(adjustment.OrganizationId, cancellationToken).ConfigureAwait(false);

        JournalEntry entry = Start(
            adjustment.OrganizationId,
            JournalSource.AdjustmentRecorded,
            $"{adjustment.Kind} recorded: {adjustment.Description}",
            adjustment.CurrencyCodeValue,
            adjustment.OccurredOn,
            actor);

        Money amount = adjustment.Amount;

        entry.AddLine(
            chart[SystemAccount.DeductionExpense].Id, JournalSide.Debit, amount, _clock.UtcNow);

        entry.AddLine(
            chart[SystemAccount.AccountsReceivable].Id, JournalSide.Credit, amount, _clock.UtcNow);

        entry.AttributeTo(receivable: adjustment.ReceivableId, adjustment: adjustment.Id);

        return Post(entry, actor);
    }

    /// <summary>
    /// Records money the agency has decided it will not collect.
    /// </summary>
    /// <remarks>
    /// <c>Dr write-offs / Cr accounts receivable</c>. A financial act with a
    /// posting, not a row disappearing from a list (ADR-0023).
    /// </remarks>
    public async Task<JournalEntry> PostWriteOffAsync(
        Receivable receivable,
        Money amount,
        UserId actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receivable);

        IReadOnlyDictionary<SystemAccount, Account> chart =
            await ChartAsync(receivable.OrganizationId, cancellationToken).ConfigureAwait(false);

        JournalEntry entry = Start(
            receivable.OrganizationId,
            JournalSource.ReceivableWrittenOff,
            $"Receivable written off: {receivable.ClosureReason}",
            amount.Currency.Value,
            DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            actor);

        entry.AddLine(
            chart[SystemAccount.WriteOffExpense].Id, JournalSide.Debit, amount, _clock.UtcNow);

        entry.AddLine(
            chart[SystemAccount.AccountsReceivable].Id, JournalSide.Credit, amount, _clock.UtcNow);

        entry.AttributeTo(receivable: receivable.Id);

        return Post(entry, actor);
    }

    /// <summary>
    /// Posts the entry that undoes another.
    /// </summary>
    /// <remarks>
    /// The original is marked reversed and otherwise untouched, and the reversing
    /// entry carries its own date and reason. Both remain readable, which is the
    /// whole difference between a correction and a rewrite.
    /// </remarks>
    public JournalEntry PostReversal(
        JournalEntry original,
        string reason,
        UserId actor,
        DateOnly? postingDate = null)
    {
        ArgumentNullException.ThrowIfNull(original);

        JournalEntry reversal = original.BuildReversal(reason, actor, _clock.UtcNow, postingDate);

        reversal.Post(actor, _clock.UtcNow, reversal.Version);

        original.NoteReversedBy(reversal.Id, reason, _clock.UtcNow);

        _ledger.AddEntry(reversal);

        return reversal;
    }

    private JournalEntry Start(
        OrganizationId organizationId,
        JournalSource source,
        string memo,
        string currency,
        DateOnly occurredOn,
        UserId actor) =>
        JournalEntry.Start(
            organizationId, source, memo, currency, occurredOn, actor, _clock.UtcNow);

    /// <summary>
    /// Posts an entry and adds it to the unit of work.
    /// </summary>
    /// <remarks>
    /// Posting happens here rather than in the caller so that no code path can add
    /// a draft entry to the ledger and forget to balance it. The aggregate refuses
    /// an unbalanced post, the kernel computes the totals, and PostgreSQL checks the
    /// same arithmetic again with a deferred trigger at commit.
    /// </remarks>
    private JournalEntry Post(JournalEntry entry, UserId actor)
    {
        entry.Post(actor, _clock.UtcNow, entry.Version);

        _ledger.AddEntry(entry);

        return entry;
    }
}
