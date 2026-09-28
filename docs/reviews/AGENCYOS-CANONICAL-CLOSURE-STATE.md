# AgencyOS canonical closure state

**Date:** 2026-09-28 · **Status:** current-state authority

This file is the single current-state authority for AgencyOS's operational-ALPHA closure. It
supersedes every earlier *current-state* statement in older reviews and release records (listed
in section 13). Those documents remain the historical evidence and are not edited. Where this
file and an older file disagree about the present, this file wins. Where they disagree about what
happened at the time, the older file is the record of that time.

It is written to be read without any chat history. Everything a reader needs to know about where
AgencyOS stands is here, or is named here with its location.

---

## 1. Current release

| | |
| --- | --- |
| Release | **ALPHA 0.1.0 build 97** |
| Product (executable) commit | **`b3f41bfd68e81ab42da899671f58e01f0988d3d2`** |
| Tag | **`alpha-b3f41bf`**, annotated object `3219b226f709c7dd784c6af52ce4869263a50085`, dereferences to `b3f41bf` |
| `AgencyOS.Windows.exe` SHA-256 | `80d4e48127cc119ea52e02caae3b443e5ccd2eeb41a563daf36b99d67c9e95de` |
| API contract | **17** (minimum supported 1) |
| Expected schema | `20260909072201_AiResultClassification` |
| Signing | **unsigned** (the ALPHA policy) |
| Release manifest | 111 artifacts, every hash verified |
| Authoritative CI | run `36404049885`, #109, `workflow_dispatch`, exactly `b3f41bf`, success |
| Qualifying Nightly | run `36427576682`, #58, `nightly.yml`, `workflow_dispatch`, exactly `b3f41bf`, success |
| Predecessor | build 96, `alpha-691e32f` → `691e32fc545a13d28fd40b1fd6c6671dcd2e55ac` |
| Release record | `docs/releases/ALPHA-0.1.0-build-97.md` |
| Branch | `operational-regression-gate` |
| Docs-only publication commit | the direct child of `b3f41bf` that adds this file and the build-97 record. A file cannot contain the hash of the commit that contains it; read it with `git log -1 --format=%H -- docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`. |
| `master` | `c85bf42c4ce1c120d68c2566134f3ec785c7ba1c` (the validation-infrastructure closure record); not merged with this branch |

**Released identity versus later commits.** The released product is exactly `b3f41bf`. Anything
after it on the branch is documentation. Check with
`git diff --stat b3f41bf..operational-regression-gate`: every path should be under `docs/`.

CI #109 results: build 0 warnings and 0 errors; Unit 4,145; Windows 1,846; Reviewer 163;
Integration 952 on PostgreSQL 18.6 with `pg_dump` and `pg_restore` 18.6; OpenAPI 3.1.1, 264 paths,
188 schemas; TLC 4/4. Every count had 0 failed and 0 skipped.

Nightly #58 results: Integration 952/0/0 on `postgres:18.6` with `postgresql-client-18` 18.6; the
Windows nightly artifact `agencyos-nightly-20260928.58` built and uploaded.

## 2. Final C1–C16 register

The definitions come from the feature-freeze exit criteria. C1–C12 were set in Reality Closure
Phase I; C13 and C14 were added in Phase II; C15 and C16 in Phase III. "Core journeys" means J1,
J3, J4, J5, J6, J7 and J12: talent to target, negotiation, terms to paper, execution, money, next
action, and takeover.

