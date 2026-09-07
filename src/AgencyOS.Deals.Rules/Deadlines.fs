namespace AgencyOS.Deals.Rules

open System

/// <summary>What a relative deadline is measured from.</summary>
/// <remarks>
/// A small controlled set, not a temporal language. Each value names an event the
/// contract can point at and AgencyOS can later learn the date of; anything else
/// is <see cref="OtherAnchor"/> with the clause's own words preserved beside it.
/// </remarks>
type AnchorEvent =
    /// The date the contract was fully executed.
    | OnExecution
    /// The date the contract became effective.
    | OnEffective
    /// Delivery of the material the clause names.
    | OnDelivery
    /// A notice having been given.
    | OnNotice
    /// Services commencing.
    | OnCommencement
    /// First broadcast or release.
    | OnFirstRelease
    /// An option's exercise window opening.
    | OnOptionWindowOpen
    /// Something this vocabulary does not name. The clause text carries the rest.
    | OtherAnchor

/// <summary>The unit a relative offset is counted in.</summary>
type OffsetUnit =
    | Days
    | Weeks
    | Months
    | Years

/// <summary>
/// How days are counted.
/// </summary>
/// <remarks>
/// Business days are accepted and stored, and deliberately <em>not</em> computed.
/// AgencyOS has no holiday calendar, and counting them as calendar days would
/// produce a deadline that looks authoritative and is wrong. The rule keeps its
/// stated intent and resolves to nothing until a calendar policy exists
/// (ADR-0022).
/// </remarks>
type CalendarBasis =
    | CalendarDays
    | BusinessDays

/// <summary>
/// When something is due.
/// </summary>
/// <remarks>
/// Three cases, because contracts genuinely say three different things: a date, a
/// rule measured from an event, or nothing structured at all. Modelling the second
/// as a date the moment it is entered would invent a day nobody agreed to.
/// </remarks>
type DeadlineRule =
    /// A stated calendar date.
    | AbsoluteDeadline of on: DateOnly
    /// An offset from an event, in one direction.
    | RelativeDeadline of
        anchor: AnchorEvent *
        offset: int *
        unit: OffsetUnit *
        before: bool *
        basis: CalendarBasis
    /// The contract states a rule this build cannot structure. Text only.
    | UnstructuredDeadline

/// <summary>Why a deadline could not be resolved to a date.</summary>
type DeadlineUnresolved =
    /// The rule is not structured, so there is nothing to compute.
    | NoStructuredRule
    /// The event the rule is measured from has not happened, or its date is unknown.
    | AnchorDateUnknown of anchor: AnchorEvent
    /// The rule counts business days, which this build will not approximate.
    | NoBusinessDayCalendar

/// <summary>A deadline rule as it crosses the language boundary.</summary>
[<CLIMutable>]
type DeadlineRuleInput =
    { /// Which <see cref="DeadlineRule"/> case this row claims to be.
      Kind: int
      /// The stated date, for an absolute rule.
      On: Nullable<DateOnly>
      /// Persisted <see cref="AnchorEvent"/> value.
      Anchor: Nullable<int>
      /// How many units.
      Offset: Nullable<int>
      /// Persisted <see cref="OffsetUnit"/> value.
      Unit: Nullable<int>
      /// True when the offset runs backwards from the anchor.
      Before: Nullable<bool>
      /// Persisted <see cref="CalendarBasis"/> value.
      Basis: Nullable<int> }

/// <summary>Anchor event mapping.</summary>
module AnchorEvent =

    let all =
        [ OnExecution
          OnEffective
          OnDelivery
          OnNotice
          OnCommencement
          OnFirstRelease
          OnOptionWindowOpen
          OtherAnchor ]

    let code anchor =
        match anchor with
        | OnExecution -> 1
        | OnEffective -> 2
        | OnDelivery -> 3
        | OnNotice -> 4
        | OnCommencement -> 5
        | OnFirstRelease -> 6
        | OnOptionWindowOpen -> 7
        | OtherAnchor -> 99

    let ofCode value =
        match value with
        | 1 -> Some OnExecution
        | 2 -> Some OnEffective
        | 3 -> Some OnDelivery
        | 4 -> Some OnNotice
        | 5 -> Some OnCommencement
        | 6 -> Some OnFirstRelease
        | 7 -> Some OnOptionWindowOpen
        | 99 -> Some OtherAnchor
        | _ -> None

