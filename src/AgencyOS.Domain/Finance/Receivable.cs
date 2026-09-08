using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using AgencyOS.Finance.Rules;

namespace AgencyOS.Domain.Finance;

/// <summary>Opaque, immutable identifier for a <see cref="Receivable"/>.</summary>
public readonly record struct ReceivableId(Guid Value)
{
    public static ReceivableId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Who the money ultimately belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The most consequential field on the row. A payment collected against a client
/// receivable is money the agency holds and owes onward; a payment collected
/// against an agency receivable is the agency's own. Posting the first as revenue
/// would overstate what the agency earned by an order of magnitude (ADR-0023).
/// </para>
/// <para>
/// AgencyOS does not know how the money is actually held - whether there is a
/// separate account, a trust arrangement or none - and makes no claim about it.
/// What it models is the economic entitlement, which is a different and more
/// defensible statement.
/// </para>
/// </remarks>
public enum ReceivableBeneficiary
{
    /// <summary>Owed to the client. The agency collects it and owes it onward.</summary>
    Client = 1,

    /// <summary>Owed to the agency. Commission, a fee, a reimbursement.</summary>
    Agency = 2,
}

/// <summary>
/// Where a receivable stands.
/// </summary>
/// <remarks>
/// Values match <see cref="ReceivableState"/> in the finance kernel. Three of the
/// five are <strong>derived</strong> from allocations and adjustments and are never
/// set by a command; the two terminal ones are deliberate acts.
/// </remarks>
public enum ReceivableStatus
{
    /// <summary>Nothing applied yet.</summary>
    Open = 1,

    /// <summary>Some applied, something remains.</summary>
    PartiallyPaid = 2,

    /// <summary>Nothing remains.</summary>
    Paid = 3,

    /// <summary>Withdrawn before collection.</summary>
    Cancelled = 4,

    /// <summary>Given up as uncollectable, deliberately and with a reason.</summary>
    WrittenOff = 5,
}

/// <summary>
/// A sum the agency expects to collect.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The balance is not a column.</strong> Outstanding is the original less
/// what allocations have applied and less what adjustments have removed, computed
/// by the finance kernel every time it is asked. A mutable balance drifts the
/// moment two code paths update it differently, and the drift is invisible because
/// the number still looks like a number (ADR-0023).
/// </para>
/// <para>
/// The same is true of status and of overdue. Both are derived. A receivable with
/// no resolvable due date is never overdue, because the contract did not say when.
/// </para>
/// <para>
/// The two figures the row does carry - <see cref="AllocatedAmountValue"/> and
/// <see cref="AdjustedAmountValue"/> - are maintained only through the aggregate's
/// own methods, which the allocation and adjustment handlers call inside the same
/// transaction as the rows they summarise. They are a running total of facts
/// stored elsewhere, and an integration test reconciles them against those facts.
/// </para>
/// </remarks>
public sealed class Receivable
{
    private Receivable()
    {
    }

    public ReceivableId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The obligation it was raised from. Required: nothing is owed for no reason.</summary>
    public MonetaryObligationId MonetaryObligationId { get; private set; }

    /// <summary>The instrument behind it, denormalised for the work queues.</summary>
    public ContractId ContractId { get; private set; }

    /// <summary>Who owes it. A party to the contract.</summary>
    public Guid PayerPartyId { get; private set; }

    /// <summary>Whose money it is once collected.</summary>
    public ReceivableBeneficiary Beneficiary { get; private set; }

    /// <summary>The client it belongs to, for a client receivable.</summary>
    public Guid? ClientPersonId { get; private set; }

    /// <summary>The representation it arises under, for commission lineage.</summary>
    public Guid? RepresentationId { get; private set; }

    /// <summary>What was originally expected. Never changed.</summary>
    public decimal OriginalAmountValue { get; private set; }

    public string CurrencyCodeValue { get; private set; } = string.Empty;

    /// <summary>A running total of applied allocations. Maintained by this aggregate only.</summary>
    public decimal AllocatedAmountValue { get; private set; }

    /// <summary>A running total of deductions and write-offs. Same rule.</summary>
    public decimal AdjustedAmountValue { get; private set; }

    /// <summary>When it falls due, or null when nothing resolvable says.</summary>
    public DateOnly? DueOn { get; private set; }

    /// <summary>The agency's own handle for it.</summary>
    public string? Reference { get; private set; }

    /// <summary>Whether it was closed by an act rather than by arithmetic.</summary>
    public bool IsClosedByAct { get; private set; }

    /// <summary>True for a write-off, false for a cancellation. Meaningless unless closed.</summary>
    public bool IsWriteOff { get; private set; }

    /// <summary>Why it was written off or cancelled. Required for either.</summary>
    public string? ClosureReason { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (ADR-0014).</summary>
    public int Version { get; private set; }

    /// <summary>What was originally expected.</summary>
    public Money OriginalAmount => Money.Create(OriginalAmountValue, CurrencyCodeValue);

    /// <summary>What is still owed. Derived, every time.</summary>
    public Money Outstanding =>
        Money.Create(FinanceRules.Outstanding(ToRulesInput()), CurrencyCodeValue);

    /// <summary>Where it stands. Derived from the arithmetic and the closure acts.</summary>
    public ReceivableStatus Status => (ReceivableStatus)FinanceRules.ReceivableState(ToRulesInput());

    /// <summary>Whether it can still take money.</summary>
    public bool AcceptsAllocation => FinanceRules.AcceptsAllocation(ToRulesInput());

    /// <summary>
    /// Whether it is past its date with something still owed.
    /// </summary>
    /// <remarks>
    /// Derived, and false when there is no resolvable due date. The M8 principle
    /// carried into finance: asserting lateness against a date nobody agreed would
    /// be an invention (ADR-0022, ADR-0023).
    /// </remarks>
    public bool IsOverdueOn(DateOnly today) => FinanceRules.IsOverdue(ToRulesInput(), today);

    /// <summary>Raises a sum the agency expects to collect.</summary>
    public static Receivable Raise(
        OrganizationId organizationId,
        MonetaryObligationId obligationId,
        ContractId contractId,
        Guid payerPartyId,
        ReceivableBeneficiary beneficiary,
        Money amount,
        UserId createdBy,
        DateTimeOffset now,
        DateOnly? dueOn = null,
        Guid? clientPersonId = null,
        Guid? representationId = null,
        string? reference = null,
        string? notes = null)
    {
        if (!Enum.IsDefined(beneficiary))
        {
            throw new DomainException($"Unknown beneficiary '{beneficiary}'.");
        }

        if (amount.Amount <= 0m)
        {
            throw new DomainException(
                "A receivable of nothing expects nothing. Record the obligation and leave it "
                + "unbilled instead.");
        }

        // A client receivable that names no client cannot be attributed, and the
        // money it collects has nowhere to go.
        if (beneficiary == ReceivableBeneficiary.Client && clientPersonId is null)
        {
            throw new DomainException(
                "A receivable collected on a client's behalf must name the client, "
                + "or the money it brings in cannot be attributed to anybody.");
        }

        return new Receivable
        {
            Id = ReceivableId.New(),
            OrganizationId = organizationId,
            MonetaryObligationId = obligationId,
            ContractId = contractId,
            PayerPartyId = payerPartyId,
            Beneficiary = beneficiary,
            ClientPersonId = clientPersonId,
            RepresentationId = representationId,
            OriginalAmountValue = amount.Amount,
            CurrencyCodeValue = amount.Currency.Value,
            AllocatedAmountValue = 0m,
            AdjustedAmountValue = 0m,
            DueOn = dueOn,
            Reference = Ensure.OptionalMax(reference, nameof(reference), 100),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            IsClosedByAct = false,
            IsWriteOff = false,
            CreatedAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>
    /// Records that an allocation applied money to this receivable.
    /// </summary>
    /// <remarks>
    /// Called by the allocation handler inside the same transaction as the
    /// allocation row. The kernel has already checked the arithmetic; this refuses
    /// again, because a running total that can be pushed negative is worse than no
    /// running total.
    /// </remarks>
    public void ApplyAllocation(Money amount, DateTimeOffset now)
    {
        RequireOpen("apply money to");
        RequireSameCurrency(amount);

        if (amount.Amount <= 0m)
        {
            throw new DomainException("An allocation of nothing applies nothing.");
        }

        if (amount.Amount > Outstanding.Amount)
        {
            throw new DomainException(
                $"That would apply {amount} against an outstanding balance of {Outstanding}.");
        }

        AllocatedAmountValue += amount.Amount;

        Touch(now);
    }

    /// <summary>Records that an allocation was reversed and its money returned.</summary>
    public void ReverseAllocation(Money amount, DateTimeOffset now)
    {
        RequireSameCurrency(amount);

        if (amount.Amount > AllocatedAmountValue)
        {
            throw new DomainException(
                "Reversing more than has been allocated would leave this receivable claiming "
                + "money it never received.");
        }

        AllocatedAmountValue -= amount.Amount;

        Touch(now);
    }

    /// <summary>
    /// Records a deduction against what is owed.
    /// </summary>
    /// <remarks>
    /// A withholding, a bank charge or a fee: money that will never arrive and is
    /// accounted for rather than chased. It reduces what is outstanding without
    /// pretending cash was received.
    /// </remarks>
    public void ApplyAdjustment(Money amount, DateTimeOffset now)
    {
        RequireOpen("adjust");
        RequireSameCurrency(amount);

        if (amount.Amount <= 0m)
        {
            throw new DomainException("An adjustment of nothing adjusts nothing.");
        }

        if (amount.Amount > Outstanding.Amount)
        {
            throw new DomainException(
                $"That would adjust {amount} against an outstanding balance of {Outstanding}.");
        }

        AdjustedAmountValue += amount.Amount;

        Touch(now);
    }

    /// <summary>Records that an adjustment was reversed.</summary>
    public void ReverseAdjustment(Money amount, DateTimeOffset now)
    {
        RequireSameCurrency(amount);

        if (amount.Amount > AdjustedAmountValue)
        {
            throw new DomainException("Reversing more than has been adjusted is not possible.");
        }

        AdjustedAmountValue -= amount.Amount;

        Touch(now);
    }

    /// <summary>
    /// Gives up on collecting what remains.
    /// </summary>
    /// <remarks>
    /// A financial act, not data cleanup. The original amount stays exactly what it
    /// was, the reason is required, and the ledger consequence is posted by the
    /// handler. Deleting the row would lose both the fact that the agency expected
    /// the money and the decision that it stopped (ADR-0023).
    /// </remarks>
    public Money WriteOff(DateTimeOffset now, int expectedVersion, string reason)
    {
        RequireVersion(expectedVersion);
        RequireOpen("write off");

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Writing off a receivable needs a reason. Giving up on money is a decision "
                + "somebody made, and the record should say who and why.");
        }

        Money remaining = Outstanding;

        if (remaining.Amount <= 0m)
        {
            throw new DomainException("Nothing is outstanding on this receivable to write off.");
        }

        IsClosedByAct = true;
        IsWriteOff = true;
        ClosureReason = Ensure.NotBlankMax(reason, nameof(reason), 1000);

        Touch(now);

        return remaining;
    }

    /// <summary>Withdraws a receivable raised in error, before anything was collected.</summary>
    public void Cancel(DateTimeOffset now, int expectedVersion, string reason)
    {
        RequireVersion(expectedVersion);
        RequireOpen("cancel");

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Cancelling a receivable needs a reason.");
        }

        if (AllocatedAmountValue > 0m)
        {
            throw new DomainException(
                "Money has been applied to this receivable, so it cannot be cancelled. "
                + "Reverse the allocations first, or write off what remains.");
        }

        IsClosedByAct = true;
        IsWriteOff = false;
        ClosureReason = Ensure.NotBlankMax(reason, nameof(reason), 1000);

        Touch(now);
    }

    /// <summary>Records the due date once an obligation's rule resolves to one.</summary>
    public void RecordDueDate(DateOnly dueOn, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        DueOn = dueOn;

        Touch(now);
    }

    /// <summary>The flat shape the finance kernel reasons about.</summary>
    public ReceivableInput ToRulesInput() =>
        new()
        {
            Id = Id.Value,
            OriginalAmount = OriginalAmountValue,
            Currency = CurrencyCodeValue,
            AllocatedAmount = AllocatedAmountValue,
            AdjustedAmount = AdjustedAmountValue,
            IsClosedByAct = IsClosedByAct,
            IsWriteOff = IsWriteOff,
            DueOn = DueOn,
        };

    private void RequireOpen(string action)
    {
        if (IsClosedByAct)
        {
            string state = IsWriteOff ? "written off" : "cancelled";

            throw new DomainException(
                $"This receivable has been {state}, so it is not possible to {action} it.");
        }
    }

    private void RequireSameCurrency(Money amount)
    {
        if (amount.Currency.Value != CurrencyCodeValue)
        {
            throw new DomainException(
                $"This receivable is in {CurrencyCodeValue} and that amount is in "
                + $"{amount.Currency}. AgencyOS holds no exchange rates, so the two cannot meet.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(Receivable), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