| # | Criterion | Final disposition | Evidence basis |
| --- | --- | --- | --- |
| C1 | No known false operator assertion in core journeys | **PROVED** | Reality Closure waves 1–12, build 95 (`alpha-901ba19`); reconciled 2026-09-24 |
| C2 | No known visible/accessible role disagreement in core journeys | **PROVED** | Waves 1–12; none known since build 83 |
| C3 | Every value a core surface displays is also announced | **PROVED** | Build 96 real-Windows RC on `691e32f`, including the representation-scope row and Narrator speech (A–E, Contracts row). Correction chain: a "known unsatisfied residual" on 2026-09-24, then proved in M1 |
| C4 | No known cross-surface contradiction in core journeys | **PROVED** | Waves 1–12 |
| C5 | No core business truth is API-only without an accepted limitation | **PROVED** | Waves 1–12 |
| C6 | Every core operator question has an authoritative or explicitly partial home | **PROVED** | Waves 1–12 |
| C7 | Core workflows executable without developer knowledge | **PROVED** | Build 96 real-Windows RC: the five-step story (represent → pursue/negotiate → paper/execute → originate/collect money → know next) through the Windows client |
| C8 | Search differences are intentional or repaired | **PROVED** | Waves 1–12 |
| C9 | The operational regression gate exists and passes | **PROVED** | The operational regression gate (`docs/reviews/operational-regression-gate/`); CI #107 on `691e32f` |
| C10 | A genuinely blind takeover succeeds under non-compromised isolation | **PROVED** | Section 4 |
| C11 | The default branch's validation signal is readable | **PROVED** | Scheduled Nightly `35972891307` on `master@e92a7e7` (`docs/reviews/VALIDATION-INFRASTRUCTURE-CLOSURE.md`) |
| C12 | Residual limitations are explicitly accepted | **OWNER ACCEPTED / SATISFIED** | Section 5 |
| C13 | Money is never displayed without its currency | **PROVED** | Waves 1–12 |
| C14 | Every shipped capability has an operator route or a recorded exception | **OWNER ACCEPTED / SATISFIED** | Section 5: the three deferred capability exceptions |
| C15 | No operator-delivered capability requires the API unless accepted | **PROVED** | Waves 1–12 |
| C16 | Every capability's calibrating value is visible | **PROVED** | Wave 11 closed F-14 |

The definitions and the Phase I → II → III → reconciled status chain are in the git-ignored
`artifacts/operational-alpha/reality-closure/20-FEATURE-FREEZE-EXIT-CRITERIA.md`, with the wave
records beside it. All seventeen Reality Closure F-ID entries are terminal (F-08 is folded into
F-02, so sixteen independent root mechanisms).

## 3. Closure stages

| Stage | Definition | Status |
| --- | --- | --- |
| **M1 — Operational Regression Gate** | Prove C3, C7 and C9 on one final candidate: the regression gate implemented and green, and a real-Windows release candidate proving the core journey and announced/visible parity. | **COMPLETE** (build 96, `691e32f`) |
| **M2 — Genuine Blind Takeover** | Prove C10: a fresh, isolated operator with no repository or builder context answers "where do we stand" from the Windows UI alone, and any product failure it exposes is repaired and proved on the affected journey. | **COMPLETE** (section 4) |
| **M3 — Owner Residual Acceptance and Feature-Freeze Exit** | Owner acceptance of C12 and C14, then a release gate on the exact closure commit: authoritative CI, a qualifying exact-SHA Nightly, a verified manifest and an annotated tag. | **COMPLETE** with the build-97 release gate (section 1) |
| **M4 — Project Self-Sufficiency** | A fresh Control Room Project reconstructs this state from durable sources alone, so the external setup chat is no longer needed. | **NOT YET COMPLETE** |

## 4. C10 terminal chain

1. **Original genuine blind takeover** on build 96 (2026-09-27/28).
   - Fresh `claude -p` operator in `C:\OperatorRuns\wren-4821`; clue "Solenne Achterberg. Tell me
     where we stand."
   - Classified **BLIND**. The takeover failed on three product defects.
   - Seals: sealed truth `e3091c328c11add4f919752e1bd7f0f396b5a419c5801c1738f4d1cb9eed8af3`;
     transcript `52d9fd0d3bdab5debd424b414939294e7b8ec25440abddd677c844ed9f085568`; evidence
     manifest `3ec5e1c57b79629c04f79ed9fe4076ec382c9dbe0f66ffb11f5b4ce1f2f4ba3b`. Package
     `AgencyOS-B96-C10-BLIND-EVIDENCE.zip`, SHA-256
     `95db05cc7fb79f30e0b12c8a3a9b6677970a1937cf783a0fdc33b870f372162e`.
