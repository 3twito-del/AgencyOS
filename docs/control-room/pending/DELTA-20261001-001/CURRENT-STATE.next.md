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
| NG-6 Commercial Arrangement–Legal Instrument Composition Architecture | CLOSED | `DECISION-20260929-008` |
| NG-7 Amount Determination & Economic Truth Architecture | CLOSED | `DECISION-20260930-001` |
| NG-8 Agency Commission & Representation-Economics Claim Architecture | CLOSED | `DECISION-20260930-004` |
| NG-9 Receivable Crystallization & Collectibility Architecture | CLOSED | `DECISION-20260930-006` |
| NG-10 Payment, Cash Application & Funds Provenance Architecture | CLOSED | `DECISION-20260930-008` |
| NG-11 Distribution Obligation, Setoff & Outbound Settlement Architecture | CLOSED | `DECISION-20260930-010` |
| NG-S1 Whole-System Architecture Synthesis & Coherence Gate | AUTHORIZED | `DECISION-20261001-001` |

NG-4 is CLOSED under `DECISION-20260929-002` after terminal Control Room architecture adjudication. `DECISION-20260929-001` remains the Owner authorization that permitted its research/design. No implementation, product code, schema/API/domain expansion or NG-5 authorization follows.

NG-5 is CLOSED under `DECISION-20260929-005` after terminal Control Room architecture adjudication: each Commercial Arrangement has conceptual direct lineage to zero or one Representation Mandate, anchored to the specific historical mandate relevant to that fact's authority provenance; pre-arrangement facts may carry optional fact-local lineage; a several-mandate, role-typed relation is not justified now and is retained only as an explicit reconsideration trigger. `DECISION-20260929-003` remains the Owner authorization that permitted its research/design. No implementation, product code, schema/API/domain expansion or NG-6 authorization follows.

NG-6 is CLOSED under `DECISION-20260929-008` after terminal Control Room architecture adjudication: a Commercial Arrangement may have zero or more Legal Instruments; each Legal Instrument is arrangement-scoped to one Commercial Arrangement (L2), with one instrument covering several Arrangements retained only as an explicit evidence-triggered reconsideration case; Agreement Snapshot carries historical agreed-state semantics (S2) without a selected persistence or derivation mechanism; a known Snapshot↔Instrument correspondence stays historically anchored; no universal precedence between commercial and legal truth is defined. `DECISION-20260929-006` remains the Owner authorization that permitted its research/design, and `DECISION-20260929-007` the Scope Lock that governed it. No implementation, product code, schema/API/domain expansion, migrations or NG-7 authorization follows.

NG-7 is CLOSED under `DECISION-20260930-001` after terminal Control Room architecture adjudication: an Amount Determination is a distinct arrangement-scoped economic fact for one independently meaningful economic component, and one Arrangement may have zero or more; method/basis and historical realization are semantically distinct; methods may exist before all inputs, before receivable/invoice/payment and before a final Legal Instrument; known source provenance stays historically anchored and may involve commercial, legal, external-schedule and realized-input facts; money preserves currency, units and percentage basis; commission reuses determination semantics but remains a distinct representation-economics claim; Receivable, Invoice, Payment/Allocation and accounting projection remain downstream and distinct. E0 rejected, E1 rejected, E2 accepted with refinement, E3 rejected; implementation remains unselected. `DECISION-20260929-009` remains the Owner authorization that permitted its research/design, and `DECISION-20260929-010` the Scope Lock that governed it. No implementation, product code, schema/API/domain expansion, migrations or NG-8 authorization follows.

NG-8 is CLOSED under `DECISION-20260930-004` after terminal Control Room architecture adjudication: a Representation-Economics Claim is a distinct historical, arrangement-scoped, claimant-specific agency-side economic claim; rule/terms are distinct from claim; a claim may exist before quantification; source/authority provenance is historical and may be composite; claim-side determination reuses NG-7 Amount Determination semantics; source-defined earned/payable/collected facts are distinct and no universal lifecycle is selected; one claim may depend on one or more represented-party Amount Determinations only where they form one source-defined basis under one independently meaningful commission treatment; the claim is distinct from Receivable, Invoice, Payment/Allocation and accounting. C0 rejected, C1 rejected, C2 accepted with refinement, C3 rejected; implementation remains unselected. `DECISION-20260930-002` remains the Owner authorization and Scope Lock that permitted it. No implementation, product code, schema/API/domain expansion, migrations or NG-9 authorization follows.

