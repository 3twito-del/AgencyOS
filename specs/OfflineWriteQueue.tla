---------------------------- MODULE OfflineWriteQueue ----------------------------
(***************************************************************************)
(* AgencyOS M3 - offline write queue and idempotent submission protocol.    *)
(*                                                                         *)
(* The Windows client queues a small set of reversible commands while       *)
(* offline and submits them on reconnect. This models the part that is      *)
(* genuinely hard: an unreliable channel between a client that may crash    *)
(* and a server that commits durably.                                       *)
(*                                                                         *)
(* Faults modelled deliberately:                                            *)
(*   - the acknowledgement is lost after the server has committed;          *)
(*   - the client crashes mid-flight and restarts from its durable queue;   *)
(*   - the same command is therefore submitted more than once;              *)
(*   - the observed version is stale by the time the server sees it;        *)
(*   - the server rejects the command outright.                             *)
(*                                                                         *)
(* The properties worth checking are the ones a careless implementation     *)
(* gets wrong: an acknowledged effect must not be lost, and a retry must    *)
(* not produce a second canonical effect.                                   *)
(***************************************************************************)

EXTENDS Integers, FiniteSets, Sequences, TLC

CONSTANTS
    Commands,        \* queued command identifiers
    MaxAttempts      \* attempts before a retryable failure is reclassified

ASSUME MaxAttempts \in Nat /\ MaxAttempts > 0

