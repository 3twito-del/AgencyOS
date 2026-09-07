namespace AgencyOS.Deals.Rules

/// <summary>
/// What a negotiated term turned into once the contract was drafted.
/// </summary>
/// <remarks>
/// Five factual outcomes and no judgement. There is deliberately no Favourable,
/// Unfavourable, Risk or Material: whether a change is acceptable is a legal
/// question about a document AgencyOS has not read, and a system that answered it
/// would be answering it wrongly some of the time in a place nobody checks
/// (ADR-0022).
/// </remarks>
type ReconciliationResult =
    /// The contract says what was negotiated.
    | Matched = 1
    /// Both carry the term, with different values.
    | Changed = 2
    /// It was negotiated and the draft does not carry it.
    | MissingFromContract = 3
    /// The draft carries it and it was not negotiated.
    | AddedInContract = 4
    /// Both carry it, in shapes that cannot be compared.
    | NotComparable = 5

/// <summary>One negotiated term set against what the contract says.</summary>
/// <remarks>
/// <see cref="Result"/> is the authority on which sides are populated: a missing
/// term has no contract row and an added one has no negotiated row.
/// </remarks>
[<CLIMutable>]
type ReconciliationLine =
    { /// The term code, shared between the offer and the contract vocabularies.
      TermCode: int
      /// What became of it.
      Result: ReconciliationResult
      /// Which way a comparable number moved. Movement, never merit.
      Direction: ValueDirection
      /// What was negotiated, or null when the contract added it.
      Negotiated: TermInput
      /// What the contract says, or null when the contract omits it.
      Contracted: TermInput }

/// <summary>
/// Comparing what was negotiated against what a draft actually says.
/// </summary>
/// <remarks>
/// Built on the same comparison the offer thread uses, because it is the same
/// operation: two sets of typed terms, joined on code. Only the vocabulary of the
/// answer differs, and it differs because the question does - "what changed
/// between our two offers" and "what did the lawyers do to what we agreed" are
/// read by different people for different reasons.
/// </remarks>
module Reconciliation =

    /// <summary>
    /// Whether two rows can be compared as values at all.
    /// </summary>
    /// <remarks>
    /// Different value kinds cannot, and neither can money in two currencies:
    /// M8 holds no exchange rates, exactly as M7 does not.
    /// </remarks>
    let private comparable (negotiated: TermInput) (contracted: TermInput) =
        match TermValue.parse negotiated, TermValue.parse contracted with
        | Ok left, Ok right ->
            TermValue.kindOf left = TermValue.kindOf right
            && TermValue.currencyOf left = TermValue.currencyOf right
        | _ -> false

    let private classify (difference: TermDifference) =
        match difference.Change with
        | TermChange.Added -> ReconciliationResult.AddedInContract
        | TermChange.Removed -> ReconciliationResult.MissingFromContract
        | TermChange.Unchanged -> ReconciliationResult.Matched
        | _ ->
            if comparable difference.Previous difference.Current then
                ReconciliationResult.Changed
            else
                ReconciliationResult.NotComparable

    /// <summary>
    /// Compares an accepted offer's terms against a contract version's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neither side is mutated and nothing is normalised away. A contract that
    /// drops the billing term reports it as missing; one that adds exclusivity
    /// reports it as added. Both are things a lawyer needs to see, and silently
    /// reconciling either would hide the reason the comparison exists.
    /// </para>
    /// <para>
    /// Total and deterministic. The same offer and version always produce the same
    /// lines, in the same order.
    /// </para>
    /// </remarks>
    let compare (negotiated: TermInput seq) (contracted: TermInput seq) =
        Comparison.compare negotiated contracted
        |> Array.map (fun difference ->
            { TermCode = difference.TermCode
              Result = classify difference
              Direction =
                if classify difference = ReconciliationResult.Changed then
                    difference.Direction
                else
                    ValueDirection.NotComparable
              Negotiated = difference.Previous
              Contracted = difference.Current })

    /// <summary>The lines where the contract does not simply say what was agreed.</summary>
    let differences (lines: ReconciliationLine seq) =
        lines
        |> Seq.filter (fun line -> line.Result <> ReconciliationResult.Matched)
        |> Array.ofSeq

    /// <summary>How many lines need somebody to look at them.</summary>
    let differenceCount lines = (differences lines).Length

    /// <summary>Whether the draft says exactly what was negotiated.</summary>
    let isFaithful lines = differenceCount lines = 0
