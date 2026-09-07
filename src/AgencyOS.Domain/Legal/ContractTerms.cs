using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Legal;

/// <summary>
/// The controlled vocabulary of terms a contract can record.
/// </summary>
/// <remarks>
/// <para>
/// The commercial members carry <strong>the same integer values as
/// <see cref="DealTermCode"/></strong>, so reconciliation joins a negotiated term
/// to a drafted one on the number rather than on a mapping table somebody has to
/// maintain. A test asserts every deal term has an identically-valued contract
/// term, which is what stops the two drifting.
/// </para>
/// <para>
/// The legal members exist because a contract says things an offer never did.
/// They are deliberately few: anything the system must later <em>act</em> on -
/// rights, options, obligations, notices - is a typed model of its own rather than
/// a term row, because a deadline buried in a text term is a deadline nothing can
/// surface (ADR-0022).
/// </para>
/// </remarks>
public enum ContractTermCode
{
    // ---- Commercial. Values match DealTermCode exactly. ----

    GuaranteedCompensation = 101,
    Fee = 102,
    Salary = 103,
    WeeklyRate = 104,
    EpisodicRate = 105,
    DayRate = 106,
    SigningPayment = 107,
    DeferredCompensation = 108,
    Bonus = 109,
    Buyout = 110,
    OptionPeriodCompensation = 111,
    ExpenseAllowance = 112,
    TravelAndAccommodation = 113,

    EpisodeCount = 201,
    ServicesCount = 202,
    TermLength = 203,
    StartDate = 204,
    EndDate = 205,

    BackendPercentage = 301,
    GrossParticipation = 302,
    NetParticipation = 303,

    CreditBilling = 401,
    CreditPlacement = 402,

    OtherTerm = 901,

    // ---- Legal. Introduced by the drafting, with no negotiated counterpart. ----

    /// <summary>What the exclusivity clause says, as a summary. The grant itself is a RightsGrant.</summary>
    ExclusivitySummary = 1001,

    /// <summary>Whose approval the contract requires, and over what.</summary>
    ApprovalRights = 1002,

    /// <summary>The circumstances in which a party may terminate.</summary>
    TerminationRights = 1003,

    /// <summary>What is to be delivered, as the clause describes it.</summary>
    DeliveryDescription = 1004,

    /// <summary>What the credit clause requires, beyond the negotiated billing.</summary>
    CreditObligationSummary = 1005,

    /// <summary>What law governs and where disputes are heard.</summary>
    GoverningLaw = 1006,

    /// <summary>What the confidentiality clause requires.</summary>
    ConfidentialitySummary = 1007,

    /// <summary>A negotiated legal term this vocabulary does not yet name.</summary>
    OtherLegalTerm = 1901,
}

/// <summary>
/// What a contract term code means and what a valid value for it looks like.
/// </summary>
/// <param name="Code">The term.</param>
/// <param name="DisplayName">What a person calls it.</param>
/// <param name="ValueKind">The one shape its value may take.</param>
/// <param name="Sensitivity">Whether reading its value needs economics access.</param>
/// <param name="IsCommercial">Whether it has a negotiated counterpart to reconcile against.</param>
public sealed record ContractTermDefinition(
    ContractTermCode Code,
    string DisplayName,
    TermValueKind ValueKind,
    TermSensitivity Sensitivity,
    bool IsCommercial);

/// <summary>
/// The single authoritative description of every term a contract can record.
/// </summary>
/// <remarks>
/// The commercial half is derived from <see cref="DealTermCatalog"/> rather than
/// retyped, so a change to a deal term's name, kind or sensitivity reaches the
/// contract vocabulary automatically. That is the whole point: two hand-maintained
/// copies of the same vocabulary would disagree, and the disagreement would show
/// up as a reconciliation that quietly stopped matching (ADR-0022).
/// </remarks>
public static class ContractTermCatalog
{
    private static readonly IReadOnlyDictionary<ContractTermCode, ContractTermDefinition> Definitions =
        Build();

