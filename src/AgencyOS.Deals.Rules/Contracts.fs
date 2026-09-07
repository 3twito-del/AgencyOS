namespace AgencyOS.Deals.Rules

/// <summary>
/// Where a contract stands as a legal instrument.
/// </summary>
/// <remarks>
/// <para>
/// Separate from, and downstream of, <see cref="DealState"/>. Terms being agreed
/// is a commercial fact; a contract drafted, signed, fully executed and effective
/// are four further facts, none of which follows from the others. This machine
/// keeps them apart (ADR-0022).
/// </para>
/// <para>
/// There is deliberately no Paid, Invoiced or Commissioned value. Whether money
/// moved is M9's question and M8 has no way to answer it.
/// </para>
/// </remarks>
type ContractState =
    /// Being drafted. Nothing has been circulated for approval.
    | ContractDraft
    /// Circulated for review by counsel or the counterparty.
    | UnderReview
    /// Cleared internally; awaiting signatures.
    | ApprovedForExecution
    /// Some required signatories have signed; not all.
    | PartiallyExecuted
    /// Every required signatory has signed.
    | Executed
    /// Drafting stopped without an agreement. Terminal.
    | Abandoned
    /// Replaced by another instrument. Terminal.
    | Superseded
    /// Ended after execution. Terminal.
    | Terminated

/// <summary>
/// What causes a contract to change state.
/// </summary>
/// <remarks>
/// Named by cause rather than left as a free table, because two of these must not
/// be requestable. Execution follows from recorded signatures, and a status
/// command that could set it would let a contract claim to be executed with
/// nobody's signature behind it - the same shape of invariant M7 protects for
/// agreed terms.
/// </remarks>
type ContractTrigger =
    /// Circulated for review.
    | SentForReview
    /// Returned to drafting.
    | ReturnedToDrafting
    /// Cleared internally for signature.
    | ApprovedForSignature
    /// A required signatory signed, and others remain.
    | SignatureRecorded
    /// The last required signatory signed.
    | ExecutionCompleted
    /// Drafting stopped.
    | DraftingAbandoned
    /// Replaced by another instrument.
    | ReplacedByAnother
    /// Ended after execution.
    | TerminationRecorded

/// <summary>
/// Where a contractual option stands.
/// </summary>
/// <remarks>
/// Every outcome except Available is terminal. An exercised option never returns
/// to available: if a later amendment restores one, that is a new legal fact
/// recorded as a new option, not a rewriting of this one (ADR-0022).
/// </remarks>
type OptionState =
    /// The option exists and has not been resolved.
    | Available
    /// The holder exercised it.
    | Exercised
    /// The holder declined it.
    | Declined
    /// Its stated deadline passed and that was recorded.
    | OptionExpired
    /// The holder gave it up before the deadline.
    | Waived
    /// It was removed by amendment or recorded in error.
    | OptionCancelled

/// <summary>What causes an option to change state.</summary>
type OptionTrigger =
    | Exercise
    | Decline
    | RecordExpiry
    | Waive
    | CancelOption

/// <summary>
/// Where an obligation stands.
/// </summary>
/// <remarks>
/// There is deliberately no Due or Overdue value. Past due is a fact about a date
/// and the current time, derivable from the row, and a stored flag would be wrong
/// from the moment the clock moved. Breach is a different matter: it is a legal
/// conclusion somebody reaches, so it is recorded rather than inferred from a
/// deadline passing (ADR-0022).
/// </remarks>
type ObligationState =
    /// Outstanding. Whether it is past due is derived, not stored.
    | Pending
    /// Done.
    | Satisfied
    /// The obligee gave it up.
    | ObligationWaived
    /// Somebody determined it was breached. Never inferred from a date.
    | Breached
    /// Removed by amendment or recorded in error.
    | ObligationCancelled

/// <summary>What causes an obligation to change state.</summary>
type ObligationTrigger =
    | Satisfy
    | WaiveObligation
    | RecordBreach
    | CancelObligation
    /// A breach determination reversed, or a waiver withdrawn by agreement.
    | Reinstate

