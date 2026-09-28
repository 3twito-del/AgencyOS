# AgencyOS Control Room decisions

**Protocol:** [CANONICAL-STATE-PROTOCOL.md](CANONICAL-STATE-PROTOCOL.md)

The durable, append-only record of explicit Owner decisions and Control Room architecture and
domain adjudications that must survive independently of chat history.

This ledger is **not** a replacement for ADRs (`docs/adr/`), which keep their governed scope. It is
**not** a list of every technical conclusion. It holds decisions whose authority, or whose
correction chain, must stay reconstructable without any conversation.

**No decisions have been migrated under this protocol yet.** The first controlled migration is
Phase 2. Historical decisions are not reconstructed here, and no ID has been assigned to any of
them.

---

## Authority

A decision entry names exactly one decision authority:
- **`OWNER`**: a decision reserved to the Owner (protocol section 3.3).
- **`CONTROL_ROOM`**: a Control Room adjudication (protocol section 3.2).

When the Owner explicitly accepts a Control Room recommendation, the authority is `OWNER`, and
the recommendation or adjudication is recorded in `Evidence / provenance`. The entry records
what the Owner actually approved. It does not silently broaden that into the whole recommendation.

**Claude is never a decision authority.** A Claude report may appear only as evidence or
provenance, and it never makes, confirms or implies a decision.

### Decision authority is typed, not ranked

**Supersession follows the authority appropriate to the decision class; authority is not a
generic escalation ladder.**

- **`OWNER` decisions.** A decision whose authority is `OWNER` may be superseded or revoked only
  by a later explicit `OWNER` decision. The Control Room may identify evidence that suggests
  reconsideration, but it cannot rewrite or revoke an Owner decision.
- **`CONTROL_ROOM` decisions.** A decision whose authority is `CONTROL_ROOM` is a Control Room
  adjudication within its proper authority boundary. A factual, evidentiary or
  domain-architecture adjudication may be superseded or corrected by a later `CONTROL_ROOM`
  adjudication, based on new evidence or a correction chain. An Owner instruction does not
  retrospectively rewrite what the evidence had established. If the Owner makes a new product or
  strategy choice in light of that evidence, it is recorded as a separate `OWNER` decision with
  its own scope and provenance.

This keeps Owner authority over Owner-reserved product and architecture choices, Control Room
authority over evidence adjudication, the historical truth about what was proved, and the
correction chains.

## Entry lifecycle

Every entry has one status:
- **`ACTIVE`**: the decision governs its stated scope.
- **`SUPERSEDED by DECISION-YYYYMMDD-NNN`**: a later decision replaces it.
- **`REVOKED by DECISION-YYYYMMDD-NNN`**: it no longer governs, and nothing replaces it. The
  revocation is itself a decision entry.

Earlier entries stay present, whatever their status.

### Immutability of entry content

**The substantive content of a published decision entry is immutable.** Its `Date`, `Authority`,
`Question`, `Decision`, `Scope`, `Evidence / provenance` (as originally published),
`Consequences`, `Supersedes`, `Unchanged`, `Open`, `Recorded by` and `Publication receipt` are
never rewritten because a later decision changes what currently governs. `Publication receipt`
follows its own one-time sealing rule (see *Publication receipt* below), and after sealing it is
fixed like every other substantive field.

The one narrow exception is the `Status:` line of an earlier entry. It may change only from
`ACTIVE` to `SUPERSEDED by DECISION-YYYYMMDD-NNN`, or from `ACTIVE` to
`REVOKED by DECISION-YYYYMMDD-NNN`, and only when all of the following hold:
1. a new published decision entry exists;
2. that new entry names the earlier Decision ID in its `Supersedes:` field;
3. the earlier entry's `Status:` line names the Decision ID that caused the transition.

Every supersession or revocation therefore requires a new decision entry. No other field of an
earlier entry, and no separate explanation, is added or changed.

**Updating lifecycle status does not edit the earlier decision into agreement with the later one.
Its original Question, Decision, Scope and reasoning remain unchanged.** A correction is always a
new entry, never an edit that makes an earlier decision appear to have always said its corrected
form.

## Publication receipt

A commit cannot contain its own SHA, so an entry cannot name the commit that first carries it. A
decision's publication receipt is therefore sealed in two commits.

1. **Pending.** A newly accepted decision entry is committed with `Publication receipt: Pending`.
   `Pending` is a sentinel, not a receipt. The decision is not yet `PUBLISHED`.
2. **Content-bearing publication commit.** The first commit that contains the accepted entry is
   the content-bearing publication commit. It is pushed normally, and its contents are then read
   back independently from the canonical remote branch. Its exact SHA is now known. The decision is
   still not fully `PUBLISHED`, because its durable receipt is not yet sealed.
