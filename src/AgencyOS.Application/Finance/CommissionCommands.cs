using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Finance.Rules;

namespace AgencyOS.Application.Finance;

/// <summary>
/// Sets the rules and works out what the agency is owed.
/// </summary>
/// <remarks>
/// Nothing here assumes a rate. There is no house percentage, no fallback and no
/// "usual" commission: a transaction with no governing rule produces no
/// entitlement and says so, because inventing one would put a number in front of a
/// client that nobody agreed (ADR-0023).
/// </remarks>
public sealed class CommissionHandler
{
    private readonly ICommissionRepository _commissions;
    private readonly IMonetaryObligationRepository _obligations;
    private readonly IFinanceEventRepository _events;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CommissionHandler(
        ICommissionRepository commissions,
        IMonetaryObligationRepository obligations,
        IFinanceEventRepository events,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _commissions = commissions;
        _obligations = obligations;
        _events = events;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Records the rule that decides what the agency is entitled to.</summary>
    public async Task<CommissionRuleId> HandleAsync(
        CreateCommissionRuleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.FinanceCommissionsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        CommissionRule rule = CommissionRule.Create(
            command.OrganizationId,
            command.RepresentationId,
            command.ClientPersonId,
            command.Basis,
            command.EffectiveFrom,
            actor,
            _clock.UtcNow,
            command.RatePercent,
            command.FixedAmount,
            command.TermCode,
            command.ContractId,
            command.EffectiveTo,
            command.Provenance,
            command.Notes);

        // Two rules in force at once is a data problem, and the calculation refuses
        // to resolve it. Catching it here means the person who created the overlap
        // hears about it rather than the person who later cannot calculate.
        IReadOnlyList<CommissionRule> existing = await _commissions
            .ListRulesForClientAsync(command.OrganizationId, command.ClientPersonId, cancellationToken)
            .ConfigureAwait(false);

        RequireNoOverlap(existing, rule);

        _commissions.AddRule(rule);

        _audit.Record(
            AuditAction.CommissionRuleCreated,
            entityType: nameof(CommissionRule),
            entityId: rule.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceCommissionsWrite,

            // The basis and the dates, never the rate. A rate in the audit log is
            // the commission readable by anybody who can read audit.
            semanticDelta: new
            {
                RepresentationId = rule.RepresentationId,
                Basis = rule.Basis.ToString(),
                EffectiveFrom = rule.EffectiveFrom.ToString("O"),
                IsContractSpecific = rule.ContractId is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return rule.Id;
    }

    /// <summary>Ends a rule from a date, which is the only way one stops governing.</summary>
    public async Task HandleAsync(
        EndCommissionRuleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _guard
            .AuthorizeAsync(
                Permission.FinanceCommissionsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        CommissionRule rule = await _commissions
            .FindRuleAsync(command.OrganizationId, command.CommissionRuleId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(CommissionRule), command.CommissionRuleId.ToString());

        rule.EndOn(command.EndsOn, _clock.UtcNow, command.ExpectedVersion);

        _audit.Record(
            AuditAction.CommissionRuleEnded,
            entityType: nameof(CommissionRule),
            entityId: rule.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceCommissionsWrite,
            semanticDelta: new { EndsOn = command.EndsOn.ToString("O") });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Works out what the agency is entitled to against one obligation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The governing date defaults to the day the obligation falls due, not to
    /// today. That is what makes historical finance reproducible: recalculating a
    /// 2027 commission in 2029 must give the 2027 answer, because the 2027 rule is
    /// what the parties were operating under (ADR-0023).
    /// </para>
    /// <para>
    /// An obligation with no calculable amount produces no entitlement. A backend
    /// participation nobody can value has no basis, and a percentage of an unknown
    /// is not zero.
    /// </para>
    /// </remarks>
    public async Task<CommissionEntitlementId> HandleAsync(
        CalculateCommissionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.FinanceCommissionsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        MonetaryObligation obligation = await FinanceSupport
            .RequireObligationAsync(
                _obligations, command.OrganizationId, command.MonetaryObligationId, cancellationToken)
            .ConfigureAwait(false);

        if (obligation.Amount is not { } basis)
        {
            throw new DomainException(
                "This obligation has no amount that can be worked out, so there is no basis for a "
                + "commission. A percentage of an unknown is not zero, and recording it as zero "
                + "would put a false figure into every total that touches it.");
        }

        DateOnly governing =
            command.GoverningOn
            ?? obligation.ResolvedDueOn
            ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        IReadOnlyList<CommissionRule> rules = await _commissions
            .ListRulesForClientAsync(command.OrganizationId, command.ClientPersonId, cancellationToken)
            .ConfigureAwait(false);

        CommissionRuleInput[] candidates = [.. rules.Select(x => x.ToRulesInput())];

        // The kernel decides which rule governs. Contract-specific beats general;
        // two in force at once is refused rather than resolved (ADR-0023).
        if (FinanceRules.DescribeGoverningProblem(
                candidates, governing, obligation.ContractId.Value) is { } problem)
        {
            throw new DomainException(problem);
        }

        Guid ruleId = FinanceRules.GoverningRule(
            candidates, governing, obligation.ContractId.Value);

        CommissionRule rule = rules.Single(x => x.Id.Value == ruleId);

        // Recalculating supersedes rather than overwrites, so what was calculated
        // before and acted on stays readable.
        IReadOnlyList<CommissionEntitlement> existing = await _commissions
            .ListEntitlementsForObligationAsync(
                command.OrganizationId, obligation.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (CommissionEntitlement previous in existing.Where(
            x => x.Status == CommissionEntitlementStatus.Calculated))
        {
            previous.Supersede(
                _clock.UtcNow, previous.Version, "Superseded by a later calculation.");
        }

        CommissionEntitlement entitlement = CommissionEntitlement.Calculate(
            command.OrganizationId,
            obligation.Id,
            obligation.ContractId,
            command.ClientPersonId,
            command.RepresentationId,
            rule,
            basis,
            governing,
            actor,
            _clock.UtcNow,
            command.ClientReceivableId,
            command.Notes);

        _commissions.AddEntitlement(entitlement);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.CommissionCalculated,
            $"Commission entitlement of {entitlement.EntitledAmount} calculated",
            actor,
            _clock.UtcNow,
            contractId: obligation.ContractId,
            obligationId: obligation.Id,
            commissionId: entitlement.Id,
            amount: entitlement.EntitledAmount));

        _audit.Record(
            AuditAction.CommissionCalculated,
            entityType: nameof(CommissionEntitlement),
            entityId: entitlement.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceCommissionsWrite,
            semanticDelta: new
            {
                ContractId = obligation.ContractId.ToString(),
                RuleId = rule.Id.ToString(),
                Basis = entitlement.Basis.ToString(),
                entitlement.CurrencyCodeValue,
                GoverningOn = governing.ToString("O"),
                Superseded = existing.Count,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entitlement.Id;
    }

    /// <summary>
    /// Records a change to what the agency is owed.
    /// </summary>
    /// <remarks>
    /// An adjustment row, never an edit to the calculated figure. "We calculated a
    /// hundred thousand and then waived ten" is a different story from "we
    /// calculated ninety thousand", and only the first is true (ADR-0023).
    /// </remarks>
    public async Task HandleAsync(
        AdjustCommissionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.FinanceAdjustmentsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        CommissionEntitlement entitlement = await _commissions
            .FindEntitlementAsync(
                command.OrganizationId, command.CommissionEntitlementId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(CommissionEntitlement), command.CommissionEntitlementId.ToString());

        entitlement.Adjust(
            command.Kind,
            command.Amount,
            command.Reason,
            actor,
            _clock.UtcNow,
            command.ExpectedVersion);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.CommissionAdjusted,
            $"Commission adjusted by {command.Amount} ({command.Kind})",
            actor,
            _clock.UtcNow,
            contractId: entitlement.ContractId,
            commissionId: entitlement.Id,
            amount: command.Amount,
            detail: command.Reason));

        _audit.Record(
            AuditAction.CommissionAdjusted,
            entityType: nameof(CommissionEntitlement),
            entityId: entitlement.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceAdjustmentsWrite,
            semanticDelta: new
            {
                Kind = command.Kind.ToString(),
                entitlement.CurrencyCodeValue,
                command.Reason,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses a rule that would overlap one already in force.
    /// </summary>
    /// <remarks>
    /// Checked at the same scope the calculation resolves at - contract-specific
    /// rules against contract-specific ones, general against general - because a
    /// specific rule sitting alongside a general one is the intended way to narrow,
    /// not an ambiguity.
    /// </remarks>
    private static void RequireNoOverlap(
        IReadOnlyList<CommissionRule> existing,
        CommissionRule candidate)
    {
        IEnumerable<CommissionRule> sameScope = existing.Where(
            rule => rule.ContractId == candidate.ContractId);

        foreach (CommissionRule rule in sameScope)
        {
            bool overlaps =
                candidate.EffectiveFrom <= (rule.EffectiveTo ?? DateOnly.MaxValue)
                && rule.EffectiveFrom <= (candidate.EffectiveTo ?? DateOnly.MaxValue);

            if (overlaps)
            {
                throw new DomainException(
                    "A commission rule is already in force for this client over that period. "
                    + "Two rules governing at once is an ambiguity AgencyOS will not resolve, "
                    + "because picking either one would favour somebody. End the existing rule "
                    + "first.");
            }
        }
    }
}

/// <summary>
/// Posts and reverses entries a person wrote by hand.
/// </summary>
/// <remarks>
/// <para>
/// The narrowest surface in the milestone, behind its own permission. Every other
/// posting in AgencyOS is a consequence of a business act - a receivable raised, a
/// payment applied, commission earned - made by the system in the same transaction
/// as the act. This is the one route by which somebody writes an entry directly,
/// and it is held apart for exactly that reason (ADR-0023).
/// </para>
/// <para>
/// Posted entries are never edited. A correction is a reversing entry, which
/// leaves both the original and the correction readable with their own dates,
/// actors and reasons.
/// </para>
/// </remarks>
public sealed class LedgerHandler
{
    private readonly ILedgerRepository _ledger;
    private readonly IFinanceEventRepository _events;
    private readonly LedgerPosting _posting;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public LedgerHandler(
        ILedgerRepository ledger,
        IFinanceEventRepository events,
        LedgerPosting posting,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _ledger = ledger;
        _events = events;
        _posting = posting;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Posts a balanced entry somebody wrote by hand.</summary>
    public async Task<JournalEntryId> HandleAsync(
        PostJournalEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.FinanceLedgerPost, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        if (command.Lines.Count == 0)
        {
            throw new DomainException(
                "An entry with no lines asserts that something happened and records nothing "
                + "about it.");
        }

        IReadOnlyDictionary<SystemAccount, Account> chart = await _posting
            .ChartAsync(command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        JournalEntry entry = JournalEntry.Start(
            command.OrganizationId,
            JournalSource.ManualAdjustment,
            command.Memo,
            command.Currency,
            command.OccurredOn,
            actor,
            _clock.UtcNow,
            command.PostingDate);

        foreach (JournalLineInputCommand line in command.Lines)
        {
            if (!chart.TryGetValue(line.Account, out Account? account))
            {
                throw new DomainException($"'{line.Account}' is not an account in this ledger.");
            }

            entry.AddLine(account.Id, line.Side, line.Amount, _clock.UtcNow, line.Memo);
        }

        // The aggregate refuses to post unbalanced lines, the kernel computes the
        // totals, and PostgreSQL checks the same arithmetic again at commit. An
        // invariant this consequential should not rest on one code path.
        entry.Post(actor, _clock.UtcNow, entry.Version);

        _ledger.AddEntry(entry);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.JournalPosted,
            $"Journal entry posted: {command.Memo}",
            actor,
            _clock.UtcNow,
            journalEntryId: entry.Id,
            amount: entry.Debits));

        _audit.Record(
            AuditAction.JournalEntryPosted,
            entityType: nameof(JournalEntry),
            entityId: entry.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceLedgerPost,
            semanticDelta: new
            {
                Source = entry.Source.ToString(),
                entry.CurrencyCodeValue,
                LineCount = entry.Lines.Count,
                PostingDate = entry.PostingDate.ToString("O"),
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entry.Id;
    }

    /// <summary>Posts the entry that undoes a posted one.</summary>
    public async Task<JournalEntryId> HandleAsync(
        ReverseJournalEntryCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.FinanceLedgerPost, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        JournalEntry original = await _ledger
            .FindEntryAsync(command.OrganizationId, command.JournalEntryId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(JournalEntry), command.JournalEntryId.ToString());

        JournalEntry reversal = _posting.PostReversal(
            original, command.Reason, actor, command.PostingDate);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.JournalReversed,
            $"Journal entry reversed: {original.Memo}",
            actor,
            _clock.UtcNow,
            journalEntryId: reversal.Id,
            amount: reversal.Debits,
            detail: command.Reason));

        _audit.Record(
            AuditAction.JournalEntryReversed,
            entityType: nameof(JournalEntry),
            entityId: original.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceLedgerPost,
            semanticDelta: new
            {
                ReversalId = reversal.Id.ToString(),
                original.CurrencyCodeValue,
                command.Reason,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return reversal.Id;
    }
}
