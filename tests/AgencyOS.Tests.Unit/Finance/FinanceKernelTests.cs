using System.Globalization;
using AgencyOS.Finance.Rules;
using Xunit;

namespace AgencyOS.Tests.Unit.Finance;

/// <summary>
/// Money arithmetic, and the invariants that keep a ledger honest.
/// </summary>
/// <remarks>
/// <para>
/// Checked systematically rather than by random search, on the M7 precedent. The
/// input space is small and structured - a handful of currencies, a bounded rate
/// range, allocation counts in single digits - so enumerating it with fixed seeds
/// covers more of it than sampling would and a failure reproduces exactly
/// (ADR-0023).
/// </para>
/// <para>
/// The properties here are the ones an accountant would ask about: nothing is
/// created, nothing disappears, and the same inputs give the same answer.
/// </para>
/// </remarks>
public sealed class MoneyArithmeticTests
{
    /// <summary>
    /// No public surface of the kernel touches binary floating point.
    /// </summary>
    /// <remarks>
    /// The M7 test applied to <c>Money</c>, walked over the finance kernel's own
    /// boundary types. A single <c>double</c> here would put rounding error into
    /// the ledger, which is the one place it can never be absorbed.
    /// </remarks>
    [Fact]
    public void NoBoundaryTypeUsesBinaryFloatingPoint()
    {
        Type[] forbidden = [typeof(double), typeof(float), typeof(Half), typeof(double?), typeof(float?)];

        Type[] boundary =
        [
            typeof(MoneyInput),
            typeof(AllocationInput),
            typeof(ReceivableInput),
            typeof(JournalLineInput),
            typeof(CommissionRuleInput),
            typeof(ReconciliationInput),
        ];

        foreach (Type type in boundary)
        {
            foreach (System.Reflection.PropertyInfo property in type.GetProperties())
            {
                Assert.DoesNotContain(property.PropertyType, forbidden);
            }
        }
    }

    [Theory]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KRW", 0)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    public void MinorUnits_FollowTheCurrencyRatherThanAnAssumption(string currency, int places) =>
        Assert.Equal(places, FinanceRules.MinorUnits(currency));

    /// <summary>Two currencies never combine without an explicit conversion.</summary>
    [Fact]
    public void MixedCurrencies_AreRefusedRatherThanConverted()
    {
        Assert.False(FinanceRules.AreCurrenciesCompatible("USD", "EUR"));
        Assert.True(FinanceRules.AreCurrenciesCompatible("USD", "USD"));

        Assert.False(FinanceRules.TryAdd(100m, 100m, "USD", "EUR").HasValue);
        Assert.False(FinanceRules.TrySubtract(100m, 50m, "USD", "GBP").HasValue);
    }

    [Fact]
    public void Subtraction_RefusesToGoBelowZero()
    {
        Assert.Equal(25m, FinanceRules.TrySubtract(100m, 75m, "USD", "USD"));
        Assert.False(FinanceRules.TrySubtract(75m, 100m, "USD", "USD").HasValue);
    }

    [Fact]
    public void NegativeAmounts_AreRefused()
    {
        Assert.False(FinanceRules.IsMoneyValid(-1m, "USD"));
        Assert.NotNull(FinanceRules.DescribeMoneyProblem(-1m, "USD"));
        Assert.True(FinanceRules.IsMoneyValid(0m, "USD"));
    }

    [Fact]
    public void MalformedCurrencies_AreRefused()
    {
        foreach (string bad in new[] { "", "  ", "US", "USDD", "usd", "12A" })
        {
            Assert.False(FinanceRules.IsMoneyValid(1m, bad));
        }
    }

    /// <summary>
    /// A rate is applied once and rounded once, at the currency's own precision.
    /// </summary>
    /// <remarks>
    /// The 333.335 case is the one that matters: ten per cent of 3,333.35 is
    /// 333.335, and banker's rounding takes it to 333.34 rather than up. Round-half-up
    /// would give the agency the extra half-cent every time, which over a year of
    /// commissions is a small systematic lie.
    /// </remarks>
    [Theory]
    [InlineData(10, 1_000_000, "USD", 100_000)]
    [InlineData(10, 3_333.35, "USD", 333.34)]
    [InlineData(10, 1_000_000, "JPY", 100_000)]
    [InlineData(12.5, 80_000, "USD", 10_000)]
    [InlineData(10, 1_000, "KWD", 100)]
    public void ApplyRate_RoundsOnceAtTheCurrencysPrecision(
        decimal rate,
        decimal basis,
        string currency,
        decimal expected) =>
        Assert.Equal(expected, FinanceRules.ApplyRate(rate, basis, currency));