/// <summary>
/// Why a legal transition was refused.
/// </summary>
/// <remarks>
/// Declared here rather than beside the commercial errors in States.fs, because
/// F# resolves types in compile order and these cases name states defined in this
/// file. Two small error unions read better than one that had to be split across
/// files anyway.
/// </remarks>
type LegalTransitionError =
    /// The contract cannot do that from where it is.
    | IllegalContractTransition of contractState: ContractState * contractTrigger: ContractTrigger
    /// The option cannot do that from where it is.
    | IllegalOptionTransition of optionState: OptionState * optionTrigger: OptionTrigger
    /// The obligation cannot do that from where it is.
    | IllegalObligationTransition of
        obligationState: ObligationState *
        obligationTrigger: ObligationTrigger

/// <summary>Contract state mapping and transitions.</summary>
module ContractState =

    /// Every state this build knows.
    let all =
        [ ContractDraft
          UnderReview
          ApprovedForExecution
          PartiallyExecuted
          Executed
          Abandoned
          Superseded
          Terminated ]

    /// The persisted value for a state.
    let code state =
        match state with
        | ContractDraft -> 1
        | UnderReview -> 2
        | ApprovedForExecution -> 3
        | PartiallyExecuted -> 4
        | Executed -> 5
        | Abandoned -> 6
        | Superseded -> 7
        | Terminated -> 8

    /// The state for a persisted value, or None when this build does not know it.
    let ofCode value =
        match value with
        | 1 -> Some ContractDraft
        | 2 -> Some UnderReview
        | 3 -> Some ApprovedForExecution
        | 4 -> Some PartiallyExecuted
        | 5 -> Some Executed
        | 6 -> Some Abandoned
        | 7 -> Some Superseded
        | 8 -> Some Terminated
        | _ -> None

    /// Whether the instrument is finished and cannot move.
    let isTerminal state =
        match state with
        | Abandoned | Superseded | Terminated -> true
        | ContractDraft | UnderReview | ApprovedForExecution | PartiallyExecuted | Executed -> false

    /// <summary>Whether every required signatory has signed.</summary>
    /// <remarks>
    /// Fully executed says nothing about effectiveness. A contract can be signed
    /// on one date and effective on another, or effective retroactively, so
    /// effectiveness is recorded separately and derived from its own date.
    /// </remarks>
    let isFullyExecuted state =
        match state with
        | Executed | Superseded | Terminated -> true
        | ContractDraft | UnderReview | ApprovedForExecution | PartiallyExecuted | Abandoned -> false

    /// Whether the drafted text may still be revised.
    let acceptsNewVersions state =
        match state with
        | ContractDraft | UnderReview | ApprovedForExecution | PartiallyExecuted -> true
        | Executed | Abandoned | Superseded | Terminated -> false

    /// <summary>Whether signatures may be recorded while the contract is here.</summary>
    /// <remarks>
    /// A draft has not been cleared for signature and an abandoned instrument is
    /// not being signed. Recording a signature against either would describe
    /// something that did not happen the way the record says.
    /// </remarks>
    let acceptsSignatures state =
        match state with
        | ApprovedForExecution | PartiallyExecuted -> true
        | ContractDraft | UnderReview | Executed | Abandoned | Superseded | Terminated -> false

    /// <summary>The complete transition function.</summary>
    let apply state trigger =
        match state, trigger with
        | ContractDraft, SentForReview -> Ok UnderReview
        | ContractDraft, ApprovedForSignature -> Ok ApprovedForExecution
        | ContractDraft, DraftingAbandoned -> Ok Abandoned

        | UnderReview, ReturnedToDrafting -> Ok ContractDraft
        | UnderReview, ApprovedForSignature -> Ok ApprovedForExecution
        | UnderReview, DraftingAbandoned -> Ok Abandoned

        | ApprovedForExecution, ReturnedToDrafting -> Ok ContractDraft
        | ApprovedForExecution, SentForReview -> Ok UnderReview
        | ApprovedForExecution, SignatureRecorded -> Ok PartiallyExecuted
        | ApprovedForExecution, ExecutionCompleted -> Ok Executed
        | ApprovedForExecution, DraftingAbandoned -> Ok Abandoned

        // Still partially executed after another non-final signature.
        | PartiallyExecuted, SignatureRecorded -> Ok PartiallyExecuted
        | PartiallyExecuted, ExecutionCompleted -> Ok Executed
        | PartiallyExecuted, ReturnedToDrafting -> Ok ContractDraft
        | PartiallyExecuted, DraftingAbandoned -> Ok Abandoned

        | Executed, ReplacedByAnother -> Ok Superseded
        | Executed, TerminationRecorded -> Ok Terminated

        | _ -> Error(IllegalContractTransition(state, trigger))

    /// Whether a trigger is legal from a state.
    let permits state trigger =
        match apply state trigger with
        | Ok _ -> true
        | Error _ -> false

    /// <summary>
    /// Whether a caller may ask for this trigger through a status command.
    /// </summary>
    /// <remarks>
    /// Execution is a consequence of recorded signatures, never a status somebody
    /// sets. Without this a contract could be marked executed with nobody's
    /// signature behind it, which is the one state this milestone exists to
    /// prevent.
    /// </remarks>
    let isCallerRequestable trigger =
        match trigger with
        | SentForReview
        | ReturnedToDrafting
        | ApprovedForSignature
        | DraftingAbandoned
        | ReplacedByAnother
        | TerminationRecorded -> true
        | SignatureRecorded | ExecutionCompleted -> false

