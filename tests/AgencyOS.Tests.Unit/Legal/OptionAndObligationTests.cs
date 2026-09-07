using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using Xunit;

// The rules kernel and the domain both name a DeadlineRule: one is the kernel's
// internal union, the other the domain's value object built on it. Tests reach for
// the domain type, so it is aliased rather than fully qualified at every use.
using DeadlineRule = AgencyOS.Domain.Legal.DeadlineRule;

namespace AgencyOS.Tests.Unit.Legal;

/// <summary>
/// The option state machine, enumerated rather than sampled.
/// </summary>
/// <remarks>
/// All 30 pairs. Every route leaves Available and none returns: an exercised
/// option that could go back would let a later amendment rewrite what the holder
/// did rather than record a new fact (ADR-0022).
/// </remarks>
public sealed class OptionTransitionTests
{
    private static readonly Dictionary<(OptionStatus From, OptionTransition Trigger), OptionStatus>
        Legal = new()
        {
            [(OptionStatus.Available, OptionTransition.Exercise)] = OptionStatus.Exercised,
            [(OptionStatus.Available, OptionTransition.Decline)] = OptionStatus.Declined,
            [(OptionStatus.Available, OptionTransition.RecordExpiry)] = OptionStatus.Expired,
            [(OptionStatus.Available, OptionTransition.Waive)] = OptionStatus.Waived,
            [(OptionStatus.Available, OptionTransition.Cancel)] = OptionStatus.Cancelled,
        };

