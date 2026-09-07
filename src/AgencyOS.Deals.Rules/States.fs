namespace AgencyOS.Deals.Rules

/// <summary>
/// Where a deal stands as a negotiation.
/// </summary>
/// <remarks>
/// Deliberately short of contract language. There is no Signed, Executed, Paid or
/// Commissioned here: terms being agreed is a commercial fact the agency can
/// assert, and an executed contract is a legal one it cannot until M8 gives it a
/// way to record the document. A status that claimed otherwise would be read as
/// the agency saying a deal was papered when nobody had papered it.
/// </remarks>
type DealState =
    /// Opened, with no offer on the table yet.
    | DealDraft
    /// At least one offer has been recorded.
    | Negotiating
    /// An offer was accepted. Commercial terms are agreed; nothing is executed.
    | TermsAgreed
    /// Talks ended without agreement.
    | NoDeal
    /// Called off. Terminal.
    | DealCancelled

/// <summary>
/// What causes a deal to change state.
/// </summary>
/// <remarks>
/// Transitions are named by their cause rather than left as a free
/// from/to table, because two of them must not be requestable directly.
/// Negotiating is reached by recording an offer and TermsAgreed by accepting one:
/// a status command that could set either would let a deal claim terms were
/// agreed with no accepted offer behind it, which is precisely the state the
/// milestone exists to make impossible.
/// </remarks>
type DealTrigger =
    /// The first offer was recorded against the deal.
    | OfferRecorded
    /// An offer was accepted.
    | OfferAccepted
    /// Negotiation was explicitly reopened, unwinding an acceptance or a closure.
    | NegotiationReopened
    /// Talks were closed without agreement.
    | ClosedNoDeal
    /// The deal was called off.
    | Cancelled

/// <summary>
/// Where one offer stands.
/// </summary>
/// <remarks>
/// A draft is a working document and its terms may be edited. Everything else is
/// a record of something that happened in the world, and its terms are frozen
/// (ADR-0021).
/// </remarks>
type OfferState =
    /// Being prepared. Terms are editable and nothing has been communicated.
    | OfferDraft
    /// Standing: made or received, awaiting an answer. Terms are frozen.
    | Open
    /// Accepted. The agreed commercial snapshot.
    | Accepted
    /// The recipient said no.
    | Rejected
    /// The proposing side pulled it.
    | Withdrawn
    /// An explicitly known expiration passed and was recorded as having passed.
    | Expired
    /// Answered by a later offer, or unwound by a reopened negotiation.
    | Superseded

/// <summary>What causes an offer to change state.</summary>
type OfferTrigger =
    /// A draft was recorded as actually made or received.
    | Opened
    /// Accepted by the receiving side.
    | Accept
    /// Rejected by the receiving side.
    | Reject
    /// Withdrawn by the proposing side.
    | Withdraw
    /// A known expiration was recorded as having passed.
    | Expire
    /// A later offer in the chain responded to this one.
    | AnsweredByCounter
    /// A reopened negotiation unwound this acceptance.
    | UnwoundByReopen

/// <summary>Why a requested transition was refused.</summary>
type TransitionError =
    /// The deal cannot do that from where it is.
    | IllegalDealTransition of dealState: DealState * dealTrigger: DealTrigger
    /// The offer cannot do that from where it is.
    | IllegalOfferTransition of offerState: OfferState * offerTrigger: OfferTrigger
    /// The offer act is legal for the offer but not while the deal is here.
    | DealDoesNotAcceptOfferActivity of state: DealState

/// <summary>Deal trigger mapping.</summary>
module DealTrigger =

    /// Every trigger this build knows.
    let all =
        [ OfferRecorded; OfferAccepted; NegotiationReopened; ClosedNoDeal; Cancelled ]

    /// The persisted value for a trigger.
    let code trigger =
        match trigger with
        | OfferRecorded -> 1
        | OfferAccepted -> 2
        | NegotiationReopened -> 3
        | ClosedNoDeal -> 4
        | Cancelled -> 5

    /// The trigger for a persisted value, or None when this build does not know it.
    let ofCode value =
        match value with
        | 1 -> Some OfferRecorded
        | 2 -> Some OfferAccepted
        | 3 -> Some NegotiationReopened
        | 4 -> Some ClosedNoDeal
        | 5 -> Some Cancelled
        | _ -> None

    /// <summary>
    /// Whether a plain status command may ask for this trigger.
    /// </summary>
    /// <remarks>
    /// Recording an offer and accepting one are consequences of offer acts, not
    /// statuses a caller may set. Without this a deal could be marked as having
    /// agreed terms with no accepted offer behind it, which is the one state the
    /// milestone exists to prevent.
    /// </remarks>
    let isCallerRequestable trigger =
        match trigger with
        | ClosedNoDeal | Cancelled | NegotiationReopened -> true
        | OfferRecorded | OfferAccepted -> false

