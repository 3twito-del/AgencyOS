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

Commits after Build 97 include Control Room documentation and Self-Update V1 engineering tooling,
tests and CI wiring outside `docs/`; they do not change the released Build 97 product identity.
In `b3f41bf..operational-regression-gate`, no path under `src/` changed. Full release-identity
evidence remains governed by closure state section 1.

## Operational closure

- **Operational Closure: COMPLETE.** M1–M4 are COMPLETE.
- The C1–C16 terminal dispositions are governed by the specialised closure state.
- Feature freeze: EXITED.
- No closure action remains.
- Reopening requires fresh contradictory canonical evidence or a genuine future operational
  failure.

Authority: [`docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`](../reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md).
The closure register is not duplicated here.

## Self-Update V1

- **Detector:** IMPLEMENTED — read-only canonical evidence detector with local Canonical Alert rendering; post-dogfood alert/state semantics hardened at `3331ea0481304385ad199d1cdf5317cb02997d91` and exact-commit CI-verified by run `36581526345` (#111, `workflow_dispatch`, success).
- **Publisher:** IMPLEMENTED — payload v1.1, fixture-validated, real canonical dogfood verified by `DELTA-20260929-001`, and post-dogfood hardened at `3331ea0481304385ad199d1cdf5317cb02997d91` with exact-commit CI run `36581526345` (#111, success).
- **Real canonical dogfood:** PASS — `DELTA-20260929-001` reached `PUBLISHED_VERIFIED` through P7 remote readback; a later R7 `resume` re-read the sealed remote and changed nothing.
- **Terminal acceptance:** COMPLETE — Control Room terminal review on 2026-09-29 accepted Self-Update V1 after the real-dogfood findings were repaired and whole-surface consistency was verified. Bounded accepted residual limitations are recorded in `DELTA-20260929-002`. Completing Self-Update V1 did not itself authorize NG-4; current NG-4 authorization is governed by the Next-generation stage and its cited Owner decision.

## Next-generation stage

| Stage | Status | Decision |
| --- | --- | --- |
| NG-0 Baseline Lock | COMPLETE | — |
| NG-1 Representation Invariant Study | CLOSED | `DECISION-20260928-001` |
| NG-OD1 Subject-family decision | APPROVED | `DECISION-20260928-002` |
| NG-2 Subject Model & Boundary | CLOSED | `DECISION-20260928-003` |
| NG-3 Commercial Chain Decomposition | CLOSED | `DECISION-20260928-004` |
| NG-4 Vertical Extension Architecture | CLOSED | `DECISION-20260929-002` |
| NG-5 Mandate–Commercial Lineage Architecture | CLOSED | `DECISION-20260929-005` |
| NG-6 Commercial Arrangement–Legal Instrument Composition Architecture | AUTHORIZED | `DECISION-20260929-006` |

NG-4 is CLOSED under `DECISION-20260929-002` after terminal Control Room architecture adjudication. `DECISION-20260929-001` remains the Owner authorization that permitted its research/design. No implementation, product code, schema/API/domain expansion or NG-5 authorization follows.

NG-5 is CLOSED under `DECISION-20260929-005` after terminal Control Room architecture adjudication: each Commercial Arrangement has conceptual direct lineage to zero or one Representation Mandate, anchored to the specific historical mandate relevant to that fact's authority provenance; pre-arrangement facts may carry optional fact-local lineage; a several-mandate, role-typed relation is not justified now and is retained only as an explicit reconsideration trigger. `DECISION-20260929-003` remains the Owner authorization that permitted its research/design. No implementation, product code, schema/API/domain expansion or NG-6 authorization follows.

Owner authorization for NG-6 research/design is `DECISION-20260929-006`; its exact Scope Lock is `DECISION-20260929-007`. The authorization is research/design only, and substantive work begins with NG-6A only after publication. No implementation, product code, schema/API/domain expansion or migrations follow, and NG-7 remains unauthorized.

## Closed architecture decisions

- Representation Mandate boundary: `DECISION-20260928-001`.
- Subject family (Person + Group + External Organization): `DECISION-20260928-002`.
- Typed represented-subject boundary: `DECISION-20260928-003`.
- Hybrid commercial decomposition: `DECISION-20260928-004`.
- Vertical Extension Architecture: `DECISION-20260929-002`.
- Mandate–Commercial Lineage Architecture: `DECISION-20260929-005`.

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
- the agreement snapshot;
- the amount-determination record;
- multi-arrangement instruments;
- the future commission model;
- ledger integration.

**Vertical architecture**
- exact persistence, schema, API, interface and module mechanics remain deferred;
- any new C3 kind referenced by a shared/core role requires explicit reviewed bridge expansion;
- Work/IP promotion to core remains evidence-triggered and is not decided merely by cross-vertical use.

**NG-6**
- the Commercial Arrangement / Agreement Snapshot / Legal Instrument contract, including the agreement snapshot and
  multi-arrangement instruments listed under Commercial, is the subject of NG-6 and remains open.

## Owner decisions

- Next-generation:
  - `DECISION-20260928-002`: represented-subject family.
  - `DECISION-20260929-001`: Owner authorization to begin NG-4 — Vertical Extension Architecture for research/design only; it does not authorize product code, schema/API/domain expansion, NG-5 or implementation.
  - `DECISION-20260929-003`: Owner authorization to begin NG-5 research/design only; no implementation/schema/API/domain expansion or later-stage authorization.
  - `DECISION-20260929-006`: Owner authorization to begin NG-6 — Commercial Arrangement–Legal Instrument Composition Architecture for research/design only; no implementation/schema/API/domain expansion, migrations or later-stage authorization.
- Operational closure: the Owner decisions in force (D1–D5, Decision C and the others) are in closure state section 6, and the residual acceptance (C12, C14) in section 5. They are not migrated here.

## Evidence limitations

- The NG-1 to NG-3 decisions were made before this durable Control Room ledger existed, and were
  migrated into it by the published `DELTA-20260928-001`.
- The migration preserves the prior adjudications. It is not a new research pass.
- Exact implementation details intentionally remain open where listed above.
- Build 97 remains the product baseline being inspected. No next-generation architecture is
  implemented.
- NG-4 is a conceptual architecture decision; no next-generation implementation exists, and exact
  implementation mechanics remain unselected.
- NG-5 is a conceptual architecture decision; no next-generation implementation exists, and exact
  implementation mechanics remain unselected.
- NG-6 is authorized for research/design but no substantive NG-6 research has yet been completed
  or canonically accepted.

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

**Begin NG-6A — Canonical & Build-97 Legal-Composition Baseline under `DECISION-20260929-006` and `DECISION-20260929-007`.**

Until NG-6 reaches a later separately authorized/accepted state:
- research/design only;
- no implementation;
- no product/schema/API/domain expansion;
- no migrations;
- no NG-7.

## Latest published delta

`DELTA-20260929-008`: PUBLISHED — Owner authorization for NG-6 Commercial Arrangement–Legal Instrument Composition Architecture research/design only under `DECISION-20260929-006`, scope-locked by `DECISION-20260929-007`; no implementation, schema/API/domain, migration or NG-7 authority follows. See `CANONICAL-DELTAS.md` for the publication receipt.
