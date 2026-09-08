-------------------------------- MODULE AiApproval --------------------------------
(***************************************************************************)
(* The AgencyOS AI approval-to-execution protocol (M12, ADR-0031).         *)
(*                                                                         *)
(* WHY THIS EXISTS                                                         *)
(*                                                                         *)
(* M12 is the first milestone in which something other than a person       *)
(* proposes a change to business truth. The protocol that stands between   *)
(* a model's request and a canonical command is the only thing keeping     *)
(* that from being a system that acts on its own.                          *)
(*                                                                         *)
(* Reviewing that protocol by reading the handler is not enough. The       *)
(* handler is correct on the path a test drives; what has to be true is    *)
(* that it is correct under every interleaving of a person deciding, an    *)
(* approval expiring, a permission being revoked, a run being cancelled    *)
(* and a client retrying - all of which happen concurrently, and none of   *)
(* which a test enumerates.                                                *)
(*                                                                         *)
(* WHAT IS MODELLED                                                        *)
(*                                                                         *)
(*   - the tool request row, and the arguments it froze when proposed      *)
(*   - the approval row, and the fingerprint it binds to                   *)
(*   - a person deciding, at any moment, including too late                *)
(*   - expiry, which happens on its own with nobody deciding anything      *)
(*   - permission revocation between the decision and the execution        *)
(*   - run cancellation, at any moment                                     *)
(*   - a client retrying execution, arbitrarily often                      *)
(*   - an attempt to rewrite the proposed arguments after the fact         *)
(*   - the canonical effect itself, counted                                *)
(*                                                                         *)
(* WHAT IS NOT MODELLED, DELIBERATELY                                      *)
(*                                                                         *)
(* The language model. It is untrusted input, and a specification of       *)
(* untrusted input is a specification of "anything". Modelling it would    *)
(* mean writing down assumptions about what a model will and will not do,  *)
(* and the entire design exists because no such assumption is safe. The    *)
(* model appears here only as the origin of a proposal that has already    *)
(* been validated against the registry - which is where it stops being     *)
(* model output and starts being a row.                                    *)
(*                                                                         *)
(* Also not modelled: the read-only tools. They carry no approval, produce *)
(* no canonical effect, and re-check their own permission on every call;   *)
(* there is no interleaving to get wrong.                                  *)
(***************************************************************************)

EXTENDS Naturals

CONSTANTS
    MaxAttempts     \* how many times execution may be attempted

ASSUME MaxAttempts \in Nat \ {0}

VARIABLES
    request,        \* the tool request's status
    approval,       \* the approval's decision
    arguments,      \* which arguments the request currently carries
    approvedArgs,   \* which arguments were in front of the person who decided
    permitted,      \* whether the caller still holds the tool's permission
    expired,        \* whether the approval's window has passed
    cancelled,      \* whether the run was cancelled
    effects,        \* canonical effects committed
    attempts        \* execution attempts consumed

vars == << request, approval, arguments, approvedArgs, permitted, expired,
           cancelled, effects, attempts >>

(***************************************************************************)
(* The statuses, matching ToolRequestStatus and ApprovalDecision in the    *)
(* domain exactly. Keeping the names identical is deliberate: a model that *)
(* describes different states than the code is a model of nothing.         *)
(***************************************************************************)
RequestStatus == {
    "AwaitingApproval",
    "Approved",
    "Rejected",
    "Executed",
    "Expired",
    "Abandoned"
}

Decisions == {"Pending", "Approved", "Rejected", "Expired"}

(***************************************************************************)
(* Arguments are modelled as an opaque token rather than as content. What  *)
(* matters is only whether what is about to run is the same thing that was *)
(* approved, which is exactly what the fingerprint answers - and a         *)
(* fingerprint over content is a function of the content, so two tokens    *)
(* is the whole of the interesting space.                                  *)
(***************************************************************************)
Arguments == {"a", "b"}

RequestTerminal == {"Executed", "Rejected", "Expired", "Abandoned"}

TypeOK ==
    /\ request \in RequestStatus
    /\ approval \in Decisions
    /\ arguments \in Arguments
    /\ approvedArgs \in Arguments \cup {"none"}
    /\ permitted \in BOOLEAN
    /\ expired \in BOOLEAN
    /\ cancelled \in BOOLEAN
    /\ effects \in Nat
    /\ attempts \in 0..MaxAttempts

Init ==
    /\ request = "AwaitingApproval"
    /\ approval = "Pending"
    /\ arguments = "a"
    /\ approvedArgs = "none"
    /\ permitted = TRUE
    /\ expired = FALSE
    /\ cancelled = FALSE
    /\ effects = 0
    /\ attempts = 0

-----------------------------------------------------------------------------
(***************************************************************************)
(* THE ENVIRONMENT                                                         *)
(*                                                                         *)
(* Everything below happens without asking AgencyOS. These are the steps   *)
(* whose interleaving with the decision is what the protocol has to        *)
(* survive.                                                                *)
(***************************************************************************)

(***************************************************************************)
(* The window passes. Nobody did this; it is the absence of a decision,    *)
(* which is why expiry has a moment and no person.                         *)
(***************************************************************************)
Expire ==
    /\ ~expired
    /\ expired' = TRUE
    /\ approval' = IF approval = "Pending" THEN "Expired" ELSE approval
    /\ request' = IF request = "AwaitingApproval" THEN "Expired" ELSE request
    /\ UNCHANGED << arguments, approvedArgs, permitted, cancelled, effects,
                    attempts >>

(***************************************************************************)
(* A grant is taken away. The interesting case is between the decision and *)
(* the execution: an approval may sit for half an hour, and the person who *)
(* could create a task when it was written may not be able to when it runs.*)
(***************************************************************************)
RevokePermission ==
    /\ permitted
    /\ permitted' = FALSE
    /\ UNCHANGED << request, approval, arguments, approvedArgs, expired,
                    cancelled, effects, attempts >>

(***************************************************************************)
(* The run is cancelled. Whatever has not happened is abandoned; whatever  *)
(* has happened stays happened, which is the half people expect to be      *)
(* wrong.                                                                  *)
(***************************************************************************)
CancelRun ==
    /\ ~cancelled
    /\ cancelled' = TRUE
    /\ request' = IF request \in RequestTerminal THEN request ELSE "Abandoned"
    /\ approval' = IF approval = "Pending" THEN "Expired" ELSE approval
    /\ UNCHANGED << arguments, approvedArgs, permitted, expired, effects,
                    attempts >>

(***************************************************************************)
(* An attempt to change what was proposed after it was proposed.           *)
(*                                                                         *)
(* This is the attack the fingerprint exists to stop, and it is in the     *)
(* model as an unguarded step on purpose: the specification must show that *)
(* even if the row were rewritten - by a bug, by a second code path, by    *)
(* anything short of the trigger that also refuses it - the approval would *)
(* no longer authorize it.                                                 *)
(***************************************************************************)
RewriteArguments ==
    /\ request \notin RequestTerminal
    /\ \E new \in Arguments :
        /\ new /= arguments
        /\ arguments' = new
    /\ UNCHANGED << request, approval, approvedArgs, permitted, expired,
                    cancelled, effects, attempts >>

-----------------------------------------------------------------------------
(***************************************************************************)
(* THE PERSON                                                              *)
(***************************************************************************)

(***************************************************************************)
(* Approving records what was in front of the person at the moment they    *)
(* decided. That is the whole content of an approval: not "this request is *)
(* allowed" but "this exact thing is allowed".                             *)
(***************************************************************************)
Approve ==
    /\ approval = "Pending"
    /\ ~expired
    /\ ~cancelled
    /\ request = "AwaitingApproval"
    /\ approval' = "Approved"
    /\ approvedArgs' = arguments
    /\ request' = "Approved"
    /\ UNCHANGED << arguments, permitted, expired, cancelled, effects,
                    attempts >>

Reject ==
    /\ approval = "Pending"
    /\ ~expired
    /\ ~cancelled
    /\ request = "AwaitingApproval"
    /\ approval' = "Rejected"
    /\ request' = "Rejected"
    /\ UNCHANGED << arguments, approvedArgs, permitted, expired, cancelled,
                    effects, attempts >>

-----------------------------------------------------------------------------
(***************************************************************************)
(* EXECUTION                                                               *)
(*                                                                         *)
(* Every condition is re-established here rather than trusted from the     *)
(* decision. An approval that was valid when the screen rendered may not   *)
(* be valid when the button is pressed, and the interval between them is   *)
(* the whole reason this specification exists.                             *)
(***************************************************************************)

Authorizes ==
    /\ approval = "Approved"
    /\ approvedArgs = arguments   \* the fingerprint, recomputed
    /\ ~expired

(***************************************************************************)
(* The canonical effect. Reached only with a current approval that         *)
(* authorizes these exact arguments, a request still in Approved, and the  *)
(* permission still held.                                                  *)
(***************************************************************************)
Execute ==
    /\ attempts < MaxAttempts
    /\ attempts' = attempts + 1
    /\ request = "Approved"
    /\ Authorizes
    /\ permitted
    /\ effects' = effects + 1
    /\ request' = "Executed"
    /\ UNCHANGED << approval, arguments, approvedArgs, permitted, expired,
                    cancelled >>

(***************************************************************************)
(* A refused attempt. Consumes an attempt and changes nothing else.        *)
(*                                                                         *)
(* Modelled as its own step so that a retry after a refusal is a real      *)
(* interleaving rather than an assumption. A client that keeps pressing    *)
(* the button must not eventually get through.                             *)
(***************************************************************************)
ExecuteRefused ==
    /\ attempts < MaxAttempts
    /\ attempts' = attempts + 1
    /\ \/ request /= "Approved"
       \/ ~Authorizes
       \/ ~permitted
    /\ UNCHANGED << request, approval, arguments, approvedArgs, permitted,
                    expired, cancelled, effects >>

Next ==
    \/ Approve
    \/ Reject
    \/ Execute
    \/ ExecuteRefused
    \/ Expire
    \/ RevokePermission
    \/ CancelRun
    \/ RewriteArguments

(***************************************************************************)
(* Weak fairness on Expire and on Execute, and on nothing else.            *)
(*                                                                         *)
(* Expiry is fair because it is the passage of time: the window closes     *)
(* whether or not anybody does anything, which is what makes a pending     *)
(* approval always settle. Execution is fair because a client that intends *)
(* to execute keeps trying.                                                *)
(*                                                                         *)
(* Deciding is deliberately NOT fair. AgencyOS does not assume a person    *)
(* ever answers, and a liveness property that rested on them answering     *)
(* would be a specification of somebody else's behaviour.                  *)
(***************************************************************************)
Spec == Init /\ [][Next]_vars /\ WF_vars(Execute) /\ WF_vars(Expire)

-----------------------------------------------------------------------------
(***************************************************************************)
(* SAFETY                                                                  *)
(***************************************************************************)

(***************************************************************************)
(* THE ONE THAT MATTERS. A canonical effect happens at most once.          *)
(*                                                                         *)
(* If this fails, a person approved one task and AgencyOS created two -    *)
(* and since the retry is what a client does when a response is lost, it   *)
(* would fail in production long before anybody noticed in testing.        *)
(***************************************************************************)
AtMostOneEffect == effects <= 1

(***************************************************************************)
(* Nothing executes without a person having allowed it. The approval is    *)
(* not a formality that a sufficiently determined code path can route      *)
(* around: no reachable state has an effect with no approval behind it.    *)
(***************************************************************************)
NoEffectWithoutApproval == (effects > 0) => (approval = "Approved")

(***************************************************************************)
(* A rejection is final. There is no path from Rejected to an effect, and  *)
(* no retry that reopens the question.                                     *)
(***************************************************************************)
RejectedNeverExecutes ==
    (approval = "Rejected") => (effects = 0 /\ request /= "Executed")

(***************************************************************************)
(* An approval that was never granted before its window passed authorizes  *)
(* nothing afterwards. Expiry is not a soft deadline.                      *)
(***************************************************************************)
ExpiredNeverExecutes ==
    (approval = "Expired") => (effects = 0 /\ request /= "Executed")

(***************************************************************************)
(* Changed arguments invalidate the approval.                              *)
(*                                                                         *)
(* The property the fingerprint exists for, stated as what it means rather *)
(* than as how it is implemented: whatever ran is what somebody looked at  *)
(* and allowed. Approving "create a task about Northgate" and executing    *)
(* "create a task about the Rousseau contract" is the failure this rules   *)
(* out.                                                                    *)
(***************************************************************************)
OnlyApprovedArgumentsExecute ==
    (request = "Executed") => (approvedArgs = arguments)

(***************************************************************************)
(* A grant lost while an approval sat unanswered stops the execution. The  *)
(* approval says a person allowed this; it does not say they still may.    *)
(*                                                                         *)
(* Stated about the moment of the transition rather than about the state   *)
(* afterwards. "An executed request implies the permission is held" would  *)
(* be false the instant somebody's role changed the next day, and would be *)
(* claiming something the system neither promises nor could keep: what     *)
(* matters is that the grant was held when the effect happened (§47).      *)
(***************************************************************************)
RevocationPreventsExecution ==
    [][(request /= "Executed" /\ request' = "Executed") => permitted]_vars

(***************************************************************************)
(* Cancellation never rolls back a committed effect.                       *)
(*                                                                         *)
(* Stated as a step property rather than as a wish: a task that was        *)
(* approved and created is a real task, and cancelling the run that        *)
(* proposed it does not unmake it. A specification that allowed the count  *)
(* to drop would be describing a system that lies about what it did.       *)
(***************************************************************************)
CancellationKeepsCommittedEffects == [][effects' >= effects]_vars

(***************************************************************************)
(* An executed request is terminal. Nothing moves it back to a state from  *)
(* which it could run again - which is the schema-level half of            *)
(* AtMostOneEffect, and the reason the trigger exists as well as the       *)
(* handler.                                                                *)
(***************************************************************************)
ExecutedIsTerminal == [][request = "Executed" => request' = "Executed"]_vars

(***************************************************************************)
(* What a person approved never changes after they approved it. The row    *)
(* records their decision, and no later step rewrites what the decision    *)
(* was about.                                                              *)
(***************************************************************************)
ApprovedArgumentsAreFrozen ==
    [][approvedArgs /= "none" => approvedArgs' = approvedArgs]_vars

(***************************************************************************)
(* A decision is made once. Pending may become anything; anything else is  *)
(* where it stays.                                                         *)
(***************************************************************************)
DecisionIsMadeOnce ==
    [][approval /= "Pending" => approval' = approval]_vars

Safety ==
    /\ TypeOK
    /\ AtMostOneEffect
    /\ NoEffectWithoutApproval
    /\ RejectedNeverExecutes
    /\ ExpiredNeverExecutes
    /\ OnlyApprovedArgumentsExecute

-----------------------------------------------------------------------------
(***************************************************************************)
(* LIVENESS                                                                *)
(*                                                                         *)
(* A question put to a person always gets an answer, even when nobody      *)
(* answers it. The window closes on its own, and an approval nobody        *)
(* attended to is expired rather than pending for ever.                    *)
(*                                                                         *)
(* This is deliberately the only liveness claim. The obvious stronger one  *)
(* - that the tool request itself always reaches a terminal status - is    *)
(* FALSE, and finding that out is one of the things this specification     *)
(* was worth writing for. A request that was approved and then never       *)
(* executed stays Approved: the approval behind it lapses, so it can never *)
(* run, but nothing sweeps the row. That is untidy rather than unsafe, and *)
(* the safety properties below are what make it so - which is a better     *)
(* answer than a specification that quietly assumed a sweeper exists.      *)
(***************************************************************************)
EveryDecisionSettles == (approval = "Pending") ~> (approval /= "Pending")

(***************************************************************************)
(* Once the window has closed, nothing further executes. The lapsed        *)
(* approval above is harmless precisely because of this: an unexecuted     *)
(* request left sitting in Approved can never become an effect.            *)
(***************************************************************************)
LapsedApprovalNeverExecutes ==
    [][(expired /\ request /= "Executed") => request' /= "Executed"]_vars

=============================================================================
