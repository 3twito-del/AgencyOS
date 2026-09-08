# Testing & Formal Methods

## Test pyramid

1. Unit tests
2. Domain invariant/property tests
3. Integration tests against real PostgreSQL via containers
4. API contract tests
5. Windows client integration/smoke tests
6. End-to-end vertical-slice tests
7. Migration/rollback tests
8. Performance regression tests
9. Chaos/recovery tests for mature critical paths

## Property-based testing
Candidates:
- FsCheck (.NET/F#)
- Hypothesis (Python)
- proptest (Rust)

## Fuzzing
Mandatory candidates:
- binary/document parsers;
- importers;
- sync protocol parsers;
- untrusted attachment metadata.

M10 brought the first two of these into the system and answered the first one by
not doing it: PDF and DOCX are reported as `Unsupported` rather than handed to a
parser nobody has fuzzed. Plain-text extraction, filename handling, media-type
resolution and HTML sanitization are covered by hostile-input unit tests —
traversal, control characters, quotes, oversize names, script in every shape it
arrives in, and external image beacons — with malformed multipart bodies exercised
end to end against the API. That is table-driven hostility rather than fuzzing,
and the distinction is worth keeping: a fuzzer finds the cases nobody thought of,
and adding real document parsers is the point at which one becomes mandatory
rather than desirable.

## What the database is tested for, not just the aggregate

From M11 onward, an invariant enforced in both the domain and the schema is tested
against **both**. Four M11 integration tests issue SQL directly: removing a
signal's last source, editing a stated forecast, deleting one, and rewriting a
thesis revision. The aggregate refuses all four as well, and testing only the
aggregate would prove that one code path is careful rather than that the schema
is — which is the whole reason the trigger exists.

The classification test is the other shape worth copying. It asserts that a reader
without the elevated grant learns nothing about a hidden claim from the list, from
the citation count on a source they may read, or from the evidence on a thesis
they may open. Three separate leaks, each of which would answer "is there
something about this person" on its own, and each of which would have passed a
test that only checked the list.

## Formal specification
Use TLA+/PlusCal selectively for:
- client/server version compatibility protocol;
- forced update/rollback logic;
- offline sync conflict rules;
- financial posting/ledger invariants;
- distributed/idempotent workflows.

Two specifications exist and both are model-checked by
`scripts/Invoke-AgencyOS.ps1 formal`, which runs as a CI step. TLC is fetched once
and pinned by SHA-256; a checksum mismatch or an unreachable release fails loudly,
because a formal check that quietly skips itself is worse than none.

**The pin accepts more than one hash, each with what was verified about it.** A
GitHub release asset is mutable, and the v1.8.0 jar was re-published on 2026-09-08
during M11 — the pin caught it, which is the mechanism working rather than
failing. The two builds were compared entry by entry: 2,223 entries each,
identical CRCs on all of them except `META-INF/MANIFEST.MF`, which differs only in
a build timestamp and the release tag, with the same `X-Git-Revision`
`b123b22654942bd7f8b1bcadcc47da4ee2cf4c0e` in both.

Adding a third hash means doing that comparison again and writing down what it
showed. Copying whatever the download produced today is the one thing this is
built to prevent, and an unrecognized checksum still fails with the known-good
list in the message.

- **`specs/OfflineWriteQueue.tla`** (M3) — the offline write queue. Checks that a
  queued command has at most one effect however often it is retried.
  *2853 states generated, 1024 distinct, no error found.*
- **`specs/OutboundSend.tla`** (M10) — the outbound send protocol, whose failure
  mode is sending a client the same commercial email twice. Models the canonical
  row, the provider's own state, a worker that crashes at any point,
  acknowledgements that arrive and acknowledgements that are lost, and
  reconciliation with all three verdicts. Checks `NeverSendsTwice`,
  `SentIsMonotonic`, `UnknownIsNeverAssumedFailed`, `CommittedIsNeverCalledFailed`,
  `SendRequiresDraft`, `CancelOnlyBeforeProvider` and `EventuallySettles`.
  *83 states generated, 48 distinct, no error found.*

Inbox synchronization is deliberately not formalized: it is idempotent by
construction — every write is keyed on the provider's message id within the
account — and has no external side effect to get wrong. Over-formalizing ordinary
work makes the specifications that matter harder to take seriously.

## Chaos scenarios
LAB may intentionally:
- drop network;
- kill API mid-operation;
- duplicate events;
- reorder events;
- expire auth;
- corrupt local cache;
- crash after DB commit before dispatch;
- crash between recording a send and hearing the provider's answer (M10; covered
  by an integration test that leaves a dispatch in `SendRequested` and asserts the
  recovering worker reconciles rather than resending);
