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

/// <summary>
/// Records money that moved, and what it turned out to be for.
/// </summary>
/// <remarks>
/// Every method here writes a payment fact, a set of allocation facts, the
/// receivable balances they change, the ledger entries they cause and the history
/// entry a person reads - all in one transaction. A consequence that could fail on
/// its own would leave the books disagreeing with the operational record with no
/// way to tell which was right (ADR-0023).
/// </remarks>
public sealed class PaymentHandler
{
    private readonly IPaymentRepository _payments;
    private readonly IReceivableRepository _receivables;
    private readonly ICommissionRepository _commissions;
    private readonly IFinanceEventRepository _events;
    private readonly ILedgerRepository _ledger;
    private readonly LedgerPosting _posting;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public PaymentHandler(
        IPaymentRepository payments,
        IReceivableRepository receivables,
        ICommissionRepository commissions,
        IFinanceEventRepository events,
        ILedgerRepository ledger,
        LedgerPosting posting,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _payments = payments;
        _receivables = receivables;
        _commissions = commissions;
        _events = events;
        _ledger = ledger;
        _posting = posting;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Records that money moved, and applies it where the caller says.
    /// </summary>
    /// <remarks>
    /// Anything not allocated stays unapplied and is reported back. There is no
    /// auto-matching: assigning a residual to whichever receivable looks closest
    /// would be the system guessing at somebody's intent and then acting on the
    /// guess (ADR-0023).
    /// </remarks>
    public async Task<RecordPaymentResult> HandleAsync(
        RecordPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.FinancePaymentsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        // A repeated bank reference is a warning, never a refusal: two genuinely
        // different payments can carry the same remittance text (ADR-0023).
        IReadOnlyList<Payment> duplicates =
            string.IsNullOrWhiteSpace(command.ExternalReference)
                ? []
                : await _payments
                    .FindByExternalReferenceAsync(
                        command.OrganizationId, command.ExternalReference, cancellationToken)
                    .ConfigureAwait(false);

        Payment payment = Payment.Record(
            command.OrganizationId,
            command.Direction,
            command.Amount,
            command.ReceivedOn,
            command.Method,
            actor,
            _clock.UtcNow,
            command.PayerPartyId,
            command.PayerName,
            command.PayeePartyId,
            command.PayeeName,
            command.ExternalReference,
            command.SourceSystem,
            command.Notes);

        _payments.Add(payment);

        await _posting.PostPaymentRecordedAsync(payment, actor, cancellationToken)
            .ConfigureAwait(false);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.PaymentRecorded,
            $"Payment of {command.Amount} recorded as received",
            actor,
            _clock.UtcNow,
            paymentId: payment.Id,
            amount: command.Amount,
            detail: command.ExternalReference));

        _audit.Record(
            AuditAction.PaymentRecorded,
            entityType: nameof(Payment),
            entityId: payment.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinancePaymentsWrite,

            // Identifiers and shape only. No amount and no bank reference: audit is
            // read by people who may hold no finance permission at all.
            semanticDelta: new
            {
                Direction = payment.Direction.ToString(),
                Method = payment.Method.ToString(),
                payment.CurrencyCodeValue,
                HasExternalReference = payment.ExternalReference is not null,
                PossibleDuplicates = duplicates.Count,
            });

        if (command.Allocations is { Count: > 0 })
        {
            await ApplyAsync(payment, command.Allocations, actor, cancellationToken)
                .ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordPaymentResult(
            payment.Id,
            payment.Allocated,
            payment.Unapplied,
            [.. duplicates.Select(x => x.Id)]);
    }

    /// <summary>Applies unapplied cash to receivables.</summary>
    public async Task<RecordPaymentResult> HandleAsync(
        AllocatePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.FinancePaymentsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Payment payment = await RequirePaymentAsync(
            command.OrganizationId, command.PaymentId, cancellationToken).ConfigureAwait(false);

        if (payment.Version != command.ExpectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(Payment), payment.Id.ToString(), command.ExpectedVersion, payment.Version);
        }