    /// <summary>
    /// A rate applied to any generated basis produces a storable figure.
    /// </summary>
    /// <remarks>
    /// The property that keeps the ledger clean: whatever comes out of a commission
    /// calculation can be written to a numeric column without further rounding, so
    /// there is never a second rounding step to disagree with the first.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RateCases))]
    public void ApplyRate_AlwaysProducesAStorableFigure(decimal rate, decimal basis, string currency)
    {
        decimal? result = FinanceRules.ApplyRate(rate, basis, currency);

        Assert.True(result.HasValue);
        Assert.True(FinanceRules.IsStorable(result.Value, currency));
    }

    /// <summary>A rate never returns more than the basis it was applied to.</summary>
    [Theory]
    [MemberData(nameof(RateCases))]
    public void ApplyRate_NeverExceedsItsBasis(decimal rate, decimal basis, string currency)
    {
        decimal result = FinanceRules.ApplyRate(rate, basis, currency)!.Value;

        // Rates are capped at 50 per cent, so half the basis is the ceiling, with a
        // minor-unit allowance for the single rounding step.
        Assert.True(result <= (basis / 2m) + 1m, $"{result} exceeded half of {basis} {currency}");
    }

    public static TheoryData<decimal, decimal, string> RateCases()
    {
        TheoryData<decimal, decimal, string> data = [];

        decimal[] rates = [1m, 3m, 5m, 10m, 12.5m, 15m, 20m, 33.33m, 50m];
        decimal[] bases = [0m, 1m, 99.99m, 1_000m, 3_333.35m, 12_345.67m, 1_000_000m, 987_654_321m];
        string[] currencies = ["USD", "EUR", "JPY", "KWD"];

        foreach (decimal rate in rates)
        {
            foreach (decimal basis in bases)
            {
                foreach (string currency in currencies)
                {
                    // JPY and KWD hold different precision, so a basis with cents
                    // is not a figure either could have carried in the first place.
                    decimal adjusted = FinanceRules.RoundToMinorUnits(basis, currency);

                    data.Add(rate, adjusted, currency);
                }
            }
        }

        return data;
    }
}

