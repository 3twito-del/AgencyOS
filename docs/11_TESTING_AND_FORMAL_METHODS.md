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
