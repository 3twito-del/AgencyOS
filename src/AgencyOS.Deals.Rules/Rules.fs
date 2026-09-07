namespace AgencyOS.Deals.Rules

open System
open System.Runtime.CompilerServices

/// <summary>
/// The one place C# talks to the rules kernel.
/// </summary>
/// <remarks>
/// <para>
/// Everything crossing here is a primitive, a plain array or a
/// <c>[&lt;CLIMutable&gt;]</c> record. Discriminated unions, options and F# lists
/// stay inside: they are what makes the rules above expressible, and they are
/// awkward to hold from C#. The translation is mechanical and total, and a test
/// walks every state and trigger in both directions so a value added on either
/// side of the boundary fails the build rather than silently losing a rule
/// (ADR-0021).
/// </para>
/// <para>
/// States and triggers cross as their persisted integers rather than as enums
/// declared here. C# owns its own enums, which is where they belong: the kernel
/// decides what is legal, not what things are called.
/// </para>
/// <para>
/// Zero is never a valid state or trigger, so it is used throughout as "no
/// answer" - an illegal transition, or an offer act with no deal consequence.
/// </para>
/// </remarks>
[<AbstractClass; Sealed; Extension>]
type DealRules private () =

    // ------------------------------------------------------------- vocabulary

    /// Every deal state this build knows, as persisted values.
    static member KnownDealStates() =
        DealState.all |> List.map DealState.code |> Array.ofList

    /// Every deal trigger this build knows, as persisted values.
    static member KnownDealTriggers() =
        DealTrigger.all |> List.map DealTrigger.code |> Array.ofList

    /// Every offer state this build knows, as persisted values.
    static member KnownOfferStates() =
        OfferState.all |> List.map OfferState.code |> Array.ofList

    /// Every offer trigger this build knows, as persisted values.
    static member KnownOfferTriggers() =
        OfferTrigger.all |> List.map OfferTrigger.code |> Array.ofList

    /// Every term value kind this build can parse.
    static member KnownValueKinds() = TermValue.allKinds |> Array.ofList

    /// Every offer direction this build knows, as persisted values.
    static member KnownDirections() =
        ProposalSide.all |> List.map ProposalSide.code |> Array.ofList

    // ------------------------------------------------------------------ deals

    /// <summary>The state a deal reaches, or 0 when the trigger is illegal there.</summary>
    static member NextDealState(state: int, trigger: int) =
        match DealState.ofCode state, DealTrigger.ofCode trigger with
        | Some state, Some trigger ->
            match DealState.apply state trigger with
            | Ok next -> DealState.code next
            | Error _ -> 0
        | _ -> 0

    /// Whether a deal trigger is legal from a state.
    static member DealPermits(state: int, trigger: int) =
        DealRules.NextDealState(state, trigger) <> 0

    /// The deal states reachable from this one, as persisted values.
    static member ReachableDealStates(state: int) =
        match DealState.ofCode state with
        | Some state -> DealState.reachableFrom state |> List.map DealState.code |> Array.ofList
        | None -> Array.empty

    /// Whether a deal is over and cannot be resumed.
    static member IsDealTerminal(state: int) =
        match DealState.ofCode state with
        | Some state -> DealState.isTerminal state
        | None -> false

    /// Whether new offer activity may be recorded while the deal is in this state.
    static member DealAcceptsOfferActivity(state: int) =
        match DealState.ofCode state with
        | Some state -> DealState.acceptsOfferActivity state
        | None -> false

    /// <summary>Whether a caller may request this deal trigger through a status command.</summary>
    static member IsCallerRequestableDealTrigger(trigger: int) =
        match DealTrigger.ofCode trigger with
        | Some trigger -> DealTrigger.isCallerRequestable trigger
        | None -> false

    // ----------------------------------------------------------------- offers

    /// <summary>The state an offer reaches, or 0 when the trigger is illegal there.</summary>
    static member NextOfferState(state: int, trigger: int) =
        match OfferState.ofCode state, OfferTrigger.ofCode trigger with
        | Some state, Some trigger ->
            match OfferState.apply state trigger with
            | Ok next -> OfferState.code next
            | Error _ -> 0
        | _ -> 0

    /// Whether an offer trigger is legal from a state.
    static member OfferPermits(state: int, trigger: int) =
        DealRules.NextOfferState(state, trigger) <> 0

    /// The offer states reachable from this one, as persisted values.
    static member ReachableOfferStates(state: int) =
        match OfferState.ofCode state with
        | Some state -> OfferState.reachableFrom state |> List.map OfferState.code |> Array.ofList
        | None -> Array.empty

    /// <summary>Whether the offer's commercial terms may still be edited.</summary>
    /// <remarks>
    /// The predicate the immutability rule rests on. Only a draft answers true.
    /// </remarks>
    static member IsOfferEditable(state: int) =
        match OfferState.ofCode state with
        | Some state -> OfferState.isEditable state
        | None -> false

    /// Whether the offer is still awaiting an answer.
    static member IsOfferStanding(state: int) =
        match OfferState.ofCode state with
        | Some state -> OfferState.isStanding state
        | None -> false

    /// Whether the offer has finished and can no longer move.
    static member IsOfferTerminal(state: int) =
        match OfferState.ofCode state with
        | Some state -> OfferState.isTerminal state
        | None -> false

    /// Whether a caller may request this offer trigger directly.
    static member IsCallerRequestableOfferTrigger(trigger: int) =
        match OfferTrigger.ofCode trigger with
        | Some trigger -> OfferTrigger.isCallerRequestable trigger
        | None -> false

    /// <summary>The deal trigger an offer act implies, or 0 when it implies none.</summary>
    static member DealConsequenceOf(offerTrigger: int) =
        match OfferTrigger.ofCode offerTrigger with
        | Some trigger ->
            match OfferState.dealConsequence trigger with
            | Some consequence -> DealTrigger.code consequence
            | None -> 0
        | None -> 0

    /// <summary>How a response of one direction relates to the offer it answers.</summary>
    /// <returns>1 when it is a counter from the other side, 2 when it is a revision of one's own.</returns>
    static member ClassifyResponse(answeredDirection: int, answeringDirection: int) =
        match ProposalSide.ofCode answeredDirection, ProposalSide.ofCode answeringDirection with
        | Some answered, Some answering ->
            match ProposalSide.classify answered answering with
            | Counter -> 1
            | Revision -> 2
        | _ -> 0

    // ------------------------------------------------------------------ terms

    /// <summary>Validates one term row.</summary>
    /// <returns>Null when the row is a value; otherwise why it is not.</returns>
    static member DescribeTermProblem(term: TermInput) =
        match TermValue.parse term with
        | Ok _ -> null
        | Error error -> Messages.describeTerm error

    /// Whether a term row describes a value this build can make sense of.
    static member IsTermValid(term: TermInput) = TermValue.isValid term

    /// The value kind a parsed row actually is, or 0 when it does not parse.
    static member ValueKindOf(term: TermInput) =
        match TermValue.parse term with
        | Ok value -> TermValue.kindOf value
        | Error _ -> 0

    /// Whether a string is shaped like an ISO 4217 alphabetic code.
    static member IsCurrencyShaped(currency: string) = TermValue.isCurrencyShaped currency

    /// Whether two term rows could be compared as numbers at all.
    static member AreComparable(left: TermInput, right: TermInput) =
        match TermValue.parse left, TermValue.parse right with
        | Ok left, Ok right -> TermValue.areComparable left right
        | _ -> false

    // ------------------------------------------------------------ negotiation

    /// <summary>Validates a whole negotiation thread against the deal's own state.</summary>
    /// <returns>Null when the thread is well formed; otherwise why it is not.</returns>
    static member DescribeChainProblem(dealState: int, offers: OfferNode[]) =
        match Negotiation.validate dealState (Array.toList offers) with
        | Ok() -> null
        | Error error -> Messages.describeChain error

    /// Whether a negotiation thread is well formed.
    static member IsChainValid(dealState: int, offers: OfferNode[]) =
        Negotiation.isValid dealState (Array.toList offers)

    /// <summary>The identifier of the offer awaiting an answer, or the empty GUID.</summary>
    static member StandingOffer(offers: OfferNode[]) =
        match Negotiation.standing offers with
        | Some offer -> offer.OfferId
        | None -> Guid.Empty

    /// <summary>The identifier of the accepted offer, or the empty GUID.</summary>
    static member AcceptedOffer(offers: OfferNode[]) =
        match Negotiation.accepted offers with
        | Some offer -> offer.OfferId
        | None -> Guid.Empty

    /// <summary>The identifier of the most recent offer in the thread, or the empty GUID.</summary>
    static member LatestOffer(offers: OfferNode[]) =
        match Negotiation.latest offers with
        | Some offer -> offer.OfferId
        | None -> Guid.Empty

    /// Whether a new offer may answer this one.
    static member MayBeAnswered(offer: OfferNode) = Negotiation.mayBeAnswered offer

    /// The next canonical sequence number for a thread.
    static member NextSequence(offers: OfferNode[]) =
        if Array.isEmpty offers then
            1
        else
            (offers |> Array.map (fun offer -> offer.Sequence) |> Array.max) + 1

    // ------------------------------------------------------------ comparison

    /// <summary>Compares an earlier offer's terms against a later one's.</summary>
    static member CompareTerms(previous: TermInput[], current: TermInput[]) =
        Comparison.compare previous current

    /// The differences that are not merely unchanged.
    static member MaterialDifferences(differences: TermDifference[]) = Comparison.material differences

