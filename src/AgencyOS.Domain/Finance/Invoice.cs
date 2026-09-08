using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Domain.Finance;

/// <summary>Opaque, immutable identifier for an <see cref="Invoice"/>.</summary>
public readonly record struct InvoiceId(Guid Value)
{
    public static InvoiceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Where an invoice stands as a document.
/// </summary>
/// <remarks>
/// Deliberately says nothing about payment. Whether an invoice has been paid is
/// arithmetic over the receivables it bills, and a <c>Paid</c> status beside those
/// receivables would be a second fact that can disagree with them (ADR-0023).
/// </remarks>
public enum InvoiceStatus
{
    /// <summary>Being prepared. Lines still editable.</summary>
    Draft = 1,

    /// <summary>Recorded as issued to the debtor. Lines frozen.</summary>
    Issued = 2,

    /// <summary>Withdrawn after issue. The record stays.</summary>
    Void = 3,
}

/// <summary>
/// A billing instrument, when one is actually used.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not every receivable needs an invoice.</strong> A studio paying against
/// a fully executed agreement on a schedule the contract sets does not necessarily
/// receive one, and forcing an invoice into that flow would create a document
/// nobody issued. So an invoice is a separate optional instrument that points at
/// receivables, rather than a required stage between the obligation and the money.
/// </para>
/// <para>
/// <strong>AgencyOS does not send it.</strong> There is no email, no PDF and no
/// delivery status. <c>Issue</c> records that somebody issued it, exactly as an M6
/// submission records that material went out and an M8 notice records that a notice
/// passed. Delivery is M10's subject (ADR-0020, ADR-0022, ADR-0023).
/// </para>
/// <para>
/// <strong>Numbering is the operator's.</strong> AgencyOS assigns no canonical
/// invoice number, because invoice numbering carries statutory weight that varies
/// by jurisdiction and the system is in no position to claim compliance with any of
/// them. The reference is supplied and is unique per organization when present.
/// </para>
/// </remarks>
public sealed class Invoice
{
    private readonly List<InvoiceLine> _lines = [];

    private Invoice()
    {
    }

    public InvoiceId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>
    /// The operator's own invoice number.
    /// </summary>
    /// <remarks>
    /// Supplied, never generated. Unique per organization when present, so a
    /// duplicate is caught, but absent is allowed for a draft nobody has numbered.
    /// </remarks>
    public string? Reference { get; private set; }

    /// <summary>Who is being billed. A contract party.</summary>
    public Guid DebtorPartyId { get; private set; }

    /// <summary>The instrument behind it.</summary>
    public ContractId ContractId { get; private set; }

    public InvoiceStatus Status { get; private set; }

    /// <summary>When it was issued, as reported. Null while it is a draft.</summary>
    public DateOnly? IssuedOn { get; private set; }

    /// <summary>When payment is expected.</summary>
    public DateOnly? DueOn { get; private set; }

    public string CurrencyCodeValue { get; private set; } = string.Empty;

    /// <summary>Where a copy of the document lives, if anywhere. M10 fills this seam.</summary>
    public string? ExternalReference { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Why it was voided. Required when it was.</summary>
    public string? VoidReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<InvoiceLine> Lines => _lines;

    /// <summary>What the lines come to. Derived, never stored.</summary>
    public Money Total =>
        Money.Create(_lines.Sum(line => line.AmountValue), CurrencyCodeValue);

    /// <summary>Whether the lines can still be changed.</summary>
    public bool IsEditable => Status == InvoiceStatus.Draft;

    /// <summary>Starts an invoice.</summary>
    public static Invoice Start(
        OrganizationId organizationId,
        ContractId contractId,
        Guid debtorPartyId,
        string currency,
        UserId createdBy,
        DateTimeOffset now,
        string? reference = null,
        DateOnly? dueOn = null,
        string? externalReference = null,
        string? notes = null) =>
        new()
        {
            Id = InvoiceId.New(),
            OrganizationId = organizationId,
            ContractId = contractId,
            DebtorPartyId = debtorPartyId,
            CurrencyCodeValue = CurrencyCode.Parse(currency).Value,
            Status = InvoiceStatus.Draft,
            Reference = Ensure.OptionalMax(reference, nameof(reference), 100),
            DueOn = dueOn,
            ExternalReference = Ensure.OptionalMax(externalReference, nameof(externalReference), 500),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            CreatedAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now,
            Version = 1,
        };

    /// <summary>
    /// Bills a receivable on this invoice.
    /// </summary>
    /// <remarks>
    /// The line points at the receivable rather than restating its amount
    /// independently, so an invoice cannot bill a figure the receivable does not
    /// carry.
    /// </remarks>
    public InvoiceLine AddLine(
        Receivable receivable,
        Money amount,
        string description,
        DateTimeOffset now,
        int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(receivable);

        RequireVersion(expectedVersion);
        RequireEditable("add lines to");

        if (receivable.OrganizationId != OrganizationId)
        {
            throw new DomainException("An invoice cannot bill a receivable from another tenant.");
        }

        if (amount.Currency.Value != CurrencyCodeValue)
        {
            throw new DomainException(
                $"This invoice is in {CurrencyCodeValue} and that line is in {amount.Currency}.");
        }

        if (amount.Amount > receivable.Outstanding.Amount)
        {
            throw new DomainException(
                $"That would bill {amount} against a receivable with {receivable.Outstanding} "
                + "outstanding.");
        }

        if (_lines.Any(line => line.ReceivableId == receivable.Id))
        {
            throw new DomainException(
                "This invoice already bills that receivable. One line per receivable keeps "
                + "the invoice total reconcilable against what is owed.");
        }

        int sequence = _lines.Count == 0 ? 1 : _lines.Max(line => line.Sequence) + 1;

        InvoiceLine line = InvoiceLine.Create(
            OrganizationId, Id, receivable.Id, amount, description, sequence);

        _lines.Add(line);

        Touch(now);

        return line;
    }

    /// <summary>Removes a line while the invoice is still a draft.</summary>
    public void RemoveLine(Guid lineId, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireEditable("remove lines from");

        InvoiceLine line =
            _lines.FirstOrDefault(x => x.Id == lineId)
            ?? throw new DomainException("That line does not belong to this invoice.");

        _lines.Remove(line);

        Touch(now);
    }

    /// <summary>
    /// Records that the invoice was issued.
    /// </summary>
    /// <remarks>
    /// A fact somebody entered, not an act AgencyOS performed. The verb is
    /// deliberately not "send": the system has no transport and cannot confirm the
    /// debtor received anything.
    /// </remarks>
    public void Issue(DateOnly issuedOn, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status != InvoiceStatus.Draft)
        {
            throw new DomainException(
                $"This invoice is already {Status.ToString().ToLowerInvariant()}.");
        }

        if (_lines.Count == 0)
        {
            throw new DomainException("An invoice with no lines bills nothing.");
        }

        if (string.IsNullOrWhiteSpace(Reference))
        {
            throw new DomainException(
                "An issued invoice needs its number. AgencyOS does not assign one, because "
                + "invoice numbering carries statutory weight it is in no position to claim.");
        }

        if (issuedOn > DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw new DomainException("An invoice cannot have been issued in the future.");
        }

        Status = InvoiceStatus.Issued;
        IssuedOn = issuedOn;

        Touch(now);
    }

    /// <summary>
    /// Withdraws an issued invoice.
    /// </summary>
    /// <remarks>
    /// Void rather than delete. An invoice that went to a debtor is a document that
    /// exists in the world, and removing the record would leave their books and the
    /// agency's disagreeing with no way to find out why.
    /// </remarks>
    public void Void(string reason, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status == InvoiceStatus.Void)
        {
            throw new DomainException("This invoice has already been voided.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Voiding an invoice needs a reason.");
        }

        Status = InvoiceStatus.Void;
        VoidReason = Ensure.NotBlankMax(reason, nameof(reason), 1000);

        Touch(now);
    }

    /// <summary>Records the operator's own invoice number.</summary>
    public void SetReference(string reference, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireEditable("number");

        Reference = Ensure.NotBlankMax(reference, nameof(reference), 100);

        Touch(now);
    }

    private void RequireEditable(string action)
    {
        if (!IsEditable)
        {
            throw new DomainException(
                $"This invoice has been {Status.ToString().ToLowerInvariant()}, so it is not "
                + $"possible to {action} it.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Invoice), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>One receivable billed on an invoice.</summary>
public sealed class InvoiceLine
{
    private InvoiceLine()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public InvoiceId InvoiceId { get; private set; }

    /// <summary>What is being billed. The amount is checked against it.</summary>
    public ReceivableId ReceivableId { get; private set; }

    public decimal AmountValue { get; private set; }

    public string CurrencyCodeValue { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int Sequence { get; private set; }

    public Money Amount => Money.Create(AmountValue, CurrencyCodeValue);

    internal static InvoiceLine Create(
        OrganizationId organizationId,
        InvoiceId invoiceId,
        ReceivableId receivableId,
        Money amount,
        string description,
        int sequence)
    {
        if (amount.Amount <= 0m)
        {
            throw new DomainException("An invoice line of nothing bills nothing.");
        }

        return new InvoiceLine
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            InvoiceId = invoiceId,
            ReceivableId = receivableId,
            AmountValue = amount.Amount,
            CurrencyCodeValue = amount.Currency.Value,
            Description = Ensure.NotBlankMax(description, nameof(description), 500),
            Sequence = sequence,
        };
    }
}

/// <summary>What kind of thing a finance event records.</summary>
/// <remarks>
/// The vocabulary of the curated financial history. One business act produces one
/// entry here even when it writes five rows, so a person reading a contract's
/// finance timeline sees what happened rather than what was persisted (ADR-0012,
/// ADR-0023).
/// </remarks>
public enum FinanceEventKind
{
    ObligationRecorded = 1,
    ObligationQuantified = 2,
    ObligationReleased = 3,
    ReceivableRaised = 4,
    ReceivableWrittenOff = 5,
    ReceivableCancelled = 6,
    InvoiceRecorded = 7,
    InvoiceIssued = 8,
    InvoiceVoided = 9,
    PaymentRecorded = 10,
    PaymentReversed = 11,
    AllocationApplied = 12,
    AllocationReversed = 13,
    AdjustmentRecorded = 14,
    CommissionCalculated = 15,
    CommissionAdjusted = 16,
    JournalPosted = 17,
    JournalReversed = 18,
}

/// <summary>
/// One entry in the curated financial history.
/// </summary>
/// <remarks>
/// Append-only, and distinct from both the audit trail and the ledger. The audit
/// log answers who did what under which permission; the ledger answers what the
/// books say; this answers what happened, in a sentence somebody can read
/// (ADR-0012, ADR-0023).
/// </remarks>
public sealed class FinanceEvent
{
    private FinanceEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public FinanceEventKind Kind { get; private set; }

    /// <summary>The contract it concerns, when it concerns one.</summary>
    public ContractId? ContractId { get; private set; }

    public MonetaryObligationId? MonetaryObligationId { get; private set; }

    public ReceivableId? ReceivableId { get; private set; }

    public InvoiceId? InvoiceId { get; private set; }

    public PaymentId? PaymentId { get; private set; }

    public CommissionEntitlementId? CommissionEntitlementId { get; private set; }

    public JournalEntryId? JournalEntryId { get; private set; }

    /// <summary>What happened, in a sentence.</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>The amount involved, when there was one.</summary>
    public decimal? AmountValue { get; private set; }

    public string? CurrencyCodeValue { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public string? Detail { get; private set; }

    public static FinanceEvent Record(
        OrganizationId organizationId,
        FinanceEventKind kind,
        string summary,
        UserId recordedBy,
        DateTimeOffset now,
        ContractId? contractId = null,
        MonetaryObligationId? obligationId = null,
        ReceivableId? receivableId = null,
        InvoiceId? invoiceId = null,
        PaymentId? paymentId = null,
        CommissionEntitlementId? commissionId = null,
        JournalEntryId? journalEntryId = null,
        Money? amount = null,
        string? detail = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown finance event kind '{kind}'.");
        }

        return new FinanceEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Kind = kind,
            ContractId = contractId,
            MonetaryObligationId = obligationId,
            ReceivableId = receivableId,
            InvoiceId = invoiceId,
            PaymentId = paymentId,
            CommissionEntitlementId = commissionId,
            JournalEntryId = journalEntryId,
            Summary = Ensure.NotBlankMax(summary, nameof(summary), 500),
            AmountValue = amount?.Amount,
            CurrencyCodeValue = amount?.Currency.Value,
            OccurredAt = now,
            RecordedBy = recordedBy,
            Detail = Ensure.OptionalMax(detail, nameof(detail), 1000),
        };
    }
}

/// <summary>
/// Joins an ordinary task to a piece of finance work.
/// </summary>
/// <remarks>
/// The M6, M7 and M8 shape, reused once more. Chasing a receivable is a task; a
/// receivable is not (ADR-0023).
/// </remarks>
public sealed class FinanceTaskLink
{
    private FinanceTaskLink()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public TaskItemId TaskItemId { get; private set; }

    public ReceivableId? ReceivableId { get; private set; }

    public InvoiceId? InvoiceId { get; private set; }

    public PaymentId? PaymentId { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public static FinanceTaskLink Create(
        OrganizationId organizationId,
        TaskItemId taskItemId,
        DateTimeOffset now,
        ReceivableId? receivableId = null,
        InvoiceId? invoiceId = null,
        PaymentId? paymentId = null)
    {
        if (taskItemId.Value == Guid.Empty)
        {
            throw new DomainException("A task link must name a task.");
        }

        if (receivableId is null && invoiceId is null && paymentId is null)
        {
            throw new DomainException(
                "A finance task link must name what the task is about.");
        }

        return new FinanceTaskLink
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            TaskItemId = taskItemId,
            ReceivableId = receivableId,
            InvoiceId = invoiceId,
            PaymentId = paymentId,
            LinkedAt = now,
        };
    }
}
