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

    // ------------------------------------------------------------- contracts
    //
    // M8. The same boundary rules apply: primitives and flat records only, states
    // as their persisted integers, zero meaning "no answer" (ADR-0022).

    /// Every contract state this build knows, as persisted values.
    static member KnownContractStates() =
        ContractState.all |> List.map ContractState.code |> Array.ofList

    /// Every contract trigger this build knows, as persisted values.
    static member KnownContractTriggers() =
        ContractTrigger.all |> List.map ContractTrigger.code |> Array.ofList

    /// <summary>The state a contract reaches, or 0 when the trigger is illegal there.</summary>
    static member NextContractState(state: int, trigger: int) =
        match ContractState.ofCode state, ContractTrigger.ofCode trigger with
        | Some state, Some trigger ->
            match ContractState.apply state trigger with
            | Ok next -> ContractState.code next
            | Error _ -> 0
        | _ -> 0

    /// Whether a contract trigger is legal from a state.
    static member ContractPermits(state: int, trigger: int) =
        DealRules.NextContractState(state, trigger) <> 0

    /// Whether the instrument is finished and cannot move.
    static member IsContractTerminal(state: int) =
        match ContractState.ofCode state with
        | Some state -> ContractState.isTerminal state
        | None -> false

    /// Whether every required signatory has signed.
    static member IsContractFullyExecuted(state: int) =
        match ContractState.ofCode state with
        | Some state -> ContractState.isFullyExecuted state
        | None -> false

    /// Whether a new drafting version may be recorded while the contract is here.
    static member ContractAcceptsNewVersions(state: int) =
        match ContractState.ofCode state with
        | Some state -> ContractState.acceptsNewVersions state
        | None -> false

    /// Whether signatures may be recorded while the contract is here.
    static member ContractAcceptsSignatures(state: int) =
        match ContractState.ofCode state with
        | Some state -> ContractState.acceptsSignatures state
        | None -> false

    /// <summary>Whether a caller may request this contract trigger through a status command.</summary>
    static member IsCallerRequestableContractTrigger(trigger: int) =
        match ContractTrigger.ofCode trigger with
        | Some trigger -> ContractState.isCallerRequestable trigger
        | None -> false

    // ---------------------------------------------------------------- options

    /// Every option state this build knows, as persisted values.
    static member KnownOptionStates() =
        OptionState.all |> List.map OptionState.code |> Array.ofList

    /// Every option trigger this build knows, as persisted values.
    static member KnownOptionTriggers() =
        OptionTrigger.all |> List.map OptionTrigger.code |> Array.ofList

    /// <summary>The state an option reaches, or 0 when the trigger is illegal there.</summary>
    static member NextOptionState(state: int, trigger: int) =
        match OptionState.ofCode state, OptionTrigger.ofCode trigger with
        | Some state, Some trigger ->
            match OptionState.apply state trigger with
            | Ok next -> OptionState.code next
            | Error _ -> 0
        | _ -> 0

    static member OptionPermits(state: int, trigger: int) =
        DealRules.NextOptionState(state, trigger) <> 0

    /// Whether the option is resolved and can no longer move.
    static member IsOptionTerminal(state: int) =
        match OptionState.ofCode state with
        | Some state -> OptionState.isTerminal state
        | None -> false

    /// Whether this state carries a date recording when the option was resolved.
    static member OptionHasResolutionDate(state: int) =
        match OptionState.ofCode state with
        | Some state -> OptionState.hasResolutionDate state
        | None -> false

    // ------------------------------------------------------------ obligations

    /// Every obligation state this build knows, as persisted values.
    static member KnownObligationStates() =
        ObligationState.all |> List.map ObligationState.code |> Array.ofList

    /// Every obligation trigger this build knows, as persisted values.
    static member KnownObligationTriggers() =
        ObligationTrigger.all |> List.map ObligationTrigger.code |> Array.ofList

    /// <summary>The state an obligation reaches, or 0 when the trigger is illegal there.</summary>
    static member NextObligationState(state: int, trigger: int) =
        match ObligationState.ofCode state, ObligationTrigger.ofCode trigger with
        | Some state, Some trigger ->
            match ObligationState.apply state trigger with
            | Ok next -> ObligationState.code next
            | Error _ -> 0
        | _ -> 0

    static member ObligationPermits(state: int, trigger: int) =
        DealRules.NextObligationState(state, trigger) <> 0

    /// Whether the obligation still counts as outstanding.
    static member IsObligationOutstanding(state: int) =
        match ObligationState.ofCode state with
        | Some state -> ObligationState.isOutstanding state
        | None -> false

    /// Whether this state carries a date recording when the obligation was resolved.
    static member ObligationHasResolutionDate(state: int) =
        match ObligationState.ofCode state with
        | Some state -> ObligationState.hasResolutionDate state
        | None -> false

    /// <summary>Whether an obligation in this state can be past due at all.</summary>
    /// <remarks>
    /// Only an outstanding one. A satisfied obligation whose date has passed is
    /// done, not overdue, and a work queue that said otherwise would send somebody
    /// chasing delivered work.
    /// </remarks>
    static member ObligationCanBeOverdue(state: int) =
        match ObligationState.ofCode state with
        | Some state -> ObligationState.canBeOverdue state
        | None -> false

    // ----------------------------------------------------------------- rights

    /// Every exclusivity value this build knows, as persisted values.
    static member KnownExclusivities() =
        Exclusivity.all |> List.map Exclusivity.code |> Array.ofList

    /// Every grant period kind this build knows.
    static member KnownPeriodKinds() = GrantPeriod.allKinds |> Array.ofList

    /// Whether an exclusivity value would exclude other holders of the same right.
    static member ExcludesOthers(exclusivity: int) =
        match Exclusivity.ofCode exclusivity with
        | Some value -> Exclusivity.excludesOthers value
        | None -> false

    /// <summary>Validates a grant period.</summary>
    /// <returns>Null when the period is well formed; otherwise why it is not.</returns>
    static member DescribePeriodProblem(period: GrantPeriodInput) =
        match GrantPeriod.parse period with
        | Ok _ -> null
        | Error error -> Messages.describeRights error

    /// Whether a grant period is well formed.
    static member IsPeriodValid(period: GrantPeriodInput) = GrantPeriod.isValid period

    /// <summary>Whether a grant is running on a given date.</summary>
    /// <remarks>False for an unstated period: the contract did not say.</remarks>
    static member PeriodCoversOn(period: GrantPeriodInput, on: DateOnly) =
        match GrantPeriod.parse period with
        | Ok parsed -> GrantPeriod.coversOn on parsed
        | Error _ -> false

    /// <summary>Whether two grant periods share any day.</summary>
    /// <remarks>
    /// Reports apparent overlap between recorded grants. It does not decide that
    /// either is invalid: which prevails is a legal question about instruments the
    /// system has not read.
    /// </remarks>
    static member PeriodsOverlap(left: GrantPeriodInput, right: GrantPeriodInput) =
        match GrantPeriod.parse left, GrantPeriod.parse right with
        | Ok left, Ok right -> GrantPeriod.overlaps left right
        | _ -> false

    // -------------------------------------------------------------- deadlines

    /// Every anchor event this build knows, as persisted values.
    static member KnownAnchorEvents() =
        AnchorEvent.all |> List.map AnchorEvent.code |> Array.ofList

    /// Every offset unit this build knows, as persisted values.
    static member KnownOffsetUnits() =
        OffsetUnit.all |> List.map OffsetUnit.code |> Array.ofList

    /// Every calendar basis this build knows, as persisted values.
    static member KnownCalendarBases() =
        CalendarBasis.all |> List.map CalendarBasis.code |> Array.ofList

    /// Every deadline rule kind this build knows.
    static member KnownDeadlineKinds() = DeadlineRule.allKinds |> Array.ofList

    /// Whether this build can compute a date on this calendar basis.
    static member IsCalendarBasisComputable(basis: int) =
        match CalendarBasis.ofCode basis with
        | Some basis -> CalendarBasis.isComputable basis
        | None -> false

    /// Whether a deadline rule is well formed.
    static member IsDeadlineRuleValid(rule: DeadlineRuleInput) = DeadlineRule.isValid rule

    /// <summary>The anchor a rule is measured from, or 0 when it has none.</summary>
    static member DeadlineAnchorOf(rule: DeadlineRuleInput) =
        match DeadlineRule.parse rule with
        | Some parsed ->
            match DeadlineRule.anchorOf parsed with
            | Some anchor -> AnchorEvent.code anchor
            | None -> 0
        | None -> 0

    /// <summary>
    /// Resolves a deadline rule to a date, or returns null when it cannot be.
    /// </summary>
    /// <remarks>
    /// Null is the honest answer three ways: no structured rule, an anchor whose
    /// date is unknown, and a business-day count this build will not approximate.
    /// <c>DescribeDeadlineProblem</c> says which.
    /// </remarks>
    static member ResolveDeadline(rule: DeadlineRuleInput, anchorDate: Nullable<DateOnly>) =
        match DeadlineRule.parse rule with
        | None -> Nullable()
        | Some parsed ->
            let anchor = if anchorDate.HasValue then Some anchorDate.Value else None

            match DeadlineRule.resolve anchor parsed with
            | Ok on -> Nullable on
            | Error _ -> Nullable()

    /// <summary>Why a deadline could not be resolved, or null when it was.</summary>
    static member DescribeDeadlineProblem(rule: DeadlineRuleInput, anchorDate: Nullable<DateOnly>) =
        match DeadlineRule.parse rule with
        | None -> "That deadline rule is not one this build understands."
        | Some parsed ->
            let anchor = if anchorDate.HasValue then Some anchorDate.Value else None

            match DeadlineRule.resolve anchor parsed with
            | Ok _ -> null
            | Error reason -> Messages.describeDeadline reason

    /// Whether a resolved deadline is behind us.
    static member IsDeadlinePast(today: DateOnly, deadline: DateOnly) =
        DeadlineRule.isPast today deadline

    /// Days remaining before a deadline. Negative once it has passed.
    static member DaysUntilDeadline(today: DateOnly, deadline: DateOnly) =
        DeadlineRule.daysUntil today deadline

    // --------------------------------------------------------- reconciliation

    /// <summary>Compares an accepted offer's terms against a contract version's.</summary>
    static member Reconcile(negotiated: TermInput[], contracted: TermInput[]) =
        Reconciliation.compare negotiated contracted

    /// The lines where the contract does not simply say what was agreed.
    static member ReconciliationDifferences(lines: ReconciliationLine[]) =
        Reconciliation.differences lines

    /// How many lines need somebody to look at them.
    static member ReconciliationDifferenceCount(lines: ReconciliationLine[]) =
        Reconciliation.differenceCount lines

    /// Whether the draft says exactly what was negotiated.
    static member IsDraftFaithful(lines: ReconciliationLine[]) = Reconciliation.isFaithful lines

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

    static member describeRights(error: RightsError) =
        match error with
        | PeriodEndsBeforeItStarts(starts, ends) ->
            // Rendered round-trip rather than by the ambient culture: this text is
            // read by whoever is fixing the row, and a date whose day and month
            // could swap depending on the server locale is not a fix.
            let from = starts.ToString("O")
            let until = ends.ToString("O")
            $"A grant cannot end on {until}, before it starts on {from}."
        | UnknownPeriodKind kind -> $"Grant period kind {kind} is not one this build understands."
        | PeriodMissingDate kind -> $"A grant period of kind {kind} must state the date it runs from."
        | UnknownExclusivity value -> $"Exclusivity {value} is not one this build understands."

    static member describeDeadline(reason: DeadlineUnresolved) =
        match reason with
        | NoStructuredRule ->
            "The contract states no deadline this build can structure, so no date is computed."
        | AnchorDateUnknown anchor ->
            $"This deadline is measured from {anchor}, which has not happened yet, "
            + "so no date is computed."
        | NoBusinessDayCalendar ->
            "This deadline counts business days. AgencyOS holds no holiday calendar, so it "
            + "records the rule and computes no date rather than counting calendar days instead."

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
