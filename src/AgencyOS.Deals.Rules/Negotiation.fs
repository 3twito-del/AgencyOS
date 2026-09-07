namespace AgencyOS.Deals.Rules

open System

/// <summary>
/// Which side proposed the terms.
/// </summary>
/// <remarks>
/// Named for the side rather than for a direction, so it cannot collide with the
/// domain's own OfferDirection enum across the boundary. The kernel's vocabulary
/// stays inside the kernel (ADR-0021).
/// </remarks>
type ProposalSide =
    /// The counterparty made or provided these terms.
    | CounterpartySide
    /// Our side made or provided these terms.
    | AgencySide

/// <summary>
/// What a later offer is doing to the one it answers.
/// </summary>
/// <remarks>
/// Derived from the two directions rather than stored, because it is not an
/// independent fact: an answer from the other side is a counter and an answer from
/// the same side is a revision of one's own position. Storing it would be a second
/// value that could disagree with the directions it came from.
/// </remarks>
type ResponseKind =
    /// The other side answered with different terms.
    | Counter
    /// The same side replaced its own earlier terms.
    | Revision

/// <summary>One offer, as the chain rules see it.</summary>
/// <remarks>
/// Deliberately carries no terms. Chain validity is a question about lineage and
/// state, and a kernel that also had the money in front of it would invite rules
/// that quietly depend on the numbers.
/// </remarks>
[<CLIMutable>]
type OfferNode =
    { /// Identity, opaque to the rules.
      OfferId: Guid
      /// Canonical order within the deal. Never inferred from identity or insertion.
      Sequence: int
      /// Persisted <see cref="OfferState"/> value.
      State: int
      /// Persisted <see cref="ProposalSide"/> value.
      Direction: int
      /// The offer this one answers, when it answers one.
      RespondsToOfferId: Nullable<Guid> }

/// <summary>Why a negotiation chain was refused.</summary>
type ChainError =
    /// An offer carries a state this build does not know.
    | UnknownOfferState of offerId: Guid * state: int
    /// An offer carries a side this build does not know.
    | UnknownOfferDirection of offerId: Guid * direction: int
    /// Two offers claim the same position in the thread.
    | DuplicateSequence of sequence: int
    /// An offer answers itself.
    | SelfResponse of offerId: Guid
    /// An offer answers something that is not in this deal.
    | ResponseOutsideDeal of offerId: Guid * missing: Guid
    /// An offer answers something that came after it.
    | ResponseOutOfOrder of offerId: Guid * answered: Guid
    /// The lineage loops.
    | ResponseCycle of offerId: Guid
    /// Two offers are standing at once.
    | MultipleStandingOffers of first: Guid * second: Guid
    /// Two offers claim to be the agreement.
    | MultipleAcceptedOffers of first: Guid * second: Guid
    /// An offer answers one that had already been accepted.
    | AnsweredAnAcceptedOffer of offerId: Guid * accepted: Guid
    /// The deal says terms are agreed and no offer says it is the agreement, or the reverse.
    | AcceptanceDisagreesWithDeal of dealState: int * acceptedCount: int

/// <summary>Proposal side mapping and response classification.</summary>
module ProposalSide =

    /// Every side this build knows.
    let all = [ CounterpartySide; AgencySide ]

    /// The persisted value for a side.
    let code side =
        match side with
        | CounterpartySide -> 1
        | AgencySide -> 2

    /// The side for a persisted value, or None when this build does not know it.
    let ofCode value =
        match value with
        | 1 -> Some CounterpartySide
        | 2 -> Some AgencySide
        | _ -> None

    /// The other side.
    let opposite side =
        match side with
        | CounterpartySide -> AgencySide
        | AgencySide -> CounterpartySide

    /// <summary>What a response from one side does to an offer from another.</summary>
    let classify answered answering =
        if answered = answering then Revision else Counter

