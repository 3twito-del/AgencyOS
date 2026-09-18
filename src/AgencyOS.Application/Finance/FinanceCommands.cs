using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Authorization;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Finance;

// -------------------------------------------------------------------- commands

/// <param name="AnchorDate">The date the due rule hangs off, when it is known.</param>
public sealed record RecordMonetaryObligationCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    ContractVersionId ContractVersionId,
    Guid PayerPartyId,
    Guid PayeePartyId,
    ObligationCategory Category,
    ObligationAmountKind AmountKind,
    DeadlineRule Due,
    Money? Amount = null,
    int? Quantity = null,
    Money? UnitAmount = null,
    TermUnit? Unit = null,
    string? Condition = null,
    DateOnly? AnchorDate = null,
    ObligationId? SourceObligationId = null,
    ContractTermCode? SourceTermCode = null,
    string? Description = null,
    string? Notes = null);

/// <summary>Fixes the amount of an obligation recorded without one.</summary>
public sealed record QuantifyObligationCommand(
    OrganizationId OrganizationId,
    MonetaryObligationId MonetaryObligationId,
    Money Amount,
    int ExpectedVersion,
    string? Reason = null);

public sealed record ReleaseObligationCommand(
    OrganizationId OrganizationId,
    MonetaryObligationId MonetaryObligationId,
    string Reason,
    int ExpectedVersion);

/// <param name="Beneficiary">Client or Agency. Decides whose money it becomes.</param>
public sealed record RaiseReceivableCommand(
    OrganizationId OrganizationId,
    MonetaryObligationId MonetaryObligationId,
    ReceivableBeneficiary Beneficiary,
    Money? Amount = null,
    DateOnly? DueOn = null,
    Guid? ClientPersonId = null,
    Guid? RepresentationId = null,
    string? Reference = null,
    string? Notes = null);

public sealed record WriteOffReceivableCommand(
    OrganizationId OrganizationId,
    ReceivableId ReceivableId,
    string Reason,
    int ExpectedVersion);

public sealed record CancelReceivableCommand(
    OrganizationId OrganizationId,
    ReceivableId ReceivableId,
    string Reason,
    int ExpectedVersion);

public sealed record RecordInvoiceCommand(
    OrganizationId OrganizationId,
    ContractId ContractId,
    Guid DebtorPartyId,
    string Currency,
    IReadOnlyList<InvoiceLineInput> Lines,
    string? Reference = null,
    DateOnly? DueOn = null,
    string? ExternalReference = null,
    string? Notes = null);

/// <param name="ReceivableId">What is being billed.</param>
/// <param name="Amount">How much of it. Never more than is outstanding.</param>
public sealed record InvoiceLineInput(
    ReceivableId ReceivableId,
    Money Amount,
    string Description);

/// <summary>Records that an invoice was issued. AgencyOS does not send it.</summary>
public sealed record IssueInvoiceCommand(
    OrganizationId OrganizationId,
    InvoiceId InvoiceId,
    DateOnly IssuedOn,
    int ExpectedVersion,
    string? Reference = null);

public sealed record VoidInvoiceCommand(
    OrganizationId OrganizationId,
    InvoiceId InvoiceId,
    string Reason,
    int ExpectedVersion);

/// <param name="Allocations">
/// What the payment is for, if the caller already knows. Anything left over stays
/// unapplied rather than being assigned to whichever receivable looks closest.
/// </param>
public sealed record RecordPaymentCommand(
    OrganizationId OrganizationId,
    PaymentDirection Direction,
    Money Amount,
    DateOnly ReceivedOn,
    PaymentMethod Method,
    Guid? PayerPartyId = null,
    string? PayerName = null,
    Guid? PayeePartyId = null,
    string? PayeeName = null,
    string? ExternalReference = null,
    string? SourceSystem = null,
    string? Notes = null,
    IReadOnlyList<AllocationInputCommand>? Allocations = null);

