# AgencyOS canonical publisher contract

**Status:** NORMATIVE CONTRACT — IMPLEMENTED ([CANONICAL-PUBLISHER.md](CANONICAL-PUBLISHER.md)); FIXTURE-VALIDATED; REAL CANONICAL DOGFOOD VERIFIED

**Governed by:** [CANONICAL-STATE-PROTOCOL.md](CANONICAL-STATE-PROTOCOL.md). Where this contract
and the protocol disagree, the protocol wins, and this contract is corrected. The delta lifecycle
fields it relies on are defined in [CANONICAL-DELTAS.md](CANONICAL-DELTAS.md).

This contract specifies the **Canonical State Publisher** for Self-Update V1. The publisher takes
a transition that has already been adjudicated, stages it without disturbing the active state,
requires a durable seal authorization, promotes the staged bytes, and proves each step by remote
readback. It turns the procedure proved by hand in `DELTA-20260928-001` (section 12) into
deterministic steps, and adds the authority gate and staging that the steady state needs.

The publisher executes accepted state. **It never decides that state should be accepted, and it
never seals without a durable seal authorization bound to the exact bytes it promotes.**

The Canonical Publisher is implemented in `tools/AgencyOS.Canonical.Publisher`
([CANONICAL-PUBLISHER.md](CANONICAL-PUBLISHER.md)) and validated against disposable fixture
repositories. On the canonical branch, `DELTA-20260929-001` then exercised `stage`, a replacement
semantic basis, `authorize`, P5–P7 and an R7 `resume`. The final publication basis is
`5f88fbe1a046b8c5ee303dafb2a83e48a0965529`, and the seal commit is
`11935ff83f15d715ff9cee48c1514f43456fe0dd`. That real dogfood exposed hardening findings after
publication, which the current implementation addresses (CANONICAL-PUBLISHER.md section 8). It
gives the publisher no semantic authority. Self-Update V1 terminal acceptance is a separate
Control Room state decision. This document remains the normative rule the publisher implements.
The read-only Canonical Detector and its local Canonical Alert renderer are implemented separately
([CANONICAL-DETECTOR.md](CANONICAL-DETECTOR.md)). No watcher, scheduler, webhook, alert transport
or sender, or workflow store exists.

---

## 1. Position in the cycle

```
Detector ──► OBSERVED evidence packet ──► Canonical Alert ──► Control Room normalization / adjudication
                                                                          │
                                   protocol-complete candidate delta or decision, as applicable
                                                                          │
      ┌──────────────── Publisher Receipt ◄── Publisher ◄── Accepted Publication Payload
      ▼                                          ▲
 evidence for the next cycle          Seal authorization record (durable, section 5)
```

| Interface | Producer | Consumer | Carries | Never carries |
| --- | --- | --- | --- | --- |
| Observation evidence packet | Detector | Control Room, via the alert | Observed facts and evidence anchors in an `OBSERVED` packet, with a temporary observation ID and no permanent Delta ID. Control Room normalization allocates the permanent Delta ID and creates the protocol section 6 `PENDING_ADJUDICATION` candidate. | Any `PENDING_ADJUDICATION`, `ACCEPTED` or `PUBLISHED` state; a Delta ID |
| Canonical Alert | Detector | Control Room | The protocol section 7 alert shape | An adjudication (protocol section 7: an alert is never one) |
| Accepted Publication Payload | Control Room (Owner where required), executed by Claude | Publisher | Section 3 | Anything the adjudication did not cover |
| Seal authorization record | Control Room or Owner, recorded by Claude | Publisher | Section 5 | Any semantic change |
| Publisher Receipt | Publisher | Control Room; the next cycle | Section 11 | Semantic conclusions |

Only the detector and the publisher are automation. **The detector must never call the publisher,
and must never construct a payload with `ACCEPTED` state on its own authority.** A payload exists
only after the authority protocol section 3 requires has adjudicated.

Detector, alert delivery and transport are out of scope here. They are defined only by the
boundary above.

## 2. Staging: the active state stays true

**`docs/control-room/CURRENT-STATE.md` always holds the last `PUBLISHED` state.** A pre-seal basis
never replaces its contents with unpublished future state. From P1 until the seal is pushed and
read back, it stays byte-for-byte what the last seal left, with one exception: a descriptive
correction unrelated to the candidate, adjudicated separately. Such a correction invalidates the
pending basis (section 4).

An accepted, unpublished transition is staged under:

```
docs/control-room/pending/<DELTA-ID>/
    CURRENT-STATE.next.md
    PUBLICATION-PAYLOAD.json
```

- **`CURRENT-STATE.next.md`** holds the exact bytes that will replace `CURRENT-STATE.md` at the
  seal: the complete intended current state. It may contain only values that are fixed before
  staging. In particular, its *Latest published delta* section names the Delta ID and points to
  `CANONICAL-DELTAS.md` for the receipt. It never contains a basis SHA or a readback time, because
  those do not exist yet.
- **`PUBLICATION-PAYLOAD.json`** holds the Accepted Publication Payload (section 3).

**Everything under `docs/control-room/pending/` is non-authoritative.** It is not a current-state
source under protocol section 4, which does not list it, and nothing in it is current state. A
pending transition is discoverable through three things: its `ACCEPTED` delta, its staging
directory, and the delta's lifecycle metadata. A fresh Control Room reading `CURRENT-STATE.md`
while a publication is held gets the correct active answer.

**One pending transition at a time.** In V1, at most one directory exists under `pending/`. A
run for a different transition stops at P0 while one is pending.

