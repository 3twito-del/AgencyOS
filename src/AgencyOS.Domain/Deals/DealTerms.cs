using AgencyOS.Domain.Common;

namespace AgencyOS.Domain.Deals;

/// <summary>
/// The shape a term's value takes.
/// </summary>
/// <remarks>
/// The values match the kinds the F# rules kernel parses. A test walks both
/// vocabularies so a kind added on either side fails the build rather than
/// becoming a term nothing can validate (ADR-0021).
/// </remarks>
public enum TermValueKind
{
    /// <summary>An amount and its currency. Never a bare number.</summary>
    Money = 1,

    /// <summary>A percentage, such as backend points.</summary>
    Percentage = 2,

    /// <summary>A whole number.</summary>
    Integer = 3,

    /// <summary>A fractional number that is not a percentage.</summary>
    Decimal = 4,

    /// <summary>Free text, for a term whose value is genuinely prose.</summary>
    Text = 5,

    /// <summary>A yes or no.</summary>
    Boolean = 6,

    /// <summary>A business date.</summary>
    Date = 7,

    /// <summary>A length of time in a stated unit.</summary>
    Duration = 8,

    /// <summary>A number of something, optionally in a stated unit.</summary>
    Count = 9,
}

/// <summary>What a duration or count is measured in.</summary>
public enum TermUnit
{
    Day = 1,
    Week = 2,
    Month = 3,
    Year = 4,

    /// <summary>Episodes of a series.</summary>
    Episode = 5,

    /// <summary>Seasons of a series.</summary>
    Season = 6,

    /// <summary>Delivered drafts of a script.</summary>
    Draft = 7,

    /// <summary>Contracted writing steps.</summary>
    Step = 8,
}

/// <summary>
/// How sensitive a term's value is.
/// </summary>
/// <remarks>
/// The two classes exist because they have genuinely different readerships. A
/// coordinator scheduling around a start date has no business seeing the fee, and
/// refusing them both would make the pipeline unusable for the work they actually
/// do (ADR-0021).
/// </remarks>
public enum TermSensitivity
{
    /// <summary>
    /// Structure rather than money: dates, counts, billing position, prose.
    /// Visible to anyone who may read the deal.
    /// </summary>
    Structural = 1,

    /// <summary>
    /// What somebody is paid, in money or in points. Requires
    /// <c>deals.economics.read</c>.
    /// </summary>
    Economic = 2,
}

/// <summary>
/// The controlled vocabulary of negotiable commercial terms.
/// </summary>
/// <remarks>
/// <para>
/// A closed enum, not free-form strings. Terms are compared, filtered and later
/// reconciled against a contract, and none of that survives two people writing
/// "Guaranteed Comp" and "guaranteed compensation" for the same thing.
/// </para>
/// <para>
/// The vocabulary stops where administration begins.
/// <see cref="OptionPeriodCompensation"/> records what an option period was
/// proposed to pay, because that is a negotiated number. It says nothing about
/// when the option must be exercised, by whom, or on what notice - those are
/// obligations, and they are M8's (ADR-0021).
/// </para>
/// </remarks>
public enum DealTermCode
{
    // ---- Compensation ----

    /// <summary>The floor: what is paid regardless of what else happens.</summary>
    GuaranteedCompensation = 101,

    /// <summary>A single engagement fee.</summary>
    Fee = 102,

    /// <summary>Periodic salary for an ongoing engagement.</summary>
    Salary = 103,

    /// <summary>Rate per week of service.</summary>
    WeeklyRate = 104,

    /// <summary>Rate per episode.</summary>
    EpisodicRate = 105,

    /// <summary>Rate per day of service.</summary>
    DayRate = 106,

    /// <summary>Paid on signature of the eventual agreement.</summary>
    SigningPayment = 107,

    /// <summary>Compensation payable later, on a negotiated basis.</summary>
    DeferredCompensation = 108,

    /// <summary>A negotiated bonus.</summary>
    Bonus = 109,

    /// <summary>A buyout of rights or of further payments.</summary>
    Buyout = 110,

    /// <summary>
    /// What an option period was proposed to pay. Economics only: exercise,
    /// deadlines and notice are M8.
    /// </summary>
    OptionPeriodCompensation = 111,

    /// <summary>A negotiated expense allowance.</summary>
    ExpenseAllowance = 112,