public sealed record AllocationInputCommand(ReceivableId ReceivableId, Money Amount, string? Notes = null);

public sealed record AllocatePaymentCommand(
    OrganizationId OrganizationId,
    PaymentId PaymentId,
    IReadOnlyList<AllocationInputCommand> Allocations,
    int ExpectedVersion);

public sealed record ReverseAllocationCommand(
    OrganizationId OrganizationId,
    PaymentId PaymentId,
    PaymentAllocationId AllocationId,
    string Reason,
    int ExpectedVersion);

/// <summary>
/// Undoes a payment recorded in error.
/// </summary>
/// <remarks>
/// A reversing payment plus a reversing journal entry. The original keeps its
/// amount, its currency and its date, because those are what somebody observed
/// (ADR-0023).
/// </remarks>
public sealed record ReversePaymentCommand(
    OrganizationId OrganizationId,
    PaymentId PaymentId,
    string Reason,
    int ExpectedVersion);

public sealed record RecordAdjustmentCommand(
    OrganizationId OrganizationId,
    ReceivableId ReceivableId,
    PaymentAdjustmentKind Kind,
    Money Amount,
    string Description,
    DateOnly OccurredOn,
    int ExpectedVersion,
    PaymentId? PaymentId = null,
    string? ExternalReference = null);

public sealed record CreateCommissionRuleCommand(
    OrganizationId OrganizationId,
    Guid RepresentationId,
    Guid ClientPersonId,
    CommissionBasisKind Basis,
    DateOnly EffectiveFrom,
    decimal? RatePercent = null,
    Money? FixedAmount = null,
    ContractTermCode? TermCode = null,
    ContractId? ContractId = null,
    DateOnly? EffectiveTo = null,
    string? Provenance = null,
    string? Notes = null);

public sealed record EndCommissionRuleCommand(
    OrganizationId OrganizationId,
    CommissionRuleId CommissionRuleId,
    DateOnly EndsOn,
    int ExpectedVersion);

/// <summary>
/// Works out what the agency is entitled to against an obligation.
/// </summary>
/// <remarks>
/// The rule is chosen by the date the obligation falls due, not by which rule is
/// current. Recalculating a 2027 commission in 2029 must give the 2027 answer
/// (ADR-0023).
/// </remarks>
public sealed record CalculateCommissionCommand(
    OrganizationId OrganizationId,
    MonetaryObligationId MonetaryObligationId,
    Guid ClientPersonId,
    Guid RepresentationId,
    DateOnly? GoverningOn = null,
    ReceivableId? ClientReceivableId = null,
    string? Notes = null);

public sealed record AdjustCommissionCommand(
    OrganizationId OrganizationId,
    CommissionEntitlementId CommissionEntitlementId,
    CommissionAdjustmentKind Kind,
    Money Amount,
    string Reason,
    int ExpectedVersion);

/// <param name="Lines">The lines. Must balance, in one currency.</param>
public sealed record PostJournalEntryCommand(
    OrganizationId OrganizationId,
    string Memo,
    string Currency,
    DateOnly OccurredOn,
    IReadOnlyList<JournalLineInputCommand> Lines,
    DateOnly? PostingDate = null);

public sealed record JournalLineInputCommand(
    SystemAccount Account,
    JournalSide Side,
    Money Amount,
    string? Memo = null);

public sealed record ReverseJournalEntryCommand(
    OrganizationId OrganizationId,
    JournalEntryId JournalEntryId,
    string Reason,
    DateOnly? PostingDate = null);

/// <summary>What a recorded payment produced.</summary>
/// <param name="Unapplied">What is left over. Never silently assigned.</param>
public sealed record RecordPaymentResult(
    PaymentId PaymentId,
    Money Allocated,
    Money Unapplied,
    IReadOnlyList<PaymentId> PossibleDuplicates);

// -------------------------------------------------------------------- handlers

