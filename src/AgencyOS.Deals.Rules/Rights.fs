namespace AgencyOS.Deals.Rules

open System

/// <summary>
/// Whether a grant excludes others, and to what degree.
/// </summary>
/// <remarks>
/// Three values because the middle one is real: a sole-exclusive grant lets the
/// grantor continue to exploit the right themselves while granting nobody else,
/// which is neither exclusive nor non-exclusive. Collapsing it into either would
/// misstate what a contract says.
/// </remarks>
type Exclusivity =
    /// Nobody else, including the grantor.
    | Exclusive
    /// Nobody else, but the grantor keeps their own use.
    | SoleExclusive
    /// Others may hold the same right.
    | NonExclusive

/// <summary>
/// How long a grant runs.
/// </summary>
/// <remarks>
/// A period rather than two loose dates, so "starts after it ends" is not
/// constructible. Perpetual is its own case rather than a null end date, because a
/// missing end date and a grant that genuinely never ends are different facts and
/// a reader must be able to tell them apart (ADR-0022).
/// </remarks>
type GrantPeriod =
    /// Runs from a date and never ends.
    | Perpetual of from: DateOnly
    /// Runs between two dates, inclusive.
    | Fixed of starts: DateOnly * ends: DateOnly
    /// Runs from a date with no end recorded. Not the same as perpetual.
    | OpenEnded of since: DateOnly
    /// The contract states no period this build can structure.
    | UnstatedPeriod

/// <summary>Why a grant was refused.</summary>
type RightsError =
    /// The period ends before it starts.
    | PeriodEndsBeforeItStarts of starts: DateOnly * ends: DateOnly
    /// A period kind this build does not know.
    | UnknownPeriodKind of kind: int
    /// A period kind that needs a date and did not carry one.
    | PeriodMissingDate of kind: int
    /// An exclusivity value this build does not know.
    | UnknownExclusivity of value: int

/// <summary>
/// A grant's period as it crosses the language boundary.
/// </summary>
/// <remarks>
/// Flat and nullable, mirroring the persisted row. Parsing it into
/// <see cref="GrantPeriod"/> is where an impossible period is rejected.
/// </remarks>
[<CLIMutable>]
type GrantPeriodInput =
    { /// Which <see cref="GrantPeriod"/> case this row claims to be.
      Kind: int
      /// When the grant starts.
      Starts: Nullable<DateOnly>
      /// When it ends, for a fixed period.
      Ends: Nullable<DateOnly> }

/// <summary>Exclusivity mapping.</summary>
module Exclusivity =

    let all = [ Exclusive; SoleExclusive; NonExclusive ]

    let code value =
        match value with
        | Exclusive -> 1
        | SoleExclusive -> 2
        | NonExclusive -> 3

    let ofCode value =
        match value with
        | 1 -> Some Exclusive
        | 2 -> Some SoleExclusive
        | 3 -> Some NonExclusive
        | _ -> None

    /// <summary>Whether this grant would conflict with another of the same right.</summary>
    /// <remarks>
    /// Advisory only. AgencyOS reports that two recorded grants appear to overlap;
    /// it does not decide that either is invalid, because which one prevails is a
    /// legal question about instruments the system has not read (ADR-0022).
    /// </remarks>
    let excludesOthers value =
        match value with
        | Exclusive | SoleExclusive -> true
        | NonExclusive -> false

/// <summary>Grant period parsing and comparison.</summary>
module GrantPeriod =

    [<Literal>]
    let PerpetualKind = 1

    [<Literal>]
    let FixedKind = 2

    [<Literal>]
    let OpenEndedKind = 3

    [<Literal>]
    let UnstatedKind = 4

    let allKinds = [ PerpetualKind; FixedKind; OpenEndedKind; UnstatedKind ]

    let kindOf period =
        match period with
        | Perpetual _ -> PerpetualKind
        | Fixed _ -> FixedKind
        | OpenEnded _ -> OpenEndedKind
        | UnstatedPeriod -> UnstatedKind

    /// <summary>Turns a flat row into a period, or says why it is not one.</summary>
    let parse (input: GrantPeriodInput) =
        match input.Kind with
        | PerpetualKind ->
            if input.Starts.HasValue then
                Ok(Perpetual input.Starts.Value)
            else
                Error(PeriodMissingDate PerpetualKind)

        | FixedKind ->
            if not input.Starts.HasValue || not input.Ends.HasValue then
                Error(PeriodMissingDate FixedKind)
            elif input.Ends.Value < input.Starts.Value then
                Error(PeriodEndsBeforeItStarts(input.Starts.Value, input.Ends.Value))
            else
                Ok(Fixed(input.Starts.Value, input.Ends.Value))

        | OpenEndedKind ->
            if input.Starts.HasValue then
                Ok(OpenEnded input.Starts.Value)
            else
                Error(PeriodMissingDate OpenEndedKind)

        | UnstatedKind -> Ok UnstatedPeriod

        | other -> Error(UnknownPeriodKind other)

    let isValid input =
        match parse input with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>Whether the grant is running on a given date.</summary>
    /// <remarks>
    /// An unstated period answers false rather than true. The contract did not say,
    /// so the system does not claim the right is live - saying yes would be the
    /// system asserting a grant it cannot substantiate.
    /// </remarks>
    let coversOn (on: DateOnly) period =
        match period with
        | Perpetual from -> on >= from
        | Fixed(starts, ends) -> on >= starts && on <= ends
        | OpenEnded since -> on >= since
        | UnstatedPeriod -> false

    /// <summary>Whether two periods share any day.</summary>
    /// <remarks>
    /// Used to report apparent overlap between exclusive grants. An unstated period
    /// never overlaps, for the same reason it never covers: nothing is known.
    /// </remarks>
    let overlaps left right =
        let bounds period =
            match period with
            | Perpetual from -> Some(from, DateOnly.MaxValue)
            | Fixed(starts, ends) -> Some(starts, ends)
            | OpenEnded since -> Some(since, DateOnly.MaxValue)
            | UnstatedPeriod -> None

        match bounds left, bounds right with
        | Some(leftStart, leftEnd), Some(rightStart, rightEnd) ->
            leftStart <= rightEnd && rightStart <= leftEnd
        | _ -> false

    /// <summary>The last day the grant runs, when that is known.</summary>
    let endsOn period =
        match period with
        | Fixed(_, ends) -> Some ends
        | Perpetual _ | OpenEnded _ | UnstatedPeriod -> None
