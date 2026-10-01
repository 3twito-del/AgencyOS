# AgencyOS canonical deltas

**Protocol:** [CANONICAL-STATE-PROTOCOL.md](CANONICAL-STATE-PROTOCOL.md)

The ledger of candidate changes to canonical state.

- **Delta identity and substantive content are append-only.** A delta is never deleted, reused,
  or rewritten into a different claim. Only the lifecycle metadata defined below may advance, and
  only according to [`CANONICAL-STATE-PROTOCOL.md`](CANONICAL-STATE-PROTOCOL.md).
- **Rejected deltas stay recorded**, with the reason.
- **A published correction never deletes an earlier entry.** It is a new entry that points at the
  entry it corrects.
- **This ledger does not itself override [`CURRENT-STATE.md`](CURRENT-STATE.md).** A delta is
  `PUBLISHED` only when it is also reflected there and the other publication conditions hold
  (protocol section 8).

## Substantive content and lifecycle metadata

### Substantive content (immutable once recorded)

The Delta ID, `Detected`, `Sources` (the evidence anchors), `Prior claim`, `Candidate/new claim`,
`Claimed transition`, `Scope`, `Evidence`, `Conflicts`, `Authority required`, `What changes if
accepted`, `Supersedes`, `Unchanged`, `Open / unresolved questions` and `Forbidden implications`
are never silently rewritten. A correction to any of them is a new delta that points to the
earlier one. The earlier claim is never edited into agreement.

### Lifecycle metadata (advances only through the protocol)

- **`Status`** moves forward only:
  - `PENDING_ADJUDICATION → ACCEPTED`, or `PENDING_ADJUDICATION → REJECTED`;
  - `ACCEPTED → PUBLISHED`.

  There is no backward transition. A `REJECTED` delta is never later converted into an accepted
  one; that needs a new delta.
- **`Adjudication`**, if first recorded as pending, is filled in exactly once with the
  authoritative adjudication for the status transition, and is immutable after that.
- **`Seal authorizations`** is append-only history, used by future staged transitions under
  [`CANONICAL-PUBLISHER-CONTRACT.md`](CANONICAL-PUBLISHER-CONTRACT.md):

  ```
  Pending → first immutable record → further immutable records appended as required
  ```

  - It starts as `Seal authorizations: Pending`. The first durable authorization replaces
    `Pending` with a collection holding one record, and the field never returns to `Pending`.
  - A later authorization is appended as a new record. No record is edited or deleted, and every
    record survives publication.
  - A record is appended only after the semantic basis it names has been verified by remote
    readback, and before the seal.
  - Each record contains at least:
    - a record identifier;
    - the scope `AUTHORIZE_SEAL_ONLY`;
    - the authority class (`CONTROL_ROOM` or `OWNER`);
    - the Delta ID;
    - the verified semantic-basis SHA;
    - the Git-blob SHA-256 of the staged `CURRENT-STATE.next.md`;
    - the Git-blob SHA-256 of the canonical `PUBLICATION-PAYLOAD.json`;
    - the authorization time in UTC;
    - a durable authority or adjudication reference;
    - the recorder's identity.

    It has no confidence value. The record syntax is defined in the contract, section 5.2.
  - A record binds only its own semantic basis and digests. It stops binding if that basis is
    replaced, or if a bound digest or staged byte changes. It is never carried forward to a new
    basis, which needs a new record.
  - Re-authorization is only for the same accepted semantic delta. If any substantive field of
    the delta changes, that is not re-authorization: it needs a new correction delta.
  - The ledger records that a seal authorization exists. The publisher may mechanically verify
    the record's presence, authority class, Delta ID, basis SHA, digests and the structure of its
    reference. It never decides whether the authority was substantively right. A chat transcript
    alone is not a durable seal authorization.
  - The field and its records are lifecycle metadata. They are excluded, as a whole block, from a
    delta's substantive digest (contract section 5.4).
  - The field applies to future staged transitions only. `DELTA-20260928-001` predates it, has no
    such field, and is not rewritten.
- **`Published`**: `Pending` is replaced exactly once, during sealing, by the UTC remote-readback
  timestamp of the publication-basis commit. It does not record the SHA or readback time of the
  sealing commit.
- **`Publication receipt`**: `Pending` is replaced exactly once, during sealing, by the
  publication-basis commit it names. It is immutable after that.

## Publication-basis commit

**Publication-basis commit:** the final pre-seal commit whose repository tree contains the accepted
delta, the complete intended `CURRENT-STATE.md`, and every applicable decision record in the exact
form adjudicated for publication, and whose contents have been verified by remote readback from
the canonical branch.

- A delta may span more than one preparatory or content commit.
- A delta's receipt names the final publication-basis commit only. It need not list the earlier
  preparatory commits.
- The later sealing commit does not need to contain or name its own SHA.
- A delta becomes `PUBLISHED` only after the sealing commit is itself pushed and read back from the
  canonical remote (protocol section 8).

### Staged transitions under Self-Update V1

For a future staged transition under
[`CANONICAL-PUBLISHER-CONTRACT.md`](CANONICAL-PUBLISHER-CONTRACT.md), the definition above is
narrowed as follows.

The final pre-seal publication-basis tree contains:
- the accepted delta;
- the applicable decision records;
- `docs/control-room/pending/<DELTA-ID>/CURRENT-STATE.next.md`;
- `docs/control-room/pending/<DELTA-ID>/PUBLICATION-PAYLOAD.json`;
- the durable `Seal authorizations` history;
- the still-active, previously published `CURRENT-STATE.md`.

In such a transition:
- `CURRENT-STATE.next.md` is the complete intended replacement for the active current state.
  Nothing under `docs/control-room/pending/` is canonical current state.
- The active `CURRENT-STATE.md` stays authoritative until the seal has been pushed and read back
  from the remote. At the seal, the exact verified staged bytes are promoted.
- The final publication basis is the latest valid authorization commit: the commit that appends
  the one record binding the remote-verified semantic basis (contract sections 4 and 5).

`DELTA-20260928-001` was a bootstrap migration published under the definition above before these
rules existed. Its candidate content was held in `CURRENT-STATE.md` itself, which was not yet the
active authority. That remains its historical record.

### Delta receipts and decision receipts

Decision receipts are governed by [`DECISIONS.md`](DECISIONS.md). A decision's receipt names that
decision's own first content-bearing publication commit. A delta's receipt names the final
publication-basis commit for the whole state transition. The two may legitimately name different
commits.

For `DELTA-20260928-001`:
- the four migrated decision receipts name `388534de2920cd4fe25074efdfb37b26b12bda23`, the
  decisions' first content-bearing publication commit, with remote readback verified at
  2026-09-28T23:15:18Z;
- the delta's receipt names `4092094d803577a79bcd74d0e122a384843787be`, the verified
  publication-basis commit, whose tree holds all four decisions, the delta, the final
  `CURRENT-STATE.md` including the NG-4 authorization boundary, and this section's rules. Its
  remote readback was verified at 2026-09-28T23:15:18Z;
- the sealing commit is `1a64ba4035f0453864ad5e7143e24b00da6e535c`, and the final sealed state was
  read back from the canonical remote at 2026-09-28T23:16:52Z.

The first delta recorded under this protocol is `DELTA-20260928-001` (under *Entries* below).
State changes before it are recorded in the prior durable sources the protocol names; they are not
reconstructed here.

---

## Template

Copy this block for each new entry, and replace `YYYYMMDD-NNN` with the detection date and a
sequence number.

```
## DELTA-YYYYMMDD-NNN

Status:
Detected:
Published:
Sources:
Prior claim:
Candidate/new claim:
Scope:
Evidence:
Conflicts:
Authority required:
Adjudication:
Seal authorizations: Pending
Supersedes:
Unchanged:
Open:
Publication receipt:
```

---

# Entries

## DELTA-20260928-001

Status: PUBLISHED

Detected: 2026-09-28T22:18:49Z

Published: 2026-09-28T23:15:18Z

Sources:
- `docs/control-room/CANONICAL-STATE-PROTOCOL.md` (published at `1d4c136`).
- `docs/control-room/DECISIONS.md`: `DECISION-20260928-001` to `-004`, migrated in this delta.
- `docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`: the operational-closure authority, sections
  1, 3, 6, 9 and 11.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, tag `alpha-b3f41bf`.
- The Control Room's pre-protocol NG-1, NG-2 and NG-3 adjudications and the Owner's NG-OD1
  approval, all of 2026-09-28. These are pre-protocol Control Room and Owner provenance, carried
  into this repository through the Control Room's Phase 2A migration instruction. No conversation
  transcript is a repository source, and none is cited as one.

Prior claim: `docs/control-room/CURRENT-STATE.md` is a `BOOTSTRAP PENDING` shell and holds no
substantive next-generation current state. `DECISIONS.md` holds no decisions.

Candidate/new claim: The durable current-state candidate records:
- the released product identity (ALPHA 0.1.0 build 97);
- terminal operational closure;
- NG-1, NG-2 and NG-3 as closed;
- the four governing decision records;
- NG-4 as the exact next bounded architecture stage.

Claimed transition: BOOTSTRAP PENDING → complete migration candidate; publication pending.

Scope: Canonical-state migration only. No product behaviour and no architecture decision is newly
made by this delta. It records decisions already made and state already established.

Evidence:
- **Machine-verifiable facts**, re-verified on 2026-09-28 at detection:
  - tag `alpha-b3f41bf` is annotated object `3219b226f709c7dd784c6af52ce4869263a50085` and
    dereferences to `b3f41bf`, locally and on `origin`;
  - CI run `36404049885` (#109, `workflow_dispatch`) and Nightly run `36427576682` (#58,
    `workflow_dispatch`) both succeeded on exactly `b3f41bf`;
  - `ApiContract.Current = 17` and the migration `20260909072201_AiResultClassification` are
    present at `b3f41bf`;
  - the build-97 release record carries the EXE SHA-256 recorded in the closure state;
  - every path changed in `b3f41bf..1d4c136` is under `docs/`.
- **Control Room adjudications**: NG-1, NG-2 (NG-2A and NG-2B) and NG-3, recorded as
  `DECISION-20260928-001`, `-003` and `-004`.
- **Owner decision**: the represented-subject family (NG-OD1), recorded as
  `DECISION-20260928-002`.
- **Evidence level**: the architecture decisions rest on source inspection of Build 97 and bounded
  cross-vertical falsification. They are design adjudications, not implementation or operator
  evidence.

Conflicts: None after applying scoped canonical precedence. Two points are recorded, not erased:
- Older seed and update files, including the Control Room Project bootstrap sources outside the
  repository and the pre-closure release and review records the closure state lists in its
  section 13, contain stale states. They are historical, and they lose to the closure state and
  to this migration for the present.
- The closure state's section 11 says "None pending". That is scoped to operational closure, and
  still holds. The same section says next-generation work needs its own explicit authorization;
  naming NG-4 as the next bounded action does not itself begin or authorize NG-4 work.

Authority required:
- machine-verifiable fact, for the repository and release identity;
- `CONTROL_ROOM`, for the migration and for the adjudicated architecture state;
- `OWNER`, as the source decision for the represented-subject family (already given; not asked
  for again here).

Adjudication: The Control Room accepted this migration for drafting in its Phase 2A instruction,
with the decision texts, IDs and current-state content specified there. This exact candidate is
submitted to the Control Room for inspection before publication.

What changes if accepted: Once publication completes (protocol section 8, and the receipt sealing
in `DECISIONS.md`), the candidate `CURRENT-STATE.md` and the four decision entries become the
durable bootstrap state.

Supersedes: The `BOOTSTRAP PENDING` shell content of `CURRENT-STATE.md`. No earlier delta or
decision.

Unchanged:
- The released product remains ALPHA 0.1.0 build 97 (`b3f41bf`).
- Operational closure remains terminal.
- No product implementation.
- `docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md` keeps its governed scope, including D1–D5,
  Decision C, the C12/C14 residual acceptance and the C1–C16 register, none of which is duplicated
  here.
- The open architecture questions remain open.

Open / unresolved questions: The open-question groups in `CURRENT-STATE.md`: representation,
subject identity and reference, commercial, and vertical architecture (the NG-4 extension
mechanism).

Forbidden implications: This migration does **not**:
- re-prove operational closure;
- begin NG-4;
- authorize implementation, schema, API or domain changes;
- make the candidate state `PUBLISHED` before the publication protocol completes.

Publication receipt: 4092094d803577a79bcd74d0e122a384843787be on origin/operational-regression-gate; remote readback verified 2026-09-28T23:15:18Z

## DELTA-20260929-001

Status: PUBLISHED

Detected: 2026-09-29T08:50:00Z

Published: 2026-09-29T12:30:01Z

