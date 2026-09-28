# AgencyOS Control Room decisions

**Protocol:** [CANONICAL-STATE-PROTOCOL.md](CANONICAL-STATE-PROTOCOL.md)

The durable, append-only record of explicit Owner decisions and Control Room architecture and
domain adjudications that must survive independently of chat history.

This ledger is **not** a replacement for ADRs (`docs/adr/`), which keep their governed scope. It is
**not** a list of every technical conclusion. It holds decisions whose authority, or whose
correction chain, must stay reconstructable without any conversation.

**Migration status.** The first controlled migration, `DELTA-20260928-001`, is published. It
added four pre-protocol decisions (`DECISION-20260928-001` to `-004`, under *Entries* below), and
their publication receipts are sealed. No other historical decision is reconstructed here, and no
other ID has been assigned.
Operational-closure decisions remain governed by
[`docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`](../reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md).

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
decides how migrated decisions are identified. In `DELTA-20260928-001` a migrated pre-protocol
decision takes an ID dated by its actual decision date, and its `Evidence / provenance` states that
the ID was assigned during migration and did not exist on that date.

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

---

# Entries

Migration provenance common to the four entries below: each durable Decision ID was assigned
during Phase 2 migration (`DELTA-20260928-001`). The underlying decision or adjudication was made
on 2026-09-28, before this decision ledger existed. The IDs do not imply that they existed on that
date.

## DECISION-20260928-001

Status: ACTIVE

Date: 2026-09-28

Authority: CONTROL_ROOM

Question: What is the stable representation primitive across materially different representation
verticals, and which parts of Build-97 "Representation" are universal versus Film/TV-specific?

Decision: The NG-1 definition is accepted:

> A Representation Mandate is an effective-dated, bounded representational appointment or
> authority connecting the agency to a concrete principal-capable represented subject. It defines
> what the agency is authorized to represent or pursue, including relevant capacities, matters,
> works/assets/interests, markets or territories and mandate conditions where applicable;
> preserves the authority provenance, attributable internal responsibility and history; and
> remains distinct from the subject's profile, the legal instrument, underlying ownership/right
> validity, rights grants, transaction participation, and economic entitlement.

- The mandate is the stronger proved primitive.
- A separately persisted umbrella "Representation" relationship remains unproved, and may instead
  be a derived roll-up.

Scope: Governs next-generation representation architecture. It does **not**:
- approve schema;
- approve a persistence shape;
- make territory mandatory;
- make exclusivity mandatory;
- define a universal commission formula;
- make Work/IP a represented subject;
- decide whether the umbrella Representation is persisted or derived.

