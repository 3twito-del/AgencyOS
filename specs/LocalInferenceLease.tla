---------------------------- MODULE LocalInferenceLease ----------------------------
(***************************************************************************)
(* The AgencyOS device-local inference lease protocol (M13, ADR-0035).     *)
(*                                                                         *)
(* WHY THIS EXISTS                                                         *)
(*                                                                         *)
(* Through M12 every model call happened inside the server process. The    *)
(* context was assembled, sent and answered without leaving a boundary     *)
(* AgencyOS controls, so the only interesting interleaving was the one     *)
(* between a person approving an action and that action running - which    *)
(* AiApproval.tla already covers.                                          *)
(*                                                                         *)
(* Device-local inference introduces a genuinely distributed step. The     *)
(* server assembles authorized context, hands it to a Windows process it   *)
(* does not control, and later accepts a result from that process. Between *)
(* those two moments the user's permission can be revoked, the run can be  *)
(* cancelled, the lease can expire, the client can crash, and the client   *)
(* can send the same result twice.                                         *)
(*                                                                         *)
(* WHAT IS MODELLED                                                        *)
(*                                                                         *)
(*   - the run, server-canonical                                           *)
(*   - the lease, and its one-time consumption                             *)
(*   - context disclosure to the device                                    *)
(*   - local execution, which may succeed, fail or never answer            *)
(*   - the result coming back, including twice                             *)
(*   - permission revocation at any point                                  *)
(*   - cancellation at any point                                           *)
(*   - expiry                                                              *)
(*   - the canonical effect, counted                                       *)
(*   - residency, and the cloud path the client might reach for            *)
(*                                                                         *)
(* WHAT IS NOT MODELLED, DELIBERATELY                                      *)
(*                                                                         *)
(* The language model, for the reason AiApproval gives: it is untrusted    *)
(* input, and a specification of untrusted input is a specification of     *)
(* "anything". The client's result is modelled as an arbitrary value that  *)
(* the server validates, which is exactly how the code treats it.          *)
(*                                                                         *)
(* Also not modelled: the approval protocol itself. M12 proved it, and     *)
(* re-proving it here would double the state space to re-establish         *)
(* properties that already hold. The canonical effect is modelled as one   *)
(* step so that the lease properties can be stated about it.               *)
(***************************************************************************)

EXTENDS Naturals

CONSTANTS
    MaxReturns      \* how many times the client may return a result

ASSUME MaxReturns \in Nat \ {0}

VARIABLES
    run,            \* the run's canonical state
    lease,          \* the lease's canonical state
    disclosed,      \* whether authorized context has reached the device
    localResult,    \* what the device produced, if anything
    presented,      \* the identity the client presents with its result
    permitted,      \* whether the user still holds the grant
    expired,        \* whether the lease window has passed
    effects,        \* accepted results committed to the run
    returns,        \* result submissions consumed
    sentToCloud     \* whether context ever reached an external provider

vars == << run, lease, disclosed, localResult, presented, permitted, expired,
           effects, returns, sentToCloud >>

(***************************************************************************)
(* States, matching AgentRunStatus and AiContextLeaseState exactly. A model *)
(* that names different states than the code is a model of nothing.        *)
(***************************************************************************)
RunStates == {
    "Queued",
    "AwaitingLocalExecution",
    "Completed",
    "Failed",
    "Cancelled"
}

LeaseStates == {"None", "Issued", "Consumed", "Invalidated"}

RunTerminal == {"Completed", "Failed", "Cancelled"}

(***************************************************************************)
(* What the client presents when it returns a result.                      *)
(*                                                                         *)
(* "Matching" is the honest case: the same tenant, user, run, residency and *)
(* context fingerprint the lease was issued against. "Mismatched" stands   *)
(* for every substitution at once - another tenant, another user, another  *)
(* run, another subject, altered context - because the server's check is a *)
(* single equality and modelling five separate wrong values would multiply *)
(* states without reaching a transition the model does not already reach.  *)
(***************************************************************************)
Presentations == {"None", "Matching", "Mismatched"}

Results == {"None", "Text", "LocalFailure"}

TypeOK ==
    /\ run \in RunStates
    /\ lease \in LeaseStates
    /\ disclosed \in BOOLEAN
    /\ localResult \in Results
    /\ presented \in Presentations
    /\ permitted \in BOOLEAN
    /\ expired \in BOOLEAN
    /\ effects \in Nat
    /\ returns \in 0..MaxReturns
    /\ sentToCloud \in BOOLEAN

Init ==
    /\ run = "Queued"
    /\ lease = "None"
    /\ disclosed = FALSE
    /\ localResult = "None"
    /\ presented = "None"
    /\ permitted = TRUE
    /\ expired = FALSE
    /\ effects = 0
    /\ returns = 0
    /\ sentToCloud = FALSE

-----------------------------------------------------------------------------
(***************************************************************************)
(* THE SERVER                                                              *)
(***************************************************************************)

(***************************************************************************)
(* Issues a lease and discloses the assembled context.                     *)
(*                                                                         *)
(* One step, because that is what the code does: the endpoint that issues  *)
(* the lease is the endpoint that returns the context. Splitting them would *)
(* model an interval that does not exist.                                  *)
(*                                                                         *)
(* Guarded on current permission. A run whose user lost the grant before   *)
(* issuance discloses nothing - which is the one revocation case AgencyOS  *)
(* can actually honour completely (C.1, C.2).                              *)
(***************************************************************************)
IssueLease ==
    /\ run = "Queued"
    /\ lease = "None"
    /\ permitted
    /\ ~expired
    /\ lease' = "Issued"
    /\ disclosed' = TRUE
    /\ run' = "AwaitingLocalExecution"
    /\ UNCHANGED << localResult, presented, permitted, expired, effects, returns,
                    sentToCloud >>

(***************************************************************************)
(* Accepts a returned result.                                              *)
(*                                                                         *)
(* Six things are re-established, none trusted from issuance: the lease is *)
(* still issued, what the client presents matches what the lease was issued *)
(* against, the window has not passed, the run is not terminal, the user   *)
(* still holds the grant, and the result is one the server can use.        *)
(***************************************************************************)
AcceptResult ==
    /\ returns < MaxReturns
    /\ returns' = returns + 1
    /\ lease = "Issued"
    /\ presented = "Matching"
    /\ localResult = "Text"
    /\ ~expired
    /\ permitted
    /\ run = "AwaitingLocalExecution"
    /\ lease' = "Consumed"
    /\ effects' = effects + 1
    /\ run' = "Completed"
    /\ UNCHANGED << disclosed, localResult, presented, permitted, expired,
                    sentToCloud >>

(***************************************************************************)
(* Refuses a returned result, consuming an attempt and changing nothing.   *)
(*                                                                         *)
(* Its own step so that a retry after refusal is a real interleaving rather *)
(* than an assumption. A client that keeps returning must not eventually    *)
(* get through.                                                            *)
(***************************************************************************)
RefuseResult ==
    /\ returns < MaxReturns
    /\ returns' = returns + 1
    /\ presented /= "None"
    /\ \/ lease /= "Issued"
       \/ presented /= "Matching"
       \/ localResult /= "Text"
       \/ expired
       \/ ~permitted
       \/ run \notin {"AwaitingLocalExecution"}
    /\ UNCHANGED << run, lease, disclosed, localResult, presented, permitted,
                    expired, effects, sentToCloud >>

(***************************************************************************)
(* The client reported that it could not run the model.                    *)
(*                                                                         *)
(* THE NO-FALLBACK STEP. The run fails and the lease is spent. There is    *)
(* deliberately no transition anywhere in this module from a local failure  *)
(* to cloud execution: crossing a residency boundary would move material    *)
(* the policy permitted only on-device, and it would do it invisibly (E).  *)
(***************************************************************************)
AcceptLocalFailure ==
    /\ returns < MaxReturns
    /\ returns' = returns + 1
    /\ lease = "Issued"
    /\ presented = "Matching"
    /\ localResult = "LocalFailure"
    /\ run = "AwaitingLocalExecution"
    /\ lease' = "Consumed"
    /\ run' = "Failed"
    /\ UNCHANGED << disclosed, localResult, presented, permitted, expired,
                    effects, sentToCloud >>

-----------------------------------------------------------------------------
(***************************************************************************)
(* THE DEVICE                                                              *)
(***************************************************************************)

(***************************************************************************)
(* Runs the model, or fails to. What comes back is untrusted.              *)
(***************************************************************************)
ExecuteLocally ==
    /\ disclosed
    /\ localResult = "None"
    /\ localResult' \in {"Text", "LocalFailure"}
    /\ UNCHANGED << run, lease, disclosed, presented, permitted, expired,
                    effects, returns, sentToCloud >>

(***************************************************************************)
(* Returns something to the server.                                        *)
(*                                                                         *)
(* The client chooses what to present, including a mismatched identity.    *)
(* Modelled as a free choice because the client is not trusted: nothing in *)
(* the protocol depends on it presenting honestly, and the properties below *)
(* have to hold when it does not.                                          *)
(***************************************************************************)
PresentResult ==
    /\ localResult /= "None"
    /\ presented' \in {"Matching", "Mismatched"}
    /\ UNCHANGED << run, lease, disclosed, localResult, permitted, expired,
                    effects, returns, sentToCloud >>

-----------------------------------------------------------------------------
(***************************************************************************)
(* THE ENVIRONMENT                                                         *)
(***************************************************************************)

(***************************************************************************)
(* The lease window passes. Nobody did this.                               *)
(***************************************************************************)
Expire ==
    /\ ~expired
    /\ expired' = TRUE
    /\ UNCHANGED << run, lease, disclosed, localResult, presented, permitted,
                    effects, returns, sentToCloud >>

(***************************************************************************)
(* The user's grant is taken away.                                         *)
(*                                                                         *)
(* THE CASE THAT CANNOT BE FULLY HONOURED. Revocation after disclosure     *)
(* does not reach into the device's memory: 'disclosed' deliberately stays  *)
(* TRUE. The specification models what AgencyOS can actually enforce -      *)
(* that no further effect follows - rather than pretending the material can *)
(* be recalled (C.3).                                                      *)
(***************************************************************************)
Revoke ==
    /\ permitted
    /\ permitted' = FALSE
    /\ UNCHANGED << run, lease, disclosed, localResult, presented, expired,
                    effects, returns, sentToCloud >>

(***************************************************************************)
(* The run is cancelled. Whatever has not happened is abandoned; whatever  *)
(* has happened stays happened.                                            *)
(***************************************************************************)
Cancel ==
    /\ run \notin RunTerminal
    /\ run' = "Cancelled"
    /\ lease' = IF lease = "Issued" THEN "Invalidated" ELSE lease
    /\ UNCHANGED << disclosed, localResult, presented, permitted, expired,
                    effects, returns, sentToCloud >>

Next ==
    \/ IssueLease
    \/ ExecuteLocally
    \/ PresentResult
    \/ AcceptResult
    \/ AcceptLocalFailure
    \/ RefuseResult
    \/ Expire
    \/ Revoke
    \/ Cancel

(***************************************************************************)
(* Weak fairness on the steps that make progress on their own. Expiry is   *)
(* the passage of time; the device eventually computes something; the      *)
(* server eventually answers what is presented to it.                      *)
(*                                                                         *)
(* Deliberately NOT fair: the client returning a result. AgencyOS does not *)
(* assume a workstation ever comes back, and a liveness property resting on *)
(* that would be a specification of somebody else's process.               *)
(***************************************************************************)
Spec ==
    Init
    /\ [][Next]_vars
    /\ WF_vars(ExecuteLocally)
    /\ WF_vars(AcceptResult)
    /\ WF_vars(AcceptLocalFailure)
    /\ WF_vars(RefuseResult)
    /\ WF_vars(Expire)

-----------------------------------------------------------------------------
(***************************************************************************)
(* SAFETY                                                                  *)
(***************************************************************************)

(***************************************************************************)
(* 1. No effect without the user's current permission.                     *)
(*                                                                         *)
(* Stated about the transition rather than the state afterwards: a grant   *)
(* changed the next day does not make yesterday's accepted result wrong.   *)
(***************************************************************************)
NoEffectWithoutCurrentPermission ==
    [][(effects' > effects) => permitted]_vars

(***************************************************************************)
(* 2. No effect without a valid lease.                                     *)
(***************************************************************************)
NoEffectWithoutValidLease ==
    [][(effects' > effects) => (lease = "Issued")]_vars

(***************************************************************************)
(* 3-6. A lease cannot cross a tenant, a user, a run or a subject.         *)
(*                                                                         *)
(* All four are the same check in the code - one equality over the         *)
(* fingerprint and the identity - so they are one property here. A         *)
(* mismatched presentation never produces an effect.                       *)
(***************************************************************************)
MismatchedPresentationNeverTakesEffect ==
    [][(effects' > effects) => (presented = "Matching")]_vars

(***************************************************************************)
(* 7. An expired lease cannot authorize a new effect.                      *)
(***************************************************************************)
ExpiredLeaseCannotAuthorize ==
    [][(effects' > effects) => ~expired]_vars

(***************************************************************************)
(* 8 and 9. THE ONE THAT MATTERS. One lease produces at most one effect.   *)
(*                                                                         *)
(* Consumption is what makes a replayed result harmless, and a replay is   *)
(* what a client does when a response is lost - so this would fail in      *)
(* production long before anybody noticed in testing.                      *)
(***************************************************************************)
AtMostOneEffect == effects <= 1

(***************************************************************************)
(* 10. A cancelled run creates nothing further.                            *)
(***************************************************************************)
CancelledRunCreatesNothing ==
    [][(effects' > effects) => (run /= "Cancelled")]_vars

(***************************************************************************)
(* 11. Cancellation never erases what was already committed.               *)
(***************************************************************************)
CancellationNeverErasesEffects == [][effects' >= effects]_vars

(***************************************************************************)
(* 12. Local-only never falls back to the cloud.                           *)
(*                                                                         *)
(* Trivially true here, and stated anyway. The specification contains no   *)
(* transition that sets sentToCloud, because the implementation contains no *)
(* path that would - and an invariant nobody can violate is exactly how you *)
(* find out when somebody adds one.                                        *)
(***************************************************************************)
LocalOnlyNeverFallsBackToCloud == ~sentToCloud

(***************************************************************************)
(* 13. The client's result is never authority by itself.                   *)
(*                                                                         *)
(* An effect requires the server to have accepted, which requires the lease *)
(* and the presentation and the permission. A returned result alone changes *)
(* nothing.                                                                *)
(***************************************************************************)
ClientResultIsNeverAuthorityAlone ==
    (effects > 0) => (lease = "Consumed")

(***************************************************************************)
(* 14. Changed context cannot reuse a lease.                               *)
(*                                                                         *)
(* Carried by the fingerprint, which is what "Mismatched" stands for.      *)
(***************************************************************************)
ChangedContextCannotReuseLease ==
    (presented = "Mismatched") => (effects = 0 \/ lease = "Consumed")

(***************************************************************************)
(* Disclosure only ever follows a lease. There is no path that hands       *)
(* context to the device without one.                                      *)
(***************************************************************************)
NoDisclosureWithoutLease == disclosed => (lease /= "None")

Safety ==
    /\ TypeOK
    /\ AtMostOneEffect
    /\ LocalOnlyNeverFallsBackToCloud
    /\ ClientResultIsNeverAuthorityAlone
    /\ ChangedContextCannotReuseLease
    /\ NoDisclosureWithoutLease

-----------------------------------------------------------------------------
(***************************************************************************)
(* LIVENESS                                                                *)
(*                                                                         *)
(* A disclosed lease always stops being usable. Either it is consumed, or  *)
(* the run is cancelled and it is invalidated, or the window closes - and   *)
(* the last of those needs nobody to do anything, which is the point.      *)
(*                                                                         *)
(* Deliberately NOT claimed: that the run reaches a terminal state. It does *)
(* not. A workstation that takes the context and never comes back leaves    *)
(* the run in AwaitingLocalExecution for ever, because AgencyOS cannot make *)
(* somebody else's process answer. The lease expiring is what makes that    *)
(* harmless, and LocalExecutionAbandoned exists in the domain for a sweeper *)
(* that does not exist yet. Recording that is better than inventing         *)
(* machinery to satisfy a property nobody needs.                            *)
(***************************************************************************)
LeaseAlwaysStopsAuthorizing ==
    (lease = "Issued") ~> (lease /= "Issued" \/ expired)

=============================================================================
