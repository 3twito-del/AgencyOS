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

## Formal specification
Use TLA+/PlusCal selectively for:
- client/server version compatibility protocol;
- forced update/rollback logic;
- offline sync conflict rules;
- financial posting/ledger invariants;
- distributed/idempotent workflows.

## Chaos scenarios
LAB may intentionally:
- drop network;
- kill API mid-operation;
- duplicate events;
- reorder events;
- expire auth;
- corrupt local cache;
- crash after DB commit before dispatch;
- interrupt migration;
- serve old/new API versions simultaneously.

No release is called resilient merely because happy-path tests pass.
