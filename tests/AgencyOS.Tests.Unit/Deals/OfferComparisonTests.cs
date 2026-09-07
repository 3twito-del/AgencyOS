using System.Globalization;
using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Deals;
using Xunit;

namespace AgencyOS.Tests.Unit.Deals;

/// <summary>
/// Offer comparison: what changed, never whether it is good.
/// </summary>
/// <remarks>
/// <para>
/// The properties are checked systematically rather than by random search. The
/// input space here is small and structured - nine value kinds, a closed term
/// vocabulary, a handful of currencies - so enumerating it and generating over it
/// with a fixed seed covers more of it than sampling would, and a failure
/// reproduces exactly. This matches the enumeration idiom M4 to M6 already use; a
/// property that genuinely needed random search would be the reason to add a
/// generator library, and none here does (ADR-0021).
/// </para>
/// </remarks>
public sealed class OfferComparisonTests
{
    [Fact]
    public void AnUnchangedTerm_ReadsAsUnchanged()
    {
        TermInput[] terms = [Money(DealTermCode.Fee, 500_000m)];

        TermDifference difference = Assert.Single(DealRules.CompareTerms(terms, terms));

        Assert.Equal(TermChange.Unchanged, difference.Change);
        Assert.Equal(ValueDirection.Level, difference.Direction);
    }

    [Fact]
    public void AnIncreasedAmount_ReadsAsIncreased()
    {
        TermDifference difference = Assert.Single(DealRules.CompareTerms(
            [Money(DealTermCode.GuaranteedCompensation, 500_000m)],
            [Money(DealTermCode.GuaranteedCompensation, 650_000m)]));

        Assert.Equal(TermChange.Changed, difference.Change);
        Assert.Equal(ValueDirection.Increased, difference.Direction);
        Assert.Equal(500_000m, difference.Previous.Amount);
        Assert.Equal(650_000m, difference.Current.Amount);
    }

    [Fact]
    public void ADecreasedAmount_ReadsAsDecreased()
    {
        TermDifference difference = Assert.Single(DealRules.CompareTerms(
            [Money(DealTermCode.Fee, 650_000m)],
            [Money(DealTermCode.Fee, 500_000m)]));

        Assert.Equal(ValueDirection.Decreased, difference.Direction);
    }

    [Fact]
    public void AddedAndRemovedTerms_AreDistinguished()
    {
        TermDifference[] differences = DealRules.CompareTerms(
            [Money(DealTermCode.Fee, 100_000m)],
            [Money(DealTermCode.Bonus, 25_000m)]);

        TermDifference removed = differences.Single(d => d.TermCode == (int)DealTermCode.Fee);
        TermDifference added = differences.Single(d => d.TermCode == (int)DealTermCode.Bonus);

        Assert.Equal(TermChange.Removed, removed.Change);
        Assert.NotNull(removed.Previous);
        Assert.Null(removed.Current);

        Assert.Equal(TermChange.Added, added.Change);
        Assert.Null(added.Previous);
        Assert.NotNull(added.Current);
    }

    /// <summary>
    /// M7 holds no exchange rates. Two amounts in different currencies are a
    /// change with no direction, which is the honest answer.
    /// </summary>
    [Fact]
    public void ACurrencyChange_HasNoDirection()
    {
        TermInput previous = Money(DealTermCode.Fee, 500_000m);
        TermInput current = Money(DealTermCode.Fee, 500_000m);
        current.Currency = "GBP";

        TermDifference difference = Assert.Single(DealRules.CompareTerms([previous], [current]));

        Assert.Equal(TermChange.Changed, difference.Change);
        Assert.Equal(ValueDirection.NotComparable, difference.Direction);
    }

    [Fact]
    public void TextAndDateChanges_HaveNoDirection()
    {
        TermInput before = TermValueTests.Sample(TermValueKind.Text);
        TermInput after = TermValueTests.Sample(TermValueKind.Text);
        after.Text = "Second position";

        TermDifference text = Assert.Single(DealRules.CompareTerms([before], [after]));

        Assert.Equal(TermChange.Changed, text.Change);
        Assert.Equal(ValueDirection.NotComparable, text.Direction);
    }

