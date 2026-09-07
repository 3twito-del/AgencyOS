namespace AgencyOS.Deals.Rules

/// <summary>What happened to one term between two offers.</summary>
type TermChange =
    /// The later offer introduces a term the earlier one did not have.
    | Added = 1
    /// The later offer drops a term the earlier one had.
    | Removed = 2
    /// Both offers carry the term with different values.
    | Changed = 3
    /// Both offers carry the term with the same value.
    | Unchanged = 4

/// <summary>
/// Which way a numeric value moved.
/// </summary>
/// <remarks>
/// Movement, never merit. Higher guaranteed compensation is good for the client
/// and longer exclusivity is usually not, and which is which depends on the term,
/// the side and the deal. M7 answers "what changed"; whether a change is welcome
/// is a judgement the agency makes, and one the system has no business asserting
/// (ADR-0021).
/// </remarks>
type ValueDirection =
    /// The values cannot be compared as numbers: different kinds, different
    /// currencies, or a kind that has no number to compare.
    | NotComparable = 0
    /// The later value is larger.
    | Increased = 1
    /// The later value is smaller.
    | Decreased = 2
    /// The numbers are equal.
    | Level = 3

/// <summary>One term's difference between two offers.</summary>
/// <remarks>
/// <see cref="Change"/> is the authority on which sides are populated: an added
/// term has no previous and a removed one has no current. Callers read it before
/// reaching for either row.
/// </remarks>
[<CLIMutable>]
type TermDifference =
    { /// The controlled term code being compared.
      TermCode: int
      /// What happened to it.
      Change: TermChange
      /// Which way the number moved, when the two values are comparable at all.
      Direction: ValueDirection
      /// The earlier row, or null when the term was added.
      Previous: TermInput
      /// The later row, or null when the term was removed.
      Current: TermInput }

/// <summary>Deterministic comparison of two offers' terms.</summary>
module Comparison =

    let private direction previous current =
        match TermValue.parse previous, TermValue.parse current with
        | Ok left, Ok right when TermValue.areComparable left right ->
            match TermValue.comparableNumber left, TermValue.comparableNumber right with
            | Some a, Some b when b > a -> ValueDirection.Increased
            | Some a, Some b when b < a -> ValueDirection.Decreased
            | Some _, Some _ -> ValueDirection.Level
            | _ -> ValueDirection.NotComparable
        | _ -> ValueDirection.NotComparable

    /// <summary>
    /// Whether two rows say the same thing.
    /// </summary>
    /// <remarks>
    /// Compared as parsed values so that presentation differences do not read as
    /// negotiation. Rows that do not parse fall back to structural equality, which
    /// is the only honest answer available for a value the kernel cannot interpret.
    /// </remarks>
    let private sameValue previous current =
        match TermValue.parse previous, TermValue.parse current with
        | Ok left, Ok right -> left = right
        | _ -> { previous with Sequence = 0 } = { current with Sequence = 0 }

    /// <summary>
    /// Compares the terms of an earlier offer against a later one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Joined on term code, which is unique within an offer. The result is
    /// ordered by the later offer's display sequence where the term survives, and
    /// by the earlier offer's where it does not, so a removed term appears where
    /// the reader last saw it rather than at the end.
    /// </para>
    /// <para>
    /// Total and deterministic: the same pair of offers always produces the same
    /// list, in the same order, with no reference to a clock, a locale or a
    /// database.
    /// </para>
    /// </remarks>
    let compare (previous: TermInput seq) (current: TermInput seq) =
        let previous = List.ofSeq previous
        let current = List.ofSeq current

        let previousByCode = dict [ for term in previous -> term.Code, term ]
        let currentByCode = dict [ for term in current -> term.Code, term ]

        let codes =
            (previous |> List.map (fun term -> term.Code))
            @ (current |> List.map (fun term -> term.Code))
            |> List.distinct

        codes
        |> List.map (fun code ->
            match previousByCode.TryGetValue code, currentByCode.TryGetValue code with
            | (true, before), (true, after) ->
                { TermCode = code
                  Change = (if sameValue before after then TermChange.Unchanged else TermChange.Changed)
                  Direction = direction before after
                  Previous = before
                  Current = after }
            | (true, before), _ ->
                { TermCode = code
                  Change = TermChange.Removed
                  Direction = ValueDirection.NotComparable
                  Previous = before
                  Current = Unchecked.defaultof<TermInput> }
            | _, (true, after) ->
                { TermCode = code
                  Change = TermChange.Added
                  Direction = ValueDirection.NotComparable
                  Previous = Unchecked.defaultof<TermInput>
                  Current = after }
            | _ ->
                // Unreachable: the code came from one of the two lists.
                { TermCode = code
                  Change = TermChange.Unchanged
                  Direction = ValueDirection.NotComparable
                  Previous = Unchecked.defaultof<TermInput>
                  Current = Unchecked.defaultof<TermInput> })
        |> List.sortBy (fun difference ->
            let order =
                match difference.Change with
                | TermChange.Removed -> difference.Previous.Sequence
                | _ -> difference.Current.Sequence

            order, difference.TermCode)
        |> Array.ofList

    /// The differences that are not merely "unchanged".
    let material (differences: TermDifference seq) =
        differences
        |> Seq.filter (fun difference -> difference.Change <> TermChange.Unchanged)
        |> Array.ofSeq