(***************************************************************************)
(* Queue item states. Names match the C# implementation exactly so the      *)
(* model and the code can be compared by reading.                           *)
(***************************************************************************)
States == { "LocalPending", "Sending", "Synced", "Conflict",
            "FailedRetryable", "FailedPermanent" }

(***************************************************************************)
(* How the server answers a command it has not already committed. Chosen    *)
(* nondeterministically so every branch is explored.                        *)
(***************************************************************************)
Verdicts == { "commit", "stale", "reject" }

(*--algorithm OfflineWriteQueue

variables
    \* Client-side queue row. Durable: it survives a crash.
    state = [c \in Commands |-> "LocalPending"],
    attempts = [c \in Commands |-> 0],

    \* Server-side idempotency ledger: which keys have produced a canonical
    \* effect, and what answer was stored for them.
    committed = {},
    verdictOf = [c \in Commands |-> "none"],

    \* Canonical effects actually performed per key. The entire point of the
    \* idempotency design is that this never exceeds one.
    effects = [c \in Commands |-> 0],

    \* Commands whose success the client has durably observed.
    acknowledged = {};

define
    Terminal(s) == s \in { "Synced", "Conflict", "FailedPermanent" }

    \* ---------------------------------------------------------------- safety

    \* One idempotency key produces at most one canonical effect, however
    \* many times it is submitted and whatever the client does in between.
    AtMostOneEffect == \A c \in Commands : effects[c] <= 1

    \* Anything the client believes succeeded really did commit.
    AcknowledgedIsCommitted ==
        \A c \in acknowledged : c \in committed

    \* A command the server committed is never reported as permanently
    \* failed. Doing so would lose a write the system actually made - the
    \* failure mode that makes naive retry-with-give-up unsafe.
    CommittedNeverPermanentlyFailed ==
        \A c \in Commands : (c \in committed) => (state[c] # "FailedPermanent")

    \* A stale command never produces a canonical effect: the server refuses
    \* it rather than overwriting newer state.
    StaleNeverOverwrites ==
        \A c \in Commands : (verdictOf[c] = "stale") => (effects[c] = 0)

    \* A conflict is only ever reported for a command that was actually
    \* found stale, never for one that simply failed to reach the server.
    ConflictOnlyWhenStale ==
        \A c \in Commands : (state[c] = "Conflict") => (verdictOf[c] = "stale")

    TypeOk ==
        /\ \A c \in Commands : state[c] \in States
        /\ \A c \in Commands : attempts[c] \in 0..MaxAttempts
        /\ \A c \in Commands : effects[c] \in 0..1
        /\ committed \subseteq Commands
        /\ acknowledged \subseteq Commands

    \* -------------------------------------------------------------- liveness
    \*
    \* Not checked. The model deliberately allows an adversarial network that
    \* loses every acknowledgement for ever, under which nothing drains. That
    \* is the honest situation: draining requires eventual delivery, which is
    \* an assumption about the world rather than a property of the protocol.
    QueueDrains == \A c \in Commands : <>(Terminal(state[c]))
end define;

\* Each command is its own process, so TLC explores every interleaving of
\* concurrent submissions as well as every fault ordering.
fair process worker \in Commands
begin
Work:
    while ~Terminal(state[self]) do
        \* Only a queued or retryable item is submitted.
        await state[self] \in { "LocalPending", "FailedRetryable" };
        state[self] := "Sending";
        \* Saturating counter. An unbounded attempt count would make the
        \* state space infinite without adding any behaviour: what matters is
        \* whether the cap has been reached, not by how much.
        attempts[self] := IF attempts[self] >= MaxAttempts
                          THEN MaxAttempts
                          ELSE attempts[self] + 1;

Server:
        \* The server consults its idempotency ledger first. A key it has
        \* already committed replays the stored answer and performs no second
        \* effect. This single branch is what makes retry safe.
        if self \in committed then
            skip;
        else
            with v \in Verdicts do
                verdictOf[self] := v;
                if v = "commit" then
                    committed := committed \cup {self};
                    effects[self] := effects[self] + 1;
                end if;
            end with;
        end if;

Respond:
        either
            \* The answer reaches the client.
            if self \in committed then
                state[self] := "Synced";
                acknowledged := acknowledged \cup {self};
            elsif verdictOf[self] = "stale" then
                state[self] := "Conflict";
            else
                state[self] := "FailedPermanent";
            end if;

        or
            \* The answer is lost in transit. The server may already have
            \* committed, so the client must never conclude failure here - it
            \* returns the item to the queue and replays, which the ledger
            \* makes safe.
            if attempts[self] >= MaxAttempts then
                state[self] := "LocalPending";
            else
                state[self] := "FailedRetryable";
            end if;

        or
            \* The client crashes after the server acted but before the
            \* outcome was durably recorded. The queue row is the durable
            \* state, so restart finds the item still pending.
            state[self] := "LocalPending";
        end either;
    end while;
end process;

end algorithm;*)
\* BEGIN TRANSLATION (chksum(pcal) = "4ad0038a" /\ chksum(tla) = "a9615e91")
VARIABLES state, attempts, committed, verdictOf, effects, acknowledged, pc

(* define statement *)
Terminal(s) == s \in { "Synced", "Conflict", "FailedPermanent" }





AtMostOneEffect == \A c \in Commands : effects[c] <= 1


AcknowledgedIsCommitted ==
    \A c \in acknowledged : c \in committed




CommittedNeverPermanentlyFailed ==
    \A c \in Commands : (c \in committed) => (state[c] # "FailedPermanent")



StaleNeverOverwrites ==
    \A c \in Commands : (verdictOf[c] = "stale") => (effects[c] = 0)



ConflictOnlyWhenStale ==
    \A c \in Commands : (state[c] = "Conflict") => (verdictOf[c] = "stale")

TypeOk ==
    /\ \A c \in Commands : state[c] \in States
    /\ \A c \in Commands : attempts[c] \in 0..MaxAttempts
    /\ \A c \in Commands : effects[c] \in 0..1
    /\ committed \subseteq Commands
    /\ acknowledged \subseteq Commands







QueueDrains == \A c \in Commands : <>(Terminal(state[c]))


vars == << state, attempts, committed, verdictOf, effects, acknowledged, pc
        >>

ProcSet == (Commands)

Init == (* Global variables *)
        /\ state = [c \in Commands |-> "LocalPending"]
        /\ attempts = [c \in Commands |-> 0]
        /\ committed = {}
        /\ verdictOf = [c \in Commands |-> "none"]
        /\ effects = [c \in Commands |-> 0]
        /\ acknowledged = {}
        /\ pc = [self \in ProcSet |-> "Work"]

Work(self) == /\ pc[self] = "Work"
              /\ IF ~Terminal(state[self])
                    THEN /\ state[self] \in { "LocalPending", "FailedRetryable" }
                         /\ state' = [state EXCEPT ![self] = "Sending"]
                         /\ attempts' = [attempts EXCEPT ![self] = IF attempts[self] >= MaxAttempts
                                                                   THEN MaxAttempts
                                                                   ELSE attempts[self] + 1]
                         /\ pc' = [pc EXCEPT ![self] = "Server"]
                    ELSE /\ pc' = [pc EXCEPT ![self] = "Done"]
                         /\ UNCHANGED << state, attempts >>
              /\ UNCHANGED << committed, verdictOf, effects, acknowledged >>

Server(self) == /\ pc[self] = "Server"
                /\ IF self \in committed
                      THEN /\ TRUE
                           /\ UNCHANGED << committed, verdictOf, effects >>
                      ELSE /\ \E v \in Verdicts:
                                /\ verdictOf' = [verdictOf EXCEPT ![self] = v]
                                /\ IF v = "commit"
                                      THEN /\ committed' = (committed \cup {self})
                                           /\ effects' = [effects EXCEPT ![self] = effects[self] + 1]
                                      ELSE /\ TRUE
                                           /\ UNCHANGED << committed, effects >>
                /\ pc' = [pc EXCEPT ![self] = "Respond"]
                /\ UNCHANGED << state, attempts, acknowledged >>

Respond(self) == /\ pc[self] = "Respond"
                 /\ \/ /\ IF self \in committed
                             THEN /\ state' = [state EXCEPT ![self] = "Synced"]
                                  /\ acknowledged' = (acknowledged \cup {self})
                             ELSE /\ IF verdictOf[self] = "stale"
                                        THEN /\ state' = [state EXCEPT ![self] = "Conflict"]
                                        ELSE /\ state' = [state EXCEPT ![self] = "FailedPermanent"]
                                  /\ UNCHANGED acknowledged
                    \/ /\ IF attempts[self] >= MaxAttempts
                             THEN /\ state' = [state EXCEPT ![self] = "LocalPending"]
                             ELSE /\ state' = [state EXCEPT ![self] = "FailedRetryable"]
                       /\ UNCHANGED acknowledged
                    \/ /\ state' = [state EXCEPT ![self] = "LocalPending"]
                       /\ UNCHANGED acknowledged
                 /\ pc' = [pc EXCEPT ![self] = "Work"]
                 /\ UNCHANGED << attempts, committed, verdictOf, effects >>

worker(self) == Work(self) \/ Server(self) \/ Respond(self)

(* Allow infinite stuttering to prevent deadlock on termination. *)
Terminating == /\ \A self \in ProcSet: pc[self] = "Done"
               /\ UNCHANGED vars

Next == (\E self \in Commands: worker(self))
           \/ Terminating

Spec == /\ Init /\ [][Next]_vars
        /\ \A self \in Commands : WF_vars(worker(self))

Termination == <>(\A self \in ProcSet: pc[self] = "Done")

\* END TRANSLATION 
=============================================================================