**Digests.** Every SHA-256 in this contract is computed over the committed Git blob, as read by
`git cat-file blob <commit>:<path>`. It is never computed over a working-tree file, which line-end
conversion can alter. The remote readback compares the same blob bytes.

## 3. Accepted Publication Payload

The payload is the publisher's only semantic input. Every field is explicit. **The publisher never
infers a field from convenience, from the repository's current content, or from another field.**
A missing or ambiguous field is `PRECONDITION_DRIFT`. A missing authority reference is
`AUTHORITY_MISSING`.

### 3.1 Serialisation

`PUBLICATION-PAYLOAD.json` is stored exactly as the **JSON Canonicalization Scheme** (RFC 8785)
output of the payload object:
- UTF-8 without a byte-order mark;
- object keys sorted as RFC 8785 specifies;
- no insignificant whitespace;
- no trailing newline.

Values are strings, booleans, arrays and objects only. There are no numbers and no `null`; an
absent value is an empty array or the string `"None"`. The publisher re-canonicalises the parsed
file and stops (`PRECONDITION_DRIFT`) if the stored bytes differ. The payload's digest is therefore
stable, and a seal authorization record can bind it.

The payload cannot contain its own digest, and it does not.

### 3.2 Fields

| Key | Content |
| --- | --- |
| `contract` | `"agencyos-canonical-publisher/v1.1"` (see *Version* below) |
| `mode` | `"PUBLISH_NEW_DELTA"`, `"ADVANCE_EXISTING_DELTA"` or `"DESCRIPTIVE_CORRECTION"` |
| `delta_id` | `"DELTA-YYYYMMDD-NNN"`, or `"None"` in `DESCRIPTIVE_CORRECTION` |
| `branch` | The canonical branch, for example `"operational-regression-gate"` |
| `expected_start_sha` | The full SHA the remote must be at when this semantic basis is built. For a replacement basis (section 4) this is the head it is built on. |
| `authority.classes` | Any of `"MACHINE_VERIFIABLE_FACT"`, `"CONTROL_ROOM"`, `"OWNER"` (protocol section 3) |
| `authority.adjudication_references` | Durable references, in the forms defined in section 5.2 |
| `authority.owner_decision_applies` | `true` or `false`. `true` requires `OWNER` Decision IDs in `authority.governing_decision_ids`. |
| `authority.governing_decision_ids` | The Decision IDs the transition relies on |
| `current_state.prior_sha256` | Digest of the active `CURRENT-STATE.md` at `expected_start_sha` |
| `current_state.next_sha256` | Digest of `CURRENT-STATE.next.md`. In `DESCRIPTIVE_CORRECTION`, the digest of the corrected `CURRENT-STATE.md`, which equals `prior_sha256` when that file is not corrected. |
| `current_state.next_text` | The complete, exact text that becomes `CURRENT-STATE.next.md`: UTF-8, LF line endings, ending in exactly one LF. Its committed blob's SHA-256 must equal `next_sha256`. The publisher never synthesises it from the claims. `"None"` in `DESCRIPTIVE_CORRECTION`. |
| `current_state.changes` | Each `{ "section", "claim" }` that changes, with the claim's full new text. `section` is the exact text after a `## ` heading, or `""` for the text before the first heading. The claim must appear within that section. |
| `current_state.unchanged` | Each `{ "section", "claim" }` that must survive byte-for-byte, in the same section of both the active and the staged state. At minimum this is the release identity and every claim the transition does not name. The two claims lists are validation assertions, independent of `next_text`. |
| `delta.kind` | `"new"` or `"existing"` |
| `delta.substantive_sha256` | The delta's publication-invariant digest (section 5.4) |
| `delta.entry_text` | The complete, exact delta entry for the semantic basis, ending in exactly one LF. It runs from the `## DELTA-…` heading to the last line, and is in its pre-seal lifecycle state: `Status: ACCEPTED`, a populated `Adjudication`, `Seal authorizations: Pending` or the earlier records preserved, `Published: Pending`, `Publication receipt: Pending`. The publisher inserts or replaces exactly this entry and never writes delta prose. Its digest must equal `substantive_sha256`. `"None"` in `DESCRIPTIVE_CORRECTION`. |
| `decisions.new_entries` | Each `{ "id", "entry_text" }`: a decision added in the semantic basis, with its complete, exact entry text (`Status: ACTIVE`, receipt `Pending`). The publisher inserts exactly these bytes and never writes decision prose from an ID. |
| `decisions.status_changes` | Each `{ "id", "status" }` earlier-entry change (`SUPERSEDED by …` / `REVOKED by …`), applied at the seal |
| `decisions.receipts_to_seal` | Each `{ "id", "content_bearing" }`, where `content_bearing` is a full SHA or `"SEMANTIC_BASIS"` for a decision first added in this transition |
| `provenance_prose` | Each `{ "path", "before", "after" }`: an exact prose replacement allowed at the seal, outside any entry, or empty |
| `corrections` | For `DESCRIPTIVE_CORRECTION`: `[{ "path", "before", "after" }]`, exact replacements the Control Room classified as descriptive. Each `before` must occur exactly once in its file, or the run stops with `PRECONDITION_DRIFT`; there is no fuzzy matching and no prose synthesis. A correction may not change a delta or decision entry, lifecycle metadata, or anything under `pending/`. An empty array in every other mode. It is distinct from `provenance_prose`, which applies only at the seal of a semantic transition. |
| `paths.allowed` | The exact paths the transition may change |
| `paths.forbidden` | Always includes the V1.1 hard-forbidden set: `src/`, `tests/`, `.github/`, `scripts/`, the protocol, the closure state, `docs/releases/` and `docs/adr/`. Publisher V1.1 never changes these, whatever the payload's authority: neither `CONTROL_ROOM` nor `OWNER` overrides them, and there is no exception field. A transition that genuinely needs one stops with `SCOPE_VIOLATION`, or `PRODUCT_BOUNDARY_VIOLATION` for `src/`. It needs a separately governed publication or a future contract revision. This is a V1 safety boundary, not a claim that such changes can never be authorized. |
| `flags.product_code_change` | Always `false` under this contract. `true` is `PRODUCT_BOUNDARY_VIOLATION`. |
| `flags.schema_change`, `flags.api_change`, `flags.domain_expansion` | `true` requires `authority.owner_decision_applies: true` |

