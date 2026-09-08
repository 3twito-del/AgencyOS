# ADR-0028 — The outbound send protocol, and why an unknown outcome is its own answer

- **Status:** Accepted
- **Date:** 2026-09-08
- **Milestone:** M10 — Canonical Documents, Communications, Outlook & Office Integration
- **Supersedes:** nothing
- **Builds on:** ADR-0014 (concurrency and idempotency), ADR-0026 (a mailbox
  belongs to a person), ADR-0029 (the worker and its leases)
- **Formal specification:** `specs/OutboundSend.tla`

## Context

Everything AgencyOS did before M10 was a database write. Sending a message is the
first operation that causes something irreversible outside it: a real message, to
a real person, that cannot be taken back.

The hard case is not failure. It is a provider that accepted a send and then failed
to say so — a timeout, a dropped connection, a process killed between the call and
the record. AgencyOS cannot tell that apart from a send that never happened, and
the two demand opposite responses. A system that guesses "failed" and retries
sends a client the same commercial email twice, and nobody finds out from the
system.

## Decisions

### 1. Nine states, and every one of them means something different

`Draft → Queued → ProviderDraftCreated → SendRequested → Sent`, with
`FailedRetryable`, `FailedPermanent`, `UnknownOutcome` and `Cancelled` as the
other ends.

- **Draft** — composed and saved. Nothing has been asked of the provider.
- **Queued** — handed to the worker. The last point at which cancelling is honest.
- **ProviderDraftCreated** — a draft exists at the provider. Repeatable, invisible
  outside, and it buys the identifier that makes recovery possible.
- **SendRequested** — written and committed *before* the provider is called.
- **Sent** — the provider confirmed it. Terminal, and monotonic.
- **UnknownOutcome** — the provider did not answer. The message may or may not
  have gone.

### 2. The state is written before the call, and that ordering is the design

`NoteSendRequested` is committed, and only then is the provider asked to send.

Written the other way round — call first, record after — a crash between the two
leaves a row saying nothing was sent while the provider is already sending it. The
next worker sends it again.

With this ordering, a row found in `SendRequested` tells a recovering worker that a
send may already have happened. It reconciles; it never sends on the strength of
not knowing.

### 3. A draft first, always

Creating a provider draft is repeatable and has no consequence anybody outside
sees. It exists so the send carries a correlation value that a later search can
match on with certainty, rather than by comparing subject lines.

Sending is only ever requested with a draft in hand. Without one there would be no
identifier to reconcile against and an unknown outcome would be unresolvable for
ever.

### 4. `UnknownOutcome` is never treated as a failure

Not in the state machine, not in the counts, not in the wording on the screen, and
not by any retry path. It leaves only for `Sent` (found) or
`ProviderDraftCreated` (proven absent) — never straight to a failure.

The Windows surface renders it as "Outcome unknown — check the mailbox" and
explains that AgencyOS will not resend it on a guess. The desk counts unknown
outcomes separately from failed sends because they are different problems: a
failure needs a decision about trying again, and an unknown outcome needs somebody
to go and look.

### 5. Reconciliation moves only on evidence

Searching the provider's sent items answers one of three things:

- **FoundSent** — the message went. The dispatch becomes `Sent`.
- **ProvenAbsent** — it is not in sent items **and the draft is still there**.
  Together those prove it did not go, which is what makes a retry safe. Absence
  alone proves nothing: a provider that lost the draft and sent the message looks
  identical.
- **Inconclusive** — reported as itself. The dispatch stays `UnknownOutcome`, a
  person is told, and nothing is guessed.

Reconciliation is claimable work, so a dispatch whose outcome is unknown is picked
up and looked at rather than sitting for ever. It is spaced by a backoff, because
reconciling is not retrying but it is still a call.

### 6. AgencyOS never claims delivery

