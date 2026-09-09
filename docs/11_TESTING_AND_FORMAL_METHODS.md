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

Four specifications exist and all are model-checked by
`scripts/Invoke-AgencyOS.ps1 formal`, which runs as a CI step. TLC is fetched once
and pinned by SHA-256; a checksum mismatch or an unreachable release fails loudly,
because a formal check that quietly skips itself is worse than none.

**The pin accepts more than one hash, each with what was verified about it.** A
GitHub release asset is mutable, and `v1.8.0` turns out not to be a fixed release
at all: upstream re-publishes that tag from current master. It has moved three
times — 2026-09-04, 2026-09-08 and 2026-09-09 — and the pin caught it every time,
which is the mechanism working rather than failing.

The 09-08 build was the same revision as the 09-04 one: 2,223 entries each,
identical CRCs on all but `META-INF/MANIFEST.MF`, differing only in a build
timestamp and the release tag, with the same `X-Git-Revision`
`b123b22654942bd7f8b1bcadcc47da4ee2cf4c0e`.

**The 09-09 build is not.** It carries revision `65fbace6` at 8,925 commits rather
than `b123b22` at 8,905 — twenty commits of upstream change — and 53 of the 2,223
entries differ: the manifest and 52 `tla2sany` parser and semantic-analyser
classes. That is a different model checker, not a restamp.

It was accepted after re-checking every specification against it, all four green
with identical **state counts**: OfflineWriteQueue 2853/1024, OutboundSend 83/48,
AiApproval 755/236, LocalInferenceLease 796/160.

**That evidence was incomplete, and M14 found the gap.** This paragraph originally
said the four specs explored "identical state spaces". They did not, quite: the
09-04 build reported an `OutboundSend` search depth of **17**, while the 09-09
build reports **14** over the same 83/48 state space. No property went unchecked
and no run reported an error, but the checker's search behaviour moved between two
builds wearing one version number, and comparing state counts alone did not catch
it. The 09-04 artifact is no longer obtainable upstream, so the discrepancy can no
longer be examined — the concrete cost of pinning a mutable tag. **Search depth is
recorded evidence from now on.**

Adding a hash means doing that comparison and writing down what it showed. Copying
whatever the download produced today is the one thing this is built to prevent,
and an unrecognized checksum still fails with the known-good list in the message.

**Resolved under M14: the pin is now `v1.7.4`.** The problem was never the hash
list — it was that `v1.8.0` is not a fixed release. Upstream re-publishes that tag
from current master, three times in five days, so no hash could hold. `v1.7.4` was
published 2024-08-05 and has not moved since; its manifest carries `X-Git-Tag
v1.7.4` at revision `5a47802b`, meaning it is built from the tag rather than from
whatever master happened to be.

All four specifications were re-verified against it before the switch and are
green at the same state counts, now recorded with their search depths:
OfflineWriteQueue 2853/1024 d15, OutboundSend 83/48 d14, AiApproval 755/236 d9,
LocalInferenceLease 796/160 d9. One accepted hash, and it is expected to stay one
(ADR-0036).

- **`specs/OfflineWriteQueue.tla`** (M3) — the offline write queue. Checks that a
  queued command has at most one effect however often it is retried.
  *2853 states generated, 1024 distinct, depth 15, no error found.*
- **`specs/OutboundSend.tla`** (M10) — the outbound send protocol, whose failure
  mode is sending a client the same commercial email twice. Models the canonical
  row, the provider's own state, a worker that crashes at any point,
  acknowledgements that arrive and acknowledgements that are lost, and
  reconciliation with all three verdicts. Checks `NeverSendsTwice`,
  `SentIsMonotonic`, `UnknownIsNeverAssumedFailed`, `CommittedIsNeverCalledFailed`,
  `SendRequiresDraft`, `CancelOnlyBeforeProvider` and `EventuallySettles`.
  *83 states generated, 48 distinct, depth 14, no error found.*

The other two are introduced where they belong: `specs/AiApproval.tla` (M12)
below, and `specs/LocalInferenceLease.tla` (M13) after it.

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
proposed arguments after the fact. 236 distinct states, depth 9, no error (755 generated).

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

## M13 — testing a protocol whose other half you cannot see

### `specs/LocalInferenceLease.tla`

Models device-local inference: a lease issued, context disclosed to a workstation,
a result returned — under every interleaving of the client returning twice, the
context changing underneath, the lease expiring, the run being cancelled and the
permission being revoked. **796 states generated, 160 distinct, depth 9, no
error**, with `MaxReturns = 2` (one return reaches the effect; the second is the
replay that must not produce a second one).

Thirteen named properties. Six as invariants — `TypeOK`, `AtMostOneEffect`,
`LocalOnlyNeverFallsBackToCloud`, `ClientResultIsNeverAuthorityAlone`,
`ChangedContextCannotReuseLease`, `NoDisclosureWithoutLease` — and seven as
temporal formulas: `NoEffectWithoutCurrentPermission`, `NoEffectWithoutValidLease`,
`MismatchedPresentationNeverTakesEffect`, `ExpiredLeaseCannotAuthorize`,
`CancelledRunCreatesNothing`, `CancellationNeverErasesEffects`,
`LeaseAlwaysStopsAuthorizing`.