Evidence / provenance:
- The Control Room's terminal NG-1 adjudication of 2026-09-28, following Build-97 source
  inspection and bounded cross-vertical falsification.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`.
- Claude research reports were evidence inputs only. The decision is the Control Room's.
- This durable Decision ID was assigned during Phase 2 migration; the underlying
  decision/adjudication was made on 2026-09-28 before the decision ledger existed.

Consequences: NG-1 is closed. Future architecture must preserve mandate-level bounded authority
and lifecycle.

Supersedes: None

Unchanged: Build-97's implemented Representation model, schema and behaviour. No product change
follows from this decision.

Open:
- persisted versus derived umbrella Representation;
- qualifying clienthood;
- how work/object coverage is implemented;
- delegated and sub-agency authority topology;
- exact commission placement.

Recorded by: Claude (Claude Code, AgencyOS executor), Phase 2A migration, on Control Room
instruction. Claude is the recorder, not the authority.

Publication receipt: 388534de2920cd4fe25074efdfb37b26b12bda23 on origin/operational-regression-gate; remote readback verified 2026-09-28T23:15:18Z

## DECISION-20260928-002

Status: ACTIVE

Date: 2026-09-28

Authority: OWNER

Question: Approve for NG-2 the conceptual represented-subject family "Person + Group +
Organization", with Work/IP as mandate object by default and not first-class represented subject
at this stage?

Decision: The Owner explicitly approved the subject family. Its canonical next-generation wording
is **"Person + Group + External Organization"**, and Work/IP is a mandate object by default and is
not a first-class represented subject at this stage.

The wording differs from the question as asked. Build-97 "Organization" is the tenant/security
boundary, so the next-generation conceptual external-business subject is called External
Organization until implementation terminology is decided. This is a terminology correction by the
Control Room, not a change to what the Owner approved.

Scope: Conceptual architecture only. It does **not**:
- create a "Subject" table;
- create an "Entity";
- rename Build-97 "Organization";
- decide whether Build-97 "Company" is exactly the future External Organization implementation;
- exclude future evidence that may justify Estate/Trust, or IP as a subject, later.

Evidence / provenance:
- Explicit Owner approval on 2026-09-28 (NG-OD1).
- The Control Room's terminology correction from "Organization" to "External Organization", to
  avoid colliding with the Build-97 tenant "Organization".
- This durable Decision ID was assigned during Phase 2 migration; the underlying
  decision/adjudication was made on 2026-09-28 before the decision ledger existed.

Consequences: NG-2 may test identity and reference architecture against these three concrete
subject kinds.

Supersedes: None

Unchanged: Build-97 "Organization" remains the tenant/security boundary.

Open:
- Company versus External Organization implementation;
- Estate/Trust;
- IP as a subject, if later evidence requires it.

Recorded by: Claude (Claude Code, AgencyOS executor), Phase 2A migration, on Control Room
instruction. Claude is the recorder, not the authority.

Publication receipt: 388534de2920cd4fe25074efdfb37b26b12bda23 on origin/operational-regression-gate; remote readback verified 2026-09-28T23:15:18Z

## DECISION-20260928-003

Status: ACTIVE

Date: 2026-09-28

Authority: CONTROL_ROOM

Question: How should the three represented-subject kinds be referenced without collapsing them
into a speculative universal Entity/Party abstraction or losing referential and tenant integrity?

Decision:

> Represented subjects form a local, closed, semantically typed reference family over
> "Person | Group | External Organization". A reference preserves the concrete subject kind and
> stable concrete identity; canonical persistence must structurally enforce referenced-subject
> existence and same-tenant containment for the supported kind. The abstraction is local to
> contexts where a principal-capable represented subject is semantically valid and does not widen
> unrelated reference families. Historical references remain anchored to the original subject and
> do not automatically follow successors.

Also accepted:
- no global "SubjectId";
- no generic "Entity";
- no shared "Party" row is justified;
- no opaque raw "(kind, Guid)" as canonical represented-subject truth;
- different modules may legitimately have different typed reference families;
- a future kind requires an explicit, reviewed addition.

Scope: The architecture boundary only. The exact storage mapping is deferred. No specific table,
EF mapping or API union has been selected.

Evidence / provenance:
- The Control Room's NG-2A and NG-2B adjudications of 2026-09-28.
- Build-97 precedents: `RelationshipEndpoint` (ADR-0011); `OpportunitySubjectRef` (ADR-0020);
  wide typed link arcs; and ADR-0019, a deliberate validator-only exception that is not adopted
  for represented-subject authority truth. The wide link arcs do not invalidate ADR-0011.
- Claude research reports were evidence inputs only.
- This durable Decision ID was assigned during Phase 2 migration; the underlying
  decision/adjudication was made on 2026-09-28 before the decision ledger existed.

Consequences: NG-2 is closed. NG-3 may reason about commerce using a stable subject boundary
without designing Group persistence.

Supersedes: None

Unchanged: DECISION-20260928-002's subject family. The Build-97 reference mechanisms and ADR-0011,
ADR-0019 and ADR-0020 are unchanged.

Open:
- Company versus External Organization implementation;
- Group persistence;
- the successor/predecessor model;
- Person reconciliation and deduplication;
- the exact storage mapping;
- a retention/deletion mechanism that prevents orphaned historical truth.

Recorded by: Claude (Claude Code, AgencyOS executor), Phase 2A migration, on Control Room
instruction. Claude is the recorder, not the authority.

Publication receipt: 388534de2920cd4fe25074efdfb37b26b12bda23 on origin/operational-regression-gate; remote readback verified 2026-09-28T23:15:18Z

## DECISION-20260928-004

Status: ACTIVE

Date: 2026-09-28

Authority: CONTROL_ROOM

Question: What commercial concepts between representation authority and accounting settlement are
irreducible core facts, and is the commercial model a linear pipeline or a non-linear structure?

Decision:

> The next-generation commercial model is a HYBRID: semantic layers connected by optional, causal
> and historical graph relationships. A mandatory linear pipeline is falsified.

Layer boundaries:
1. Representation authority
2. Market activity
3. Commercial negotiation
4. Legal truth
5. Economic truth
6. Collection/billing
7. Cash application
8. Accounting projection

Accepted conclusions:
- pursuit/Opportunity is an optional precursor or context, not a universal mandatory parent;
- a commercial arrangement/deal is distinct from a proposal/offer;
- an agreement snapshot is distinct from legal instrument truth;
- a legal instrument is distinct from a rights grant;
- a legal/operational obligation is distinct from a receivable;
- an invoice is optional and distinct from a receivable;
- a payment is distinct from its allocation/application;
- the ledger/accounting does not replace upstream commercial or legal truth;
- commercial activity needs conceptual mandate lineage;
- amount determination is a strong core capability where a real obligation starts contingent,
  formula-based or unquantified;
- agency commission/fee entitlement is a representation-economics capability;
- a generic universal "EconomicEntitlement" super-concept is not justified;
- "DealKind" and term vocabularies are vertical-specific rather than neutral core.

Scope: A commercial architecture decomposition. It does **not**:
- approve schema;
- approve a generic Transaction entity;
- approve generic JSON terms;
- approve a universal rights ontology;
- approve a universal "Settlement" primitive;
- define the vertical-extension mechanism;
- begin Music architecture.

Evidence / provenance:
- The Control Room's terminal NG-3 adjudication of 2026-09-28.
- Build-97 source inspection across Deal, Offer, Contract, RightsGrant, MonetaryObligation,
  Commission, Receivable, Invoice, Payment and Ledger (product commit
  `b3f41bfd68e81ab42da899671f58e01f0988d3d2`).
- Bounded Film/TV ↔ live-music falsification.
- The Claude report was evidence only, not authority.
- This durable Decision ID was assigned during Phase 2 migration; the underlying
  decision/adjudication was made on 2026-09-28 before the decision ledger existed.

Consequences: NG-3 is closed. The next architecture stage is NG-4, Vertical Extension
Architecture.

Supersedes: None

Unchanged: Build-97's implemented commercial, legal and finance model. DECISION-20260928-001 to
-003 are unchanged.

Open:
- the exact commercial-to-mandate cardinality;
- the exact agreement-snapshot representation;
- the exact amount-determination record;
- one legal instrument covering several arrangements;
- the future commission model;
- the exact ledger integration.

Recorded by: Claude (Claude Code, AgencyOS executor), Phase 2A migration, on Control Room
instruction. Claude is the recorder, not the authority.

Publication receipt: 388534de2920cd4fe25074efdfb37b26b12bda23 on origin/operational-regression-gate; remote readback verified 2026-09-28T23:15:18Z
