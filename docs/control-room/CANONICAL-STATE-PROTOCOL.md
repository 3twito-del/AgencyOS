# AgencyOS canonical state protocol

**Status:** normative · **Phase:** 1, protocol foundation only · **Date:** 2026-09-29

This protocol governs how evidence about AgencyOS becomes canonical current state. It defines
rules. It implements nothing: there is no watcher, alert sender, scheduled job or publisher yet,
and any future automation must obey this document.

> Evidence may be detected automatically.
> Semantic state transitions require the authority appropriate to the claim.

---

## 1. Purpose

AgencyOS current state must be **durable, reconstructable and conversation-independent**. A
reader with the repository and fresh GitHub verification must be able to answer "where does
AgencyOS stand?" without any chat history.

The ChatGPT Control Room is where state changes are **adjudicated**. It is not the place where
current state is **stored**. Current state is stored in the repository.

### Roles (already decided; preserved here)

- **Owner:** final product and architecture decisions, and acceptance of residual risk.
- **ChatGPT Control Room:** independent critic, architect and planner, evidence arbiter, and
  author of Claude prompts.
- **Claude:** the sole executor of AgencyOS repository changes.

Claude never turns its own report into canonical semantic truth because it wrote or committed the
report.

## 2. State-transition lifecycle

Every candidate change to canonical state moves through exactly these states:

```
OBSERVED → PENDING_ADJUDICATION → ACCEPTED | REJECTED
                                      ACCEPTED → PUBLISHED
```

| State | Meaning |
| --- | --- |
| **OBSERVED** | Evidence or a possible state change was detected. No semantic conclusion is canonical. |
| **PENDING_ADJUDICATION** | The evidence has been normalised into an explicit candidate delta (section 6) and requires the appropriate authority (section 3). |
| **ACCEPTED** | The Control Room and/or the Owner, according to the authority boundary, has explicitly accepted the semantic delta. |
| **REJECTED** | The candidate delta was considered and does not change canonical current state. It stays recorded. |
| **PUBLISHED** | An accepted delta has been written into the durable canonical current-state artifacts, and the publication has been verified (section 8). |

**`PUBLISHED` is the only state that may change the durable bootstrap answer to "where does
AgencyOS stand?"** An accepted but unpublished delta is a decision awaiting publication. It is not
current state.

## 3. Authority classes

### 3.1 MACHINE-VERIFIABLE FACT

Facts a program can check against the repository or GitHub, for example:
- a branch SHA, a commit's parent, or a tag's dereference;
- a CI or Nightly result;
- an artifact hash;
- whether a file exists;
- whether a commit's changes stay under `docs/`.

These may be verified automatically. **Automatic verification of a fact does not authorise any
broader semantic conclusion.** "CI #109 succeeded on `b3f41bf`" is a fact; "C10 is proved" is not
a fact a program may conclude from it.

### 3.2 CONTROL-ROOM ADJUDICATION

Required for semantic claims, for example:
- a finding is proved or closed;
- an architecture invariant survives or fails;
- a stage is complete;
- an ADR's reconsideration threshold is met;
- evidence is sufficient;
- the canonical interpretation of conflicting sources.

### 3.3 OWNER DECISION

Required wherever existing governance reserves authority to the Owner, including:
- a genuine product or domain ambiguity between surviving alternatives;
- acceptance of residual risk;
- expansion of the contract, API, schema or domain boundary where Owner approval is required;
- explicit strategic choices reserved for the Owner.

Owner approval is never inferred from silence.

### 3.4 EXECUTION RECEIPT

A Claude implementation or research report is **evidence only**. It may be cited as an evidence
anchor. It never self-promotes to `ACCEPTED`, whatever it concludes and wherever it is committed.

## 4. Canonical precedence

When sources disagree about the **current** state:

1. `docs/control-room/CURRENT-STATE.md`.
2. Fresh authoritative repository or runtime evidence, for facts that can have changed (branch
   SHAs, tags, CI results, file contents).
3. Accepted and published canonical deltas (`docs/control-room/CANONICAL-DELTAS.md`).
4. Terminal specialised canonical records, **for their governed scope**. Operational closure is
   governed by `docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`.
5. Older canonical snapshots and incremental updates, including release records, review records,
   ADRs and Control Room Project bootstrap source files held outside the repository.
6. Conversation transcripts, Claude reports and setup-chat material, **as evidence inputs only**.

**A newer source wins only for the scope it actually governs.** Historical evidence is preserved,
and must not be rewritten into having said something it did not say at the time.