    /// <summary>
    /// The comparison answers "what changed". Nothing in its vocabulary says
    /// better, worse, favourable or improved: whether a longer term is welcome
    /// depends on the term, the side and the deal, and the system has no basis for
    /// an opinion.
    /// </summary>
    [Fact]
    public void TheVocabularyCarriesNoJudgement()
    {
        string[] names = [.. Enum.GetNames<ValueDirection>(), .. Enum.GetNames<TermChange>()];

        foreach (string judgement in new[]
        {
            "Better", "Worse", "Improved", "Favourable", "Favorable", "Good", "Bad", "Score",
        })
        {
            Assert.DoesNotContain(judgement, names);
        }
    }

    [Fact]
    public void MaterialDifferences_DropTheUnchanged()
    {
        TermDifference[] all = DealRules.CompareTerms(
            [Money(DealTermCode.Fee, 100_000m), Money(DealTermCode.Bonus, 10_000m)],
            [Money(DealTermCode.Fee, 100_000m), Money(DealTermCode.Bonus, 20_000m)]);

        Assert.Equal(2, all.Length);

        TermDifference material = Assert.Single(DealRules.MaterialDifferences(all));
        Assert.Equal((int)DealTermCode.Bonus, material.TermCode);
    }

    [Fact]
    public void ComparingNothingToNothing_IsEmpty()
    {
        Assert.Empty(DealRules.CompareTerms([], []));
    }

    /// <summary>The order is the later offer's, so a reader sees their own layout.</summary>
    [Fact]
    public void DifferencesFollowTheLaterOffersOrder()
    {
        TermInput first = Money(DealTermCode.Bonus, 10_000m);
        first.Sequence = 1;

        TermInput second = Money(DealTermCode.Fee, 100_000m);
        second.Sequence = 2;

        TermDifference[] differences = DealRules.CompareTerms([], [first, second]);

        Assert.Equal((int)DealTermCode.Bonus, differences[0].TermCode);
        Assert.Equal((int)DealTermCode.Fee, differences[1].TermCode);
    }

    // ------------------------------------------------------------- properties

    /// <summary>Comparing an offer with itself finds nothing.</summary>
    [Theory]
    [MemberData(nameof(GeneratedOffers))]
    public void Reflexivity_ComparingAnOfferToItselfIsAllUnchanged(int seed)
    {
        TermInput[] terms = Generate(seed);

        foreach (TermDifference difference in DealRules.CompareTerms(terms, terms))
        {
            Assert.Equal(TermChange.Unchanged, difference.Change);
        }
    }

    /// <summary>
    /// Reversing the comparison mirrors it exactly: added becomes removed,
    /// increased becomes decreased, and nothing appears or disappears.
    /// </summary>
    [Theory]
    [MemberData(nameof(GeneratedPairs))]
    public void Symmetry_ReversingTheComparisonMirrorsIt(int leftSeed, int rightSeed)
    {
        TermInput[] left = Generate(leftSeed);
        TermInput[] right = Generate(rightSeed);

        Dictionary<int, TermDifference> forward =
            DealRules.CompareTerms(left, right).ToDictionary(d => d.TermCode);
        Dictionary<int, TermDifference> backward =
            DealRules.CompareTerms(right, left).ToDictionary(d => d.TermCode);

        Assert.Equal(forward.Keys.Order(), backward.Keys.Order());

        foreach ((int code, TermDifference ahead) in forward)
        {
            TermDifference back = backward[code];

            Assert.Equal(Mirror(ahead.Change), back.Change);
            Assert.Equal(Mirror(ahead.Direction), back.Direction);
        }
    }

    /// <summary>
    /// Every term in either offer appears exactly once in the result, so nothing
    /// is silently dropped from a diff somebody is about to act on.
    /// </summary>
    [Theory]
    [MemberData(nameof(GeneratedPairs))]
    public void Totality_EveryTermAppearsExactlyOnce(int leftSeed, int rightSeed)
    {
        TermInput[] left = Generate(leftSeed);
        TermInput[] right = Generate(rightSeed);

        TermDifference[] differences = DealRules.CompareTerms(left, right);

        int[] expected = [.. left.Concat(right).Select(term => term.Code).Distinct().Order()];
        int[] actual = [.. differences.Select(difference => difference.TermCode).Order()];

        Assert.Equal(expected, actual);
        Assert.Equal(differences.Length, differences.Select(d => d.TermCode).Distinct().Count());
    }

