using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using Xunit;

namespace AgencyOS.Tests.Unit.Deals;

/// <summary>Money, and the arithmetic it is not allowed to be.</summary>
public sealed class MoneyTests
{
    [Fact]
    public void MoneyIsAlwaysAnAmountAndACurrency()
    {
        Money money = Money.Create(500_000m, "USD");

        Assert.Equal(500_000m, money.Amount);
        Assert.Equal("USD", money.Currency.Value);
        Assert.Equal("500000.00 USD", money.ToString());
    }

    /// <summary>
    /// The type carries no floating-point surface at all. A double overload here
    /// would be the one place the prohibition could be bypassed by accident.
    /// </summary>
    [Fact]
    public void MoneyExposesNoBinaryFloatingPoint()
    {
        Type[] forbidden = [typeof(double), typeof(float), typeof(Half)];

        foreach (System.Reflection.MethodInfo method in typeof(Money).GetMethods())
        {
            Assert.DoesNotContain(method.ReturnType, forbidden);

            foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
            {
                Assert.DoesNotContain(parameter.ParameterType, forbidden);
            }
        }

        foreach (System.Reflection.PropertyInfo property in typeof(Money).GetProperties())
        {
            Assert.DoesNotContain(property.PropertyType, forbidden);
        }
    }