**The publisher rejects (`AUTHORITY_MISSING`) any payload that claims a semantic transition
requiring `CONTROL_ROOM` or `OWNER` authority without a durable reference.** A payload whose only
authority is `MACHINE_VERIFIABLE_FACT` may publish only machine-verifiable facts (protocol section
3.1).

**Self-materialising.** Every byte of semantic text the publisher writes comes verbatim from one
of these fields:
- `current_state.next_text`;
- `delta.entry_text`;
- `decisions.new_entries`;
- `provenance_prose`;
- `corrections`.

The publisher derives only hashes, commit SHAs, readback times, the next `SA-n` record number and
the paths this contract fixes. A new entry is appended at the end of its ledger, after one blank
line. An existing entry is replaced in place.

**Version.** `v1.1` adds the exact-text fields above and removes `stop_after`. `v1` carried IDs
and digests but not the bytes needed to materialise them, and was never implemented. Where a run
stops is now fixed by the command (section 6), not by the payload. A payload that names any other
version is refused as `PRECONDITION_DRIFT`; it is never read as this one. So is a payload with any
member this table does not list, `stop_after` included: unknown members are refused, never
ignored.

## 4. Publication bases

A transition is published from two kinds of commit.

- **`SEMANTIC_BASIS`**: a remote-verified commit that stages the accepted transition before any
  seal authorization for it. Its tree contains:
  - the `ACCEPTED` delta, with its `Adjudication` populated;
  - the applicable decision records, with receipts `Pending`;
  - `pending/<DELTA-ID>/PUBLICATION-PAYLOAD.json`;
  - `pending/<DELTA-ID>/CURRENT-STATE.next.md`;
  - the still-active, previously published `CURRENT-STATE.md`, unchanged.

  On the first semantic basis, the delta reads `Seal authorizations: Pending`.
- **`AUTHORIZATION_COMMIT`**: the direct child of a `SEMANTIC_BASIS`. It appends exactly one seal
  authorization record that binds that basis (section 5), and changes nothing else.

**The final publication basis is the latest valid `AUTHORIZATION_COMMIT`.** Its tree is the
complete publication basis: everything in its semantic basis, plus the durable authorization
history. Nothing in any basis claims that unpublished state is current.

This is the Self-Update V1 form of the `CANONICAL-DELTAS.md` definition of a publication basis,
which the ledger adopts for staged transitions.
- The delta's `Publication receipt` names the final publication basis.
- `Published` records the time of its readback (P4A).
- A decision's receipt names its own content-bearing commit: the first semantic basis that
  contains it, or an earlier commit. The time is the same P4A readback.

### Invalidation and replacement

An authorization record stops binding when any of the following happens before the seal:
- a different semantic basis replaces the one it names;
- staged bytes, or either bound digest, change;
- a separately adjudicated descriptive correction changes the branch's ancestry during the hold;
- the Control Room requires a pre-seal correction;
- the remote moves, so that a new verified basis is needed.

The record stays in the ledger permanently. Nothing is reset or overwritten.

A **replacement semantic basis** is built on top of the current remote head, never by rewriting
history. It is a new commit that:
- regenerates the staged files as required, with `expected_start_sha` set to that head and
  `current_state.prior_sha256` set to the active `CURRENT-STATE.md` there;
- keeps the delta's existing `Seal authorizations` history unchanged;
- is pushed and remote-verified (P3–P4) before a new record may be appended in a new
  `AUTHORIZATION_COMMIT` (P4A).

The latest valid authorization commit then becomes the publication basis. An older record never
authorizes a newer basis.

### Semantic identity across bases

**Re-authorization is permitted only while the same accepted semantic delta is being published.**
A replacement basis may correct publication mechanics, the staging representation, descriptive
prose, or another non-substantive publication issue.

The publisher checks mechanically that these are identical to the previous semantic basis:
- the delta's `delta.substantive_sha256`;
- the byte content of every entry in `decisions.new_entries`.

If either differs, it stops with `SEMANTIC_CHANGE_REQUIRES_NEW_DELTA`. That covers any change to the
claim, scope, evidence, authority requirement, adjudication, open questions or forbidden
implications. **Such a change is not a re-authorization.** It needs a new correction delta under
the protocol's correction-chain rules. The append-only authorization history can never be used
to change an accepted semantic decision.

## 5. Seal authorizations

**No seal (P5) happens without a seal authorization record that is recorded in the repository,
pushed, read back from the remote, and bound to the exact semantic basis being promoted.** An
instruction that exists only in a conversation is not one. The publisher verifies correspondence.
**It does not judge whether the authority decided rightly.**

### 5.1 Append-only history

`Seal authorizations` is a delta lifecycle field ([CANONICAL-DELTAS.md](CANONICAL-DELTAS.md)).
It is append-only history:

```
Pending → first immutable record → further immutable records appended as required
```

- It starts as `Seal authorizations: Pending` on the first semantic basis.
- The first authorization replaces `Pending` with a collection holding one record. It never
  returns to `Pending`.