    public static TheoryData<OptionStatus, OptionTransition> EveryPair()
    {
        TheoryData<OptionStatus, OptionTransition> data = [];

        foreach (OptionStatus status in Enum.GetValues<OptionStatus>())
        {
            foreach (OptionTransition transition in Enum.GetValues<OptionTransition>())
            {
                data.Add(status, transition);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void EveryTransition_MatchesThePublishedTable(OptionStatus from, OptionTransition trigger)
    {
        bool expected = Legal.ContainsKey((from, trigger));

        Assert.Equal(expected, ContractOption.Permits(from, trigger));

        if (expected)
        {
            Assert.Contains(Legal[(from, trigger)], ContractOption.ReachableFrom(from));
        }
    }

    [Fact]
    public void TheTableIsCompletelyEnumerated()
    {
        int pairs = Enum.GetValues<OptionStatus>().Length * Enum.GetValues<OptionTransition>().Length;

        Assert.Equal(30, pairs);
        Assert.Equal(5, Legal.Count);
    }

    /// <summary>Every outcome but Available is final. An option resolves once.</summary>
    [Theory]
    [InlineData(OptionStatus.Exercised)]
    [InlineData(OptionStatus.Declined)]
    [InlineData(OptionStatus.Expired)]
    [InlineData(OptionStatus.Waived)]
    [InlineData(OptionStatus.Cancelled)]
    public void AResolvedOptionAcceptsNothing(OptionStatus resolved)
    {
        Assert.Empty(ContractOption.ReachableFrom(resolved));
        Assert.True(DealRules.IsOptionTerminal((int)resolved));
    }

    /// <summary>Four of the five outcomes carry the date they happened on.</summary>
    [Theory]
    [InlineData(OptionStatus.Exercised, true)]
    [InlineData(OptionStatus.Declined, true)]
    [InlineData(OptionStatus.Expired, true)]
    [InlineData(OptionStatus.Waived, true)]
    [InlineData(OptionStatus.Cancelled, false)]
    [InlineData(OptionStatus.Available, false)]
    public void ResolutionDatesAreRequiredWhereTheyMeanSomething(
        OptionStatus status,
        bool carriesDate)
    {
        Assert.Equal(carriesDate, DealRules.OptionHasResolutionDate((int)status));
    }
}

/// <summary>The option aggregate: windows, exercise and expiry.</summary>
public sealed class ContractOptionTests
{
    private static readonly OrganizationId Tenant = new(Guid.CreateVersion7());
    private static readonly UserId Actor = new(Guid.CreateVersion7());
    private static readonly ContractId Contract = ContractId.New();
    private static readonly ContractVersionId VersionId = ContractVersionId.New();
    private static readonly Guid Holder = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2027, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewOption_IsAvailableAndRecordsThat()
    {
        ContractOption option = Available();

        Assert.Equal(OptionStatus.Available, option.Status);
        Assert.False(option.IsTerminal);
        Assert.Equal(new DateOnly(2027, 9, 1), option.ResolvedDeadlineOn);

        OptionEvent recorded = Assert.Single(option.Events);
        Assert.Equal(OptionEventKind.Recorded, recorded.Kind);
    }

    [Fact]
    public void ExercisingRecordsTheDateAndClosesTheOption()
    {
        ContractOption option = Available();

        option.Exercise(new DateOnly(2027, 8, 15), Now, Actor, option.Version, "Picked up season two.");

        Assert.Equal(OptionStatus.Exercised, option.Status);
        Assert.Equal(new DateOnly(2027, 8, 15), option.ResolvedOn);
        Assert.True(option.IsTerminal);
    }

    /// <summary>
    /// An exercised option never goes back. A later amendment restoring one is a
    /// new legal fact and a new row.
    /// </summary>
    [Fact]
    public void AnExercisedOptionCannotBeReopened()
    {
        ContractOption option = Available();

        option.Exercise(new DateOnly(2027, 8, 15), Now, Actor, option.Version);

        foreach (Action act in new Action[]
        {
            () => option.Decline(new DateOnly(2027, 8, 20), Now, Actor, option.Version),
            () => option.Waive(new DateOnly(2027, 8, 20), Now, Actor, option.Version),
            () => option.Cancel(Now, Actor, option.Version),
        })
        {
            Assert.Throws<DomainException>(act);
        }
    }

    /// <summary>
    /// Nothing expires because a clock ticked. The deadline has to be knowable and
    /// behind us, and somebody has to record it.
    /// </summary>
    [Fact]
    public void ExpiryIsRecordedRatherThanInferred()
    {
        ContractOption early = Available();

        // The deadline is still ahead.
        Assert.Throws<DomainException>(() => early.RecordExpiry(Now, Actor, early.Version));

        ContractOption lapsed = ContractOption.Record(
            Tenant,
            Contract,
            VersionId,
            OptionKind.Employment,
            Holder,
            "Season two services",
            DeadlineRule.On_(new DateOnly(2027, 5, 1)),
            Actor,
            Now);

        Assert.Equal(OptionStatus.Available, lapsed.Status);
        Assert.True(lapsed.IsPastDeadlineOn(DateOnly.FromDateTime(Now.UtcDateTime)));

        lapsed.RecordExpiry(Now, Actor, lapsed.Version);

        Assert.Equal(OptionStatus.Expired, lapsed.Status);
        Assert.Equal(new DateOnly(2027, 5, 1), lapsed.ResolvedOn);
    }

    /// <summary>
    /// An option whose deadline nobody can work out cannot expire, because nothing
    /// knows when it would have.
    /// </summary>
    [Fact]
    public void AnOptionWithAnUnresolvedDeadline_CannotExpire()
    {
        ContractOption option = ContractOption.Record(
            Tenant,
            Contract,
            VersionId,
            OptionKind.Sequel,
            Holder,
            "Sequel rights",
            DeadlineRule.After(30, DeadlineOffsetUnit.Days, DeadlineAnchor.OnDelivery),
            Actor,
            Now);

        Assert.Null(option.ResolvedDeadlineOn);
        Assert.False(option.IsPastDeadlineOn(new DateOnly(2099, 1, 1)));

        DomainException failure =
            Assert.Throws<DomainException>(() => option.RecordExpiry(Now, Actor, option.Version));

        Assert.Contains("not a date this build can work out", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Thirty days after delivery" becomes a date when delivery does. The rule
    /// never changes; only what is known about it.
    /// </summary>
    [Fact]
    public void ADeadlineResolvesOnceItsAnchorIsKnown()
    {
        ContractOption option = ContractOption.Record(
            Tenant,
            Contract,
            VersionId,
            OptionKind.Sequel,
            Holder,
            "Sequel rights",
            DeadlineRule.After(30, DeadlineOffsetUnit.Days, DeadlineAnchor.OnDelivery),
            Actor,
            Now);

        Assert.Null(option.ResolvedDeadlineOn);

        option.ResolveDeadline(new DateOnly(2027, 7, 1), Now, option.Version);

        Assert.Equal(new DateOnly(2027, 7, 31), option.ResolvedDeadlineOn);
        Assert.Equal(DeadlineRuleKind.Relative, option.Deadline.Kind);
    }

    /// <summary>Exercisability comes from the window, not from a stored flag.</summary>
    [Fact]
    public void ExercisabilityIsDerivedFromTheWindow()
    {
        ContractOption option = ContractOption.Record(
            Tenant,
            Contract,
            VersionId,
            OptionKind.Renewal,
            Holder,
            "Renewal",
            DeadlineRule.On_(new DateOnly(2027, 9, 1)),
            Actor,
            Now,
            windowOpensOn: new DateOnly(2027, 7, 1));

        Assert.False(option.IsExercisableOn(new DateOnly(2027, 6, 30)));
        Assert.True(option.IsExercisableOn(new DateOnly(2027, 7, 1)));
        Assert.True(option.IsExercisableOn(new DateOnly(2027, 9, 1)));
        Assert.False(option.IsExercisableOn(new DateOnly(2027, 9, 2)));

        option.Exercise(new DateOnly(2027, 7, 15), Now, Actor, option.Version);

        Assert.False(option.IsExercisableOn(new DateOnly(2027, 8, 1)));
    }

    [Fact]
    public void ADeadlineBeforeTheWindowOpens_IsRefused()
    {
        Assert.Throws<DomainException>(() =>
            ContractOption.Record(
                Tenant,
                Contract,
                VersionId,
                OptionKind.Renewal,
                Holder,
                "Renewal",
                DeadlineRule.On_(new DateOnly(2027, 7, 1)),
                Actor,
                Now,
                windowOpensOn: new DateOnly(2027, 8, 1)));
    }

    [Fact]
    public void AStaleExercise_IsRefused()
    {
        ContractOption option = Available();

        Assert.Throws<ConcurrencyConflictException>(() =>
            option.Exercise(new DateOnly(2027, 8, 15), Now, Actor, option.Version - 1));
    }

    private static ContractOption Available() =>
        ContractOption.Record(
            Tenant,
            Contract,
            VersionId,
            OptionKind.Employment,
            Holder,
            "Season two services",
            DeadlineRule.On_(new DateOnly(2027, 9, 1)),
            Actor,
            Now);
}

/// <summary>
/// The obligation state machine, enumerated rather than sampled.
/// </summary>
/// <remarks>
/// All 25 pairs. A breach determination can be reversed because it is a judgement
/// somebody made; a satisfied obligation cannot, because that would mean the
/// delivery unhappened (ADR-0022).
/// </remarks>
public sealed class ObligationTransitionTests
{
    private static readonly Dictionary<(ObligationStatus From, ObligationTransition Trigger), ObligationStatus>
        Legal = new()
        {
            [(ObligationStatus.Pending, ObligationTransition.Satisfy)] = ObligationStatus.Satisfied,
            [(ObligationStatus.Pending, ObligationTransition.Waive)] = ObligationStatus.Waived,
            [(ObligationStatus.Pending, ObligationTransition.RecordBreach)] = ObligationStatus.Breached,
            [(ObligationStatus.Pending, ObligationTransition.Cancel)] = ObligationStatus.Cancelled,

            [(ObligationStatus.Breached, ObligationTransition.Satisfy)] = ObligationStatus.Satisfied,
            [(ObligationStatus.Breached, ObligationTransition.Waive)] = ObligationStatus.Waived,
            [(ObligationStatus.Breached, ObligationTransition.Reinstate)] = ObligationStatus.Pending,
            [(ObligationStatus.Breached, ObligationTransition.Cancel)] = ObligationStatus.Cancelled,

            [(ObligationStatus.Waived, ObligationTransition.Reinstate)] = ObligationStatus.Pending,
        };

    public static TheoryData<ObligationStatus, ObligationTransition> EveryPair()
    {
        TheoryData<ObligationStatus, ObligationTransition> data = [];

        foreach (ObligationStatus status in Enum.GetValues<ObligationStatus>())
        {
            foreach (ObligationTransition transition in Enum.GetValues<ObligationTransition>())
            {
                data.Add(status, transition);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void EveryTransition_MatchesThePublishedTable(
        ObligationStatus from,
        ObligationTransition trigger)
    {
        bool expected = Legal.ContainsKey((from, trigger));

        Assert.Equal(expected, Obligation.Permits(from, trigger));

        if (expected)
        {
            Assert.Contains(Legal[(from, trigger)], Obligation.ReachableFrom(from));
        }
    }

    [Fact]
    public void TheTableIsCompletelyEnumerated()
    {
        int pairs =
            Enum.GetValues<ObligationStatus>().Length * Enum.GetValues<ObligationTransition>().Length;

        Assert.Equal(25, pairs);
        Assert.Equal(9, Legal.Count);
    }

    /// <summary>Delivered work stays delivered.</summary>
    [Fact]
    public void ASatisfiedObligationIsFinal()
    {
        Assert.Empty(Obligation.ReachableFrom(ObligationStatus.Satisfied));
    }

    /// <summary>
    /// The vocabulary has no Due or Overdue value. Past due is arithmetic over the
    /// row and today, and a stored flag would be wrong the moment the clock moved.
    /// </summary>
    [Fact]
    public void TheStatusVocabulary_HasNoStoredDueness()
    {
        string[] names = [.. Enum.GetNames<ObligationStatus>()];

        Assert.DoesNotContain("Due", names);
        Assert.DoesNotContain("Overdue", names);
        Assert.DoesNotContain("PastDue", names);
        Assert.DoesNotContain("Late", names);

        Assert.Contains("Pending", names);
        Assert.Contains("Breached", names);
    }

    /// <summary>Only outstanding work can be overdue. Done work never is.</summary>
    [Theory]
    [InlineData(ObligationStatus.Pending, true)]
    [InlineData(ObligationStatus.Breached, true)]
    [InlineData(ObligationStatus.Satisfied, false)]
    [InlineData(ObligationStatus.Waived, false)]
    [InlineData(ObligationStatus.Cancelled, false)]
    public void OnlyOutstandingObligationsCanBeOverdue(ObligationStatus status, bool canBe)
    {
        Assert.Equal(canBe, DealRules.ObligationCanBeOverdue((int)status));
        Assert.Equal(canBe, DealRules.IsObligationOutstanding((int)status));
    }
}

/// <summary>The obligation aggregate: dueness, satisfaction and breach.</summary>
public sealed class ObligationTests
{
    private static readonly OrganizationId Tenant = new(Guid.CreateVersion7());
    private static readonly UserId Actor = new(Guid.CreateVersion7());
    private static readonly ContractId Contract = ContractId.New();
    private static readonly ContractVersionId VersionId = ContractVersionId.New();
    private static readonly Guid Obligor = Guid.CreateVersion7();
    private static readonly Guid Obligee = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2027, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewObligationIsPendingAndRecordsThat()
    {
        Obligation obligation = Pending();

        Assert.Equal(ObligationStatus.Pending, obligation.Status);
        Assert.True(obligation.IsOutstanding);
        Assert.Equal(new DateOnly(2027, 5, 1), obligation.ResolvedDueOn);

        Assert.Single(obligation.Events);
    }

    /// <summary>
    /// Past due is derived, and it is not breach. An obligation can be weeks late
    /// and still pending, which is exactly what a work queue must surface.
    /// </summary>
    [Fact]
    public void PastDueIsDerived_AndIsNotBreach()
    {
        Obligation obligation = Pending();

        Assert.True(obligation.IsPastDueOn(new DateOnly(2027, 6, 1)));
        Assert.False(obligation.IsPastDueOn(new DateOnly(2027, 4, 30)));

        // Still pending. Nothing turned lateness into a legal conclusion.
        Assert.Equal(ObligationStatus.Pending, obligation.Status);
    }

    [Fact]
    public void ASatisfiedObligationIsNotOverdueHoweverLateItWas()
    {
        Obligation obligation = Pending();

        obligation.Satisfy(new DateOnly(2027, 5, 20), Now, Actor, obligation.Version);

        Assert.Equal(ObligationStatus.Satisfied, obligation.Status);
        Assert.Equal(new DateOnly(2027, 5, 20), obligation.ResolvedOn);
        Assert.False(obligation.IsPastDueOn(new DateOnly(2027, 6, 1)));
    }

    /// <summary>
    /// Breach is a determination, so recording one requires saying what it rests on.
    /// </summary>
    [Fact]
    public void RecordingBreachRequiresTheDeterminationBehindIt()
    {
        Obligation obligation = Pending();

        Assert.Throws<DomainException>(() =>
            obligation.RecordBreach(Now, Actor, obligation.Version, reason: "   "));

        obligation.RecordBreach(
            Now, Actor, obligation.Version, "Delivery not made after notice and cure period.");

        Assert.Equal(ObligationStatus.Breached, obligation.Status);
        Assert.True(obligation.IsOutstanding);
    }

    /// <summary>A breach determination can be reversed; it was a judgement.</summary>
    [Fact]
    public void ABreachDeterminationCanBeReversed()
    {
        Obligation obligation = Pending();

        obligation.RecordBreach(Now, Actor, obligation.Version, "Missed delivery.");
        obligation.Reinstate(Now, Actor, obligation.Version, "Counsel withdrew the determination.");

        Assert.Equal(ObligationStatus.Pending, obligation.Status);
        Assert.Null(obligation.ResolvedOn);
    }

    [Fact]
    public void AnObligationWithAnUnresolvedDueDateIsNeverOverdue()
    {
        Obligation obligation = Obligation.Record(
            Tenant,
            Contract,
            VersionId,
            Obligor,
            Obligee,
            ObligationKind.Delivery,
            "Deliver the first draft",
            DeadlineRule.After(30, DeadlineOffsetUnit.Days, DeadlineAnchor.OnCommencement),
            Actor,
            Now);

        Assert.Null(obligation.ResolvedDueOn);
        Assert.False(obligation.IsPastDueOn(new DateOnly(2099, 1, 1)));

        obligation.ResolveDueDate(new DateOnly(2027, 1, 1), Now, obligation.Version);

        Assert.Equal(new DateOnly(2027, 1, 31), obligation.ResolvedDueOn);
        Assert.True(obligation.IsPastDueOn(new DateOnly(2027, 6, 1)));
    }

    [Fact]
    public void APartyCannotOweItself()
    {
        Assert.Throws<DomainException>(() =>
            Obligation.Record(
                Tenant,
                Contract,
                VersionId,
                Obligor,
                Obligor,
                ObligationKind.Delivery,
                "Deliver something",
                DeadlineRule.On_(new DateOnly(2027, 5, 1)),
                Actor,
                Now));
    }

    [Fact]
    public void AStaleSatisfaction_IsRefused()
    {
        Obligation obligation = Pending();

        Assert.Throws<ConcurrencyConflictException>(() =>
            obligation.Satisfy(new DateOnly(2027, 5, 20), Now, Actor, obligation.Version - 1));
    }

    private static Obligation Pending() =>
        Obligation.Record(
            Tenant,
            Contract,
            VersionId,
            Obligor,
            Obligee,
            ObligationKind.Delivery,
            "Deliver the first draft",
            DeadlineRule.On_(new DateOnly(2027, 5, 1)),
            Actor,
            Now);
}