    /// <summary>Travel and accommodation provision, as negotiated.</summary>
    TravelAndAccommodation = 113,

    // ---- Volume and term ----

    /// <summary>How many episodes the engagement covers.</summary>
    EpisodeCount = 201,

    /// <summary>How many units of service are contracted.</summary>
    ServicesCount = 202,

    /// <summary>How long the engagement runs.</summary>
    TermLength = 203,

    /// <summary>When services start.</summary>
    StartDate = 204,

    /// <summary>When services end.</summary>
    EndDate = 205,

    // ---- Backend ----

    /// <summary>Backend points, on a basis stated in the notes.</summary>
    BackendPercentage = 301,

    /// <summary>Participation in gross.</summary>
    GrossParticipation = 302,

    /// <summary>Participation in net.</summary>
    NetParticipation = 303,

    // ---- Credit ----

    /// <summary>The billing the credit carries, as negotiated.</summary>
    CreditBilling = 401,

    /// <summary>Where the credit appears.</summary>
    CreditPlacement = 402,

    // ---- Anything else ----

    /// <summary>
    /// A negotiated term this vocabulary does not yet name, carried as text with
    /// a label. The pressure valve, deliberately the only one.
    /// </summary>
    OtherTerm = 901,
}

/// <summary>
/// What a term code means and what a valid value for it looks like.
/// </summary>
/// <param name="Code">The term.</param>
/// <param name="DisplayName">What a person calls it.</param>
/// <param name="ValueKind">The one shape its value may take.</param>
/// <param name="Sensitivity">Who may read its value.</param>
/// <param name="AllowedUnits">
/// Units the value may be measured in. Empty when the kind has no unit.
/// </param>
/// <param name="ApplicableKinds">
/// The deal kinds it means something for, or null when it means something for all
/// of them.
/// </param>
/// <param name="Minimum">Lower bound, when the term has one.</param>
/// <param name="Maximum">Upper bound, when the term has one.</param>
public sealed record DealTermDefinition(
    DealTermCode Code,
    string DisplayName,
    TermValueKind ValueKind,
    TermSensitivity Sensitivity,
    IReadOnlySet<TermUnit> AllowedUnits,
    IReadOnlySet<DealKind>? ApplicableKinds = null,
    decimal? Minimum = null,
    decimal? Maximum = null)
{
    /// <summary>Whether this term means anything for a deal of the given kind.</summary>
    public bool AppliesTo(DealKind kind) => ApplicableKinds is null || ApplicableKinds.Contains(kind);
}

/// <summary>
/// The single authoritative description of every supported term.
/// </summary>
/// <remarks>
/// <para>
/// One table, consulted by validation, display, redaction and comparison alike.
/// The alternative is a switch statement per concern, which is how M5's saved-view
/// filters lost seven fields: two parallel shapes that depend on a person
/// remembering every entry will eventually disagree, and the disagreement is
/// silent.
/// </para>
/// <para>
/// Completeness is tested structurally rather than reviewed: every code has a
/// definition, every definition names a real code, every value kind the table uses
/// is one the rules kernel can parse, and every money or percentage term is
/// classified economic. A term added without a definition fails the build.
/// </para>
/// </remarks>
public static class DealTermCatalog
{
    private static readonly IReadOnlySet<TermUnit> NoUnits = new HashSet<TermUnit>();

    /// <summary>
    /// Episodic terms mean nothing for an outright sale or a partnership.
    /// </summary>
    /// <remarks>
    /// The only applicability constraint in the table, and deliberately so. A
    /// catalog that refused half the vocabulary on every deal would teach people
    /// to pick whichever kind let their term through.
    /// </remarks>
    private static readonly IReadOnlySet<DealKind> EpisodicKinds = new HashSet<DealKind>
    {
        DealKind.TalentEmployment,
        DealKind.Writing,
        DealKind.Directing,
        DealKind.Producing,
        DealKind.Services,
        DealKind.ProjectLicense,
        DealKind.Package,
        DealKind.Other,
    };