/// <summary>Records what an operative contract says is payable.</summary>
public sealed class MonetaryObligationHandler
{
    private readonly IContractRepository _contracts;
    private readonly IContractVersionRepository _versions;
    private readonly IMonetaryObligationRepository _obligations;
    private readonly IFinanceEventRepository _events;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public MonetaryObligationHandler(
        IContractRepository contracts,
        IContractVersionRepository versions,
        IMonetaryObligationRepository obligations,
        IFinanceEventRepository events,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _contracts = contracts;
        _versions = versions;
        _obligations = obligations;
        _events = events;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Records a sum a contract says is payable.
    /// </summary>
    /// <remarks>
    /// The contract must be <strong>operative</strong>: executed, or with a
    /// recorded effective date, and not abandoned or superseded. A negotiation whose
    /// terms are agreed is not a source of collectible amounts, and neither is a
    /// draft somebody is still marking up (ADR-0023).
    /// </remarks>
    public async Task<MonetaryObligationId> HandleAsync(
        RecordMonetaryObligationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.FinanceWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Contract contract = await FinanceSupport
            .RequireOperativeContractAsync(
                _contracts, command.OrganizationId, command.ContractId, cancellationToken)
            .ConfigureAwait(false);

        FinanceSupport.RequireParty(contract, command.PayerPartyId, "payer");
        FinanceSupport.RequireParty(contract, command.PayeePartyId, "payee");

        ContractVersion version = await _versions
            .FindAsync(command.OrganizationId, command.ContractVersionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(
                nameof(ContractVersion), command.ContractVersionId.ToString());

        if (version.ContractId != contract.Id)
        {
            throw new DomainException("That version belongs to a different contract.");
        }

        // The contract's own dates, when the rule hangs off one of them. Anything
        // else stays unresolved rather than being guessed onto a day (ADR-0022).
        DateOnly? anchor = command.AnchorDate ?? command.Due.Anchor switch
        {
            DeadlineAnchor.OnExecution => contract.ExecutedOn,
            DeadlineAnchor.OnEffective => contract.EffectiveOn,
            _ => null,
        };

        MonetaryObligation obligation = MonetaryObligation.Record(
            command.OrganizationId,
            contract.Id,
            command.ContractVersionId,
            command.PayerPartyId,
            command.PayeePartyId,
            command.Category,
            command.AmountKind,
            command.Due,
            actor,
            _clock.UtcNow,
            command.Amount,
            command.Quantity,
            command.UnitAmount,
            command.Unit,
            command.Condition,
            anchor,
            command.SourceObligationId,
            command.SourceTermCode,
            command.Description,
            command.Notes);

        _obligations.Add(obligation);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.ObligationRecorded,
            obligation.IsQuantified
                ? $"Payment obligation recorded: {obligation.Amount}"
                : $"Payment obligation recorded, amount {obligation.AmountKind.ToString().ToLowerInvariant()}",
            actor,
            _clock.UtcNow,
            contractId: contract.Id,
            obligationId: obligation.Id,
            amount: obligation.Amount,
            detail: command.Description));

        _audit.Record(
            AuditAction.MonetaryObligationRecorded,
            entityType: nameof(MonetaryObligation),
            entityId: obligation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceWrite,

            // Identifiers, categories and shape. Never the figure: telemetry and
            // audit are exported to places that hold no finance permission.
            semanticDelta: new
            {
                ContractId = contract.Id.ToString(),
                Category = obligation.Category.ToString(),
                AmountKind = obligation.AmountKind.ToString(),
                obligation.CurrencyCodeValue,
                Quantified = obligation.IsQuantified,
                DueResolved = obligation.ResolvedDueOn is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return obligation.Id;
    }

    /// <summary>Fixes an amount that was genuinely unknown when the obligation was recorded.</summary>
    public async Task HandleAsync(
        QuantifyObligationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.FinanceWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        MonetaryObligation obligation = await FinanceSupport
            .RequireObligationAsync(
                _obligations, command.OrganizationId, command.MonetaryObligationId, cancellationToken)
            .ConfigureAwait(false);

        obligation.Quantify(command.Amount, _clock.UtcNow, command.ExpectedVersion, command.Reason);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.ObligationQuantified,
            $"Payment obligation quantified at {command.Amount}",
            actor,
            _clock.UtcNow,
            contractId: obligation.ContractId,
            obligationId: obligation.Id,
            amount: command.Amount,
            detail: command.Reason));

        _audit.Record(
            AuditAction.MonetaryObligationQuantified,
            entityType: nameof(MonetaryObligation),
            entityId: obligation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceWrite,
            semanticDelta: new { obligation.CurrencyCodeValue, command.Reason });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records that an obligation will not fall due after all.</summary>
    public async Task HandleAsync(
        ReleaseObligationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.FinanceWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        MonetaryObligation obligation = await FinanceSupport
            .RequireObligationAsync(
                _obligations, command.OrganizationId, command.MonetaryObligationId, cancellationToken)
            .ConfigureAwait(false);

        obligation.Release(_clock.UtcNow, command.ExpectedVersion, command.Reason);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.ObligationReleased,
            "Payment obligation released",
            actor,
            _clock.UtcNow,
            contractId: obligation.ContractId,
            obligationId: obligation.Id,
            detail: command.Reason));