/// <summary>Offset unit mapping.</summary>
module OffsetUnit =

    let all = [ Days; Weeks; Months; Years ]

    let code unit =
        match unit with
        | Days -> 1
        | Weeks -> 2
        | Months -> 3
        | Years -> 4

    let ofCode value =
        match value with
        | 1 -> Some Days
        | 2 -> Some Weeks
        | 3 -> Some Months
        | 4 -> Some Years
        | _ -> None

/// <summary>Calendar basis mapping.</summary>
module CalendarBasis =

    let all = [ CalendarDays; BusinessDays ]

    let code basis =
        match basis with
        | CalendarDays -> 1
        | BusinessDays -> 2

    let ofCode value =
        match value with
        | 1 -> Some CalendarDays
        | 2 -> Some BusinessDays
        | _ -> None

    /// Whether this build can compute a date on this basis.
    let isComputable basis =
        match basis with
        | CalendarDays -> true
        | BusinessDays -> false

/// <summary>Deadline rule parsing and resolution.</summary>
module DeadlineRule =

    [<Literal>]
    let AbsoluteKind = 1

    [<Literal>]
    let RelativeKind = 2

    [<Literal>]
    let UnstructuredKind = 3

    let allKinds = [ AbsoluteKind; RelativeKind; UnstructuredKind ]

    let kindOf rule =
        match rule with
        | AbsoluteDeadline _ -> AbsoluteKind
        | RelativeDeadline _ -> RelativeKind
        | UnstructuredDeadline -> UnstructuredKind

    /// <summary>Turns a flat row into a rule, or returns None when it is not one.</summary>
    let parse (input: DeadlineRuleInput) =
        match input.Kind with
        | AbsoluteKind -> if input.On.HasValue then Some(AbsoluteDeadline input.On.Value) else None

        | RelativeKind ->
            match
                (if input.Anchor.HasValue then AnchorEvent.ofCode input.Anchor.Value else None),
                (if input.Unit.HasValue then OffsetUnit.ofCode input.Unit.Value else None),
                (if input.Basis.HasValue then CalendarBasis.ofCode input.Basis.Value else None)
            with
            | Some anchor, Some unit, Some basis when input.Offset.HasValue ->
                Some(
                    RelativeDeadline(
                        anchor,
                        input.Offset.Value,
                        unit,
                        (input.Before.HasValue && input.Before.Value),
                        basis))
            | _ -> None

        | UnstructuredKind -> Some UnstructuredDeadline

        | _ -> None

    let isValid input = (parse input).IsSome

    /// <summary>Applies an offset to a date on the calendar.</summary>
    let private shift (from: DateOnly) offset unit before =
        let signed = if before then -offset else offset

        match unit with
        | Days -> from.AddDays signed
        | Weeks -> from.AddDays(signed * 7)
        | Months -> from.AddMonths signed
        | Years -> from.AddYears signed

    /// <summary>
    /// Resolves a rule to a date, or says why it cannot be resolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Total and deterministic: the same rule and the same anchor date always give
    /// the same answer, with no reference to today, a locale or a database.
    /// </para>
    /// <para>
    /// Three ways to get no date, each named. An unknown anchor and a business-day
    /// basis both produce an <em>unresolved</em> deadline rather than a guess,
    /// because a wrong legal deadline shown as a real one is worse than a blank
    /// (ADR-0022).
    /// </para>
    /// </remarks>
    let resolve (anchorDate: DateOnly option) rule =
        match rule with
        | AbsoluteDeadline on -> Ok on

        | UnstructuredDeadline -> Error NoStructuredRule

        | RelativeDeadline(anchor, offset, unit, before, basis) ->
            if not (CalendarBasis.isComputable basis) then
                Error NoBusinessDayCalendar
            else
                match anchorDate with
                | Some from -> Ok(shift from offset unit before)
                | None -> Error(AnchorDateUnknown anchor)

    /// Whether the rule can be resolved with the anchor date available.
    let isResolvable anchorDate rule =
        match resolve anchorDate rule with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>The anchor a rule is measured from, when it has one.</summary>
    let anchorOf rule =
        match rule with
        | RelativeDeadline(anchor, _, _, _, _) -> Some anchor
        | AbsoluteDeadline _ | UnstructuredDeadline -> None

    /// <summary>
    /// Whether a resolved deadline has passed, given the date today.
    /// </summary>
    /// <remarks>
    /// Past due is a fact about a date. It is computed here and never stored,
    /// because a stored flag is wrong from the moment the clock moves.
    /// </remarks>
    let isPast (today: DateOnly) (deadline: DateOnly) = deadline < today

    /// <summary>How many days remain before a deadline. Negative once it has passed.</summary>
    let daysUntil (today: DateOnly) (deadline: DateOnly) = deadline.DayNumber - today.DayNumber