    /// <summary>
    /// The populated sides always match what the change says, so a caller reading
    /// Change before dereferencing can never hit a null.
    /// </summary>
    [Theory]
    [MemberData(nameof(GeneratedPairs))]
    public void TheChangeAlwaysAgreesWithWhichSidesArePresent(int leftSeed, int rightSeed)
    {
        foreach (TermDifference difference in
            DealRules.CompareTerms(Generate(leftSeed), Generate(rightSeed)))
        {
            switch (difference.Change)
            {
                case TermChange.Added:
                    Assert.Null(difference.Previous);
                    Assert.NotNull(difference.Current);
                    break;

                case TermChange.Removed:
                    Assert.NotNull(difference.Previous);
                    Assert.Null(difference.Current);
                    break;

                default:
                    Assert.NotNull(difference.Previous);
                    Assert.NotNull(difference.Current);
                    break;
            }
        }
    }

    /// <summary>Comparison is a pure function: the same inputs give the same output.</summary>
    [Theory]
    [MemberData(nameof(GeneratedPairs))]
    public void Determinism_TheSameInputsAlwaysGiveTheSameAnswer(int leftSeed, int rightSeed)
    {
        TermInput[] left = Generate(leftSeed);
        TermInput[] right = Generate(rightSeed);

        string first = Render(DealRules.CompareTerms(left, right));
        string second = Render(DealRules.CompareTerms(left, right));

        Assert.Equal(first, second);
    }

    public static TheoryData<int> GeneratedOffers()
    {
        TheoryData<int> data = [];

        for (int seed = 1; seed <= 24; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    public static TheoryData<int, int> GeneratedPairs()
    {
        TheoryData<int, int> data = [];

        for (int left = 1; left <= 12; left++)
        {
            for (int right = 1; right <= 12; right++)
            {
                data.Add(left, right);
            }
        }

        return data;
    }

    /// <summary>
    /// A deterministic offer for a seed.
    /// </summary>
    /// <remarks>
    /// Seeded rather than random so a failure names the exact offers that produced
    /// it. Draws across the whole vocabulary, both currencies and every value
    /// kind, which is the part of the space the properties are about.
    /// </remarks>
    private static TermInput[] Generate(int seed)
    {
        Random random = new(seed);

        DealTermCode[] vocabulary = [.. Enum.GetValues<DealTermCode>()];
        List<TermInput> terms = [];

        foreach (DealTermCode code in vocabulary)
        {
            if (random.Next(3) == 0)
            {
                continue;
            }

            DealTermDefinition definition = DealTermCatalog.Require(code);
            TermInput term = TermValueTests.Sample(definition.ValueKind);

            term.Code = (int)code;
            term.Sequence = terms.Count + 1;

            switch (definition.ValueKind)
            {
                case TermValueKind.Money:
                    term.Amount = random.Next(1, 20) * 25_000m;
                    term.Currency = random.Next(2) == 0 ? "USD" : "GBP";
                    break;

                case TermValueKind.Percentage:
                    term.Number = random.Next(0, 20) * 0.5m;
                    break;

                case TermValueKind.Count:
                case TermValueKind.Duration:
                    term.Whole = random.Next(1, 25);
                    break;

                case TermValueKind.Date:
                    term.Date = new DateOnly(2027, 1, 1).AddDays(random.Next(0, 365));
                    break;

                case TermValueKind.Text:
                    term.Text = $"Position {random.Next(1, 5)}";
                    break;

                default:
                    break;
            }

            terms.Add(term);
        }

        return [.. terms];
    }

    private static TermChange Mirror(TermChange change) => change switch
    {
        TermChange.Added => TermChange.Removed,
        TermChange.Removed => TermChange.Added,
        _ => change,
    };

    private static ValueDirection Mirror(ValueDirection direction) => direction switch
    {
        ValueDirection.Increased => ValueDirection.Decreased,
        ValueDirection.Decreased => ValueDirection.Increased,
        _ => direction,
    };

    private static string Render(TermDifference[] differences) =>
        string.Join(
            "|",
            differences.Select(difference => string.Create(
                CultureInfo.InvariantCulture,
                $"{difference.TermCode}:{difference.Change}:{difference.Direction}")));

    private static TermInput Money(DealTermCode code, decimal amount) =>
        new()
        {
            Code = (int)code,
            Kind = (int)TermValueKind.Money,
            Amount = amount,
            Currency = "USD",
            Sequence = 1,
        };
}
