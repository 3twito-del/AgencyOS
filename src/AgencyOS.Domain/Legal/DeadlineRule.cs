using AgencyOS.Deals.Rules;
using AgencyOS.Domain.Common;

namespace AgencyOS.Domain.Legal;

/// <summary>What a relative deadline is measured from.</summary>
/// <remarks>Values match the F# rules kernel's own codes.</remarks>
public enum DeadlineAnchor
{
    OnExecution = 1,
    OnEffective = 2,
    OnDelivery = 3,
    OnNotice = 4,
    OnCommencement = 5,
    OnFirstRelease = 6,
    OnOptionWindowOpen = 7,

    /// <summary>Something this vocabulary does not name. The description carries it.</summary>
    Other = 99,
}

/// <summary>The unit a relative offset is counted in.</summary>
public enum DeadlineOffsetUnit
{
    Days = 1,
    Weeks = 2,
    Months = 3,
    Years = 4,
}

/// <summary>
/// How days are counted.
/// </summary>
/// <remarks>
/// Business days are accepted and stored, and deliberately not computed. AgencyOS
/// holds no holiday calendar, so a business-day rule keeps its stated intent and
/// resolves to nothing until one exists. Counting them as calendar days would
/// produce a legal deadline that looks authoritative and is wrong (ADR-0022).
/// </remarks>
public enum DeadlineCalendarBasis
{
    CalendarDays = 1,
    BusinessDays = 2,
}

/// <summary>How a deadline is expressed.</summary>
public enum DeadlineRuleKind
{
    /// <summary>A stated calendar date.</summary>
    Absolute = 1,

    /// <summary>An offset from an event.</summary>
    Relative = 2,

    /// <summary>The contract says something this build cannot structure.</summary>
    Unstructured = 3,
}

/// <summary>
/// When something is due, as the contract expresses it.
/// </summary>
/// <remarks>
/// <para>
/// Three cases because contracts genuinely say three different things: a date, a
/// rule measured from an event, or words. "Within 30 days after delivery" is not a
/// date until delivery happens, and turning it into one the moment somebody types
/// it would invent a day nobody agreed to (ADR-0022).
/// </para>
/// <para>
/// <see cref="Description"/> always carries the clause's own wording, so an
/// unstructured rule still tells a reader what the contract requires even when
/// nothing can be computed from it.
/// </para>
/// </remarks>
/// <param name="Kind">How the deadline is expressed.</param>
/// <param name="On">The stated date, for an absolute rule.</param>
/// <param name="Anchor">The event a relative rule is measured from.</param>
/// <param name="Offset">How many units.</param>
/// <param name="Unit">What the offset counts.</param>
/// <param name="Before">Whether the offset runs backwards from the anchor.</param>
/// <param name="Basis">Whether days are calendar or business days.</param>
/// <param name="Description">The clause's own wording.</param>
public sealed record DeadlineRule(
    DeadlineRuleKind Kind,
    DateOnly? On = null,
    DeadlineAnchor? Anchor = null,
    int? Offset = null,
    DeadlineOffsetUnit? Unit = null,
    bool Before = false,
    DeadlineCalendarBasis Basis = DeadlineCalendarBasis.CalendarDays,
    string? Description = null)
{
    /// <summary>A deadline the contract states as a date.</summary>
    public static DeadlineRule On_(DateOnly date, string? description = null) =>
        new(DeadlineRuleKind.Absolute, On: date, Description: description);

    /// <summary>A deadline measured from an event.</summary>
    public static DeadlineRule After(
        int offset,
        DeadlineOffsetUnit unit,
        DeadlineAnchor anchor,
        DeadlineCalendarBasis basis = DeadlineCalendarBasis.CalendarDays,
        string? description = null) =>
        new(DeadlineRuleKind.Relative, Anchor: anchor, Offset: offset, Unit: unit,
            Before: false, Basis: basis, Description: description);

    /// <summary>A deadline measured backwards from an event.</summary>
    public static DeadlineRule Before_(
        int offset,
        DeadlineOffsetUnit unit,
        DeadlineAnchor anchor,
        DeadlineCalendarBasis basis = DeadlineCalendarBasis.CalendarDays,
        string? description = null) =>
        new(DeadlineRuleKind.Relative, Anchor: anchor, Offset: offset, Unit: unit,
            Before: true, Basis: basis, Description: description);

    /// <summary>A rule the contract states in words this build cannot structure.</summary>
    public static DeadlineRule AsWritten(string description) =>
        new(DeadlineRuleKind.Unstructured, Description: description);

    /// <summary>Checks the rule is one the kernel can read, and refuses it otherwise.</summary>
    public DeadlineRule Validated()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new DomainException($"Unknown deadline rule kind '{Kind}'.");
        }

        if (Kind == DeadlineRuleKind.Unstructured && string.IsNullOrWhiteSpace(Description))
        {
            throw new DomainException(
                "A deadline the contract states in words must carry those words, "
                + "or nothing records what is actually required.");
        }

        if (!DealRules.IsDeadlineRuleValid(ToRulesInput()))
        {
            throw new DomainException(
                Kind == DeadlineRuleKind.Absolute
                    ? "A deadline stated as a date must carry one."
                    : "A deadline measured from an event must state the event, the offset and the unit.");
        }

        return this;
    }

    /// <summary>The flat shape the rules kernel resolves.</summary>
    public DeadlineRuleInput ToRulesInput() =>
        new()
        {
            Kind = (int)Kind,
            On = On,
            Anchor = Anchor is { } anchor ? (int)anchor : null,
            Offset = Offset,
            Unit = Unit is { } unit ? (int)unit : null,
            Before = Before,
            Basis = (int)Basis,
        };

    /// <summary>
    /// The date this rule falls on, or null when it cannot be worked out.
    /// </summary>
    /// <remarks>
    /// Null three ways, each honest: the contract states no structured rule, the
    /// event it is measured from has not happened, or it counts business days and
    /// AgencyOS has no calendar for them. <see cref="WhyUnresolved"/> says which.
    /// </remarks>
    public DateOnly? Resolve(DateOnly? anchorDate) =>
        DealRules.ResolveDeadline(ToRulesInput(), anchorDate);

    /// <summary>Why the rule could not be resolved, or null when it was.</summary>
    public string? WhyUnresolved(DateOnly? anchorDate) =>
        DealRules.DescribeDeadlineProblem(ToRulesInput(), anchorDate);

    /// <summary>Whether the rule needs an event date before it can be resolved.</summary>
    public bool NeedsAnchorDate => Kind == DeadlineRuleKind.Relative;
}
