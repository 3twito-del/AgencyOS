-------------------------------- MODULE OutboundSend --------------------------------
(***************************************************************************)
(* The AgencyOS outbound send protocol (M10, ADR-0028).                    *)
(*                                                                         *)
(* WHY THIS EXISTS                                                         *)
(*                                                                         *)
(* Everything AgencyOS did before M10 was a database write. This is the    *)
(* first operation that causes something irreversible outside it: a real   *)
(* message, to a real person, that cannot be taken back.                   *)
(*                                                                         *)
(* The hard case is not failure. It is a provider that accepted a send and *)
(* then failed to say so - a timeout, a dropped connection, a process      *)
(* killed between the call and the record. AgencyOS cannot tell that apart *)
(* from a send that never happened, and the two demand opposite responses. *)
(* A system that guesses "failed" and retries sends a client the same      *)
(* commercial email twice, and nobody finds out from the system.           *)
(*                                                                         *)
(* So the protocol never guesses. It records the intent to send BEFORE     *)
(* calling the provider, and when the answer is missing it says so and     *)
(* goes and looks.                                                         *)
(*                                                                         *)
(* WHAT IS MODELLED                                                        *)
(*                                                                         *)
(*   - the canonical dispatch row in PostgreSQL                            *)
(*   - the provider's own state: draft present, message committed          *)
(*   - a worker that crashes at any point, including mid-call              *)
(*   - acknowledgements that arrive and acknowledgements that are lost     *)
(*   - reconciliation, which answers found / absent / cannot tell          *)
(*                                                                         *)
(* WHAT IS NOT MODELLED, DELIBERATELY                                      *)
(*                                                                         *)
(* Inbox synchronization. It is idempotent by construction - every write   *)
(* is keyed on the provider's message id within the account - and          *)
(* modelling it would spend effort where there is no external side effect  *)
(* to get wrong.                                                           *)
(***************************************************************************)

EXTENDS Naturals, FiniteSets

CONSTANTS
    MaxAttempts     \* how many times a pre-commit failure may be retried

ASSUME MaxAttempts \in Nat \ {0}

VARIABLES
    state,          \* the canonical dispatch state in PostgreSQL
    draftExists,    \* whether a draft exists at the provider
    committed,      \* whether the provider has actually sent the message
    delivered,      \* how many external messages the recipient has received
    attempts,       \* pre-commit attempts consumed
    leased          \* whether a worker currently holds the row

vars == << state, draftExists, committed, delivered, attempts, leased >>

(***************************************************************************)
(* The states, matching OutboundDispatchState in the domain exactly.       *)
(* Keeping the names identical is deliberate: a model that describes       *)
(* different states than the code is a model of nothing.                   *)
(***************************************************************************)
States == {
    "Draft",
    "Queued",
    "ProviderDraftCreated",
    "SendRequested",
    "Sent",
    "FailedRetryable",
    "FailedPermanent",
    "UnknownOutcome",
    "Cancelled"
}

Terminal == {"Sent", "FailedPermanent", "Cancelled"}

TypeOK ==
    /\ state \in States
    /\ draftExists \in BOOLEAN
    /\ committed \in BOOLEAN
    /\ delivered \in Nat
    /\ attempts \in 0..MaxAttempts
    /\ leased \in BOOLEAN

Init ==
    /\ state = "Draft"
    /\ draftExists = FALSE
    /\ committed = FALSE
    /\ delivered = 0
    /\ attempts = 0
    /\ leased = FALSE

-----------------------------------------------------------------------------
(***************************************************************************)
(* A person submits the message. The last point at which nothing has been  *)
(* asked of the provider, and therefore the last point at which cancelling *)
(* is possible.                                                            *)
(***************************************************************************)
Queue ==
    /\ state = "Draft"
    /\ state' = "Queued"
    /\ UNCHANGED << draftExists, committed, delivered, attempts, leased >>

Cancel ==
    /\ state \in {"Draft", "Queued"}
    /\ state' = "Cancelled"
    /\ UNCHANGED << draftExists, committed, delivered, attempts, leased >>

(***************************************************************************)
(* A worker claims the row. One at a time: the claim is a row lock with an *)
(* expiring lease, so two workers reaching for the same dispatch produce   *)
(* one winner and one worker that moves on.                                *)
(*                                                                         *)
(* There is no free-standing release. A worker gives the claim back as     *)
(* part of completing a step, exactly as the code does, or it dies and the *)
(* lease lapses. Releasing without doing anything is not a behaviour the   *)
(* implementation has, and modelling it let the checker satisfy fairness   *)
(* by claiming and releasing for ever while the message went nowhere.      *)
(***************************************************************************)
Claim ==
    /\ ~leased
    /\ state \in {"Queued", "ProviderDraftCreated", "SendRequested",
                  "FailedRetryable", "UnknownOutcome"}
    /\ leased' = TRUE
    /\ UNCHANGED << state, draftExists, committed, delivered, attempts >>

(***************************************************************************)
(* Creating the provider draft. Repeatable and invisible to anybody        *)
(* outside, which is exactly why the protocol does it before committing to *)
(* anything: it buys an identifier that makes recovery possible.           *)
(***************************************************************************)
CreateDraft ==
    /\ leased
    /\ state \in {"Queued", "FailedRetryable"}
    /\ attempts < MaxAttempts
    /\ draftExists' = TRUE
    /\ state' = "ProviderDraftCreated"
    /\ leased' = FALSE
    /\ UNCHANGED << committed, delivered, attempts >>

CreateDraftFails ==
    /\ leased
    /\ state \in {"Queued", "FailedRetryable"}
    /\ attempts < MaxAttempts
    /\ attempts' = attempts + 1
    /\ state' = "FailedRetryable"
    /\ leased' = FALSE
    /\ UNCHANGED << draftExists, committed, delivered >>

GiveUp ==
    /\ leased
    /\ state \in {"Queued", "FailedRetryable", "ProviderDraftCreated"}
    /\ attempts >= MaxAttempts
    /\ state' = "FailedPermanent"
    /\ leased' = FALSE
    /\ UNCHANGED << draftExists, committed, delivered, attempts >>

-----------------------------------------------------------------------------
(***************************************************************************)
(* THE CRITICAL STEP.                                                      *)
(*                                                                         *)
(* The state is written and COMMITTED before the provider is called. This  *)
(* single ordering is what makes the protocol safe: after any crash, a row *)
(* in SendRequested tells the recovering worker that a send may already    *)
(* have happened, and it reconciles instead of sending.                    *)
(*                                                                         *)
(* Written the other way round - call first, record after - a crash        *)
(* between the two leaves a row saying nothing was sent while the provider *)
(* is already sending it. The next worker sends it again.                  *)
(***************************************************************************)
RequestSend ==
    /\ leased
    /\ state = "ProviderDraftCreated"
    /\ draftExists
    /\ attempts < MaxAttempts
    /\ attempts' = attempts + 1
    /\ state' = "SendRequested"
    /\ UNCHANGED << draftExists, committed, delivered, leased >>

(***************************************************************************)
(* The provider commits and answers. The ordinary case.                    *)
(***************************************************************************)
ProviderAccepts ==
    /\ leased
    /\ state = "SendRequested"
    /\ ~committed
    /\ committed' = TRUE
    /\ delivered' = delivered + 1
    /\ draftExists' = FALSE          \* the provider consumes the draft
    /\ state' = "Sent"
    /\ leased' = FALSE
    /\ UNCHANGED << attempts >>

(***************************************************************************)
(* The provider commits and the acknowledgement is lost. The message HAS   *)
(* gone; AgencyOS cannot prove it. This is the case the whole design is    *)
(* built around.                                                           *)
(***************************************************************************)
ProviderAcceptsAcknowledgementLost ==
    /\ leased
    /\ state = "SendRequested"
    /\ ~committed
    /\ committed' = TRUE
    /\ delivered' = delivered + 1
    /\ draftExists' = FALSE
    /\ state' = "UnknownOutcome"
    /\ leased' = FALSE
    /\ UNCHANGED << attempts >>

(***************************************************************************)
(* The provider states a refusal before committing. Safe: it said it did   *)
(* not send, so returning to a sendable state cannot duplicate anything.   *)
(***************************************************************************)
ProviderRefuses ==
    /\ leased
    /\ state = "SendRequested"
    /\ ~committed
    /\ state' = "FailedRetryable"
    /\ leased' = FALSE
    /\ UNCHANGED << draftExists, committed, delivered, attempts >>

(***************************************************************************)
(* No answer at all, and nothing was committed. Indistinguishable at the   *)
(* time from the lost-acknowledgement case above, which is precisely why   *)
(* both go to UnknownOutcome rather than to a failure.                     *)
(***************************************************************************)
ProviderTimesOut ==
    /\ leased
    /\ state = "SendRequested"
    /\ ~committed
    /\ state' = "UnknownOutcome"
    /\ leased' = FALSE
    /\ UNCHANGED << draftExists, committed, delivered, attempts >>

-----------------------------------------------------------------------------
(***************************************************************************)
(* A worker dies. The lease lapses and another worker picks the row up in  *)
(* whatever state it was left. Nothing in memory survives; everything that *)
(* matters is the row.                                                     *)
(*                                                                         *)
(* Note that a crash in SendRequested leaves the row in SendRequested,     *)
(* which is exactly the point of writing it first.                         *)
(***************************************************************************)
WorkerCrashes ==
    /\ leased
    /\ leased' = FALSE
    /\ UNCHANGED << state, draftExists, committed, delivered, attempts >>

(***************************************************************************)
(* Recovery from a crash mid-send: the row says a send may have happened,  *)
(* so the worker moves it to UnknownOutcome and reconciles. It never sends *)
(* again on the strength of not knowing.                                   *)
(***************************************************************************)
RecoverInFlight ==
    /\ leased
    /\ state = "SendRequested"
    /\ state' = "UnknownOutcome"
    /\ leased' = FALSE
    /\ UNCHANGED << draftExists, committed, delivered, attempts >>

-----------------------------------------------------------------------------
(***************************************************************************)
(* RECONCILIATION: the only route out of UnknownOutcome, and it moves only *)
(* on evidence.                                                            *)
(***************************************************************************)

(* The message is in the provider's sent items. It went. *)
ReconcileFound ==
    /\ leased
    /\ state = "UnknownOutcome"
    /\ committed
    /\ state' = "Sent"
    /\ leased' = FALSE
    /\ UNCHANGED << draftExists, committed, delivered, attempts >>

(***************************************************************************)
(* The message is absent AND the draft is still there. Together these      *)
(* prove it did not go, which is what makes a retry safe. Absence alone    *)
(* proves nothing: a provider that lost the draft and sent the message     *)
(* would look the same.                                                    *)
(***************************************************************************)
ReconcileProvenAbsent ==
    /\ leased
    /\ state = "UnknownOutcome"
    /\ ~committed
    /\ draftExists
    /\ state' = "ProviderDraftCreated"
    /\ leased' = FALSE
    /\ UNCHANGED << draftExists, committed, delivered, attempts >>

(***************************************************************************)
(* A reconciliation that cannot answer is not an action here. It changes   *)
(* nothing, and [][Next]_vars already allows the state to stay where it    *)
(* is - which is precisely the behaviour: the state stays unknown for      *)
(* exactly as long as it is unknown, and a person is told.                 *)
(***************************************************************************)

Next ==
    \/ Queue
    \/ Cancel
    \/ Claim
    \/ CreateDraft
    \/ CreateDraftFails
    \/ GiveUp
    \/ RequestSend
    \/ ProviderAccepts
    \/ ProviderAcceptsAcknowledgementLost
    \/ ProviderRefuses
    \/ ProviderTimesOut
    \/ WorkerCrashes
    \/ RecoverInFlight
    \/ ReconcileFound
    \/ ReconcileProvenAbsent

(***************************************************************************)
(* Everything a worker does once it holds the claim. Grouped so fairness   *)
(* can be stated about progress rather than about activity: a loop of      *)
(* claim-and-crash is activity, and the protocol should still finish.      *)
(***************************************************************************)
Progress ==
    \/ CreateDraft
    \/ CreateDraftFails
    \/ GiveUp
    \/ RequestSend
    \/ ProviderAccepts
    \/ ProviderAcceptsAcknowledgementLost
    \/ ProviderRefuses
    \/ ProviderTimesOut
    \/ RecoverInFlight
    \/ ReconcileFound
    \/ ReconcileProvenAbsent

(***************************************************************************)
(* Strong fairness on progress, weak fairness on claiming. Strong is       *)
(* needed because a crash disables every step momentarily: weak fairness   *)
(* would let a worker claim, die, claim, die for ever and never be         *)
(* required to finish anything. Strong fairness says that a step which is  *)
(* enabled infinitely often eventually runs, which is what a system with a *)
(* worker that mostly works actually does.                                 *)
(***************************************************************************)
Spec ==
    /\ Init
    /\ [][Next]_vars
    /\ WF_vars(Claim)
    /\ SF_vars(Progress)

-----------------------------------------------------------------------------
(***************************************************************************)
(* SAFETY                                                                  *)
(***************************************************************************)

(***************************************************************************)
(* THE ONE THAT MATTERS. One canonical intent produces at most one message *)
(* in the recipient's inbox. If this fails, a client received the same     *)
(* commercial email twice because AgencyOS guessed.                        *)
(***************************************************************************)
NeverSendsTwice == delivered <= 1

(***************************************************************************)
(* Sent is terminal. A confirmed send is never un-confirmed: the recipient *)
(* has the message, and no later code path may say otherwise.              *)
(***************************************************************************)
SentIsMonotonic == [][state = "Sent" => state' = "Sent"]_vars

(***************************************************************************)
(* An unknown outcome is never turned into a failure by assumption. It     *)
(* leaves UnknownOutcome only for Sent (found) or ProviderDraftCreated     *)
(* (proven absent) - never straight to FailedPermanent or FailedRetryable. *)
(***************************************************************************)
UnknownIsNeverAssumedFailed ==
    [][state = "UnknownOutcome" =>
        state' \in {"UnknownOutcome", "Sent", "ProviderDraftCreated"}]_vars

(***************************************************************************)
(* A message the provider actually sent is never reported as failed. This  *)
(* is the mirror of the duplicate: telling somebody a contract was not     *)
(* sent when it was is its own kind of damage.                             *)
(***************************************************************************)
CommittedIsNeverCalledFailed ==
    committed => state \notin {"FailedPermanent", "FailedRetryable"}

(***************************************************************************)
(* A send is only ever requested with a provider draft in hand. Without    *)
(* one there is no identifier to reconcile against later, and the unknown  *)
(* outcome would be unresolvable for ever.                                 *)
(***************************************************************************)
SendRequiresDraft ==
    (state = "SendRequested" /\ ~committed) => draftExists

(***************************************************************************)
(* Cancellation is only possible before anything reached the provider.     *)
(* AgencyOS cannot unsend anything and does not offer to.                  *)
(***************************************************************************)
CancelOnlyBeforeProvider == state = "Cancelled" => ~committed

Safety ==
    /\ TypeOK
    /\ NeverSendsTwice
    /\ CommittedIsNeverCalledFailed
    /\ SendRequiresDraft
    /\ CancelOnlyBeforeProvider

-----------------------------------------------------------------------------
(***************************************************************************)
(* LIVENESS                                                                *)
(*                                                                         *)
(* A queued message eventually settles: sent, permanently failed,          *)
(* cancelled, or sitting in UnknownOutcome for a person to resolve. The    *)
(* last is a real resting place, not a bug: when the provider cannot say   *)
(* what happened, the honest end state is to say so and stop.              *)
(***************************************************************************)
EventuallySettles ==
    (state = "Queued") ~> (state \in Terminal \/ state = "UnknownOutcome")

=============================================================================