    /// <summary>
    /// Minor units come from the currency, not from an assumption that money has
    /// two decimal places.
    /// </summary>
    [Theory]
    [InlineData("USD", 2)]
    [InlineData("GBP", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KRW", 0)]
    [InlineData("CLP", 0)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    [InlineData("TND", 3)]
    public void EachCurrencyCarriesItsOwnMinorUnits(string code, int minorUnits)
    {
        Assert.Equal(minorUnits, CurrencyCode.Parse(code).MinorUnits);
    }

    [Fact]
    public void AZeroDecimalCurrency_RefusesFractionalAmounts()
    {
        Assert.Equal(1_000_000m, Money.Create(1_000_000m, "JPY").Amount);

        Assert.Throws<DomainException>(() => Money.Create(1_000_000.5m, "JPY"));
    }

    [Fact]
    public void AThreeDecimalCurrency_AcceptsThreePlaces()
    {
        Assert.Equal(1_250.125m, Money.Create(1_250.125m, "KWD").Amount);

        Assert.Throws<DomainException>(() => Money.Create(1_250.1255m, "KWD"));
    }

    [Fact]
    public void AnUnknownCurrency_IsRefusedRatherThanStored()
    {
        Assert.Throws<DomainException>(() => Money.Create(1m, "XYZ"));
        Assert.Throws<DomainException>(() => Money.Create(1m, ""));
        Assert.Throws<DomainException>(() => Money.Create(1m, null));

        Assert.False(CurrencyCode.IsKnown("XYZ"));
        Assert.Null(CurrencyCode.TryParse("XYZ"));
    }

    [Fact]
    public void CurrencyCodesAreNormalisedToUpperCase()
    {
        Assert.Equal("USD", CurrencyCode.Parse("usd").Value);
        Assert.Equal("EUR", CurrencyCode.Parse(" eur ").Value);
    }

    /// <summary>
    /// Negative money is refused because a term records what somebody is to be
    /// paid. A deduction is a different term, and expressing it as negative
    /// compensation would silently invert every total built on it.
    /// </summary>
    [Fact]
    public void NegativeMoneyIsRefused()
    {
        Assert.Throws<DomainException>(() => Money.Create(-1m, "USD"));
    }

    /// <summary>Every code in the table is well formed and self-consistent.</summary>
    [Fact]
    public void TheCurrencyTableIsWellFormed()
    {
        Assert.NotEmpty(CurrencyCode.All);

        foreach (string code in CurrencyCode.All)
        {
            Assert.Equal(3, code.Length);
            Assert.Equal(code.ToUpperInvariant(), code);
            Assert.True(DealRules.IsCurrencyShaped(code), $"{code} is not shaped like a currency code.");

            int minorUnits = CurrencyCode.Parse(code).MinorUnits;
            Assert.InRange(minorUnits, 0, 3);
        }
    }
}

/// <summary>
/// Term values, and the illegal combinations the rules kernel makes unbuildable.
/// </summary>
public sealed class TermValueTests
{
    [Fact]
    public void EveryValueKind_HasAWorkingRoundTrip()
    {
        foreach (TermValueKind kind in Enum.GetValues<TermValueKind>())
        {
            TermInput input = Sample(kind);

            Assert.Null(DealRules.DescribeTermProblem(input));
            Assert.Equal((int)kind, DealRules.ValueKindOf(input));
        }
    }

    /// <summary>
    /// A term carrying a value its kind has no use for is refused, not ignored.
    /// Ignoring it means the value silently disappears on the next read.
    /// </summary>
    [Fact]
    public void AValueTheKindHasNoUseFor_IsRefused()
    {
        TermInput money = Sample(TermValueKind.Money);
        money.Text = "and also this";

        Assert.NotNull(DealRules.DescribeTermProblem(money));
    }

    [Fact]
    public void MoneyWithoutACurrency_IsRefused()
    {
        TermInput money = Sample(TermValueKind.Money);
        money.Currency = null;

        Assert.NotNull(DealRules.DescribeTermProblem(money));

        TermInput malformed = Sample(TermValueKind.Money);
        malformed.Currency = "dollars";

        Assert.NotNull(DealRules.DescribeTermProblem(malformed));
    }

    [Fact]
    public void AKindMissingItsValue_IsRefused()
    {
        foreach (TermValueKind kind in Enum.GetValues<TermValueKind>())
        {
            TermInput empty = new() { Code = 1, Kind = (int)kind, Sequence = 1 };

            Assert.NotNull(DealRules.DescribeTermProblem(empty));
        }
    }

    [Fact]
    public void AnUnknownKind_IsRefused()
    {
        TermInput unknown = new() { Code = 1, Kind = 42, Sequence = 1 };

        Assert.NotNull(DealRules.DescribeTermProblem(unknown));
        Assert.Equal(0, DealRules.ValueKindOf(unknown));
    }

    /// <summary>
    /// The kind admits escalations above 100%, because "110% of the prior season
    /// fee" is an ordinary term. The catalog narrows it where a share is meant.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(2.5, true)]
    [InlineData(100, true)]
    [InlineData(110, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    [InlineData(-1, false)]
    public void PercentageBoundsAreGenerousAtTheKindLevel(double percentage, bool valid)
    {
        TermInput input = Sample(TermValueKind.Percentage);
        input.Number = (decimal)percentage;

        Assert.Equal(valid, DealRules.IsTermValid(input));
    }

    [Fact]
    public void ADurationMustStateItsUnit()
    {
        TermInput unitless = Sample(TermValueKind.Duration);
        unitless.Unit = null;

        Assert.NotNull(DealRules.DescribeTermProblem(unitless));
    }

    [Fact]
    public void BlankTextIsRefused()
    {
        TermInput blank = Sample(TermValueKind.Text);
        blank.Text = "   ";

        Assert.NotNull(DealRules.DescribeTermProblem(blank));
    }

    /// <summary>
    /// Comparability is a property of the pair, and money in different currencies
    /// is not comparable. M7 holds no exchange rates, and inventing one would
    /// produce a difference somebody might act on.
    /// </summary>
    [Fact]
    public void AmountsInDifferentCurrencies_AreNotComparable()
    {
        TermInput dollars = Sample(TermValueKind.Money);

        TermInput pounds = Sample(TermValueKind.Money);
        pounds.Currency = "GBP";

        Assert.True(DealRules.AreComparable(dollars, dollars));
        Assert.False(DealRules.AreComparable(dollars, pounds));
    }

    [Fact]
    public void DifferentKinds_AreNeverComparable()
    {
        Assert.False(
            DealRules.AreComparable(Sample(TermValueKind.Money), Sample(TermValueKind.Percentage)));
    }

    [Fact]
    public void TextAndBooleanValues_AreNotNumericallyComparable()
    {
        Assert.False(DealRules.AreComparable(Sample(TermValueKind.Text), Sample(TermValueKind.Text)));
        Assert.False(
            DealRules.AreComparable(Sample(TermValueKind.Boolean), Sample(TermValueKind.Boolean)));
    }

    internal static TermInput Sample(TermValueKind kind) => kind switch
    {
        TermValueKind.Money => new TermInput
        {
            Code = (int)DealTermCode.Fee, Kind = (int)kind, Amount = 500_000m, Currency = "USD", Sequence = 1,
        },
        TermValueKind.Percentage => new TermInput
        {
            Code = (int)DealTermCode.BackendPercentage, Kind = (int)kind, Number = 2.5m, Sequence = 1,
        },
        TermValueKind.Integer => new TermInput
        {
            Code = (int)DealTermCode.OtherTerm, Kind = (int)kind, Whole = 7, Sequence = 1,
        },
        TermValueKind.Decimal => new TermInput
        {
            Code = (int)DealTermCode.OtherTerm, Kind = (int)kind, Number = 1.75m, Sequence = 1,
        },
        TermValueKind.Text => new TermInput
        {
            Code = (int)DealTermCode.CreditBilling, Kind = (int)kind, Text = "First position", Sequence = 1,
        },
        TermValueKind.Boolean => new TermInput
        {
            Code = (int)DealTermCode.OtherTerm, Kind = (int)kind, Flag = true, Sequence = 1,
        },
        TermValueKind.Date => new TermInput
        {
            Code = (int)DealTermCode.StartDate, Kind = (int)kind, Date = new DateOnly(2027, 1, 11), Sequence = 1,
        },
        TermValueKind.Duration => new TermInput
        {
            Code = (int)DealTermCode.TermLength,
            Kind = (int)kind,
            Whole = 2,
            Unit = (int)TermUnit.Year,
            Sequence = 1,
        },
        TermValueKind.Count => new TermInput
        {
            Code = (int)DealTermCode.EpisodeCount,
            Kind = (int)kind,
            Whole = 10,
            Unit = (int)TermUnit.Episode,
            Sequence = 1,
        },
        _ => throw new InvalidOperationException($"No sample for {kind}."),
    };
}

/// <summary>
/// The term catalog, checked structurally rather than by review.
/// </summary>
/// <remarks>
/// M5 lost seven saved-view filters because two parallel shapes depended on a
/// person remembering every entry. These tests are the same defence applied to the
/// term vocabulary before it can happen again (ADR-0021).
/// </remarks>
public sealed class DealTermCatalogTests
{
    [Fact]
    public void EveryTermCode_HasADefinition()
    {
        foreach (DealTermCode code in Enum.GetValues<DealTermCode>())
        {
            Assert.True(
                DealTermCatalog.Find(code) is not null,
                $"Term '{code}' has no catalog definition.");
        }
    }

    [Fact]
    public void EveryDefinition_NamesARealTermCode()
    {
        foreach (DealTermDefinition definition in DealTermCatalog.All)
        {
            Assert.True(Enum.IsDefined(definition.Code));
        }

        Assert.Equal(Enum.GetValues<DealTermCode>().Length, DealTermCatalog.All.Count);
    }

    [Fact]
    public void EveryDefinition_UsesAValueKindTheRulesKernelCanParse()
    {
        int[] parsable = DealRules.KnownValueKinds();

        foreach (DealTermDefinition definition in DealTermCatalog.All)
        {
            Assert.Contains((int)definition.ValueKind, parsable);
        }
    }

    /// <summary>
    /// The redaction rule rests on this: nothing carrying money or points may be
    /// classified as structure, or it would be visible without
    /// <c>deals.economics.read</c>.
    /// </summary>
    [Fact]
    public void EveryMoneyOrPercentageTerm_IsClassifiedEconomic()
    {
        foreach (DealTermDefinition definition in DealTermCatalog.All)
        {
            bool carriesValue =
                definition.ValueKind is TermValueKind.Money or TermValueKind.Percentage;

            if (carriesValue)
            {
                Assert.Equal(TermSensitivity.Economic, definition.Sensitivity);
            }
        }
    }

    /// <summary>And the reverse: nothing classified as structure carries money.</summary>
    [Fact]
    public void NoStructuralTerm_CarriesMoney()
    {
        foreach (DealTermDefinition definition in DealTermCatalog.All)
        {
            if (definition.Sensitivity == TermSensitivity.Structural)
            {
                Assert.NotEqual(TermValueKind.Money, definition.ValueKind);
                Assert.NotEqual(TermValueKind.Percentage, definition.ValueKind);
            }
        }
    }

    [Fact]
    public void EveryDefinitionHasADisplayName()
    {
        foreach (DealTermDefinition definition in DealTermCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
        }
    }

    /// <summary>Units are only stated for the kinds that can carry one.</summary>
    [Fact]
    public void OnlyDurationsAndCounts_AllowUnits()
    {
        foreach (DealTermDefinition definition in DealTermCatalog.All)
        {
            bool unitBearing = definition.ValueKind is TermValueKind.Duration or TermValueKind.Count;

            if (!unitBearing)
            {
                Assert.Empty(definition.AllowedUnits);
            }
            else
            {
                Assert.NotEmpty(definition.AllowedUnits);
            }
        }
    }

    /// <summary>
    /// The catalog narrows what the kind admits: a share of something cannot
    /// exceed the whole of it.
    /// </summary>
    [Fact]
    public void ParticipationTerms_AreBoundedToOneHundredPercent()
    {
        foreach (DealTermCode code in new[]
        {
            DealTermCode.BackendPercentage,
            DealTermCode.GrossParticipation,
            DealTermCode.NetParticipation,
        })
        {
            DealTermDefinition definition = DealTermCatalog.Require(code);

            Assert.Equal(0m, definition.Minimum);
            Assert.Equal(100m, definition.Maximum);
        }
    }

    /// <summary>
    /// The vocabulary records option economics and administers nothing. Exercise,
    /// deadlines, notice and rights are M8.
    /// </summary>
    [Fact]
    public void TheVocabularyStopsShortOfRightsAdministration()
    {
        string[] names = [.. Enum.GetNames<DealTermCode>()];

        Assert.DoesNotContain("OptionExercise", names);
        Assert.DoesNotContain("OptionDeadline", names);
        Assert.DoesNotContain("RightsGrant", names);
        Assert.DoesNotContain("Territory", names);
        Assert.DoesNotContain("Exclusivity", names);
        Assert.DoesNotContain("NoticePeriod", names);

        Assert.Contains("OptionPeriodCompensation", names);
    }

    [Fact]
    public void ApplicabilityIsHonouredWhenBuildingATerm()
    {
        Assert.False(DealTermCatalog.Require(DealTermCode.EpisodeCount).AppliesTo(DealKind.ProjectSale));
        Assert.True(DealTermCatalog.Require(DealTermCode.EpisodeCount).AppliesTo(DealKind.Writing));

        Assert.DoesNotContain(
            DealTermCatalog.For(DealKind.ProjectSale),
            definition => definition.Code == DealTermCode.EpisodeCount);
    }

    [Fact]
    public void EconomicClassification_IsQueryableByCode()
    {
        Assert.True(DealTermCatalog.IsEconomic(DealTermCode.GuaranteedCompensation));
        Assert.True(DealTermCatalog.IsEconomic(DealTermCode.BackendPercentage));

        Assert.False(DealTermCatalog.IsEconomic(DealTermCode.StartDate));
        Assert.False(DealTermCatalog.IsEconomic(DealTermCode.EpisodeCount));
        Assert.False(DealTermCatalog.IsEconomic(DealTermCode.CreditBilling));
    }
}