NG-9 is CLOSED under `DECISION-20260930-006` after terminal Control Room architecture adjudication: a Receivable is a distinct historical, Commercial-Arrangement-scoped collectible position for one independently meaningful source-defined crystallization/due position; upstream Amount Determination and Representation-Economics Claim remain distinct; no universal crystallization trigger exists; one upstream economic source may produce zero or more Receivables, and one Receivable may depend on one or more upstream facts only where they genuinely form one source-defined collectible position; partial/installment/periodic crystallization is distinct from partial Payment; Invoice is distinct and not universally required, but may be a source-defined condition; obligor, creditor/beneficiary and observed payer/source of cash remain distinct; the due rule and its provenance are distinct from the resolved date; historical correction, cancellation, settlement and assumption truth is not silently rewritten; Payment may precede Receivable; Payment/Allocation and accounting remain downstream and distinct. R0 rejected, R1 rejected, R2 accepted with refinement, R3 rejected; implementation remains unselected. `DECISION-20260930-005` remains the Owner authorization and Scope Lock that permitted it. No implementation, product code, schema/API/domain expansion, migrations or NG-10 authorization follows.

NG-10 is CLOSED under `DECISION-20260930-008` after terminal Control Room architecture adjudication: a Payment is a distinct historical observed/reported cash-movement fact; Receivable and Payment remain distinct; Application is an independent historical association of a defined portion of one Payment to one Receivable, with zero or more Applications per Payment and per Receivable; partial Application is distinct from partial Receivable crystallization; current balances do not replace Application history; Application reversal does not reverse cash; refund/recovery involving cash movement is later historical cash truth; unapplied amount does not establish beneficial ownership or unrestricted availability; source-defined Funds Holding / Control semantics are required where beneficial entitlement or control differs from physical receipt, and no universal trust-account entity follows; payer, source-of-funds, recipient/conduit and beneficiary roles remain distinct where known; payment dates are source-specific where more than movement and recorded dates matter; deductions/withholdings are not universally cash Applications; Payment and Receivable currencies remain distinct and conversion provenance is preserved where applicable; Ledger/accounting remains downstream. P0 rejected, P1 accepted with refinement, P2 accepted with refinement, P3 rejected; implementation remains unselected. `DECISION-20260930-007` remains the Owner authorization and Scope Lock that permitted it. No implementation, product code, schema/API/domain expansion, migrations or NG-11 authorization follows.

NG-11 is CLOSED under `DECISION-20260930-010` after terminal Control Room architecture adjudication: a source-defined Distribution Obligation is a distinct historical obligation/position to transfer, distribute, remit or return defined value to an entitled party, distinct from Funds Holding / Control, Payment, Receivable, Application and Ledger/accounting; outgoing cash reuses terminal NG-10 Payment, and Payment alone does not prove discharge; cash discharge requires a distinct obligation-specific historical Cash Discharge Link (one outgoing Payment, one Distribution Obligation, a defined portion), which is not NG-10 Application; source-defined non-cash reduction/satisfaction is preserved without fabricated Payment, and a source-defined net obligation needs no fabricated discharge fact; no universal Payable, Settlement, Setoff, Transaction or MoneyMovement; Ledger remains downstream. D0 rejected, D1 rejected, D2 accepted with refinement, D3 rejected; implementation remains unselected. `DECISION-20260930-009` remains the Owner authorization and Scope Lock that permitted it. No implementation, product code, schema/API/domain expansion, migrations or NG-12 authorization follows.