/// <summary>Renders refusals as sentences a person can act on.</summary>
/// <remarks>
/// Kept beside the boundary rather than inside the rule modules: the rules decide
/// what is wrong, and wording is a presentation concern that should not be able to
/// change what they decide.
/// </remarks>
and private Messages =

    static member describeTerm(error: TermError) =
        match error with
        | UnknownValueKind kind -> $"Term value kind {kind} is not one this build understands."
        | MissingValue(kind, field) -> $"A term of kind {kind} must carry a {field} value."
        | ExtraneousValue(kind, field) -> $"A term of kind {kind} must not carry a {field} value."
        | InvalidCurrency currency ->
            if String.IsNullOrWhiteSpace currency then
                "A money term must name the currency it is denominated in."
            else
                $"'{currency}' is not shaped like an ISO 4217 currency code."
        | OutOfRange(kind, value, low, high) ->
            $"A term of kind {kind} must be between {low} and {high}; {value} is not."
        | InvalidUnit kind -> $"A term of kind {kind} must state the unit it is measured in."
        | InvalidText reason -> reason

    static member describeChain(error: ChainError) =
        match error with
        | UnknownOfferState(offerId, state) -> $"Offer {offerId} carries state {state}, which this build does not understand."
        | UnknownOfferDirection(offerId, direction) ->
            $"Offer {offerId} carries direction {direction}, which this build does not understand."
        | DuplicateSequence sequence -> $"Two offers claim position {sequence} in the negotiation."
        | SelfResponse offerId -> $"Offer {offerId} cannot respond to itself."
        | ResponseOutsideDeal(offerId, missing) -> $"Offer {offerId} responds to {missing}, which is not part of this deal."
        | ResponseOutOfOrder(offerId, answered) -> $"Offer {offerId} responds to {answered}, which came after it."
        | ResponseCycle offerId -> $"The negotiation lineage through offer {offerId} loops."
        | MultipleStandingOffers(first, second) ->
            $"Offers {first} and {second} are both standing; a deal has one offer on the table at a time."
        | MultipleAcceptedOffers(first, second) -> $"Offers {first} and {second} both claim to be the agreement."
        | AnsweredAnAcceptedOffer(offerId, accepted) ->
            $"Offer {offerId} responds to {accepted}, which had already been accepted. Reopen the negotiation first."
        | AcceptanceDisagreesWithDeal(dealState, acceptedCount) ->
            $"A deal in state {dealState} cannot have {acceptedCount} accepted offer(s)."