3. **Receipt-sealing commit.** After that remote verification succeeds, one subsequent docs-only
   commit changes exactly `Publication receipt: Pending` to a receipt that identifies the
   content-bearing publication commit, for example:

   ```
   Publication receipt: <full SHA> on origin/operational-regression-gate; remote readback verified <timestamp UTC>
   ```

   The receipt names the earlier content-bearing commit, never the sealing commit itself. The
   sealing commit does not need to contain its own SHA.

The decision is `PUBLISHED` only when all of these hold:
1. the content-bearing commit is on the canonical remote branch;
2. its contents were read back from that remote;
3. the receipt-sealing commit records that verified commit;
4. the sealing commit itself is pushed normally;
5. the final state is read back from the canonical remote.

`Publication receipt: Pending` may be replaced exactly once, by the final verified receipt, and
only through this sealing step. **After that replacement, `Publication receipt` is immutable.** It
is not rewritten because the branch later advances. The recorded SHA remains the commit that first
published the substantive accepted decision.

Sealing does not change the decision's `Status:`. Decision lifecycle (`ACTIVE`, `SUPERSEDED`,
`REVOKED`) and canonical publication lifecycle (`ACCEPTED`, `PUBLISHED`; protocol section 2) are
separate. An `ACTIVE` decision may still be awaiting publication. There is no decision status for
publication; the protocol governs `ACCEPTED` and `PUBLISHED`.

## Decision ID

New entries use `DECISION-YYYYMMDD-NNN`: the date the decision was made, followed by a
three-digit sequence number for that date, starting at `001`.

IDs are never reused. They are not reconstructed or fabricated for historical decisions. Phase 2
decides how migrated decisions are identified.

## Decision versus delta

A **delta** asks: *what changed in canonical current state?*

A **decision** asks: *what explicit authoritative choice or adjudication governs that state?*

They are related, but one cannot stand in for the other:
- A new remote branch SHA can be a machine-verifiable state delta with no decision behind it.
- An Owner choice between two surviving architecture alternatives requires a decision.
- A Control Room adjudication that a next-generation stage is closed needs durable adjudicative
  provenance here if it is migrated into current state.
- Publishing a decision normally creates or accompanies a canonical delta, because
  `CURRENT-STATE.md` may change as a consequence.

The artifacts' roles:

| Artifact | Role |
| --- | --- |
| [`CURRENT-STATE.md`](CURRENT-STATE.md) | The active, concise current state. It cites the governing Decision IDs where useful. |
| [`CANONICAL-DELTAS.md`](CANONICAL-DELTAS.md) | The chronological ledger of state changes. |
| `DECISIONS.md` (this file) | The durable ledger of decisions, with their correction chains. |

---

## Fields

Every field is required. Write `None` rather than leaving a field out. Each entry must be
self-contained, so that a reader needs no conversation to understand it.

| Field | Content |
| --- | --- |
| Status | `ACTIVE`, `SUPERSEDED by DECISION-YYYYMMDD-NNN` or `REVOKED by DECISION-YYYYMMDD-NNN`. After publication, this is the only field that may change (see *Immutability of entry content*). |
| Date | The date the authority made the decision. This is not necessarily the date it was recorded. |
| Authority | `OWNER` or `CONTROL_ROOM`. |
| Question | The actual decision point, as it stood when it was decided, including the alternatives that survived to it. It is not reworded to fit the answer. |
| Decision | What was actually accepted, in its original terms. A later reinterpretation is not recorded here. |
| Scope | What the decision governs, and explicitly what it does not govern. |
| Evidence / provenance | What the decision rested on and where it was made: canonical sources, the Control Room adjudication, the Owner instruction, Claude reports. Each is cited with an exact anchor (commit, path, run ID). A Claude report is evidence, never authority. |
| Consequences | What becomes allowed, closed or constrained as a result. |
| Supersedes | The prior Decision ID this entry replaces or revokes, or `None`. |
| Unchanged | What stays as it was, stated explicitly so that the decision is not read more broadly than it was made. |
| Open | The adjacent questions this decision does not resolve, so that it does not appear to settle them. |
| Recorded by | Who wrote this durable record. This is not who held the decision authority. |
| Publication receipt | `Pending` until sealed. After sealing, the full SHA of the content-bearing publication commit, its canonical branch and the UTC time of its remote readback (see *Publication receipt*). It never names the commit that contains it. |

## Template

Copy this block for each new entry.

```
## DECISION-YYYYMMDD-NNN

Status:
Date:
Authority:
Question:
Decision:
Scope:
Evidence / provenance:
Consequences:
Supersedes:
Unchanged:
Open:
Recorded by:
Publication receipt:
```
