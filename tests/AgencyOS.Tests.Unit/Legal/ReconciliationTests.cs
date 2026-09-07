using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;
using Xunit;

namespace AgencyOS.Tests.Unit.Legal;

/// <summary>
/// Negotiated against drafted: what changed, and nothing about whether it is good.
/// </summary>
/// <remarks>
/// Checked systematically rather than by random search, on the M7 pattern: the
/// input space is a closed vocabulary and nine value kinds, so enumerating it
/// covers more than sampling would and a failure reproduces exactly (ADR-0022).
/// </remarks>
public sealed class ReconciliationTests
{
    [Fact]
    public void ATermTheContractRepeats_Matches()
    {
        ReconciliationLine line = Assert.Single(DealRules.Reconcile(
            [Money(DealTermCode.GuaranteedCompensation, 650_000m)],
            [Money(DealTermCode.GuaranteedCompensation, 650_000m)]));

        Assert.Equal(ReconciliationResult.Matched, line.Result);
    }

    [Fact]
    public void ATermTheContractChanges_ReadsAsChangedWithItsDirection()
    {
        ReconciliationLine line = Assert.Single(DealRules.Reconcile(
            [Percentage(DealTermCode.BackendPercentage, 3m)],
            [Percentage(DealTermCode.BackendPercentage, 2.5m)]));

        Assert.Equal(ReconciliationResult.Changed, line.Result);
        Assert.Equal(ValueDirection.Decreased, line.Direction);
        Assert.Equal(3m, line.Negotiated.Number);
        Assert.Equal(2.5m, line.Contracted.Number);
    }

    [Fact]
    public void ATermTheContractDrops_ReadsAsMissing()
    {
        ReconciliationLine line = Assert.Single(DealRules.Reconcile(
            [Text(DealTermCode.CreditBilling, "First position")],
            []));

        Assert.Equal(ReconciliationResult.MissingFromContract, line.Result);
        Assert.NotNull(line.Negotiated);
        Assert.Null(line.Contracted);
    }

    [Fact]
    public void ATermTheContractIntroduces_ReadsAsAdded()
    {
        ReconciliationLine line = Assert.Single(DealRules.Reconcile(
            [],
            [Text(ContractTermCode.ExclusivitySummary, "12 months exclusive")]));

        Assert.Equal(ReconciliationResult.AddedInContract, line.Result);
        Assert.Null(line.Negotiated);
        Assert.NotNull(line.Contracted);
    }

    /// <summary>
    /// Money in two currencies cannot be compared. M8 holds no exchange rates, and
    /// reporting a direction would be arithmetic on a rate nobody supplied.
    /// </summary>
    [Fact]
    public void ACurrencyChange_IsNotComparable()
    {
        TermInput negotiated = Money(DealTermCode.Fee, 100_000m);
        TermInput contracted = Money(DealTermCode.Fee, 100_000m);
        contracted.Currency = "GBP";

        ReconciliationLine line = Assert.Single(DealRules.Reconcile([negotiated], [contracted]));

        Assert.Equal(ReconciliationResult.NotComparable, line.Result);
        Assert.Equal(ValueDirection.NotComparable, line.Direction);
    }

    /// <summary>A term the draft restates in a different shape cannot be compared.</summary>
    [Fact]
    public void ADifferentValueShape_IsNotComparable()
    {
        TermInput negotiated = Money(DealTermCode.GuaranteedCompensation, 650_000m);
        TermInput contracted = Text(ContractTermCode.GuaranteedCompensation, "To be agreed");

        ReconciliationLine line = Assert.Single(DealRules.Reconcile([negotiated], [contracted]));

        Assert.Equal(ReconciliationResult.NotComparable, line.Result);
    }

    /// <summary>
    /// The result vocabulary carries no judgement. Whether a change is acceptable
    /// is a legal question about a document AgencyOS has not read.
    /// </summary>
    [Fact]
    public void TheResultVocabularyCarriesNoJudgement()
    {
        string[] names = [.. Enum.GetNames<ReconciliationResult>()];

        foreach (string judgement in new[]
        {
            "Favourable", "Favorable", "Unfavourable", "Unfavorable", "Risk", "Material",
            "Bad", "Good", "Worse", "Better", "Score",
        })
        {
            Assert.DoesNotContain(judgement, names);
        }

        Assert.Equal(5, names.Length);
    }

