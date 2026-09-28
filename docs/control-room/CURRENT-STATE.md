# AgencyOS current canonical state

**Status:** CURRENT

**Protocol:** [CANONICAL-STATE-PROTOCOL.md](CANONICAL-STATE-PROTOCOL.md)

Canonical bootstrap state published by `DELTA-20260928-001`
([`CANONICAL-DELTAS.md`](CANONICAL-DELTAS.md)). Specialised authorities retain their governed
scope, including
[`docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`](../reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md)
for operational closure. This file states current position; it does not replace their evidence or
history.

Decision records: [`DECISIONS.md`](DECISIONS.md).

---

## Release identity

| | |
| --- | --- |
| Release | **ALPHA 0.1.0 build 97** |
| Product commit | `b3f41bfd68e81ab42da899671f58e01f0988d3d2` |
| Tag | `alpha-b3f41bf`, annotated object `3219b226f709c7dd784c6af52ce4869263a50085`, dereferences to `b3f41bf` |
| `AgencyOS.Windows.exe` SHA-256 | `80d4e48127cc119ea52e02caae3b443e5ccd2eeb41a563daf36b99d67c9e95de` |
| API contract | 17 (minimum supported 1) |
| Expected schema | `20260909072201_AiResultClassification` |
| Authoritative CI | run `36404049885`, #109, `workflow_dispatch`, exactly `b3f41bf`, success |
| Qualifying Nightly | run `36427576682`, #58, `nightly.yml`, `workflow_dispatch`, exactly `b3f41bf`, success |
| Release record | `docs/releases/ALPHA-0.1.0-build-97.md` |

Every later commit on `operational-regression-gate` is documentation and does not change the
released product identity. Check with `git diff --stat b3f41bf..operational-regression-gate`:
every path is under `docs/`. Full detail: closure state section 1.

## Operational closure

- **Operational Closure: COMPLETE.** M1–M4 are COMPLETE.
- The C1–C16 terminal dispositions are governed by the specialised closure state.
- Feature freeze: EXITED.
- No closure action remains.
- Reopening requires fresh contradictory canonical evidence or a genuine future operational
  failure.

Authority: [`docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`](../reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md).
The closure register is not duplicated here.

## Next-generation stage

| Stage | Status | Decision |
| --- | --- | --- |
| NG-0 Baseline Lock | COMPLETE | — |
| NG-1 Representation Invariant Study | CLOSED | `DECISION-20260928-001` |
| NG-OD1 Subject-family decision | APPROVED | `DECISION-20260928-002` |
| NG-2 Subject Model & Boundary | CLOSED | `DECISION-20260928-003` |
| NG-3 Commercial Chain Decomposition | CLOSED | `DECISION-20260928-004` |
| **NG-4 Vertical Extension Architecture** | **NEXT** | — |

NG-4 has not begun under this migration work.

## Closed architecture decisions

- Representation Mandate boundary: `DECISION-20260928-001`.
- Subject family (Person + Group + External Organization): `DECISION-20260928-002`.
- Typed represented-subject boundary: `DECISION-20260928-003`.
- Hybrid commercial decomposition: `DECISION-20260928-004`.

The decision text is in [`DECISIONS.md`](DECISIONS.md) and is not restated here.
Operational-closure-specific decisions remain governed by the specialised closure record and are
not duplicated here.

## Open questions

**Representation**
- umbrella Representation, persisted or derived;
- qualifying clienthood;
- delegated and sub-agency topology;
- exact commission placement.

**Subject identity and reference**
- Company ↔ External Organization implementation;
- Group persistence;
- successor/predecessor semantics;
- Person reconciliation;
- exact storage and retention mechanics.

**Commercial**
- commercial ↔ mandate cardinality;
- the agreement snapshot;
- the amount-determination record;
- multi-arrangement instruments;
- the future commission model;
- ledger integration.

**Vertical architecture**
- the extension mechanism itself. This is NG-4's subject and remains undecided.

## Owner decisions

- Next-generation: `DECISION-20260928-002` (the represented-subject family) in
  [`DECISIONS.md`](DECISIONS.md).
- Operational closure: the Owner decisions in force (D1–D5, Decision C and the others) are in
  closure state section 6, and the residual acceptance (C12, C14) in section 5. They are not
  migrated here.

## Evidence limitations

- The NG-1 to NG-3 decisions were made before this durable Control Room ledger existed, and were
  migrated into it by the published `DELTA-20260928-001`.
- The migration preserves the prior adjudications. It is not a new research pass.
- Exact implementation details intentionally remain open where listed above.
- Build 97 remains the product baseline being inspected. No next-generation architecture is
  implemented.

## Forbidden work

- Reopening operational closure without new contradictory evidence or a future failure.
- A new closure census or blind run.
- Next-generation product implementation.
- Schema, API or domain expansion from these architecture decisions without Owner approval where
  required.
- A generic Entity, Subject or Party abstraction.
- A speculative plugin framework.
- A generic Transaction.
- A generic JSON-terms junk drawer.
- A universal rights ontology.
- A mega lifecycle.
- Implementation disguised as migration.

## Exact next bounded action

**NG-4 — Vertical Extension Architecture: determine how evidence-backed vertical vocabularies,
policies, typed terms and workflows extend the stable representation/commercial core without
speculative plugin infrastructure or vertical leakage into the core.**

NG-4 research/design has not begun as part of this migration.

**Authorization boundary:** "NEXT" identifies sequence, not authorization. Neither Operational
Closure/M4 nor `DELTA-20260928-001` authorizes NG-4 work. Starting NG-4 research/design requires
its own explicit authorization or decision under AgencyOS governance.

## Latest published delta

`DELTA-20260928-001`: PUBLISHED — publication basis 4092094d803577a79bcd74d0e122a384843787be;
verified 2026-09-28T23:15:18Z.