/// <summary>
/// Applying money to what is owed, and losing none of it.
/// </summary>
public sealed class AllocationTests
{
    /// <summary>
    /// Conservation: everything allocated plus everything unapplied is the payment.
    /// </summary>
    /// <remarks>
    /// The single most important arithmetic property in the milestone. A residual
    /// that quietly disappears is cash the agency received and cannot account for,
    /// and it would surface months later as a bank reconciliation nobody can close
    /// (ADR-0023).
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllocationSets))]
    public void Conservation_AllocatedPlusUnappliedIsThePayment(decimal payment, decimal[] lines)
    {
        AllocationInput[] applied =
            [.. lines.Select(amount => Line(amount, applied: true))];

        decimal unapplied = FinanceRules.Unapplied(payment, applied);

        Assert.Equal(payment, lines.Sum() + unapplied);
    }

    /// <summary>A reversed line stops counting, and its money returns to unapplied.</summary>
    [Fact]
    public void ReversingALine_ReturnsItsMoneyToUnapplied()
    {
        AllocationInput[] lines = [Line(60_000m, applied: true), Line(40_000m, applied: false)];

        Assert.Equal(40_000m, FinanceRules.Unapplied(100_000m, lines));
    }

    [Fact]
    public void Outstanding_IsOriginalLessAllocatedLessAdjusted()
    {
        ReceivableInput receivable = Receivable(100_000m, allocated: 60_000m, adjusted: 5_000m);

        Assert.Equal(35_000m, FinanceRules.Outstanding(receivable));
    }

    /// <summary>Outstanding never goes below zero, whatever the arithmetic says.</summary>
    [Theory]
    [MemberData(nameof(ReceivableCases))]
    public void Outstanding_IsNeverNegative(decimal original, decimal allocated, decimal adjusted) =>
        Assert.True(FinanceRules.Outstanding(Receivable(original, allocated, adjusted)) >= 0m);

    /// <summary>
    /// A receivable's status follows its arithmetic, in every generated case.
    /// </summary>
    /// <remarks>
    /// This is the test that would fail first if somebody added a status column
    /// that a handler sets by hand. Status is derived, and derived means it cannot
    /// disagree with the numbers it came from.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReceivableCases))]
    public void Status_FollowsTheArithmetic(decimal original, decimal allocated, decimal adjusted)
    {
        ReceivableInput receivable = Receivable(original, allocated, adjusted);

        int state = FinanceRules.ReceivableState(receivable);
        decimal outstanding = FinanceRules.Outstanding(receivable);

        if (outstanding == 0m && original > 0m)
        {
            Assert.Equal((int)ReceivableState.Paid, state);
        }
        else if (allocated > 0m || adjusted > 0m)
        {
            Assert.Equal((int)ReceivableState.PartiallyPaid, state);
        }
        else
        {
            Assert.Equal((int)ReceivableState.ReceivableOpen, state);
        }
    }

    [Fact]
    public void APartialPayment_IsOrdinaryRatherThanAnError()
    {
        ReceivableInput receivable = Receivable(100_000m, allocated: 75_000m, adjusted: 0m);

        Assert.Equal((int)ReceivableState.PartiallyPaid, FinanceRules.ReceivableState(receivable));
        Assert.Equal(25_000m, FinanceRules.Outstanding(receivable));
        Assert.True(FinanceRules.AcceptsAllocation(receivable));
    }

    [Fact]
    public void OverAllocation_IsRefused()
    {
        ReceivableInput receivable = Receivable(100_000m, allocated: 90_000m, adjusted: 0m);

        Assert.False(FinanceRules.IsAllocationValid(100_000m, "USD", [], receivable, 20_000m));
        Assert.Contains(
            "outstanding balance",
            FinanceRules.DescribeAllocationProblem(100_000m, "USD", [], receivable, 20_000m),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AllocatingMoreThanThePaymentHolds_IsRefused()
    {
        ReceivableInput receivable = Receivable(500_000m, allocated: 0m, adjusted: 0m);

        AllocationInput[] existing = [Line(90_000m, applied: true)];

        Assert.False(
            FinanceRules.IsAllocationValid(100_000m, "USD", existing, receivable, 20_000m));
    }

    [Fact]
    public void CrossCurrencyAllocation_IsRefused()
    {
        ReceivableInput receivable = Receivable(100_000m, allocated: 0m, adjusted: 0m, currency: "EUR");

        Assert.False(FinanceRules.IsAllocationValid(100_000m, "USD", [], receivable, 50_000m));
        Assert.Contains(
            "same currency",
            FinanceRules.DescribeAllocationProblem(100_000m, "USD", [], receivable, 50_000m),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AllocatingNothing_IsRefused() =>
        Assert.False(FinanceRules.IsAllocationValid(
            100_000m, "USD", [], Receivable(100_000m, 0m, 0m), 0m));

    [Fact]
    public void AWrittenOffReceivable_TakesNoMoreMoney()
    {
        ReceivableInput receivable = Receivable(100_000m, 0m, 0m, closed: true, writeOff: true);

        Assert.False(FinanceRules.AcceptsAllocation(receivable));
        Assert.Equal((int)ReceivableState.WrittenOff, FinanceRules.ReceivableState(receivable));
    }

    /// <summary>
    /// A receivable with no resolvable due date is never overdue.
    /// </summary>
    /// <remarks>
    /// The M8 deadline principle, carried into finance. The contract did not say
    /// when, so asserting lateness against a date nobody agreed would be an
    /// invention (ADR-0022, ADR-0023).
    /// </remarks>
    [Fact]
    public void AReceivableWithNoDueDate_IsNeverOverdue()
    {
        ReceivableInput undated = Receivable(100_000m, 0m, 0m, undated: true);

        Assert.False(FinanceRules.IsOverdue(undated, new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void APaidReceivable_IsNotOverdueHoweverLate()
    {
        ReceivableInput paid = Receivable(
            100_000m, allocated: 100_000m, adjusted: 0m, dueOn: new DateOnly(2020, 1, 1));

        Assert.False(FinanceRules.IsOverdue(paid, new DateOnly(2030, 1, 1)));
    }

    /// <summary>
    /// Apportioning loses nothing to rounding, in every generated case.
    /// </summary>
    /// <remarks>
    /// The classic penny-drift bug, tested out of existence: three equal shares of
    /// a hundred are 33.33, 33.33 and 33.34, not three of 33.33 and a cent nobody
    /// can find.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ApportionCases))]
    public void Apportion_Conserves(string currency, decimal total, decimal[] weights)
    {
        decimal[] shares = FinanceRules.Apportion(currency, total, weights);

        Assert.Equal(weights.Length, shares.Length);
        Assert.Equal(total, shares.Sum());
    }

    public static TheoryData<decimal, decimal[]> AllocationSets()
    {
        TheoryData<decimal, decimal[]> data = [];

        data.Add(100_000m, []);
        data.Add(100_000m, [100_000m]);
        data.Add(100_000m, [50_000m, 50_000m]);
        data.Add(100_000m, [90_000m]);
        data.Add(100_000m, [33_333.33m, 33_333.33m, 33_333.34m]);
        data.Add(0.03m, [0.01m, 0.01m]);
        data.Add(1_000_000m, [1m, 2m, 3m, 4m, 5m]);

        return data;
    }

    public static TheoryData<decimal, decimal, decimal> ReceivableCases()
    {
        TheoryData<decimal, decimal, decimal> data = [];

        decimal[] originals = [0m, 1m, 100m, 100_000m, 987_654.32m];
        decimal[] fractions = [0m, 0.25m, 0.5m, 1m, 1.5m];

        foreach (decimal original in originals)
        {
            foreach (decimal allocatedFraction in fractions)
            {
                foreach (decimal adjustedFraction in new[] { 0m, 0.1m })
                {
                    data.Add(
                        original,
                        decimal.Round(original * allocatedFraction, 2),
                        decimal.Round(original * adjustedFraction, 2));
                }
            }
        }

        return data;
    }

    public static TheoryData<string, decimal, decimal[]> ApportionCases()
    {
        TheoryData<string, decimal, decimal[]> data = [];

        (decimal Total, decimal[] Weights)[] cases =
        [
            (100m, [1m, 1m, 1m]),
            (100m, [1m]),
            (0.05m, [1m, 1m, 1m]),
            (1_000_000m, [7m, 11m, 13m]),
            (333.33m, [1m, 2m, 3m, 4m]),
            (99.99m, [1m, 1m]),
        ];

        foreach (string currency in new[] { "USD", "JPY", "KWD" })
        {
            foreach ((decimal total, decimal[] weights) in cases)
            {
                data.Add(currency, FinanceRules.RoundToMinorUnits(total, currency), weights);
            }
        }

        return data;
    }

    private static AllocationInput Line(decimal amount, bool applied) =>
        new()
        {
            ReceivableId = Guid.NewGuid(),
            Amount = amount,
            Currency = "USD",
            IsApplied = applied,
        };

    /// <summary>
    /// Builds a boundary receivable.
    /// </summary>
    /// <remarks>
    /// A builder rather than a <c>with</c> expression, because an F#
    /// <c>[&lt;CLIMutable&gt;]</c> record is not a C# record and has no
    /// non-destructive mutation. The M7 kernel tests hit the same wall.
    /// </remarks>
    internal static ReceivableInput Receivable(
        decimal original,
        decimal allocated,
        decimal adjusted,
        string currency = "USD",
        bool closed = false,
        bool writeOff = false,
        DateOnly? dueOn = null,
        bool undated = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            OriginalAmount = original,
            Currency = currency,
            AllocatedAmount = allocated,
            AdjustedAmount = adjusted,
            IsClosedByAct = closed,
            IsWriteOff = writeOff,
            DueOn = undated ? null : dueOn ?? new DateOnly(2027, 6, 30),
        };
}

/// <summary>
/// The one invariant that makes a ledger a ledger.
/// </summary>
public sealed class JournalTests
{
    [Fact]
    public void ABalancedEntry_Posts()
    {
        Guid cash = Guid.NewGuid();
        Guid receivable = Guid.NewGuid();

        JournalLineInput[] lines = [Debit(cash, 100_000m), Credit(receivable, 100_000m)];

        Assert.True(FinanceRules.IsBalanced(lines));
        Assert.Null(FinanceRules.DescribePostingProblem(lines));
        Assert.Equal(100_000m, FinanceRules.Debits(lines));
        Assert.Equal(100_000m, FinanceRules.Credits(lines));
    }

    [Fact]
    public void AnUnbalancedEntry_IsRefusedAndSaysByHowMuch()
    {
        JournalLineInput[] lines =
            [Debit(Guid.NewGuid(), 100_000m), Credit(Guid.NewGuid(), 90_000m)];

        Assert.False(FinanceRules.IsBalanced(lines));

        string problem = FinanceRules.DescribePostingProblem(lines);

        Assert.Contains("10,000.00", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyEntry_IsRefused()
    {
        Assert.False(FinanceRules.IsBalanced([]));
        Assert.Contains("no lines", FinanceRules.DescribePostingProblem([]), StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroLine_IsRefused()
    {
        JournalLineInput[] lines =
        [
            Debit(Guid.NewGuid(), 100m),
            Credit(Guid.NewGuid(), 100m),
            Debit(Guid.NewGuid(), 0m),
        ];

        Assert.False(FinanceRules.IsBalanced(lines));
    }

    [Fact]
    public void AOneSidedEntry_IsRefused()
    {
        Assert.False(FinanceRules.IsBalanced([Debit(Guid.NewGuid(), 100m)]));
        Assert.Contains(
            "no credits",
            FinanceRules.DescribePostingProblem([Debit(Guid.NewGuid(), 100m)]),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMixedCurrencyEntry_IsRefused()
    {
        JournalLineInput[] lines =
        [
            Debit(Guid.NewGuid(), 100m),
            Credit(Guid.NewGuid(), 100m, "EUR"),
        ];

        Assert.False(FinanceRules.IsBalanced(lines));
    }

    /// <summary>
    /// A reversal mirrors every line, and the pair nets to nothing.
    /// </summary>
    /// <remarks>
    /// The correction model in one property: the original still says what it said,
    /// the reversal says the opposite, and together they have no effect on any
    /// balance (ADR-0023).
    /// </remarks>
    [Theory]
    [MemberData(nameof(BalancedEntries))]
    public void AReversal_NetsTheOriginalToNothing(decimal[] debits, decimal[] credits)
    {
        JournalLineInput[] original =
        [
            .. debits.Select(amount => Debit(Guid.NewGuid(), amount)),
            .. credits.Select(amount => Credit(Guid.NewGuid(), amount)),
        ];

        Assert.True(FinanceRules.IsBalanced(original));

        JournalLineInput[] reversal = FinanceRules.ReverseLines(original);

        Assert.True(FinanceRules.IsBalanced(reversal));

        // Same accounts, opposite sides, so every account nets to zero across the
        // pair.
        Assert.Equal(FinanceRules.Debits(original), FinanceRules.Credits(reversal));
        Assert.Equal(FinanceRules.Credits(original), FinanceRules.Debits(reversal));

        JournalLineInput[] both = [.. original, .. reversal];

        Assert.Equal(FinanceRules.Debits(both), FinanceRules.Credits(both));
    }

    /// <summary>Reversing twice returns the original sides.</summary>
    [Theory]
    [MemberData(nameof(BalancedEntries))]
    public void ReversingTwice_ReturnsTheOriginal(decimal[] debits, decimal[] credits)
    {
        JournalLineInput[] original =
        [
            .. debits.Select(amount => Debit(Guid.NewGuid(), amount)),
            .. credits.Select(amount => Credit(Guid.NewGuid(), amount)),
        ];

        JournalLineInput[] twice = FinanceRules.ReverseLines(FinanceRules.ReverseLines(original));

        Assert.Equal(
            original.Select(line => (line.AccountId, line.Side, line.Amount)),
            twice.Select(line => (line.AccountId, line.Side, line.Amount)));
    }

    /// <summary>
    /// Every state and trigger pair, enumerated.
    /// </summary>
    /// <remarks>
    /// Three states and three triggers is nine pairs, of which two are legal. Small
    /// enough to state completely, which is the point: a state added later without
    /// a rule fails here immediately.
    /// </remarks>
    [Fact]
    public void EveryStateAndTriggerPair_IsAccountedFor()
    {
        int[] states = FinanceRules.KnownJournalStates();
        int[] triggers = FinanceRules.KnownJournalTriggers();

        Assert.Equal(3, states.Length);
        Assert.Equal(3, triggers.Length);

        List<(int State, int Trigger)> legal = [];

        foreach (int state in states)
        {
            foreach (int trigger in triggers)
            {
                if (FinanceRules.JournalPermits(state, trigger))
                {
                    legal.Add((state, trigger));
                }
            }
        }

        Assert.Equal(2, legal.Count);
        Assert.Contains(((int)JournalState.JournalDraft, (int)JournalTrigger.Post), legal);
        Assert.Contains(((int)JournalState.Posted, (int)JournalTrigger.Reverse), legal);
    }

    [Fact]
    public void APostedEntry_CannotBeEditedOrPostedAgain()
    {
        Assert.False(FinanceRules.IsJournalEditable((int)JournalState.Posted));
        Assert.False(FinanceRules.IsJournalEditable((int)JournalState.Reversed));
        Assert.True(FinanceRules.IsJournalEditable((int)JournalState.JournalDraft));

        Assert.False(FinanceRules.JournalPermits(
            (int)JournalState.Posted, (int)JournalTrigger.Post));
    }

    /// <summary>A draft is invisible to every balance; a reversed entry is not.</summary>
    [Fact]
    public void OnlyPostedAndReversedEntriesAffectBalances()
    {
        Assert.False(FinanceRules.AffectsBalance((int)JournalState.JournalDraft));
        Assert.True(FinanceRules.AffectsBalance((int)JournalState.Posted));
        Assert.True(FinanceRules.AffectsBalance((int)JournalState.Reversed));
    }

    /// <summary>
    /// The debit-and-credit convention is written down in exactly one place.
    /// </summary>
    [Theory]
    [InlineData(AccountCategory.Asset, EntrySide.Debit, 100)]
    [InlineData(AccountCategory.Asset, EntrySide.Credit, -100)]
    [InlineData(AccountCategory.Expense, EntrySide.Debit, 100)]
    [InlineData(AccountCategory.Liability, EntrySide.Credit, 100)]
    [InlineData(AccountCategory.Liability, EntrySide.Debit, -100)]
    [InlineData(AccountCategory.Revenue, EntrySide.Credit, 100)]
    [InlineData(AccountCategory.Revenue, EntrySide.Debit, -100)]
    [InlineData(AccountCategory.Equity, EntrySide.Credit, 100)]
    public void BalanceMovement_FollowsTheAccountsNature(
        AccountCategory category,
        EntrySide side,
        decimal expected) =>
        Assert.Equal(expected, FinanceRules.SignedAmount((int)category, (int)side, 100m));

    public static TheoryData<decimal[], decimal[]> BalancedEntries()
    {
        TheoryData<decimal[], decimal[]> data = [];

        data.Add([100_000m], [100_000m]);
        data.Add([100_000m], [90_000m, 10_000m]);
        data.Add([60_000m, 40_000m], [100_000m]);
        data.Add([1m], [1m]);
        data.Add([33_333.33m, 33_333.33m, 33_333.34m], [100_000m]);
        data.Add([0.01m], [0.01m]);

        return data;
    }

    private static JournalLineInput Debit(Guid account, decimal amount, string currency = "USD") =>
        new()
        {
            AccountId = account,
            Side = (int)EntrySide.Debit,
            Amount = amount,
            Currency = currency,
        };

    private static JournalLineInput Credit(Guid account, decimal amount, string currency = "USD") =>
        new()
        {
            AccountId = account,
            Side = (int)EntrySide.Credit,
            Amount = amount,
            Currency = currency,
        };
}

/// <summary>
/// Entitlement, collection, and the difference between them.
/// </summary>
public sealed class CommissionTests
{
    /// <summary>
    /// The example from the milestone brief, held apart.
    /// </summary>
    /// <remarks>
    /// A million of compensation at ten per cent entitles the agency to a hundred
    /// thousand. Four hundred thousand collected earns forty thousand in cash. A
    /// system that reported either as the other would be wrong in a way somebody
    /// would act on (ADR-0023).
    /// </remarks>
    [Fact]
    public void EntitlementAndCollection_AreDifferentNumbers()
    {
        CommissionRuleInput rule = Rate(10m);

        decimal entitled = FinanceRules.Entitlement(rule, 1_000_000m, "USD")!.Value;

        Assert.Equal(100_000m, entitled);

        decimal collected = FinanceRules.Collected(rule, entitled, 400_000m, "USD")!.Value;

        Assert.Equal(40_000m, collected);

        Assert.Equal(
            60_000m, FinanceRules.OutstandingCommission(entitled, collected, 0m, "USD"));
    }

    /// <summary>Collecting everything earns exactly the entitlement, never more.</summary>
    [Theory]
    [MemberData(nameof(RateAndBasis))]
    public void CollectingEverything_EarnsExactlyTheEntitlement(decimal rate, decimal basis)
    {
        CommissionRuleInput rule = Rate(rate);

        decimal entitled = FinanceRules.Entitlement(rule, basis, "USD")!.Value;
        decimal collected = FinanceRules.Collected(rule, entitled, basis, "USD")!.Value;

        Assert.Equal(entitled, collected);
    }

    /// <summary>Collection never exceeds entitlement, however much cash arrives.</summary>
    [Theory]
    [MemberData(nameof(RateAndBasis))]
    public void Collection_NeverExceedsEntitlement(decimal rate, decimal basis)
    {
        CommissionRuleInput rule = Rate(rate);

        decimal entitled = FinanceRules.Entitlement(rule, basis, "USD")!.Value;

        // More cash than the obligation: an overpayment does not earn more commission.
        decimal collected = FinanceRules.Collected(rule, entitled, basis * 2m, "USD")!.Value;

        Assert.Equal(entitled, collected);
    }

    /// <summary>The same rule and basis give the same answer, every time.</summary>
    [Theory]
    [MemberData(nameof(RateAndBasis))]
    public void Calculation_IsDeterministic(decimal rate, decimal basis)
    {
        CommissionRuleInput rule = Rate(rate);

        decimal first = FinanceRules.Entitlement(rule, basis, "USD")!.Value;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(first, FinanceRules.Entitlement(rule, basis, "USD"));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(50.01)]
    [InlineData(1000)]
    public void RatesOutsideTheBounds_AreRefused(decimal rate)
    {
        Assert.False(FinanceRules.IsRateValid(rate));
        Assert.False(FinanceRules.IsRuleValid(Rate(rate)));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(10)]
    [InlineData(50)]
    public void RatesInsideTheBounds_AreAccepted(decimal rate) =>
        Assert.True(FinanceRules.IsRateValid(rate));

    /// <summary>
    /// The rule that governed the transaction is chosen by date, not by recency.
    /// </summary>
    /// <remarks>
    /// Historical finance has to use the rule that was in force when the money was
    /// earned. Recalculating a 2027 commission at a 2029 rate would silently restate
    /// what the agency was owed two years ago (ADR-0023).
    /// </remarks>
    [Fact]
    public void TheGoverningRule_IsTheOneInForceOnTheDay()
    {
        CommissionRuleInput old = Rate(
            10m, from: new DateOnly(2026, 1, 1), to: new DateOnly(2027, 12, 31));

        CommissionRuleInput current = Rate(12m, from: new DateOnly(2028, 1, 1));

        CommissionRuleInput[] rules = [old, current];

        Assert.Equal(
            old.Id, FinanceRules.GoverningRule(rules, new DateOnly(2027, 6, 1), null));

        Assert.Equal(
            current.Id, FinanceRules.GoverningRule(rules, new DateOnly(2029, 6, 1), null));
    }

    /// <summary>A rule confined to a contract beats one that governs broadly.</summary>
    [Fact]
    public void AContractSpecificRule_BeatsTheGeneralOne()
    {
        Guid contract = Guid.NewGuid();

        CommissionRuleInput general = Rate(10m);
        CommissionRuleInput specific = Rate(5m, contractId: contract);

        Assert.Equal(
            specific.Id,
            FinanceRules.GoverningRule([general, specific], new DateOnly(2027, 6, 1), contract));
    }

    /// <summary>No rule means no commission, not a default one.</summary>
    [Fact]
    public void NoGoverningRule_IsRefusedRatherThanDefaulted()
    {
        CommissionRuleInput future = Rate(10m, from: new DateOnly(2030, 1, 1));

        Assert.Equal(
            Guid.Empty, FinanceRules.GoverningRule([future], new DateOnly(2027, 6, 1), null));

        Assert.Contains(
            "no default rate",
            FinanceRules.DescribeGoverningProblem([future], new DateOnly(2027, 6, 1), null),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Two rules in force at once is refused, not resolved.
    /// </summary>
    /// <remarks>
    /// Picking the higher would favour the agency and picking the newer would favour
    /// whoever edited last. Either would hide a data problem that a person needs to
    /// fix, so the system says so instead (ADR-0023).
    /// </remarks>
    [Fact]
    public void TwoRulesInForce_AreRefusedRatherThanResolved()
    {
        CommissionRuleInput first = Rate(10m);
        CommissionRuleInput second = Rate(12m);

        Assert.Equal(
            Guid.Empty,
            FinanceRules.GoverningRule([first, second], new DateOnly(2027, 6, 1), null));

        Assert.Contains(
            "end one of them",
            FinanceRules.DescribeGoverningProblem([first, second], new DateOnly(2027, 6, 1), null),
            StringComparison.Ordinal);
    }

    /// <summary>A basis nobody can establish produces no entitlement, not a zero.</summary>
    [Fact]
    public void AnUnknownBasis_ProducesNoEntitlementRatherThanZero()
    {
        CommissionRuleInput rule = Rate(10m);

        Assert.Contains(
            "not known",
            FinanceRules.DescribeEntitlementProblem(rule, 0m, ""),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFixedRule_IgnoresTheBasisAndReturnsItsOwnSum()
    {
        CommissionRuleInput rule = new()
        {
            Id = Guid.NewGuid(),
            Basis = (int)CommissionBasis.FixedAmount,
            RatePercent = null,
            FixedAmount = 25_000m,
            Currency = "USD",
            TermCode = null,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = null,
            ContractId = null,
        };

        Assert.True(FinanceRules.IsRuleValid(rule));
        Assert.Equal(25_000m, FinanceRules.Entitlement(rule, 1_000_000m, "USD"));

        // Earned as cash arrives, up to the fee and no further.
        Assert.Equal(10_000m, FinanceRules.Collected(rule, 25_000m, 10_000m, "USD"));
        Assert.Equal(25_000m, FinanceRules.Collected(rule, 25_000m, 900_000m, "USD"));
    }

    [Fact]
    public void ASpecificTermRule_NeedsTheTermItAppliesTo()
    {
        CommissionRuleInput incomplete = Rate(10m, basis: CommissionBasis.SpecificTerm);

        Assert.False(FinanceRules.IsRuleValid(incomplete));

        CommissionRuleInput complete = Rate(
            10m, basis: CommissionBasis.SpecificTerm, termCode: 105);

        Assert.True(FinanceRules.IsRuleValid(complete));
    }

    [Fact]
    public void CrossCurrencyCommission_IsRefused()
    {
        CommissionRuleInput fixedEuros = new()
        {
            Id = Guid.NewGuid(),
            Basis = (int)CommissionBasis.FixedAmount,
            RatePercent = null,
            FixedAmount = 10_000m,
            Currency = "EUR",
            TermCode = null,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = null,
            ContractId = null,
        };

        Assert.False(FinanceRules.Collected(fixedEuros, 10_000m, 5_000m, "USD").HasValue);
    }

    public static TheoryData<decimal, decimal> RateAndBasis()
    {
        TheoryData<decimal, decimal> data = [];

        foreach (decimal rate in new[] { 1m, 5m, 10m, 12.5m, 15m, 33.33m, 50m })
        {
            foreach (decimal basis in new[]
            {
                0m, 1m, 999.99m, 100_000m, 333_333.33m, 1_000_000m, 87_654_321.99m,
            })
            {
                data.Add(rate, basis);
            }
        }

        return data;
    }

    /// <summary>Builds a boundary commission rule.</summary>
    /// <remarks>A builder for the reason the receivable one is.</remarks>
    internal static CommissionRuleInput Rate(
        decimal rate,
        CommissionBasis basis = CommissionBasis.GrossCompensation,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? contractId = null,
        int? termCode = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Basis = (int)basis,
            RatePercent = rate,
            FixedAmount = null,
            Currency = null,
            TermCode = termCode,
            EffectiveFrom = from ?? new DateOnly(2026, 1, 1),
            EffectiveTo = to,
            ContractId = contractId,
        };
}

/// <summary>
/// Explaining the difference between what was owed and what arrived.
/// </summary>
public sealed class ReconciliationTests
{
    /// <summary>The brief's reconciled example.</summary>
    [Fact]
    public void ADeductionThatExplainsTheGap_Reconciles()
    {
        ReconciliationInput input = Input(expected: 100_000m, allocated: 97_500m, deductions: 2_500m);

        Assert.True(FinanceRules.IsReconciled(input));
        Assert.Equal(0m, FinanceRules.Variance(input));
        Assert.Equal(
            (int)ReconciliationOutcome.Reconciled, FinanceRules.ReconciliationOutcome(input));
    }

    /// <summary>The brief's unexplained example.</summary>
    [Fact]
    public void AGapWithNoDeduction_StaysUnexplained()
    {
        ReconciliationInput input = Input(expected: 100_000m, allocated: 95_000m, deductions: 0m);

        Assert.False(FinanceRules.IsReconciled(input));
        Assert.Equal(5_000m, FinanceRules.Variance(input));
        Assert.Equal(
            (int)ReconciliationOutcome.Shortfall, FinanceRules.ReconciliationOutcome(input));

        Assert.Contains(
            "not explained", FinanceRules.DescribeReconciliation(input), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverpayment_ReadsAsExcessRatherThanAnError()
    {
        ReconciliationInput input = Input(expected: 100_000m, allocated: 105_000m, deductions: 0m);

        Assert.Equal((int)ReconciliationOutcome.Excess, FinanceRules.ReconciliationOutcome(input));
        Assert.Equal(5_000m, FinanceRules.Variance(input));
    }

    [Fact]
    public void NothingApplied_ReadsAsNotStarted() =>
        Assert.Equal(
            (int)ReconciliationOutcome.NotStarted,
            FinanceRules.ReconciliationOutcome(Input(100_000m, 0m, 0m)));

    /// <summary>
    /// The description states the arithmetic and reaches no conclusion.
    /// </summary>
    /// <remarks>
    /// This is the test that would fail first if somebody added a helpful
    /// explanation to the reconciliation screen. A shortfall may be withholding, a
    /// bank charge, a dispute or a mistake, and the system does not know which.
    /// </remarks>
    [Fact]
    public void TheDescription_NeverGuessesWhy()
    {
        string description = FinanceRules.DescribeReconciliation(Input(100_000m, 95_000m, 0m));

        foreach (string word in new[]
        {
            "tax", "withholding", "fraud", "error", "dispute", "wrong", "should",
        })
        {
            Assert.DoesNotContain(word, description, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Variance arithmetic holds across generated cases.</summary>
    [Theory]
    [MemberData(nameof(VarianceCases))]
    public void Variance_IsExpectedLessEverythingAccountedFor(
        decimal expected,
        decimal allocated,
        decimal deductions,
        decimal writtenOff)
    {
        ReconciliationInput input = new()
        {
            Expected = expected,
            Allocated = allocated,
            Deductions = deductions,
            WrittenOff = writtenOff,
            Currency = "USD",
        };

        decimal accounted = FinanceRules.Accounted(input);

        Assert.Equal(allocated + deductions + writtenOff, accounted);
        Assert.Equal(Math.Abs(expected - accounted), FinanceRules.Variance(input));
    }

    public static TheoryData<decimal, decimal, decimal, decimal> VarianceCases()
    {
        TheoryData<decimal, decimal, decimal, decimal> data = [];

        decimal[] values = [0m, 0.01m, 1_000m, 97_500m, 100_000m, 105_000m];

        foreach (decimal expected in values)
        {
            foreach (decimal allocated in values)
            {
                data.Add(expected, allocated, 0m, 0m);
                data.Add(expected, allocated, 2_500m, 0m);
                data.Add(expected, allocated, 0m, 1_000m);
            }
        }

        return data;
    }

    private static ReconciliationInput Input(
        decimal expected,
        decimal allocated,
        decimal deductions) =>
        new()
        {
            Expected = expected,
            Allocated = allocated,
            Deductions = deductions,
            WrittenOff = 0m,
            Currency = "USD",
        };
}