### The property that turned out to be false, again

A run does **not** always reach a terminal state. A workstation that takes the
context and never comes back leaves the run in `AwaitingLocalExecution` for ever,
because AgencyOS cannot make somebody else's process answer. Writing a termination
property would have been writing something untrue.

What is proved instead is that the *lease* always stops authorizing — consumed, or
invalidated with the run, or simply expired, and the last needs nobody to do
anything. That is what makes an abandoned run untidy rather than exploitable.
`LocalExecutionAbandoned` exists in the domain for a sweeper a later milestone may
add.

Two milestones running, the specification's value was the property it refused to
prove.

### The client half is tested as decisions, not as a UI

`AgencyOS.Windows.Platform` holds the workstation decisions — activation,
notification policy, diagnostics, document handoff, capability description, the
local inference protocol — and contains **no WinRT**. That is what makes 521
Windows tests possible without a UI thread; the WinUI project provides thin
adapters over the operating system and holds no decisions to test.

### Structural tests over things behavioural tests cannot reach

Three kinds, each guarding a rule that a plausible change would break silently:

- **`XamlAccessibilityTests`** parses the shipped XAML. An interactive control
  with no accessible name, a notice with no title anywhere, a text control with a
  fixed height or a hard-coded colour fails the build. Accessibility regressions
  are silent — the page still looks right — so a periodic review is the wrong
  instrument.
- **`ClientBoundaryTests`** reads the project graph and the client source. No
  client project may reference `AgencyOS.Infrastructure`, `AgencyOS.Application`,
  `AgencyOS.Domain` or `AgencyOS.Api`, name provider credential material, or
  address a model provider. `DiagnosticSummary.cs` is excluded from the credential
  scan by name, because it lists those words in order to redact them.
- **`AiPersistenceCompletenessTests`** compares the mapped model against the
  database. It exists because M13 shipped two properties that were added to an
  aggregate and never mapped, and the migration failed at run time with
  `42703: column "residency" does not exist`. The column-naming check is the one
  that would have caught it.

### The twenty-one security regressions

M13's new surfaces — a citation, a toast, a deep link — are each a pointer minted
while somebody was authorized and followed later. The suite asserts that none
carries authority, across unit, Windows and integration levels: capability before
disclosure, no cloud fallback, the lease crossing no tenant, user, run, subject,
residency or altered context, expiry, replay, cancellation, untrusted local
output, stored results re-authorizing, citation drill-down, approval privacy,
stale notifications, Restricted at every residency, no client credential,
allow-listed diagnostics, and a temp path that never becomes a document identity.

The alternate paths are asserted rather than assumed: withholding a result from
the detail route proves nothing if the list route carries it or the trace quotes
it, so both are tested. The second holds only because run steps record what
AgencyOS did rather than what the model said.

## M14 — measuring without a stopwatch

### Round trips, not wall-clock

Hosted CI cannot assert a time. A shared runner's numbers move for reasons that
have nothing to do with the code, and a flaky performance gate teaches people to
ignore the gate (§53).

What it can assert is an algorithmic invariant with no baseline and no magic
number: **the count of database round trips a read makes must not depend on how
many rows come back.** That is the definition of an N+1, and it is the defect that
hides in development where every tenant has four people. Each test compares two
measurements of the same endpoint — five rows against fifty — so it cannot drift,
cannot be tuned, and never needs re-baselining when the schema changes.

`QueryCounter` hooks in through `IDbContextOptionsConfiguration` rather than by
re-registering the DbContext, so the harness measures the options the application
actually built instead of a copy that would drift.

**It carries an `EverObserved` flag, and one test asserts only that.** A counter
never wired into the host would report zero on both sides of every comparison and
pass every N+1 assertion — the most convincing false green available. There is a
test whose only job is to fail in that case.

Measured result: no N+1 in the people-list or search paths.

### Structural tests over things behaviour cannot reach

- **Architectural fitness.** The project graph, the domain's freedom from
  persistence technology, and the rule that every paged read clamps its page size.
  The clamp test counts what it examined and fails if the scan stops finding
  services, because a structural test that quietly matches nothing is the failure
  mode these are most prone to.
- **Blob store conformance.** One suite, two implementations — the shipped
  filesystem store and a second that shares no code with it. Writing the second one
  immediately found a defect in the first: `ExistsAsync` and `DeleteAsync` threw
  where the contract says they answer.
- **Existence disclosure.** Four cases pinning when a refusal may admit that
  something exists (ADR-0038), so a future change to one surface has to be a
  decision about the rule rather than a local edit.

### When a test cannot say why it failed

An unexplained 500 reaches a test as a status, a title and a trace id that leads
nowhere, and the test host's console does not survive into a CI run's output.
`CapturedLogs` keeps the host's recent errors so an assertion can quote them.

It was written after two CI cycles spent guessing at a 500, and it identified the
cause on its first run: `22021: invalid byte sequence for encoding "UTF8": 0x00`.
A NUL byte in an uploaded text file was destroying the entire ingestion. One cycle
spent building an instrument beat three spent eliminating hypotheses.