        await ApplyAsync(payment, command.Allocations, actor, cancellationToken)
            .ConfigureAwait(false);

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RecordPaymentResult(payment.Id, payment.Allocated, payment.Unapplied, []);
    }

    /// <summary>Returns an allocation's money to unapplied, keeping the line as history.</summary>
    public async Task HandleAsync(
        ReverseAllocationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.FinancePaymentsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Payment payment = await RequirePaymentAsync(
            command.OrganizationId, command.PaymentId, cancellationToken).ConfigureAwait(false);

        PaymentAllocation allocation = payment.ReverseAllocation(
            command.AllocationId, _clock.UtcNow, command.ExpectedVersion, actor, command.Reason);

        Receivable receivable = await FinanceSupport
            .RequireReceivableAsync(
                _receivables, command.OrganizationId, allocation.ReceivableId, cancellationToken)
            .ConfigureAwait(false);

        receivable.ReverseAllocation(allocation.Amount, _clock.UtcNow);

        // The entry that actually posted is reversed, not a fresh copy of it.
        // Rebuilding and re-posting the lines would leave two originals in the
        // ledger with only one of them undone (ADR-0023).
        JournalEntry? entry = await _ledger
            .FindEntryForAllocationAsync(command.OrganizationId, allocation.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entry is { Status: JournalEntryStatus.Posted })
        {
            _posting.PostReversal(entry, command.Reason, actor);
        }

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.AllocationReversed,
            $"Allocation of {allocation.Amount} reversed",
            actor,
            _clock.UtcNow,
            contractId: receivable.ContractId,
            receivableId: receivable.Id,
            paymentId: payment.Id,
            amount: allocation.Amount,
            detail: command.Reason));

        _audit.Record(
            AuditAction.PaymentAllocationReversed,
            entityType: nameof(PaymentAllocation),
            entityId: allocation.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinancePaymentsWrite,
            semanticDelta: new
            {
                PaymentId = payment.Id.ToString(),
                ReceivableId = receivable.Id.ToString(),
                command.Reason,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Undoes a payment recorded in error.
    /// </summary>
    /// <remarks>
    /// A reversing payment for the same amount, plus the reversing journal entry.
    /// The original keeps its amount, its currency and its date, because those are
    /// what somebody observed and observations are not edited (ADR-0023).
    /// </remarks>
    public async Task<PaymentId> HandleAsync(
        ReversePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(
                Permission.FinancePaymentsWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Payment original = await RequirePaymentAsync(
            command.OrganizationId, command.PaymentId, cancellationToken).ConfigureAwait(false);

        if (original.Version != command.ExpectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(Payment), original.Id.ToString(), command.ExpectedVersion, original.Version);
        }

        Payment reversal = Payment.Record(
            command.OrganizationId,
            original.Direction == PaymentDirection.Incoming
                ? PaymentDirection.Outgoing
                : PaymentDirection.Incoming,
            original.Amount,
            DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            original.Method,
            actor,
            _clock.UtcNow,
            original.PayerPartyId,
            original.PayerName,
            original.PayeePartyId,
            original.PayeeName,
            original.ExternalReference,
            original.SourceSystem,
            $"Reversal: {command.Reason}");

        reversal.NoteReversalOf(original.Id, _clock.UtcNow);

        _payments.Add(reversal);

        original.NoteReversedBy(reversal.Id, command.Reason, _clock.UtcNow);

        await _posting.PostPaymentRecordedAsync(reversal, actor, cancellationToken)
            .ConfigureAwait(false);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.PaymentReversed,
            $"Payment of {original.Amount} reversed",
            actor,
            _clock.UtcNow,
            paymentId: original.Id,
            amount: original.Amount,
            detail: command.Reason));

        _audit.Record(
            AuditAction.PaymentReversed,
            entityType: nameof(Payment),
            entityId: original.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinancePaymentsWrite,
            semanticDelta: new
            {
                ReversalId = reversal.Id.ToString(),
                original.CurrencyCodeValue,
                command.Reason,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return reversal.Id;
    }

    /// <summary>
    /// Applies a payment across receivables, posting each line and its commission.
    /// </summary>
    /// <remarks>
    /// The receivables are loaded in one query rather than one per line, because a
    /// payment settling six invoices is ordinary and six round trips inside a
    /// transaction holding row locks is how a concurrent allocation deadlocks.
    /// </remarks>
    private async Task ApplyAsync(
        Payment payment,
        IReadOnlyList<AllocationInputCommand> lines,
        UserId actor,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Receivable> receivables = await _receivables
            .FindManyAsync(
                payment.OrganizationId,
                [.. lines.Select(line => line.ReceivableId).Distinct()],
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<ReceivableId, Receivable> byId = receivables.ToDictionary(x => x.Id);

        foreach (AllocationInputCommand line in lines)
        {
            if (!byId.TryGetValue(line.ReceivableId, out Receivable? receivable))
            {
                throw new EntityNotFoundException(nameof(Receivable), line.ReceivableId.ToString());
            }

            PaymentAllocation allocation = payment.Allocate(
                receivable, line.Amount, _clock.UtcNow, payment.Version, actor, line.Notes);

            receivable.ApplyAllocation(line.Amount, _clock.UtcNow);

            await _posting.PostAllocationAsync(payment, allocation, actor, cancellationToken)
                .ConfigureAwait(false);

            _events.Add(FinanceEvent.Record(
                payment.OrganizationId,
                FinanceEventKind.AllocationApplied,
                $"{line.Amount} applied to receivable {receivable.Reference ?? receivable.Id.ToString()}",
                actor,
                _clock.UtcNow,
                contractId: receivable.ContractId,
                receivableId: receivable.Id,
                paymentId: payment.Id,
                amount: line.Amount));

            _audit.Record(
                AuditAction.PaymentAllocated,
                entityType: nameof(PaymentAllocation),
                entityId: allocation.Id.ToString(),
                organizationId: payment.OrganizationId,
                permission: Permission.FinancePaymentsWrite,
                semanticDelta: new
                {
                    PaymentId = payment.Id.ToString(),
                    ReceivableId = receivable.Id.ToString(),
                    allocation.CurrencyCodeValue,
                });

            await EarnCommissionAsync(receivable, line.Amount, actor, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Moves the commission earned on collected funds into revenue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only for a <em>client</em> receivable, and only in proportion to what
    /// actually arrived. A hundred-thousand entitlement against a million-dollar
    /// obligation earns forty thousand when four hundred thousand is collected, and
    /// this is where that arithmetic reaches the ledger (ADR-0023).
    /// </para>
    /// <para>
    /// Nothing happens when no entitlement has been calculated. Commission is not
    /// invented from a default rate, here or anywhere.
    /// </para>
    /// </remarks>
    private async Task EarnCommissionAsync(
        Receivable receivable,
        Money collected,
        UserId actor,
        CancellationToken cancellationToken)
    {
        if (receivable.Beneficiary != ReceivableBeneficiary.Client)
        {
            return;
        }

        IReadOnlyList<CommissionEntitlement> entitlements = await _commissions
            .ListEntitlementsForObligationAsync(
                receivable.OrganizationId, receivable.MonetaryObligationId, cancellationToken)
            .ConfigureAwait(false);

        CommissionEntitlement? entitlement = entitlements.FirstOrDefault(
            x => x.Status == CommissionEntitlementStatus.Calculated);

        if (entitlement is null)
        {
            return;
        }

        Money earned = entitlement.CollectedAgainst(collected);

        if (earned.Amount <= 0m)
        {
            return;
        }

        await _posting
            .PostCommissionCollectedAsync(
                entitlement, earned, receivable.DueOn ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
                actor, cancellationToken)
            .ConfigureAwait(false);

        _events.Add(FinanceEvent.Record(
            receivable.OrganizationId,
            FinanceEventKind.CommissionCalculated,
            $"Commission of {earned} earned on collected funds",
            actor,
            _clock.UtcNow,
            contractId: receivable.ContractId,
            receivableId: receivable.Id,
            commissionId: entitlement.Id,
            amount: earned));
    }

    private async Task<Payment> RequirePaymentAsync(
        OrganizationId organizationId,
        PaymentId id,
        CancellationToken cancellationToken) =>
        await _payments.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Payment), id.ToString());
}

/// <summary>Records invoices, which AgencyOS does not send.</summary>
public sealed class InvoiceHandler
{
    private readonly IInvoiceRepository _invoices;
    private readonly IReceivableRepository _receivables;
    private readonly IContractRepository _contracts;
    private readonly IFinanceEventRepository _events;
    private readonly TenantGuard _guard;
    private readonly AuditRecorder _audit;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public InvoiceHandler(
        IInvoiceRepository invoices,
        IReceivableRepository receivables,
        IContractRepository contracts,
        IFinanceEventRepository events,
        TenantGuard guard,
        AuditRecorder audit,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _invoices = invoices;
        _receivables = receivables;
        _contracts = contracts;
        _events = events;
        _guard = guard;
        _audit = audit;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Records an invoice against receivables that already exist.
    /// </summary>
    /// <remarks>
    /// An invoice bills what is owed; it does not create it. A receivable exists
    /// because a contract says money is due, and an invoice is one way of asking
    /// for it - a way some payers do not use at all (ADR-0023).
    /// </remarks>
    public async Task<InvoiceId> HandleAsync(
        RecordInvoiceCommand command,
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

        FinanceSupport.RequireParty(contract, command.DebtorPartyId, "debtor");

        if (command.Lines.Count == 0)
        {
            throw new DomainException("An invoice with no lines bills nothing.");
        }

        await RequireReferenceAvailableAsync(
            command.OrganizationId, command.Reference, null, cancellationToken).ConfigureAwait(false);

        Invoice invoice = Invoice.Start(
            command.OrganizationId,
            contract.Id,
            command.DebtorPartyId,
            command.Currency,
            actor,
            _clock.UtcNow,
            command.Reference,
            command.DueOn,
            command.ExternalReference,
            command.Notes);

        IReadOnlyList<Receivable> receivables = await _receivables
            .FindManyAsync(
                command.OrganizationId,
                [.. command.Lines.Select(line => line.ReceivableId).Distinct()],
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<ReceivableId, Receivable> byId = receivables.ToDictionary(x => x.Id);

        foreach (InvoiceLineInput line in command.Lines)
        {
            if (!byId.TryGetValue(line.ReceivableId, out Receivable? receivable))
            {
                throw new EntityNotFoundException(nameof(Receivable), line.ReceivableId.ToString());
            }

            invoice.AddLine(receivable, line.Amount, line.Description, _clock.UtcNow, invoice.Version);
        }

        _invoices.Add(invoice);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.InvoiceRecorded,
            $"Invoice recorded for {invoice.Total}",
            actor,
            _clock.UtcNow,
            contractId: contract.Id,
            invoiceId: invoice.Id,
            amount: invoice.Total));

        _audit.Record(
            AuditAction.InvoiceRecorded,
            entityType: nameof(Invoice),
            entityId: invoice.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceWrite,
            semanticDelta: new
            {
                ContractId = contract.Id.ToString(),
                invoice.CurrencyCodeValue,
                LineCount = invoice.Lines.Count,
                HasReference = invoice.Reference is not null,
            });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return invoice.Id;
    }

    /// <summary>
    /// Records that an invoice was issued.
    /// </summary>
    /// <remarks>
    /// A fact somebody entered. AgencyOS has no transport, sends no email and
    /// attaches no document; delivery is M10's subject (ADR-0023).
    /// </remarks>
    public async Task HandleAsync(
        IssueInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.FinanceWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Invoice invoice = await RequireInvoiceAsync(
            command.OrganizationId, command.InvoiceId, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(command.Reference))
        {
            await RequireReferenceAvailableAsync(
                    command.OrganizationId, command.Reference, invoice.Id, cancellationToken)
                .ConfigureAwait(false);

            invoice.SetReference(command.Reference, _clock.UtcNow, command.ExpectedVersion);
            invoice.Issue(command.IssuedOn, _clock.UtcNow, invoice.Version);
        }
        else
        {
            invoice.Issue(command.IssuedOn, _clock.UtcNow, command.ExpectedVersion);
        }

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.InvoiceIssued,
            $"Invoice {invoice.Reference} recorded as issued",
            actor,
            _clock.UtcNow,
            contractId: invoice.ContractId,
            invoiceId: invoice.Id,
            amount: invoice.Total));

        _audit.Record(
            AuditAction.InvoiceIssued,
            entityType: nameof(Invoice),
            entityId: invoice.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceWrite,
            semanticDelta: new { IssuedOn = command.IssuedOn.ToString("O") });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Withdraws an issued invoice, keeping the record.</summary>
    public async Task HandleAsync(
        VoidInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId actor = await _guard
            .AuthorizeAsync(Permission.FinanceWrite, command.OrganizationId, cancellationToken)
            .ConfigureAwait(false);

        Invoice invoice = await RequireInvoiceAsync(
            command.OrganizationId, command.InvoiceId, cancellationToken).ConfigureAwait(false);

        invoice.Void(command.Reason, _clock.UtcNow, command.ExpectedVersion);

        _events.Add(FinanceEvent.Record(
            command.OrganizationId,
            FinanceEventKind.InvoiceVoided,
            $"Invoice {invoice.Reference} voided",
            actor,
            _clock.UtcNow,
            contractId: invoice.ContractId,
            invoiceId: invoice.Id,
            detail: command.Reason));

        _audit.Record(
            AuditAction.InvoiceVoided,
            entityType: nameof(Invoice),
            entityId: invoice.Id.ToString(),
            organizationId: command.OrganizationId,
            permission: Permission.FinanceWrite,
            semanticDelta: new { command.Reason });

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses an invoice number already in use.
    /// </summary>
    /// <remarks>
    /// AgencyOS does not assign numbers, because invoice numbering carries
    /// statutory weight that varies by jurisdiction. What it does is refuse to hold
    /// the same operator-supplied number twice in one tenant, so a duplicate is
    /// caught rather than filed (ADR-0023).
    /// </remarks>
    private async Task RequireReferenceAvailableAsync(
        OrganizationId organizationId,
        string? reference,
        InvoiceId? excluding,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        if (await _invoices
                .ReferenceExistsAsync(organizationId, reference.Trim(), excluding, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new DomainException(
                $"Invoice number '{reference.Trim()}' is already in use in this organization.");
        }
    }

    private async Task<Invoice> RequireInvoiceAsync(
        OrganizationId organizationId,
        InvoiceId id,
        CancellationToken cancellationToken) =>
        await _invoices.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Invoice), id.ToString());
}