NG-S1 — AUTHORIZED for research/design only under `DECISION-20261001-001`, which locks its exact scope and supersedes `DECISION-20260930-011`, replacing only its fixed prompt-count mechanism. NG-S1 is a whole-system synthesis/falsification gate over the closed NG-1 through NG-11 architecture; it is not NG-12 and adds no new domain capability. Substantive NG-S1 research has not yet begun. Current estimated likely remaining substantive prompts: approximately 2; forecast only, not a cap, quota, authorization boundary, or stopping rule; revise when evidence changes the path. Its terminal architecture status will be exactly one of COHERENT, COHERENT WITH TARGETED GAPS or NOT YET COHERENT. No implementation, product code, schema/API/domain/client/UI expansion, persistence selection or migrations follow, and NG-12 remains unauthorized.

## Closed architecture decisions

- Representation Mandate boundary: `DECISION-20260928-001`.
- Subject family (Person + Group + External Organization): `DECISION-20260928-002`.
- Typed represented-subject boundary: `DECISION-20260928-003`.
- Hybrid commercial decomposition: `DECISION-20260928-004`.
- Vertical Extension Architecture: `DECISION-20260929-002`.
- Mandate–Commercial Lineage Architecture: `DECISION-20260929-005`.
- Commercial Arrangement–Legal Instrument Composition Architecture: `DECISION-20260929-008`.
- Amount Determination & Economic Truth Architecture: `DECISION-20260930-001`.
- Representation-Economics Claim Architecture: `DECISION-20260930-004`.
- Receivable Crystallization & Collectibility Architecture: `DECISION-20260930-006`.
- Payment, Cash Application & Funds Provenance Architecture: `DECISION-20260930-008`.
- Distribution Obligation, Setoff & Outbound Settlement Architecture: `DECISION-20260930-010`.

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
- the exact Agreement Snapshot persistence/derivation mechanism and Snapshot↔Instrument anchoring representation;
- the exact persistence/derivation/version representation for Amount Determination methods and realizations;
- the exact component-relation representation;
- one Legal Instrument covering several Arrangements, reconsidered only on the `DECISION-20260929-008` evidence trigger;
- the exact persistence/placement of commission rules and Representation-Economics Claims, and their representation-authority linkage mechanics;
- the exact Receivable persistence/placement and claim/source-contribution linkage implementation;
- the exact Invoice link mechanics and the persistence/placement of Payment, Application and Funds Holding / Control semantics (their semantics are terminal under `DECISION-20260930-008`);
- the exact provenance/role, Application effective-date, refund/recovery, cross-currency Application and trust/client-account implementation mechanics;
- the exact persistence/placement of Distribution Obligations, Cash Discharge Links and non-cash reduction/satisfaction facts, and their contribution-lineage, due/release/hold, correction and cross-currency representation (their semantics are terminal under `DECISION-20260930-010`);
- the exact downstream finance/ledger integration.

**Vertical architecture**
- exact persistence, schema, API, interface and module mechanics remain deferred;
- any new C3 kind referenced by a shared/core role requires explicit reviewed bridge expansion;
- Work/IP promotion to core remains evidence-triggered and is not decided merely by cross-vertical use.

## Owner decisions

- Next-generation:
  - `DECISION-20260928-002`: represented-subject family.
  - `DECISION-20260929-001`: Owner authorization to begin NG-4 — Vertical Extension Architecture for research/design only; it does not authorize product code, schema/API/domain expansion, NG-5 or implementation.
  - `DECISION-20260929-003`: Owner authorization to begin NG-5 research/design only; no implementation/schema/API/domain expansion or later-stage authorization.
  - `DECISION-20260929-006`: Owner authorization to begin NG-6 — Commercial Arrangement–Legal Instrument Composition Architecture for research/design only; no implementation/schema/API/domain expansion, migrations or later-stage authorization.
  - `DECISION-20260929-009`: Owner authorization to begin NG-7 / the next post-NG-6 stage for research/design only; its exact title and Scope Lock are not decided; no implementation/schema/API/domain expansion, migrations or later-stage authorization.
  - `DECISION-20260930-002`: NG-8 authorization and exact Scope Lock — Agency Commission & Representation-Economics Claim Architecture, research/design only; no implementation/schema/API/domain expansion, migrations or NG-9 authority.
  - `DECISION-20260930-005`: Owner authorization and exact Scope Lock for NG-9 Receivable Crystallization & Collectibility Architecture, research/design only; no implementation/schema/API/domain expansion, migrations or NG-10 authority.
  - `DECISION-20260930-007`: Owner authorization and exact Scope Lock for NG-10 Payment, Cash Application & Funds Provenance Architecture, research/design only; no implementation/schema/API/domain expansion, migrations or NG-11 authority.
  - `DECISION-20260930-009`: Owner authorization and exact Scope Lock for NG-11 Distribution Obligation, Setoff & Outbound Settlement Architecture, research/design only; no implementation/schema/API/domain expansion, migrations or NG-12 authority.
  - `DECISION-20261001-001`: governing Owner authorization and exact Scope Lock for NG-S1 Whole-System Architecture Synthesis & Coherence Gate, research/design only, with an adaptive prompt forecast that is never a cap; supersedes `DECISION-20260930-011`; no implementation/schema/API/domain/client/UI expansion, persistence selection, migrations or NG-12 authority.