Sources:
- Canonical Publisher implementation commit `c12190961aa1dfe8cfa2658db69b308d969bcc10` on `operational-regression-gate`.
- CI run `36532575195` (#110, `workflow_dispatch`), success on exactly `c12190961aa1dfe8cfa2658db69b308d969bcc10`.
- `docs/control-room/CANONICAL-PUBLISHER-CONTRACT.md`, `CANONICAL-PUBLISHER.md` and `CANONICAL-STATE-PROTOCOL.md` at `c12190961aa1dfe8cfa2658db69b308d969bcc10`.

Prior claim: The canonical current state does not yet record Self-Update V1 implementation status or a real Canonical Publisher dogfood result. The Publisher governance documents state that the Publisher is implemented and fixture-validated, but first real canonical use is pending.

Candidate/new claim: The Detector/local Alert renderer and Canonical Publisher are implemented. The Publisher is fixture-validated and exact-commit CI-verified at `c12190961aa1dfe8cfa2658db69b308d969bcc10`. This delta is the bounded first real canonical Publisher transition. If and only if this delta reaches `PUBLISHED` through successful P7 remote readback, that publication establishes the first real canonical Publisher dogfood as PASS. Self-Update V1 terminal acceptance remains pending separate Control Room review.

Claimed transition: SELF-UPDATE V1 PUBLISHER — IMPLEMENTED / FIXTURE-VALIDATED / REAL CANONICAL USE PENDING → FIRST REAL CANONICAL DOGFOOD PASS IF PUBLISHED; TERMINAL ACCEPTANCE PENDING.

Scope: Canonical Self-Update V1 infrastructure status only. The Publisher transition may change only the current-state and delta-ledger surfaces plus its non-authoritative pending staging files. No product behavior, schema, API, domain model, Owner decision, next-generation architecture or NG-4 work is changed or authorized.

Evidence:
- Remote source inspection verifies the Phase 3C Publisher implementation at `c12190961aa1dfe8cfa2658db69b308d969bcc10`.
- CI #110 / run `36532575195` completed successfully on exactly that SHA, including the canonical-infrastructure test gate.
- The dogfood-PASS portion of the candidate is mechanically gated by this delta itself reaching `PUBLISHED` only after Publisher P7 remote readback. Until then it remains accepted/unpublished staged text and is not current canonical state.

Conflicts:
- The Publisher governance/status documents currently say first real canonical use is pending. That is true before this dogfood.
- Because Publisher V1.1 hard-forbids the canonical-state protocol and this transition intentionally does not rewrite implementation-status prose outside its narrow state/ledger scope, successful P7 may leave descriptive status wording elsewhere requiring a separately adjudicated descriptive correction before terminal Phase 3D acceptance. This delta does not claim that whole-surface terminal cleanup is already complete.

Authority required:
- machine-verifiable fact, for repository identity and CI;
- `CONTROL_ROOM`, for the bounded dogfood semantic transition and interpretation of what successful P7 establishes.
- No Owner decision is required.

Adjudication: The Control Room accepted `DELTA-20260929-001` on 2026-09-29 as the bounded first real canonical Publisher dogfood transition. The candidate may become current only if the Publisher reaches `PUBLISHED` through successful P7 remote readback. Successful publication establishes dogfood PASS but does not by itself close Self-Update V1; terminal acceptance requires a separate Control Room review. NG-4 remains not authorized.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260929-001
  Semantic basis: ee745bd1983a03d74d2c70394ed2d2ffe02f3a49
  CURRENT-STATE.next.md blob SHA-256: 492a6a6b2356fdeef393ba7bf9fe49269e4d357d74a027de91436e7684be8ba2
  PUBLICATION-PAYLOAD.json blob SHA-256: 19fe8a51190c4eb13cb99c995a3ffd9069f9c67b5e4cb26202ff21fcc9d5a19a
  Authorized: 2026-09-29T12:24:51Z
  Reference: Adjudication of DELTA-20260929-001
  Recorded by: Claude

What changes if accepted: `CURRENT-STATE.md` gains the Self-Update V1 status section and advances its latest-published-delta reference to `DELTA-20260929-001`. This ledger records the dogfood transition and its eventual publication receipt.

Supersedes: The pre-dogfood operational-status claim that first real Canonical Publisher use is pending. It does not supersede any prior delta, decision, release identity, closure finding or architecture decision.

Unchanged:
- ALPHA 0.1.0 build 97 and its released product identity remain unchanged.
- Operational closure remains terminal.
- NG-1, NG-2 and NG-3 remain closed.
- NG-OD1 remains approved.
- NG-4 remains NEXT but not authorized and not begun.
- No product code, schema, API, domain model or runtime behavior changes.
- No Owner decision changes.
- The existing open architecture questions remain open.

Open / unresolved questions:
- Whether the real dogfood publication receipt and post-publication readback are sufficient for terminal Phase 3D / Self-Update V1 acceptance.
- Any descriptive same-surface wording corrections revealed by successful real dogfood.
- Watcher, scheduler, webhook and alert transport remain unimplemented and are not required by this transition.

Forbidden implications: This delta does **not**:
- close Self-Update V1 before separate Control Room terminal review;
- authorize NG-4 research or design;
- authorize next-generation implementation;
- modify product code, schema, API or domain boundaries;
- grant semantic authority to the Detector or Publisher;
- infer Owner approval;
- claim that staged or ACCEPTED state is current before P7;
- claim that all post-dogfood descriptive wording is already corrected.

Publication receipt: 5f88fbe1a046b8c5ee303dafb2a83e48a0965529 on origin/operational-regression-gate; remote readback verified 2026-09-29T12:30:01Z

## DELTA-20260929-002

Status: PUBLISHED

Detected: 2026-09-29T14:37:00Z

Published: 2026-09-29T15:18:09Z

Sources:
- Canonical branch state `3331ea0481304385ad199d1cdf5317cb02997d91`.
- CI run `36581526345` (#111, `workflow_dispatch`), success on exactly `3331ea0481304385ad199d1cdf5317cb02997d91`.
- Published real-dogfood delta `DELTA-20260929-001`, whose seal is `11935ff83f15d715ff9cee48c1514f43456fe0dd` and whose final publication basis is `5f88fbe1a046b8c5ee303dafb2a83e48a0965529`.
- `docs/control-room/CANONICAL-STATE-PROTOCOL.md`, `CANONICAL-PUBLISHER-CONTRACT.md`, `CANONICAL-PUBLISHER.md` and `CANONICAL-DETECTOR.md` at `3331ea0481304385ad199d1cdf5317cb02997d91`.
- Remote source inspection of the hardened Detector and Publisher at `3331ea0481304385ad199d1cdf5317cb02997d91`.

Prior claim: Self-Update V1 has an implemented Detector/local Alert renderer and an implemented Canonical Publisher. Real canonical dogfood is PASS, and post-dogfood hardening is published and exact-commit CI-verified, but `CURRENT-STATE.md` still records terminal acceptance as PENDING CONTROL ROOM REVIEW.

Candidate/new claim: Self-Update V1 is terminally COMPLETE. The Detector/local Alert renderer and Publisher v1.1 are implemented; the Publisher is fixture-validated; `DELTA-20260929-001` proves the real P7/R7 path; the findings exposed by that dogfood are repaired at `3331ea0481304385ad199d1cdf5317cb02997d91`; exact-commit CI #111 succeeds; whole-surface Control Room review found no remaining unexplained Self-Update V1 gap. The bounded residual limitations listed below are explicitly accepted and do not block completion.

Claimed transition: SELF-UPDATE V1 TERMINAL ACCEPTANCE PENDING → COMPLETE.

Scope: Canonical Self-Update V1 terminal-state publication only. This delta changes no product behavior, product identity, schema, API, domain model, architecture decision or Owner decision. It does not begin or authorize NG-4.

Evidence:
- `DELTA-20260929-001` is PUBLISHED with one `SA-1` record binding semantic basis `ee745bd1983a03d74d2c70394ed2d2ffe02f3a49`; its publication receipt names `5f88fbe1a046b8c5ee303dafb2a83e48a0965529`.
- The dogfood seal `11935ff83f15d715ff9cee48c1514f43456fe0dd` passed P7, and R7 subsequently returned `PUBLISHED_VERIFIED` without mutation.
- Post-dogfood hardening commit `3331ea0481304385ad199d1cdf5317cb02997d91` repairs H3D-001, H3D-002, H3D-003 and the real-ledger regression exposed by dogfood.
- H3D-001: descriptive corrections now resolve authority/adjudication references mechanically before mutation.
- H3D-002: future descriptive-correction commits durably carry and remotely re-verify the complete exact accepted payload.
- H3D-003: Canonical Alert current-state wording uses the observed/end-of-range state, and the staging question is direction-neutral.
- Publisher v1.1's 18,000-byte descriptive-correction transport bound is aligned across implementation, normative contract, documentation and tests.
- CI #111 / run `36581526345` succeeded on exactly `3331ea0481304385ad199d1cdf5317cb02997d91`, including build, unit, Windows, reviewer, canonical infrastructure, API-contract, version-metadata, formal/TLC, release-artifact and manifest-verification steps, plus the PostgreSQL integration job.
- Source and governance readback at `3331ea0481304385ad199d1cdf5317cb02997d91` agree and do not claim terminal acceptance ahead of this delta.

Conflicts: None remain for Self-Update V1 terminal acceptance. The historical `dfbd7ba0ef2fb7e02f6dfd94e4e4c0c56b98ee2e` descriptive correction predates H3D-002 and therefore does not embed its original accepted payload; its exact correction diff, classification, authority, payload digest and durable reference are recorded in the Publisher correction chain and were independently verified. History is not rewritten.

Authority required:
- machine-verifiable fact, for repository identity, CI and lifecycle evidence;
- `CONTROL_ROOM`, for the terminal Self-Update V1 evidence adjudication and acceptance of the bounded residual limitations.
- No Owner decision is required because this transition does not alter product, architecture, schema, API or domain boundaries.

Adjudication: The Control Room completed terminal review on 2026-09-29 at `3331ea0481304385ad199d1cdf5317cb02997d91` and ACCEPTED Self-Update V1 as terminally complete, subject only to this exact delta reaching `PUBLISHED` through successful Publisher P7 remote readback. The Control Room explicitly accepts the bounded residual limitations listed in this delta. This adjudication does not authorize NG-4.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260929-002
  Semantic basis: 611997f9a80e673e8a71a90cea930ab7eeb05813
  CURRENT-STATE.next.md blob SHA-256: e782c027e959b5830f9ecaeb9a73eca1e15d5c1d216176115ac4283ca966c2fc
  PUBLICATION-PAYLOAD.json blob SHA-256: 9d782fb218480e539ef04fe16826e14e707c8af69e9085dfefbc959ef8cbb87f
  Authorized: 2026-09-29T15:12:43Z
  Reference: Adjudication of DELTA-20260929-002
  Recorded by: Claude

What changes if accepted: `CURRENT-STATE.md` records Self-Update V1 terminal acceptance as COMPLETE, records the hardened Detector/Publisher evidence and real dogfood PASS, advances Latest published delta to `DELTA-20260929-002`, and makes the next bounded action explicit Owner authorization for NG-4 rather than NG-4 work itself.

Supersedes: The Self-Update V1 terminal-acceptance-PENDING claim in `CURRENT-STATE.md`. It does not supersede `DELTA-20260929-001`; that delta remains the published real-dogfood evidence.

Unchanged:
- ALPHA 0.1.0 build 97 and its released product identity remain unchanged.
- Operational closure remains terminal.
- NG-1, NG-2 and NG-3 remain closed.
- NG-OD1 remains approved.
- NG-4 remains NEXT, not authorized and not begun.
- No product code, schema, API, domain model or runtime behavior changes.
- No Owner decision changes.
- Detector and Publisher retain zero semantic decision authority.
- The existing next-generation architecture questions remain open.

Open / unresolved questions:
- None block Self-Update V1 terminal acceptance.
- No watcher, scheduler, webhook, alert transport or workflow store exists; these are accepted outside the Self-Update V1 terminal requirement.
- Detector does not collect CI/Nightly results or historical tag movement; external authoritative verification remains required when those facts matter.
- One real canonical Publisher transition plus fixture coverage does not prove every possible future repository or remote failure mode.
- Historical correction `dfbd7ba0ef2fb7e02f6dfd94e4e4c0c56b98ee2e` predates durable full-payload correction records; its bounded correction chain is accepted.
- `DESCRIPTIVE_CORRECTION` payloads in Publisher v1.1 are limited to 18,000 canonical bytes by the documented transport mechanism.

Forbidden implications: This delta does **not**:
- authorize NG-4 research, design or implementation;
- create an Owner decision;
- modify product code, schema, API or domain boundaries;
- grant semantic authority to the Detector or Publisher;
- claim that automation can judge whether evidence is sufficient;
- claim that one dogfood transition proves all future failure modes;
- require a watcher, scheduler, webhook or transport for Self-Update V1 acceptance;
- reopen operational closure;
- alter any prior architecture decision.

Publication receipt: 6aa7444350ea382fe1800009f5ef7c235063c662 on origin/operational-regression-gate; remote readback verified 2026-09-29T15:18:09Z

## DELTA-20260929-003

Status: PUBLISHED

Detected: 2026-09-29T16:12:24Z

Published: 2026-09-29T16:36:37Z

Sources:
- Explicit Owner authorization recorded as `DECISION-20260929-001`.
- Canonical branch state `c629c5bbd01e597be0518a553fc012b6e670110a`.
- `docs/control-room/CURRENT-STATE.md` at that SHA.
- `docs/control-room/DECISIONS.md` and `CANONICAL-STATE-PROTOCOL.md` at that SHA.
- The Owner instruction of 2026-09-29 quoted verbatim in `DECISION-20260929-001`.

Prior claim: Self-Update V1 is COMPLETE. NG-4 — Vertical Extension Architecture is NEXT, but it is not authorized and has not begun. The exact next bounded action is obtaining explicit Owner authorization before NG-4 research/design starts. The published `CURRENT-STATE.md` states NG-4's authorization status in two places: the Next-generation stage records NG-4 as NEXT and not authorized, and the Self-Update V1 terminal-acceptance bullet carries the clause "NG-4 remains not authorized and has not begun."

Candidate/new claim: The Owner has explicitly authorized NG-4 — Vertical Extension Architecture for research and design only. Once this transition is PUBLISHED, NG-4 research/design may begin under `DECISION-20260929-001`. Product code, schema/API/domain expansion, NG-5 and implementation remain unauthorized. The Self-Update V1 section no longer acts as a second current-status authority for NG-4: it records only the historical boundary that completing Self-Update V1 did not itself authorize NG-4. Current NG-4 authorization is governed by the Next-generation stage and `DECISION-20260929-001`.

Claimed transition: NG-4 NEXT / NOT AUTHORIZED → AUTHORIZED FOR RESEARCH AND DESIGN ONLY.

Scope: Governance authorization only. This delta publishes the Owner decision and advances canonical current state to reflect that NG-4 research/design may begin after publication. It performs no NG-4 research/design itself and changes no product, schema, API or domain implementation.

Evidence:
- The Owner explicitly authorized NG-4 research/design on 2026-09-29.
- The exact Owner wording is preserved in `DECISION-20260929-001`.
- The previously published `CURRENT-STATE.md` requires explicit Owner authorization before NG-4 starts.
- No product-code, schema, API or domain change is part of this transition.

Conflicts: None.

Authority required:
- `OWNER`, for the authorization to begin NG-4 research/design;
- `CONTROL_ROOM`, for faithful normalization of the Owner decision into canonical state;
- machine-verifiable fact, for repository identity and publication evidence.

Adjudication: The Owner explicitly approved starting NG-4 — Vertical Extension Architecture for research and design only, excluding product code, schema/API/domain expansion, NG-5 and implementation. The Control Room accepts this exact bounded decision for canonical publication without broadening it. The authorization becomes current only if this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: OWNER
  Delta: DELTA-20260929-003
  Semantic basis: d4a3d9ad8b2611aa42c676b9ba09e733af8635a6
  CURRENT-STATE.next.md blob SHA-256: 6009b81f95a901688a73a280963290ee3ceea7832e0c7aca0aadbda6e9c8cb1b
  PUBLICATION-PAYLOAD.json blob SHA-256: e446180b1974bfadd2908200ce38dd0d5fb8e33531c31c5332cca7c733c445da
  Authorized: 2026-09-29T16:31:49Z
  Reference: DECISION-20260929-001
  Recorded by: Claude

What changes if accepted:
- `DECISION-20260929-001` becomes the durable Owner authorization for NG-4 research/design.
- `CURRENT-STATE.md` records NG-4 as AUTHORIZED rather than merely NEXT/unapproved.
- The stale NG-4 authorization-status clause inside `Self-Update V1` is replaced by a boundary statement: Self-Update V1 itself did not authorize NG-4; current authorization is governed by the Next-generation stage and its cited Owner decision.
- The exact next bounded action becomes beginning NG-4 research/design under the published Owner decision.

Supersedes:
- The current-state claim that NG-4 is not authorized.
- The stale NG-4 current-status clause in the `Self-Update V1` terminal-acceptance bullet.
- It supersedes no earlier Decision ID.

Unchanged:
- Self-Update V1 remains COMPLETE.
- The evidence and accepted residuals of Self-Update V1 remain unchanged.
- Only its stale NG-4 current-status clause changes.
- Operational Closure remains terminal.
- Build 97 remains the product baseline.
- NG-1, NG-2 and NG-3 remain CLOSED.
- NG-OD1 / `DECISION-20260928-002` remains ACTIVE.
- No product code changes.
- No schema, API or domain expansion.
- No implementation.
- NG-5 remains unauthorized and not begun.
- No NG-4 architecture conclusion has yet been made.

Open / unresolved questions:
- Every substantive NG-4 architecture question remains open.
- The extension mechanism is undecided.
- The Film/TV reference architecture is not yet designed.
- Cross-vertical falsification has not yet occurred.
- Any later Owner-reserved ambiguity remains unresolved until separately decided.

Forbidden implications: This delta does **not** mean:
- NG-4 architecture is already decided.
- product implementation is approved.
- schema/API/domain expansion is approved.
- NG-5 may begin.
- any future NG-4 recommendation is pre-approved.
- Claude has Owner authority.
- publication of this authorization itself constitutes NG-4 research/design completion.

Publication receipt: 593b5b93312f0b85cfad78d1b0311acb9abe3f64 on origin/operational-regression-gate; remote readback verified 2026-09-29T16:36:37Z

## DELTA-20260929-004

Status: PUBLISHED

Detected: 2026-09-29T18:25:00Z

Published: 2026-09-29T18:48:47Z

Sources:
- Canonical branch state `85918d6680f4d5c432827d080a01ccf8fc3f646e` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md`, `CANONICAL-DELTAS.md` and `CANONICAL-STATE-PROTOCOL.md` at that SHA.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, whose `src/` and `docs/adr/` are byte-identical at that SHA.
- The bounded NG-4 research chain NG-4A to NG-4E/F, adjudicated by the Control Room and recorded as `DECISION-20260929-002`.

Prior claim: NG-4 — Vertical Extension Architecture is AUTHORIZED for research/design under `DECISION-20260929-001`, but no NG-4 architecture conclusion is yet canonical; the extension mechanism and core-versus-vertical boundary remain open.

Candidate/new claim: NG-4 has reached a terminal conceptual architecture conclusion under `DECISION-20260929-002`. The accepted architecture is the bounded compiled-monolith / vertical-semantic-context / category-differentiated-composition architecture, with explicit role-local typed bridges where a shared/core role must reference a vertical-owned object, as defined in `DECISION-20260929-002`. NG-4 becomes CLOSED only when this transition is PUBLISHED. Product implementation, schema/API/domain expansion and NG-5 remain unauthorized.

Claimed transition: NG-4 AUTHORIZED / RESEARCH-DESIGN OPEN → CLOSED.

Scope: Canonical publication of the Control Room's terminal NG-4 architecture adjudication only.

Evidence:
- Machine-verifiable repository facts:
  - canonical HEAD `85918d6680f4d5c432827d080a01ccf8fc3f646e` (`Publish DELTA-20260929-003`), with `DECISION-20260929-001` ACTIVE and `DELTA-20260929-003` PUBLISHED;
  - `git diff b3f41bf..85918d6 -- src docs/adr` is empty, so every Build-97 anchor below is released product source.
- Source and ADR evidence (Build 97):
  - Film/TV vocabulary sits in core-looking types: `src/AgencyOS.Domain/Deals/Deal.cs:26` (`DealKind`, "Broad enough for film and television"), `Deals/DealTerms.cs:44`/`:105` (term units and codes), `Legal/ContractParty.cs:18`, `Legal/RightsGrant.cs:26`/`:75`/`:103`, `Finance/MonetaryObligation.cs:25`, `Legal/DeadlineRule.cs` (`OnFirstRelease`).
  - `Deal` requires Opportunity and target (`Deal.cs:268`, `:271`), an implementation accident relative to `DECISION-20260928-004`.
  - Contract terms mirror deal terms by integer (`Legal/ContractTerms.cs:13`); reconciliation compares typed terms on a shared code (`src/AgencyOS.Deals.Rules/Reconciliation.fs`; ADR-0022 §6).
  - Role-local typed reference precedents: `Relationships/RelationshipEndpoint.cs:27` (ADR-0011) and `Opportunities/OpportunitySubject.cs:47` (ADR-0020); ADR-0019's validator-only raw identifier is the anti-precedent; `Opportunity.cs:328` shows subject-kind requirements placed in core.
  - `Projects/SourceProperty.cs` is "an underlying property … as described", not a rights record.
  - Commission takes one `MonetaryObligation` amount as its basis (`src/AgencyOS.Application/Finance/CommissionCommands.cs:176`); `SpecificTerm` requires and stores a term code (`src/AgencyOS.Finance.Rules/Commission.fs:125`; `Finance/Commission.cs:132`) that does not select the basis; one governing rule, "two in force at once is refused rather than resolved" (ADR-0023 §6).
  - ADR-0040 §5 ("Unsigned-but-binding is not modelled, and is not proxied") and its rejected "allow and warn" alternative.
  - A code-declared term catalogue is published at runtime through `GET /deal-terms` (`src/AgencyOS.Api/Endpoints/M7Endpoints.cs:605`); the API parses enum names (`M7Endpoints.cs:143`); the Windows client hard-codes vocabulary (`src/AgencyOS.Windows/Dialogs/RecordMonetaryObligationDialog.xaml:47`).
  - ADR-0037 §1 keeps a compiled modular monolith; no dynamic assembly loading exists in `src/`.
- Control Room semantic adjudication: the Adjudication field below, recorded as `DECISION-20260929-002`.

Conflicts: None that survive adjudication. The correction chain is preserved rather than reproduced:
- the earlier claim that every real-world fact must be recordable with a non-blocking finding was falsified by ADR-0040 and is not adopted;
- the earlier claim that vertical modules can contribute members to a closed reference family at build time was unproved; the accepted contract is an explicit reviewed bridge expansion;
- cross-vertical use alone was rejected as grounds for promoting a C3 object to core; SourceProperty(Book) is not proven to share identity with a future Literary Work;
- the earlier assumption that gross-compensation commission sums vertical term rows was corrected; the basis is one `MonetaryObligation` amount;
- the K4-versus-K5 code/data fork was a false dichotomy; semantic authority, historical resolution and runtime publication are separate axes;
- bounded contexts inside the monolith and a typed C1/C2 descriptor catalogue were not rejected; only their strawman forms were.

Authority required:
- `CONTROL_ROOM`, for the semantic terminal adjudication and stage completion;
- machine-verifiable fact, for repository and publication facts;
- no new Owner decision is required. `DECISION-20260929-001` remains the Owner authorization that allowed NG-4 research/design.

Adjudication: The Control Room independently reviewed the NG-4A through NG-4E/F evidence and correction chain, re-verified the canonical branch and the decisive Build-97 source anchors, attempted terminal falsification, and ACCEPTS the semantic architecture described by `DECISION-20260929-002` as sufficient to close NG-4 once this publication reaches P7. No genuine Owner-reserved ambiguity remains. The accepted architecture includes the narrowings recorded in `DECISION-20260929-002`: Build-97 aggregate and technique shapes are precedents rather than invariants; C3 bridges require explicit reviewed expansion and zero-core-touch extensibility is not required; no durable finding category is approved; definition history is required without selecting a version or storage mechanism; and runtime plugins are not justified under current evidence.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260929-004
  Semantic basis: a715e511cfa43dfc00a349f844d81ab6f7402314
  CURRENT-STATE.next.md blob SHA-256: cc0df33a94585842ed4b00ac629cb534bf117151847a6d9f24a47a18f57af8ac
  PUBLICATION-PAYLOAD.json blob SHA-256: 77351990fde195334babc370a15262919399e9a60300eb39e28895c848ed509f
  Authorized: 2026-09-29T18:46:27Z
  Reference: Adjudication of DELTA-20260929-004
  Recorded by: Claude

What changes if accepted:
- `DECISION-20260929-002` is added as the durable NG-4 architecture decision.
- `CURRENT-STATE.md` records NG-4 as CLOSED.
- The extension-mechanism and core-versus-vertical question is replaced by the accepted architecture plus bounded residuals.
- The next bounded action becomes obtaining explicit Owner authorization before beginning NG-5 or any other post-NG-4 stage.
- No implementation authority follows.

Supersedes: The current-state claim that NG-4 architecture remains undecided/open. It supersedes no prior Decision ID.

Unchanged:
- Build 97 released product identity.
- Operational Closure remains COMPLETE and terminal.
- Self-Update V1 remains COMPLETE and terminal.
- NG-1, NG-2 and NG-3 remain CLOSED.
- `DECISION-20260928-001` to `-004` remain in force.
- `DECISION-20260929-001` remains the historical Owner authorization that allowed NG-4 research/design.
- No product code change.
- No schema, API or domain expansion.
- No implementation.
- NG-5 remains unauthorized and not begun.
- The deferred core questions remain deferred.

Open / unresolved questions:
- A newly referenceable C3 kind may require explicit reviewed expansion of a shared composition contract.
- Exact persistence, schema, API, interface and module mechanics remain deferred.
- Cross-vertical mandate-lineage reporting depends on the already-deferred commercial ↔ mandate cardinality.
- Work/IP promotion remains unresolved until its identity/lifecycle earns neutral-core status.
- Film/TV-specific guild schedule content was not directly researched; the C6 architecture itself was sufficiently established by bounded cross-vertical evidence.
- Previously deferred core questions remain deferred: persisted vs derived umbrella Representation; qualifying clienthood; delegated/sub-agency authority topology; exact commission placement/model; Company ↔ External Organization implementation; Group persistence; successor/predecessor semantics; Person reconciliation; exact subject storage/retention mechanics; commercial ↔ mandate cardinality; agreement snapshot; exact amount-determination record; multi-arrangement legal instruments; scope/exclusivity/territory placement; ledger integration.
- None of these blocks NG-4 closure.

Forbidden implications: This delta does **not**:
- authorize product implementation;
- authorize schema/API/domain expansion;
- authorize NG-5;
- approve a generic Entity, Party, SubjectId or Transaction;
- approve generic JSON terms;
- approve a universal rights ontology;
- approve a mega lifecycle;
- approve a universal rules engine or C1–C6 registry;
- approve runtime plugins under current evidence;
- require zero-core-touch extension;
- promote Work/IP to represented subject or universal core identity;
- make exact Build-97 offer/rights/option/deadline/state-machine/storage mechanisms universal;
- require a particular definition Version field or persistence design;
- make Claude an architecture authority;
- reopen Operational Closure.

Publication receipt: e4c396903f886b73174ed9a88ea6dbbfdf5e9149 on origin/operational-regression-gate; remote readback verified 2026-09-29T18:48:47Z

## DELTA-20260929-005

Status: PUBLISHED

Detected: 2026-09-29T18:54:37Z

Published: 2026-09-29T19:17:36Z

Sources:
- Explicit Owner authorization recorded as `DECISION-20260929-003`.
- Canonical branch state `66af408aced50ccd14bfc913f0fe81754b72864b` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.
- The published `DECISION-20260929-002` and `DELTA-20260929-004`.

Prior claim: NG-4 is CLOSED under `DECISION-20260929-002`. NG-5 is unauthorized and not begun. The exact next bounded action is obtaining explicit Owner authorization before beginning NG-5 or another post-NG-4 stage.

Candidate/new claim: The Owner has explicitly authorized NG-5 research/design to begin under `DECISION-20260929-003`. The authorization is research/design only. NG-5's exact canonical title and bounded substantive question remain to be established by the Control Room before substantive research begins. Product code, implementation, schema/API/domain expansion, migrations and later stages remain unauthorized.

Claimed transition: NG-5 NOT AUTHORIZED / NOT BEGUN → AUTHORIZED FOR RESEARCH/DESIGN ONLY.

Scope: Governance authorization only. This transition performs no NG-5 research/design itself and makes no architecture conclusion.

Evidence:
- Machine-verifiable canonical repository state: HEAD `66af408aced50ccd14bfc913f0fe81754b72864b` (`Publish DELTA-20260929-004`); `CURRENT-STATE.md` there records NG-4 as CLOSED under `DECISION-20260929-002`, NG-5 as unauthorized and not begun, and an exact next bounded action requiring explicit Owner authorization before NG-5.
- Published governance: `DECISION-20260929-002` (ACTIVE) and `DELTA-20260929-004` (PUBLISHED) closed NG-4 and left NG-5 unauthorized.
- Explicit Owner decision: recorded, with the Owner's exact response, as `DECISION-20260929-003`.
- No product-code, schema, API, domain or migration change is part of this transition.

Conflicts: None.

Authority required:
- `OWNER`, for the authorization to begin NG-5 research/design;
- `CONTROL_ROOM`, for faithful normalization and publication planning;
- machine-verifiable fact, for repository and publication evidence.

Adjudication: The Owner explicitly approved the Control Room's binary authorization question by replying “לאשר.” The Control Room normalizes that approval narrowly as authorization for NG-5 research/design only, because the question concerned beginning the next post-NG-4 stage including NG-5 research/design and did not request implementation authority. The exact NG-5 title and bounded question remain open. No implementation, schema, API or domain authority is inferred. The authorization becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: OWNER
  Delta: DELTA-20260929-005
  Semantic basis: 37c90ad75b8795d140af4bb70cc90791957cf69f
  CURRENT-STATE.next.md blob SHA-256: 48a5133fbdad47d1aa24967541699969c8189dc1ac5699359e833229bd29fe27
  PUBLICATION-PAYLOAD.json blob SHA-256: 79c33af2dd2dfe2980bc3045ae34cf80773812714df4cb53163a2e4eb8f142a0
  Authorized: 2026-09-29T19:16:41Z
  Reference: DECISION-20260929-003
  Recorded by: Claude

What changes if accepted:
- `DECISION-20260929-003` becomes the durable Owner authorization for NG-5 research/design.
- `CURRENT-STATE.md` records NG-5 as AUTHORIZED rather than unauthorized/not begun.
- The exact next bounded action becomes Control Room definition of the bounded NG-5 research/design question before substantive Claude research.
- No implementation authority follows.

Supersedes: The current-state claim that NG-5 is unauthorized and may not begin without Owner authorization. It supersedes no Decision ID.

Unchanged:
- NG-4 remains CLOSED under `DECISION-20260929-002`.
- All earlier architecture decisions remain in force.
- Operational Closure remains terminal.
- Self-Update V1 remains terminal.
- No product change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- No NG-6 authorization.
- NG-4 deferred questions remain deferred.

Open / unresolved questions:
- The exact NG-5 canonical title.
- The exact NG-5 bounded substantive question.
- NG-5 substages and substantive prompt budget.
- All previously deferred questions not explicitly resolved by a future stage.

Forbidden implications: This delta does **not**:
- begin substantive NG-5 research by itself;
- define an NG-5 architecture conclusion;
- authorize implementation;
- authorize product-code changes;
- authorize schema/API/domain expansion;
- authorize migrations;
- authorize NG-6;
- reopen NG-4;
- modify DECISION-20260929-002;
- imply that Claude may define NG-5's scope on its own;
- imply Owner approval of an as-yet-unwritten implementation plan.

Publication receipt: 4d5f6ad421b4a6630e35cdc8abc493dba80c43c9 on origin/operational-regression-gate; remote readback verified 2026-09-29T19:17:36Z

## DELTA-20260929-006

Status: PUBLISHED

Detected: 2026-09-29T20:26:52Z

Published: 2026-09-29T20:44:33Z

Sources:
- Canonical branch state `fc4ae8555fd4b8f7b91b7e2f9ab9f550e3a640f9` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, whose `src/` and `docs/adr/` are byte-identical at that SHA.
- The bounded NG-5 research chain NG-5A and NG-5B, adjudicated by the Control Room and recorded as `DECISION-20260929-004`.

Prior claim: NG-5 is AUTHORIZED for research/design under `DECISION-20260929-003`; its exact title and bounded question remain to be established; the exact commercial ↔ mandate cardinality is an open question.

Candidate/new claim: NG-5 — Mandate–Commercial Lineage Architecture has reached a terminal conceptual architecture conclusion under `DECISION-20260929-004`: each Commercial Arrangement has conceptual direct lineage to zero or one Representation Mandate; established lineage is to the specific historical mandate relevant to the fact's authority provenance and is never silently re-derived; zero lineage is a legitimate state that distinguishes "no valid mandate established" from "provenance unknown or unresolved"; pre-arrangement facts may carry optional fact-local zero-or-one lineage; a several-mandate, role-typed relation is not justified now and is retained only as the explicit H3 reconsideration trigger. NG-5 becomes CLOSED only when this transition is PUBLISHED. Product code, implementation, schema/API/domain expansion and NG-6 remain unauthorized.

Claimed transition: NG-5 AUTHORIZED / RESEARCH-DESIGN OPEN → NG-5 MANDATE–COMMERCIAL LINEAGE ARCHITECTURE CLOSED.

Scope: Canonical publication of the Control Room's terminal NG-5 architecture adjudication only. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable repository facts:
  - canonical HEAD `fc4ae8555fd4b8f7b91b7e2f9ab9f550e3a640f9` (`Publish DELTA-20260929-005`), with `DECISION-20260929-003` ACTIVE and `DELTA-20260929-005` PUBLISHED;
  - `CURRENT-STATE.md` there lists "commercial ↔ mandate cardinality" as an open question.
- Build-97 source evidence (NG-5A, product commit `b3f41bf`):
  - the ordinary commercial chain Opportunity → target → Deal → Offer → Contract → MonetaryObligation carries no representation or mandate reference; the Deal's subject is read through the opportunity (ADR-0021);
  - `commission_rules`, `commission_entitlements` and `receivables` carry caller-supplied `representation_id` values with no foreign key (`src/AgencyOS.Infrastructure/Persistence/Migrations/20260908071739_FinanceLedgerAndCommissions.cs`; `src/AgencyOS.Application/Finance/CommissionCommands.cs`).
- External domain evidence (NG-5B):
  - WGA Rider W 2021 §3.C.1, §3.C.2.b, §3.C.4, §3.C.5 and WGA Franchise Agreement 2021 §3.B.5.a–c (https://www.wga.org/uploadedfiles/employers_agents/agencies/rider-w-2021.pdf; https://www.wga.org/uploadedfiles/employers_agents/agencies/franchise-agreement-2021.pdf);
  - AFM Booking Agent Agreement §6(d), §12(a), §13(d) and Schedule 1(A)(III) (https://www.afm.org/wp-content/uploads/2019/09/AFM-Booking-Agent-Agreement.pdf);
  - AFM Form L-1 clause 7 (https://nashvillemusicians.org/sites/default/files/AFM%20L1.pdf);
  - Marathon Entertainment, Inc. v. Blasi, 42 Cal.4th 974 (2008), used narrowly for the distinction between real entertainment/employment facts and unlawful/unlicensed procurement.
- Control Room semantic adjudication: the Adjudication field below, recorded as `DECISION-20260929-004`.

Conflicts: None that survive adjudication. The correction chain is preserved:
- commission-tail evidence proves historical/economic provenance but is not conflated with multiple arrangement-authority lineages;
- AFM booking plus personal-management commission does not prove that one Commercial Arrangement requires two mandate lineages;
- a multi-musician L-1 instrument does not prove one multi-mandate Commercial Arrangement, because one instrument may cover several arrangements;
- WGA §3.B.5.c does not itself prove that the same principal simultaneously crossed writer and rights-holder authority scopes in one event;
- H2 being unfalsified after valid decomposition is the reason it is the minimum accepted neutral-core contract, not a reason to expand to H3.

Authority required:
- `CONTROL_ROOM`, for the semantic architecture adjudication and stage completion;
- machine-verifiable fact, for repository and publication facts;
- no new Owner decision is required. `DECISION-20260929-003` remains the Owner authorization provenance that allowed NG-5 research/design.

Adjudication: The Control Room independently reviewed the NG-5A source baseline and the NG-5B cross-vertical falsification evidence, applied the corrections recorded under Conflicts, and ACCEPTS the zero-or-one Mandate–Commercial Arrangement lineage contract described by `DECISION-20260929-004` as the minimum neutral-core semantic contract, sufficient to close NG-5 once this publication reaches P7. H0 and H1 are rejected, H2 is accepted, and H3 is not justified now and is retained only as the explicit reconsideration trigger. No genuine Owner-reserved ambiguity remains.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260929-006
  Semantic basis: 443624084ea3a6d3b33f49d15b5549b5350b46dc
  CURRENT-STATE.next.md blob SHA-256: 73b00c18d1a4eabf32848e473810e8dbc8a3748ec7231784558ec4b3eda5abeb
  PUBLICATION-PAYLOAD.json blob SHA-256: 5f2dd7de8c046ed10a7b4dba5585ae390af30bde7042e179215058da16d411f9
  Authorized: 2026-09-29T20:40:39Z
  Reference: Adjudication of DELTA-20260929-006
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260929-004` is added as the durable NG-5 architecture decision.
- `CURRENT-STATE.md` records NG-5 Mandate–Commercial Lineage Architecture as CLOSED.
- The open question "commercial ↔ mandate cardinality" is closed by `DECISION-20260929-004`, and the stale NG-5 title/question open items are removed.
- The next bounded action becomes obtaining explicit Owner authorization before beginning any post-NG-5 stage, including NG-6.
- No implementation authority follows.

Supersedes: The current-state claims that NG-5's title and bounded question remain to be established and that the commercial ↔ mandate cardinality is open. It supersedes no prior Decision ID.

Unchanged:
- Build 97 released product identity and all implementation behavior.
- Operational Closure remains terminal.
- Self-Update V1 remains terminal.
- NG-1 through NG-4 remain CLOSED; `DECISION-20260928-001` to `-004` and `DECISION-20260929-002` remain in force.
- `DECISION-20260929-003` remains the Owner authorization that allowed NG-5 research/design.
- No product code change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- NG-6 remains unauthorized and not begun.

Open / unresolved questions:
- The deferred items listed as Open in `DECISION-20260929-004` remain open.
- None of these blocks NG-5 closure.

Forbidden implications: This delta does **not**:
- authorize product implementation;
- authorize schema/API/domain expansion;
- authorize migrations;
- authorize NG-6;
- select a database relation, FK, persistence strategy or API shape;
- require a mandate for every Commercial Arrangement;
- make Opportunity or Proposal a universal mandatory parent;
- approve a raw many-to-many mandate/arrangement model;
- approve a generic Entity, Party, SubjectId or Transaction;
- repair Build-97 RepresentationId/FK weaknesses;
- redesign commission;
- make Claude an architecture authority.

Publication receipt: 88fe45c91d8704b18ea98fab739877322c787655 on origin/operational-regression-gate; remote readback verified 2026-09-29T20:44:33Z

## DELTA-20260929-007

Status: PUBLISHED

Detected: 2026-09-29T20:45:19Z

Published: 2026-09-29T21:20:43Z

Sources:
- Canonical branch state `ad16ebf66886edc0610b248b20b53fc2af50c1d9` on `operational-regression-gate`, the seal of `DELTA-20260929-006`.
- `docs/control-room/DECISIONS.md` at that SHA, containing the published `DECISION-20260929-004`.
- The Control Room's locked NG-5 Scope-Lock question and its finding on the `DECISION-20260929-004` Question field.

Prior claim: NG-5 is CLOSED under `DECISION-20260929-004` with the correct zero-or-one architecture, but `DECISION-20260929-004`'s Question field used a shortened normalization rather than the exact locked NG-5 question.

Candidate/new claim: NG-5 remains CLOSED with exactly the same architecture, now governed by `DECISION-20260929-005`, which preserves the exact locked Question and supersedes `DECISION-20260929-004` solely for this correction-chain purpose.

Claimed transition: NG-5 CLOSED / architecture unchanged / question provenance normalized → NG-5 CLOSED / architecture unchanged / exact locked question durably preserved.

Scope: Canonical correction-chain publication only. The NG-5 architecture is unchanged. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable repository facts:
  - canonical HEAD `ad16ebf66886edc0610b248b20b53fc2af50c1d9` (`Publish DELTA-20260929-006`), with `DELTA-20260929-006` PUBLISHED and `DECISION-20260929-004` ACTIVE and sealed to `443624084ea3a6d3b33f49d15b5549b5350b46dc`;
  - the published `DECISION-20260929-004` Question omits "lineage roles where more than one mandate participates" and phrases the precursor clause as "whether optional pre-arrangement commercial facts need independent mandate lineage of their own", whereas the locked question reads "only where necessary to preserve pre-arrangement authority provenance, must optional Market Activity / Opportunity / Proposal context carry direct mandate lineage of its own?".
- Protocol fact: Publisher V1.1 cannot alter the bytes of a pushed staged new decision through a replacement semantic basis (that would be `SEMANTIC_CHANGE_REQUIRES_NEW_DELTA`), and the published decision's substantive content is immutable except for its lifecycle `Status:` line (`DECISIONS.md`, *Immutability of entry content*).
- Control Room adjudication: the Adjudication field below.

Conflicts: None in the architecture. The only corrected conflict is the durable Question wording of `DECISION-20260929-004`.

Authority required:
- `CONTROL_ROOM`, for the correction adjudication;
- machine-verifiable fact, for repository and publication facts;
- no Owner decision is required. This remains a Control Room correction, not a new product choice.

Adjudication: The Control Room found that `DECISION-20260929-004` carried the correct terminal NG-5 architecture but recorded a shortened normalization of the locked NG-5 question. It ACCEPTS `DECISION-20260929-005` as the corrected durable decision: it carries the exact locked Question, restates the architecture of `DECISION-20260929-004` without change of meaning, and supersedes it solely to preserve question provenance. The zero-or-one architecture, the H0–H3 disposition and every implementation boundary are unchanged. The correction becomes canonical only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260929-007
  Semantic basis: 353961b2097be8429b308d103215f52c6db1af63
  CURRENT-STATE.next.md blob SHA-256: 03a0ac873c8464042b7515c2a995dd03f10ffd7ee0eac629b315e52cbeb0fc0e
  PUBLICATION-PAYLOAD.json blob SHA-256: 7623f795fd25b774fc8d63088367a7d606f60a68cf55951148469072376a7170
  Authorized: 2026-09-29T20:50:48Z
  Reference: Adjudication of DELTA-20260929-007
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260929-005` is added as the governing durable NG-5 decision.
- `DECISION-20260929-004` moves from ACTIVE to SUPERSEDED by `DECISION-20260929-005`; no other field of it changes, and its publication receipt stays as sealed.
- `CURRENT-STATE.md` cites `DECISION-20260929-005` for NG-5, and its latest published delta becomes `DELTA-20260929-007`.
- No architecture conclusion changes.

Supersedes: `DECISION-20260929-004`, solely for the correction-chain purpose above.

Unchanged:
- The NG-5 zero-or-one architecture and the H0–H3 disposition.
- NG-5 remains CLOSED.
- The exact commercial ↔ mandate cardinality remains closed.
- NG-1 through NG-4 and their decisions.
- `DECISION-20260929-003` as the Owner authorization for NG-5 research/design.
- The deferred questions listed as Open in `DECISION-20260929-005`.
- Build 97 and all implementation behavior.
- No product code change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- NG-6 remains unauthorized and not begun.

Open / unresolved questions:
- The deferred items listed as Open in `DECISION-20260929-005`, which are the same as in `DECISION-20260929-004`.
- None of these blocks the correction.

Forbidden implications: This delta does **not**:
- reopen NG-5;
- change zero-or-one cardinality;
- change H0–H3 disposition;
- authorize implementation;
- authorize schema/API/domain expansion;
- authorize migrations;
- change commission architecture;
- authorize NG-6.

Publication receipt: f1a648784999251485b1de71482ad8307dee740f on origin/operational-regression-gate; remote readback verified 2026-09-29T21:20:43Z

## DELTA-20260929-008

Status: PUBLISHED

Detected: 2026-09-29T22:23:00Z

Published: 2026-09-29T22:33:04Z

Sources:
- Explicit Owner authorization recorded as `DECISION-20260929-006`.
- The Control Room Scope Lock recorded as `DECISION-20260929-007`.
- Canonical branch state `bd52078ec030cfab5399c3f18cc4a24104195b60` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.

Prior claim: NG-5 is CLOSED under `DECISION-20260929-005`. No post-NG-5 stage, including NG-6, is authorized. NG-6 has no authorized title or bounded question.

Candidate/new claim: The Owner has authorized NG-6 — Commercial Arrangement–Legal Instrument Composition Architecture for research/design only under `DECISION-20260929-006`. The Control Room has established the exact bounded Scope Lock under `DECISION-20260929-007`. Once this delta reaches PUBLISHED, NG-6 research/design may begin, the first substantive work is NG-6A, no implementation, schema, API, domain or migration authority follows, and NG-7 remains unauthorized.

Claimed transition: NG-6 UNAUTHORIZED / NOT BEGUN → NG-6 AUTHORIZED FOR RESEARCH/DESIGN ONLY / SCOPE LOCKED.

Scope: Governance authorization and scope-lock publication only. This transition performs no NG-6 research/design itself and makes no architecture conclusion. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable canonical repository state: HEAD `bd52078ec030cfab5399c3f18cc4a24104195b60` (`Publish DELTA-20260929-007`); `CURRENT-STATE.md` there records NG-5 as CLOSED under `DECISION-20260929-005`, requires explicit Owner authorization before any post-NG-5 stage including NG-6, and lists "the agreement snapshot" and "multi-arrangement instruments" as open Commercial questions.
- Published governance: `DECISION-20260928-004` left the exact Agreement Snapshot representation and one Legal Instrument covering several Arrangements open; `DECISION-20260929-005` closed mandate-to-commercial lineage and kept the legal-instrument composition question deferred.
- Explicit Owner decision: recorded, with the Owner's exact response, as `DECISION-20260929-006`.
- Control Room Scope Lock: recorded as `DECISION-20260929-007`; no new external/domain research was performed in creating it.
- No product-code, schema, API, domain or migration change is part of this transition.

Conflicts: None.

Authority required:
- `OWNER`, for the NG-6 stage authorization;
- `CONTROL_ROOM`, for the exact Scope Lock;
- machine-verifiable fact, for repository and publication facts.

Adjudication: The Owner explicitly approved the Control Room's question whether to define NG-6 around Commercial Arrangement–Legal Instrument Composition Architecture for research/design only, by replying “מאשר.” The Control Room normalizes that approval narrowly as authorization for NG-6 research/design only, and establishes the exact bounded Scope Lock recorded in `DECISION-20260929-007` before any substantive NG-6 research. No implementation, schema, API, domain or migration authority is inferred. The authorization and Scope Lock become canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: OWNER
  Delta: DELTA-20260929-008
  Semantic basis: 575ad86afea73d962c51a0348bfe774dd0f643e4
  CURRENT-STATE.next.md blob SHA-256: f2466157806d6b64243d08d6958b249e836a2e5bd92d791f2ad260b138526519
  PUBLICATION-PAYLOAD.json blob SHA-256: c19365faacb088fcb5f0c35d3be673fc36e2d76c761674b6ef9b30eb92c7deb5
  Authorized: 2026-09-29T22:31:43Z
  Reference: Adjudication of DELTA-20260929-008
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260929-006` becomes the durable Owner authorization for NG-6 research/design.
- `DECISION-20260929-007` becomes the durable NG-6 Scope Lock.
- `CURRENT-STATE.md` records NG-6 Commercial Arrangement–Legal Instrument Composition Architecture as AUTHORIZED under `DECISION-20260929-006`, with its Scope Lock in `DECISION-20260929-007`.
- The open Agreement Snapshot and multi-arrangement-instrument questions are preserved as the subject of NG-6, not resolved.
- The exact next bounded action becomes beginning NG-6A — Canonical & Build-97 Legal-Composition Baseline.
- No implementation authority follows.

Supersedes: The current-state claim that no post-NG-5 stage, including NG-6, is authorized. It supersedes no Decision ID.

Unchanged:
- Operational Closure remains terminal.
- Self-Update V1 remains terminal.
- NG-1 through NG-5 remain CLOSED.
- `DECISION-20260929-005` remains the governing NG-5 architecture decision.
- Build 97 released product identity.
- Every unrelated deferred question.
- All forbidden generic abstractions and boundaries already established.
- No product change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- NG-7 remains unauthorized and not begun.

Open / unresolved questions:
- The final NG-6 architecture conclusion, including the evidence and adjudication of L0–L3 and S0–S2.
- Any genuinely underdetermined case within the locked question.
- Any genuine Owner-reserved ambiguity found by the bounded research.
- All questions explicitly excluded by `DECISION-20260929-007`.

Forbidden implications: This delta does **not**:
- reopen NG-5;
- implement NG-4, NG-5 or NG-6;
- approve schema/API/domain expansion;
- approve migrations;
- decide Arrangement↔Legal Instrument cardinality;
- decide Agreement Snapshot persistence or representation;
- approve many-to-many composition;
- create a generic Contract/Transaction/Entity/Party abstraction;
- approve a universal rights ontology;
- decide amount determination;
- decide commission architecture;
- decide ledger integration;
- authorize NG-7;
- make Claude a semantic authority.

Publication receipt: 76d00b056831e62905575cef112203bb7cc58756 on origin/operational-regression-gate; remote readback verified 2026-09-29T22:33:04Z

## DELTA-20260929-009

Status: PUBLISHED

Detected: 2026-09-30T00:58:16Z

Published: 2026-09-30T01:25:36Z

Sources:
- Canonical branch state `19cd428dd526963bf6308d18c31e076a10c24dae` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, inspected as predecessor evidence only.
- The bounded NG-6 research chain NG-6A and NG-6B under the Scope Lock `DECISION-20260929-007`, adjudicated by the Control Room and recorded as `DECISION-20260929-008`.

Prior claim: NG-6 — Commercial Arrangement–Legal Instrument Composition Architecture is AUTHORIZED for research/design only under `DECISION-20260929-006`, scope-locked by `DECISION-20260929-007`; the Commercial Arrangement / Agreement Snapshot / Legal Instrument contract, including the agreement snapshot and multi-arrangement instruments, is open as the subject of NG-6.

Candidate/new claim: NG-6 has reached a terminal conceptual architecture conclusion under `DECISION-20260929-008`: a Commercial Arrangement may have zero or more Legal Instruments; each Legal Instrument is arrangement-scoped to one Commercial Arrangement in the present neutral core (L2), with L3 retained only as an evidence-triggered reconsideration case; Agreement Snapshot has historical semantics (S2) without a selected persistence or derivation mechanism; a known Snapshot↔Instrument correspondence stays anchored historically; and no universal precedence rule between commercial and legal truth is defined.

Claimed transition: NG-6 AUTHORIZED / RESEARCH-DESIGN OPEN → NG-6 COMMERCIAL ARRANGEMENT–LEGAL INSTRUMENT COMPOSITION ARCHITECTURE CLOSED.

Scope: Canonical publication of the Control Room's terminal NG-6 architecture adjudication only. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable repository facts:
  - canonical HEAD `19cd428dd526963bf6308d18c31e076a10c24dae` (`Publish DELTA-20260929-008`), with `DECISION-20260929-006` and `DECISION-20260929-007` ACTIVE and `DELTA-20260929-008` PUBLISHED;
  - `CURRENT-STATE.md` there records NG-6 as AUTHORIZED and lists the Commercial Arrangement / Agreement Snapshot / Legal Instrument contract as the open NG-6 subject.
- Build-97 predecessor evidence (NG-6A, product commit `b3f41bf`):
  - Deal→Contract 0..n and Contract→Deal exactly one, as predecessor topology only (`src/AgencyOS.Domain/Legal/Contract.cs`; `src/AgencyOS.Infrastructure/Persistence/Configurations/M8Configurations.cs`);
  - the accepted immutable Offer as a partial historical commercial-snapshot analogue (`src/AgencyOS.Domain/Deals/Offer.cs`);
  - ContractVersion as drafting history, and typed ContractRelationship kinds as instrument precedents (`src/AgencyOS.Domain/Legal/ContractVersion.cs`; `src/AgencyOS.Domain/Legal/Notice.cs`);
  - RightsGrant as a distinct fact (`src/AgencyOS.Domain/Legal/RightsGrant.cs`).
- External domain evidence (NG-6B, exactly two verticals):
  - 2023 WGA–AMPTP MBA Articles 13.A.3 and 14.B (https://www.wga.org/uploadedfiles/contracts/mba23.pdf);
  - SAG-AFTRA agreement evidence on definite engagement by an accepted verbal call with the written contract following later;
  - Musicians' Union live engagement standard contracts guidance (https://musiciansunion.org.uk/legal-money/contracts-and-agreements/standard-contracts/live-engagement-standard-contracts);
  - AFM LS-1 Q&A (https://members.afm.org/uploads/file/officers%20edge/OEWinter02.pdf);
  - AFM/CFM T2C travelling engagement contract (https://cfmusicians.afm.org/uploads/file/Travelling%20Eng%20Contract%20-%20T2C.pdf);
  - CFM/AFM multi-musician "severally" forms and MU multi-engagement forms, as L3 pressure tests.
- Control Room semantic adjudication: the Adjudication field below, recorded as `DECISION-20260929-008`.

Conflicts: None that survive adjudication. The correction chain is preserved:
- Claude's NG-6B report classified S0 as empirically falsified too broadly; the evidence proves independent canonical commercial-agreement truth but does not select persisted versus derived technical representation, so S2 is accepted at the semantic/historical level only;
- the NG-6A corrections stand: Build-97 agreed-but-unpapered state does not by itself prove permanent zero-instrument Arrangements; one-Deal-per-Contract is predecessor precedent; Build-97 had no independent Agreement Snapshot fact;
- the multi-musician and multi-engagement instruments did not prove an indivisible one-instrument/multiple-Arrangement case, so they do not justify L3;
- no contradiction with NG-1 through NG-5 was found.

Authority required:
- `CONTROL_ROOM`, for the semantic architecture adjudication and stage completion;
- machine-verifiable fact, for repository and publication facts;
- no new Owner decision is required. `DECISION-20260929-006` remains the Owner authorization provenance that allowed NG-6 research/design.

Adjudication: The Control Room independently reviewed the NG-6A predecessor baseline and the NG-6B two-vertical falsification evidence, applied the corrections recorded under Conflicts, and ACCEPTS the conceptual architecture recorded as `DECISION-20260929-008`: L0 and L1 rejected; L2 accepted as the current minimum neutral-core contract; L3 not justified now and retained only as an evidence-triggered reconsideration case; S0 and S1 rejected; S2 accepted at the semantic level with persistence/derivation unselected. This becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260929-009
  Semantic basis: f3635faf6b7e5a747b12f89e2c4696853d1d2629
  CURRENT-STATE.next.md blob SHA-256: 20fcf730fa06cf267fba502a6bee6f93ea4e82305639b1953f4aa8d2a32cb5f3
  PUBLICATION-PAYLOAD.json blob SHA-256: 1775bca54a17ab9f6408ca58e2d08b1a66254dc7de567ccfa13e3d256cbc93b2
  Authorized: 2026-09-30T01:18:51Z
  Reference: Adjudication of DELTA-20260929-009
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260929-008` is added as the durable NG-6 architecture decision.
- `CURRENT-STATE.md` records NG-6 Commercial Arrangement–Legal Instrument Composition Architecture as CLOSED under `DECISION-20260929-008`.
- The open Commercial questions "the agreement snapshot" and "multi-arrangement instruments" and the NG-6 open-subject note are replaced by the questions `DECISION-20260929-008` leaves open.
- The next bounded action becomes obtaining explicit Owner authorization before beginning any post-NG-6 stage, including NG-7.
- No implementation authority follows.

Supersedes: The current-state claims that NG-6 is AUTHORIZED with research/design open and that the Commercial Arrangement / Agreement Snapshot / Legal Instrument contract is an open NG-6 subject. It supersedes no prior Decision ID.

Unchanged:
- Build 97 released product identity and all implementation behavior.
- Operational Closure remains terminal.
- Self-Update V1 remains terminal.
- NG-1 through NG-5 remain CLOSED; `DECISION-20260928-001` to `-004`, `DECISION-20260929-002` and `DECISION-20260929-005` remain in force.
- `DECISION-20260929-006` remains the Owner authorization that allowed NG-6 research/design.
- `DECISION-20260929-007` remains the Scope Lock that governed NG-6.
- No product code change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- NG-7 remains unauthorized and not begun.

Open / unresolved questions:
- The deferred items listed as Open in `DECISION-20260929-008` remain open.
- None of these blocks NG-6 closure.

Forbidden implications: This delta does **not**:
- authorize product implementation;
- authorize schema/API/domain expansion;
- authorize migrations;
- authorize NG-7;
- select a database table, FK, EF mapping, persistence strategy or API shape;
- choose between persisted and derived Agreement Snapshots;
- declare L3 impossible;
- approve a raw Arrangement↔Legal Instrument many-to-many model;
- make Proposal/Offer the Agreement Snapshot;
- adopt Build-97 Deal, Contract or ContractVersion as canonical architecture;
- introduce a universal legal-effect engine;
- define a universal precedence of commercial over legal truth or the reverse;
- reintroduce mandate multiplicity;
- make Claude an architecture authority.

Publication receipt: 914676e14de9c8ec5602032a711ccc186af070e9 on origin/operational-regression-gate; remote readback verified 2026-09-30T01:25:36Z

## DELTA-20260929-010

Status: PUBLISHED

Detected: 2026-09-30T02:25:06Z

Published: 2026-09-30T02:34:09Z

Sources:
- Explicit Owner authorization recorded as `DECISION-20260929-009`.
- Canonical branch state `c8bd3fc38e6e0e840691b33157e9f46a433fae5f` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.

Prior claim: NG-6 is CLOSED under `DECISION-20260929-008`. No post-NG-6 stage, including NG-7, is authorized; explicit Owner authorization is required before one begins.

Candidate/new claim: NG-7 / the next post-NG-6 stage becomes AUTHORIZED for research/design only under `DECISION-20260929-009`.

Claimed transition: NG-7 UNAUTHORIZED / NOT BEGUN → NG-7 AUTHORIZED FOR RESEARCH/DESIGN ONLY.

Scope: Governance authorization publication only. It records no NG-7 title, bound question or Scope Lock, performs no NG-7 research and makes no architecture conclusion. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable canonical repository state: HEAD `c8bd3fc38e6e0e840691b33157e9f46a433fae5f` (`Publish DELTA-20260929-009`); `CURRENT-STATE.md` there records NG-6 as CLOSED under `DECISION-20260929-008` and requires explicit Owner authorization before any post-NG-6 stage, including NG-7.
- Explicit Owner decision: recorded, with the Owner's exact response and the exact question it answered, as `DECISION-20260929-009`.
- No product-code, schema, API, domain or migration change is part of this transition.

Conflicts: None.

Authority required:
- `OWNER`, for the decision to begin NG-7 research/design;
- `CONTROL_ROOM`, for faithful narrow normalization and publication planning;
- machine-verifiable fact, for repository and publication facts.

Adjudication: The Owner replied “מאשר.” to the Control Room's exact bounded authorization question whether to begin NG-7 / the next post-NG-6 stage for research and design only, without implementation or product/schema/API/domain expansion, or to stop at NG-6 CLOSED. The Control Room normalizes that reply narrowly as research/design authorization only. It does not decide the NG-7 title, bound question, Scope Lock, hypotheses, substages, exit criteria or any conclusion; the Control Room must establish the Scope Lock before substantive NG-7 research. The authorization becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: OWNER
  Delta: DELTA-20260929-010
  Semantic basis: f0c0a5c215fc94cf47b1a6966f1f5ed6c8c71311
  CURRENT-STATE.next.md blob SHA-256: 33c49e803bca0ab2a356efc9b92b42f47a8424a2a75991d70149a19dbe2dca34
  PUBLICATION-PAYLOAD.json blob SHA-256: fad9b08a30ca758463b30d9df4402a739cccb62bbb5e205fdda7adc48cd7edae
  Authorized: 2026-09-30T02:32:20Z
  Reference: Adjudication of DELTA-20260929-010
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260929-009` becomes the durable Owner authorization for NG-7 research/design.
- `CURRENT-STATE.md` records NG-7 as AUTHORIZED for research/design only under `DECISION-20260929-009`, with its exact title and bounded Scope Lock still to be established by the Control Room.
- The exact next bounded action becomes Control Room definition and canonical establishment of the NG-7 title, bounded question, Scope Lock, falsification boundary, substages and exit criteria.
- No implementation authority follows.

Supersedes: The current-state claim that no post-NG-6 stage, including NG-7, is authorized. It supersedes no Decision ID.

Unchanged:
- NG-6 remains CLOSED under `DECISION-20260929-008`.
- All NG-1 through NG-6 architecture decisions remain in force.
- Build 97 remains the released product identity.
- Operational Closure remains COMPLETE and terminal.
- Self-Update V1 remains COMPLETE.
- No next-generation architecture is implemented.
- No product/schema/API/domain behavior changes.
- No migrations.
- NG-8 remains unauthorized.

Open / unresolved questions:
- The exact NG-7 title, bound question and Scope Lock.
- Hypotheses, falsification boundary, substages and exit criteria.
- Any genuine Owner-reserved ambiguity discovered later.

Forbidden implications: This delta does **not**:
- establish an NG-7 title;
- establish the NG-7 Scope Lock;
- select an NG-7 architecture;
- claim that NG-7 research has occurred;
- start substantive NG-7 research;
- reopen NG-1 through NG-6;
- implement any next-generation architecture;
- approve schema/API/domain expansion;
- approve migrations;
- grant NG-8 or later-stage authority;
- make Claude a semantic authority.

Publication receipt: 85e6598d2c83019f7a81fd6f65b0304d7b3af97f on origin/operational-regression-gate; remote readback verified 2026-09-30T02:34:09Z

## DELTA-20260929-011

Status: PUBLISHED

Detected: 2026-09-30T02:42:53Z

Published: 2026-09-30T02:53:30Z

Sources:
- The Control Room Scope Lock recorded as `DECISION-20260929-010`.
- The published Owner authorization `DECISION-20260929-009`.
- Canonical branch state `0428ab3a22bb1cab88da7d21ff89d6b826fc7673` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.

Prior claim: NG-7 is AUTHORIZED for research/design only under `DECISION-20260929-009`; its exact title and bounded Scope Lock remain to be established by the Control Room before substantive research.

Candidate/new claim: NG-7 is now exactly scope-locked as "Amount Determination & Economic Truth Architecture" under `DECISION-20260929-010`, within the already-published Owner research/design authorization in `DECISION-20260929-009`.

Claimed transition: NG-7 AUTHORIZED / NOT SCOPE-LOCKED → NG-7 AUTHORIZED FOR RESEARCH/DESIGN ONLY / SCOPE LOCKED.

Scope: Governance Scope Lock publication only. This transition performs no NG-7 research and makes no architecture conclusion. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable canonical repository state: HEAD `0428ab3a22bb1cab88da7d21ff89d6b826fc7673` (`Publish DELTA-20260929-010`); `CURRENT-STATE.md` there records NG-7 as AUTHORIZED under `DECISION-20260929-009`, assigns definition of the exact NG-7 title and Scope Lock to the Control Room, and lists the amount-determination record, the future commission model and ledger integration as open Commercial questions.
- Published governance: `DECISION-20260928-004`, `DECISION-20260929-002`, `DECISION-20260929-005` and `DECISION-20260929-008`, as recorded in the evidence of `DECISION-20260929-010`.
- Control Room Scope Lock: recorded as `DECISION-20260929-010`; no substantive NG-7 research was performed in creating it.
- No product-code, schema, API, domain or migration change is part of this transition.

Conflicts: None.

Authority required:
- `CONTROL_ROOM`, for the Scope Lock;
- machine-verifiable fact, for repository and publication facts;
- no new Owner decision is required, because `DECISION-20260929-009` already authorizes NG-7 research/design and the active `CURRENT-STATE.md` explicitly assigns Scope Lock definition to the Control Room.

Adjudication: The Control Room establishes the exact NG-7 title, bound question, scope, closed distinctions, falsification models E0–E3, two-vertical research boundary, substages NG-7A–D and exit criteria recorded in `DECISION-20260929-010`, within the Owner research/design authorization of `DECISION-20260929-009`. The Scope Lock pre-selects no hypothesis. It becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260929-011
  Semantic basis: 55af69ff5fea0e2a3e26c45eade16cf3c6936a62
  CURRENT-STATE.next.md blob SHA-256: f1e17f9d2c7158e1000e04de3745d26ee961ee79cf9efe49358e094f14195f6d
  PUBLICATION-PAYLOAD.json blob SHA-256: 7d33185d5ec6049be635cb948d642288f1abac0cd1b26e48389301c545058213
  Authorized: 2026-09-30T02:51:02Z
  Reference: Adjudication of DELTA-20260929-011
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260929-010` becomes the durable NG-7 Scope Lock.
- `CURRENT-STATE.md` records NG-7 as NG-7 Amount Determination & Economic Truth Architecture, still AUTHORIZED under `DECISION-20260929-009`, with `DECISION-20260929-010` as its governing Scope Lock.
- The exact next bounded action becomes NG-7A — the Build-97 predecessor source baseline for amount determination and adjacent economic/finance semantics.
- No implementation authority follows.

Supersedes: The current-state claim that NG-7's exact title and bounded Scope Lock remain to be established. It supersedes no Decision ID.

Unchanged:
- NG-6 remains CLOSED under `DECISION-20260929-008`.
- All NG-1 through NG-6 architecture decisions remain in force.
- `DECISION-20260929-009` remains the Owner authorization for NG-7 research/design.
- Build 97 remains the released product identity.
- Operational Closure remains COMPLETE and terminal.
- Self-Update V1 remains COMPLETE.
- No product/schema/API/domain behavior changes.
- No migrations.
- NG-8 remains unauthorized.

Open / unresolved questions:
- The evidence and final adjudication of E0–E3.
- Every exit criterion listed in `DECISION-20260929-010`.
- All questions explicitly excluded by `DECISION-20260929-010`.

Forbidden implications: This delta does **not**:
- claim that substantive NG-7 research has begun;
- decide E0–E3;
- choose an Amount Determination persistence model;
- decide commission architecture;
- decide receivable/invoice/payment/ledger architecture;
- permit implementation;
- approve schema/API/domain expansion;
- approve migrations;
- grant NG-8 or later-stage authority;
- reopen NG-1 through NG-6;
- make Claude a semantic authority.

Publication receipt: 2eaa873744c397254033faf6dd941590227254ae on origin/operational-regression-gate; remote readback verified 2026-09-30T02:53:30Z

## DELTA-20260930-001

Status: PUBLISHED

Detected: 2026-09-30T06:38:52Z

Published: 2026-09-30T06:47:43Z

Sources:
- Canonical branch state `6f82cf5490aaaf29396160c2229f6d3a538d5624` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, inspected as predecessor evidence only (NG-7A).
- The bounded NG-7 research chain NG-7A and NG-7B under the Scope Lock `DECISION-20260929-010`, adjudicated by the Control Room and recorded as `DECISION-20260930-001`.

Prior claim: NG-7 Amount Determination & Economic Truth Architecture is AUTHORIZED for research/design only under `DECISION-20260929-009` and scope-locked by `DECISION-20260929-010`; no substantive NG-7 research has been canonically accepted; the amount-determination record is an open question.

Candidate/new claim: NG-7 has reached a terminal conceptual semantic architecture conclusion under `DECISION-20260930-001`: an Amount Determination is a distinct arrangement-scoped economic fact for one independently meaningful economic component, with zero or more per Arrangement; governing method/basis and historical realization are semantically distinct; methods may exist before all inputs, before receivable/invoice/payment and before a final Legal Instrument; known provenance stays historically anchored across commercial, legal, external-schedule and realized-input facts; money preserves currency, units and percentage basis; commission reuses determination semantics but remains a distinct representation-economics claim; Receivable, Invoice, Payment/Allocation and accounting projection remain downstream and distinct; E0 rejected, E1 rejected, E2 accepted with refinement, E3 rejected as claim unification; implementation remains unselected.

Claimed transition: NG-7 AUTHORIZED / SCOPE-LOCKED / RESEARCH COMPLETE → NG-7 AMOUNT DETERMINATION & ECONOMIC TRUTH ARCHITECTURE CLOSED.

Scope: Canonical publication of the Control Room's terminal NG-7 architecture adjudication only. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable repository facts:
  - canonical HEAD `6f82cf5490aaaf29396160c2229f6d3a538d5624` (`Publish DELTA-20260929-011`), with `DECISION-20260929-009` and `DECISION-20260929-010` ACTIVE and `DELTA-20260929-011` PUBLISHED;
  - `CURRENT-STATE.md` there records NG-7 as AUTHORIZED and scope-locked, with no substantive NG-7 research canonically accepted, and lists the amount-determination record as open.
- Build-97 predecessor evidence (NG-7A, product commit `b3f41bf`): `src/AgencyOS.Domain/Finance/MonetaryObligation.cs`, `Receivable.cs`, `Commission.cs`, `Payment.cs`, `Invoice.cs`, `Ledger.cs`; `src/AgencyOS.Domain/Deals/Money.cs`; the finance application handlers, persistence migration and API/client projections.
- External domain evidence (NG-7B, exactly two verticals): WGA, DGA and SAG-AFTRA primary/authoritative material for Film/TV; AFM, Musicians' Union and bounded primary booking/engagement forms for live music / artist booking.
- Control Room semantic adjudication: the Adjudication field below, recorded as `DECISION-20260930-001`.

Conflicts: None that survive adjudication. The NG-7 correction chain is preserved:
- Build 97 does not lack an amount-determination predecessor; MonetaryObligation is strong and explicit;
- Build 97 is not scalar-only; Fixed/Formula/Contingent/Unknown and multiple obligations already exist;
- E2 required refinement because determination method and later realization are semantically distinct;
- Amount Determination provenance is not limited to commercial/legal records; external governing schedules and later realized inputs may participate;
- commission shares Amount Determination calculation semantics but remains a distinct representation-economics claim;
- greater-of/crediting/offset relations are real but not sufficiently proved as a universal neutral-core taxonomy.

Authority required:
- `CONTROL_ROOM`, for the terminal NG-7 semantic adjudication;
- machine-verifiable fact, for repository and publication facts;
- no new Owner decision is required to close NG-7. The Owner's existing authorization `DECISION-20260929-009` permitted the bounded research/design stage, and no post-NG-7 stage is authorized by this closure.

Adjudication: The Control Room independently reviewed the NG-7A Build-97 predecessor baseline and the NG-7B two-vertical falsification evidence, applied the corrections recorded under Conflicts, and ACCEPTS the conceptual semantic architecture recorded as `DECISION-20260930-001`: E0 rejected; E1 rejected; E2 accepted with the method-versus-realization refinement; E3 rejected as a claim-unification architecture; no Owner-reserved ambiguity remains. This becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260930-001
  Semantic basis: 812325fafda011f3345380a329773d7b9a1be45b
  CURRENT-STATE.next.md blob SHA-256: a515fd7123eb004668ea51edb0cecde98bda1dfd4088763e250db95479feff97
  PUBLICATION-PAYLOAD.json blob SHA-256: 8d5acbf71999b0105fd8b9884602a75560c8e311323b45ed45d12533afc077a5
  Authorized: 2026-09-30T06:44:07Z
  Reference: Adjudication of DELTA-20260930-001
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260930-001` is added as the durable NG-7 architecture decision.
- `CURRENT-STATE.md` records NG-7 Amount Determination & Economic Truth Architecture as CLOSED under `DECISION-20260930-001`.
- The open question "the amount-determination record" and the NG-7 open-subject note are replaced by the residual implementation questions `DECISION-20260930-001` leaves open.
- The next bounded action becomes obtaining explicit Owner authorization before beginning any post-NG-7 stage, including NG-8.
- No implementation authority follows.

Supersedes: The current-state claims that NG-7 research is not canonically accepted and that the amount-determination record is wholly open. It supersedes no prior Decision ID.

Unchanged:
- Build 97 released product identity and all implementation behavior.
- Operational Closure remains terminal.
- Self-Update V1 remains terminal.
- NG-1 through NG-6 remain CLOSED under their existing decisions.
- `DECISION-20260929-009` remains the Owner authorization that allowed NG-7 research/design.
- `DECISION-20260929-010` remains the Scope Lock that governed NG-7.
- No product code change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- NG-8 remains unauthorized and not begun.

Open / unresolved questions:
- The deferred items listed as Open in `DECISION-20260930-001` remain open.
- None of these blocks NG-7 closure.

Forbidden implications: This delta does **not**:
- permit implementation;
- approve schema/API/domain expansion;
- approve migrations;
- grant NG-8 or later-stage authority;
- choose a persistence, version-storage or event mechanism;
- approve a generic expression language or calculation DSL;
- approve a universal component-relation taxonomy;
- create a universal EconomicEntitlement;
- decide the commission model or placement;
- decide downstream finance cardinalities;
- claim legal enforceability or collectibility for pre-instrument economics;
- promote Build-97 MonetaryObligation limitations to neutral-core invariants;
- reopen NG-1 through NG-6;
- make Claude an architecture authority.

Publication receipt: fc5850383c4cde689a14bb847746b6fd5f0d482a on origin/operational-regression-gate; remote readback verified 2026-09-30T06:47:43Z

## DELTA-20260930-002

Status: PUBLISHED

Detected: 2026-09-30T07:27:43Z

Published: 2026-09-30T07:51:52Z

Sources:
- Explicit Owner decisions recorded as `DECISION-20260930-002` and `DECISION-20260930-003`.
- Canonical branch state `440e494fc2492de3d95c5c15bc1f445a9d52c4f1` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.

Prior claim: NG-7 is CLOSED under `DECISION-20260930-001`. No post-NG-7 stage, including NG-8, is authorized; explicit Owner authorization is required before one begins. Control Room conversation transitions are governed by project instructions and ad hoc continuity practice, with no canonical correspondence contract.

Candidate/new claim:
1. NG-8 — Agency Commission & Representation-Economics Claim Architecture is explicitly OWNER-AUTHORIZED and SCOPE-LOCKED for research/design only under `DECISION-20260930-002`.
2. Control Room correspondence, Claude prompts and cross-conversation handoffs are explicitly OWNER-GOVERNED by the Recursive Control-Room Correspondence & Transition Contract under `DECISION-20260930-003`, whose recursion covers the message configuration itself.

Claimed transition:
1. Post-NG-7 work UNAUTHORIZED → NG-8 OWNER-AUTHORIZED AND SCOPE-LOCKED FOR RESEARCH/DESIGN ONLY.
2. Control Room conversation transition UNDER PROJECT INSTRUCTIONS AND AD HOC PRACTICE → OWNER-GOVERNED RECURSIVE CORRESPONDENCE / MESSAGE / HANDOFF CONTRACT.

Scope: Governance authorization and correspondence-policy publication only. This transition performs no NG-8 research and makes no architecture conclusion. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable canonical repository state: HEAD `440e494fc2492de3d95c5c15bc1f445a9d52c4f1` (`Publish DELTA-20260930-001`); `CURRENT-STATE.md` there records NG-7 as CLOSED under `DECISION-20260930-001` and requires explicit Owner authorization before any post-NG-7 stage, including NG-8; no NG-8 authorization exists.
- Explicit Owner decisions: recorded as `DECISION-20260930-002` (the complete NG-8 stage definition) and `DECISION-20260930-003` (the recursive correspondence contract, including the Owner's clarification that recursion covers the message configuration itself).
- No product-code, schema, API, domain or migration change is part of this transition.

Conflicts: None.

Authority required:
- `OWNER`, for the NG-8 authorization with its exact Scope Lock and for the correspondence contract;
- `CONTROL_ROOM`, for faithful normalization and publication planning;
- machine-verifiable fact, for repository and publication facts.

Adjudication: The Owner explicitly approved the Control Room's complete NG-8 stage definition for research/design only, and explicitly adopted the Recursive Control-Room Correspondence & Transition Contract, clarifying that recursion covers the configuration of the message itself — prompts, correction/refutation reporting, prompt budget, current state, branch/HEAD, Decision/Delta IDs, stage/substage/state, proved facts, open items, forbidden work, exact next action and Owner Action — and that each handoff must instruct the next to reproduce the same configuration. The Control Room normalizes both approvals as recorded, without widening either. The correspondence contract governs continuity only and grants no stage or product authority. Both become canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: OWNER
  Delta: DELTA-20260930-002
  Semantic basis: ace4f9890488516da49be163d482f65fab7c21fb
  CURRENT-STATE.next.md blob SHA-256: 7f91adf71b50866c5bdd112e301a314a2586cb8dc9c2bc1dd0c79f4f4fba7569
  PUBLICATION-PAYLOAD.json blob SHA-256: fad1dbeb1ebaca67d9c1f307993b08d9a9593849936bc49fa008c0f6a52a1715
  Authorized: 2026-09-30T07:48:38Z
  Reference: Adjudication of DELTA-20260930-002
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260930-002` becomes the durable Owner authorization and exact Scope Lock for NG-8.
- `DECISION-20260930-003` becomes the durable Owner correspondence/transition contract.
- `CURRENT-STATE.md` records NG-8 Agency Commission & Representation-Economics Claim Architecture as AUTHORIZED under `DECISION-20260930-002`, with substantive research not yet begun.
- `CURRENT-STATE.md` records the correspondence/transition policy concisely by reference to `DECISION-20260930-003`.
- The exact next bounded action becomes NG-8A — the Build-97 predecessor baseline for commission and representation-economics claim semantics.
- No implementation authority follows.

Supersedes: The current-state claim that no post-NG-7 stage, including NG-8, is authorized. It supersedes no Decision ID.

Unchanged:
- NG-0 through NG-7 remain closed/approved exactly as currently published.
- `DECISION-20260930-001` remains the governing NG-7 architecture decision.
- Build 97 remains the released product identity.
- Operational Closure remains COMPLETE and terminal.
- Self-Update V1 remains COMPLETE.
- The canonical/domain authority hierarchy, Owner final decision authority and Claude's executor-only role.
- No product/schema/API/domain behavior changes.
- No migrations.
- NG-9 remains unauthorized.

Open / unresolved questions:
- The terminal NG-8 architecture result.
- Every exit criterion listed in `DECISION-20260930-002`.
- Implementation, NG-9 and later stages.

Forbidden implications: This delta does **not**:
- claim that NG-8A research has begun;
- decide the NG-8 architecture;
- select a C0–C3 winner;
- permit implementation;
- approve schema/API/domain expansion;
- approve migrations;
- grant NG-9 or later-stage authority;
- let the correspondence contract grant stage or product authority;
- reopen NG-1 through NG-7;
- treat Claude as an authority.

Publication receipt: f22a9040e0ed61f685421f296958b82ec958f9cf on origin/operational-regression-gate; remote readback verified 2026-09-30T07:51:52Z

## DELTA-20260930-003

Status: PUBLISHED

Detected: 2026-09-30T14:59:44Z

Published: 2026-09-30T15:51:47Z

Sources:
- Canonical branch state `1d3be012dfffb69c77856af03716bdc46ba5ea7e` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, inspected as predecessor evidence only (NG-8A).
- The bounded NG-8 research chain NG-8A and NG-8B under the Scope Lock `DECISION-20260930-002`, adjudicated by the Control Room and recorded as `DECISION-20260930-004`.

Prior claim: NG-8 is AUTHORIZED / OPEN IN RESEARCH under `DECISION-20260930-002`; terminal commission-claim architecture is unresolved.

Candidate/new claim: NG-8 is CLOSED under `DECISION-20260930-004`: a Representation-Economics Claim is a distinct historical, arrangement-scoped, claimant-specific agency-side economic claim under a representation-authority lineage; rule/terms are distinct from claim; a claim may exist before quantification; source/authority provenance is historical and may be composite; claim-side determination reuses NG-7 Amount Determination semantics; source-defined earned/payable/collected facts are distinct with no universal lifecycle and no universal governing date; one claim may depend on one or more represented-party Amount Determinations only where they form one source-defined basis under one independently meaningful commission treatment; the claim is distinct from Receivable, Invoice, Payment/Allocation and accounting; C0 rejected, C1 rejected, C2 accepted with refinement, C3 rejected; implementation remains unselected.

Claimed transition: NG-8 AUTHORIZED / OPEN IN RESEARCH → NG-8 CLOSED.

Scope: Canonical publication of the Control Room's terminal NG-8 architecture adjudication only. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable repository facts:
  - canonical HEAD `1d3be012dfffb69c77856af03716bdc46ba5ea7e` (`Publish DELTA-20260930-002`), with `DECISION-20260930-002` and `DECISION-20260930-003` ACTIVE and `DELTA-20260930-002` PUBLISHED;
  - `CURRENT-STATE.md` there records NG-8 as AUTHORIZED under `DECISION-20260930-002` and NG-9 as unauthorized.
- Build-97 predecessor evidence (NG-8A, product commit `b3f41bf`): `src/AgencyOS.Domain/Finance/Commission.cs`; `src/AgencyOS.Finance.Rules/Commission.fs`, `Rules.fs`, `Money.fs`; `src/AgencyOS.Application/Finance/CommissionCommands.cs`, `FinanceCommands.cs`, `PaymentCommands.cs`, `LedgerPosting.cs`; the finance persistence configuration, migration, queries and API contracts.
- External domain evidence (NG-8B, exactly two verticals): WGA Franchise Agreement and Rider W, SAG Rule 16(g) and current SAG-AFTRA guidance, DGA–ATA Rider D and California Labor Code §§1700–1700.47 for Film/TV; the AFM Booking Agent Agreement, AFM Bylaws, AFM Entertainment Booking Agreement, New York General Business Law Article 11 and the UK Conduct of Employment Agencies and Employment Businesses Regulations 2003 / Employment Agencies Act 1973 for live music / artist booking.
- Control Room semantic adjudication: the Adjudication field below, recorded as `DECISION-20260930-004`.

Conflicts: None that survive adjudication. The complete NG-8 correction chain is preserved:
- effective dating does not itself guarantee historically correct governing-rule selection;
- Build-97 rule selection is client-scoped despite recorded RepresentationId;
- adjustments are not fully integrated into downstream commission/revenue truth;
- SpecificTerm lacks executed source-term resolution;
- GrossCompensation executes against one obligation, not aggregate gross;
- the per-allocation cap is not cumulative entitlement protection;
- allocation reversal was not proved to reverse related commission revenue;
- a claim may exist before the represented-party amount is finally quantified;
- claimant/beneficiary does not imply the represented party is always the payer;
- payment date is not a universal governing date;
- one commission rule per client is not a neutral invariant;
- the NG-8B proposition that "one claim tied to exactly one Amount Determination" was empirically falsified was too strong; one claim may depend on one or more represented-party Amount Determinations only where they form one source-defined basis under one independently meaningful commission treatment;
- C0 rejection establishes independent claim semantics, not a mandatory persisted claim entity/table.

Authority required:
- `CONTROL_ROOM`, for the terminal NG-8 semantic adjudication;
- machine-verifiable fact, for repository and publication facts;
- no new Owner decision is required to close NG-8. The Owner's existing authorization `DECISION-20260930-002` permitted the bounded research/design stage, and no post-NG-8 stage is authorized by this closure.

Adjudication: The Control Room independently reviewed the NG-8A Build-97 predecessor baseline and the NG-8B two-vertical falsification evidence, applied the corrections recorded under Conflicts, and ACCEPTS the conceptual semantic architecture recorded as `DECISION-20260930-004`: C0 rejected; C1 rejected; C2 accepted with refinement; C3 rejected; no Owner-reserved ambiguity remains; no NG-8B2 is required. This becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260930-003
  Semantic basis: 3672ed2fd127c36c855708cc0964739685d30c6d
  CURRENT-STATE.next.md blob SHA-256: 8ee57fc16509b95019302997a177e87cf7c52f30accc41ac49a3dd8e4365c8d8
  PUBLICATION-PAYLOAD.json blob SHA-256: 888cad791e19477b8f077e77ccc346596c1e1a864ebe1e74bef35660506687a8
  Authorized: 2026-09-30T15:32:41Z
  Reference: Adjudication of DELTA-20260930-003
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260930-004` is added as the durable NG-8 architecture decision.
- `CURRENT-STATE.md` records NG-8 Agency Commission & Representation-Economics Claim Architecture as CLOSED under `DECISION-20260930-004`.
- The open question on the exact commission model/placement/cardinality is narrowed to its implementation-open mechanics.
- The next bounded action becomes obtaining explicit Owner authorization before beginning any post-NG-8 stage, including NG-9.
- No implementation authority follows.

Supersedes: The current-state claim that NG-8 is open in research with unresolved commission-claim architecture. It supersedes no prior Decision ID.

Unchanged:
- NG-0 through NG-7 terminal decisions remain unchanged.
- `DECISION-20260930-002` remains the Owner authorization / Scope Lock that permitted NG-8.
- `DECISION-20260930-003` remains the active recursive correspondence contract.
- Build 97 released product identity and all implementation behavior.
- No product code change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- NG-9 remains unauthorized and not begun.

Open / unresolved questions:
- The implementation-open items listed as Open in `DECISION-20260930-004` remain open.
- None of these blocks NG-8 closure.

Forbidden implications: This delta does **not**:
- permit implementation;
- choose a persistence, event or version mechanism;
- approve schema/API/domain expansion;
- approve migrations;
- create a universal MonetaryClaim or EconomicEntitlement;
- impose a universal commission lifecycle;
- impose a universal governing date;
- grant NG-9 or later-stage authority;
- reopen NG-1 through NG-7;
- make Claude an architecture authority.

Publication receipt: 02a2e862d9df88fe77d3db5016cfb0678896db60 on origin/operational-regression-gate; remote readback verified 2026-09-30T15:51:47Z

## DELTA-20260930-004

Status: PUBLISHED

Detected: 2026-09-30T17:02:29Z

Published: 2026-09-30T17:31:14Z

Sources:
- Explicit Owner decision recorded as `DECISION-20260930-005`.
- Canonical branch state `80e300436a554bf23c5a970dfdcc2a3d54463639` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.

Prior claim: NG-8 is CLOSED under `DECISION-20260930-004`. No post-NG-8 stage, including NG-9, is authorized.

Candidate/new claim: NG-9 — Receivable Crystallization & Collectibility Architecture is OWNER-AUTHORIZED and SCOPE-LOCKED for research/design only under `DECISION-20260930-005`.

Claimed transition: NO POST-NG-8 STAGE AUTHORIZED → NG-9 OWNER-AUTHORIZED / SCOPE-LOCKED FOR RESEARCH/DESIGN ONLY.

Scope: Governance authorization and Scope Lock publication only. This transition performs no NG-9 research and makes no architecture conclusion. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable canonical repository state: HEAD `80e300436a554bf23c5a970dfdcc2a3d54463639` (`Publish DELTA-20260930-003`); `CURRENT-STATE.md` there records NG-8 as CLOSED under `DECISION-20260930-004` and requires explicit Owner authorization before any post-NG-8 stage, including NG-9; no NG-9 authorization exists.
- Explicit Owner decision: recorded, with the complete Control Room NG-9 stage definition the Owner approved, as `DECISION-20260930-005`.
- `DECISION-20260930-003` remains the active recursive correspondence contract.
- No product-code, schema, API, domain or migration change is part of this transition.

Conflicts: None.

Authority required:
- `OWNER`, for the NG-9 authorization and its exact Scope Lock;
- `CONTROL_ROOM`, for faithful normalization and publication planning;
- machine-verifiable fact, for repository and publication facts.

Adjudication: The Owner explicitly approved the Control Room's complete NG-9 stage definition — title, bounded question, in-scope and out-of-scope items, closed distinctions, hypotheses R0–R3 with none preselected, the two-vertical research boundary, substages NG-9A–D, exit criteria, Owner-decision rule, prompt budget and authority boundary — for research/design only. The Control Room records that approval without widening it. It becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: OWNER
  Delta: DELTA-20260930-004
  Semantic basis: e5561e87ec6452eb48b0607d0dadbacf616d8c6f
  CURRENT-STATE.next.md blob SHA-256: 8ae5679e949770504436dc6630efda3b341da64844a0bc8c9b4e4933f0b5a39c
  PUBLICATION-PAYLOAD.json blob SHA-256: 8903f8dac6208e5638beb159fc258378ab14db5c48ca684f870eb2fe87d0b6ad
  Authorized: 2026-09-30T17:26:13Z
  Reference: Adjudication of DELTA-20260930-004
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260930-005` becomes the durable Owner authorization and exact Scope Lock for NG-9.
- `CURRENT-STATE.md` records NG-9 Receivable Crystallization & Collectibility Architecture as AUTHORIZED under `DECISION-20260930-005`, with substantive research not yet begun.
- The exact next bounded action becomes NG-9A — the Build-97 Receivable predecessor baseline.
- No implementation authority follows.

Supersedes: The current-state claim that no post-NG-8 stage, including NG-9, is authorized. It supersedes no Decision ID.

Unchanged:
- Operational Closure remains COMPLETE.
- Self-Update V1 remains COMPLETE.
- NG-0 through NG-8 remain closed/approved exactly under their existing terminal decisions.
- `DECISION-20260930-003` remains the active recursive correspondence contract.
- Build 97 remains the released product identity.
- No product/schema/API/domain behavior changes.
- No migrations.
- NG-10 remains unauthorized.

Open / unresolved questions:
- NG-9A and NG-9B evidence.
- The terminal R0–R3 disposition and NG-9 semantic architecture.
- All implementation mechanics.
- NG-10 and all later stages.

Forbidden implications: This delta does **not**:
- claim that NG-9A research has begun;
- select an R0–R3 winner;
- decide the Receivable architecture;
- permit implementation;
- choose a persistence mechanism;
- approve schema/API/domain expansion;
- approve migrations;
- grant NG-10 or later-stage authority;
- reopen NG-1 through NG-8;
- treat Claude as an authority.

Publication receipt: bd63f440c008dcce389605a4d61cef58791b84bd on origin/operational-regression-gate; remote readback verified 2026-09-30T17:31:14Z

## DELTA-20260930-005

Status: PUBLISHED

Detected: 2026-09-30T20:15:18Z

Published: 2026-09-30T21:03:41Z

Sources:
- Canonical branch state `d3b16a0f9a8014d4e5ca92e9546232d550341eae` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, inspected as predecessor evidence only (NG-9A).
- The bounded NG-9 research chain NG-9A and NG-9B under the Scope Lock `DECISION-20260930-005`, adjudicated by the Control Room and recorded as `DECISION-20260930-006`.

Prior claim: NG-9 is AUTHORIZED / OPEN IN RESEARCH under `DECISION-20260930-005`; terminal Receivable architecture and R0–R3 dispositions are unresolved.

Candidate/new claim: NG-9 is CLOSED under `DECISION-20260930-006`: a Receivable is a distinct historical, Commercial-Arrangement-scoped collectible position for one independently meaningful source-defined crystallization/due position; upstream Amount Determination and Representation-Economics Claim remain distinct; there is no universal crystallization trigger; one upstream economic source may produce zero or more Receivables, and one Receivable may depend on one or more upstream facts only where they genuinely form one source-defined collectible position; partial/installment/periodic crystallization is distinct from partial Payment; Invoice is distinct and not universally required but may be a source-defined condition; obligor, creditor/beneficiary and observed payer/source of cash remain distinct; due rule/provenance is distinct from resolved date; historical change is not silently rewritten; Payment may precede Receivable; Payment/Allocation and accounting remain downstream and distinct; R0 rejected, R1 rejected, R2 accepted with refinement, R3 rejected; implementation remains unselected.

Claimed transition: NG-9 AUTHORIZED / OPEN IN RESEARCH → NG-9 CLOSED.

Scope: Canonical publication of the Control Room's terminal NG-9 architecture adjudication only. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable repository facts:
  - canonical HEAD `d3b16a0f9a8014d4e5ca92e9546232d550341eae` (`Publish DELTA-20260930-004`), with `DECISION-20260930-005` and `DECISION-20260930-003` ACTIVE and `DELTA-20260930-004` PUBLISHED;
  - `CURRENT-STATE.md` there records NG-9 as AUTHORIZED under `DECISION-20260930-005` and NG-10 as unauthorized.
- Build-97 predecessor evidence (NG-9A, product commit `b3f41bf`): `src/AgencyOS.Domain/Finance/Receivable.cs`, `MonetaryObligation.cs`, `Invoice.cs`, `Payment.cs`, `Commission.cs`; `src/AgencyOS.Finance.Rules/Allocation.fs`; `src/AgencyOS.Application/Finance/FinanceCommands.cs`, `PaymentCommands.cs`, `LedgerPosting.cs`; the finance persistence configuration, migration and API contracts.
- External domain evidence (NG-9B, exactly two verticals), including the Control-Room-checked load-bearing sources: the 2023 WGA Theatrical and Television Basic Agreement; the Musicians' Union Standard Live Engagement Contract L2 (2025 form); AFM Form T2C Travelling Engagement Contract; the UK Conduct of Employment Agencies and Employment Businesses Regulations 2003, regulation 25; the University of Memphis Performance Agreement.
- Control Room semantic adjudication: the Adjudication field below, recorded as `DECISION-20260930-006`.

Conflicts: None that survive adjudication. The complete material correction chain is preserved in `DECISION-20260930-006` §W:
- NG-9A: one-obligation/one-Receivable is lifecycle behavior, not persistence uniqueness; amount override lacks structured provenance and a partial override can strand the remainder; Invoice lacks Contract/Debtor consistency and duplicate-billing protection; due-date provenance is not preserved; observed payer is not checked against the contractual debtor; predecessor pressure on R0 is mixed;
- NG-9B: installments may be separately triggered due positions; the obligor may change by assumption; cash may precede entitlement; Invoice may condition payability/timing in particular sources; source rules may group facts or defer payability until an accumulation threshold;
- NG-9C: rejecting universal invoice-gating does not make Invoice irrelevant; not every source event creates a Receivable; obligor assumption requires historical obligor provenance without deciding same-identity versus successor representation; cash preceding entitlement does not make every advance an unapplied Payment.

Authority required:
- `CONTROL_ROOM`, for the terminal NG-9 semantic adjudication;
- machine-verifiable fact, for repository and publication facts;
- no new Owner decision is required to close NG-9. The Owner's existing authorization `DECISION-20260930-005` permitted the bounded research/design stage, and no post-NG-9 stage is authorized by this closure.

Adjudication: The Control Room independently reviewed the NG-9A Build-97 predecessor baseline and the NG-9B two-vertical falsification evidence, independently verified the load-bearing primary sources, applied the corrections recorded under Conflicts, and ACCEPTS the conceptual semantic architecture recorded as `DECISION-20260930-006`: R0 rejected; R1 rejected; R2 accepted with refinement; R3 rejected; no Owner-reserved ambiguity remains; no NG-9B2 is required. This becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260930-005
  Semantic basis: a7af74f1b81df15d8598d5d7dfa3449bca4e7b90
  CURRENT-STATE.next.md blob SHA-256: 63a5f14bb824600edd9381989d82a60377fcd6e01268de37cee5c47130bb9806
  PUBLICATION-PAYLOAD.json blob SHA-256: dfea25dea967de4eb9f851dd8f9e8c4712dd3923e94a30c6a37b18fb869221a8
  Authorized: 2026-09-30T20:47:04Z
  Reference: Adjudication of DELTA-20260930-005
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260930-006` is added as the durable NG-9 architecture decision.
- `CURRENT-STATE.md` records NG-9 Receivable Crystallization & Collectibility Architecture as CLOSED under `DECISION-20260930-006`.
- The open question on the exact claim-to-Receivable mechanics is narrowed to its implementation-open mechanics.
- The next bounded action becomes obtaining explicit Owner authorization before beginning any post-NG-9 stage, including NG-10.
- No implementation authority follows.

Supersedes: The current-state claim that NG-9 is open in research with unresolved Receivable architecture. It supersedes no prior Decision ID.

Unchanged:
- Operational Closure remains COMPLETE.
- Self-Update V1 remains COMPLETE.
- NG-0 through NG-8 remain unchanged.
- `DECISION-20260930-005` remains the Owner authorization/Scope Lock that permitted NG-9.
- `DECISION-20260930-003` remains the recursive correspondence contract.
- Build 97 released product identity and all implementation behavior.
- No product code change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- NG-10 remains unauthorized and not begun.

Open / unresolved questions:
- The implementation-open items and evidence-triggered reconsideration cases listed as Open in `DECISION-20260930-006` remain open.
- None of these blocks NG-9 closure.

Forbidden implications: This delta does **not**:
- permit implementation;
- choose a persistence, event or version mechanism;
- approve schema/API/domain expansion;
- approve migrations;
- impose a universal invoice gate;
- impose a universal crystallization trigger;
- create a generic MonetaryClaim or EconomicEntitlement;
- impose a universal Receivable lifecycle;
- grant NG-10 or later-stage authority;
- reopen NG-1 through NG-8;
- make Claude an architecture authority.

Publication receipt: 936127e918617a2573086abb825e5464db4e3271 on origin/operational-regression-gate; remote readback verified 2026-09-30T21:03:41Z

## DELTA-20260930-006

Status: PUBLISHED

Detected: 2026-09-30T21:28:02Z

Published: 2026-09-30T22:31:23Z

Sources:
- Explicit Owner decision recorded as `DECISION-20260930-007`.
- Canonical branch state `576f812c4d9eedb7b3fc618a7dbf9148300cb492` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.

Prior claim: NG-9 is CLOSED under `DECISION-20260930-006`. No post-NG-9 stage, including NG-10, is authorized.

Candidate/new claim: NG-10 — Payment, Cash Application & Funds Provenance Architecture is OWNER-AUTHORIZED and SCOPE-LOCKED for research/design only under `DECISION-20260930-007`.

Claimed transition: NO POST-NG-9 STAGE AUTHORIZED → NG-10 OWNER-AUTHORIZED / SCOPE-LOCKED FOR RESEARCH/DESIGN ONLY.

Scope: Governance authorization and Scope Lock publication only. This transition performs no NG-10 research and makes no architecture conclusion. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable canonical repository state: HEAD `576f812c4d9eedb7b3fc618a7dbf9148300cb492` (`Publish DELTA-20260930-005`); `CURRENT-STATE.md` there records NG-9 as CLOSED under `DECISION-20260930-006` and requires explicit Owner authorization before any post-NG-9 stage, including NG-10; no NG-10 authorization exists.
- Explicit Owner decision: recorded, with the complete Control Room NG-10 stage definition the Owner approved, as `DECISION-20260930-007`.
- `DECISION-20260930-003` remains the active recursive correspondence contract.
- The Owner's instruction about a future recursive conversation transition is recorded in `DECISION-20260930-007` as continuity/governance provenance only; it grants no stage authority and triggers no transition.
- No product-code, schema, API, domain or migration change is part of this transition.

Conflicts: None.

Authority required:
- `OWNER`, for the NG-10 authorization and its exact Scope Lock;
- `CONTROL_ROOM`, for faithful normalization and publication planning;
- machine-verifiable fact, for repository and publication facts.

Adjudication: The Owner explicitly approved the Control Room's complete NG-10 stage definition — title, bounded question, in-scope and out-of-scope items, closed distinctions, hypotheses P0–P3 with none preselected, the two-vertical research boundary, substages NG-10A–D, exit criteria, Owner-decision rule, prompt budget and authority boundary — for research/design only. The Control Room records that approval without widening it. It becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: OWNER
  Delta: DELTA-20260930-006
  Semantic basis: acf628c4b6297e5157fe762c24e7c7cfbaa2ad0f
  CURRENT-STATE.next.md blob SHA-256: 70650ad3456e9a7ece1f651d41a4749a1d49101fda7c63d36f703cde6138e1fc
  PUBLICATION-PAYLOAD.json blob SHA-256: 9c22f631a2f5e1e2d8f144baddbda29e304d39f5a4a0deb88772fc43dc483efd
  Authorized: 2026-09-30T22:17:22Z
  Reference: Adjudication of DELTA-20260930-006
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260930-007` becomes the durable Owner authorization and exact Scope Lock for NG-10.
- `CURRENT-STATE.md` records NG-10 Payment, Cash Application & Funds Provenance Architecture as AUTHORIZED under `DECISION-20260930-007`, with substantive research not yet begun.
- The exact next bounded action becomes NG-10A — the Build-97 Payment / Allocation predecessor baseline.
- No implementation authority follows.

Supersedes: The current-state claim that no post-NG-9 stage, including NG-10, is authorized. It supersedes no Decision ID.

Unchanged:
- Operational Closure remains COMPLETE.
- Self-Update V1 remains COMPLETE.
- NG-0 through NG-9 remain closed/approved exactly under their existing terminal decisions.
- `DECISION-20260930-003` remains the active recursive correspondence contract.
- Build 97 remains the released product identity.
- No product/schema/API/domain behavior changes.
- No migrations.
- NG-11 remains unauthorized.

Open / unresolved questions:
- NG-10A and NG-10B evidence.
- The terminal P0–P3 dispositions and NG-10 semantic architecture.
- All implementation mechanics.
- The later appropriate recursive conversation-transition point.
- NG-11 and all later stages.

Forbidden implications: This delta does **not**:
- claim that NG-10A research has begun;
- select a P0–P3 winner;
- decide the Payment/Application architecture;
- permit implementation;
- choose a persistence mechanism;
- select a payment-rail implementation;
- decide a bank-reconciliation architecture;
- approve schema/API/domain expansion;
- approve migrations;
- grant NG-11 or later-stage authority;
- trigger an immediate cross-conversation transition;
- reopen NG-1 through NG-9;
- treat Claude as an authority.

Publication receipt: cff6a8e4b0f07e9d0e90372fb6e7014eeeed5104 on origin/operational-regression-gate; remote readback verified 2026-09-30T22:31:23Z

## DELTA-20260930-007

Status: PUBLISHED

Detected: 2026-10-01T00:03:27Z

Published: 2026-10-01T00:15:50Z

Sources:
- Canonical branch state `2c8ac0a05eb02653d7088d266ee6549ace81352f` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, inspected as predecessor evidence only (NG-10A).
- The bounded NG-10 research chain NG-10A and NG-10B under the Scope Lock `DECISION-20260930-007`, adjudicated by the Control Room and recorded as `DECISION-20260930-008`.

Prior claim: NG-10 is AUTHORIZED / OPEN IN RESEARCH under `DECISION-20260930-007`; Payment/Application/Holding-Control architecture and P0–P3 dispositions are unresolved.

Candidate/new claim: NG-10 is CLOSED under `DECISION-20260930-008`: a Payment is a distinct historical observed/reported cash-movement fact; Receivable and Payment remain distinct; Application is an independent historical one-Payment-to-one-Receivable association fact, with zero or more Applications per Payment and per Receivable; partial Application is distinct from partial Receivable crystallization; current balances do not replace Application history; Application reversal does not reverse cash; refund/recovery involving cash movement is later historical cash truth; unapplied amount does not establish beneficial ownership or unrestricted availability; source-defined Funds Holding / Control semantics are required where beneficial entitlement or control differs from physical receipt, without a universal trust-account entity; payer, source-of-funds, recipient/conduit and beneficiary roles remain distinct where known; payment dates are source-specific where material; deductions/withholdings are not universally cash Applications; Payment and Receivable currencies remain distinct with conversion provenance preserved where applicable; Ledger/accounting remains downstream; P0 rejected, P1 accepted with refinement, P2 accepted with refinement, P3 rejected; implementation remains unselected.

Claimed transition: NG-10 AUTHORIZED / OPEN IN RESEARCH → NG-10 CLOSED.

Scope: Canonical publication of the Control Room's terminal NG-10 architecture adjudication only. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable repository facts:
  - canonical HEAD `2c8ac0a05eb02653d7088d266ee6549ace81352f` (`Publish DELTA-20260930-006`), with `DECISION-20260930-007` and `DECISION-20260930-003` ACTIVE and `DELTA-20260930-006` PUBLISHED;
  - `CURRENT-STATE.md` there records NG-10 as AUTHORIZED under `DECISION-20260930-007` and NG-11 as unauthorized.
- Build-97 predecessor evidence (NG-10A, product commit `b3f41bf`): `src/AgencyOS.Domain/Finance/Payment.cs`, `Receivable.cs`, `Ledger.cs`; `src/AgencyOS.Finance.Rules/Allocation.fs`; `src/AgencyOS.Application/Finance/PaymentCommands.cs`, `FinanceCommands.cs`, `LedgerPosting.cs`; `src/AgencyOS.Infrastructure/Persistence/Configurations/M9Configurations.cs`, the finance migration and `Persistence/Queries/FinanceQueries.cs`; the finance API contracts and client projections.
- External domain evidence (NG-10B, exactly two verticals), including the load-bearing sources: the 2023 WGA Theatrical and Television Basic Agreement; the UK Conduct of Employment Agencies and Employment Businesses Regulations 2003, regulation 25; the AFM Booking Agent Agreement; California Labor Code §1700.25; bounded guild/union payment and holding materials.
- Control Room semantic adjudication: the Adjudication field below, recorded as `DECISION-20260930-008`.

Conflicts: None that survive adjudication. The complete material correction chain is preserved in `DECISION-20260930-008` §W:
- NG-10A: Build-97 finance query truth is in Infrastructure/Persistence/Queries/FinanceQueries.cs; positive Build-97 Unapplied is direction-blind; Payment reversal and Application reversal are distinct mechanisms; PaymentAdjustment is a Receivable-side deduction, not Payment correction; Application reversal is not proved to restore every downstream consequence, including commission-side consequences;
- NG-10B / NG-10C: unapplied arithmetic does not establish beneficial ownership, control, refundability or restriction; conduit receipt and beneficiary receipt may be different events; where ownership/control/release differs from possession a Funds Holding / Control distinction is required; refund/recovery of money that actually moved is a later cash-movement fact; payment date is not one universal timestamp; Application may be rule-governed and challengeable; held-funds semantics create no universal TrustAccount, Escrow or persisted HeldFunds entity; a holding/control entitlement transition may occur without another cash movement and must not fabricate a Payment.

Authority required:
- `CONTROL_ROOM`, for the terminal NG-10 semantic adjudication;
- machine-verifiable fact, for repository and publication facts;
- no new Owner decision is required to close NG-10. The Owner's existing authorization `DECISION-20260930-007` permitted the bounded research/design stage, and no post-NG-10 stage is authorized by this closure.

Adjudication: The Control Room independently reviewed the NG-10A Build-97 predecessor baseline and the NG-10B two-vertical falsification evidence, independently verified the load-bearing primary sources, applied the corrections recorded under Conflicts, and ACCEPTS the conceptual semantic architecture recorded as `DECISION-20260930-008`: P0 rejected; P1 accepted with refinement; P2 accepted with refinement; P3 rejected; no Owner-reserved ambiguity remains; no NG-10B2 is required. This becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260930-007
  Semantic basis: b96e3de55ffd6895a8197c68e49ab9b8caac4050
  CURRENT-STATE.next.md blob SHA-256: 3da661f5ab17f7e740d9946f4bd7992582a4b555f397d44361ce416b1530f608
  PUBLICATION-PAYLOAD.json blob SHA-256: 4c642597890bd42004c30d8e42a92e10b2cd5891bfaeb3f2a70cb606feaea484
  Authorized: 2026-10-01T00:09:01Z
  Reference: Adjudication of DELTA-20260930-007
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260930-008` is added as the durable NG-10 architecture decision.
- `CURRENT-STATE.md` records NG-10 Payment, Cash Application & Funds Provenance Architecture as CLOSED under `DECISION-20260930-008`.
- The open question on Invoice link and Payment/Allocation mechanics is narrowed to its implementation-open mechanics.
- The next bounded action becomes obtaining explicit Owner authorization before beginning any post-NG-10 stage, including NG-11.
- No implementation authority follows.

Supersedes: The current-state claim that NG-10 is open in research with unresolved Payment/Application/Holding-Control architecture. It supersedes no prior Decision ID.

Unchanged:
- Operational Closure remains COMPLETE.
- Self-Update V1 remains COMPLETE.
- NG-0 through NG-9 remain unchanged.
- `DECISION-20260930-007` remains the Owner authorization/Scope Lock that permitted NG-10.
- `DECISION-20260930-003` remains the recursive correspondence contract.
- Build 97 released product identity and all implementation behavior.
- No product code change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- NG-11 remains unauthorized and not begun.
- The Owner's future conversation-transition instruction is not triggered by this publication.

Open / unresolved questions:
- The implementation-open items and the evidence-triggered reconsideration case listed as Open in `DECISION-20260930-008` remain open.
- None of these blocks NG-10 closure.

Forbidden implications: This delta does **not**:
- permit implementation;
- choose a persistence mechanism;
- approve schema/API/domain expansion;
- approve migrations;
- select a payment-rail implementation;
- select a bank-reconciliation design;
- impose a universal trust account;
- impose a universal Transaction or MoneyMovement;
- impose a universal payment lifecycle;
- select an FX engine;
- grant NG-11 or later-stage authority;
- trigger an immediate cross-conversation transition;
- reopen NG-1 through NG-9;
- make Claude an architecture authority.

Publication receipt: 4c779ac1f4fa14e7949630699eb9d57027271a33 on origin/operational-regression-gate; remote readback verified 2026-10-01T00:15:50Z

## DELTA-20260930-008

Status: PUBLISHED

Detected: 2026-10-01T00:49:50Z

Published: 2026-10-01T01:08:06Z

Sources:
- Explicit Owner decision recorded as `DECISION-20260930-009`.
- Canonical branch state `df11edc6695219edb14af52ec072072ccb131029` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.

Prior claim: NG-10 is CLOSED under `DECISION-20260930-008`. No post-NG-10 stage, including NG-11, is authorized.

Candidate/new claim: NG-10 remains CLOSED under `DECISION-20260930-008`. NG-11 — Distribution Obligation, Setoff & Outbound Settlement Architecture is OWNER-AUTHORIZED and SCOPE-LOCKED for research/design only under `DECISION-20260930-009`; NG-11 research has not begun; its substantive research budget is 2 prompts.

Claimed transition: NO POST-NG-10 STAGE AUTHORIZED → NG-11 OWNER-AUTHORIZED / SCOPE-LOCKED FOR RESEARCH/DESIGN ONLY.

Scope: Governance authorization and Scope Lock publication only. This transition performs no NG-11 research and makes no architecture conclusion. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable canonical repository state: HEAD `df11edc6695219edb14af52ec072072ccb131029` (`Publish DELTA-20260930-007`); `CURRENT-STATE.md` there records NG-10 as CLOSED under `DECISION-20260930-008` and requires explicit Owner authorization before any post-NG-10 stage, including NG-11; no NG-11 authorization exists.
- Explicit Owner decision: recorded, with the complete Control Room NG-11 stage definition the Owner approved, as `DECISION-20260930-009`. The stage was fully defined before the identifier "NG-11" was assigned to it.
- `DECISION-20260930-003` remains the active recursive correspondence contract.
- No product-code, schema, API, domain or migration change is part of this transition.

Conflicts: None.

Authority required:
- `OWNER`, for the NG-11 authorization and its exact Scope Lock;
- `CONTROL_ROOM`, for faithful normalization and publication planning;
- machine-verifiable fact, for repository and publication facts.

Adjudication: The Owner explicitly approved the Control Room's complete NG-11 stage definition — bounded question, in-scope and out-of-scope items, closed distinctions, hypotheses D0–D3 with none accepted, the two-vertical research boundary, substages NG-11A–D, exit criteria, prompt budget and authority boundary — for research/design only. The Control Room records that approval without widening it. It becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: OWNER
  Delta: DELTA-20260930-008
  Semantic basis: c1e2256fbf9820ac2e27b090d61c0b1d9dc8705e
  CURRENT-STATE.next.md blob SHA-256: 89c160123705d7f36f83afce58afa1691df8cc15229f7a5007a7bd76a276e094
  PUBLICATION-PAYLOAD.json blob SHA-256: 3687b388d20a6ddffae06f37d809091bea37a3e45c24e3a1944248fb3bf992a6
  Authorized: 2026-10-01T01:04:28Z
  Reference: Adjudication of DELTA-20260930-008
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260930-009` becomes the durable Owner authorization and exact Scope Lock for NG-11.
- `CURRENT-STATE.md` records NG-11 Distribution Obligation, Setoff & Outbound Settlement Architecture as AUTHORIZED under `DECISION-20260930-009`, with substantive research not yet begun.
- The exact next bounded action becomes NG-11A — the Build-97 outbound-money predecessor baseline.
- No implementation authority follows.

Supersedes: The current-state claim that no post-NG-10 stage, including NG-11, is authorized. It supersedes no Decision ID.

Unchanged:
- Operational Closure remains COMPLETE.
- Self-Update V1 remains COMPLETE.
- NG-0 through NG-10 remain closed/approved exactly under their existing terminal decisions.
- The full material NG-10 correction chain in `DECISION-20260930-008` §W remains intact.
- Existing implementation-open questions from NG-6 through NG-10 remain implementation-open.
- `DECISION-20260930-003` remains the active recursive correspondence contract.
- Build 97 remains the released product identity.
- No product/schema/API/domain behavior changes.
- No migrations.
- NG-12 remains unauthorized.

Open / unresolved questions:
- NG-11A and NG-11B evidence.
- The terminal D0–D3 dispositions and NG-11 semantic architecture.
- All implementation mechanics.
- NG-12 and all later stages.

Forbidden implications: This delta does **not**:
- claim that NG-11A research has begun;
- select a D0–D3 winner;
- decide the distribution-obligation architecture;
- permit implementation;
- choose a persistence mechanism;
- approve schema/API/domain expansion;
- approve migrations;
- select a Ledger architecture;
- impose a universal Payable or Settlement;
- grant NG-12 or later-stage authority;
- reopen NG-1 through NG-10;
- treat Claude as an authority.

Publication receipt: b3deb88b076ec0636da47b4284c7859af3df71ed on origin/operational-regression-gate; remote readback verified 2026-10-01T01:08:06Z

## DELTA-20260930-009

Status: PUBLISHED

Detected: 2026-10-01T02:15:13Z

Published: 2026-10-01T02:28:16Z

Sources:
- Canonical branch state `056c8e40000dec56db8af74d5665f97e3f108d90` on `operational-regression-gate`.
- `docs/control-room/CURRENT-STATE.md`, `DECISIONS.md` and `CANONICAL-DELTAS.md` at that SHA.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, inspected as predecessor evidence only (NG-11A).
- The bounded NG-11 research chain NG-11A and NG-11B under the Scope Lock `DECISION-20260930-009`, adjudicated by the Control Room and recorded as `DECISION-20260930-010`.

Prior claim: NG-11 is OWNER-AUTHORIZED and SCOPE-LOCKED for research/design only under `DECISION-20260930-009`; D0–D3 unresolved.

Candidate/new claim: NG-11 is CLOSED under terminal Control Room decision `DECISION-20260930-010` with D0 rejected, D1 rejected, D2 accepted with refinement, D3 rejected: a source-defined Distribution Obligation is distinct from Payment, Receivable, Application, Funds Holding / Control and Ledger/accounting; outgoing cash reuses terminal NG-10 Payment; cash discharge requires a distinct obligation-specific historical Cash Discharge Link; source-defined non-cash reduction/satisfaction is preserved without fabricated Payment; no universal Payable, Settlement, Setoff, Transaction or MoneyMovement; Ledger remains downstream; implementation remains unselected; no implementation or NG-12 authority follows.

Claimed transition: NG-11 AUTHORIZED / OPEN IN RESEARCH → NG-11 CLOSED.

Scope: Canonical publication of the Control Room's terminal NG-11 architecture adjudication only. Product code changed: NO. Implementation, schema, API or domain changed: NO.

Evidence:
- Machine-verifiable repository facts:
  - canonical HEAD `056c8e40000dec56db8af74d5665f97e3f108d90` (`Publish DELTA-20260930-008`), with `DECISION-20260930-009` and `DECISION-20260930-003` ACTIVE and `DELTA-20260930-008` PUBLISHED;
  - `CURRENT-STATE.md` there records NG-11 as AUTHORIZED under `DECISION-20260930-009` with D0–D3 unresolved and NG-12 unauthorized.
- Build-97 predecessor evidence (NG-11A, product commit `b3f41bf`): `src/AgencyOS.Domain/Finance/Payment.cs`, `Commission.cs`, `Ledger.cs`; `src/AgencyOS.Finance.Rules/Commission.fs`; `src/AgencyOS.Application/Finance/PaymentCommands.cs`, `CommissionCommands.cs`, `LedgerPosting.cs`; `src/AgencyOS.Infrastructure/Persistence/Queries/FinanceQueries.cs`; `docs/adr/ADR-0023-finance-money-commission-and-the-ledger.md`.
- External domain evidence (NG-11B, exactly two verticals), including the Control-Room-verified load-bearing sources: California Labor Code §1700.25 (DLSE compilation Rev. 1/25); WGA Rider W (2021) §§3–4; the AFM Booking Agent Agreement; UK SI 2003/3319 Regulation 25 / Schedule 2, subject to the recorded temporal-currentness caveat.
- Control Room semantic adjudication: the Adjudication field below, recorded as `DECISION-20260930-010`.

Conflicts: None that survive adjudication. The complete material correction chain 1–19 is preserved in `DECISION-20260930-010` §O:
- NG-10 / inherited corrections 1–13 are preserved unchanged;
- NG-11A: ClientFundsPayable is also reversed by Receivable cancellation; PaymentMethod.Offset carries no proved distinct setoff semantic; neither Build-97 commission query projection nor ledger posting is universal domain truth;
- NG-11C: describing UK SI 2003/3319 simply as "current" was too strong, and its temporal-currentness limitation is recorded; a currently-due Distribution Obligation is a position, while creation, due/release transitions and discharge/correction are historical facts; a distinct non-cash discharge record is not required for every gross-to-net difference.

Authority required:
- `CONTROL_ROOM`, for the terminal NG-11 semantic adjudication;
- machine-verifiable fact, for repository and publication facts;
- no new Owner decision is required to close NG-11. The Owner's existing authorization `DECISION-20260930-009` permitted the bounded research/design stage, and no post-NG-11 stage is authorized by this closure.

Adjudication: The Control Room independently reviewed the NG-11A Build-97 predecessor baseline and the NG-11B two-vertical falsification evidence, independently verified the load-bearing primary sources, applied the corrections recorded under Conflicts, and ACCEPTS the conceptual semantic architecture recorded as `DECISION-20260930-010`: D0 rejected; D1 rejected; D2 accepted with refinement; D3 rejected; no Owner-reserved ambiguity remains; no NG-11B2 is required. This becomes canonical current state only when this delta reaches PUBLISHED through successful Publisher P7 remote readback.

Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-20260930-009
  Semantic basis: c9b1373173b879b1fbfd3a9b502455abbbe58803
  CURRENT-STATE.next.md blob SHA-256: 673db3a0da092ffc5f97935405a713699690d6b16b01c4131dc90fff05bba5b4
  PUBLICATION-PAYLOAD.json blob SHA-256: f87cc246489edc4566af5492fe1eff684e384dc8fec21d4d133de98ce2a87599
  Authorized: 2026-10-01T02:22:45Z
  Reference: Adjudication of DELTA-20260930-009
  Recorded by: Claude (AgencyOS executor)

What changes if accepted:
- `DECISION-20260930-010` is added as the durable NG-11 architecture decision.
- `CURRENT-STATE.md` records NG-11 Distribution Obligation, Setoff & Outbound Settlement Architecture as CLOSED under `DECISION-20260930-010`.
- NG-11 implementation-open mechanics are added to the open questions.
- The next bounded action becomes obtaining explicit Owner authorization before beginning any post-NG-11 stage, including NG-12.
- No implementation authority follows.

Supersedes: The current-state claim that NG-11 is open in research with unresolved Distribution Obligation architecture. It supersedes no prior Decision ID.

Unchanged:
- Operational Closure remains COMPLETE.
- Self-Update V1 remains COMPLETE.
- NG-0 through NG-10 terminal state remains unchanged.
- `DECISION-20260930-009` remains the Owner authorization/Scope Lock that permitted NG-11.
- `DECISION-20260930-003` governance remains unchanged.
- Build 97 released product identity and all implementation behavior.
- All implementation-open questions from NG-6 through NG-11 remain implementation-open.
- No product code change.
- No schema, API or domain expansion.
- No implementation.
- No migrations.
- NG-12 remains unauthorized and not begun.

Open / unresolved questions:
- The implementation-open items and evidence-triggered reconsideration cases listed as Open in `DECISION-20260930-010` remain open.
- None of these blocks NG-11 closure.

Forbidden implications: This delta does **not**:
- permit implementation;
- choose a persistence mechanism;
- approve schema/API/domain expansion;
- approve migrations;
- select a Ledger architecture;
- design a chart of accounts or journal entries;
- impose a universal Payable;
- impose a universal Setoff taxonomy;
- impose a universal Settlement;
- impose a universal TrustAccount;
- impose a universal Transaction or MoneyMovement;
- select an FX engine;
- grant NG-12 or later-stage authority;
- reopen NG-1 through NG-10;
- make Claude an architecture authority.

Publication receipt: 65bd27a2af37e2f1b1ba61f177a9a4b8fd413f84 on origin/operational-regression-gate; remote readback verified 2026-10-01T02:28:16Z