    private static readonly IReadOnlyDictionary<DealTermCode, DealTermDefinition> Definitions =
        new[]
        {
            Money(DealTermCode.GuaranteedCompensation, "Guaranteed compensation"),
            Money(DealTermCode.Fee, "Fee"),
            Money(DealTermCode.Salary, "Salary"),
            Money(DealTermCode.WeeklyRate, "Weekly rate"),
            Money(DealTermCode.EpisodicRate, "Episodic rate", EpisodicKinds),
            Money(DealTermCode.DayRate, "Day rate"),
            Money(DealTermCode.SigningPayment, "Signing payment"),
            Money(DealTermCode.DeferredCompensation, "Deferred compensation"),
            Money(DealTermCode.Bonus, "Bonus"),
            Money(DealTermCode.Buyout, "Buyout"),
            Money(DealTermCode.OptionPeriodCompensation, "Option period compensation"),
            Money(DealTermCode.ExpenseAllowance, "Expense allowance"),
            Money(DealTermCode.TravelAndAccommodation, "Travel and accommodation"),

            new DealTermDefinition(
                DealTermCode.EpisodeCount,
                "Episodes",
                TermValueKind.Count,
                TermSensitivity.Structural,
                Units(TermUnit.Episode),
                EpisodicKinds,
                Minimum: 1),

            new DealTermDefinition(
                DealTermCode.ServicesCount,
                "Units of service",
                TermValueKind.Count,
                TermSensitivity.Structural,
                Units(TermUnit.Day, TermUnit.Week, TermUnit.Draft, TermUnit.Step, TermUnit.Episode),
                Minimum: 1),

            new DealTermDefinition(
                DealTermCode.TermLength,
                "Term length",
                TermValueKind.Duration,
                TermSensitivity.Structural,
                Units(TermUnit.Day, TermUnit.Week, TermUnit.Month, TermUnit.Year, TermUnit.Season),
                Minimum: 1),

            Date(DealTermCode.StartDate, "Start date"),
            Date(DealTermCode.EndDate, "End date"),

            // Points are bounded to 100 here even though the kind admits more:
            // a share of something cannot exceed the whole of it, and a
            // transposed digit in a backend term is expensive.
            Percentage(DealTermCode.BackendPercentage, "Backend points"),
            Percentage(DealTermCode.GrossParticipation, "Gross participation"),
            Percentage(DealTermCode.NetParticipation, "Net participation"),

            Text(DealTermCode.CreditBilling, "Billing"),
            Text(DealTermCode.CreditPlacement, "Credit placement"),
            Text(DealTermCode.OtherTerm, "Other negotiated term"),
        }.ToDictionary(definition => definition.Code);

    /// <summary>Every supported term, in vocabulary order.</summary>
    public static IReadOnlyCollection<DealTermDefinition> All { get; } =
        [.. Definitions.Values.OrderBy(definition => definition.Code)];

    /// <summary>The definition for a term, or null when this build has none.</summary>
    public static DealTermDefinition? Find(DealTermCode code) =>
        Definitions.TryGetValue(code, out DealTermDefinition? definition) ? definition : null;

    /// <summary>The definition for a term, refusing one this build does not know.</summary>
    public static DealTermDefinition Require(DealTermCode code) =>
        Find(code) ?? throw new DomainException($"Term '{code}' is not one this build supports.");

    /// <summary>The terms that mean something for a deal of this kind.</summary>
    public static IReadOnlyCollection<DealTermDefinition> For(DealKind kind) =>
        [.. All.Where(definition => definition.AppliesTo(kind))];

    /// <summary>Whether a term's value may only be read with <c>deals.economics.read</c>.</summary>
    public static bool IsEconomic(DealTermCode code) =>
        Find(code)?.Sensitivity == TermSensitivity.Economic;

    private static DealTermDefinition Money(
        DealTermCode code,
        string displayName,
        IReadOnlySet<DealKind>? kinds = null) =>
        new(code, displayName, TermValueKind.Money, TermSensitivity.Economic, NoUnits, kinds);

    private static DealTermDefinition Percentage(DealTermCode code, string displayName) =>
        new(
            code,
            displayName,
            TermValueKind.Percentage,
            TermSensitivity.Economic,
            NoUnits,
            Minimum: 0m,
            Maximum: 100m);

    private static DealTermDefinition Date(DealTermCode code, string displayName) =>
        new(code, displayName, TermValueKind.Date, TermSensitivity.Structural, NoUnits);

    private static DealTermDefinition Text(DealTermCode code, string displayName) =>
        new(code, displayName, TermValueKind.Text, TermSensitivity.Structural, NoUnits);

    private static IReadOnlySet<TermUnit> Units(params TermUnit[] units) => new HashSet<TermUnit>(units);
}