    /// <summary>Every supported term, in vocabulary order.</summary>
    public static IReadOnlyCollection<ContractTermDefinition> All { get; } =
        [.. Definitions.Values.OrderBy(definition => definition.Code)];

    /// <summary>The terms that have a negotiated counterpart to reconcile against.</summary>
    public static IReadOnlyCollection<ContractTermDefinition> Commercial { get; } =
        [.. Definitions.Values.Where(definition => definition.IsCommercial).OrderBy(x => x.Code)];

    /// <summary>The definition for a term, or null when this build has none.</summary>
    public static ContractTermDefinition? Find(ContractTermCode code) =>
        Definitions.TryGetValue(code, out ContractTermDefinition? definition) ? definition : null;

    /// <summary>The definition for a term, refusing one this build does not know.</summary>
    public static ContractTermDefinition Require(ContractTermCode code) =>
        Find(code) ?? throw new DomainException($"Term '{code}' is not one this build supports.");

    /// <summary>Whether reading this term's value requires <c>deals.economics.read</c>.</summary>
    public static bool IsEconomic(ContractTermCode code) =>
        Find(code)?.Sensitivity == TermSensitivity.Economic;

    /// <summary>The negotiated term this one reconciles against, when it has one.</summary>
    /// <remarks>
    /// The codes share their integer values deliberately, so this is a cast rather
    /// than a lookup - and a test proves the correspondence holds for every deal
    /// term.
    /// </remarks>
    public static DealTermCode? NegotiatedCounterpart(ContractTermCode code) =>
        Enum.IsDefined((DealTermCode)(int)code) ? (DealTermCode)(int)code : null;

    private static Dictionary<ContractTermCode, ContractTermDefinition> Build()
    {
        Dictionary<ContractTermCode, ContractTermDefinition> definitions = [];

        // The commercial half, taken from the deal catalog so the two cannot drift.
        foreach (DealTermDefinition deal in DealTermCatalog.All)
        {
            ContractTermCode code = (ContractTermCode)(int)deal.Code;

            definitions[code] = new ContractTermDefinition(
                code, deal.DisplayName, deal.ValueKind, deal.Sensitivity, IsCommercial: true);
        }

        // The legal half. All text: these summarise what a clause says, and the
        // structured legal consequences live in their own models.
        foreach ((ContractTermCode code, string name) in new[]
        {
            (ContractTermCode.ExclusivitySummary, "Exclusivity"),
            (ContractTermCode.ApprovalRights, "Approval rights"),
            (ContractTermCode.TerminationRights, "Termination rights"),
            (ContractTermCode.DeliveryDescription, "Delivery"),
            (ContractTermCode.CreditObligationSummary, "Credit obligation"),
            (ContractTermCode.GoverningLaw, "Governing law"),
            (ContractTermCode.ConfidentialitySummary, "Confidentiality"),
            (ContractTermCode.OtherLegalTerm, "Other legal term"),
        })
        {
            definitions[code] = new ContractTermDefinition(
                code, name, TermValueKind.Text, TermSensitivity.Structural, IsCommercial: false);
        }

        return definitions;
    }
}

/// <summary>
/// One term recorded from a drafting version.
/// </summary>
/// <remarks>
/// <para>
/// Structurally identical to <see cref="OfferTerm"/>, and deliberately so: the
/// same value kinds, the same typed columns, the same money semantics. A second
/// money system would have been two places for rounding to differ, and
/// reconciliation would have had to translate between them before it could compare
/// anything (ADR-0022).
/// </para>
/// <para>
/// Immutable once its version is recorded. The domain refuses changes and
/// PostgreSQL refuses them independently.
/// </para>
/// </remarks>
public sealed class ContractTerm
{
    private ContractTerm()
    {
    }

    public ContractTermId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public ContractVersionId ContractVersionId { get; private set; }

    public ContractTermCode Code { get; private set; }

    public TermValueKind ValueKind { get; private set; }

    /// <summary>Money amount. Never a floating-point type.</summary>
    public decimal? AmountValue { get; private set; }