/// <summary>Offer trigger mapping.</summary>
module OfferTrigger =

    /// Every trigger this build knows.
    let all =
        [ Opened; Accept; Reject; Withdraw; Expire; AnsweredByCounter; UnwoundByReopen ]

    /// The persisted value for a trigger.
    let code trigger =
        match trigger with
        | Opened -> 1
        | Accept -> 2
        | Reject -> 3
        | Withdraw -> 4
        | Expire -> 5
        | AnsweredByCounter -> 6
        | UnwoundByReopen -> 7

    /// The trigger for a persisted value, or None when this build does not know it.
    let ofCode value =
        match value with
        | 1 -> Some Opened
        | 2 -> Some Accept
        | 3 -> Some Reject
        | 4 -> Some Withdraw
        | 5 -> Some Expire
        | 6 -> Some AnsweredByCounter
        | 7 -> Some UnwoundByReopen
        | _ -> None

    /// <summary>
    /// Whether a caller may ask for this trigger directly.
    /// </summary>
    /// <remarks>
    /// Supersession is never requested. An offer is superseded because a later
    /// offer answered it or because a reopen unwound it, and letting a caller set
    /// it by hand would detach the status from the event that caused it.
    /// </remarks>
    let isCallerRequestable trigger =
        match trigger with
        | Opened | Accept | Reject | Withdraw | Expire -> true
        | AnsweredByCounter | UnwoundByReopen -> false

/// <summary>The deal state machine, stated once and enumerated by tests.</summary>
module DealState =

    /// Every state this build knows.
    let all = [ DealDraft; Negotiating; TermsAgreed; NoDeal; DealCancelled ]

    /// Every trigger this build knows, taken from the one list that defines them.
    let allTriggers = DealTrigger.all

    /// The persisted value for a state.
    let code state =
        match state with
        | DealDraft -> 1
        | Negotiating -> 2
        | TermsAgreed -> 3
        | NoDeal -> 4
        | DealCancelled -> 5

    /// The state for a persisted value, or None when this build does not know it.
    let ofCode value =
        match value with
        | 1 -> Some DealDraft
        | 2 -> Some Negotiating
        | 3 -> Some TermsAgreed
        | 4 -> Some NoDeal
        | 5 -> Some DealCancelled
        | _ -> None

    /// Whether the deal is over and cannot be resumed.
    let isTerminal state =
        match state with
        | DealCancelled -> true
        | DealDraft | Negotiating | TermsAgreed | NoDeal -> false

    /// <summary>
    /// The complete transition function.
    /// </summary>
    /// <remarks>
    /// A cancelled deal accepts nothing: calling something off and then carrying
    /// on is a new negotiation, and recording it as the same one would erase that
    /// it was ever stopped. This mirrors the M6 rule for a cancelled pursuit.
    /// </remarks>
    let apply state trigger =
        match state, trigger with
        | DealDraft, OfferRecorded -> Ok Negotiating
        | DealDraft, Cancelled -> Ok DealCancelled

        | Negotiating, OfferRecorded -> Ok Negotiating
        | Negotiating, OfferAccepted -> Ok TermsAgreed
        | Negotiating, ClosedNoDeal -> Ok NoDeal
        | Negotiating, Cancelled -> Ok DealCancelled

        // Reopening unwinds the acceptance. The accepted offer is superseded
        // rather than edited, so what was agreed on the day survives intact.
        | TermsAgreed, NegotiationReopened -> Ok Negotiating

        | NoDeal, NegotiationReopened -> Ok Negotiating

        | _ -> Error(IllegalDealTransition(state, trigger))

    /// Whether a trigger is legal from a state.
    let permits state trigger =
        match apply state trigger with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>
    /// The states reachable from this one, in any way.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="apply"/> rather than written out beside it, so the
    /// published table cannot drift from the function that enforces it.
    /// </remarks>
    let reachableFrom state =
        allTriggers
        |> List.choose (fun trigger ->
            match apply state trigger with
            | Ok next -> Some next
            | Error _ -> None)
        |> List.distinct

    /// <summary>
    /// Whether new offer activity may be recorded while the deal is here.
    /// </summary>
    /// <remarks>
    /// A cancelled or no-deal negotiation refuses offers for the reason M6
    /// refused submissions against a closed pursuit: the record would describe
    /// something that did not happen the way it says. Reopening first is the
    /// explicit act that makes it true again.
    /// </remarks>
    let acceptsOfferActivity state =
        match state with
        | DealDraft | Negotiating -> true
        | TermsAgreed | NoDeal | DealCancelled -> false