/// <summary>Contract trigger mapping.</summary>
module ContractTrigger =

    /// Every trigger this build knows.
    let all =
        [ SentForReview
          ReturnedToDrafting
          ApprovedForSignature
          SignatureRecorded
          ExecutionCompleted
          DraftingAbandoned
          ReplacedByAnother
          TerminationRecorded ]

    /// The persisted value for a trigger.
    let code trigger =
        match trigger with
        | SentForReview -> 1
        | ReturnedToDrafting -> 2
        | ApprovedForSignature -> 3
        | SignatureRecorded -> 4
        | ExecutionCompleted -> 5
        | DraftingAbandoned -> 6
        | ReplacedByAnother -> 7
        | TerminationRecorded -> 8

    /// The trigger for a persisted value, or None when this build does not know it.
    let ofCode value =
        match value with
        | 1 -> Some SentForReview
        | 2 -> Some ReturnedToDrafting
        | 3 -> Some ApprovedForSignature
        | 4 -> Some SignatureRecorded
        | 5 -> Some ExecutionCompleted
        | 6 -> Some DraftingAbandoned
        | 7 -> Some ReplacedByAnother
        | 8 -> Some TerminationRecorded
        | _ -> None

/// <summary>Option state mapping and transitions.</summary>
module OptionState =

    let all = [ Available; Exercised; Declined; OptionExpired; Waived; OptionCancelled ]

    let code state =
        match state with
        | Available -> 1
        | Exercised -> 2
        | Declined -> 3
        | OptionExpired -> 4
        | Waived -> 5
        | OptionCancelled -> 6

    let ofCode value =
        match value with
        | 1 -> Some Available
        | 2 -> Some Exercised
        | 3 -> Some Declined
        | 4 -> Some OptionExpired
        | 5 -> Some Waived
        | 6 -> Some OptionCancelled
        | _ -> None

    /// Whether the option is resolved and can no longer move.
    let isTerminal state =
        match state with
        | Exercised | Declined | OptionExpired | Waived | OptionCancelled -> true
        | Available -> false

    /// Whether the option carries a date recording when it was resolved.
    let hasResolutionDate state =
        match state with
        | Exercised | Declined | OptionExpired | Waived -> true
        | Available | OptionCancelled -> false

    /// <summary>The complete transition function.</summary>
    /// <remarks>
    /// Every route leaves Available and none returns. A later amendment that
    /// restores an option is a new legal fact and a new row.
    /// </remarks>
    let apply state trigger =
        match state, trigger with
        | Available, Exercise -> Ok Exercised
        | Available, Decline -> Ok Declined
        | Available, RecordExpiry -> Ok OptionExpired
        | Available, Waive -> Ok Waived
        | Available, CancelOption -> Ok OptionCancelled
        | _ -> Error(IllegalOptionTransition(state, trigger))

    let permits state trigger =
        match apply state trigger with
        | Ok _ -> true
        | Error _ -> false