- Each later authorization appends one new record. No record is ever edited or deleted.
- Every record survives publication.

### 5.2 Record syntax

A populated field is written with the header at column 0 and every record line indented, so that
no record line can be mistaken for a field (section 5.4):

```
Seal authorizations:
- Record: SA-1
  Scope: AUTHORIZE_SEAL_ONLY
  Authority: CONTROL_ROOM
  Delta: DELTA-YYYYMMDD-NNN
  Semantic basis: <full SHA>
  CURRENT-STATE.next.md blob SHA-256: <digest>
  PUBLICATION-PAYLOAD.json blob SHA-256: <digest>
  Authorized: <UTC when the authority gave it>
  Reference: <durable reference>
  Recorded by: <who wrote this record; not the authority>
```

- `Record` is `SA-1`, `SA-2`, … within the delta, increasing by one per record and never reused.
- `Scope` is always `AUTHORIZE_SEAL_ONLY`. The record lets the publisher promote exactly the
  bound bytes, and nothing else.
- `Authority` is `OWNER` when the governed transition requires Owner authority, meaning
  `authority.owner_decision_applies` or an `OWNER` class. Otherwise it is `CONTROL_ROOM`. That
  includes a transition accepted as `MACHINE_VERIFIABLE_FACT` only: every semantic-state promotion
  requires a durable seal authorization, and none bypasses the gate. The record permits execution
  of bytes already accepted. It is not a new semantic adjudication, and the publisher never
  creates one.
- `Reference` is durable and takes one of three forms:
  - a Decision ID present in `DECISIONS.md`;
  - `<repository path>@<full SHA>`, resolving in the repository;
  - `Adjudication of <DELTA-ID>`, meaning the delta's own populated `Adjudication` field.

  A chat transcript is not a durable reference.
- A record carries no confidence value.

### 5.3 Binding rule

A record permits P5 only if all of these hold mechanically:
1. it names the current Delta ID;
2. its `Authority` is a class the transition requires;
3. its `Semantic basis` is exactly the parent of the final `AUTHORIZATION_COMMIT`;
4. that semantic basis passed P4;
5. its `CURRENT-STATE.next.md` digest equals the committed blob staged for promotion;
6. its payload digest equals the committed canonical payload blob;
7. its `Reference` exists in one of the section 5.2 forms;
8. no later commit has invalidated that basis: the remote head is still the authorization
   commit.

Only the last record can bind. Every earlier record names an earlier basis. Any failure is
`SEAL_AUTHORIZATION_INVALID`.

No self-reference arises:
- a record names the earlier, already-verified semantic basis;
- the authorization commit adds only that record;
- the delta's receipt later names the authorization commit, from the seal commit, which does not
  name itself.

### 5.4 Entry parsing and the publication-invariant digest

`delta.substantive_sha256` must not change when authorization records are appended, or when the
seal advances lifecycle fields. It is computed from a deterministic parse:

1. **Entry.** Entries are parsed only after the ledger's `# Entries` heading, so the template is
   never read as an entry. A delta entry starts at its `## DELTA-…` heading. It ends before the
   next line that begins with `#`, before the next line that is exactly `---`, or at the end of
   the file. The trailing blank lines immediately before that structural boundary are ledger
   separators and belong to no entry, so the entry ends at its last non-blank line. Blank lines
   between non-blank lines inside the entry stay part of their field blocks, and no whitespace
   inside retained content is normalised. Appending an entry therefore never changes the digest
   of the one before it. The heading line is part of the entry and lies in no excluded block, so
   it is included in the digest.
2. **Field header.** A line is a field header only if it starts at column 0 with one of the
   ledger's field names, followed by `:`. The field names are the substantive and lifecycle
   fields listed in `CANONICAL-DELTAS.md`, in the spellings its template and entries use
   (`Open` and `Open / unresolved questions` are both field names). The list is closed, and
   adding a name is a ledger change.
3. **Field block.** A field block is its header line plus every following line up to, and not
   including, the next field header or the end of the entry. Blank lines and indented lines
   belong to the block above them.
4. **Excluded blocks.** The blocks excluded from the digest are the four fields that move during
   publication: `Status`, `Seal authorizations`, `Published` and `Publication receipt`. This
   covers the whole `Seal authorizations` block: its header, every record, and every nested line.
5. **Digest.** The digest is SHA-256 over the retained lines in order, each terminated by LF, as
   UTF-8.

`Adjudication` is lifecycle metadata in the ledger. In a staged V1 transition, however, it is
populated before the first semantic basis and never changes afterwards, so it is retained in the
digest and protected by the semantic-identity check.

**Invariant:** appending a record, or sealing, never changes `delta.substantive_sha256`. P2, P4A
and P5 each re-check it.

## 6. Phases

Each phase passes or stops. **A stop is never repaired by inference.**