/// <summary>The offer state machine, stated once and enumerated by tests.</summary>
module OfferState =

    /// Every state this build knows.
    let all = [ OfferDraft; Open; Accepted; Rejected; Withdrawn; Expired; Superseded ]

    /// Every trigger this build knows, taken from the one list that defines them.
    let allTriggers = OfferTrigger.all

    /// The persisted value for a state.
    let code state =
        match state with
        | OfferDraft -> 1
        | Open -> 2
        | Accepted -> 3
        | Rejected -> 4
        | Withdrawn -> 5
        | Expired -> 6
        | Superseded -> 7

    /// The state for a persisted value, or None when this build does not know it.
    let ofCode value =
        match value with
        | 1 -> Some OfferDraft
        | 2 -> Some Open
        | 3 -> Some Accepted
        | 4 -> Some Rejected
        | 5 -> Some Withdrawn
        | 6 -> Some Expired
        | 7 -> Some Superseded
        | _ -> None

    /// <summary>
    /// Whether the offer's commercial terms may still be edited.
    /// </summary>
    /// <remarks>
    /// The single most important predicate in the milestone. Everything except a
    /// draft is a record of something that happened, and rewriting it would
    /// change what the agency is recorded as having proposed or been offered.
    /// </remarks>
    let isEditable state =
        match state with
        | OfferDraft -> true
        | Open | Accepted | Rejected | Withdrawn | Expired | Superseded -> false

    /// Whether the offer is still awaiting an answer.
    let isStanding state =
        match state with
        | Open -> true
        | OfferDraft | Accepted | Rejected | Withdrawn | Expired | Superseded -> false

    /// Whether the offer has finished and can no longer move.
    let isTerminal state =
        match state with
        | Rejected | Withdrawn | Expired | Superseded -> true
        | OfferDraft | Open | Accepted -> false

    /// <summary>The complete transition function.</summary>
    /// <remarks>
    /// An accepted offer can only be superseded, and only by an explicit reopen.
    /// It can never be rejected, withdrawn or expired: those would claim the
    /// counterparty or the agency did something after agreement that they did not
    /// do.
    /// </remarks>
    let apply state trigger =
        match state, trigger with
        | OfferDraft, Opened -> Ok Open
        // Abandoning a draft nobody ever saw. Recorded rather than deleted.
        | OfferDraft, Withdraw -> Ok Withdrawn

        | Open, Accept -> Ok Accepted
        | Open, Reject -> Ok Rejected
        | Open, Withdraw -> Ok Withdrawn
        | Open, Expire -> Ok Expired
        | Open, AnsweredByCounter -> Ok Superseded

        | Accepted, UnwoundByReopen -> Ok Superseded

        | _ -> Error(IllegalOfferTransition(state, trigger))

    /// Whether a trigger is legal from a state.
    let permits state trigger =
        match apply state trigger with
        | Ok _ -> true
        | Error _ -> false

    /// The states reachable from this one, derived from the transition function.
    let reachableFrom state =
        allTriggers
        |> List.choose (fun trigger ->
            match apply state trigger with
            | Ok next -> Some next
            | Error _ -> None)
        |> List.distinct

    /// <summary>
    /// The deal-level consequence of an offer trigger, when there is one.
    /// </summary>
    /// <remarks>
    /// Recording an offer moves a draft deal into negotiation and accepting one
    /// agrees its terms. Stating the coupling here keeps the two machines from
    /// being advanced independently by a caller who updates one and forgets the
    /// other.
    /// </remarks>
    let dealConsequence trigger =
        match trigger with
        | Opened -> Some OfferRecorded
        | Accept -> Some OfferAccepted
        | Reject | Withdraw | Expire | AnsweredByCounter | UnwoundByReopen -> None