2. **The three failures.**
   - **BF-01:** a no-match People search said the directory was empty ("No people yet").
   - **BF-02:** the true count of 3 contract differences could not be opened, because the
     comparison was gated on visible draft-term rows. The count itself was always correct.
   - **BF-03:** Deal, Pipeline and Contract Activity omitted the event time, and Deal Activity
     explained a superseded offer with a raw GUID. The time was always in the data.
3. **Repair** `967e8e4`, CI #108 (`36371030038`) green.
4. **Affected blind rerun** on `967e8e4` (alpha/108 validation build), fresh operator in
   `C:\OperatorRuns\heron-6309`.
   - The takeover answer was materially correct and BLIND.
   - BF-01 and BF-03 passed live.
   - BF-02 was **not demonstrated**: the operator reached the enabled "Reconcile" command and
     declined it under its read-only instruction, because nothing said the action only reads.
   - Seals: transcript `b45a8095ebb62d1216b671266e52a13d7a60742fcb10181edcb72b6b73507b70`;
     evidence manifest `d3689f369eb6fb6fbd64998d363565326647a45d32d5947207aed32492c3f6fe`.
   - A builder-only GET corroborated the three results with zero writes. It was **not** counted as
     operator capability.
5. **Terminal BF-02 repair** `b3f41bf`.
   - The command became **"Review differences"**, with accessible help and a tooltip reading
     "Read-only comparison. Shows how this draft differs from the agreed terms. Does not change the
     contract."
   - The comparison tab says the same before and after use.
   - The path remains one GET; no API, domain or permission change.