Until Phase 2 migration is adjudicated and published, `CURRENT-STATE.md` is a bootstrap shell and
does not supersede the substantive contents of the sources below it (see that file).

### Control Room artifacts

- `docs/control-room/CURRENT-STATE.md`: the active, concise current state. It stays the first
  current-state bootstrap source.
- `docs/control-room/CANONICAL-DELTAS.md`: the chronological ledger of state transitions.
- `docs/control-room/DECISIONS.md`: the durable authority for explicit Owner decisions and Control
  Room adjudications migrated or recorded under this protocol, and for their correction chains.
  It is authoritative for the content and history of the decisions it records. `CURRENT-STATE.md`
  cites them and does not restate them differently.

Specialised records and ADRs keep their governed scope.

### Prior and specialised durable sources

These keep their authority for their governed scope and are neither deleted nor rewritten by this
protocol:
- `docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`: the terminal operational-ALPHA closure record;
- `docs/releases/`: release identity records;
- `docs/adr/`: architecture decisions;
- `docs/reviews/`: review and evidence records;
- Control Room Project bootstrap source files, outside the repository.

## 5. Correction-chain invariant

Every semantic canonical transition preserves:
- the **prior claim**;
- the **new claim**;
- the **triggering evidence**;
- the **authority** that accepted it;
- what was **superseded**;
- what **remained unchanged**;
- what is still **unresolved**.

There is no silent rewriting of history. A correction is a new entry that points at what it
corrects. It never edits the earlier entry into agreement.

## 6. Candidate delta contract

A candidate delta in `PENDING_ADJUDICATION` must state:

| Field | Content |
| --- | --- |
| Delta ID | `DELTA-YYYYMMDD-NNN` |
| Detected | Timestamp, UTC |
| Sources / evidence anchors | Exact commits, paths, run IDs, artifact hashes, report locations |
| Current canonical claim | What the canonical sources say now, with their location |
| Proposed new claim | What would be true if accepted |
| Exact scope | What the claim governs, and what it does not |
| Claimed transition | For example "C-criterion NOT PROVED → PROVED" or "stage OPEN → COMPLETE" |
| Evidence level | Where it sits on the evidence ladder: source, deterministic tests, integration/API, live Windows, accessibility/keyboard, genuine isolated blind operator, real operations |
| Conflicts | Sources or evidence that disagree |
| Authority required | Machine-verifiable fact, Control-Room adjudication or Owner decision |
| What changes if accepted | The exact canonical statements that would change |
| What remains unchanged | The explicitly preserved statements |
| Unresolved questions | What acceptance would leave open |
| Forbidden implications | What acceptance must not be read as meaning |

## 7. Alert contract (future; not implemented in this phase)

When a candidate delta needs adjudication, a future alert to the Control Room takes this shape:

```
AGENCYOS CANONICAL ALERT

Delta:
Trigger:
Current canonical state:
Candidate change:
Evidence anchors:
Conflicts:
Authority required:
Product code changed: YES/NO
Recommended Control Room action:
```

An alert is a request for adjudication. It is never an adjudication.

## 8. Publication invariant

Publication is atomic at the semantic level. A delta is `PUBLISHED` only when all of these hold:
1. the accepted delta is recorded in `CANONICAL-DELTAS.md`;
2. `CURRENT-STATE.md` is advanced to reflect it;
3. the decision record in `docs/control-room/DECISIONS.md` is updated, where one applies;
4. the correction chain (section 5) is preserved;
5. the repository publication is verified: the commit is on the canonical remote branch, and its
   contents are read back from the remote.

If only some of these are written, the delta is **not** fully published, and current state has not
changed.

## 9. Conversation independence

- The external setup chat is **not** a current-state authority.
- No conversation transcript is required to reconstruct current AgencyOS state.
- A fresh Control Room must be able to bootstrap from the durable canonical sources in section 4,
  plus fresh GitHub verification for facts that can have changed.
- Conversations and reports remain valuable **evidence**. They enter canonical state only through
  this protocol.

## 10. Anti-authority rules

The detector, publisher or any automation acting under this protocol must never:
- close a finding because tests are green;
- turn a Claude verdict into canonical truth automatically;
- infer Owner approval from silence;
- promote work in progress because it is committed or visible on the remote;
- replace evidence limitations with confidence language;
- erase or compress a correction chain;
- perform product changes;
- broaden scope because an abstraction is convenient.