        _audit.Record(
            AuditAction.MonetaryObligationReleased,
            entityType: nameof(MonetaryObligation),
            entityId: obligation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceWrite,
            semanticDelta: new { command.Reason });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Raises, writes off and cancels what the agency expects to collect.</summary>
public sealed class ReceivableHandler
{
    private readonly IMonetaryObligationRepository _obligations;
    private readonly IReceivableRepository _receivables;
    private readonly IContractRepository _contracts;
    private readonly IFinanceEventRepository _events;
    private readonly ILedgerRepository _ledger;
    private readonly LedgerPosting _posting;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ReceivableHandler(
        IMonetaryObligationRepository obligations,
        IReceivableRepository receivables,
        IContractRepository contracts,
        IFinanceEventRepository events,
        ILedgerRepository ledger,
        LedgerPosting posting,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _obligations = obligations;
        _receivables = receivables;
        _contracts = contracts;
        _events = events;
        _ledger = ledger;
        _posting = posting;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Raises a receivable from a quantified obligation.
    /// </summary>
    /// <remarks>
    /// Refuses an obligation with no calculable amount. A contingent bonus whose
    /// condition has not occurred and a participation nobody can value are real
    /// obligations that are not yet collectible, and raising a receivable for zero
    /// against either would put a false figure into every total (ADR-0023).
    /// </remarks>
    public async Task<ReceivableId> HandleAsync(
        RaiseReceivableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.FinanceWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        MonetaryObligation obligation = await FinanceSupport
            .RequireObligationAsync(
                _obligations, command.OrganizationId, command.MonetaryObligationId, cancellationToken)
            .ConfigureAwait(false);

        if (!obligation.AcceptsReceivable)
        {
            throw new DomainException(
                obligation.IsQuantified
                    ? $"This obligation is {obligation.Status.ToString().ToLowerInvariant()}, so no "
                        + "receivable can be raised from it."
                    : "This obligation has no amount that can be worked out, so there is nothing "
                        + "to collect yet. Record the amount first, when it is known.");
        }

        Money amount = command.Amount ?? obligation.Amount
            ?? throw new DomainException("This obligation carries no amount.");

        Receivable receivable = Receivable.Raise(
            command.OrganizationId,
            obligation.Id,
            obligation.ContractId,
            obligation.PayerPartyId,
            command.Beneficiary,
            amount,
            actor,
            _clock.UtcNow,
            command.DueOn ?? obligation.ResolvedDueOn,
            command.ClientPersonId,
            command.RepresentationId,
            command.Reference,
            command.Notes);

        _receivables.Add(receivable);

        obligation.NoteRaised(_clock.UtcNow);

        // The books learn about it in the same transaction. Gross credits client
        // funds payable, never revenue (ADR-0023).
        await _posting.PostReceivableRaisedAsync(receivable, actor, cancellationToken)
            .ConfigureAwait(false);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.ReceivableRaised,
            $"Receivable raised for {amount}",
            actor,
            _clock.UtcNow,
            contractId: receivable.ContractId,
            obligationId: obligation.Id,
            receivableId: receivable.Id,
            amount: amount));