    /// <summary>The currency the amount is in. Set exactly when the amount is.</summary>
    public string? CurrencyCodeValue { get; private set; }

    public decimal? NumericValue { get; private set; }

    public long? IntegerValue { get; private set; }

    public string? TextValue { get; private set; }

    public bool? BooleanValue { get; private set; }

    public DateOnly? DateValue { get; private set; }

    public TermUnit? Unit { get; private set; }

    public int Sequence { get; private set; }

    /// <summary>Where in the document this came from: a clause or section number.</summary>
    public string? ClauseReference { get; private set; }

    public string? Label { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>How sensitive this particular term is. Assigned, never inferred.</summary>
    public PrivilegeClass Privilege { get; private set; }

    /// <summary>The money this term carries, when it carries money.</summary>
    public Money? AsMoney =>
        ValueKind == TermValueKind.Money && AmountValue is { } amount && CurrencyCodeValue is { } currency
            ? Money.Create(amount, currency)
            : null;

    /// <summary>Whether the value requires <c>deals.economics.read</c>.</summary>
    public bool IsEconomic => ContractTermCatalog.IsEconomic(Code);

    /// <summary>Whether the term requires <c>contracts.privileged.read</c>.</summary>
    public bool IsPrivileged =>
        Privilege is PrivilegeClass.LegalStrategy or PrivilegeClass.AttorneyClientPrivileged;

    internal static ContractTerm Create(
        OrganizationId organizationId,
        ContractVersionId versionId,
        ContractTermCode code,
        DealTermValue value,
        int sequence,
        string? clauseReference = null,
        string? label = null,
        string? notes = null,
        PrivilegeClass privilege = PrivilegeClass.Ordinary)
    {
        ArgumentNullException.ThrowIfNull(value);

        ContractTermDefinition definition = ContractTermCatalog.Require(code);

        if (!Enum.IsDefined(privilege))
        {
            throw new DomainException($"Unknown privilege class '{privilege}'.");
        }

        if (value.Kind != definition.ValueKind)
        {
            throw new DomainException(
                $"Term '{definition.DisplayName}' is a {definition.ValueKind} term; "
                + $"a {value.Kind} value was supplied.");
        }

        // Currency membership is reference data and stays on this side, exactly as
        // it does for offer terms; the rules kernel checks only the shape.
        string? currency = value.Currency is null ? null : CurrencyCode.Parse(value.Currency).Value;

        ContractTerm term = new()
        {
            Id = ContractTermId.New(),
            OrganizationId = organizationId,
            ContractVersionId = versionId,
            Code = code,
            ValueKind = value.Kind,
            AmountValue = value.Amount,
            CurrencyCodeValue = currency,
            NumericValue = value.Number,
            IntegerValue = value.Whole,
            TextValue = value.Text?.Trim(),
            BooleanValue = value.Flag,
            DateValue = value.Date,
            Unit = value.Unit,
            Sequence = sequence,
            ClauseReference = Ensure.OptionalMax(clauseReference, nameof(clauseReference), 100),
            Label = Ensure.OptionalMax(label, nameof(label), 200),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 2000),
            Privilege = privilege,
        };

        if (DealRules.DescribeTermProblem(term.ToRulesInput()) is { } problem)
        {
            throw new DomainException(problem);
        }

        if (term.ValueKind == TermValueKind.Money && term.AmountValue is { } amount)
        {
            term.AmountValue = Money.Create(amount, currency!).Amount;
        }

        return term;
    }

    /// <summary>The flat shape the rules kernel reconciles.</summary>
    public TermInput ToRulesInput() =>
        new()
        {
            Code = (int)Code,
            Kind = (int)ValueKind,
            Amount = AmountValue,
            Currency = CurrencyCodeValue,
            Number = NumericValue,
            Whole = IntegerValue,
            Text = TextValue,
            Flag = BooleanValue,
            Date = DateValue,
            Unit = Unit is { } unit ? (int)unit : null,
            Sequence = Sequence,
        };
}
