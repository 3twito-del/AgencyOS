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

### Delta receipts and decision receipts

Decision receipts are governed by [`DECISIONS.md`](DECISIONS.md). A decision's receipt names that
decision's own first content-bearing publication commit. A delta's receipt names the final
publication-basis commit for the whole state transition. The two may legitimately name different
commits.

For `DELTA-20260928-001`:
- the four migrated decision entries first appeared in `388534de2920cd4fe25074efdfb37b26b12bda23`,
  so their receipts will name `388534d`, once a fresh remote readback of it is verified;
- the delta's receipt will name the pre-seal commit that adds this section. Its tree holds all four
  decisions, the delta, the final `CURRENT-STATE.md` candidate including the NG-4 authorization
  boundary, and this clarification. The receipt is not written until that commit has been pushed
  and verified by remote readback.

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