        _audit.Record(
            AuditAction.ReceivableRaised,
            entityType: nameof(Receivable),
            entityId: receivable.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceWrite,
            semanticDelta: new
            {
                ContractId = receivable.ContractId.ToString(),
                Beneficiary = receivable.Beneficiary.ToString(),
                receivable.CurrencyCodeValue,
                HasDueDate = receivable.DueOn is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return receivable.Id;
    }

    /// <summary>
    /// Gives up on collecting what remains.
    /// </summary>
    /// <remarks>
    /// A financial act with a reason and a posting, never a row disappearing. The
    /// original amount stays exactly what it was (ADR-0023).
    /// </remarks>
    public async Task HandleAsync(
        WriteOffReceivableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.FinanceAdjustmentsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Receivable receivable = await FinanceSupport
            .RequireReceivableAsync(
                _receivables, command.OrganizationId, command.ReceivableId, cancellationToken)
            .ConfigureAwait(false);

        Money written = receivable.WriteOff(_clock.UtcNow, command.ExpectedVersion, command.Reason);

        await _posting.PostWriteOffAsync(receivable, written, actor, cancellationToken)
            .ConfigureAwait(false);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.ReceivableWrittenOff,
            $"Receivable written off: {written}",
            actor,
            _clock.UtcNow,
            contractId: receivable.ContractId,
            receivableId: receivable.Id,
            amount: written,
            detail: command.Reason));

        _audit.Record(
            AuditAction.ReceivableWrittenOff,
            entityType: nameof(Receivable),
            entityId: receivable.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceAdjustmentsWrite,
            semanticDelta: new { receivable.CurrencyCodeValue, command.Reason });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Withdraws a receivable raised in error, before anything was collected.</summary>
    public async Task HandleAsync(
        CancelReceivableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.FinanceWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Receivable receivable = await FinanceSupport
            .RequireReceivableAsync(
                _receivables, command.OrganizationId, command.ReceivableId, cancellationToken)
            .ConfigureAwait(false);

        receivable.Cancel(_clock.UtcNow, command.ExpectedVersion, command.Reason);

        // A cancellation undoes the recognition; it is not a write-off. Posting to
        // the write-off expense account would record that the agency gave up on
        // money it was owed, when what happened is that it was never owed at all
        // (ADR-0023).
        JournalEntry? recognition = await _ledger
            .FindEntryForReceivableAsync(
                command.OrganizationId,
                receivable.Id,
                JournalSource.ReceivableRaised,
                cancellationToken)
            .ConfigureAwait(false);

        if (recognition is { Status: JournalEntryStatus.Posted })
        {
            _posting.PostReversal(recognition, command.Reason, actor);
        }

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.ReceivableCancelled,
            "Receivable cancelled",
            actor,
            _clock.UtcNow,
            contractId: receivable.ContractId,
            receivableId: receivable.Id,
            detail: command.Reason));