6. **Targeted operator proof** on `b3f41bf` (alpha/109 validation build, whose executable is
   byte-identical to build 97's).
   - Fresh operator in `C:\OperatorRuns\brook-9022`. Clue: "Solenne Achterberg. Without changing
     any data, AgencyOS says her contract differs from the agreed terms in three places. Tell me
     exactly what those differences are."
   - The operator saw the read-only semantics before invoking, invoked "Review differences", and
     reported three `MissingFromContract` results: **140,000.00 GBP**, **First position**,
     **2027-02-01**.
   - **Zero business writes:** the database write counter was 686 before and after, and all 31 API
     requests were GETs.
   - Transcript seal `4a40d1fbb5f38cb807fab58e50b58eacd6f02da7292fd3fe4d62de566086dcba`.
   - Evidence-manifest seal `b05f2f23c096195858bc87146bc29448266a29437090a992ecb7a613c16b3e51`.
   - Terminal evidence package `AgencyOS-b3f41bf-BF02-TERMINAL-EVIDENCE.zip`, SHA-256
     `e1304ecfb353e1c7f6847ea1734595e0cfee339c87547540b05540fd82bbd25d`.
   - Packaging disclosure: the database server log `pg.log` gained 1,092 bytes of shutdown lines
     after the seal. Its first 1,460 bytes match the sealed hash exactly, and the package manifest
     records this.
7. **Adjudication:** Control Room adjudicated **C10 PROVED** and **BF-01, BF-02, BF-03 CLOSED**.

The code-level correction chain, with reproductions and negative controls, is in
`docs/reviews/blind-takeover/C10-BLIND-FAILURE-REPAIR-EVIDENCE.md`.

The raw run evidence is git-ignored, on the AgencyOS host:
- `artifacts/reviewer/c10-20260928/`
- `artifacts/reviewer/c10-rerun-967e8e4-20260928T0546Z/`
- `artifacts/reviewer/c10-bf02-b3f41bf-20260928T0931Z/`

## 5. Accepted residual limitations (C12) and recorded exceptions (C14)

The owner accepts each of the following as knowingly imperfect at this closure. None is a known
false statement on an exercised surface.

1. **`UpdateTalentProfileAsync`**: a deferred capability exception. There is no operator route,
   and it was deliberately not investigated.
2. **Direct `CreateProspectAsync`**: a deferred capability exception, on the same basis.
3. **The offline write queue**: a deferred capability exception, on the same basis.
4. **Reconciliation compares structured terms.** It compares the terms recorded on the deal with
   those recorded on the contract version. It is not document reading or legal interpretation,
   and a version with no transcribed terms reads as every agreed term "not carried into the
   draft".
5. **Project → Commercial may be partial.** For example, it may not list a contract that exists.
6. **Internal owner identity may lack People-directory context.** An internal user who owns or is
   assigned work may have no People record, so the directory cannot show their membership.
7. **Finance secondary identifiers.** The recorded Finance activity and ledger lines may show
   receivable identifiers as secondary text.
8. **"Disciplines" versus "Acting, Theatre".** A talent profile's "Disciplines: None recorded" can
   appear beside a header naming the represented areas. They are different facts under
   ambiguous labels.

Items 1–3 are the C14 exceptions; all eight are the C12 list. This list is closed: nothing is
accepted that is not written here.

## 6. Owner decisions in force

- **D1 — BROAD BUT BOUNDED announced/visible parity.**
- **D2 — Real Windows final candidate mandatory.** Closure needs authoritative CI, PostgreSQL
  18.6, negative controls and sealed real-Windows evidence. A fragile GUI Nightly is not required.
- **D3 — The final C7 chain:** represent → pursue/negotiate → paper/execute → originate/collect
  money → know next.
- **D4 — PRIMARY overflow.** The target is 160 characters; an overflowing PRIMARY value keeps its
  role and a recognisable fragment, with the full value on the same row. Frozen: 110/70/18 (the
  owner's wording).
- **D5 — TRUTH-FIRST.** PRIMARY truth outranks the 160 target; the full truth stays on the same
  row; no arbitrary cap and no reclassification; same-mechanism siblings need no repeated
  approval. Recorded in full in `docs/reviews/operational-regression-gate/PRODUCT-REPAIR-EVIDENCE.md` §10.
- **Decision C.** Representation and the talent profile stay separate. After signing, the page
  says a talent profile is still needed and offers to create one; it is created only when the
  operator chooses, and its state is shown truthfully.
- **Audit-002 precondition classifications.** `ApproveAiActionDialog` and
  `IngestAttachmentDialog` are intentionally external preconditions. `ResolveParticipantDialog` is
  an audit fixture gap (`docs/reviews/audit-002/final-closure/AUDIT-002-FINAL-PRECONDITION-DECISIONS.md`).
- **Environment authority** (`docs/reviews/VALIDATION-INFRASTRUCTURE-CLOSURE.md` §6):
  - PostgreSQL 18.6 is the authoritative ALPHA validation baseline.
  - Local PostgreSQL 19 beta is LAB corroboration only.
  - An older local `pg_dump` is not a blocker.
  - Historical contract-14 LAB state is retired.
  - The canonical Windows provisioning platform is `windows-x64`.

## 7. Control Room / Claude boundary

- **The owner** makes every final decision.
- **The ChatGPT Control Room** is critic, adjudicator, planner, evidence reviewer and prompt
  author. It does not implement, commit or push AgencyOS.
- **Claude** (Claude Code on the AgencyOS host) is the sole AgencyOS executor.
- **A Claude report, or Claude's confidence, is not proof.** A status holds only when the evidence
  behind it has been checked against the question it answers. Green tests alone do not settle an
  operator claim.

## 8. Evidence hierarchy

**What decides what is true** — the normative/source hierarchy, applied to the exact question:

domain invariants → ADR/governance → persistence/query truth → API contract → client model → UI →
tests → release notes → Claude report.

**What proves it** — the evidence ladder, weakest to strongest:

source → deterministic semantic tests → integration/API → live Windows → accessibility/keyboard →
genuine isolated blind operator → real operations.

**API-only evidence does not prove normal Windows operator capability**, unless that capability
has been explicitly accepted as API-only. A builder-side API read, like the BF-02 GET in section 4,
corroborates server truth and zero writes. It does not show that an operator can reach or
understand the capability.

## 9. Feature freeze

**FEATURE FREEZE = EXITED**, with the successful build-97 release gate.

No broad closure audit, census or blind run may be reopened without fresh contradictory canonical
evidence or a genuine future operational failure.

## 10. Product hypothesis

**THESIS DEMONSTRATED — OPERATIONAL-ALPHA CLOSURE SCOPE.**

The verdict stayed at THESIS PARTIALLY DEMONSTRATED from build 78 to build 96. The reason was that
J12, a genuinely blind takeover, had not been proved. That evidence now exists (section 4): a
fresh operator with no builder context reconstructed a client's commercial story from the Windows
UI, and the product failures it exposed are repaired and proved.

This verdict covers the operational-ALPHA scope proved here: the core journeys on a synthetic
tenant. It does not say that the wider AgencyOS vision in `docs/00_VISION.md` is complete.

## 11. Next permitted action

**M4 — fresh ChatGPT Project self-sufficiency test**

No further product closure work is permitted without fresh contradictory evidence.

## 12. Forbidden now

- Reopening any closed criterion, stage or F-ID without fresh contradictory canonical evidence.
- A new census, closure audit or blind run without such evidence.
- Product changes presented as closure work.
- Retagging or moving `alpha-b3f41bf`, or tagging a docs-only commit as a release.
- Merging to `master`, force-pushing or rewriting history without an explicit instruction.
- Weakening authorization, auditability, data integrity, tests or update enforcement.
- Treating a Claude report, green tests or API-only evidence as operator proof.

## 13. What this supersedes

These statements are historical and no longer current. The documents themselves are unchanged:

- `docs/releases/ALPHA-0.1.0-build-96.md` §6: "C10 NOT YET PROVED", "C12, C14 OWNER ACCEPTANCE
  REQUIRED", "Feature freeze ACTIVE", "THESIS PARTIALLY DEMONSTRATED".
- `docs/reviews/VALIDATION-INFRASTRUCTURE-CLOSURE.md` §8: C3 as a residual; C7, C9 and C10 not yet
  proved; C12 and C14 awaiting acceptance; the freeze active.
- `docs/reviews/blind-takeover/C10-BLIND-FAILURE-REPAIR-EVIDENCE.md` §10 and §11: "still to be
  proved: authoritative CI and one targeted operator proof". Both are now done (sections 1 and 4).
- The thesis line in releases 78–96: "THESIS PARTIALLY DEMONSTRATED".
- `artifacts/operational-alpha/reality-closure/20-FEATURE-FREEZE-EXIT-CRITERIA.md`, "Current status
  — reconciled 2026-09-24" (git-ignored).

## 14. Sources

In the repository:
- `docs/releases/ALPHA-0.1.0-build-96.md`, `ALPHA-0.1.0-build-97.md`
- `docs/reviews/VALIDATION-INFRASTRUCTURE-CLOSURE.md`
- `docs/reviews/operational-regression-gate/PRODUCT-REPAIR-EVIDENCE.md`
- `docs/reviews/blind-takeover/C10-BLIND-FAILURE-REPAIR-EVIDENCE.md`
- `docs/reviews/audit-002/final-closure/AUDIT-002-FINAL-PRECONDITION-DECISIONS.md`
- `CLAUDE.md`

On GitHub (`3twito-del/AgencyOS`):
- tags `alpha-b3f41bf` and `alpha-691e32f`;
- CI runs `36404049885` (#109) and `36371030038` (#108);
- Nightly run `36427576682` (#58).

On the AgencyOS host (git-ignored):
- `artifacts/operational-alpha/reality-closure/`
- `artifacts/reviewer/c10-*`

Owner and Control Room dispositions (C10 PROVED; C12 and C14 accepted; D1–D4 wording; the residual
list; the stage definitions; the boundary) are recorded here on the owner's instruction of
2026-09-28.