| Phase | Action | Passes when |
| --- | --- | --- |
| **P0 Pre-flight** | Fetch; check the repository, branch, heads, tree; parse the artifacts and the payload. | The branch is `branch`; the remote and local HEAD are exactly `expected_start_sha`; the tree is clean; no `pending/` directory exists for a different Delta ID; the artifacts parse (section 5.4; every entry has every field; IDs are unique); the payload is canonical and complete; `current_state.prior_sha256` matches; the mutation lies inside `paths.allowed`; the delta's lifecycle state permits the transition. |
| **P1 Stage** | Write the semantic basis tree (section 4). | The active `CURRENT-STATE.md` is unchanged; the staged files match the payload; nothing claims `PUBLISHED`; the delta is `ACCEPTED`, and its `Seal authorizations` is `Pending` or holds only earlier records. |
| **P2 Validate** | Check the staged tree (section 7.1). | Every check passes. |
| **P3 Publish semantic basis** | Commit once; normal fast-forward push. | The push is accepted without force. |
| **P4 Semantic basis readback** | Fetch; read the artifacts and the staging files from the remote. | The remote head is the semantic basis; the remote blobs equal the validated tree; the payload's digests match. |
| *Hold* | Stop with `BASIS_VERIFIED_AWAITING_SEAL`. | This is always reached, because a record names the semantic-basis SHA and cannot exist before P4. |
| **P4A Append seal authorization** | On a durable authorization for this basis: append one record (section 5.2); change nothing else; commit; normal push; read back from the remote. | On the remote: the diff from the semantic basis adds exactly one record at the end of the delta's `Seal authorizations` block; the digest is unchanged; the binding rule passes. This commit is the **final publication basis**. Record `basis_readback_utc` when the readback checks complete. |
| **P5 Seal** | Promote exactly the whitelist in section 7.2. | The binding rule is re-checked and passes; `CURRENT-STATE.md` is replaced by bytes equal to the bound `CURRENT-STATE.next.md`; nothing outside the whitelist differs. |
| **P6 Publish seal** | Fetch; confirm the remote is still the final basis; commit once; normal fast-forward push. | The remote was the final basis just before the push, and the push was accepted without force. |
| **P7 Final readback** | Fetch; read every changed path from the remote. | The remote head is the sealing SHA; the remote blobs equal the sealed tree; `pending/<DELTA-ID>/` is absent. Record `seal_readback_utc`. **Only now is the transition `PUBLISHED`** (protocol section 8). |

**Command boundaries.** A seal authorization names the semantic-basis SHA, which exists only
after P4. So where a run stops is fixed by the command:
- **`stage`** runs P0 → P1 → P2 → P3 → P4 and always stops at the mandatory hold, with
  `BASIS_VERIFIED_AWAITING_SEAL`. It never enters P4A.
- **`authorize`** takes an explicit, durable seal authorization bound to the verified semantic
  basis, and runs or resumes P4A → P5 → P6 → P7 unless a failure stops it. It starts only from a
  state compatible with R2, R3 or R4. There is no V1 rest state between a successful P4A and P5.
- **`resume`** derives R0–R7 from repository evidence and continues only along the permitted
  route. It never infers an authorization.
- **`correct`** runs the descriptive-correction path (section 7.3).

**P5 is never entered before P4A has passed.**

**Pre-seal corrections** are made through a replacement semantic basis (section 4). A local commit
that was never pushed may be amended, but only on authorization. A pushed commit is never amended.

**No history rewriting.** No phase force-pushes, rebases, amends a pushed commit, merges, tags,
or touches `master`.

## 7. Checks

### 7.1 Semantic-basis validation (P2)

Each is machine-checkable:

1. **Scope:** the changed paths are a subset of `paths.allowed` plus
   `docs/control-room/pending/<DELTA-ID>/`, and disjoint from `paths.forbidden`; there is no
   product path.
2. **Active state untouched:** `CURRENT-STATE.md` has the digest `current_state.prior_sha256`.
3. **Staged state:**
   - `CURRENT-STATE.next.md` has the digest `current_state.next_sha256`;
   - every `current_state.unchanged` claim appears in it byte-for-byte as in the prior state;
   - every `current_state.changes` claim appears exactly as specified;
   - every Delta ID and Decision ID it cites exists;
   - it contains no basis SHA or readback time.
4. **Delta identity:** the entry's publication-invariant digest (section 5.4) equals
   `delta.substantive_sha256`. For a replacement basis, it also equals the previous basis's value.
   Otherwise the failure is `SEMANTIC_CHANGE_REQUIRES_NEW_DELTA`.
5. **Other entries:** every other delta entry is byte-identical to `expected_start_sha`.
6. **Decision immutability:** every earlier decision entry is byte-identical. New entries are
   exactly those in `decisions.new_entries`. On a replacement basis they are byte-identical to the
   previous basis; otherwise the failure is `SEMANTIC_CHANGE_REQUIRES_NEW_DELTA`.
7. **Authorization history:** existing records are byte-identical to the previous basis, and none
   is added in a semantic basis.
8. **Correction chain:** every `Supersedes:` names an entry that exists. Every planned
   `decisions.status_changes` entry has a new entry that names it.
9. **Authority:** every class other than machine fact has a durable reference, and every cited
   Decision ID exists.
10. **Lifecycle:** the requested status changes are permitted forward transitions.
11. **Forbidden implications:** no text is introduced that the delta lists under `Forbidden
    implications`, by exact phrase. This is mechanical and narrow. It does not replace the
    adjudication.

### 7.2 Seal whitelist (P5)

The seal performs exactly this promotion:

1. replace `docs/control-room/CURRENT-STATE.md` with bytes equal to
   `pending/<DELTA-ID>/CURRENT-STATE.next.md` at the bound semantic basis, whose digest the
   binding record names;
2. in the delta:
   - advance `Status: ACCEPTED` → `PUBLISHED`;
   - set `Published` to `basis_readback_utc`;
   - set `Publication receipt` to `<final basis SHA> on origin/<branch>; remote readback verified
     <basis_readback_utc>`;
   - leave `Seal authorizations` exactly as it is;
3. for each `decisions.receipts_to_seal` entry, set the receipt to `<content-bearing SHA> on
   origin/<branch>; remote readback verified <basis_readback_utc>`, with `"SEMANTIC_BASIS"`
   resolved to the first semantic basis containing that decision; apply each
   `decisions.status_changes` entry;