- interrupt migration;
- serve old/new API versions simultaneously.

No release is called resilient merely because happy-path tests pass.

## M12 — testing an untrusted component

### CI never reaches a model provider

`FakeModelProvider` is registered in every environment and is the authoritative
test infrastructure, not a stub. Two reasons, and the second is the important one.

A green build that depends on somebody else's service being reachable goes red for
reasons that have nothing to do with the code. And no real provider will emit a
forged approval, a request for `sql.execute`, or a claim to have been granted
administrator rights on demand — which is exactly the class of response that has
to be proved harmless.

An unscripted call returns `ProviderUnavailable` rather than something plausible,
so a test that forgot to say what should happen fails rather than passing by
accident.

**Promotion to ALPHA does not depend on live external provider availability.**

### The prompt-injection corpus asserts the envelope, not obedience

Fifteen literal payloads — direct override, role confusion, exfiltration, false
authority, fence escape. For each, three assertions: it arrives labelled as data,
it cannot close the fence containing it, and it is not silently discarded. An
end-to-end test stores one in a research case and reads what the provider was
actually sent.

None of them asserts that a model resists injection. AgencyOS cannot test that and
does not depend on it: the defences that matter are a closed tool registry and an
approval, and they are tested separately.

### Architecture tests pin the write surface

Every tool derives from the base class whose authorization check is sealed;
exactly one tool is a canonical write and it is `task.create`; no tool has an
external effect; no tool constructor takes a `DbContext`, an `IConfiguration`, an
`HttpClient`, a `Process` or a `FileStream`; every allow-listed name resolves;
every agent has a versioned prompt; every tool names a real permission.

Each of those is a rule a future change could break by writing perfectly
reasonable code, and none would fail a behavioural test.

### `specs/AiApproval.tla`

Models the approval-to-execution protocol under every interleaving of a person
deciding, an approval expiring, a permission revoked between the decision and the
execution, a run cancelled, a client retrying, and an attempt to rewrite the
proposed arguments after the fact. 236 distinct states, depth 9, no error.

Checked: at most one canonical effect; no effect without an approval; rejected and
expired never execute; only the approved arguments execute; the permission is held
at the moment of execution; committed effects never roll back; an executed request
is terminal; approved arguments are frozen; a decision is made once; a pending
approval always settles.

**The language model is deliberately not modelled.** It is untrusted input, and a
specification of untrusted input is a specification of "anything"; writing one
would mean recording assumptions about model behaviour, and the entire design
exists because no such assumption is safe.

Weak fairness is asserted on expiry and on execution and on nothing else. Deciding
is deliberately not fair: AgencyOS does not assume a person ever answers, and a
liveness property resting on that would be a specification of somebody else's
behaviour.

### The property that turned out to be false

The natural claim — that a tool request always reaches a terminal status — does
not hold. A request approved and never executed stays `Approved`: the approval
lapses so it can never run, but nothing sweeps the row. `LapsedApprovalNeverExecutes`
is why that is untidy rather than unsafe. Finding it is most of what the
specification was worth writing for.
