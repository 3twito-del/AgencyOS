using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Finance.Rules;

namespace AgencyOS.Domain.Finance;

/// <summary>Opaque, immutable identifier for a <see cref="Payment"/>.</summary>
public readonly record struct PaymentId(Guid Value)
{
    public static PaymentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for a <see cref="PaymentAllocation"/>.</summary>
public readonly record struct PaymentAllocationId(Guid Value)
{
    public static PaymentAllocationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Which way money moved.</summary>
public enum PaymentDirection
{
    /// <summary>Money the agency received.</summary>
    Incoming = 1,

    /// <summary>Money the agency paid out, including to a client.</summary>
    Outgoing = 2,
}

/// <summary>How the money moved, as reported.</summary>
/// <remarks>
/// What somebody told AgencyOS, never what a bank confirmed. There is no
/// settlement status here because the system has no way to observe settlement
/// (ADR-0023).
/// </remarks>
public enum PaymentMethod
{
    BankTransfer = 1,
    Cheque = 2,
    Card = 3,
    Cash = 4,
    Offset = 5,
    Other = 99,
}

/// <summary>Where a payment stands.</summary>
/// <remarks>
/// Two states and no draft. Recording a payment <em>is</em> the act of asserting
/// that money moved; a draft payment would be a claim nobody has made yet, and the
/// place for that is not writing the row (ADR-0023).
/// </remarks>
public enum PaymentStatus
{
    /// <summary>Asserted as a fact. Immutable in its financial particulars.</summary>
    Recorded = 1,

    /// <summary>Undone by a reversing record. The original still says what it said.</summary>
    Reversed = 2,
}

/// <summary>What a deduction was.</summary>
/// <remarks>
/// Factual categories only. <see cref="Withholding"/> records that an amount was
/// withheld, and says nothing about whether it was correctly withheld or what it
/// will be credited against - AgencyOS implements no tax engine and infers no tax
/// liability (ADR-0023).
/// </remarks>
public enum PaymentAdjustmentKind
{
    /// <summary>An amount the payer withheld and remitted elsewhere, as reported.</summary>
    Withholding = 1,

    /// <summary>A bank charge deducted from the transfer.</summary>
    BankFee = 2,

    /// <summary>A wire or intermediary charge.</summary>
    WireFee = 3,

    /// <summary>A discount or settlement reduction the parties agreed.</summary>
    AgreedReduction = 4,

    /// <summary>An amount the agency gave up as uncollectable.</summary>
    WriteOff = 5,

    /// <summary>Something the vocabulary does not name. The description carries it.</summary>
    Other = 99,
}

/// <summary>
/// Money that actually moved.
/// </summary>
/// <remarks>
/// <para>
/// The evidential row of the milestone. Everything else in finance is an
/// expectation, a calculation or a consequence; this is the assertion that a sum
/// changed hands on a day.
/// </para>
/// <para>
/// Its financial particulars - amount, currency and the date it was received - are
/// <strong>immutable once recorded</strong>. A typo is corrected by reversing the
/// payment and recording the right one, so both the mistake and the correction
/// survive with their own dates and actors. Editing them in place would rewrite
/// what the agency is recorded as having observed (ADR-0023).
/// </para>
/// <para>
/// A payment is never inferred. An invoice marked paid does not create one, and a
/// receivable reaching zero does not either: those are consequences of a payment
/// existing, not evidence that one does.
/// </para>
/// </remarks>
public sealed class Payment
{
    private readonly List<PaymentAllocation> _allocations = [];

    private Payment()
    {
    }

    public PaymentId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PaymentDirection Direction { get; private set; }

    /// <summary>Who paid. A contract party, a company or a person, by reference.</summary>
    public Guid? PayerPartyId { get; private set; }

    /// <summary>Who paid, when the payer is not a contract party.</summary>
    public string? PayerName { get; private set; }

    /// <summary>Who was paid, for an outgoing payment.</summary>
    public Guid? PayeePartyId { get; private set; }

    public string? PayeeName { get; private set; }

    /// <summary>Immutable once recorded.</summary>
    public decimal AmountValue { get; private set; }

    /// <summary>Immutable once recorded.</summary>
    public string CurrencyCodeValue { get; private set; } = string.Empty;

    /// <summary>
    /// The day the money moved, as reported. Immutable once recorded.
    /// </summary>
    /// <remarks>
    /// Freely backdated and deliberately distinct from <see cref="RecordedAt"/>.
    /// A wire that landed on Friday and was entered on Monday happened on Friday
    /// (ADR-0023).
    /// </remarks>
    public DateOnly ReceivedOn { get; private set; }

    /// <summary>When AgencyOS was told.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    public PaymentMethod Method { get; private set; }

    /// <summary>
    /// The payer's or bank's own identifier for the movement.
    /// </summary>
    /// <remarks>
    /// Not assumed unique. Two genuinely different payments can carry the same
    /// remittance text, so this supports duplicate <em>detection</em> and is never
    /// used as an identity (ADR-0023).
    /// </remarks>
    public string? ExternalReference { get; private set; }

    /// <summary>Where the reference came from. The seam a future bank import fills.</summary>
    public string? SourceSystem { get; private set; }

    public string? Notes { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>The payment that reversed this one.</summary>
    public PaymentId? ReversedByPaymentId { get; private set; }

    /// <summary>The payment this one reverses.</summary>
    public PaymentId? ReversalOfPaymentId { get; private set; }

    /// <summary>Why it was reversed. Required when it is.</summary>
    public string? ReversalReason { get; private set; }

    public UserId RecordedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<PaymentAllocation> Allocations => _allocations;

    /// <summary>What moved.</summary>
    public Money Amount => Money.Create(AmountValue, CurrencyCodeValue);

    /// <summary>What has been applied to something.</summary>
    public Money Allocated =>
        Money.Create(
            _allocations.Where(x => x.IsApplied).Sum(x => x.AmountValue), CurrencyCodeValue);

    /// <summary>
    /// What has not.
    /// </summary>
    /// <remarks>
    /// Derived, and never discarded. Ten thousand left over from a hundred-thousand
    /// payment is cash the agency holds; a system that dropped it would be short of
    /// its own bank account (ADR-0023).
    /// </remarks>
    public Money Unapplied =>
        Money.Create(
            FinanceRules.Unapplied(AmountValue, ToRulesAllocations()), CurrencyCodeValue);

    /// <summary>Whether any of it is still free to apply.</summary>
    public bool HasUnappliedCash =>
        Status == PaymentStatus.Recorded && Unapplied.Amount > 0m;

    /// <summary>Whether it can still take allocations.</summary>
    public bool AcceptsAllocation => Status == PaymentStatus.Recorded;

    /// <summary>Whether it is a reversal of something.</summary>
    public bool IsReversal => ReversalOfPaymentId is not null;

    /// <summary>Records money that moved.</summary>
    public static Payment Record(
        OrganizationId organizationId,
        PaymentDirection direction,
        Money amount,
        DateOnly receivedOn,
        PaymentMethod method,
        UserId recordedBy,
        DateTimeOffset now,
        Guid? payerPartyId = null,
        string? payerName = null,
        Guid? payeePartyId = null,
        string? payeeName = null,
        string? externalReference = null,
        string? sourceSystem = null,
        string? notes = null)
    {
        foreach ((bool defined, string name) in new[]
        {
            (Enum.IsDefined(direction), nameof(direction)),
            (Enum.IsDefined(method), nameof(method)),
        })
        {
            if (!defined)
            {
                throw new DomainException($"Unknown {name} on a payment.");
            }
        }

        if (amount.Amount <= 0m)
        {
            throw new DomainException("A payment of nothing records a movement that did not happen.");
        }

        if (receivedOn > DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw new DomainException(
                "A payment cannot have been received in the future. "
                + "An expected payment is a receivable, not a payment.");
        }

        if (payerPartyId is null && string.IsNullOrWhiteSpace(payerName))
        {
            throw new DomainException("A payment must say who paid.");
        }

        return new Payment
        {
            Id = PaymentId.New(),
            OrganizationId = organizationId,
            Direction = direction,
            PayerPartyId = payerPartyId,
            PayerName = Ensure.OptionalMax(payerName, nameof(payerName), 300),
            PayeePartyId = payeePartyId,
            PayeeName = Ensure.OptionalMax(payeeName, nameof(payeeName), 300),
            AmountValue = amount.Amount,
            CurrencyCodeValue = amount.Currency.Value,
            ReceivedOn = receivedOn,
            RecordedAt = now,
            Method = method,
            ExternalReference = Ensure.OptionalMax(externalReference, nameof(externalReference), 200),
            SourceSystem = Ensure.OptionalMax(sourceSystem, nameof(sourceSystem), 100),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            Status = PaymentStatus.Recorded,
            RecordedBy = recordedBy,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Applies part or all of this payment to a receivable.
    /// </summary>
    /// <remarks>
    /// The kernel validates against the payment's remaining funds and the
    /// receivable's outstanding balance before the row is created, so an
    /// over-allocation is impossible from either side. The receivable's own running
    /// total is updated by the handler in the same transaction.
    /// </remarks>
    public PaymentAllocation Allocate(
        Receivable receivable,
        Money amount,
        DateTimeOffset now,
        int expectedVersion,
        UserId actor,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(receivable);

        RequireVersion(expectedVersion);

        if (Status != PaymentStatus.Recorded)
        {
            throw new DomainException(
                "This payment has been reversed, so it has nothing left to apply.");
        }

        if (receivable.OrganizationId != OrganizationId)
        {
            throw new DomainException("A payment cannot be applied across tenants.");
        }

        if (FinanceRules.DescribeAllocationProblem(
                AmountValue,
                CurrencyCodeValue,
                ToRulesAllocations(),
                receivable.ToRulesInput(),
                amount.Amount) is { } problem)
        {
            throw new DomainException(problem);
        }

        PaymentAllocation allocation = PaymentAllocation.Create(
            OrganizationId, Id, receivable.Id, amount, now, actor, notes);

        _allocations.Add(allocation);

        Touch(now);

        return allocation;
    }

    /// <summary>
    /// Reverses one allocation, returning its money to unapplied.
    /// </summary>
    /// <remarks>
    /// The line stays, marked reversed, because it is a record of what somebody
    /// once decided this money was for. Deleting it would leave the receivable's
    /// history with an unexplained gap.
    /// </remarks>
    public PaymentAllocation ReverseAllocation(
        PaymentAllocationId allocationId,
        DateTimeOffset now,
        int expectedVersion,
        UserId actor,
        string reason)
    {
        RequireVersion(expectedVersion);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Reversing an allocation needs a reason.");
        }

        PaymentAllocation allocation =
            _allocations.FirstOrDefault(x => x.Id == allocationId)
            ?? throw new DomainException("That allocation does not belong to this payment.");

        allocation.Reverse(now, actor, reason);

        Touch(now);

        return allocation;
    }

    /// <summary>
    /// Marks this payment reversed by another.
    /// </summary>
    /// <remarks>
    /// Called on the original when its reversal is recorded. The original's amount,
    /// currency and date are untouched: what the agency observed on the day is
    /// exactly what it still says it observed.
    /// </remarks>
    public void NoteReversedBy(PaymentId reversal, string reason, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Reversed)
        {
            throw new DomainException("This payment has already been reversed.");
        }

        if (reversal == Id)
        {
            throw new DomainException("A payment cannot reverse itself.");
        }

        if (_allocations.Any(x => x.IsApplied))
        {
            throw new DomainException(
                "This payment still has money applied to receivables. Reverse the allocations "
                + "first, so each receivable's balance is corrected on its own record.");
        }

        Status = PaymentStatus.Reversed;
        ReversedByPaymentId = reversal;
        ReversalReason = Ensure.NotBlankMax(reason, nameof(reason), 1000);

        Touch(now);
    }

    /// <summary>Marks this payment as the reversal of another.</summary>
    public void NoteReversalOf(PaymentId original, DateTimeOffset now)
    {
        if (original == Id)
        {
            throw new DomainException("A payment cannot reverse itself.");
        }

        ReversalOfPaymentId = original;

        Touch(now);
    }

    /// <summary>The flat shapes the finance kernel reasons about.</summary>
    public AllocationInput[] ToRulesAllocations() =>
        [.. _allocations.Select(x => x.ToRulesInput())];

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Payment), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>
/// How much of a payment went to which receivable.
/// </summary>
/// <remarks>
/// A row rather than a column on the payment, because the relationship is many to
/// many in both directions: one payment can settle several receivables and one
/// receivable can be settled by several payments. A <c>Payment.ReceivableId</c>
/// would have made the ordinary case unrepresentable (ADR-0023).
/// </remarks>
public sealed class PaymentAllocation
{
    private PaymentAllocation()
    {
    }

    public PaymentAllocationId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PaymentId PaymentId { get; private set; }

    public ReceivableId ReceivableId { get; private set; }

    public decimal AmountValue { get; private set; }

    public string CurrencyCodeValue { get; private set; } = string.Empty;

    /// <summary>Whether it still counts. A reversed line is history, not money.</summary>
    public bool IsApplied { get; private set; }

    public DateTimeOffset AppliedAt { get; private set; }

    public UserId AppliedBy { get; private set; }

    /// <summary>When it was reversed, if it was.</summary>
    public DateTimeOffset? ReversedAt { get; private set; }

    public UserId? ReversedBy { get; private set; }

    /// <summary>Why. Required when reversed.</summary>
    public string? ReversalReason { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>How much this line applied.</summary>
    public Money Amount => Money.Create(AmountValue, CurrencyCodeValue);

    internal static PaymentAllocation Create(
        OrganizationId organizationId,
        PaymentId paymentId,
        ReceivableId receivableId,
        Money amount,
        DateTimeOffset now,
        UserId actor,
        string? notes)
    {
        if (amount.Amount <= 0m)
        {
            throw new DomainException("An allocation of nothing applies nothing.");
        }

        return new PaymentAllocation
        {
            Id = PaymentAllocationId.New(),
            OrganizationId = organizationId,
            PaymentId = paymentId,
            ReceivableId = receivableId,
            AmountValue = amount.Amount,
            CurrencyCodeValue = amount.Currency.Value,
            IsApplied = true,
            AppliedAt = now,
            AppliedBy = actor,
            Notes = Ensure.OptionalMax(notes, nameof(notes), 1000),
        };
    }

    internal void Reverse(DateTimeOffset now, UserId actor, string reason)
    {
        if (!IsApplied)
        {
            throw new DomainException("This allocation has already been reversed.");
        }

        IsApplied = false;
        ReversedAt = now;
        ReversedBy = actor;
        ReversalReason = Ensure.NotBlankMax(reason, nameof(reason), 1000);
    }

    /// <summary>The flat shape the finance kernel reasons about.</summary>
    public AllocationInput ToRulesInput() =>
        new()
        {
            ReceivableId = ReceivableId.Value,
            Amount = AmountValue,
            Currency = CurrencyCodeValue,
            IsApplied = IsApplied,
        };
}

/// <summary>Opaque, immutable identifier for a <see cref="PaymentAdjustment"/>.</summary>
public readonly record struct PaymentAdjustmentId(Guid Value)
{
    public static PaymentAdjustmentId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Money that will never arrive, and why.
/// </summary>
/// <remarks>
/// <para>
/// If a hundred thousand is owed and ninety-seven and a half arrives, the missing
/// two and a half is either something somebody can name or an unexplained
/// variance. This row is where the nameable ones live.
/// </para>
/// <para>
/// It records a fact somebody entered, never an inference. A gap with no adjustment
/// against it stays a gap: attributing it to withholding would produce a reconciled
/// receivable and a wrong tax record, and nobody would look at either again
/// (ADR-0023).
/// </para>
/// </remarks>
public sealed class PaymentAdjustment
{
    private PaymentAdjustment()
    {
    }

    public PaymentAdjustmentId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The receivable it reduces.</summary>
    public ReceivableId ReceivableId { get; private set; }

    /// <summary>The payment it arrived with, when it did.</summary>
    public PaymentId? PaymentId { get; private set; }

    public PaymentAdjustmentKind Kind { get; private set; }

    public decimal AmountValue { get; private set; }

    public string CurrencyCodeValue { get; private set; } = string.Empty;

    /// <summary>What it was, in the words of whoever recorded it.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>A withholding certificate number, a bank advice reference.</summary>
    public string? ExternalReference { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    /// <summary>Whether it still counts.</summary>
    public bool IsApplied { get; private set; }

    public DateTimeOffset? ReversedAt { get; private set; }

    public string? ReversalReason { get; private set; }

    public Money Amount => Money.Create(AmountValue, CurrencyCodeValue);

    /// <summary>Records a deduction as a fact.</summary>
    public static PaymentAdjustment Record(
        OrganizationId organizationId,
        ReceivableId receivableId,
        PaymentAdjustmentKind kind,
        Money amount,
        string description,
        DateOnly occurredOn,
        UserId recordedBy,
        DateTimeOffset now,
        PaymentId? paymentId = null,
        string? externalReference = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"Unknown adjustment kind '{kind}'.");
        }

        if (amount.Amount <= 0m)
        {
            throw new DomainException("An adjustment of nothing adjusts nothing.");
        }

        return new PaymentAdjustment
        {
            Id = PaymentAdjustmentId.New(),
            OrganizationId = organizationId,
            ReceivableId = receivableId,
            PaymentId = paymentId,
            Kind = kind,
            AmountValue = amount.Amount,
            CurrencyCodeValue = amount.Currency.Value,
            Description = Ensure.NotBlankMax(description, nameof(description), 1000),
            ExternalReference = Ensure.OptionalMax(externalReference, nameof(externalReference), 200),
            OccurredOn = occurredOn,
            RecordedAt = now,
            RecordedBy = recordedBy,
            IsApplied = true,
        };
    }

    /// <summary>Undoes an adjustment recorded in error.</summary>
    public void Reverse(DateTimeOffset now, string reason)
    {
        if (!IsApplied)
        {
            throw new DomainException("This adjustment has already been reversed.");
        }

        IsApplied = false;
        ReversedAt = now;
        ReversalReason = Ensure.NotBlankMax(reason, nameof(reason), 1000);
    }
}