4. delete `docs/control-room/pending/<DELTA-ID>/`;
5. apply each `provenance_prose` replacement exactly.

Any other byte difference, a digest that no longer matches the binding record, or a changed
`delta.substantive_sha256` is `SUBSTANTIVE_MUTATION_DURING_SEAL`. The run stops before
committing.

The promotion is **not** a new adjudication. It executes bytes the authority has already bound.
The sealing commit never names its own SHA. The staged files stay in history at every basis,
where the delta's receipt reaches them.

### 7.3 Descriptive correction

A **descriptive correction** changes wording so that it agrees with state that is already
published. It asserts no new claim and moves no lifecycle. Examples: tense, or naming commits that
already exist.

**Whether a change is descriptive is a semantic judgement, and the publisher does not make it.**
The Control Room classifies the change in the payload. The publisher then checks mechanically:
- no delta entry and no decision entry changed by a single byte;
- no lifecycle value changed, and no authorization record changed;
- no entry was added;
- nothing under `pending/` changed;
- the paths are inside `paths.allowed`.

Its authority is checked like a semantic payload's. Every `authority.adjudication_references`
entry must be one of the three durable forms of section 5.2 and must resolve at the pre-correction
head, by the same resolution semantic staging uses. So must every governing decision. A missing,
malformed or unresolved reference is `AUTHORITY_MISSING` in P0, before any write.

It runs P0 → P1 → P2 → P3 → P4 and ends there, with `CORRECTION_VERIFIED`. It uses no staging
and no seal. Because it has no Delta ID, it has no staging directory. Its payload is supplied to
the run and returned in the receipt. It is **not** a state transition and needs no delta (protocol
section 2). If a check fails, the change is not descriptive. If a transition is pending, the
correction invalidates its basis (section 4).

**The correction commit is the durable record** because its message carries the exact accepted
payload, and nothing else:

```
Apply a descriptive canonical correction

AgencyOS-Correction-Contract: agencyos-canonical-publisher/v1.1
AgencyOS-Correction-Payload-SHA256: <SHA-256 of the payload bytes>
AgencyOS-Correction-Payload-Base64: <standard padded Base64 of the exact payload bytes, one line>
```

- The Base64 value decodes byte for byte to the canonical payload given to the run. The bytes hash
  to the recorded SHA-256 and parse under the v1.1 parser as a `DESCRIPTIVE_CORRECTION`. So the
  classification, authority classes and adjudication references can be reconstructed from the
  repository alone.
- The publisher generates no semantic data for the record. The correction's file diff remains the
  evidence of what changed.
- In P3 the local commit object must reconstruct the payload before it is pushed. In P4, after
  the remote head is confirmed to be the correction commit and every corrected blob matches, the
  commit object at that remote head must reconstruct the exact payload. Otherwise the result is
  `REMOTE_BASIS_MISMATCH`, never `CORRECTION_VERIFIED`.
- This record is provenance, not workflow state: section 10's recovery truth is unchanged.

**Payload size bound.** The V1.1 `correct` command carries the exact canonical correction payload
in the commit's metadata. So this implementation accepts a `DESCRIPTIVE_CORRECTION` payload of at
most 18,000 canonical bytes, counted as the exact RFC 8785 payload bytes supplied to the run. An
18,000-byte payload is accepted and a larger one is `PRECONDITION_DRIFT`.
- The refusal happens in P0, after the authority checks and before any working-tree, index, commit
  or push mutation.
- The bound is an implementation transport and safety limit of the current record mechanism. It is
  not a Git format limit, not a governance rule and not semantic authority.
- It gives no permission to truncate, split or omit the accepted payload. The publisher never
  stores only part of a payload: it records the whole payload or refuses the correction.
- It applies only to `DESCRIPTIVE_CORRECTION`. Semantic publication payloads are staged as files
  under `pending/`, and this bound does not apply to them.
- A future publisher revision may change the transport mechanism or the bound only through an
  explicit change that aligns this contract and the implementation. Neither the v1.1 payload
  schema nor its contract identifier depends on it.

Corrections committed before this rule, such as `dfbd7ba0ef2fb7e02f6dfd94e4e4c0c56b98ee2e`, carry
no such record. Their provenance is recorded in CANONICAL-PUBLISHER.md section 8.

## 8. Machine-verifiable versus semantic

| The publisher MAY verify mechanically | The publisher MUST NOT decide |
| --- | --- |
| SHA ancestry and branch heads | That an architecture stage is closed |
| File paths and scope | That evidence is sufficient |
| Byte equality and blob digests | That a finding is proved |
| Tag dereferences | That an Owner ambiguity is resolved |
| CI and Nightly state | That an ADR should be reconsidered |
| Presence or absence of required fields | That a residual risk is acceptable |
| Permitted lifecycle transitions | That a product or domain expansion is authorized |
| That an authorization record exists and binds the basis | That a seal should be authorized |
| That the publication-invariant digest is unchanged | Whether a changed digest reflects a good correction |
| Remote readback equality | That a change is descriptive rather than semantic |
| That cited IDs exist | That a candidate should be `ACCEPTED` |

The right-hand column needs the authority in protocol section 3. The publisher checks that such
authority is **referenced and bound**. It never checks that it was **right**.

## 9. Failure semantics

Every failure stops the run. **No failure is retried automatically.** A retry is a new run.