/// <summary>Negotiation chain rules.</summary>
/// <remarks>
/// M7 keeps one canonical chronological thread per deal. Branching is not
/// supported and is not silently tolerated: two standing offers is a refusal, not
/// a shape the reader has to interpret.
/// </remarks>
module Negotiation =

    /// The offer currently awaiting an answer, when there is one.
    let standing (offers: OfferNode seq) =
        offers
        |> Seq.filter (fun offer ->
            match OfferState.ofCode offer.State with
            | Some state -> OfferState.isStanding state
            | None -> false)
        |> Seq.sortBy (fun offer -> offer.Sequence)
        |> Seq.tryLast

    /// The accepted offer, when there is one.
    let accepted (offers: OfferNode seq) =
        offers
        |> Seq.filter (fun offer -> OfferState.ofCode offer.State = Some Accepted)
        |> Seq.sortBy (fun offer -> offer.Sequence)
        |> Seq.tryHead

    /// <summary>
    /// The most recent offer in the thread, whatever became of it.
    /// </summary>
    /// <remarks>
    /// Ordered by the canonical sequence, never by identifier or insertion order.
    /// Version 7 GUIDs happen to sort by creation time, and relying on that would
    /// make the thread's order an accident of the identifier scheme.
    /// </remarks>
    let latest (offers: OfferNode seq) =
        offers |> Seq.sortBy (fun offer -> offer.Sequence) |> Seq.tryLast

    let private firstDuplicateSequence (offers: OfferNode list) =
        offers
        |> List.countBy (fun offer -> offer.Sequence)
        |> List.tryFind (fun (_, count) -> count > 1)
        |> Option.map fst

    let private walkForCycle (byId: Collections.Generic.IDictionary<Guid, OfferNode>) (start: OfferNode) =
        let rec walk (current: OfferNode) seen depth =
            if depth > byId.Count then
                Some start.OfferId
            elif not current.RespondsToOfferId.HasValue then
                None
            else
                let next = current.RespondsToOfferId.Value

                if Set.contains next seen then
                    Some start.OfferId
                else
                    match byId.TryGetValue next with
                    | true, node -> walk node (Set.add next seen) (depth + 1)
                    | _ -> None

        walk start (Set.singleton start.OfferId) 0

    let private validateNode (byId: Collections.Generic.IDictionary<Guid, OfferNode>) (offer: OfferNode) =
        match OfferState.ofCode offer.State with
        | None -> Some(UnknownOfferState(offer.OfferId, offer.State))
        | Some _ ->
            match ProposalSide.ofCode offer.Direction with
            | None -> Some(UnknownOfferDirection(offer.OfferId, offer.Direction))
            | Some _ when not offer.RespondsToOfferId.HasValue -> None
            | Some _ ->
                let answeredId = offer.RespondsToOfferId.Value

                if answeredId = offer.OfferId then
                    Some(SelfResponse offer.OfferId)
                else
                    match byId.TryGetValue answeredId with
                    | false, _ -> Some(ResponseOutsideDeal(offer.OfferId, answeredId))
                    | true, answered when answered.Sequence >= offer.Sequence ->
                        Some(ResponseOutOfOrder(offer.OfferId, answeredId))
                    | true, answered when OfferState.ofCode answered.State = Some Accepted ->
                        Some(AnsweredAnAcceptedOffer(offer.OfferId, answeredId))
                    | true, _ -> walkForCycle byId offer |> Option.map ResponseCycle

    let private validateUniqueness (offers: OfferNode list) =
        let withState state =
            offers
            |> List.filter (fun offer -> OfferState.ofCode offer.State = Some state)
            |> List.sortBy (fun offer -> offer.Sequence)

        match withState Open with
        | first :: second :: _ -> Some(MultipleStandingOffers(first.OfferId, second.OfferId))
        | _ ->
            match withState Accepted with
            | first :: second :: _ -> Some(MultipleAcceptedOffers(first.OfferId, second.OfferId))
            | _ -> None

    /// <summary>
    /// Checks a whole thread, including its agreement with the deal's own state.
    /// </summary>
    /// <remarks>
    /// The last check is the one that matters most: a deal saying its terms are
    /// agreed while no offer says it is the agreement is exactly the state that
    /// would let the agency believe it had a deal it could not produce. It is
    /// enforced here, and separately by a partial unique index in PostgreSQL.
    /// </remarks>
    let validate (dealState: int) (offers: OfferNode seq) =
        let offers = List.ofSeq offers

        match firstDuplicateSequence offers with
        | Some sequence -> Error(DuplicateSequence sequence)
        | None ->

        let byId = dict [ for offer in offers -> offer.OfferId, offer ]

        match offers |> List.tryPick (validateNode byId) with
        | Some error -> Error error
        | None ->

        match validateUniqueness offers with
        | Some error -> Error error
        | None ->

        let acceptedCount =
            offers
            |> List.filter (fun offer -> OfferState.ofCode offer.State = Some Accepted)
            |> List.length

        let agreed = DealState.ofCode dealState = Some TermsAgreed

        if agreed <> (acceptedCount = 1) then
            Error(AcceptanceDisagreesWithDeal(dealState, acceptedCount))
        else
            Ok()

    /// Whether a thread is well formed.
    let isValid dealState offers =
        match validate dealState offers with
        | Ok() -> true
        | Error _ -> false

    /// <summary>
    /// Whether a new offer may answer this one.
    /// </summary>
    /// <remarks>
    /// Only a standing offer can be countered. Answering an accepted offer would
    /// change what was agreed without anybody reopening the negotiation, and
    /// answering a rejected or withdrawn one describes a conversation that had
    /// already ended.
    /// </remarks>
    let mayBeAnswered (offer: OfferNode) =
        match OfferState.ofCode offer.State with
        | Some state -> OfferState.isStanding state
        | None -> false