    /// <summary>A whole draft, read the way a lawyer reads it.</summary>
    [Fact]
    public void AWholeDraft_ReportsEveryDifferenceExactlyOnce()
    {
        TermInput[] negotiated =
        [
            Money(DealTermCode.GuaranteedCompensation, 650_000m),
            Percentage(DealTermCode.BackendPercentage, 3m),
            Text(DealTermCode.CreditBilling, "First position"),
        ];

        TermInput[] contracted =
        [
            Money(ContractTermCode.GuaranteedCompensation, 650_000m),
            Percentage(ContractTermCode.BackendPercentage, 2.5m),
            Text(ContractTermCode.ExclusivitySummary, "12 months exclusive"),
        ];

        ReconciliationLine[] lines = DealRules.Reconcile(negotiated, contracted);

        Assert.Equal(4, lines.Length);

        Assert.Equal(
            ReconciliationResult.Matched,
            lines.Single(x => x.TermCode == (int)DealTermCode.GuaranteedCompensation).Result);
        Assert.Equal(
            ReconciliationResult.Changed,
            lines.Single(x => x.TermCode == (int)DealTermCode.BackendPercentage).Result);
        Assert.Equal(
            ReconciliationResult.MissingFromContract,
            lines.Single(x => x.TermCode == (int)DealTermCode.CreditBilling).Result);
        Assert.Equal(
            ReconciliationResult.AddedInContract,
            lines.Single(x => x.TermCode == (int)ContractTermCode.ExclusivitySummary).Result);

        Assert.Equal(3, DealRules.ReconciliationDifferenceCount(lines));
        Assert.False(DealRules.IsDraftFaithful(lines));
    }

    [Fact]
    public void ADraftThatSaysWhatWasAgreed_IsFaithful()
    {
        TermInput[] terms = [Money(DealTermCode.GuaranteedCompensation, 650_000m)];

        ReconciliationLine[] lines = DealRules.Reconcile(terms, terms);

        Assert.True(DealRules.IsDraftFaithful(lines));
        Assert.Empty(DealRules.ReconciliationDifferences(lines));
    }

    [Fact]
    public void ComparingNothingToNothing_IsEmpty()
    {
        Assert.Empty(DealRules.Reconcile([], []));
    }

    // ------------------------------------------------------------- properties

    /// <summary>A draft that repeats the offer exactly matches on every line.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Reflexivity_AnIdenticalDraftMatchesThroughout(int seed)
    {
        TermInput[] terms = Generate(seed);

        foreach (ReconciliationLine line in DealRules.Reconcile(terms, terms))
        {
            Assert.Equal(ReconciliationResult.Matched, line.Result);
        }
    }

    /// <summary>
    /// Swapping the sides mirrors the answer: what was missing becomes added, and
    /// the reverse. Anything else would mean the classification depends on which
    /// argument came first.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void Symmetry_SwappingTheSidesMirrorsMissingAndAdded(int leftSeed, int rightSeed)
    {
        TermInput[] left = Generate(leftSeed);
        TermInput[] right = Generate(rightSeed);

        Dictionary<int, ReconciliationLine> forward =
            DealRules.Reconcile(left, right).ToDictionary(x => x.TermCode);
        Dictionary<int, ReconciliationLine> backward =
            DealRules.Reconcile(right, left).ToDictionary(x => x.TermCode);

        Assert.Equal(forward.Keys.Order(), backward.Keys.Order());

        foreach ((int code, ReconciliationLine ahead) in forward)
        {
            Assert.Equal(Mirror(ahead.Result), backward[code].Result);
        }
    }