| Failure | Raised in | Repository may already be mutated? | Delta state after | Evidence returned to Control Room |
| --- | --- | --- | --- | --- |
| `PRECONDITION_DRIFT` | P0; P2; any resume | No | Unchanged | Expected against actual branch, heads, tree, pending directories and digests; any unparseable artifact; or staged accepted text that contains an exact phrase its own delta lists under `Forbidden implications`. That is a mechanical self-contradiction, checked by exact match only. |
| `AUTHORITY_MISSING` | P0, P2 | No | Unchanged | The claimed class; the missing reference |
| `SEAL_AUTHORIZATION_INVALID` | P2, P4A, P5 | Possibly: a semantic basis, or an authorization commit, may be on the remote | `ACCEPTED`, not published; every record kept | The records; each binding check that failed; any intervening commits. At P2 it is raised when an existing record is edited, deleted, reordered or otherwise rewritten. Appending a record is legal only in P4A. |
| `SEMANTIC_CHANGE_REQUIRES_NEW_DELTA` | P2, on an initial or a replacement basis | Working tree only | `ACCEPTED`, not published; unchanged | The mutated entry and its first differing line. Raised when the delta's digest differs from the payload's or from the previous basis's; when an earlier, immutable delta or decision entry changes outside a permitted lifecycle operation; or when a new decision differs from the previous basis. |
| `SCOPE_VIOLATION` | P0, P2 | Working tree only; nothing committed or pushed | Unchanged | The offending paths |
| `PRODUCT_BOUNDARY_VIOLATION` | P0, P2 | Working tree only | Unchanged | The product paths or flags |
| `REMOTE_BASIS_MISMATCH` | P3, P4, P4A; any resume | Yes: a basis may be on the remote | `ACCEPTED`, not published | The remote head; a blob diff against the expected basis. It is also raised when the remote has moved past the semantic basis that a proposed authorization names, before that authorization has become the final basis. |
| `REMOTE_MOVED_BEFORE_SEAL` | P6; any resume | Yes: the bases are on the remote; the seal is local only | `ACCEPTED`, not published; the latest record no longer binds (section 4) | The new remote head and its commits since the final basis. Reserved for movement after the final authorization basis exists and before P6 pushes the seal. |
| `SUBSTANTIVE_MUTATION_DURING_SEAL` | P5 | Local only; the seal is not committed | `ACCEPTED`, not published | The byte diff outside section 7.2 |
| `FINAL_READBACK_MISMATCH` | P7 | Yes: the seal is on the remote | **Not `PUBLISHED`**, whatever the file says | The remote head; a blob diff against the sealed tree |

A local-only failure leaves the uncommitted changes in place and reports them, so the Control Room
sees exactly what was produced.

**A semantic conflict is never recovered silently.** This includes a payload that contradicts the
artifacts, an ID collision, a cited decision that is `SUPERSEDED`, or a replacement basis that
changes the accepted claim. It is reported under the matching class above, with the conflict
stated.

## 10. Idempotency and crash recovery

**Recovery truth is the repository.** It consists of:
- the remote and local heads, and their ancestry;
- the delta's lifecycle fields, including every authorization record;
- the presence of the `pending/` directory;
- the staged digests;
- the final artifact state.

Commit messages and any local log are advisory only. No database, daemon or service holds workflow
state, because every phase that matters leaves a commit, a file or a record.

**Authorization is never inferred from the existence of a basis commit, and an older record never
authorizes a newer basis.** Authorization exists only as a record that passes the binding rule.

| State | Evidence | Resume |
| --- | --- | --- |
| **R0** Accepted delta, not staged | Delta `ACCEPTED` on the branch (or in the payload) and no `pending/<DELTA-ID>/` anywhere | Start at P1. |
| **R1** Semantic basis local only | Remote = `expected_start_sha`; one local commit staging `pending/<DELTA-ID>/`, with no new record | Re-run P2 on it, then P3. |
| **R2** Semantic basis on the remote, verified; no record binds it | Remote = that commit; `Seal authorizations` is `Pending`, or holds only records naming other bases | Re-run P4 (it only reads), then wait at the hold for an authorization. |
| **R3** New record committed locally, not pushed | Remote = the semantic basis; a local child whose only change is one appended record | Validate the record against section 5.3 and the digest invariant, then push and read back (P4A) if the remote is unchanged. |
| **R4** Authorization commit on the remote | Remote = a child of the semantic basis whose last record names that basis; `pending/` present; delta `ACCEPTED` | Re-run the P4A readback and binding rule, taking a fresh `basis_readback_utc`. P5 may proceed only if the latest record binds. |
| **R5** Seal local only | Remote = the final basis; a local child whose diff from it is exactly the section 7.2 whitelist | If the remote is still the final basis, P6; otherwise `REMOTE_MOVED_BEFORE_SEAL`. |
| **R6** Seal on the remote; final readback incomplete | Remote = a child of the final basis; delta `PUBLISHED`; `pending/<DELTA-ID>/` absent | Re-run P7. |
| **R7** Fully published | As R6, and P7's checks pass; the `CURRENT-STATE.md` digest equals the bound `CURRENT-STATE.next.md` digest | No mutation. Return `PUBLISHED_VERIFIED`, marked as already published. |

**If R4 is invalidated later** (section 4):
- keep every record;
- build and verify a replacement semantic basis;
- return to R2 for that basis;
- append a new record through R3 and R4.

A delta may therefore pass through R2–R4 more than once. Each pass leaves one more record.

A state that matches no row is `PRECONDITION_DRIFT`, never "the closest row". The same delta found
`PUBLISHED` with a receipt naming a different basis is also `PRECONDITION_DRIFT`.

## 11. Publisher Receipt

One envelope, readable by people and parseable by machine. It is plain `key: value` lines, with
lists indented. There are no confidence scores.