        _audit.Record(
            AuditAction.ReceivableCancelled,
            entityType: nameof(Receivable),
            entityId: receivable.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceWrite,
            semanticDelta: new { command.Reason });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records a deduction that reduces what will ever arrive.</summary>
    public async Task<PaymentAdjustmentId> HandleAsync(
        RecordAdjustmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.FinanceAdjustmentsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Receivable receivable = await FinanceSupport
            .RequireReceivableAsync(
                _receivables, command.OrganizationId, command.ReceivableId, cancellationToken)
            .ConfigureAwait(false);

        PaymentAdjustment adjustment = PaymentAdjustment.Record(
            command.OrganizationId,
            receivable.Id,
            command.Kind,
            command.Amount,
            command.Description,
            command.OccurredOn,
            actor,
            _clock.UtcNow,
            command.PaymentId,
            command.ExternalReference);

        receivable.ApplyAdjustment(command.Amount, _clock.UtcNow);

        _receivables.AddAdjustment(adjustment);

        await _posting.PostAdjustmentAsync(adjustment, actor, cancellationToken).ConfigureAwait(false);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.AdjustmentRecorded,
            $"{command.Kind} of {command.Amount} recorded",
            actor,
            _clock.UtcNow,
            contractId: receivable.ContractId,
            receivableId: receivable.Id,
            paymentId: command.PaymentId,
            amount: command.Amount,
            detail: command.Description));

        _audit.Record(
            AuditAction.PaymentAdjustmentRecorded,
            entityType: nameof(PaymentAdjustment),
            entityId: adjustment.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceAdjustmentsWrite,
            semanticDelta: new
            {
                ReceivableId = receivable.Id.ToString(),
                Kind = command.Kind.ToString(),
                adjustment.CurrencyCodeValue,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return adjustment.Id;
    }
}

/// <summary>Shared checks for the finance commands.</summary>
internal static class FinanceSupport
{
    /// <summary>
    /// Loads a contract and refuses one that is not legally operative.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Operative means <strong>executed</strong>. Derived from M8's own status rather
    /// than stored, so there is no flag beside it to disagree with (ADR-0022,
    /// ADR-0023, ADR-0040).
    /// </para>
    /// <para>
    /// An effective date used to be sufficient on its own, which meant a draft
    /// nobody had signed could carry a collectible amount as soon as somebody typed
    /// a date on it. An effective date answers <em>from when do the terms apply</em>;
    /// it does not answer <em>is there operative paper</em>. The two are still
    /// separate — a contract may be effective before, after or regardless of when it
    /// was signed, and that is untouched — but only execution makes money
    /// collectible (ADR-0040).
    /// </para>
    /// </remarks>
    internal static async Task<Contract> RequireOperativeContractAsync(
        IContractRepository contracts,
        OrganizationId organizationId,
        ContractId contractId,
        CancellationToken cancellationToken)
    {
        Contract contract =
            await contracts.FindAsync(organizationId, contractId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Contract), contractId.ToString());

        if (contract.Status is ContractStatus.Abandoned or ContractStatus.Superseded)
        {
            throw new DomainException(
                $"This contract has been {contract.Status.ToString().ToLowerInvariant()}, so "
                + "nothing is collectible under it.");
        }

        if (contract.Status != ContractStatus.Executed)
        {
            throw new DomainException(
                "This contract is not executed, so it is not yet operative. Agreed "
                + "commercial terms are not a collectible legal amount, and an effective "
                + "date alone does not make them one; record the signatures that execute "
                + "it first.");
        }

        return contract;
    }

    internal static void RequireParty(Contract contract, Guid partyId, string role)
    {
        if (contract.Parties.All(party => party.Id != partyId))
        {
            throw new DomainException($"The {role} is not a party to this contract.");
        }
    }

    internal static async Task<MonetaryObligation> RequireObligationAsync(
        IMonetaryObligationRepository obligations,
        OrganizationId organizationId,
        MonetaryObligationId id,
        CancellationToken cancellationToken) =>
        await obligations.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(MonetaryObligation), id.ToString());

    internal static async Task<Receivable> RequireReceivableAsync(
        IReceivableRepository receivables,
        OrganizationId organizationId,
        ReceivableId id,
        CancellationToken cancellationToken) =>
        await receivables.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Receivable), id.ToString());
}
