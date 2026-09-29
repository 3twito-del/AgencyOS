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