```
AGENCYOS PUBLISHER RECEIPT

Delta ID:
Mode:
Result:
Failure class:
Branch:
Starting SHA:
Semantic-basis SHA:
Final publication-basis SHA:
Basis readback UTC:
Staged CURRENT-STATE SHA-256:
Payload SHA-256:
Delta substantive SHA-256:
Binding seal authorization: <record ID; authority; authorized UTC; reference>
Earlier authorization records:
Sealing SHA:
Seal readback UTC:
Changed paths:
Decision receipts affected:
Product code changed: YES/NO
Unresolved warnings:
Next required authority/action:
```

| Result | Meaning |
| --- | --- |
| `PUBLISHED_VERIFIED` | P7 passed. The transition is `PUBLISHED`. |
| `BASIS_VERIFIED_AWAITING_SEAL` | P4 passed. A durable seal authorization record is required next. |
| `CORRECTION_VERIFIED` | A descriptive correction passed P4. |
| `STOPPED_PRECONDITION` | `PRECONDITION_DRIFT` or `SEMANTIC_CHANGE_REQUIRES_NEW_DELTA` |
| `STOPPED_AUTHORITY` | `AUTHORITY_MISSING` or `SEAL_AUTHORIZATION_INVALID` |
| `STOPPED_SCOPE` | `SCOPE_VIOLATION` or `PRODUCT_BOUNDARY_VIOLATION` |
| `FAILED_SEAL_VALIDATION` | `SUBSTANTIVE_MUTATION_DURING_SEAL` |
| `FAILED_REMOTE_VERIFICATION` | `REMOTE_BASIS_MISMATCH`, `REMOTE_MOVED_BEFORE_SEAL` or `FINAL_READBACK_MISMATCH` |

Fields that do not apply read `None`. The receipt is an **execution receipt** (protocol section
3.4). It is evidence for the next cycle, never authority. Its durable facts are already in the
artifacts and the basis trees, so the receipt needs no storage of its own.

## 12. Worked example: `DELTA-20260928-001` (bootstrap, historical)

**Classification.** `DELTA-20260928-001` was a **bootstrap migration**. It ran by hand, before
this contract existed and before `CURRENT-STATE.md` became the active authority. Its technique of
making `CURRENT-STATE.md` itself the candidate was valid only because that file was not yet
authoritative. It is historical evidence, **not the steady-state pattern**: Self-Update V1 stages
in `pending/` instead (section 2) and requires a durable authorization record (section 5). The
delta has no `Seal authorizations` field, and none is added. The history below is recorded as it
happened and is not rewritten.

| Commit | Parent | Role, and its V1 counterpart |
| --- | --- | --- |
| `388534de2920cd4fe25074efdfb37b26b12bda23` | `1d4c136` | First basis, pushed, read back and held for Control Room inspection (Phase 2B). **Content-bearing publication commit** of the four decisions. The candidate content sat in `CURRENT-STATE.md` behind a whole-file candidate header; V1 would stage it in `pending/` and leave the active file untouched. |
| `7639a3e` (never pushed; amended) | `388534d` | A local pre-seal correction, amended on authorization before any push. |
| `4092094d803577a79bcd74d0e122a384843787be` | `388534d` | A pre-seal correction on top of the pushed basis, and the **final publication basis**, read back at 2026-09-28T23:15:18Z. `DECISIONS.md` is byte-identical to `388534d`. V1 would treat it as a replacement semantic basis followed by an authorization commit. Here, the seal authorization was a Control Room instruction that was not recorded in the repository, so no authorization commit exists. |
| `1a64ba4035f0453864ad5e7143e24b00da6e535c` | `4092094` | **Seal** (P5–P7): delta `PUBLISHED`; the receipt names `4092094`; the decision receipts name `388534d`; `CURRENT-STATE.md` activated. Final readback at 2026-09-28T23:16:52Z. |
| `1e78558826f994cbd844e5a746070e830e77f49f` | `1a64ba4` | **Descriptive correction** (section 7.3): tense only; no entry, status or receipt changed. **Not a state transition.** |

The history remains representable. It is the origin of four V1 features:
- the hold;
- a replacement basis built on top of a pushed one;
- decision receipts naming an earlier content-bearing commit;
- the descriptive-correction mode.

It also shows why V1 closes two gaps that the bootstrap run left open:
- the seal authorization was not durable;
- the candidate was exposed in the authoritative file.

A minor timing difference is also recorded: `2026-09-28T23:15:18Z` was taken when the remote head
was confirmed, moments before the content comparison finished. P4A records the time when the
checks complete.

## 13. Non-goals and remaining questions

This contract does not implement or authorize:
- a filesystem or GitHub watcher, scheduler or webhook;
- alert delivery;
- autonomous adjudication of any kind;
- product-code, schema, API or domain changes;
- NG-4, or any next-generation work;
- a plugin architecture or a generic workflow engine;
- a database, daemon or workflow service;
- a UI.

**Resolved by this contract and the ledger:**
- how a seal authorization is recorded, bound, invalidated and re-authorized (sections 4 and 5;
  `CANONICAL-DELTAS.md`);
- how pending state is represented without touching the active `CURRENT-STATE.md` (section 2);
- where the V1 payload is stored, and in what form (sections 2 and 3.1).

**Implementation-level choices**, made in [CANONICAL-PUBLISHER.md](CANONICAL-PUBLISHER.md): the
implementation language (C#, `net10.0`) and the CLI syntax. Still open:
- whether detection begins with polling or webhooks;
- log formatting beyond the normative receipt.

No normative prerequisite remains. First use on the real canonical branch is a separate,
adjudicated step.
