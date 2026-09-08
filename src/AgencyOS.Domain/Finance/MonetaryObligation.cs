using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Finance;

/// <summary>Opaque, immutable identifier for a <see cref="MonetaryObligation"/>.</summary>
public readonly record struct MonetaryObligationId(Guid Value)
{
    public static MonetaryObligationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What kind of money the contract says changes hands.
/// </summary>
/// <remarks>
/// Categories the agency actually distinguishes when chasing payment, not an
/// accounting taxonomy. What the money is <em>for</em> is a legal fact M8 records
/// on the term; this says what shape it arrives in.
/// </remarks>
public enum ObligationCategory
{
    /// <summary>A guarantee, fee or salary payable on a stated date.</summary>
    Compensation = 1,

    /// <summary>One instalment of a larger guaranteed sum.</summary>
    Instalment = 2,

    /// <summary>Payable per episode, week, day or other unit of service.</summary>
    Episodic = 3,

    /// <summary>Payable on signature.</summary>
    SigningPayment = 4,

    /// <summary>Payable if a stated condition occurs.</summary>
    Bonus = 5,

    /// <summary>Payable later than the work, by agreement.</summary>
    Deferred = 6,

    /// <summary>Payable on an option being exercised.</summary>
    OptionPayment = 7,

    /// <summary>Reimbursement of stated costs.</summary>
    Expense = 8,

    /// <summary>
    /// A share of receipts.
    /// </summary>
    /// <remarks>
    /// Recorded so the entitlement exists in the system, and deliberately never
    /// calculated. AgencyOS has no statements, no defined gross or net basis and no
    /// distribution inputs, so a number here would be invented (ADR-0023).
    /// </remarks>
    Participation = 9,

    /// <summary>Something the vocabulary does not name.</summary>
    Other = 99,
}

/// <summary>
/// How much is due, and whether that is a question the system can answer.
/// </summary>
/// <remarks>
/// The four honest answers. A contingent bonus and an unquantified backend
/// participation are both real obligations with no number attached, and recording
/// either as zero would put a false figure into every total that touches it
/// (ADR-0023).
/// </remarks>
public enum ObligationAmountKind
{
    /// <summary>A stated sum.</summary>
    Fixed = 1,

    /// <summary>A quantity at a unit rate: ten episodes at fifty thousand.</summary>
    Formula = 2,

    /// <summary>A stated sum, payable only if something happens.</summary>
    Contingent = 3,

    /// <summary>Genuinely not calculable from what AgencyOS holds.</summary>
    Unknown = 4,
}

/// <summary>Where a monetary obligation stands.</summary>
/// <remarks>
/// Deliberately short: whether it has been billed, collected or written off are
/// facts about the receivable it produced, not about the obligation itself.
/// </remarks>
public enum MonetaryObligationStatus
{
    /// <summary>Recorded and expected.</summary>
    Expected = 1,

    /// <summary>A receivable has been raised for it.</summary>
    Raised = 2,

    /// <summary>The condition it depended on did not occur, or it was released.</summary>
    Released = 3,

    /// <summary>Recorded in error.</summary>
    Cancelled = 4,
}

/// <summary>
/// A sum the contract says somebody must pay.
/// </summary>
/// <remarks>
/// <para>
/// The bridge between M8's legal record and M9's money. It is created from an
/// <strong>operative</strong> contract - one that has been executed or has a
/// recorded effective date - and it points back at the M8 obligation or contract
/// term that produced it, so the answer to "why does the agency think it is owed
/// this" is a link rather than an assertion (ADR-0023).
/// </para>
/// <para>
/// It duplicates no legal meaning. What the money is for, who owes it and under
/// what clause all remain M8's. What this adds is the amount, the currency and the
/// date, expressed in a form finance can act on.
/// </para>
/// <para>
/// The due date is an M8 <see cref="DeadlineRule"/>, reused rather than reinvented,
/// so a payment date measured from delivery resolves honestly or not at all -
/// exactly as the delivery obligation it came from does.
/// </para>
/// </remarks>
public sealed class MonetaryObligation
{
    private MonetaryObligation()
    {
    }

    public MonetaryObligationId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The instrument that created it. Required.</summary>
    public ContractId ContractId { get; private set; }

    /// <summary>The version it was read out of.</summary>
    public ContractVersionId ContractVersionId { get; private set; }

    /// <summary>The M8 obligation it implements, when it implements one.</summary>
    public ObligationId? SourceObligationId { get; private set; }

    /// <summary>The contract term that states the figure, when one does.</summary>
    public ContractTermCode? SourceTermCode { get; private set; }

    /// <summary>Who owes it. A party to the contract.</summary>
    public Guid PayerPartyId { get; private set; }

    /// <summary>Who is owed it. A party to the contract.</summary>
    public Guid PayeePartyId { get; private set; }

    public ObligationCategory Category { get; private set; }

    public ObligationAmountKind AmountKind { get; private set; }

    /// <summary>What is due, when that is known. Null is a real answer.</summary>
    public decimal? AmountValue { get; private set; }

    /// <summary>What it is denominated in. Present whenever any figure is.</summary>
    public string? CurrencyCodeValue { get; private set; }

    /// <summary>How many units, for a formula obligation.</summary>
    public int? Quantity { get; private set; }

    /// <summary>What one unit pays, for a formula obligation.</summary>
    public decimal? UnitAmountValue { get; private set; }

    /// <summary>What the quantity counts: episodes, weeks, days.</summary>
    public TermUnit? Unit { get; private set; }

    /// <summary>The condition a contingent obligation waits on, in the contract's words.</summary>
    public string? Condition { get; private set; }

    /// <summary>When it falls due, as the contract expresses it.</summary>
    public DeadlineRule Due { get; private set; } = null!;

    /// <summary>
    /// The date the rule resolved to, when it resolved.
    /// </summary>
    /// <remarks>
    /// A cache of a pure function, recomputed when the anchor becomes known. Null
    /// means the contract's own rule produced no date, which is a fact rather than
    /// missing data.
    /// </remarks>
    public DateOnly? ResolvedDueOn { get; private set; }

    public MonetaryObligationStatus Status { get; private set; }

    public string? Description { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public UserId RecordedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (ADR-0014).</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Whether a collectible figure can be worked out.
    /// </summary>
    /// <remarks>
    /// True only for a fixed sum or a complete formula. A contingent obligation
    /// whose condition has not occurred, and a participation nobody can value, both
    /// answer false - and no receivable may be raised from either (ADR-0023).
    /// </remarks>
    public bool IsQuantified =>
        AmountKind switch
        {
            ObligationAmountKind.Fixed => AmountValue is not null,
            ObligationAmountKind.Formula => Quantity is not null && UnitAmountValue is not null,
            _ => false,
        };

    /// <summary>What is actually due, worked out from whichever fields carry it.</summary>
    public Money? Amount
    {
        get
        {
            if (CurrencyCodeValue is not { } currency)
            {
                return null;
            }

            return AmountKind switch
            {
                ObligationAmountKind.Fixed when AmountValue is { } fixedAmount =>
                    Money.Create(fixedAmount, currency),

                ObligationAmountKind.Formula
                    when Quantity is { } quantity && UnitAmountValue is { } unit =>
                    Money.Create(quantity * unit, currency),

                _ => null,
            };
        }
    }

    /// <summary>Whether a receivable may still be raised against it.</summary>
    public bool AcceptsReceivable =>
        Status == MonetaryObligationStatus.Expected && IsQuantified;

    /// <summary>
    /// Records a sum an operative contract says is payable.
    /// </summary>
    /// <remarks>
    /// The contract being operative is checked by the handler, which has the
    /// contract in hand. What this refuses is an internally incoherent obligation:
    /// a fixed sum with no figure, a formula with no rate, a contingent payment
    /// with no condition.
    /// </remarks>
    public static MonetaryObligation Record(
        OrganizationId organizationId,
        ContractId contractId,
        ContractVersionId versionId,
        Guid payerPartyId,
        Guid payeePartyId,
        ObligationCategory category,
        ObligationAmountKind amountKind,
        DeadlineRule due,
        UserId recordedBy,
        DateTimeOffset now,
        Money? amount = null,
        int? quantity = null,
        Money? unitAmount = null,
        TermUnit? unit = null,
        string? condition = null,
        DateOnly? anchorDate = null,
        ObligationId? sourceObligationId = null,
        ContractTermCode? sourceTermCode = null,
        string? description = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(due);

        foreach ((bool defined, string name) in new[]
        {
            (Enum.IsDefined(category), nameof(category)),
            (Enum.IsDefined(amountKind), nameof(amountKind)),
        })
        {
            if (!defined)
            {
                throw new DomainException($"Unknown {name} on a monetary obligation.");
            }
        }

        if (payerPartyId == payeePartyId)
        {
            throw new DomainException("A party cannot owe money to itself.");
        }

        MonetaryObligation obligation = new()
        {
            Id = MonetaryObligationId.New(),
            OrganizationId = organizationId,
            ContractId = contractId,
            ContractVersionId = versionId,
            SourceObligationId = sourceObligationId,
            SourceTermCode = sourceTermCode,
            PayerPartyId = payerPartyId,
            PayeePartyId = payeePartyId,
            Category = category,
            AmountKind = amountKind,
            Unit = unit,
            Condition = Ensure.OptionalMax(condition, nameof(condition), 1000),
            Due = due.Validated(),
            Status = MonetaryObligationStatus.Expected,
            Description = Ensure.OptionalMax(description, nameof(description), 1000),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            RecordedAt = now,
            RecordedBy = recordedBy,
            UpdatedAt = now,
            Version = 1,
        };

        ApplyAmount(obligation, amountKind, amount, quantity, unitAmount, condition);

        obligation.ResolvedDueOn = due.Resolve(anchorDate);

        return obligation;
    }

    /// <summary>
    /// Fixes the amount of an obligation that was recorded without one.
    /// </summary>
    /// <remarks>
    /// The path a contingent bonus takes once its condition occurs, and the path a
    /// participation takes if a statement ever arrives to value it. It moves an
    /// obligation from unquantified to quantified and never the other way: a figure
    /// somebody relied on does not become unknown again.
    /// </remarks>
    public void Quantify(Money amount, DateTimeOffset now, int expectedVersion, string? reason = null)
    {
        RequireVersion(expectedVersion);

        if (Status != MonetaryObligationStatus.Expected)
        {
            throw new DomainException(
                $"This obligation is {Status.ToString().ToLowerInvariant()}, so its amount is settled.");
        }

        if (IsQuantified)
        {
            throw new DomainException(
                "This obligation already carries an amount. Correcting a figure a receivable was "
                + "raised from is an adjustment, not an edit.");
        }

        AmountKind = ObligationAmountKind.Fixed;
        AmountValue = amount.Amount;
        CurrencyCodeValue = amount.Currency.Value;
        Quantity = null;
        UnitAmountValue = null;

        if (!string.IsNullOrWhiteSpace(reason))
        {
            Notes = Ensure.OptionalMax(
                string.IsNullOrWhiteSpace(Notes) ? reason : $"{Notes}\n{reason}",
                nameof(reason),
                4000);
        }

        Touch(now);
    }

    /// <summary>Notes that a receivable has been raised against it.</summary>
    public void NoteRaised(DateTimeOffset now)
    {
        if (Status == MonetaryObligationStatus.Raised)
        {
            return;
        }

        if (Status != MonetaryObligationStatus.Expected)
        {
            throw new DomainException(
                $"This obligation is {Status.ToString().ToLowerInvariant()} and cannot be billed.");
        }

        Status = MonetaryObligationStatus.Raised;

        Touch(now);
    }

    /// <summary>Records that the obligation will not fall due after all.</summary>
    public void Release(DateTimeOffset now, int expectedVersion, string reason)
    {
        RequireVersion(expectedVersion);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Releasing an obligation needs a reason. A contingent payment whose condition did "
                + "not occur and one the parties agreed to waive are different facts.");
        }

        if (Status is MonetaryObligationStatus.Released or MonetaryObligationStatus.Cancelled)
        {
            throw new DomainException(
                $"This obligation is already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = MonetaryObligationStatus.Released;
        Notes = Append(Notes, reason);

        Touch(now);
    }

    /// <summary>Removes an obligation recorded in error, before anything was billed.</summary>
    public void Cancel(DateTimeOffset now, int expectedVersion, string? reason = null)
    {
        RequireVersion(expectedVersion);

        if (Status == MonetaryObligationStatus.Raised)
        {
            throw new DomainException(
                "A receivable has been raised from this obligation. Cancel the receivable first, "
                + "so the reason survives on the record that carries the money.");
        }

        Status = MonetaryObligationStatus.Cancelled;
        Notes = Append(Notes, reason);

        Touch(now);
    }

    /// <summary>Recomputes the due date once the event it hangs off has one.</summary>
    public void ResolveDueDate(DateOnly? anchorDate, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        ResolvedDueOn = Due.Resolve(anchorDate);

        Touch(now);
    }

    private static void ApplyAmount(
        MonetaryObligation obligation,
        ObligationAmountKind kind,
        Money? amount,
        int? quantity,
        Money? unitAmount,
        string? condition)
    {
        switch (kind)
        {
            case ObligationAmountKind.Fixed:
                if (amount is not { } fixedAmount)
                {
                    throw new DomainException("A fixed obligation needs the amount that is due.");
                }

                obligation.AmountValue = fixedAmount.Amount;
                obligation.CurrencyCodeValue = fixedAmount.Currency.Value;
                break;

            case ObligationAmountKind.Formula:
                if (quantity is not { } count || unitAmount is not { } rate)
                {
                    throw new DomainException(
                        "A formula obligation needs both a quantity and what one unit pays.");
                }

                if (count <= 0)
                {
                    throw new DomainException("A formula obligation needs a positive quantity.");
                }

                obligation.Quantity = count;
                obligation.UnitAmountValue = rate.Amount;
                obligation.CurrencyCodeValue = rate.Currency.Value;
                break;

            case ObligationAmountKind.Contingent:
                if (string.IsNullOrWhiteSpace(condition))
                {
                    throw new DomainException(
                        "A contingent obligation needs the condition it depends on, in the "
                        + "contract's own words.");
                }

                // The sum may be stated even though it is not yet payable: a bonus
                // of fifty thousand if the picture is greenlit is a known figure
                // waiting on an unknown event.
                if (amount is { } contingent)
                {
                    obligation.AmountValue = contingent.Amount;
                    obligation.CurrencyCodeValue = contingent.Currency.Value;
                }
                else if (unitAmount is { } contingentUnit)
                {
                    obligation.CurrencyCodeValue = contingentUnit.Currency.Value;
                }

                break;

            case ObligationAmountKind.Unknown:
                if (amount is not null || quantity is not null || unitAmount is not null)
                {
                    throw new DomainException(
                        "An obligation recorded as not calculable must not carry a figure. "
                        + "If the amount is known, record it as fixed.");
                }

                break;

            default:
                throw new DomainException($"Unknown amount kind '{kind}'.");
        }
    }

    private static string? Append(string? existing, string? addition) =>
        string.IsNullOrWhiteSpace(addition)
            ? existing
            : Ensure.OptionalMax(
                string.IsNullOrWhiteSpace(existing) ? addition : $"{existing}\n{addition}",
                nameof(addition),
                4000);

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(MonetaryObligation), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
