using AgencyOS.Deals.Rules;
using Xunit;

// Named Legal rather than Contracts: a namespace ending in Contracts shadows
// AgencyOS.Contracts for every file that resolves a relative Contracts.* name,
// which is the exact ambiguity the milestone brief warned about.
namespace AgencyOS.Tests.Unit.Legal;

/// <summary>
/// Grant periods: the shapes a contract can state, and the ones it cannot.
/// </summary>
public sealed class GrantPeriodTests
{
    [Fact]
    public void AFixedPeriodRuns_BetweenItsDatesInclusive()
    {
        GrantPeriodInput period = Fixed(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        Assert.Null(DealRules.DescribePeriodProblem(period));

        Assert.True(DealRules.PeriodCoversOn(period, new DateOnly(2027, 1, 1)));
        Assert.True(DealRules.PeriodCoversOn(period, new DateOnly(2027, 6, 15)));
        Assert.True(DealRules.PeriodCoversOn(period, new DateOnly(2027, 12, 31)));

        Assert.False(DealRules.PeriodCoversOn(period, new DateOnly(2026, 12, 31)));
        Assert.False(DealRules.PeriodCoversOn(period, new DateOnly(2028, 1, 1)));
    }

    [Fact]
    public void APeriodCannotEndBeforeItStarts()
    {
        GrantPeriodInput backwards = Fixed(new DateOnly(2027, 12, 31), new DateOnly(2027, 1, 1));

        Assert.NotNull(DealRules.DescribePeriodProblem(backwards));
        Assert.False(DealRules.IsPeriodValid(backwards));
    }

    [Fact]
    public void APerpetualGrantRunsForever()
    {
        GrantPeriodInput perpetual = new()
        {
            Kind = 1, Starts = new DateOnly(2027, 1, 1), Ends = null,
        };

        Assert.True(DealRules.PeriodCoversOn(perpetual, new DateOnly(2999, 1, 1)));
        Assert.False(DealRules.PeriodCoversOn(perpetual, new DateOnly(2026, 12, 31)));
    }

    /// <summary>
    /// A contract that says nothing about the period is not a contract that
    /// granted rights forever. Unstated answers no.
    /// </summary>
    [Fact]
    public void AnUnstatedPeriod_NeverCoversAnything()
    {
        GrantPeriodInput unstated = new() { Kind = 4, Starts = null, Ends = null };

        Assert.Null(DealRules.DescribePeriodProblem(unstated));
        Assert.False(DealRules.PeriodCoversOn(unstated, new DateOnly(2027, 6, 1)));
        Assert.False(DealRules.PeriodsOverlap(unstated, unstated));
    }

    /// <summary>
    /// Open-ended and perpetual are different facts. One means the contract did
    /// not say when it ends; the other means it never does.
    /// </summary>
    [Fact]
    public void OpenEndedAndPerpetual_AreDistinctKinds()
    {
        int[] kinds = DealRules.KnownPeriodKinds();

        Assert.Equal(4, kinds.Length);
        Assert.Contains(1, kinds);
        Assert.Contains(3, kinds);
    }

    [Fact]
    public void APeriodMissingItsStartDate_IsRefused()
    {
        foreach (int kind in new[] { 1, 2, 3 })
        {
            GrantPeriodInput missing = new() { Kind = kind, Starts = null, Ends = null };

            Assert.NotNull(DealRules.DescribePeriodProblem(missing));
        }
    }

    [Fact]
    public void AnUnknownPeriodKind_IsRefused()
    {
        Assert.NotNull(DealRules.DescribePeriodProblem(new GrantPeriodInput { Kind = 42 }));
    }

    // ------------------------------------------------------------- properties

    /// <summary>Overlap is symmetric: if A meets B, B meets A.</summary>
    [Theory]
    [MemberData(nameof(PeriodPairs))]
    public void OverlapIsSymmetric(int leftSeed, int rightSeed)
    {
        GrantPeriodInput left = Generate(leftSeed);
        GrantPeriodInput right = Generate(rightSeed);

        Assert.Equal(
            DealRules.PeriodsOverlap(left, right),
            DealRules.PeriodsOverlap(right, left));
    }

    /// <summary>A well-formed period overlaps itself, unless it states nothing.</summary>
    [Theory]
    [MemberData(nameof(PeriodSeeds))]
    public void AStatedPeriodOverlapsItself(int seed)
    {
        GrantPeriodInput period = Generate(seed);

        Assert.Equal(period.Kind != 4, DealRules.PeriodsOverlap(period, period));
    }

    /// <summary>Anything the period covers falls inside its own bounds.</summary>
    [Theory]
    [MemberData(nameof(PeriodSeeds))]
    public void CoverageNeverPrecedesTheStart(int seed)
    {
        GrantPeriodInput period = Generate(seed);

        if (!period.Starts.HasValue)
        {
            return;
        }

        Assert.False(DealRules.PeriodCoversOn(period, period.Starts.Value.AddDays(-1)));
    }

    public static TheoryData<int> PeriodSeeds()
    {
        TheoryData<int> data = [];

        for (int seed = 1; seed <= 16; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    public static TheoryData<int, int> PeriodPairs()
    {
        TheoryData<int, int> data = [];

        for (int left = 1; left <= 8; left++)
        {
            for (int right = 1; right <= 8; right++)
            {
                data.Add(left, right);
            }
        }

        return data;
    }

    /// <summary>A deterministic well-formed period for a seed.</summary>
    private static GrantPeriodInput Generate(int seed)
    {
        Random random = new(seed);

        int kind = random.Next(1, 5);
        DateOnly start = new DateOnly(2027, 1, 1).AddDays(random.Next(0, 400));

        return kind switch
        {
            2 => Fixed(start, start.AddDays(random.Next(1, 900))),
            4 => new GrantPeriodInput { Kind = 4 },
            _ => new GrantPeriodInput { Kind = kind, Starts = start },
        };
    }

    private static GrantPeriodInput Fixed(DateOnly starts, DateOnly ends) =>
        new() { Kind = 2, Starts = starts, Ends = ends };
}

/// <summary>
/// Deadline rules: what resolves to a date, and what honestly does not.
/// </summary>
public sealed class DeadlineRuleTests
{
    [Fact]
    public void AnAbsoluteDeadline_IsTheDateItStates()
    {
        DeadlineRuleInput rule = new() { Kind = 1, On = new DateOnly(2027, 3, 1) };

        Assert.Equal(new DateOnly(2027, 3, 1), DealRules.ResolveDeadline(rule, null));
        Assert.Null(DealRules.DescribeDeadlineProblem(rule, null));
    }

    [Theory]
    [InlineData(30, 1, false, "2027-03-31")]
    [InlineData(30, 1, true, "2027-01-30")]
    [InlineData(2, 2, false, "2027-03-15")]
    [InlineData(6, 3, false, "2027-09-01")]
    [InlineData(1, 4, false, "2028-03-01")]
    public void ARelativeDeadline_CountsFromItsAnchor(
        int offset,
        int unit,
        bool before,
        string expected)
    {
        DeadlineRuleInput rule = Relative(offset, unit, before);

        DateOnly? resolved = DealRules.ResolveDeadline(rule, new DateOnly(2027, 3, 1));

        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), resolved);
    }

    /// <summary>
    /// "30 days after delivery" is not a date until delivery happens. The rule is
    /// kept and no date is invented.
    /// </summary>
    [Fact]
    public void AnUnknownAnchor_LeavesTheDeadlineUnresolved()
    {
        DeadlineRuleInput rule = Relative(30, 1, before: false);

        Assert.Null(DealRules.ResolveDeadline(rule, null));

        string problem = DealRules.DescribeDeadlineProblem(rule, null);

        Assert.NotNull(problem);
        Assert.Contains("has not happened", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// AgencyOS holds no holiday calendar, so it records a business-day rule and
    /// computes nothing rather than quietly counting calendar days.
    /// </summary>
    [Fact]
    public void ABusinessDayRule_IsRecordedAndNotApproximated()
    {
        DeadlineRuleInput rule = Relative(10, 1, before: false);
        rule.Basis = 2;

        Assert.True(DealRules.IsDeadlineRuleValid(rule));
        Assert.Null(DealRules.ResolveDeadline(rule, new DateOnly(2027, 3, 1)));

        string problem = DealRules.DescribeDeadlineProblem(rule, new DateOnly(2027, 3, 1));

        Assert.Contains("holiday calendar", problem, StringComparison.Ordinal);

        Assert.False(DealRules.IsCalendarBasisComputable(2));
        Assert.True(DealRules.IsCalendarBasisComputable(1));
    }

    [Fact]
    public void AnUnstructuredRule_ResolvesToNothingAndSaysSo()
    {
        DeadlineRuleInput rule = new() { Kind = 3 };

        Assert.True(DealRules.IsDeadlineRuleValid(rule));
        Assert.Null(DealRules.ResolveDeadline(rule, new DateOnly(2027, 3, 1)));
        Assert.NotNull(DealRules.DescribeDeadlineProblem(rule, new DateOnly(2027, 3, 1)));
    }

    [Fact]
    public void AMalformedRule_IsRefused()
    {
        Assert.False(DealRules.IsDeadlineRuleValid(new DeadlineRuleInput { Kind = 1 }));
        Assert.False(DealRules.IsDeadlineRuleValid(new DeadlineRuleInput { Kind = 2 }));
        Assert.False(DealRules.IsDeadlineRuleValid(new DeadlineRuleInput { Kind = 42 }));
    }

    [Fact]
    public void PastDueIsComputedFromTheDate()
    {
        DateOnly today = new(2027, 6, 1);

        Assert.True(DealRules.IsDeadlinePast(today, new DateOnly(2027, 5, 31)));
        Assert.False(DealRules.IsDeadlinePast(today, today));
        Assert.False(DealRules.IsDeadlinePast(today, new DateOnly(2027, 6, 2)));

        Assert.Equal(-1, DealRules.DaysUntilDeadline(today, new DateOnly(2027, 5, 31)));
        Assert.Equal(30, DealRules.DaysUntilDeadline(today, new DateOnly(2027, 7, 1)));
    }

    // ------------------------------------------------------------- properties

    /// <summary>Resolution is a pure function of the rule and the anchor.</summary>
    [Theory]
    [MemberData(nameof(Offsets))]
    public void ResolutionIsDeterministic(int offset, int unit, bool before)
    {
        DeadlineRuleInput rule = Relative(offset, unit, before);
        DateOnly anchor = new(2027, 3, 1);

        Assert.Equal(
            DealRules.ResolveDeadline(rule, anchor),
            DealRules.ResolveDeadline(rule, anchor));
    }

    /// <summary>A forward offset lands on or after the anchor; a backward one before.</summary>
    [Theory]
    [MemberData(nameof(Offsets))]
    public void DirectionIsHonoured(int offset, int unit, bool before)
    {
        DateOnly anchor = new(2027, 3, 1);

        DateOnly resolved = DealRules.ResolveDeadline(Relative(offset, unit, before), anchor)!.Value;

        if (before)
        {
            Assert.True(resolved <= anchor);
        }
        else
        {
            Assert.True(resolved >= anchor);
        }
    }

    /// <summary>A zero offset is the anchor itself, whichever way it points.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AZeroOffsetIsTheAnchor(int unit)
    {
        DateOnly anchor = new(2027, 3, 1);

        Assert.Equal(anchor, DealRules.ResolveDeadline(Relative(0, unit, false), anchor));
        Assert.Equal(anchor, DealRules.ResolveDeadline(Relative(0, unit, true), anchor));
    }

    public static TheoryData<int, int, bool> Offsets()
    {
        TheoryData<int, int, bool> data = [];

        foreach (int offset in new[] { 0, 1, 10, 30, 90 })
        {
            foreach (int unit in new[] { 1, 2, 3, 4 })
            {
                data.Add(offset, unit, false);
                data.Add(offset, unit, true);
            }
        }

        return data;
    }

    private static DeadlineRuleInput Relative(int offset, int unit, bool before) =>
        new()
        {
            Kind = 2,
            Anchor = 3,
            Offset = offset,
            Unit = unit,
            Before = before,
            Basis = 1,
        };
}