    /// <summary>Every term on either side appears exactly once.</summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void Totality_NothingIsDroppedFromTheComparison(int leftSeed, int rightSeed)
    {
        TermInput[] left = Generate(leftSeed);
        TermInput[] right = Generate(rightSeed);

        ReconciliationLine[] lines = DealRules.Reconcile(left, right);

        int[] expected = [.. left.Concat(right).Select(x => x.Code).Distinct().Order()];
        int[] actual = [.. lines.Select(x => x.TermCode).Order()];

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// The result always agrees with which sides are populated, so a caller reading
    /// it first can never hit a null.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheResultAlwaysAgreesWithWhichSidesArePresent(int leftSeed, int rightSeed)
    {
        foreach (ReconciliationLine line in DealRules.Reconcile(Generate(leftSeed), Generate(rightSeed)))
        {
            switch (line.Result)
            {
                case ReconciliationResult.AddedInContract:
                    Assert.Null(line.Negotiated);
                    Assert.NotNull(line.Contracted);
                    break;

                case ReconciliationResult.MissingFromContract:
                    Assert.NotNull(line.Negotiated);
                    Assert.Null(line.Contracted);
                    break;

                default:
                    Assert.NotNull(line.Negotiated);
                    Assert.NotNull(line.Contracted);
                    break;
            }
        }
    }

    /// <summary>Only a changed line carries a direction.</summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void OnlyChangedLinesCarryADirection(int leftSeed, int rightSeed)
    {
        foreach (ReconciliationLine line in DealRules.Reconcile(Generate(leftSeed), Generate(rightSeed)))
        {
            if (line.Result != ReconciliationResult.Changed)
            {
                Assert.Equal(ValueDirection.NotComparable, line.Direction);
            }
        }
    }

    public static TheoryData<int> Seeds()
    {
        TheoryData<int> data = [];

        for (int seed = 1; seed <= 16; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    public static TheoryData<int, int> Pairs()
    {
        TheoryData<int, int> data = [];

        for (int left = 1; left <= 10; left++)
        {
            for (int right = 1; right <= 10; right++)
            {
                data.Add(left, right);
            }
        }

        return data;
    }

    /// <summary>A deterministic set of commercial terms for a seed.</summary>
    private static TermInput[] Generate(int seed)
    {
        Random random = new(seed);

        List<TermInput> terms = [];

        foreach (DealTermDefinition definition in DealTermCatalog.All)
        {
            if (random.Next(3) == 0)
            {
                continue;
            }

            TermInput term = definition.ValueKind switch
            {
                TermValueKind.Money => Money(definition.Code, random.Next(1, 20) * 25_000m),
                TermValueKind.Percentage => Percentage(definition.Code, random.Next(0, 20) * 0.5m),
                TermValueKind.Text => Text(definition.Code, $"Position {random.Next(1, 5)}"),
                TermValueKind.Date => new TermInput
                {
                    Code = (int)definition.Code,
                    Kind = (int)TermValueKind.Date,
                    Date = new DateOnly(2027, 1, 1).AddDays(random.Next(0, 300)),
                    Sequence = terms.Count + 1,
                },
                TermValueKind.Count => new TermInput
                {
                    Code = (int)definition.Code,
                    Kind = (int)TermValueKind.Count,
                    Whole = random.Next(1, 25),
                    Unit = definition.AllowedUnits.Count > 0
                        ? (int)definition.AllowedUnits.First()
                        : null,
                    Sequence = terms.Count + 1,
                },
                TermValueKind.Duration => new TermInput
                {
                    Code = (int)definition.Code,
                    Kind = (int)TermValueKind.Duration,
                    Whole = random.Next(1, 25),
                    Unit = (int)definition.AllowedUnits.First(),
                    Sequence = terms.Count + 1,
                },
                _ => Text(definition.Code, "value"),
            };

            term.Sequence = terms.Count + 1;
            terms.Add(term);
        }

        return [.. terms];
    }

    private static ReconciliationResult Mirror(ReconciliationResult result) => result switch
    {
        ReconciliationResult.AddedInContract => ReconciliationResult.MissingFromContract,
        ReconciliationResult.MissingFromContract => ReconciliationResult.AddedInContract,
        _ => result,
    };

    private static TermInput Money(DealTermCode code, decimal amount) => Money((int)code, amount);

    private static TermInput Money(ContractTermCode code, decimal amount) => Money((int)code, amount);

    private static TermInput Money(int code, decimal amount) =>
        new()
        {
            Code = code,
            Kind = (int)TermValueKind.Money,
            Amount = amount,
            Currency = "USD",
            Sequence = 1,
        };

    private static TermInput Percentage(DealTermCode code, decimal value) =>
        Percentage((int)code, value);

    private static TermInput Percentage(ContractTermCode code, decimal value) =>
        Percentage((int)code, value);

    private static TermInput Percentage(int code, decimal value) =>
        new()
        {
            Code = code,
            Kind = (int)TermValueKind.Percentage,
            Number = value,
            Sequence = 1,
        };

    private static TermInput Text(DealTermCode code, string value) => Text((int)code, value);

    private static TermInput Text(ContractTermCode code, string value) => Text((int)code, value);

    private static TermInput Text(int code, string value) =>
        new()
        {
            Code = code,
            Kind = (int)TermValueKind.Text,
            Text = value,
            Sequence = 1,
        };
}

/// <summary>
/// The contract term catalog, checked structurally rather than by review.
/// </summary>
/// <remarks>
/// The commercial half is derived from the deal catalog, so these tests are what
/// prove the derivation holds. Two hand-maintained copies of one vocabulary would
/// drift, and the drift would surface as a reconciliation that quietly stopped
/// matching (ADR-0022).
/// </remarks>
public sealed class ContractTermCatalogTests
{
    /// <summary>
    /// The join key. Every negotiated term has an identically-numbered contract
    /// term, which is what lets reconciliation match on the number alone.
    /// </summary>
    [Fact]
    public void EveryDealTermHasAnIdenticallyValuedContractTerm()
    {
        foreach (DealTermCode deal in Enum.GetValues<DealTermCode>())
        {
            ContractTermCode contract = (ContractTermCode)(int)deal;

            Assert.True(
                Enum.IsDefined(contract),
                $"Deal term '{deal}' ({(int)deal}) has no contract term with the same value.");

            Assert.Equal(deal.ToString(), contract.ToString());
            Assert.Equal(deal, ContractTermCatalog.NegotiatedCounterpart(contract));
        }
    }

    [Fact]
    public void EveryContractTermHasADefinition()
    {
        foreach (ContractTermCode code in Enum.GetValues<ContractTermCode>())
        {
            Assert.True(
                ContractTermCatalog.Find(code) is not null,
                $"Term '{code}' has no catalog definition.");
        }

        Assert.Equal(Enum.GetValues<ContractTermCode>().Length, ContractTermCatalog.All.Count);
    }

    /// <summary>The commercial half agrees with the deal catalog, field by field.</summary>
    [Fact]
    public void TheCommercialHalfMirrorsTheDealCatalog()
    {
        foreach (DealTermDefinition deal in DealTermCatalog.All)
        {
            ContractTermDefinition contract =
                ContractTermCatalog.Require((ContractTermCode)(int)deal.Code);

            Assert.Equal(deal.DisplayName, contract.DisplayName);
            Assert.Equal(deal.ValueKind, contract.ValueKind);
            Assert.Equal(deal.Sensitivity, contract.Sensitivity);
            Assert.True(contract.IsCommercial);
        }

        Assert.Equal(DealTermCatalog.All.Count, ContractTermCatalog.Commercial.Count);
    }

    /// <summary>
    /// The redaction rule depends on this: nothing carrying money or points may be
    /// classified as structure.
    /// </summary>
    [Fact]
    public void EveryMoneyOrPercentageTerm_IsEconomic()
    {
        foreach (ContractTermDefinition definition in ContractTermCatalog.All)
        {
            if (definition.ValueKind is TermValueKind.Money or TermValueKind.Percentage)
            {
                Assert.Equal(TermSensitivity.Economic, definition.Sensitivity);
                Assert.True(ContractTermCatalog.IsEconomic(definition.Code));
            }
        }
    }

    /// <summary>The legal half introduces no economics, so it needs no economic gate.</summary>
    [Fact]
    public void TheLegalHalfCarriesNoFigures()
    {
        foreach (ContractTermDefinition definition in
            ContractTermCatalog.All.Where(x => !x.IsCommercial))
        {
            Assert.Equal(TermValueKind.Text, definition.ValueKind);
            Assert.Equal(TermSensitivity.Structural, definition.Sensitivity);
            Assert.Null(ContractTermCatalog.NegotiatedCounterpart(definition.Code));
        }
    }

    /// <summary>
    /// The vocabulary records what a clause says and administers nothing. Rights,
    /// options, obligations and notices are typed models, not term rows.
    /// </summary>
    [Fact]
    public void TheVocabularyDoesNotAdministerWhatItDescribes()
    {
        string[] names = [.. Enum.GetNames<ContractTermCode>()];

        foreach (string forbidden in new[]
        {
            "OptionExercise", "OptionDeadline", "RightsGrant", "NoticeDeadline",
            "Invoice", "Payment", "Commission",
        })
        {
            Assert.DoesNotContain(forbidden, names);
        }

        Assert.Contains("ExclusivitySummary", names);
        Assert.Contains("OptionPeriodCompensation", names);
    }

    [Fact]
    public void AnUnknownTermIsRefused()
    {
        Assert.Throws<DomainException>(() => ContractTermCatalog.Require((ContractTermCode)12345));
    }
}

/// <summary>Contract versions: transcription, freezing and supersession.</summary>
public sealed class ContractVersionTests
{
    private static readonly OrganizationId Tenant = new(Guid.CreateVersion7());
    private static readonly UserId Actor = new(Guid.CreateVersion7());
    private static readonly ContractId Contract = ContractId.New();
    private static readonly DateTimeOffset Now = new(2027, 4, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewVersion_StartsAsAnEditableDraft()
    {
        ContractVersion version = Draft();

        Assert.Equal(ContractVersionStatus.Draft, version.Status);
        Assert.True(version.IsEditable);
        Assert.Equal(1, version.VersionNumber);
    }

    /// <summary>
    /// AgencyOS holds a reference, not the document. The property says so rather
    /// than leaving the screen to guess.
    /// </summary>
    [Fact]
    public void AgencyOsRecordsAReferenceRatherThanTheDocument()
    {
        Assert.False(ContractVersion.HoldsDocument);

        ContractVersion version = ContractVersion.Start(
            Tenant,
            Contract,
            1,
            "Counterparty draft",
            VersionDirection.Inbound,
            Actor,
            Now,
            externalReference: "DMS-88421",
            sourceSystem: "Counsel document system",
            displayFileName: "undertow-writer-v1.docx",
            mediaType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        Assert.Equal("DMS-88421", version.ExternalReference);
        Assert.Equal("undertow-writer-v1.docx", version.DisplayFileName);
    }

    [Fact]
    public void ADraftsTermsCanBeAddedChangedAndRemoved()
    {
        ContractVersion version = Draft();

        version.AddTerm(
            ContractTermCode.Fee,
            DealTermValue.OfMoney(Money.Create(100_000m, "USD")),
            Now,
            version.Version,
            clauseReference: "4.1");

        version.UpdateTerm(
            ContractTermCode.Fee,
            DealTermValue.OfMoney(Money.Create(140_000m, "USD")),
            Now,
            version.Version,
            clauseReference: "4.1");

        Assert.Equal(140_000m, Assert.Single(version.Terms).AsMoney!.Value.Amount);

        version.RemoveTerm(ContractTermCode.Fee, Now, version.Version);

        Assert.Empty(version.Terms);
    }

    /// <summary>
    /// A recorded version is a milestone somebody relied on. Its extracted terms
    /// are frozen through every editing path.
    /// </summary>
    [Fact]
    public void ARecordedVersionsTermsCannotBeChanged()
    {
        ContractVersion version = Draft();

        version.AddTerm(
            ContractTermCode.Fee,
            DealTermValue.OfMoney(Money.Create(100_000m, "USD")),
            Now,
            version.Version);

        version.Record(Now, version.Version);

        Assert.False(version.IsEditable);

        Assert.Throws<DomainException>(() =>
            version.AddTerm(
                ContractTermCode.Bonus,
                DealTermValue.OfMoney(Money.Create(1m, "USD")),
                Now,
                version.Version));

        Assert.Throws<DomainException>(() =>
            version.UpdateTerm(
                ContractTermCode.Fee,
                DealTermValue.OfMoney(Money.Create(1m, "USD")),
                Now,
                version.Version));

        Assert.Throws<DomainException>(() =>
            version.RemoveTerm(ContractTermCode.Fee, Now, version.Version));

        Assert.Equal(100_000m, Assert.Single(version.Terms).AsMoney!.Value.Amount);
    }

    /// <summary>
    /// A version recorded before anybody read it is ordinary, so empty is allowed -
    /// unlike an offer, which must propose something.
    /// </summary>
    [Fact]
    public void AVersionWithNoExtractedTermsCanStillBeRecorded()
    {
        ContractVersion version = Draft();

        version.Record(Now, version.Version);

        Assert.Equal(ContractVersionStatus.Recorded, version.Status);
        Assert.Empty(version.Terms);
    }

    [Fact]
    public void ATermAppearsAtMostOncePerVersion()
    {
        ContractVersion version = Draft();

        version.AddTerm(
            ContractTermCode.Fee,
            DealTermValue.OfMoney(Money.Create(100_000m, "USD")),
            Now,
            version.Version);

        Assert.Throws<DomainException>(() =>
            version.AddTerm(
                ContractTermCode.Fee,
                DealTermValue.OfMoney(Money.Create(120_000m, "USD")),
                Now,
                version.Version));
    }

    [Fact]
    public void AVersionCannotHaveArrivedInTheFuture()
    {
        Assert.Throws<DomainException>(() =>
            ContractVersion.Start(
                Tenant, Contract, 1, "Future", VersionDirection.Inbound, Actor, Now,
                receivedOn: new DateOnly(2099, 1, 1)));
    }

    [Fact]
    public void AStaleTermChange_IsRefused()
    {
        ContractVersion version = Draft();

        Assert.Throws<ConcurrencyConflictException>(() =>
            version.AddTerm(
                ContractTermCode.Fee,
                DealTermValue.OfMoney(Money.Create(1m, "USD")),
                Now,
                version.Version - 1));
    }

    private static ContractVersion Draft() =>
        ContractVersion.Start(
            Tenant, Contract, 1, "Counterparty draft", VersionDirection.Inbound, Actor, Now);
}
