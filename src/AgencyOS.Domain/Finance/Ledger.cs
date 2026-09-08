using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Finance.Rules;

namespace AgencyOS.Domain.Finance;

/// <summary>Opaque, immutable identifier for a ledger <see cref="Account"/>.</summary>
public readonly record struct AccountId(Guid Value)
{
    public static AccountId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Opaque, immutable identifier for a <see cref="JournalEntry"/>.</summary>
public readonly record struct JournalEntryId(Guid Value)
{
    public static JournalEntryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What kind of thing a ledger account is.
/// </summary>
/// <remarks>
/// Values match <see cref="AccountCategory"/> in the finance kernel, which owns the
/// convention for which side increases which category.
/// </remarks>
public enum LedgerAccountCategory
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Revenue = 4,
    Expense = 5,

    /// <summary>Money known to be between two accounts. Should trend to zero.</summary>
    Clearing = 6,
}

/// <summary>
/// The accounts AgencyOS operates.
/// </summary>
/// <remarks>
/// <para>
/// A small fixed set, seeded per organization, not a chart-of-accounts designer.
/// M9 needs exactly enough structure to keep client money apart from agency money
/// and to record what has been collected; a general-purpose ERP chart would be
/// scope the milestone has no use for (ADR-0023).
/// </para>
/// <para>
/// The names are deliberately neutral. AgencyOS has adopted no accounting
/// framework, claims no GAAP or IFRS compliance, and having double entry does not
/// give it one.
/// </para>
/// </remarks>
public enum SystemAccount
{
    /// <summary>Money the agency holds.</summary>
    Cash = 1,

    /// <summary>Money owed to the agency or its clients by third parties.</summary>
    AccountsReceivable = 2,

    /// <summary>
    /// Money the agency holds that belongs to a client.
    /// </summary>
    /// <remarks>
    /// The account that makes the milestone honest. Gross receipts land here, not
    /// in revenue, and commission moves out of here into revenue when it is earned.
    /// </remarks>
    ClientFundsPayable = 3,

    /// <summary>Commission the agency has earned in cash.</summary>
    CommissionRevenue = 4,

    /// <summary>Money received that has not been applied to anything yet.</summary>
    UnappliedCash = 5,

    /// <summary>Amounts given up as uncollectable.</summary>
    WriteOffExpense = 6,

    /// <summary>Deductions somebody recorded: withholding, bank charges, fees.</summary>
    DeductionExpense = 7,

    /// <summary>Money whose home is not yet established.</summary>
    Suspense = 8,
}

/// <summary>Which side of a journal line an amount sits on.</summary>
/// <remarks>Values match <see cref="EntrySide"/> in the finance kernel.</remarks>
public enum JournalSide
{
    Debit = 1,
    Credit = 2,
}

/// <summary>Where a journal entry stands.</summary>
/// <remarks>Values match <see cref="JournalState"/> in the finance kernel.</remarks>
public enum JournalEntryStatus
{
    /// <summary>Being assembled. Invisible to every balance.</summary>
    Draft = 1,

    /// <summary>Balanced and committed. Immutable from here.</summary>
    Posted = 2,

    /// <summary>Undone by a reversing entry. The original still says what it said.</summary>
    Reversed = 3,
}

/// <summary>
/// What business event a journal entry records.
/// </summary>
/// <remarks>
/// A typed discriminator rather than a free <c>(sourceType, guid)</c> pair. Every
/// value here corresponds to a table the entry can point at through a real foreign
/// key, so "which payment produced this posting" is a join rather than a
/// convention (ADR-0023).
/// </remarks>
public enum JournalSource
{
    /// <summary>A receivable was recognised.</summary>
    ReceivableRaised = 1,

    /// <summary>A payment was recorded.</summary>
    PaymentRecorded = 2,

    /// <summary>A payment was applied to a receivable.</summary>
    PaymentAllocated = 3,

    /// <summary>Commission was earned in cash.</summary>
    CommissionCollected = 4,

    /// <summary>A deduction was recorded against a receivable.</summary>
    AdjustmentRecorded = 5,

    /// <summary>A receivable was written off.</summary>
    ReceivableWrittenOff = 6,

    /// <summary>An entry was reversed.</summary>
    Reversal = 7,

    /// <summary>Somebody posted it by hand.</summary>
    ManualAdjustment = 8,
}

/// <summary>
/// One account in the organization's ledger.
/// </summary>
/// <remarks>
/// Seeded once per organization from <see cref="SystemAccount"/>, so every tenant
/// has the same small chart and no tenant can post to another's accounts.
/// </remarks>
public sealed class Account
{
    private Account()
    {
    }

    public AccountId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>Which of the operational accounts this is.</summary>
    public SystemAccount Kind { get; private set; }

    public LedgerAccountCategory Category { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>A short stable code for display and export.</summary>
    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Whether the account increases on the debit side.</summary>
    public bool IncreasesOnDebit =>
        Category is LedgerAccountCategory.Asset
            or LedgerAccountCategory.Expense
            or LedgerAccountCategory.Clearing;

    /// <summary>The accounts every organization gets, in a fixed order.</summary>
    /// <remarks>
    /// Stated once here rather than in a migration, so the set is readable beside
    /// the code that posts to it and the seeding is testable without a database.
    /// </remarks>
    public static IReadOnlyList<(SystemAccount Kind, LedgerAccountCategory Category, string Code, string Name, string Description)> Chart { get; } =
    [
        (SystemAccount.Cash, LedgerAccountCategory.Asset, "1000", "Cash",
            "Money the agency holds, however it is held."),

        (SystemAccount.AccountsReceivable, LedgerAccountCategory.Asset, "1100", "Accounts receivable",
            "Amounts owed to the agency or its clients by third parties."),

        (SystemAccount.UnappliedCash, LedgerAccountCategory.Clearing, "1900", "Unapplied cash",
            "Money received and not yet applied to a receivable."),

        (SystemAccount.Suspense, LedgerAccountCategory.Clearing, "1990", "Suspense",
            "Amounts whose home is not yet established."),

        (SystemAccount.ClientFundsPayable, LedgerAccountCategory.Liability, "2000", "Client funds payable",
            "Money the agency holds that belongs to a client. Not agency revenue."),

        (SystemAccount.CommissionRevenue, LedgerAccountCategory.Revenue, "4000", "Commission revenue",
            "Commission the agency has earned in cash."),

        (SystemAccount.DeductionExpense, LedgerAccountCategory.Expense, "5000", "Deductions",
            "Withholding, bank charges and fees recorded against a receivable."),

        (SystemAccount.WriteOffExpense, LedgerAccountCategory.Expense, "5100", "Write-offs",
            "Amounts given up as uncollectable."),
    ];

    /// <summary>Creates one account of the standard chart.</summary>
    public static Account Create(
        OrganizationId organizationId,
        SystemAccount kind,
        LedgerAccountCategory category,
        string code,
        string name,
        string description,
        DateTimeOffset now)
    {
        foreach ((bool defined, string field) in new[]
        {
            (Enum.IsDefined(kind), nameof(kind)),
            (Enum.IsDefined(category), nameof(category)),
        })
        {
            if (!defined)
            {
                throw new DomainException($"Unknown {field} on a ledger account.");
            }
        }

        return new Account
        {
            Id = AccountId.New(),
            OrganizationId = organizationId,
            Kind = kind,
            Category = category,
            Code = Ensure.NotBlankMax(code, nameof(code), 20),
            Name = Ensure.NotBlankMax(name, nameof(name), 100),
            Description = Ensure.OptionalMax(description, nameof(description), 500),
            CreatedAt = now,
        };
    }

    /// <summary>Seeds the whole chart for an organization.</summary>
    public static IReadOnlyList<Account> SeedChart(OrganizationId organizationId, DateTimeOffset now) =>
        [.. Chart.Select(entry => Create(
            organizationId, entry.Kind, entry.Category, entry.Code, entry.Name, entry.Description, now))];
}

/// <summary>
/// One line of a journal entry.
/// </summary>
/// <remarks>
/// An explicit side and a positive amount, never a signed number. Sign-as-convention
/// is readable only to whoever wrote it, and every reader afterwards has to
/// remember which way round it went (ADR-0023).
/// </remarks>
public sealed class JournalLine
{
    private JournalLine()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public JournalEntryId JournalEntryId { get; private set; }

    public AccountId AccountId { get; private set; }

    public JournalSide Side { get; private set; }

    /// <summary>Always positive. The side carries the direction.</summary>
    public decimal AmountValue { get; private set; }

    public string CurrencyCodeValue { get; private set; } = string.Empty;

    /// <summary>Display order within the entry.</summary>
    public int Sequence { get; private set; }

    public string? Memo { get; private set; }

    public Money Amount => Money.Create(AmountValue, CurrencyCodeValue);

    internal static JournalLine Create(
        OrganizationId organizationId,
        JournalEntryId entryId,
        AccountId accountId,
        JournalSide side,
        Money amount,
        int sequence,
        string? memo)
    {
        if (!Enum.IsDefined(side))
        {
            throw new DomainException($"Unknown side '{side}' on a journal line.");
        }

        if (amount.Amount <= 0m)
        {
            throw new DomainException(
                "A journal line of nothing moves nothing. Remove it, or give it an amount.");
        }

        return new JournalLine
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            JournalEntryId = entryId,
            AccountId = accountId,
            Side = side,
            AmountValue = amount.Amount,
            CurrencyCodeValue = amount.Currency.Value,
            Sequence = sequence,
            Memo = Ensure.OptionalMax(memo, nameof(memo), 500),
        };
    }

    /// <summary>The flat shape the finance kernel balances.</summary>
    public JournalLineInput ToRulesInput() =>
        new()
        {
            AccountId = AccountId.Value,
            Side = (int)Side,
            Amount = AmountValue,
            Currency = CurrencyCodeValue,
        };
}

/// <summary>
/// One accounting event, in balanced lines.
/// </summary>
/// <remarks>
/// <para>
/// The canonical financial record. A draft is invisible to every balance; a posted
/// entry is immutable and counts; a reversed entry still counts, because the money
/// moved and then moved back and both movements happened.
/// </para>
/// <para>
/// Balance is enforced three times over, deliberately: the aggregate refuses to
/// post unbalanced lines, the F# kernel computes the totals, and a deferred
/// PostgreSQL constraint trigger checks the same arithmetic at commit. An
/// invariant that would let an unbalanced entry become canonical is not one that
/// should depend on a single code path remembering (ADR-0023).
/// </para>
/// <para>
/// Three dates, kept apart. <see cref="OccurredOn"/> is when the economic event
/// happened, <see cref="RecordedAt"/> is when AgencyOS was told, and
/// <see cref="PostingDate"/> is the date the entry belongs to for accounting
/// purposes. M9 builds no period close, but posting date is a real column and
/// indexed, so one can be added later without rewriting history.
/// </para>
/// </remarks>
public sealed class JournalEntry
{
    private readonly List<JournalLine> _lines = [];

    private JournalEntry()
    {
    }

    public JournalEntryId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public JournalEntryStatus Status { get; private set; }

    public JournalSource Source { get; private set; }

    /// <summary>What the entry says it is about, for a person reading the ledger.</summary>
    public string Memo { get; private set; } = string.Empty;

    /// <summary>The currency every line carries.</summary>
    public string CurrencyCodeValue { get; private set; } = string.Empty;

    /// <summary>When the economic event happened.</summary>
    public DateOnly OccurredOn { get; private set; }

    /// <summary>The date the entry belongs to for accounting.</summary>
    public DateOnly PostingDate { get; private set; }

    /// <summary>When AgencyOS was told.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>When it was posted, if it has been.</summary>
    public DateTimeOffset? PostedAt { get; private set; }

    public UserId? PostedBy { get; private set; }

    // ---- typed provenance, one of which is populated per source ----

    public ReceivableId? ReceivableId { get; private set; }

    public PaymentId? PaymentId { get; private set; }

    public PaymentAllocationId? PaymentAllocationId { get; private set; }

    public PaymentAdjustmentId? PaymentAdjustmentId { get; private set; }

    public CommissionEntitlementId? CommissionEntitlementId { get; private set; }

    /// <summary>The entry this one reverses.</summary>
    public JournalEntryId? ReversalOfEntryId { get; private set; }

    /// <summary>The entry that reversed this one.</summary>
    public JournalEntryId? ReversedByEntryId { get; private set; }

    /// <summary>Why it was reversed. Required when it was.</summary>
    public string? ReversalReason { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (ADR-0014).</summary>
    public int Version { get; private set; }

    public IReadOnlyCollection<JournalLine> Lines => _lines;

    /// <summary>What the debits come to.</summary>
    public Money Debits =>
        Money.Create(FinanceRules.Debits(ToRulesLines()), CurrencyCodeValue);

    /// <summary>What the credits come to.</summary>
    public Money Credits =>
        Money.Create(FinanceRules.Credits(ToRulesLines()), CurrencyCodeValue);

    /// <summary>Whether the lines are fit to post.</summary>
    public bool IsBalanced => FinanceRules.IsBalanced(ToRulesLines());

    /// <summary>Whether it can still be changed.</summary>
    public bool IsEditable => Status == JournalEntryStatus.Draft;

    /// <summary>Whether it counts towards a balance.</summary>
    public bool AffectsBalance => FinanceRules.AffectsBalance((int)Status);

    /// <summary>Starts an entry, which is not part of the ledger until it is posted.</summary>
    public static JournalEntry Start(
        OrganizationId organizationId,
        JournalSource source,
        string memo,
        string currency,
        DateOnly occurredOn,
        UserId createdBy,
        DateTimeOffset now,
        DateOnly? postingDate = null)
    {
        if (!Enum.IsDefined(source))
        {
            throw new DomainException($"Unknown journal source '{source}'.");
        }

        return new JournalEntry
        {
            Id = JournalEntryId.New(),
            OrganizationId = organizationId,
            Status = JournalEntryStatus.Draft,
            Source = source,
            Memo = Ensure.NotBlankMax(memo, nameof(memo), 500),
            CurrencyCodeValue = CurrencyCode.Parse(currency).Value,
            OccurredOn = occurredOn,

            // Defaults to the economic date rather than to today, so an entry for a
            // payment received last week belongs to last week unless somebody says
            // otherwise.
            PostingDate = postingDate ?? occurredOn,

            RecordedAt = now,
            CreatedBy = createdBy,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Adds a line while the entry is still a draft.</summary>
    public JournalLine AddLine(
        AccountId accountId,
        JournalSide side,
        Money amount,
        DateTimeOffset now,
        string? memo = null)
    {
        RequireEditable("add lines to");

        if (amount.Currency.Value != CurrencyCodeValue)
        {
            throw new DomainException(
                $"This entry is in {CurrencyCodeValue} and that line is in {amount.Currency}. "
                + "Debits and credits balance within one currency, so an entry carries one.");
        }

        int sequence = _lines.Count == 0 ? 1 : _lines.Max(line => line.Sequence) + 1;

        JournalLine line = JournalLine.Create(
            OrganizationId, Id, accountId, side, amount, sequence, memo);

        _lines.Add(line);

        Touch(now);

        return line;
    }

    /// <summary>Records what the entry is about, so provenance is a join not a note.</summary>
    public void AttributeTo(
        ReceivableId? receivable = null,
        PaymentId? payment = null,
        PaymentAllocationId? allocation = null,
        PaymentAdjustmentId? adjustment = null,
        CommissionEntitlementId? commission = null)
    {
        RequireEditable("attribute");

        ReceivableId = receivable ?? ReceivableId;
        PaymentId = payment ?? PaymentId;
        PaymentAllocationId = allocation ?? PaymentAllocationId;
        PaymentAdjustmentId = adjustment ?? PaymentAdjustmentId;
        CommissionEntitlementId = commission ?? CommissionEntitlementId;
    }

    /// <summary>
    /// Commits the entry to the ledger.
    /// </summary>
    /// <remarks>
    /// The one irreversible act in the milestone. After this the lines cannot be
    /// changed by any application path, and a correction means posting another
    /// entry that says the opposite.
    /// </remarks>
    public void Post(UserId actor, DateTimeOffset now, int expectedVersion)
    {
        RequireVersion(expectedVersion);

        if (Status != JournalEntryStatus.Draft)
        {
            throw new DomainException(
                $"This entry is already {Status.ToString().ToLowerInvariant()}.");
        }

        if (FinanceRules.DescribePostingProblem(ToRulesLines()) is { } problem)
        {
            throw new DomainException(problem);
        }

        Status = JournalEntryStatus.Posted;
        PostedAt = now;
        PostedBy = actor;

        Touch(now);
    }

    /// <summary>
    /// Builds the entry that undoes this one.
    /// </summary>
    /// <remarks>
    /// Every line mirrored to the opposite side, same accounts, same amounts, so
    /// the pair nets to nothing. The original is untouched and still says exactly
    /// what it said on the day it was posted.
    /// </remarks>
    public JournalEntry BuildReversal(
        string reason,
        UserId actor,
        DateTimeOffset now,
        DateOnly? postingDate = null)
    {
        if (Status != JournalEntryStatus.Posted)
        {
            throw new DomainException(
                Status == JournalEntryStatus.Draft
                    ? "A draft has not been posted, so there is nothing to reverse. Discard it instead."
                    : "This entry has already been reversed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Reversing a posted entry needs a reason. The original stays on the record, so "
                + "the reversal has to say why it is there.");
        }

        JournalEntry reversal = Start(
            OrganizationId,
            JournalSource.Reversal,
            $"Reversal of {Memo}",
            CurrencyCodeValue,
            DateOnly.FromDateTime(now.UtcDateTime),
            actor,
            now,
            postingDate);

        foreach (JournalLine line in _lines.OrderBy(x => x.Sequence))
        {
            reversal.AddLine(
                line.AccountId,
                line.Side == JournalSide.Debit ? JournalSide.Credit : JournalSide.Debit,
                line.Amount,
                now,
                line.Memo);
        }

        reversal.ReversalOfEntryId = Id;

        reversal.AttributeTo(
            ReceivableId,
            PaymentId,
            PaymentAllocationId,
            PaymentAdjustmentId,
            CommissionEntitlementId);

        return reversal;
    }

    /// <summary>Marks this entry reversed by another.</summary>
    public void NoteReversedBy(JournalEntryId reversal, string reason, DateTimeOffset now)
    {
        if (Status != JournalEntryStatus.Posted)
        {
            throw new DomainException("Only a posted entry can be reversed.");
        }

        if (reversal == Id)
        {
            throw new DomainException("An entry cannot reverse itself.");
        }

        Status = JournalEntryStatus.Reversed;
        ReversedByEntryId = reversal;
        ReversalReason = Ensure.NotBlankMax(reason, nameof(reason), 1000);

        Touch(now);
    }

    /// <summary>The flat shapes the finance kernel balances.</summary>
    public JournalLineInput[] ToRulesLines() =>
        [.. _lines.OrderBy(line => line.Sequence).Select(line => line.ToRulesInput())];

    private void RequireEditable(string action)
    {
        if (!IsEditable)
        {
            throw new DomainException(
                $"This entry has been {Status.ToString().ToLowerInvariant()}, so it is not possible "
                + $"to {action} it. Post a reversing entry instead.");
        }
    }

    private void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(
                nameof(JournalEntry), Id.ToString(), expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