`Sent` means the provider confirmed it accepted the message. It does not mean
delivered, opened or read, and no surface says otherwise. A successful Graph call
is a mail server saying it took the message, which is a different fact from the
message arriving.

### 7. Cancelling is possible only before the provider

`Draft` and `Queued`. After that AgencyOS cannot unsend anything and does not offer
a button that implies it can.

### 8. Sending is authorized against the mailbox, twice

The caller needs `communications.send` **and** must own the mailbox. There is no
`From` field anywhere in the request: the sending mailbox is named by identifier
and checked against its owner server-side, so knowing an identifier buys nothing.
Being able to read a shared agency mailbox is deliberately not enough to send as
it.

A refusal is reported as the missing permission rather than as a missing mailbox,
so a caller cannot learn which account identifiers are real.

### 9. Sending is never queued offline

`docs/13_OFFLINE_CLASSIFICATION.md` classifies every outbound operation
`ONLINE_ONLY`. An actual send is never placed in the M3 offline write queue: a
message queued for four hours is a message somebody has already been told was
sent, and the queue's own retry semantics are exactly wrong for an irreversible
external act.

### 10. Idempotency keys and provider identity are different things

The M3 idempotency key makes *the command* execute at most once: pressing "send"
twice creates one dispatch. It says nothing about whether the provider sent the
message once, and cannot.

The correlation value carried onto the provider draft is what makes the *external*
act recoverable. The residual guarantee, stated honestly:

- One canonical intent produces at most one provider send **attempt** per
  reconciliation cycle, and never a second attempt without evidence the first did
  not commit.
- A provider that accepts a message and reports nothing leaves AgencyOS unable to
  prove the send until reconciliation succeeds. During that window the honest
  state is `UnknownOutcome`, and it is shown as such.
- If a provider both commits a send and permanently loses the record of it,
  AgencyOS cannot detect that. Nothing can.

### 11. There is no fake transaction across the provider and PostgreSQL

They do not share one. The protocol is a sequence of individually committed
states, each of which is recoverable on its own, and the code does not pretend
otherwise anywhere.

## Formal verification

`specs/OutboundSend.tla` models the canonical row, the provider's own state, a
worker that crashes at any point, acknowledgements that arrive and
acknowledgements that are lost, and reconciliation with all three verdicts.

TLC checks, at `MaxAttempts = 3`:

- `NeverSendsTwice` — one intent produces at most one message in the recipient's
  inbox. **The one that matters.**
- `SentIsMonotonic` — a confirmed send is never un-confirmed.
- `UnknownIsNeverAssumedFailed` — `UnknownOutcome` leaves only for `Sent` or
  `ProviderDraftCreated`.
- `CommittedIsNeverCalledFailed` — a message the provider sent is never reported
  as failed.
- `SendRequiresDraft` — a send is only requested with a draft in hand.
- `CancelOnlyBeforeProvider` — nothing committed is ever cancelled.
- `EventuallySettles` — a queued message reaches a terminal state or rests in
  `UnknownOutcome` for a person.

**Result: 48 distinct states, no error found.** The specification runs in CI as a
build step, alongside `specs/OfflineWriteQueue.tla`.

Two modelling notes, because they changed the specification rather than the code.
A free-standing "release the claim" action let the checker satisfy fairness by
claiming and releasing for ever while the message went nowhere; the implementation
has no such operation — a worker releases as part of completing a step — so the
model does not either. And progress needs *strong* fairness rather than weak:
a crash disables every step momentarily, and weak fairness would permit a worker
that claims and dies for ever.

Inbox synchronization is deliberately not formalized. It is idempotent by
construction and has no external side effect to get wrong.

## Consequences

- A client is never sent the same commercial email twice because AgencyOS guessed.
- Some sends sit in `UnknownOutcome` until somebody looks or reconciliation
  succeeds. That is the honest cost, and it is visible rather than hidden.
- The protocol is slower than calling `sendMail` and writing a row afterwards. It
  is slower on purpose.