/// <summary>Option trigger mapping.</summary>
module OptionTrigger =

    let all = [ Exercise; Decline; RecordExpiry; Waive; CancelOption ]

    let code trigger =
        match trigger with
        | Exercise -> 1
        | Decline -> 2
        | RecordExpiry -> 3
        | Waive -> 4
        | CancelOption -> 5

    let ofCode value =
        match value with
        | 1 -> Some Exercise
        | 2 -> Some Decline
        | 3 -> Some RecordExpiry
        | 4 -> Some Waive
        | 5 -> Some CancelOption
        | _ -> None

/// <summary>Obligation state mapping and transitions.</summary>
module ObligationState =

    let all = [ Pending; Satisfied; ObligationWaived; Breached; ObligationCancelled ]

    let code state =
        match state with
        | Pending -> 1
        | Satisfied -> 2
        | ObligationWaived -> 3
        | Breached -> 4
        | ObligationCancelled -> 5

    let ofCode value =
        match value with
        | 1 -> Some Pending
        | 2 -> Some Satisfied
        | 3 -> Some ObligationWaived
        | 4 -> Some Breached
        | 5 -> Some ObligationCancelled
        | _ -> None

    /// Whether the obligation still counts as outstanding.
    let isOutstanding state =
        match state with
        | Pending | Breached -> true
        | Satisfied | ObligationWaived | ObligationCancelled -> false

    /// Whether the state carries a date recording when it was resolved.
    let hasResolutionDate state =
        match state with
        | Satisfied | ObligationWaived -> true
        | Pending | Breached | ObligationCancelled -> false

    /// <summary>
    /// Whether an obligation in this state can be past due.
    /// </summary>
    /// <remarks>
    /// Only an outstanding one. A satisfied obligation whose date has passed is
    /// not overdue, it is done, and a report that said otherwise would send
    /// somebody chasing work already delivered.
    /// </remarks>
    let canBeOverdue state = isOutstanding state

    /// <summary>The complete transition function.</summary>
    /// <remarks>
    /// A breach determination can be reversed, because it is a judgement somebody
    /// made rather than a fact about a date, and judgements are revisited. A
    /// satisfied obligation is not reinstated: that would mean the delivery
    /// unhappened.
    /// </remarks>
    let apply state trigger =
        match state, trigger with
        | Pending, Satisfy -> Ok Satisfied
        | Pending, WaiveObligation -> Ok ObligationWaived
        | Pending, RecordBreach -> Ok Breached
        | Pending, CancelObligation -> Ok ObligationCancelled

        | Breached, Satisfy -> Ok Satisfied
        | Breached, WaiveObligation -> Ok ObligationWaived
        | Breached, Reinstate -> Ok Pending
        | Breached, CancelObligation -> Ok ObligationCancelled

        | ObligationWaived, Reinstate -> Ok Pending

        | _ -> Error(IllegalObligationTransition(state, trigger))

    let permits state trigger =
        match apply state trigger with
        | Ok _ -> true
        | Error _ -> false

/// <summary>Obligation trigger mapping.</summary>
module ObligationTrigger =

    let all = [ Satisfy; WaiveObligation; RecordBreach; CancelObligation; Reinstate ]

    let code trigger =
        match trigger with
        | Satisfy -> 1
        | WaiveObligation -> 2
        | RecordBreach -> 3
        | CancelObligation -> 4
        | Reinstate -> 5

    let ofCode value =
        match value with
        | 1 -> Some Satisfy
        | 2 -> Some WaiveObligation
        | 3 -> Some RecordBreach
        | 4 -> Some CancelObligation
        | 5 -> Some Reinstate
        | _ -> None