- Control Room governance:
  - `DECISION-20260930-003`: Recursive Control-Room Correspondence & Transition Contract.
- Operational closure: the Owner decisions in force (D1–D5, Decision C and the others) are in closure state section 6, and the residual acceptance (C12, C14) in section 5. They are not migrated here.

**Control Room correspondence / transition policy** (`DECISION-20260930-003`):
- the configuration of the message itself is recursively preserved, not only workflow continuity;
- the correction chain, prompts, prompt budget, proved/open/forbidden and the exact next action are mandatory
  continuity fields;
- every future cross-conversation handoff must self-replicate the same configuration, including the instruction
  that the following handoff reproduce it again;
- the policy is continuity/governance only and grants no stage or product authority.

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
- NG-6 is a conceptual architecture decision; no next-generation implementation exists, and exact
  implementation mechanics remain unselected.
- NG-7 is a conceptual semantic architecture decision, based on bounded evidence from Build 97 plus exactly
  Film/TV and live music / artist booking; no next-generation implementation exists, and exact
  implementation remains unselected.
- NG-8 is a conceptual semantic architecture decision, derived from Build-97 predecessor inspection plus
  exactly Film/TV representation and live music / artist booking; no next-generation implementation exists,
  and exact implementation remains unselected.
- NG-9 is a conceptual semantic architecture decision, derived from exact Build-97 predecessor inspection plus
  exactly Film/TV representation and live music / artist booking; the primary evidence was bounded and includes
  historical/time-specific sources used for falsification; exact implementation remains unselected.
- NG-10 is a conceptual semantic architecture decision, derived from exact Build-97 predecessor inspection plus
  exactly Film/TV representation and live music / artist booking; the bounded primary evidence includes
  historical/time-specific sources used for falsification; exact implementation remains unselected.
- NG-11 is a conceptual semantic architecture decision, derived from exact Build-97 predecessor inspection plus
  exactly Film/TV representation and live music / artist booking; the bounded primary evidence includes UK SI 2003/3319
  regulation 25 / Schedule 2 with a recorded temporal-currentness limitation; exact implementation remains unselected.
- NG-S1 is authorized but no NG-S1 synthesis research has yet been canonically accepted.

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

**NG-S1A — Integrated Canonical Model: build the integrated concept model from the closed NG-1 through NG-11 decisions and identify candidate contradictions, overlaps, gaps and implementation-only questions, without inventing new semantics.**

Until NG-S1A begins under the published authorization:
- research/design only;
- no implementation of NG-4 through NG-11 architecture;
- no product/schema/API/domain/client/UI expansion;
- no persistence selection or migrations;
- no NG-12.

## Latest published delta

`DELTA-20261001-001`: PUBLISHED — NG-S1 prompt-count governance corrected under `DECISION-20261001-001`, which supersedes `DECISION-20260930-011`: the fixed two-prompt budget is replaced by an adaptive forecast (approximately 2 remaining substantive prompts; never a cap, quota, authorization boundary or stopping rule); NG-S1 scope, prohibitions, exit states and the NG-12 boundary are unchanged; no implementation, schema/API/domain, migration or NG-12 authority follows. See `CANONICAL-DELTAS.md` for the publication receipt.
