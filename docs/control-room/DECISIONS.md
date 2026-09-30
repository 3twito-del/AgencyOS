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

## DECISION-20260929-001

Status: ACTIVE

Date: 2026-09-29

Authority: OWNER

Question: Should AgencyOS begin NG-4 — Vertical Extension Architecture now, and if so, what scope of work is authorized?

Decision: The Owner explicitly authorizes the start of **NG-4 — Vertical Extension Architecture for research and design only**. This authorization does **not** include product code, schema/API/domain expansion, NG-5, or implementation.

Scope:
- Authorizes NG-4 research and design after this decision is canonically PUBLISHED.
- Allows evidence gathering, source inspection, falsification, architecture analysis and design work within NG-4.
- Does not authorize implementation of any resulting design.
- Does not authorize product-code changes.
- Does not authorize schema, API or domain expansion.
- Does not authorize NG-5.
- Does not pre-approve any architecture conclusion reached during NG-4.
- Any genuine Owner-reserved ambiguity discovered during NG-4 returns to the Owner.

Evidence / provenance:
- Explicit Owner instruction to the AgencyOS Control Room on 2026-09-29:
  “אני מאשר להתחיל את NG-4 — Vertical Extension Architecture, למחקר ותכנון בלבד. האישור אינו כולל קוד מוצר, schema/API/domain expansion, NG-5 או implementation.”
- `docs/control-room/CURRENT-STATE.md` at `c629c5bbd01e597be0518a553fc012b6e670110a` records NG-4 as NEXT, not authorized and not begun, and requires explicit Owner authorization before NG-4 starts.
- The Control Room normalized the Owner instruction into this decision without broadening its scope.
- Claude is the recorder/executor only and is not a decision authority.

Consequences:
- Once this decision and its accompanying canonical delta are fully PUBLISHED, NG-4 research/design may begin.
- The next bounded work becomes NG-4 research/design under this decision.
- No implementation authority follows from this decision.

Supersedes: None

Unchanged:
- Self-Update V1 remains COMPLETE.
- Operational Closure remains COMPLETE and terminal.
- Build 97 remains the released product identity.
- NG-1, NG-2 and NG-3 remain CLOSED.
- `DECISION-20260928-002` remains ACTIVE.
- No product, schema, API or domain behavior changes.
- NG-5 remains not begun.
- All existing architecture questions remain unresolved except the authorization-to-start question answered here.

Open:
- The NG-4 extension mechanism.
- Core-versus-vertical boundaries.
- Vertical contribution categories and contracts.
- Multi-vertical composition.
- Film/TV reference architecture.
- Cross-vertical falsification.
- Any Owner-reserved architecture ambiguity discovered by NG-4.

Recorded by: Claude (AgencyOS executor), on explicit Owner instruction normalized by the Control Room. Claude is the recorder, not the authority.

Publication receipt: d4a3d9ad8b2611aa42c676b9ba09e733af8635a6 on origin/operational-regression-gate; remote readback verified 2026-09-29T16:36:37Z

## DECISION-20260929-002

Status: ACTIVE

Date: 2026-09-29

Authority: CONTROL_ROOM

Question: Has NG-4 — Vertical Extension Architecture reached a terminal architecture conclusion, and if so what core/vertical extension boundary governs future design?

Decision: NG-4 is terminally accepted as a conceptual architecture stage. The accepted architecture is a **compiled modular monolith + bounded vertical semantic contexts + category-differentiated composition contracts + explicit role-local typed bridges where a shared/core role must reference a vertical-owned object**. It is conceptual architecture only and approves no product implementation, storage mapping, schema, API, class/interface shape, migration, module/package layout or NG-5 work.

Governing conclusions:
- The neutral core remains the single owner of the shared representation, commercial, legal, economic and cash truth established by NG-1 to NG-3.
- Verticals extend it through the contribution categories C1–C6 under bounded semantic ownership; N1 and N2 are consumers/translators only.
- Cross-context references to vertical-owned C3 objects use explicit role-local typed bridges; each newly admissible kind is an explicit reviewed expansion.
- Core invariants cannot be relaxed by vertical rules.
- One resulting core fact has one canonical owner and no competing determination writers.
- Definition meaning and history remain resolvable without selecting one code, data or version-storage mechanism.
- No single VerticalId and no universal generic extension abstraction is approved.
- Film/TV proves the model can express a sophisticated first vertical without defining universality.
- Exact implementation remains unauthorized and deferred.

A. Neutral core boundary. The neutral core owns: represented-subject identity and the typed subject boundary; representation-mandate authority, effective dating and history; the neutral semantic boundaries of market activity, commercial arrangement, proposal/offer, legal truth, economic truth, collection/billing, cash application and accounting projection; canonical identities for the shared commercial, legal and economic facts those decisions established; money/currency truth; obligations, receivables, payments and allocations; neutral amount-determination and representation commission/fee capabilities; and the composition contracts required for vertical semantics to participate without duplicating shared truth. This does not make every current Build-97 aggregate or technique a permanent neutral-core mechanism: exact Build-97 offer immutability and derived-accepted-offer mechanics are precedents, not the invariant; exact Build-97 Representation statuses and state machine are not promoted; exact Build-97 amount-kind enums and storage are not promoted; exact Build-97 RightsGrant, ContractOption and DeadlineRule aggregate and mechanism shapes are not made universal by NG-4. The already-decided distinctions are preserved: offer/proposal != arrangement; agreement snapshot != legal instrument; legal instrument != recorded rights grant; obligation != receivable; payment != allocation.

B. Vertical contribution taxonomy.
- C1 — Vertical Vocabulary: controlled vertical meaning such as kind, role, capacity, medium, category or event anchor.
- C2 — Vertical Typed Term: a typed value-shaped vertical fact associated with a commercial, legal or economic host whose shared identity remains owned by the core.
- C3 — Vertical Domain Object: a vertical-owned identity-bearing object whose independent identity, reference, history or lifecycle has been earned; never a generic extension bag.
- C4 — Admissibility / Constraint Rule: a context-scoped rule governing whether a canonical action or assertion is semantically admissible.
- C5 — Determination Rule: a context-scoped rule or explicit rule composition that derives a material result whose canonical truth remains owned by the proper core fact.
- C6 — External Normative / Reference Dataset: externally authored, source-identified, effective-dated facts materially used by vertical rules.
- N1 — vertical-aware query/projection: consumer only, never semantic owner.
- N2 — integration adapter: translator into owned semantics, never semantic owner.
- No seventh durable "finding" category is proven by current evidence.

C. Definition contract (C1/C2). Semantic authority belongs to the vertical context. Definition identity must be stable, authority-scoped and independent of display labels. Historical instances must retain resolvable original meaning; changed meaning may require a new identity or another explicit historical-version mechanism, and NG-4 does not mandate a Version field or one storage strategy. Material instance value, currency, unit and basis must remain historically interpretable where applicable. Display metadata may evolve without changing business identity when meaning is unchanged. Definitions needed by clients and queries must be published through an authoritative runtime/canonical distribution surface; the server/runtime may be that authoritative publication and distribution surface, while the vertical remains the semantic authority for C1, C2, C4 and C5, and for C6 the external source owns the external meaning and effective regime. Clients are not independent semantic authorities.

D. C3 contract. C3 objects remain vertical-owned. When a core or shared semantic role legitimately needs to reference a vertical-owned C3 object, the bridge is local to that role; the reference is semantically typed; it preserves stable concrete identity; referenced existence must be enforceable; same-tenant containment must be preserved; history remains anchored to the original object; and every newly admissible kind is an explicit reviewed expansion of that shared composition contract. Vertical modules cannot self-register arbitrary C3 kinds without a core/shared-contract change, and zero-core-touch extensibility is not required. Entity, Party, SubjectId, a universal (VerticalId, Kind, Id) and a generic object store are not used. A vertical concept is promoted into neutral core only when evidence shows that its identity or lifecycle itself is vertical-neutral shared truth across contexts; cross-vertical use alone is insufficient for promotion. Work/IP remains a mandate object by default under DECISION-20260928-002, and NG-4 does not promote Work/IP to a represented subject or universal core entity.

E. C4 contract. Core invariants apply first and cannot be relaxed by a vertical. Vertical C4 constraints are context-scoped and may narrow admissibility. Where a canonical assertion participates in several relevant contexts, incompatible required constraints cannot be silently overridden. External occurrence or evidence, canonical domain assertion or state, admissibility of a canonical operation, and optional derived or advisory information are distinct. The claim that every real-world fact must be recorded with a finding is rejected: ADR-0040 is the counterexample, because real unsigned-but-binding transactions may exist while collectible obligations are still refused under the model. No generic durable compliance-finding primitive is approved.

F. C5 contract. The resulting core fact has one canonical semantic owner and one authoritative determination path. Multiple rules or inputs may participate only through explicit composition semantics such as composition, cap, floor or precedence. Independent competing writers of the same canonical fact are not allowed. A vertical term is not itself money owed. C5 may consume vertical terms, core facts and C6 reference data; the resulting obligation, entitlement or money truth remains in its proper core owner. Historical explanation must retain sufficient attribution to the applicable rule, regime and reference data; NG-4 does not select the exact persistence record for that attribution. Build-97 evidence is preserved: commission calculation takes one MonetaryObligation amount as its basis, and SpecificTerm currently requires and stores a term-code qualifier that does not select the monetary basis in the commission kernel.

G. C6 contract. The external source is authority for its published content and effective period. AgencyOS may hold a faithful ingested, versioned representation with provenance. The vertical rule owns its interpretation and use. The resulting canonical domain fact remains owned by core. Exact ingestion infrastructure is not selected.

H. Client / API / query contract. Clients and projections must not become competing definition authorities. Definitions needed for rendering, selection or querying are published as descriptors. Unknown definitions must remain distinguishable from absence and must not be guessed. Core and client code may understand only neutral metadata actually required by neutral capabilities, such as typed value shape, money/currency and security/economic classification where those capabilities consume them; no generic metadata/tag ontology is created. Build-97 hard-coded client vocabulary and enum-name API coupling are implementation leakage, not the future architecture. Exact future API payloads are deferred.

I. Cross-vertical composition. There is no single VerticalId on a represented subject, mandate, arrangement or legal instrument. One shared fact may participate in several vertical contexts. Shared identities and money remain single-owner facts. Similar labels in different verticals do not imply semantic identity. Cross-vertical reports consume shared core truth and enrich it with vertical semantics when definitions are understood.

J. Deployment. Under present evidence AgencyOS remains a compiled modular monolith with bounded semantic contexts. A bounded context does not imply a separate process or database. Runtime plugins and dynamic loading are not justified by current evidence; this does not claim they can never be justified by future independent-release or runtime requirements.

K. Film/TV reference-vertical proof. Film/TV survives decontamination. Film/TV-specific concepts — DealKind members; Film/TV term codes and units; Project, Package and ProjectRole semantics; the SourceProperty description; subject requirements; party-role vocabulary; rights vocabularies; vertical obligation categories; vertical deadline anchors — do not need to define neutral-core vocabulary. They can be represented through C1–C6 and role-local bridges while preserving one shared arrangement, legal, economic and cash truth. SourceProperty(Book) is not proven to be the same canonical identity as a future Literary Work; a future Literary Work may remain literary-owned and be referenced through an explicit bridge; promotion of Work identity to core requires separate evidence that the identity or lifecycle itself is neutral shared truth.

L. Correction chain. Rejected: central vertical enums as the universal extension point; one VerticalId per core aggregate; vertical-specific duplicate Deal, Contract or Payment truth; generic JSON/property bags; global Entity, Party or SubjectId; a generic Transaction; a universal rights ontology; a mega lifecycle; a universal registry owning C1–C6; a universal rules-as-data behavioural engine; mandatory separate-process bounded contexts; a runtime plugin framework under current evidence; K4-versus-K5 as a code-versus-data architecture fork; pure vertical-owned C2 instances; "record every real event with a non-blocking finding"; unrestricted or self-registering C3 kind contribution; zero-core-touch extensibility as a required property. Survives locally: bounded semantic contexts inside the monolith; typed C1/C2 descriptor catalogues; runtime publication of descriptors; vertical-owned C3 objects; explicit role-local typed bridges; typed neutral comparison/reconciliation capabilities; evidence-triggered future promotion from vertical to core.

Scope: Conceptual next-generation architecture only. It does **not** implement the design, and it gives no approval for schema, API, domain expansion, storage mappings, migrations or product code. NG-5 stays unauthorized. It supersedes none of NG-1, NG-2 or NG-3, and Operational Closure is not reopened.

Evidence / provenance:
- The published governing decisions `DECISION-20260928-001` to `-004` and `DECISION-20260929-001` in `docs/control-room/DECISIONS.md`.
- Build-97 source evidence at product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`; `src/` and `docs/adr/` are byte-identical at canonical HEAD `85918d6680f4d5c432827d080a01ccf8fc3f646e`.
- The bounded NG-4 source/domain falsification chain (NG-4A to NG-4E/F), adjudicated by the Control Room and recorded in `DELTA-20260929-004`.
- Claude research reports were evidence inputs only. The decision is the Control Room's.

Consequences:
- NG-4 may be marked CLOSED once `DELTA-20260929-004` reaches PUBLISHED.
- Future vertical design must preserve this boundary.
- Any implementation or schema/API/domain expansion still requires separate future authority.
- NG-5 remains unauthorized and not begun.

Supersedes: None

Unchanged:
- All governing NG-1 to NG-3 decisions, `DECISION-20260928-001` to `-004`.
- `DECISION-20260929-001`'s bounded authorization.
- Build 97 product identity.
- Operational Closure.
- Self-Update V1.
- The deferred questions listed under Open.

Open:
- A newly referenceable C3 kind may require explicit reviewed expansion of a shared composition contract.
- Exact persistence, schema, API, interface and module mechanics remain deferred.
- Cross-vertical mandate-lineage reporting depends on the already-deferred commercial ↔ mandate cardinality.
- Work/IP promotion remains unresolved until its identity/lifecycle earns neutral-core status.
- Film/TV-specific guild schedule content was not directly researched; the C6 architecture itself was sufficiently established by bounded cross-vertical evidence.
- Previously deferred core questions remain deferred: persisted vs derived umbrella Representation; qualifying clienthood; delegated/sub-agency authority topology; exact commission placement/model; Company ↔ External Organization implementation; Group persistence; successor/predecessor semantics; Person reconciliation; exact subject storage/retention mechanics; commercial ↔ mandate cardinality; agreement snapshot; exact amount-determination record; multi-arrangement legal instruments; scope/exclusivity/territory placement; ledger integration.
- None of these blocks NG-4 architecture closure.

Recorded by: Claude (AgencyOS executor), on Control Room instruction. Claude is the recorder, not the authority.

Publication receipt: a715e511cfa43dfc00a349f844d81ab6f7402314 on origin/operational-regression-gate; remote readback verified 2026-09-29T18:48:47Z

## DECISION-20260929-003

Status: ACTIVE

Date: 2026-09-29

Authority: OWNER

Question: Should the next post-NG-4 stage, including NG-5 research/design, be authorized to begin, while implementation and product/schema/API/domain expansion remain unauthorized?

Decision: The Owner explicitly approves beginning **NG-5 research/design** as the next post-NG-4 stage.

Owner response: “לאשר.”

- The authorization is research/design only.
- NG-5's exact canonical title and bounded substantive question have not yet been established.
- The Control Room must establish that bounded question before issuing substantive NG-5 Claude research work.
- No implementation follows from this authorization.

Scope:
- Authorization to begin NG-5 research/design only.
- The NG-4 architecture is not modified, and NG-4 is not reopened or implemented.
- No schema, API or domain expansion, no product code, no migrations and no NG-6 or later stage is approved by this decision.

Evidence / provenance:
- The Owner's explicit approval on 2026-09-29. The Control Room asked: “האם לאשר התחלה של שלב post-NG-4 הבא, כולל NG-5 research/design, או להשאירו בלתי מורשה?” The Owner replied: “לאשר.”
- `docs/control-room/CURRENT-STATE.md` at `66af408aced50ccd14bfc913f0fe81754b72864b`, whose exact next bounded action requires explicit Owner authorization before beginning any post-NG-4 stage, including NG-5.
- `DECISION-20260929-002` and `DELTA-20260929-004`, which canonically closed NG-4 while leaving NG-5 unauthorized.
- Claude is the recorder/executor only and is not a decision authority.

Consequences:
- Once the accompanying delta is PUBLISHED, NG-5 research/design may begin.
- The Control Room may define the exact bounded NG-5 research question and then issue research prompts under this authorization.
- Implementation remains separately unauthorized.

Supersedes: None

Unchanged:
- Operational Closure remains terminal.
- Self-Update V1 remains terminal.
- NG-1 through NG-4 remain CLOSED as already published.
- `DECISION-20260929-002` remains the governing NG-4 architecture decision.
- All NG-4 residual/deferred questions remain as published.
- Build 97 product identity remains unchanged.
- No product, schema, API, domain or runtime behaviour changes.

Open:
- The exact NG-5 title.
- The exact bounded NG-5 substantive research/design question.
- The prompt budget and substages for NG-5.
- All questions already left deferred by NG-4.

Recorded by: Claude (AgencyOS executor), on explicit Owner instruction normalized by the Control Room. Claude is the recorder, not the decision authority.

Publication receipt: 37c90ad75b8795d140af4bb70cc90791957cf69f on origin/operational-regression-gate; remote readback verified 2026-09-29T19:17:36Z

## DECISION-20260929-004

Status: SUPERSEDED by DECISION-20260929-005

Date: 2026-09-29

Authority: CONTROL_ROOM

Question: What is the minimum neutral-core semantic lineage contract between Representation Mandate(s) and Commercial Arrangement(s), including allowed cardinality, temporal/historical anchoring, the semantics of a commercial fact for which no valid mandate can be established, and whether optional pre-arrangement commercial facts need independent mandate lineage of their own?

Decision: NG-5 — Mandate–Commercial Lineage Architecture is terminally accepted as a conceptual architecture stage:
1. The neutral-core mandate-to-commercial-arrangement contract is **ZERO OR ONE**. Each Commercial Arrangement has conceptual direct lineage to zero or one Representation Mandate. From the other direction, one Representation Mandate may govern or provide lineage for any number of Commercial Arrangements. This is a semantic architecture decision only; it does not select a database relation, FK, persistence strategy, API shape or implementation mechanism.
2. When a valid Representation Mandate is established for a Commercial Arrangement, the lineage is to the specific historical Mandate relevant to that Arrangement's authority provenance. It must not be silently re-derived from the current Representation/Mandate, from represented-subject identity alone, or from a later successor relationship. A later mandate does not rewrite the historical lineage of an earlier commercial fact.
3. Zero mandate lineage is a legitimate Commercial Arrangement state. AgencyOS must be able to record a real commercial fact without fabricating agency authority. Semantic absence must distinguish an affirmative conclusion that no valid mandate is established for the fact from authority provenance that is unknown, unresolved or not yet established. This decision does not fix final enum names or persistence mechanics.
4. Market Activity / Opportunity / Proposal / Offer or another pre-arrangement commercial-negotiation fact may carry its own zero-or-one Representation Mandate lineage when the authority provenance of that fact is independently material. Such lineage is fact-local. It is not automatically inherited from a later Commercial Arrangement, and a later Arrangement must not retroactively rewrite an earlier fact's mandate provenance. The capability is optional: NG-5 does not make Opportunity or Proposal universal mandatory parents.
5. Successive representation must preserve historical provenance. If commercial activity occurred under Mandate A and a later commercial fact occurred under Mandate B, each fact remains anchored to the mandate relevant to that fact. Post-termination commission rights, tail commissions, renewal commissions, management commissions or other economic consequences do not by themselves create an additional Commercial-Arrangement mandate lineage. Representation authority remains distinct from economic entitlement under `DECISION-20260928-001` and `DECISION-20260928-004`.
6. Multiple represented principals, several agents around one project, or one legal instrument containing several performers do not by themselves justify several Representation Mandates on one Commercial Arrangement. A legal instrument may cover several Commercial Arrangements; that question remains mechanically deferred from NG-3. A multi-client project or multi-musician instrument therefore must not be used as automatic justification for a raw many-to-many mandate/arrangement model.
7. A several-mandate, role-typed Commercial Arrangement relation is NOT justified in the present neutral core. It is reconsidered only if future concrete evidence proves a case in which: one canonical Commercial Arrangement remains indivisible after valid semantic decomposition; the apparent multiplicity is not merely several represented principals, several arrangements under one instrument, transaction participation, legal-party structure or economic-entitlement provenance; and limiting the Arrangement to one mandate lineage would lose materially correct authority provenance. That is the explicit H3 reconsideration trigger.
8. Hypothesis disposition:
   - H0 — derive-only / no direct Commercial-Arrangement mandate relation: REJECTED.
   - H1 — exactly one mandate lineage for every Commercial Arrangement: REJECTED.
   - H2 — zero or one mandate lineage: ACCEPTED as the current minimum neutral-core contract.
   - H3 — several role-differentiated mandate lineages: NOT JUSTIFIED NOW; retained only as the reconsideration case described in 7.
9. The deferred items listed under Open are not decided by NG-5.
10. No product code, migrations, schema/API/domain expansion, repair of Build-97 RepresentationId/FK weaknesses, commission redesign, generic Entity/Party/SubjectId/Transaction abstraction, universal JSON/property bags, universal role vocabularies, universal rules engine, runtime plugins or NG-6 work is approved by this decision.

Scope: Conceptual next-generation mandate/commercial lineage architecture only. It does **not** implement the design, select persistence, schema, API or interface mechanics, or approve product code, migrations or NG-6.

Evidence / provenance:
- `DECISION-20260928-001`: Representation Mandate is effective-dated bounded authority and remains distinct from transaction participation and economic entitlement.
- `DECISION-20260928-002` and `DECISION-20260928-003`: the represented-subject family and typed subject reference boundary.
- `DECISION-20260928-004`: the commercial model is a hybrid graph; Opportunity is optional; commercial activity requires conceptual mandate lineage; the exact commercial-to-mandate cardinality was explicitly left open.
- `DECISION-20260929-002`: the NG-4 vertical extension architecture.
- `DECISION-20260929-003`: the Owner authorization for NG-5 research/design only.
- Build-97 source baseline at product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2` and the NG-5A source inspection: Build 97 does not provide enforceable ordinary commercial-chain mandate lineage, while downstream finance carries caller-supplied RepresentationId values without proving the future semantic contract.
- WGA Franchise Agreement and Rider W (2021 forms), https://www.wga.org/uploadedfiles/employers_agents/agencies/franchise-agreement-2021.pdf and https://www.wga.org/uploadedfiles/employers_agents/agencies/rider-w-2021.pdf: Rider W §3.C.1, §3.C.2.b, §3.C.4 and §3.C.5 on post-termination contracts, renewals and improvements; Franchise Agreement §3.B.5.a–c on concurrent representation and project contexts.
- American Federation of Musicians Booking Agent Agreement, https://www.afm.org/wp-content/uploads/2019/09/AFM-Booking-Agent-Agreement.pdf: §6(d) on effectiveness of exclusive retaining arrangements; §12(a) on rebooking attribution; §13(d) on termination of representational authority while preserving specified prior economic consequences; Schedule 1(A)(III) on the separate approved Personal Management Agreement commission.
- AFM Form L-1, https://nashvillemusicians.org/sites/default/files/AFM%20L1.pdf: the multi-musician contract structure and clause 7 concerning participating musicians and their agent or agents.
- Marathon Entertainment, Inc. v. Blasi, 42 Cal.4th 974 (2008), used narrowly for the distinction between real entertainment/employment facts and unlawful/unlicensed procurement; it is not a ruling about AgencyOS arrangement cardinality.
- Control Room corrections to the NG-5B research report: commission-tail evidence proves historical/economic provenance but must not be conflated with multiple arrangement-authority lineages; AFM booking plus personal-management commission does not prove that one Commercial Arrangement requires two mandate lineages; a multi-musician L-1 instrument does not prove one multi-mandate Commercial Arrangement because one instrument may cover several arrangements; WGA §3.B.5.c does not itself prove that the same principal simultaneously crossed writer and rights-holder authority scopes in one event; H2 being unfalsified after valid decomposition is the reason it is the minimum accepted neutral-core contract, not a reason to expand to H3.
- Claude research reports were evidence inputs only. The decision is the Control Room's.

Consequences:
- NG-5 is CLOSED once `DELTA-20260929-006` reaches PUBLISHED.
- The exact commercial ↔ mandate cardinality is no longer an open question.
- H3 has only the explicit reconsideration trigger in Decision item 7.

Supersedes: None

Unchanged:
- Build 97 and all implementation behavior.
- NG-1 through NG-4 and their decisions, `DECISION-20260928-001` to `-004` and `DECISION-20260929-002`.
- `DECISION-20260929-003` as the Owner authorization that allowed NG-5 research/design.
- All deferred questions listed under Open.

Open:
- Persisted versus derived umbrella Representation.
- Mandate scope/exclusivity/territory placement mechanics.
- Delegated/sub-agency topology.
- Exact persistence/schema/API/interface mapping.
- Exact agreement-snapshot representation.
- Exact amount-determination record.
- Exact multi-arrangement-instrument implementation.
- Commission model and exact commission placement.
- Ledger integration.
- Company ↔ External Organization implementation.
- Group persistence.
- Successor mechanics beyond the historical-lineage invariant already decided.
- NG-6 title, scope or authorization.

Recorded by: Claude (AgencyOS executor), on Control Room instruction. Claude is the recorder/executor only, not the authority.

Publication receipt: 443624084ea3a6d3b33f49d15b5549b5350b46dc on origin/operational-regression-gate; remote readback verified 2026-09-29T20:44:33Z

## DECISION-20260929-005

Status: ACTIVE

Date: 2026-09-29

Authority: CONTROL_ROOM

Question: What is the minimum neutral-core semantic lineage contract between Representation Mandate(s) and Commercial Arrangement(s), including allowed cardinality, lineage roles where more than one mandate participates, temporal/historical anchoring, and the semantics of a commercial fact for which no valid mandate can be established; and, only where necessary to preserve pre-arrangement authority provenance, must optional Market Activity / Opportunity / Proposal context carry direct mandate lineage of its own?

Decision: NG-5 — Mandate–Commercial Lineage Architecture is terminally accepted as a conceptual architecture stage:
1. The neutral-core mandate-to-commercial-arrangement contract is **ZERO OR ONE**. Each Commercial Arrangement has conceptual direct lineage to zero or one Representation Mandate. From the other direction, one Representation Mandate may govern or provide lineage for any number of Commercial Arrangements. This is a semantic architecture decision only; it does not select a database relation, FK, persistence strategy, API shape or implementation mechanism.
2. When a valid Representation Mandate is established for a Commercial Arrangement, the lineage is to the specific historical Mandate relevant to that Arrangement's authority provenance. It must not be silently re-derived from the current Representation/Mandate, from represented-subject identity alone, or from a later successor relationship. A later mandate does not rewrite the historical lineage of an earlier commercial fact.
3. Zero mandate lineage is a legitimate Commercial Arrangement state. AgencyOS must be able to record a real commercial fact without fabricating agency authority. Semantic absence must distinguish an affirmative conclusion that no valid mandate is established for the fact from authority provenance that is unknown, unresolved or not yet established. This decision does not fix final enum names or persistence mechanics.
4. Market Activity / Opportunity / Proposal / Offer or another pre-arrangement commercial-negotiation fact may carry its own zero-or-one Representation Mandate lineage when the authority provenance of that fact is independently material. Such lineage is fact-local. It is not automatically inherited from a later Commercial Arrangement, and a later Arrangement must not retroactively rewrite an earlier fact's mandate provenance. The capability is optional: NG-5 does not make Opportunity or Proposal universal mandatory parents.
5. Successive representation must preserve historical provenance. If commercial activity occurred under Mandate A and a later commercial fact occurred under Mandate B, each fact remains anchored to the mandate relevant to that fact. Post-termination commission rights, tail commissions, renewal commissions, management commissions or other economic consequences do not by themselves create an additional Commercial-Arrangement mandate lineage. Representation authority remains distinct from economic entitlement under `DECISION-20260928-001` and `DECISION-20260928-004`.
6. Multiple represented principals, several agents around one project, or one legal instrument containing several performers do not by themselves justify several Representation Mandates on one Commercial Arrangement. A legal instrument may cover several Commercial Arrangements; that question remains mechanically deferred from NG-3. A multi-client project or multi-musician instrument therefore must not be used as automatic justification for a raw many-to-many mandate/arrangement model.
7. A several-mandate, role-typed Commercial Arrangement relation is NOT justified in the present neutral core. It is reconsidered only if future concrete evidence proves a case in which: one canonical Commercial Arrangement remains indivisible after valid semantic decomposition; the apparent multiplicity is not merely several represented principals, several arrangements under one instrument, transaction participation, legal-party structure or economic-entitlement provenance; and limiting the Arrangement to one mandate lineage would lose materially correct authority provenance. That is the explicit H3 reconsideration trigger.
8. Hypothesis disposition:
   - H0 — derive-only / no direct Commercial-Arrangement mandate relation: REJECTED.
   - H1 — exactly one mandate lineage for every Commercial Arrangement: REJECTED.
   - H2 — zero or one mandate lineage: ACCEPTED as the current minimum neutral-core contract.
   - H3 — several role-differentiated mandate lineages: NOT JUSTIFIED NOW; retained only as the reconsideration case described in 7.
9. The deferred items listed under Open are not decided by NG-5.
10. No product code, migrations, schema/API/domain expansion, repair of Build-97 RepresentationId/FK weaknesses, commission redesign, generic Entity/Party/SubjectId/Transaction abstraction, universal JSON/property bags, universal role vocabularies, universal rules engine, runtime plugins or NG-6 work is approved by this decision.

Scope: The same conceptual NG-5 mandate/commercial lineage architecture only. This correction changes durable question/provenance fidelity, not architecture. It does **not** implement the design, select persistence, schema, API or interface mechanics, or approve product code, migrations or NG-6.

Evidence / provenance:
- Correction chain: `DECISION-20260929-004` carried the correct terminal architecture, but its Question field was a shortened normalization of the locked Scope-Lock question: it omitted the clause on lineage roles where more than one mandate participates and weakened the precursor-lineage qualifier.
- The Control Room identified that publication-record defect before treating NG-5 closure as terminally clean.
- Publisher V1.1 does not permit changing the bytes of a pushed staged decision through a replacement basis, so the correction is preserved through this new decision and `DELTA-20260929-007` rather than by rewriting history.
- All NG-5A/NG-5B evidence and the substantive architecture adjudication recorded in `DECISION-20260929-004` remain unchanged; that evidence is restated below.
- `DECISION-20260928-001`: Representation Mandate is effective-dated bounded authority and remains distinct from transaction participation and economic entitlement.
- `DECISION-20260928-002` and `DECISION-20260928-003`: the represented-subject family and typed subject reference boundary.
- `DECISION-20260928-004`: the commercial model is a hybrid graph; Opportunity is optional; commercial activity requires conceptual mandate lineage; the exact commercial-to-mandate cardinality was explicitly left open.
- `DECISION-20260929-002`: the NG-4 vertical extension architecture.
- `DECISION-20260929-003`: the Owner authorization for NG-5 research/design only.
- Build-97 source baseline at product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2` and the NG-5A source inspection: Build 97 does not provide enforceable ordinary commercial-chain mandate lineage, while downstream finance carries caller-supplied RepresentationId values without proving the future semantic contract.
- WGA Franchise Agreement and Rider W (2021 forms), https://www.wga.org/uploadedfiles/employers_agents/agencies/franchise-agreement-2021.pdf and https://www.wga.org/uploadedfiles/employers_agents/agencies/rider-w-2021.pdf: Rider W §3.C.1, §3.C.2.b, §3.C.4 and §3.C.5 on post-termination contracts, renewals and improvements; Franchise Agreement §3.B.5.a–c on concurrent representation and project contexts.
- American Federation of Musicians Booking Agent Agreement, https://www.afm.org/wp-content/uploads/2019/09/AFM-Booking-Agent-Agreement.pdf: §6(d) on effectiveness of exclusive retaining arrangements; §12(a) on rebooking attribution; §13(d) on termination of representational authority while preserving specified prior economic consequences; Schedule 1(A)(III) on the separate approved Personal Management Agreement commission.
- AFM Form L-1, https://nashvillemusicians.org/sites/default/files/AFM%20L1.pdf: the multi-musician contract structure and clause 7 concerning participating musicians and their agent or agents.
- Marathon Entertainment, Inc. v. Blasi, 42 Cal.4th 974 (2008), used narrowly for the distinction between real entertainment/employment facts and unlawful/unlicensed procurement; it is not a ruling about AgencyOS arrangement cardinality.
- Control Room corrections to the NG-5B research report: commission-tail evidence proves historical/economic provenance but must not be conflated with multiple arrangement-authority lineages; AFM booking plus personal-management commission does not prove that one Commercial Arrangement requires two mandate lineages; a multi-musician L-1 instrument does not prove one multi-mandate Commercial Arrangement because one instrument may cover several arrangements; WGA §3.B.5.c does not itself prove that the same principal simultaneously crossed writer and rights-holder authority scopes in one event; H2 being unfalsified after valid decomposition is the reason it is the minimum accepted neutral-core contract, not a reason to expand to H3.
- Claude research reports were evidence inputs only. The decision is the Control Room's.

Consequences:
- NG-5 remains CLOSED.
- No architecture conclusion changes.
- `DECISION-20260929-005` becomes the governing durable NG-5 decision after publication.
- The exact commercial ↔ mandate cardinality remains closed.
- H3 retains exactly the same reconsideration trigger, Decision item 7.

Supersedes: DECISION-20260929-004

Unchanged:
- The complete semantic Decision of `DECISION-20260929-004`, restated above without change of meaning.
- Build 97 and all implementation behavior.
- NG-1 through NG-4 and their decisions, `DECISION-20260928-001` to `-004` and `DECISION-20260929-002`.
- `DECISION-20260929-003` as the Owner authorization that allowed NG-5 research/design.
- All deferred questions listed under Open.
- No product implementation.
- No schema, API or domain expansion.
- No NG-6 authorization.

Open:
- Persisted versus derived umbrella Representation.
- Mandate scope/exclusivity/territory placement mechanics.
- Delegated/sub-agency topology.
- Exact persistence/schema/API/interface mapping.
- Exact agreement-snapshot representation.
- Exact amount-determination record.
- Exact multi-arrangement-instrument implementation.
- Commission model and exact commission placement.
- Ledger integration.
- Company ↔ External Organization implementation.
- Group persistence.
- Successor mechanics beyond the historical-lineage invariant already decided.
- NG-6 title, scope or authorization.

Recorded by: Claude (AgencyOS executor), on Control Room instruction. Claude is the recorder/executor only, not the authority.

Publication receipt: 353961b2097be8429b308d103215f52c6db1af63 on origin/operational-regression-gate; remote readback verified 2026-09-29T21:20:43Z

## DECISION-20260929-006

Status: ACTIVE

Date: 2026-09-29

Authority: OWNER

Question: Should AgencyOS begin NG-6 — Commercial Arrangement–Legal Instrument Composition Architecture for research/design only, while implementation and product/schema/API/domain expansion remain unauthorized?

Decision: The Owner explicitly approves beginning **NG-6 — Commercial Arrangement–Legal Instrument Composition Architecture for research/design only**.

Owner response: “מאשר.”

The response is normalized only in the context in which it was given: the Control Room had asked whether the Owner approved defining NG-6 around Commercial Arrangement–Legal Instrument Composition Architecture for research/design only. The Owner's accompanying request about when to send a new-chat handoff is not additional semantic product authority.

Scope:
- Authorizes NG-6 research and design only after this decision and its accompanying canonical transition are fully PUBLISHED.
- Allows bounded evidence gathering, canonical/source inspection, primary-source domain research, falsification and conceptual architecture analysis within the Control Room Scope Lock recorded in `DECISION-20260929-007`.
- Does not authorize implementation.
- Does not authorize product-code changes.
- Does not authorize schema, API or domain expansion.
- Does not authorize migrations.
- NG-7 and any later stage remain unauthorized.
- Does not pre-approve the eventual NG-6 architecture conclusion.
- Any genuine Owner-reserved product/strategy ambiguity discovered by NG-6 returns to the Owner.

Evidence / provenance:
- Explicit Owner approval in the Control Room conversation on 2026-09-29.
- The immediately preceding Control Room question asked whether the Owner approved defining NG-6 around Commercial Arrangement–Legal Instrument Composition Architecture for research/design only.
- The Owner replied: “מאשר.”
- `docs/control-room/CURRENT-STATE.md` at `bd52078ec030cfab5399c3f18cc4a24104195b60` states that explicit Owner authorization is required before any post-NG-5 stage, including NG-6.
- `DECISION-20260929-005` and `DELTA-20260929-007` canonically close NG-5 and leave NG-6 unauthorized.
- Claude is recorder/executor only and is not decision authority.

Consequences:
- Once the accompanying delta is PUBLISHED, NG-6 research/design may begin under `DECISION-20260929-007`'s exact Scope Lock.
- No implementation authority follows.
- The first bounded substantive work is NG-6A — Canonical & Build-97 Legal-Composition Baseline.

Supersedes: None

Unchanged:
- Operational Closure remains terminal.
- Self-Update V1 remains terminal.
- NG-1 through NG-5 remain CLOSED.
- `DECISION-20260929-005` remains the governing NG-5 architecture decision.
- Build 97 remains the released product identity.
- No product behavior changes.
- No schema/API/domain expansion.
- No implementation.
- All questions outside the NG-6 Scope Lock remain deferred.
- NG-7 remains unauthorized.

Open:
- The final NG-6 architecture conclusion.
- Any genuine Owner-reserved ambiguity found by the bounded research.
- All questions explicitly excluded by `DECISION-20260929-007`.

Recorded by: Claude (AgencyOS executor), on explicit Owner instruction normalized by the Control Room. Claude is recorder/executor only, not authority.

Publication receipt: 575ad86afea73d962c51a0348bfe774dd0f643e4 on origin/operational-regression-gate; remote readback verified 2026-09-29T22:33:04Z

## DECISION-20260929-007

Status: ACTIVE

Date: 2026-09-29

Authority: CONTROL_ROOM

Question: What exact bounded research/design question, falsification boundary and exit criteria govern NG-6 under DECISION-20260929-006?

Decision: NG-6 is scope-locked as:

**NG-6 — Commercial Arrangement–Legal Instrument Composition Architecture**

**BOUND QUESTION**

What is the minimum neutral-core semantic contract among Commercial Arrangement(s), Agreement Snapshot(s), and Legal Instrument(s), including whether a Commercial Arrangement may exist before or without a governing Legal Instrument; the allowed Arrangement↔Legal Instrument cardinality in both directions, including amendment, replacement and supplemental instruments and one instrument covering more than one Arrangement; the temporal/historical anchoring required so later instruments do not silently rewrite earlier commercial or legal truth; and what Agreement Snapshot canonically represents without collapsing it into proposal/offer, Legal Instrument, rights-grant, economic-obligation or payment truth?

**IN SCOPE**

1. The semantic distinction among:
   - Commercial Arrangement;
   - Agreement Snapshot;
   - Legal Instrument.
2. Whether a Commercial Arrangement may exist:
   - before a governing Legal Instrument exists;
   - while an instrument is unexecuted/incomplete;
   - or without a formal governing instrument.
3. Arrangement ↔ Legal Instrument cardinality in BOTH directions.
4. One Commercial Arrangement affected over time by:
   - an original instrument;
   - amendment;
   - restatement;
   - replacement;
   - supplemental instrument;
   - side letter, rider or schedule where evidence shows it has independent legal significance.
5. One Legal Instrument covering more than one apparent Commercial Arrangement.
6. The decomposition question: when one real-world document/event appears to contain several commercial relationships, determine whether neutral truth is:
   - one Arrangement;
   - several related Arrangements;
   - or underdetermined by current evidence.
7. Historical/temporal anchoring sufficient to prevent:
   - a later amendment from silently rewriting prior commercial truth;
   - a replacement instrument from erasing prior legal truth;
   - current legal state from being substituted for historical state.
8. The semantic role of Agreement Snapshot:
   - what commercial truth it represents;
   - whether it is canonical truth or only a derived presentation;
   - what historical meaning must remain resolvable;
   - without choosing persistence or aggregate shape.
9. Rights grants, options, obligations and economic facts only as boundary tests necessary to prove they remain distinct from Arrangement / Snapshot / Instrument truth.

**OUT OF SCOPE**

Do NOT solve or select:
- exact persistence;
- database tables or FKs;
- EF mappings;
- schema;
- API payloads;
- interfaces/classes/module layout;
- document storage;
- e-signature infrastructure;
- signature workflow;
- clause extraction;
- document AI;
- approval workflow;
- template-management architecture;
- a general legal-enforceability engine;
- jurisdiction-wide legal compliance architecture;
- a universal rights ontology;
- exact rights-grant implementation;
- amount-determination persistence;
- commission model or commission placement;
- receivables;
- invoices;
- payments;
- allocations;
- ledger/accounting integration;
- mandate scope/exclusivity/territory mechanics;
- delegated/sub-agency topology;
- Company ↔ External Organization implementation;
- Group persistence;
- product implementation;
- migrations;
- NG-7.

Do not reopen NG-1 through NG-5 absent genuine contradictory canonical evidence.

**GOVERNING DISTINCTIONS TO PRESERVE**

Preserve all previously closed distinctions:
- proposal/offer ≠ Commercial Arrangement;
- Agreement Snapshot ≠ Legal Instrument truth;
- Legal Instrument ≠ Rights Grant;
- legal/operational obligation ≠ receivable;
- payment ≠ allocation/application;
- ledger/accounting ≠ upstream commercial/legal truth;
- subject identity ≠ representation authority;
- representation authority ≠ economic entitlement;
- one real-world document ≠ automatically one Commercial Arrangement;
- several represented people ≠ automatically one multi-principal Arrangement;
- one Legal Instrument covering several facts does not automatically justify a generic many-to-many abstraction.

**FALSIFICATION MODELS**

Test these candidate models. Do not select a winner before evidence.
- L0 — COLLAPSE: Commercial Arrangement and Legal Instrument can be treated as the same semantic fact; Agreement Snapshot need not be independently meaningful.
- L1 — OPTIONAL ONE-TO-ONE: A Commercial Arrangement exists independently and may have zero or one governing Legal Instrument; each Legal Instrument belongs to exactly one Commercial Arrangement.
- L2 — ONE-ARRANGEMENT / MULTI-INSTRUMENT HISTORY: A Commercial Arrangement exists independently and may relate to multiple Legal Instruments over time, but every Legal Instrument belongs to exactly one Commercial Arrangement.
- L3 — COMPOSITION: A Commercial Arrangement may relate to multiple Legal Instruments, and a Legal Instrument may cover multiple Commercial Arrangements. If evidence supports this, determine what semantic differentiation/history is required and do not default to a raw many-to-many relation.

A model is not falsified merely because a more flexible model is convenient. Before accepting evidence for L3, challenge whether the correct neutral decomposition is several Arrangements under one Legal Instrument.

**AGREEMENT-SNAPSHOT AXIS**

Independently test:
- S0 — Agreement Snapshot is only a derived/presentation view and needs no independently meaningful canonical commercial semantics.
- S1 — Agreement Snapshot represents canonical agreed commercial terms distinct from Legal Instrument truth, but one current semantic state is sufficient.
- S2 — historical Agreement Snapshot semantics are required because commercial terms can change or be superseded without permitting later state to rewrite earlier commercial truth.

Do not interpret S1 or S2 as a storage/versioning decision.

**REQUIRED FALSIFICATION CASES**

Test, where evidence exists:
1. Arrangement before governing instrument.
2. Commercially or legally meaningful bargain without a formal executed instrument.
3. One Arrangement followed by amendment/restatement/replacement.
4. Main agreement plus side letter/rider/schedule with independently material legal effect.
5. One document/instrument covering several engagements, principals or apparent commercial arrangements.
6. Decomposition challenge for that multi-arrangement document.
7. Negotiated/agreed commercial terms differing from the later executed instrument.
8. A later instrument changing only part of the relationship while historical prior truth must remain explainable.
9. A superseded/terminated instrument whose historical relation must remain intact.
10. Rights/option/obligation provisions embedded in an instrument without making those downstream facts identical to the instrument itself.

**RESEARCH BOUNDARY**

Use exactly TWO vertical contexts for cross-vertical falsification:
- A. Film/TV representation — reference vertical.
- B. Live music / artist booking representation — contrast vertical.

Do not add a third vertical merely for confidence or breadth. If an important case remains unproved after those two, mark it UNPROVED or UNDERDETERMINED. Do not silently broaden the stage.

**BUILD-97 DISCIPLINE**

Pin Build-97 source evidence to `b3f41bfd68e81ab42da899671f58e01f0988d3d2`. Do not assume:
- Build-97 Deal == canonical Commercial Arrangement;
- Build-97 Contract == future canonical Legal Instrument;
- Build-97 ContractVersion/Offer/RightsGrant shapes, if present, are universal future architecture.

Inspect them as predecessor evidence only. Later product code is not Build-97 source evidence.

**SUBSTAGES**

- NG-6A — Canonical & Build-97 Legal-Composition Baseline: factual only; inspect governing decisions and Build-97 commercial/legal topology; identify what is persisted, derived, coupled or absent; no architecture verdict.
- NG-6B — Cross-Vertical Composition Falsification: Film/TV + live music/booking; primary-source evidence preferred; test L0–L3 and S0–S2; no implementation design.
- NG-6C — Control Room Architecture Adjudication: Control Room synthesizes the minimum surviving neutral-core semantic contract; challenge unnecessary generality; preserve all closed NG-1–NG-5 distinctions; decide CLOSED / genuine OWNER DECISION REQUIRED / one targeted residual correction.
- NG-6D — OPTIONAL ONLY IF NEEDED: at most one focused evidence prompt for a concrete unresolved contradiction; not automatic; no third-vertical census.

**PROMPT BUDGET**

Initial substantive research/design budget: approximately 3–4 Claude prompts. Publication/execution prompts do not count as substantive research prompts.

**OWNER-DECISION RULE**

Do not send ordinary architecture questions to the Owner merely because several implementation shapes are possible. Owner decision is required only if bounded evidence leaves genuine surviving product/strategy alternatives within Owner-reserved authority. A contradiction with a closed decision is a correction-chain event, not an Owner preference poll.

**EXIT CRITERIA**

NG-6 is READY TO CLOSE only when all are true:
1. Arrangement / Agreement Snapshot / Legal Instrument semantic boundaries are clear.
2. The semantics of an Arrangement before/without a governing instrument are clear.
3. Arrangement→Instrument cardinality is clear.
4. Instrument→Arrangement cardinality is clear.
5. Amendment/restatement/replacement history semantics are clear.
6. Multi-arrangement-instrument decomposition is clear enough to reject unexplained raw many-to-many modeling.
7. Agreement Snapshot's neutral-core semantic role is clear.
8. Historical truth cannot be silently rewritten by later instruments or snapshots.
9. Rights-grant and economic facts remain distinct.
10. The contract survives Film/TV and live-music evidence or limitations are explicitly bounded.
11. No unexplained conflict with NG-1 through NG-5 remains.
12. Exact persistence/schema/API/interface/storage mechanics remain unselected.
13. All unrelated deferred questions remain explicitly deferred.

NG-6 becomes CLOSED only after terminal Control Room adjudication is canonically PUBLISHED. Green tests, Claude confidence or a research report cannot close the stage.

Scope: Conceptual NG-6 research/design governance only. No implementation authority.

Evidence / provenance:
- `DECISION-20260928-004` established the hybrid commercial architecture and explicitly left open: exact Agreement Snapshot representation; one Legal Instrument covering several Arrangements.
- `DECISION-20260929-005` closed mandate-to-commercial lineage while preserving the legal-instrument composition question as deferred.
- `docs/control-room/CURRENT-STATE.md` at `bd52078ec030cfab5399c3f18cc4a24104195b60` still lists Agreement Snapshot and multi-arrangement instruments as open Commercial questions.
- `DECISION-20260929-006` supplies Owner authority for NG-6 research/design.
- The Control Room established this bounded Scope Lock before any substantive NG-6 research.
- No new external/domain research was performed in creating this Scope Lock.

Consequences:
- Once `DELTA-20260929-008` is fully PUBLISHED, NG-6A may begin.
- The Scope Lock must not be reopened merely because research reveals interesting adjacent questions.
- Adjacent defects/questions are recorded but do not broaden NG-6 automatically.
- No implementation follows.

Supersedes: None

Unchanged:
- NG-1 through NG-5 architecture decisions.
- Operational Closure.
- Self-Update V1.
- Build 97 product identity and behavior.
- All unrelated deferred questions.
- No product/schema/API/domain/migration authority.
- NG-7 remains unauthorized.

Open:
- The evidence and final adjudication of L0–L3.
- The evidence and final adjudication of S0–S2.
- Any genuinely underdetermined case within the locked question.
- The exact implementation mechanics, which remain deferred even after NG-6 architecture closure.

Recorded by: Claude (AgencyOS executor), on Control Room instruction. Claude is recorder/executor only, not authority.

Publication receipt: 575ad86afea73d962c51a0348bfe774dd0f643e4 on origin/operational-regression-gate; remote readback verified 2026-09-29T22:33:04Z

## DECISION-20260929-008

Status: ACTIVE

Date: 2026-09-29

Authority: CONTROL_ROOM

Question: What is the minimum neutral-core semantic contract among Commercial Arrangement(s), Agreement Snapshot(s), and Legal Instrument(s), including whether a Commercial Arrangement may exist before or without a governing Legal Instrument; the allowed Arrangement↔Legal Instrument cardinality in both directions, including amendment, replacement and supplemental instruments and one instrument covering more than one Arrangement; the temporal/historical anchoring required so later instruments do not silently rewrite earlier commercial or legal truth; and what Agreement Snapshot canonically represents without collapsing it into proposal/offer, Legal Instrument, rights-grant, economic-obligation or payment truth?

Decision: NG-6 — Commercial Arrangement–Legal Instrument Composition Architecture is terminally accepted as a conceptual architecture stage:

1. **Semantic boundaries.**
   - Commercial Arrangement is the durable commercial bargain/relationship: the commercial truth about what the parties have agreed to do, provide or receive. It is distinct from negotiation, Proposal/Offer and Legal Instrument.
   - Agreement Snapshot is the Arrangement-scoped historical semantic state answering: "What commercial terms were agreed for this Commercial Arrangement at this historical agreement point?"
   - An Agreement Snapshot represents the agreed commercial outcome, not the proposal that preceded it. A Proposal/Offer may be evidence or provenance for an agreement, but Proposal/Offer is not the Agreement Snapshot and is not the Commercial Arrangement.
   - Legal Instrument is distinct legal/instrument truth: an instrument that records, governs, memorializes, amends, restates, replaces, supplements or otherwise legally expresses relevant terms. Its content, execution/effectiveness and history are not identical to commercial-agreement truth.
   - Agreement Snapshot is distinct from Legal Instrument. Legal Instrument is distinct from Rights Grant.
   - Rights, options, legal/operational obligations and economic facts remain separate downstream facts even when an instrument is their source or evidence.
2. **Commercial Arrangement without Legal Instrument.** The neutral core permits a real Commercial Arrangement to exist with zero governing Legal Instruments. This permits:
   - a Commercial Arrangement before paper exists;
   - an agreed-but-unpapered Arrangement;
   - an Arrangement for which no governing Legal Instrument is ever established in AgencyOS.

   This is a recordability/domain-truth rule, not a legal-enforceability finding. A vertical may impose stricter paper requirements, but the neutral core must not fabricate a Legal Instrument or reject true commercial agreement merely because no paper exists. Therefore the neutral-core Commercial Arrangement → Legal Instrument cardinality is **ZERO OR MORE**.
3. **Multiple Legal Instruments for one Commercial Arrangement.** One Commercial Arrangement may relate historically to multiple Legal Instruments. Examples of semantic roles include:
   - initial short-form/deal memorandum;
   - long-form instrument;
   - separately material rider or supplemental instrument;
   - amendment;
   - restatement;
   - replacement/superseding instrument;
   - other independently material later instrument.

   A drafting revision of the same instrument is not by itself a new Legal Instrument. An amendment/restatement/replacement/supplement that has independent legal-instrument identity is not merely a later drafting version of the original instrument. Therefore L1 — optional one-to-one — is rejected.
4. **Legal Instrument → Commercial Arrangement cardinality.** The current minimum neutral core selects L2, not L3. Each Legal Instrument is arrangement-scoped to ONE Commercial Arrangement in the present neutral core.
   - Evidence did not prove a case in the two authorized verticals where one real-world Legal Instrument remains semantically indivisible as one instrument; valid commercial decomposition independently requires two or more Commercial Arrangements; and forcing either the Arrangements to merge or the Legal Instrument to split would make the recorded truth false rather than merely inconvenient.
   - A document containing multiple performers, dates, performances, services, roles, obligations, rights or economic facts does not by itself establish several Commercial Arrangements.
   - Before any future Instrument→multiple-Arrangement capability is admitted, valid semantic decomposition must be attempted.
   - L3 is NOT declared impossible. It is not justified in the present neutral core.
   - Explicit reconsideration trigger: reopen Legal Instrument → Commercial Arrangement multiplicity only if concrete future evidence proves that one real-world Legal Instrument remains indivisible as one legal instrument while valid semantic decomposition independently proves two or more Commercial Arrangements, and limiting that Legal Instrument to one Arrangement would make canonical commercial/legal truth false rather than merely inconvenient.
   - No unexplained raw Arrangement↔Legal Instrument many-to-many relation is approved.
5. **Agreement Snapshot — historical semantics.** The neutral-core semantic contract is S2.
   - Agreement Snapshot has canonical historical meaning: later commercial agreement, renegotiation or paper must not silently rewrite what was commercially agreed at an earlier point.
   - A Commercial Arrangement may therefore have multiple historical agreed states over time.
   - This is a semantic architecture decision only. It does NOT decide whether Agreement Snapshots are persisted records; derived deterministically from immutable facts/events; stored as versions; represented by event sourcing; or assigned a particular identifier.
   - A derived technical representation remains possible as long as it preserves the canonical historical answer.
   - S0 is rejected only insofar as it would make Agreement Snapshot a merely presentational concept with no canonical historical commercial meaning. NG-6 does not reject derivation as an implementation technique.
6. **Snapshot ↔ Legal Instrument historical anchoring.**
   - When it is known that a Legal Instrument memorialized, papered or implemented a particular historical agreed commercial state, that relationship must remain anchored to that historical Agreement Snapshot. It must not be dynamically re-derived from the Arrangement's later/current agreement state.
   - A later renegotiation must not repoint an earlier Legal Instrument to later agreed terms.
   - If AgencyOS does not know which historical agreed state an instrument corresponded to, it must preserve that fact as unknown/unresolved rather than infer the current state.
   - Exact persistence/linkage mechanics remain deferred.
7. **Commercial truth versus legal truth.** The neutral core defines no universal precedence rule saying either that Agreement Snapshot always controls Legal Instrument truth, or that Legal Instrument always controls prior commercial-agreement truth. Vertical rules, governing agreements and individual instruments may establish different precedence. AgencyOS must preserve the distinct commercial and legal facts and their historical relationship rather than collapse them or invent a universal winner.
8. **Legal-Instrument history.** A later amendment, restatement, replacement, rider, supplemental instrument or other later Legal Instrument does not erase the earlier Legal Instrument. The architecture must preserve enough historical semantics to explain:
   - which Legal Instrument existed at a historical point;
   - which later instrument related to or changed an earlier instrument;
   - which earlier facts remain historical facts after the later instrument.

   Typed relationships such as amendment, supersession, restatement or supplementation do not constitute a universal legal-effect engine. NG-6 does not decide clause-level effect, retroactivity, enforceability or jurisdictional interpretation.
9. **Closed prior architecture remains in force.** Preserved:
   - proposal/offer ≠ Commercial Arrangement;
   - Agreement Snapshot ≠ Legal Instrument;
   - Legal Instrument ≠ Rights Grant;
   - legal/operational obligation ≠ receivable;
   - payment ≠ allocation/application;
   - ledger/accounting ≠ upstream commercial/legal truth;
   - subject identity ≠ representation authority;
   - representation authority ≠ economic entitlement;
   - one real-world document ≠ automatically one Commercial Arrangement;
   - several represented people ≠ automatically one multi-principal Arrangement;
   - one instrument containing several facts ≠ automatically generic many-to-many.

   `DECISION-20260929-005` remains controlling: each Commercial Arrangement has zero-or-one conceptual direct lineage to a historical Representation Mandate; one Representation Mandate may lineage many Commercial Arrangements; legal-instrument composition does not reintroduce raw mandate multiplicity.
10. **Hypothesis disposition:**
    - L0 — COLLAPSE: REJECTED.
    - L1 — OPTIONAL ONE-TO-ONE: REJECTED.
    - L2 — ONE-ARRANGEMENT / MULTI-INSTRUMENT HISTORY: ACCEPTED as the current minimum neutral-core contract.
    - L3 — COMPOSITION / one Legal Instrument may cover several Commercial Arrangements: NOT JUSTIFIED in the present neutral core; the explicit evidence-triggered reconsideration rule in 4 is retained.
    - S0 — purely derived/presentation with no canonical historical agreement semantics: REJECTED.
    - S1 — canonical agreed commercial truth but only one current semantic state required: REJECTED as insufficient for historical truth.
    - S2 — historical Agreement Snapshot semantics: ACCEPTED at the semantic level; persistence/derivation remains unselected.

Scope: Conceptual NG-6 architecture only. This decision does NOT select or authorize persistence; database tables; foreign keys; EF mapping; schema; API payloads; interfaces/classes/module layout; document storage; e-signature/signature workflow; clause extraction/document AI; approval workflow; templates; a legal-enforceability engine; a universal rights ontology; rights-grant implementation; amount-determination persistence; commission implementation; receivables/invoices/payments/allocations; ledger/accounting; mandate mechanics beyond `DECISION-20260929-005`; Company ↔ External Organization implementation; Group persistence; product code; migrations; or NG-7.

Evidence / provenance:
- Canonical:
  - `DECISION-20260928-004`: hybrid commercial decomposition; Arrangement distinct from Proposal/Offer; Agreement Snapshot distinct from Legal Instrument; one-instrument/several-arrangements and the exact Snapshot representation left open.
  - `DECISION-20260929-005`: mandate/commercial lineage and historical provenance.
  - `DECISION-20260929-006`: Owner authorization for NG-6 research/design only.
  - `DECISION-20260929-007`: the exact NG-6 Scope Lock.
  - `DELTA-20260929-008`: the published NG-6 authorization transition.
- Build-97 predecessor, product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2` (NG-6A):
  - a Deal could precede and exist without Contract rows in the predecessor;
  - Deal→Contract was 0..n; Contract→Deal was exactly one direct predecessor relation;
  - the accepted Offer was a historical commercial-snapshot analogue but not the canonical future Agreement Snapshot;
  - ContractVersion represented drafting history, not amendment/restatement architecture;
  - amendment, side letter and similar kinds were distinct Contract/instrument precedents;
  - RightsGrant remained distinct;
  - these are predecessor precedents, not canonical neutral-core invariants.
- NG-6B bounded two-vertical evidence, Film/TV:
  - 2023 WGA–AMPTP MBA Article 13.A.3: a written document memorializing an agreement already reached; the cover sheet does not alter the prior agreement and the writer's agreement prevails (https://www.wga.org/uploadedfiles/contracts/mba23.pdf);
  - WGA MBA Article 14.B: writing plus additional capacities may be covered by one contract or by separate contracts;
  - SAG-AFTRA historical agreement evidence that a performer may be definitely engaged through an accepted verbal call and that the written contract can follow later;
  - DGA and other Film/TV material was supporting evidence only; the decision does not depend on an unverified current-version proposition.
- NG-6B bounded two-vertical evidence, live music / booking:
  - Musicians' Union standard live-contract guidance: signed-contract practice plus a letter/email confirmation fallback (https://musiciansunion.org.uk/legal-money/contracts-and-agreements/standard-contracts/live-engagement-standard-contracts);
  - AFM/EPF LS-1 Q&A: a single engagement may require another agreement for additional terms; multiple dates can still constitute a defined single engagement under specified conditions (https://members.afm.org/uploads/file/officers%20edge/OEWinter02.pdf);
  - AFM/CFM T2C: an integration clause superseding prior oral/written representations and requiring a written signed amendment (https://cfmusicians.afm.org/uploads/file/Travelling%20Eng%20Contract%20-%20T2C.pdf);
  - CFM/AFM multi-musician "severally" forms and MU multi-engagement forms were pressure tests for L3 but did not prove an indivisible one-instrument/multiple-Arrangement case.
- Explicit Control Room correction to the Claude NG-6B report: Claude classified S0 as empirically falsified too broadly. The evidence proves independent canonical commercial-agreement truth, but does not select persisted versus derived technical representation. The final S2 decision is semantic/historical only.
- Claude research reports were evidence inputs only. The decision is the Control Room's.

Consequences:
- NG-6 architecture research/design is terminally adjudicated.
- After this decision and its corresponding delta are fully PUBLISHED and remotely verified, NG-6 becomes CLOSED.
- No implementation authority follows.
- NG-7 remains unauthorized.
- Exact persistence/schema/API/interface/storage mechanics remain deferred.
- L3 may be reconsidered only on the explicit evidence trigger in Decision item 4.

Supersedes: None

Unchanged:
- `DECISION-20260928-001` to `-004`, `DECISION-20260929-002` and `DECISION-20260929-005`.
- `DECISION-20260929-006` as the Owner authorization that allowed NG-6 research/design.
- `DECISION-20260929-007` as the NG-6 Scope Lock that governed the research.
- Build 97 and all implementation behavior.
- No product implementation.
- No schema, API or domain expansion.
- No migrations.
- NG-7 remains unauthorized.

Open:
- The exact persistence/derivation mechanism for Agreement Snapshot.
- The exact technical representation of Snapshot↔Instrument historical anchoring.
- Persistence/schema/API/interface/storage mechanics.
- Legal-effect computation.
- Clause extraction.
- Rights implementation.
- Amount determination.
- Finance/ledger.
- L3 reconsideration, only if its concrete trigger is met.
- All other questions excluded by `DECISION-20260929-007`.

Recorded by: Claude (AgencyOS executor), on Control Room instruction. Claude is the recorder/executor only, not the authority.

Publication receipt: f3635faf6b7e5a747b12f89e2c4696853d1d2629 on origin/operational-regression-gate; remote readback verified 2026-09-30T01:25:36Z

## DECISION-20260929-009

Status: ACTIVE

Date: 2026-09-29

Authority: OWNER

Question: Should AgencyOS begin NG-7 / the next post-NG-6 stage now for research and design only, excluding implementation and product/schema/API/domain expansion, or remain at NG-6 CLOSED?

Decision: The Owner explicitly approves beginning **NG-7 / the next post-NG-6 stage for research and design only**.

Owner response: “מאשר.”

This authorization is intentionally narrow. It does not define or pre-approve the exact NG-7 architecture topic, title, bound question, Scope Lock, hypotheses, substages, exit criteria or conclusion. Before substantive NG-7 research begins, the Control Room must establish the exact bounded NG-7 Scope Lock. No implementation authority follows.

Scope:
- Authorizes NG-7 research/design after this authorization is fully PUBLISHED and after the Control Room establishes the bounded NG-7 Scope Lock.
- Allows the subsequent Control Room work needed to define that Scope Lock.
- Does not authorize product code.
- Does not authorize implementation.
- Does not authorize schema, API or domain expansion.
- Does not authorize migrations.
- Does not authorize NG-8 or any later stage.
- Does not pre-approve any NG-7 architecture conclusion.

Evidence / provenance:
- Explicit Owner reply “מאשר.” on 2026-09-29.
- It answered the Control Room's binary authorization question: “האם לאשר התחלה של NG-7 / השלב הבא אחרי NG-6 למחקר ותכנון בלבד, ללא implementation וללא product/schema/API/domain expansion, או לעצור במצב NG-6 CLOSED?”
- `docs/control-room/CURRENT-STATE.md` at `c8bd3fc38e6e0e840691b33157e9f46a433fae5f` records NG-6 as CLOSED under `DECISION-20260929-008`, and its exact next bounded action requires explicit Owner authorization before any post-NG-6 stage, including NG-7.
- Claude is recorder/executor only, not decision authority.

Consequences:
- Once this decision and its accompanying delta are fully PUBLISHED, NG-7 is authorized for research/design only.
- The immediate next bounded work is Control Room definition of the exact NG-7 Scope Lock.
- Substantive NG-7 research must not begin before that Scope Lock is established.
- No implementation authority follows.

Supersedes: None

Unchanged:
- NG-6 remains CLOSED under `DECISION-20260929-008`.
- All NG-1 through NG-6 architecture decisions remain in force.
- Build 97 remains the released product identity.
- Operational Closure remains COMPLETE and terminal.
- Self-Update V1 remains COMPLETE.
- No next-generation architecture is implemented.
- No product/schema/API/domain behavior changes.
- Existing deferred/open questions remain open unless a later bounded NG-7 decision addresses them.
- NG-8 remains unauthorized.

Open:
- The exact NG-7 title.
- The exact NG-7 bound question.
- The exact NG-7 Scope Lock.
- Hypotheses and the falsification boundary.
- Substages and exit criteria.
- Any genuine Owner-reserved ambiguity discovered later.
- Implementation.
- NG-8 and later stages.

Recorded by: Claude (AgencyOS executor), on explicit Owner instruction normalized by the Control Room. Claude is recorder/executor only, not authority.

Publication receipt: f0c0a5c215fc94cf47b1a6966f1f5ed6c8c71311 on origin/operational-regression-gate; remote readback verified 2026-09-30T02:34:09Z

## DECISION-20260929-010

Status: ACTIVE

Date: 2026-09-29

Authority: CONTROL_ROOM

Question: What exact bounded title, research/design question, falsification boundary, substages and exit criteria govern NG-7 under the Owner authorization in DECISION-20260929-009?

Decision: NG-7 is scope-locked as:

**NG-7 — Amount Determination & Economic Truth Architecture**

**BOUND QUESTION**

What is the minimum neutral-core semantic contract for Amount Determination as the bridge from commercial/legal truth to economic truth: what an Amount Determination canonically represents; whether economic truth may exist before and independently of receivable, invoice and payment facts; how fixed, rate/formula-based, percentage, unit-based, contingent, periodic and later-adjusted compensation is represented; what historical/source anchoring prevents later agreement, instrument or realized-value changes from silently rewriting earlier economic truth; how currency and units remain truthful; how multiple economic components relate to one Commercial Arrangement or Legal Instrument; and where the semantic boundary lies between Amount Determination, agency commission/fee entitlement, receivable, invoice, payment/allocation and accounting projection—without selecting persistence or creating a universal EconomicEntitlement.

**IN SCOPE**

1. The canonical semantic meaning of Amount Determination.
2. The boundary between upstream commercial/legal truth and economic truth.
3. Whether and how economic truth exists before receivable, invoice or payment facts.
4. Fixed, rate-based, formula-based, percentage-based, unit-based, contingent, periodic and initially unquantified amounts.
5. The distinction among:
   - source terms;
   - a determination method/basis;
   - a determined or determinable economic amount;
   - later realized inputs or values;
   without pre-selecting a persistence shape.
6. Historical anchoring:
   - later renegotiation must not silently rewrite earlier economic truth;
   - later Legal Instruments must not silently repoint earlier determinations;
   - later realized values/recalculations/adjustments must preserve prior historical meaning.
7. Source/provenance relationship from Amount Determination to the relevant Commercial Arrangement, Agreement Snapshot and/or Legal Instrument where applicable, including pressure-testing whether one determination can depend on more than one source fact.
8. Multiple economic components associated with one Arrangement or Instrument.
9. Money/currency and unit truth, including preserving original currency and relevant quantity/rate units.
10. Unknown, unquantified, contingent, formula-dependent and later-resolved economic truth only to the extent required to avoid false absence or false certainty.
11. Agency commission/fee entitlement only as a boundary test against Amount Determination.
12. Receivable, invoice, payment, allocation, cash application and accounting projection only as downstream boundary tests.
13. Vertical vocabulary/typed-term pressure only as needed to keep the neutral core from absorbing Film/TV- or music-specific concepts.
14. Historical corrections/adjustments only at the semantic level.

**OUT OF SCOPE**

- persistence design;
- database tables;
- foreign keys;
- EF mapping;
- schema;
- API payloads;
- interfaces/classes/module layout;
- event-sourcing choice;
- exact calculation-engine implementation;
- exact commission formula/model/placement;
- commission implementation;
- receivable lifecycle;
- invoicing or accounts-receivable implementation;
- payment processing;
- payment allocation implementation;
- cash application;
- ledger/accounting implementation;
- tax, withholding or jurisdictional compliance;
- FX conversion/accounting policy;
- forecasting, valuation or profitability architecture;
- royalty-accounting implementation;
- rights-grant implementation;
- universal rights ontology;
- legal-enforceability/legal-effect engine;
- product implementation;
- migrations;
- NG-8.

**CLOSED DISTINCTIONS THAT MUST NOT BE REOPENED WITHOUT CONTRADICTORY EVIDENCE**

- Proposal/Offer ≠ Commercial Arrangement.
- Agreement Snapshot ≠ Legal Instrument.
- Legal Instrument ≠ Rights Grant.
- Legal/operational obligation ≠ receivable.
- Invoice is optional and ≠ receivable.
- Payment ≠ allocation/application.
- Ledger/accounting does not replace upstream commercial/legal truth.
- Amount Determination is a strong neutral-core capability when an obligation begins contingent, formula-based or unquantified.
- Agency commission/fee entitlement is a representation-economics capability.
- A generic universal EconomicEntitlement super-concept is not justified by current evidence.
- Money preserves currency truth.
- Deal/term vocabulary may remain vertical-specific.
- `DECISION-20260929-005` Mandate lineage remains in force.
- `DECISION-20260929-008` L2/S2 Arrangement–Instrument/Snapshot architecture remains in force.
- No generic Entity/Party/Transaction abstraction is authorized.

**FALSIFICATION MODELS**

- E0 — COLLAPSE: No independent Amount Determination semantics are required; monetary truth can live only as fields on the Commercial Arrangement or Legal Instrument.
- E1 — CURRENT SCALAR: An Arrangement needs at most one mutable current monetary total; historical/componentized economic truth is unnecessary.
- E2 — HISTORICAL COMPONENTIZED DETERMINATION: Amount Determination is distinct economic truth, historically anchored to its source basis; one Arrangement may have multiple economic components; a determination may exist before receivable/invoice; and it can truthfully represent contingent, formula-based or initially unquantified economics without fabricating a current scalar.
- E3 — UNIVERSAL ENTITLEMENT: A generic neutral-core EconomicEntitlement abstraction should unify compensation, agency commission/fee claims, receivables and other monetary claims.

These are hypotheses to test. The Scope Lock does NOT pre-select a winner.

**RESEARCH BOUNDARY**

Research exactly two verticals:
- A. Film/TV representation.
- B. Live music / artist booking.

No third vertical during NG-7 unless the Control Room explicitly reopens the Scope Lock based on a concrete falsification failure. Use primary/authoritative sources where reasonably available.

The vertical research should deliberately pressure-test:
- fixed compensation/guarantees;
- episodic, weekly, per-service or unit-based compensation;
- rates and formulas;
- percentages;
- bonuses/options/escalators where relevant;
- contingent or not-yet-quantifiable compensation;
- later actual-value inputs;
- multiple economic components;
- deposits/payment timing only as a boundary from economic truth to payment truth.

Do not turn the research into a comprehensive survey of compensation law, guild rules, taxation or royalty accounting.

**SUBSTAGES**

- NG-7A — Build-97 predecessor baseline: source inspection only. Establish what Build 97 currently treats as monetary/economic truth, amount determination, compensation, commission, receivable/invoice/payment and accounting-related facts. Distinguish precedent from invariant. No architecture conclusion.
- NG-7B — Two-vertical falsification: test E0–E3 using exactly the two authorized verticals and the pressure cases above. Separate empirical findings from architectural inference.
- NG-7C — Control Room terminal adjudication: the Control Room decides the minimum neutral-core semantic contract, correction chain, hypothesis disposition and whether any genuine Owner-reserved ambiguity remains.
- NG-7D — Canonical publication/closure: publish the accepted architecture only after Control Room adjudication. No implementation authority follows.

**EXIT CRITERIA**

NG-7 may close only when the evidence is sufficient to decide, at semantic architecture level:
1. what Amount Determination canonically means;
2. whether it can exist before receivable/invoice/payment;
3. the minimum source/provenance relationship to commercial/legal truth;
4. whether one Arrangement may carry several economic components and the minimum justified cardinality;
5. how contingent/formula-based/unquantified economics are represented without false certainty;
6. the historical rule for renegotiation, amendment, recalculation and later actual values;
7. currency/unit truth;
8. the boundary from Amount Determination to agency commission/fee entitlement;
9. the boundary from Amount Determination to receivable, invoice, payment/allocation and accounting projection;
10. the disposition of E0–E3.

Closure does NOT require choosing persistence, schema, API, implementation, calculation engine, commission model, receivable lifecycle or ledger integration.

If the evidence leaves a genuine product/strategy choice that belongs to the Owner rather than an evidentiary architecture adjudication, stop as OWNER DECISION REQUIRED.

Scope: Conceptual NG-7 research/design governance only. No implementation authority.

Evidence / provenance:
- `DECISION-20260928-004`: hybrid commercial decomposition; amount determination identified as a strong core capability where an obligation starts contingent, formula-based or unquantified; agency commission/fee entitlement identified as a representation-economics capability; a generic universal EconomicEntitlement not justified; the exact amount-determination record left open.
- `DECISION-20260929-002`: the neutral core owns shared commercial/legal/economic boundaries, money/currency truth and the neutral amount-determination capability; exact amount-kind enums/storage were not made invariant.
- `DECISION-20260929-005`: historical Mandate-to-commercial lineage.
- `DECISION-20260929-008`: L2/S2 Arrangement–Legal Instrument/Agreement Snapshot architecture and historical anchoring; amount-determination persistence, commission, receivables/invoices/payments/allocations and ledger explicitly deferred.
- `DECISION-20260929-009`: Owner authorization for NG-7 research/design only.
- `docs/control-room/CURRENT-STATE.md` at `0428ab3a22bb1cab88da7d21ff89d6b826fc7673`: the exact amount-determination record remains open; the commission model and ledger integration remain separate open questions.
- The Control Room's present Scope Lock adjudication.
- Claude is recorder/executor only, not architecture authority.

Consequences:
- Once this Scope Lock is fully PUBLISHED, NG-7A may begin.
- NG-7 remains research/design only.
- No implementation authority follows.
- No schema/API/domain expansion follows.
- No migrations follow.
- NG-8 remains unauthorized.

Supersedes: None

Unchanged:
- All prior published architecture decisions remain in force except where a future evidence-backed correction explicitly supersedes one.
- No product behavior changes.

Open:
- All questions explicitly excluded above.
- Exact implementation.
- Commission model/placement.
- Receivable/invoice/payment/allocation architecture beyond boundary tests.
- Ledger/accounting integration.
- NG-8.

Recorded by: Claude (AgencyOS executor), on Control Room instruction. Claude is recorder/executor only, not authority.

Publication receipt: 55af69ff5fea0e2a3e26c45eade16cf3c6936a62 on origin/operational-regression-gate; remote readback verified 2026-09-30T02:53:30Z

## DECISION-20260930-001

Status: ACTIVE

Date: 2026-09-30

Authority: CONTROL_ROOM

Question: Has NG-7 — Amount Determination & Economic Truth Architecture reached a terminal semantic architecture conclusion under DECISION-20260929-010, and if so what minimum neutral-core contract governs economic components, determination methods and realizations, source/history anchoring, currency/unit truth, commission boundaries and downstream receivable/payment/accounting boundaries?

Decision: NG-7 — Amount Determination & Economic Truth Architecture is terminally accepted as a conceptual semantic architecture stage:

1. **Amount Determination.** An Amount Determination is the neutral-core economic fact for one independently meaningful economic component of a Commercial Arrangement. It preserves:
   - what economic component is being determined;
   - the determination method/basis;
   - source/provenance;
   - currency and unit meaning;
   - its historical realizations where applicable.

   It does not itself assert legal enforceability. It is distinct from Commercial Arrangement; Agreement Snapshot; Legal Instrument; Rights Grant; agency commission/fee claim; Receivable; Invoice; Payment; Payment Allocation/Application; and Ledger/accounting projection.
2. **Arrangement cardinality.** A Commercial Arrangement may have zero or more Amount Determinations. Each Amount Determination is semantically scoped to exactly one Commercial Arrangement in the present neutral-core contract. Additional source facts do not change that Arrangement scope. No multi-Arrangement Amount Determination is justified by the bounded evidence.
3. **Method versus realization.** The neutral core must preserve the semantic distinction between:
   - A. the agreed or otherwise governing determination method/basis; and
   - B. one or more later historical realizations/evaluations of that method when required.

   A method may be meaningful before every numerical input is known. The same method may produce repeated realizations over time, such as periodic residual calculations or show settlements. A later realization must not silently rewrite the earlier agreed method, earlier realized inputs or earlier realized results. Whether method and realization are persisted as separate records, derived, evented, versioned or represented by another implementation mechanism remains unselected.
4. **Minimum shapes.** The bounded two-vertical evidence justifies neutral semantic support for at least:
   - fixed amount;
   - quantity × unit rate;
   - percentage × identified/defined basis;
   - contingent amount or method;
   - initially unquantified determination;
   - method awaiting one or more later realized inputs;
   - repeated realizations from one governing method.

   This decision does NOT authorize a generic expression language, a calculation DSL or a universal formula syntax. Greater-of, crediting, offsets, floors, caps and similar component relations are economically meaningful where evidenced, but the evidence does not justify a universal neutral-core relation taxonomy now. The neutral core must preserve component identity sufficiently for vertical semantics to express such relations without collapsing the components into a single scalar.
5. **Source / provenance.** Every Amount Determination must retain enough semantic provenance to answer why its method or realized result is true. Its source basis may include, as applicable:
   - the Commercial Arrangement;
   - a historical Agreement Snapshot;
   - a Legal Instrument or amendment;
   - an option/exercise or other later commercial/legal event;
   - an external governing schedule or collective agreement;
   - a later realized operating fact such as quantity, receipts, ticket audit, revenue statement or similar input.

   One determination may depend on more than one source fact. Known provenance stays historically anchored; it is not dynamically repointed to a later agreement, instrument, schedule or input. Unknown or unresolved provenance remains unknown/unresolved. This does not establish a universal precedence rule between commercial, legal and external-source truth.
6. **Pre-instrument economic truth.** Economic determination semantics may exist before a final Legal Instrument exists. A commercially agreed rate, guarantee, percentage, method or other economic component may therefore be canonically representable at Arrangement / historical agreement state before long-form execution. Recording that economic truth does not itself claim legal enforceability, collectibility, receivable status or billing status. Build-97's requirement that MonetaryObligation begin only after an executed Contract is a predecessor implementation limitation, not a neutral-core invariant.
7. **History.** Renegotiation, amendment, option exercise, escalation, later realized inputs, recalculation, correction and settlement must not silently rewrite earlier economic truth. The architecture must preserve, where applicable:
   - the prior governing method;
   - previously known values;
   - later realized input;
   - resulting realization;
   - correction, adjustment or supersession relationship.

   This is a semantic requirement only. No storage/version/event mechanism is selected.
8. **Money / currency / unit truth.** A monetary value preserves its currency. Quantity/rate determinations preserve the relevant unit. Percentage determinations are incomplete without an identified/defined basis. Where a source/basis value is in one currency and a resulting determination is in another, the original basis value/currency and the resulting value/currency must remain distinguishable, together with the applicable provenance for the conversion. No FX calculation engine or accounting policy is selected. Unknown currency/unit meaning must not be replaced with a guessed default.
9. **Commission / agency-fee boundary.** Agency commission/fee is a distinct representation-economics claim. It is not merely another represented-party compensation component and must not be collapsed into the represented party's Amount Determination. However, commission/fee calculation reuses the same neutral Amount Determination semantics: fixed amount; percentage × defined basis; contingencies; realized inputs where applicable. Its distinct semantics arise from:
   - source/authority in the representation relationship or commission rule;
   - agency beneficiary;
   - its own earning/timing/lifecycle conditions;
   - its dependency on represented-party economic truth.

   The exact commission model, placement, cardinality and implementation remain open. No separate universal commission calculation language is justified.
10. **Receivable / invoice / payment / accounting boundary.** An Amount Determination may exist before any Receivable, Invoice or Payment.
    - Receivable is a downstream collectible/crystallized claim.
    - Invoice is an optional billing artifact and is not the economic determination.
    - Payment is cash movement.
    - Allocation/Application records the application of cash to a claim.
    - Ledger/accounting is a downstream projection and does not replace upstream commercial/legal/economic truth.

    A deposit or other Payment may occur before a final realization is known; the existence of cash does not prove that the final economic amount has been determined. This decision does not select cardinalities or implementation mechanics for these downstream facts.
11. **Component relations.** Several economically meaningful components may coexist in one Commercial Arrangement. The neutral core therefore rejects a single-current-total model. Crediting, offset, greater-of and similar relations may affect the economic result, but no universal relation enum/ontology is approved. Under the existing Vertical Extension Architecture, vertical semantics may express evidence-earned relations over neutral component identities without duplicating or replacing neutral economic truth.
12. **Hypothesis disposition:**
    - E0 — COLLAPSE: REJECTED. Economic truth cannot truthfully live only as fields on Arrangement or Legal Instrument.
    - E1 — CURRENT SCALAR: REJECTED. One mutable current monetary total cannot preserve multiple simultaneous, contingent, unresolved or historically changing components.
    - E2 — HISTORICAL COMPONENTIZED DETERMINATION: ACCEPTED WITH REFINEMENT. The accepted form explicitly distinguishes governing determination method from one or more historical realizations and allows source basis to include commercial, legal, external-schedule and later-realized facts.
    - E3 — UNIVERSAL ENTITLEMENT: REJECTED as a claim-unification architecture. Compensation/economic determination, agency commission/fee claim, Receivable and Payment remain semantically distinct. Their shared calculation shapes are handled by the Amount Determination capability rather than by a universal EconomicEntitlement super-concept.
13. **Build-97 predecessor disposition.** Build-97 MonetaryObligation is accepted as a strong predecessor, not the neutral-core invariant.
    - Its useful precedents include: Fixed / Formula / Contingent / Unknown; refusal to fabricate zero for unknown economics; separate downstream Receivable / Invoice / Payment / Allocation; currency truth; separate commission entitlement.
    - Its limitations are not promoted: post-execution-only economic truth; quantity × rate as the only formula shape; no percentage-basis determination; Quantify overwriting prior determination shape; weak source-term identity; single-obligation commission basis; API/client projection losses; implementation-specific ledger behavior.

Scope: Conceptual/semantic NG-7 architecture only. This decision does NOT select or authorize persistence; tables; schema; foreign keys; EF mappings; API payloads; classes/interfaces/modules; event sourcing; a version-storage mechanism; a calculation engine; a generic formula DSL; the exact component-relation representation; the exact commission model or placement; commission implementation; receivable/invoice/payment/allocation cardinalities or implementation; ledger/accounting integration; tax/withholding; an FX engine/policy; royalty-accounting implementation; a legal enforceability engine; product code; migrations; or NG-8.

Evidence / provenance:
- A. Canonical foundations: `DECISION-20260928-004`; `DECISION-20260929-002`; `DECISION-20260929-005`; `DECISION-20260929-008`; `DECISION-20260929-009`; `DECISION-20260929-010`.
- B. NG-7A Build-97 source inspection at product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`, including the verified predecessor findings around MonetaryObligation; Receivable; CommissionRule / CommissionEntitlement; Payment / Allocation; Ledger; Money; and the finance API/client projections.
- C. NG-7B bounded two-vertical evidence:
  - Film/TV: WGA, DGA and SAG-AFTRA primary/authoritative material plus bounded supporting evidence;
  - Live music / artist booking: AFM, Musicians' Union and bounded primary booking/engagement forms;
  - exactly two verticals were researched, and no third vertical was needed.
- D. Control Room terminal adjudication: E0 rejected; E1 rejected; E2 accepted with the method-versus-realization refinement; E3 rejected as claim unification; no Owner-reserved ambiguity remains.
- Correction chain preserved from NG-7:
  1. Build 97 does not lack an amount-determination predecessor; MonetaryObligation is strong and explicit.
  2. Build 97 is not scalar-only; Fixed/Formula/Contingent/Unknown and multiple obligations already exist.
  3. E2 required refinement because determination method and later realization are semantically distinct.
  4. Amount Determination provenance is not limited to commercial/legal records; external governing schedules and later realized inputs may participate.
  5. Commission shares Amount Determination calculation semantics but remains a distinct representation-economics claim.
  6. Greater-of/crediting/offset relations are real but not sufficiently proved as a universal neutral-core taxonomy.
- Claude reports are evidence inputs only. Claude is not the architecture authority.

Consequences:
- NG-7 is CLOSED once this decision and delta reach canonical PUBLISHED state.
- The minimum neutral-core Amount Determination & Economic Truth contract above governs future design.
- No implementation authority follows.
- No schema/API/domain expansion follows.
- No migrations follow.
- NG-8 remains unauthorized.
- Beginning any post-NG-7 stage, including NG-8, requires explicit Owner authorization.

Supersedes: None

Unchanged:
- `DECISION-20260929-009` remains the Owner authorization that permitted NG-7 research/design.
- `DECISION-20260929-010` remains the published Scope Lock that governed NG-7.
- NG-1 through NG-6 remain closed under their existing decisions.
- Build-97 released product behavior is unchanged.
- Previously open implementation questions remain open unless explicitly resolved by this semantic decision.

Open:
- The persistence/derivation/version mechanism for determination methods and realizations.
- The exact component-relation representation.
- The exact commission model/placement/cardinality.
- The exact Receivable/Invoice/Payment/Allocation cardinalities.
- Ledger/accounting integration.
- FX mechanics.
- Implementation.
- NG-8 and later stages.

Recorded by: Claude (AgencyOS executor), on Control Room instruction. Claude is recorder/executor only, not authority.

Publication receipt: 812325fafda011f3345380a329773d7b9a1be45b on origin/operational-regression-gate; remote readback verified 2026-09-30T06:47:43Z

## DECISION-20260930-002

Status: ACTIVE

Date: 2026-09-30

Authority: OWNER

Question: Approve NG-8 — Agency Commission & Representation-Economics Claim Architecture for research/design only, under the complete bounded stage definition proposed by the Control Room, with no implementation, product/schema/API/domain expansion, migrations or NG-9 authority?

Decision: The Owner approves **NG-8 — Agency Commission & Representation-Economics Claim Architecture** exactly under the following bounded stage contract.

**TITLE**

NG-8 — Agency Commission & Representation-Economics Claim Architecture

**BOUNDED QUESTION**

What is the minimum neutral-core semantic architecture for agency commission / representation-economics claims: what fact makes the agency economically entitled to compensation; how that claim derives authority from a Representation Mandate or other representation authority while depending on one or more represented-party Amount Determinations; when the claim exists, accrues, becomes earned, becomes determinable, is reduced, adjusted, waived, superseded or extinguished; whether one represented-party economic component may generate zero, one or several agency claims and vice versa; how effective-dated rules, contract/deal-specific overrides, caps/floors/exclusions and collected-versus-earned distinctions remain historically truthful; and where the boundary lies between the commission/fee claim itself, its Amount Determination semantics, Receivable, Invoice, Payment, allocation/cash application and accounting revenue recognition — without selecting persistence or implementing finance.

**IN SCOPE**

1. Semantic meaning of agency commission / representation-economics claim.
2. Source / authority:
   - Representation Mandate;
   - representation agreement/rule where evidence justifies;
   - arrangement-specific override where applicable.
3. Relation to represented-party Amount Determination.
4. Distinction among:
   - rule/method;
   - claim/entitlement;
   - calculated/determinable amount;
   - earned state;
   - collected state.
5. Fixed fee, percentage × basis, contingent commission, floors, caps and exclusions where evidenced.
6. Effective dating and historical rule changes.
7. Governing-date semantics.
8. General rule versus arrangement/contract-specific rule.
9. Semantic cardinality:
   - one rule to many claims;
   - one represented-party component to zero, one or several agency claims;
   - one agency claim depending on one or several represented-party economic components.
10. Recalculation, supersession, adjustment, waiver/release and correction.
11. Commission on options, bonuses, residuals and settlements only to test neutral semantics.
12. Earned versus collectible versus collected.
13. Agency-beneficiary truth.
14. Boundary to Receivable / Invoice / Payment / Allocation.
15. Boundary to revenue/accounting recognition.
16. Currency/basis provenance inherited from NG-7.
17. Vertical-specific rules only as pressure against false neutrality.

**OUT OF SCOPE**

- persistence/database/schema/FKs;
- API/classes/modules;
- commission calculation-engine implementation;
- generic expression DSL;
- receivable lifecycle design;
- invoicing implementation;
- payment processing;
- cash-application implementation;
- ledger-posting implementation;
- revenue-recognition accounting-policy implementation;
- tax/withholding;
- FX engine;
- payroll;
- royalty-accounting implementation;
- legal-enforceability engine;
- broad talent-manager/licensing-law compliance survey except where required to falsify a semantic claim;
- product code;
- migrations;
- NG-9.

**CLOSED DISTINCTIONS — DO NOT REOPEN WITHOUT CONTRADICTORY EVIDENCE**

- Representation Mandate ≠ Commercial Arrangement.
- Commercial Arrangement ≠ Legal Instrument.
- Amount Determination ≠ agency commission/fee claim.
- agency commission/fee claim ≠ Receivable.
- Receivable ≠ Invoice.
- Payment ≠ allocation/application.
- ledger/accounting ≠ upstream economic truth.
- commission calculation may reuse Amount Determination semantics.
- no universal EconomicEntitlement.
- no separate universal commission calculation language is currently justified.
- money preserves currency.
- historical truth must not be silently rewritten.
- no generic Entity/Party/Transaction abstraction.

**FALSIFICATION HYPOTHESES**

- C0 — DERIVED ONLY: No independent commission claim semantics are required. The agency's current amount can always be computed directly from a representation rule plus represented-party Amount Determination.
- C1 — ONE CURRENT ENTITLEMENT: An independent claim is needed, but one mutable current entitlement per client/representation or Arrangement is sufficient.
- C2 — HISTORICAL REPRESENTATION-ECONOMICS CLAIM: A separate historically anchored representation-economics claim is required. Rule and claim are distinct. The claim is anchored to representation authority/rule and to its represented-party economic basis. It may be unearned, unquantified, earned, adjusted or superseded without collapsing into Receivable or Payment.
- C3 — GENERIC MONETARY CLAIM: Commission proves the need for a neutral-core generic MonetaryClaim/EconomicClaim that unifies commission, represented-party compensation claims, receivables and potentially other monetary claims.

The Scope Lock pre-selects NONE of C0–C3.

**AUTHORIZED RESEARCH BOUNDARY**

Exactly two verticals:
- A. Film/TV representation.
- B. Live music / artist booking.

No third vertical unless concrete evidence from the two authorized verticals leaves a genuine ambiguity that blocks terminal adjudication and the Control Room explicitly reopens the boundary.

**NG-8A — BUILD-97 PREDECESSOR BASELINE**

Source inspection only, at the exact Build-97 predecessor `b3f41bfd68e81ab42da899671f58e01f0988d3d2`. Focus on: CommissionRule; CommissionEntitlement; Representation relation; Client relation; Contract relation; MonetaryObligation; Receivable; Payment/collection; ledger/revenue path; effective dating; governing date; recalculation/supersession; adjustments; fixed versus percentage/rate basis; contract-specific override; persisted versus derived facts; API/client/UI only where semantically relevant.

NG-8A establishes precedent only. No external vertical research. No architecture conclusion. No C0–C3 winner.

**NG-8B — TWO-VERTICAL FALSIFICATION**

Use exactly the two authorized verticals. Test C0–C3. Separate empirical source fact, source interpretation and architecture pressure. Do not make the terminal architecture decision.

**NG-8C — CONTROL ROOM TERMINAL ADJUDICATION**

The Control Room decides: semantic meaning; authority/source; cardinality; rule versus claim; earned/determined/collected distinctions; history; commission ↔ Amount Determination relation; downstream boundaries; C0–C3 dispositions; whether any genuine Owner-reserved ambiguity remains. Claude is never the terminal architecture authority.

**NG-8D — CANONICAL PUBLICATION / CLOSURE**

Publish the accepted architecture only after Control Room adjudication. No implementation authority follows.

**EXIT CRITERIA**

NG-8 may close only when evidence is sufficient to decide semantically:
1. what a commission/representation-economics claim is;
2. the source of its authority;
3. whether rule and claim are distinct facts;
4. when the claim exists;
5. the distinction among determinable, earned, collectible and collected;
6. minimum justified cardinality to Representation / Arrangement / Amount Determination;
7. whether a claim may depend on several Amount Determinations;
8. historical treatment of rule changes, overrides and effective dates;
9. historical treatment of recalculation, adjustment and supersession without silent rewrite;
10. fixed/rate/contingent/cap/floor/exclusion semantics without a generic DSL;
11. currency/basis provenance;
12. boundary to Receivable;
13. boundary to Payment/collection;
14. boundary to ledger/revenue recognition;
15. disposition of C0–C3.

NG-8 closure does NOT require: persistence choice; schema; API; implementation; calculation engine; AR/payment implementation; ledger implementation.

Scope: Research/design and conceptual semantic architecture only. NO authority for implementation; product code; schema/API/domain expansion; migrations; or NG-9.

Evidence / provenance:
- Explicit Owner approval in the Control Room conversation on 2026-09-30.
- `DECISION-20260930-001` / `DELTA-20260930-001` as the terminal NG-7 basis.
- The Control Room's complete NG-8 stage definition, approved by the Owner.
- Claude is recorder/executor only, not decision authority.

Consequences:
- Once this decision is PUBLISHED, NG-8 is AUTHORIZED and SCOPE-LOCKED for research/design only.
- NG-8A becomes the exact next bounded substantive action.
- No implementation authority follows.
- NG-9 remains unauthorized.

Supersedes: None

Unchanged:
- NG-0 through NG-7 remain closed/approved exactly as currently published.
- Build-97 released product behavior is unchanged.
- All unresolved implementation questions remain unresolved unless NG-8 semantically adjudicates their boundary.
- No implementation follows.

Open:
- The terminal NG-8 architecture result.
- Implementation.
- NG-9 and later stages.

Recorded by: Claude (AgencyOS executor), on explicit Owner instruction transmitted by the Control Room. Claude is recorder/executor only, not authority.

Publication receipt: ace4f9890488516da49be163d482f65fab7c21fb on origin/operational-regression-gate; remote readback verified 2026-09-30T07:51:52Z

## DECISION-20260930-003

Status: ACTIVE

Date: 2026-09-30

Authority: OWNER

Question: Adopt the Recursive Control-Room Correspondence & Transition Contract so that not only workflow continuity but the complete configuration of substantive Control Room messages, Claude prompts and future conversation handoffs recursively reproduces itself until the Owner explicitly changes the policy?

Decision: The Owner adopts the **Recursive Control-Room Correspondence & Transition Contract**. The recursion applies BOTH to workflow continuity AND to the structural configuration and required details of the message itself.

**A. REQUIRED CONTROL ROOM MESSAGE CONFIGURATION**

Every substantive Control Room stage/status message must preserve this configuration unless the Owner explicitly requests another presentation for that specific message:
1. "מצב נוכחי";
2. actual branch/HEAD whenever repository state matters;
3. governing Decision/Delta IDs;
4. current NG stage and substage;
5. stage state using exactly one of: OPEN IN RESEARCH; BLOCKED; OWNER DECISION REQUIRED; READY TO CLOSE; CLOSED;
6. "מה הוכח";
7. "מה תוקן / הופרך" — if no correction exists, state exactly: "אין תיקון חדש.";
8. "מה עדיין פתוח";
9. "מה אסור כרגע";
10. "תקציב פרומפטים";
11. "הפעולה הבאה המדויקת";
12. exactly one Owner-action line in one of these forms:
    - "OWNER ACTION: none — Control Room analysis is next."
    - "OWNER ACTION: send the Claude prompt below."
    - "OWNER DECISION REQUIRED: <exact question>."
    - "OWNER ACTION: open the next conversation and paste the handoff below."

When Claude execution/research/publication is truly next, include exactly one exact copy-paste Claude prompt. When Claude is not next, do not manufacture a Claude prompt.

**B. REQUIRED CLAUDE-PROMPT CONFIGURATION**

Every substantive Claude prompt must preserve, where applicable: repository; canonical branch; expected HEAD/baseline; governing Decision/Delta IDs; exact stage/substage; exact scope; explicit out-of-scope; closed distinctions that must not be reopened; falsification hypotheses where applicable; evidence hierarchy; correction discipline; authority boundaries; allowed and forbidden repository actions; required evidence/report structure; explicit stop conditions; exact next handback to the Control Room; the current prompt-budget context where material; and this Recursive Control-Room Correspondence & Transition Contract.

A Claude report never creates authority merely by containing these fields.

**C. CORRECTION CHAIN IS RECURSIVE**

Future messages and handoffs must preserve material correction chains. They must not collapse the original claim, the contradicting evidence, the correction and the current governing conclusion. A new conversation must not make a corrected claim appear to have always been the original claim.

**D. PROMPT BUDGET IS RECURSIVE**

Every substantive stage status must state the remaining or estimated substantive Claude prompt budget. The handoff to a new conversation must carry that budget. A new conversation continues the existing budget context rather than silently resetting it.

**E. PROVED / OPEN / FORBIDDEN IS RECURSIVE**

Every transition must separately preserve: what is proved; what was corrected/refuted; what remains open; what is currently forbidden. Absence from a handoff must not be interpreted as closure.

**F. EXACT NEXT ACTION IS RECURSIVE**

Every substantive status/handoff must identify exactly one bounded next action. A conversation transition is continuity only. It does not authorize the next stage or action by itself.

**G. CROSS-CONVERSATION HANDOFF**

When the Control Room determines that a new conversation is appropriate, it must first complete the currently required adjudication/publication boundary. Then it produces a handoff that includes, at minimum: current canonical branch/HEAD; release/product baseline where relevant; governing Decision/Delta IDs; terminal closed stages; current stage/substage; stage state; complete proved findings needed for continuation; complete material correction chain; unresolved questions; forbidden work; prompt budget; exact next action; exact Owner Action; any exact Claude prompt already authorized but not yet executed, if applicable; and the Recursive Control-Room Correspondence & Transition Contract itself.

**H. STRUCTURAL SELF-REPLICATION**

The handoff must not merely say "continue using the same policy." It must carry enough explicit content to reproduce the same required configuration in the next conversation. The next conversation must in turn apply this contract to its own future handoff. Therefore the transition rule is recursively self-replicating. In particular, future handoffs must continue to preserve: prompts; what was corrected/refuted; prompt budget; every other mandatory message field above; and the instruction that the subsequent handoff must again preserve the same configuration.

**I. STAGE DEFINITION BEFORE TRANSITION**

When a new stage requires definition:
1. the Control Room defines the stage completely in the active conversation;
2. the package includes: title; bounded question; in-scope; out-of-scope; closed distinctions; hypotheses; research boundary; substages; exit criteria; authority boundaries;
3. the Owner approves the complete package in that active conversation;
4. only then may an execution/research prompt be issued;
5. do not move to a new conversation merely to define the stage.

**J. AUTHORITY**

This communication/transition contract preserves continuity; does not itself authorize architecture, research, implementation or publication; does not make Claude a decision authority; does not override Owner-reserved decisions; and does not permit a future stage to start without its required authorization.

**K. DURATION**

This contract governs future Control Room stage work and conversation transitions until the Owner explicitly modifies or revokes it. A later explicit Owner instruction may modify it. Absent such modification, it continues recursively without requiring the Owner to re-approve the correspondence policy at every transition.

Scope: Control Room governance, correspondence structure, Claude-prompt continuity and conversation-transition continuity only. It does NOT authorize product work; authorize an NG stage; change domain architecture; change repository/product implementation authority; or make a chat transcript alone canonical product truth.

Evidence / provenance:
- Explicit Owner approval on 2026-09-30.
- The Owner's explicit clarification that recursion covers the detailed message configuration itself, including prompts, correction/refutation reporting, prompt budget and every other required detail.
- The Control Room normalization above.
- Claude is recorder/executor only, not decision authority.

Consequences:
- Future Control Room messages, prompts and cross-conversation handoffs must preserve this recursive configuration.
- Material omissions in a future handoff are continuity defects and must be corrected before relying on the handoff for bounded continuation.
- The policy itself must be carried forward recursively.

Supersedes: None

Unchanged:
- The canonical/domain authority hierarchy.
- Owner final decision authority.
- Claude's executor-only role.
- Existing published architecture decisions.
- Product state.

Open: None within this correspondence-policy scope.

Recorded by: Claude (AgencyOS executor), on explicit Owner instruction transmitted by the Control Room. Claude is recorder/executor only, not authority.

Publication receipt: ace4f9890488516da49be163d482f65fab7c21fb on origin/operational-regression-gate; remote readback verified 2026-09-30T07:51:52Z

## DECISION-20260930-004

Status: ACTIVE

Date: 2026-09-30

Authority: CONTROL_ROOM

Question: What is the minimum neutral-core semantic architecture for agency commission / representation-economics claims: what makes such a claim true, how it derives authority from representation and arrangement-specific source facts, how it relates to represented-party Amount Determinations, how its historical determination/earning/payability/collection truth is preserved, what cardinalities are justified, and where its boundary lies relative to Receivable, Invoice, Payment, Allocation and accounting — under the exact NG-8 Scope Lock of DECISION-20260930-002?

Decision: NG-8 — Agency Commission & Representation-Economics Claim Architecture is terminally adjudicated as follows.

**A. REPRESENTATION-ECONOMICS CLAIM**

A Representation-Economics Claim is a historical agency-side economic claim arising under a specific representation-authority lineage and source-defined commission/fee terms, concerning one Commercial Arrangement and one independently meaningful commission treatment. It preserves:
- why the agency-side claimant has the claim;
- its historical representation-authority/source provenance;
- the represented-party economic basis on which it depends;
- the method by which the claim amount is or will be determined;
- source-defined conditions affecting existence, earning/accrual, payability or continuation;
- historical changes that affect the claim.

It does not itself assert universal legal enforceability. This is semantic architecture only; it does not require a persisted entity/table.

**B. RULE / TERMS ARE DISTINCT FROM CLAIM**

Commission/fee rules or terms are reusable/source-bound semantics such as rate or method, defined basis, exclusions, caps/floors, timing conditions and tail/post-termination conditions. A claim is the concrete historical economic position arising when source-defined claim-creating facts are satisfied. Therefore:
- a rule may exist without a claim;
- a rule may generate zero or many claims;
- a claim may survive termination of the active representation relationship;
- later rule changes do not silently rewrite prior claims.

**C. SOURCE / AUTHORITY**

Each claim must preserve its historical representation-authority lineage. Its source provenance may be composite and may include:
- Representation Mandate / representation agreement;
- guild, union or franchise terms;
- statute/regulation;
- Commercial Arrangement or engagement-specific terms;
- later qualifying events such as option exercise, rebooking, performance/completion, receipt, waiver, settlement, forfeiture or similar source-defined facts.

Known source provenance remains historically anchored and must not dynamically repoint. Unknown or unresolved provenance remains unknown/unresolved. No universal precedence rule among all source classes is selected; precedence is source-defined.

**D. CLAIM MAY EXIST BEFORE QUANTIFICATION**

Claim existence is distinct from amount determination. A claim may be unquantified; contingent; or based on a known percentage/method while future represented-party values are not yet realized. Unknown amount is never zero. Build-97's inability to create an entitlement before a represented-party obligation is quantified is a predecessor limitation, not a neutral-core invariant.

**E. NO UNIVERSAL CLAIM LIFECYCLE**

The neutral core must preserve distinctions among:
1. claim authority/existence;
2. amount determinability;
3. source-defined earned/accrued condition, where meaningful;
4. source-defined payable/due condition;
5. operational collectible/Receivable recording;
6. collected cash.

There is no universal ordering or mandatory enum lifecycle across these facts. "Earned" is not a mandatory universal neutral-core state.

**F. CARDINALITY**

Current neutral-core cardinality:
- a representation-authority lineage may govern zero or more commission/fee rules or term sets over time;
- a rule/term set may generate zero or more Representation-Economics Claims;
- a Commercial Arrangement may have zero or more Representation-Economics Claims;
- each Representation-Economics Claim is scoped to exactly one Commercial Arrangement in the present neutral-core contract;
- each claim is anchored to exactly one agency-side representation-authority lineage / claimant;
- one represented-party economic component may participate in zero, one or several distinct claims when different claimants or representation-authority lineages legitimately exist;
- a single claim may depend on one or more represented-party Amount Determinations only when those determinations jointly form one source-defined basis under one independently meaningful commission treatment;
- where claimant, authority, rate/method, exclusions, lifecycle conditions or source-defined treatment differ, separate claims remain separate.

No multi-Arrangement claim is justified in the current neutral core. A representation-level retainer or fee not tied to a Commercial Arrangement is not proved by the bounded evidence and is not forced into this claim contract.

**G. GOVERNING TRUTH**

There is no universal governing date. A claim preserves source-defined governing fact(s), which may include procurement/substantial negotiation, contract or engagement formation, option exercise, service/performance completion, rebooking, receipt, termination-relative conditions, or another source-defined event. Payment/receipt may govern payability in some sources but is not a universal determinant of claim identity, claimant, historical rule or claim existence. Later recalculation must not silently substitute a current date/rule for historical governing source truth.

**H. CLAIM-SIDE AMOUNT DETERMINATION**

Representation-Economics Claim reuses the NG-7 Amount Determination semantic capability. The claim-side amount determination is distinct from the represented-party Amount Determination(s) that form its economic basis. Supported semantic shapes are inherited where source evidence requires them, including fixed/charge, percentage × identified/defined basis, contingent/unquantified method, later realization and repeated realizations/payable increments. No commission-specific formula DSL is selected.

**I. BASIS / EXCLUSIONS / CAPS / FLOORS**

Commission basis is not merely an untyped number. The source may define included economic components, excluded components, rate, cap, floor/minimum-retention rule and other qualifying conditions. These facts must remain semantically distinguishable where evidenced. No universal neutral-core taxonomy of every commission exclusion/cap/floor is selected.

**J. HISTORY / CHANGE**

Renegotiation, representation termination, new claimant, adjustment, waiver, reduction, forfeiture, settlement, correction, recalculation or supersession must not silently rewrite earlier claim truth. As applicable, preserve prior claim state, later event/source, resulting new amount/state, and correction/adjustment/supersession relationship. Correction is not original truth. Waiver/reduction is not merely recalculation. A later claim is not silent mutation of an earlier claim. The persistence/event/version mechanism remains unselected.

**K. CURRENCY / BASIS PROVENANCE**

The claim must preserve: the original represented-party basis and its definition; the original basis currency; the claim/result currency; and conversion provenance if currencies differ. Unknown currency must not be guessed. No FX engine or accounting conversion policy is selected.

**L. RECEIVABLE / INVOICE BOUNDARY**

Representation-Economics Claim is distinct from Receivable and Invoice. A claim may exist before any Receivable or Invoice. Receivable is a downstream operational fact recording a crystallized/collectible amount according to the product's finance workflow. Invoice is an optional billing artifact. The exact claim-to-Receivable cardinality is not selected.

**M. PAYMENT / ALLOCATION BOUNDARY**

Payment is cash movement. Allocation/Application is the association/application of cash to a downstream claim/Receivable or other supported finance fact. Collection may satisfy a source-defined condition of a commission claim, but payment does not create or prove the claim's historical authority. Build-97's allocation-time commission earning/posting convention is not promoted to a neutral-core invariant.

**N. ACCOUNTING BOUNDARY**

Ledger/accounting revenue recognition is a downstream projection. A Representation-Economics Claim is not an accounting revenue fact. No neutral-core revenue-recognition policy is selected.

**O. CLAIMANT / PAYER**

The claim's beneficiary/claimant is the agency-side representative under its historical representation-authority lineage. The payer/source of funds may differ. Therefore: beneficiary identity ≠ payer identity ≠ source of funds.

**P. HYPOTHESIS DISPOSITIONS**

- C0 — DERIVED ONLY: REJECTED. The evidence rejects collapse to rule + represented-party Amount Determination alone because claim-specific historical facts such as claimant lineage, procurement/tail, waiver/reduction/forfeiture and timing conditions must remain meaningful. This rejection is semantic only. It does NOT require a dedicated persisted table/entity.
- C1 — ONE CURRENT ENTITLEMENT: REJECTED. Concurrent/tailing claims, multiple claimants, component-specific treatments and historical change cannot be truthfully represented by one mutable current entitlement.
- C2 — HISTORICAL REPRESENTATION-ECONOMICS CLAIM: ACCEPTED WITH REFINEMENT. Accepted form: a historically anchored, arrangement-scoped, claimant-specific Representation-Economics Claim for one independently meaningful commission treatment, with source-defined authority and conditions, one-or-more represented-party economic basis components where they genuinely form one defined basis, distinct claim-side Amount Determination semantics, and source-specific earning/payability/collection milestones rather than a universal lifecycle.
- C3 — GENERIC MONETARY CLAIM: REJECTED. Commission, represented-party compensation, Receivable and Payment have materially different authority, beneficiary and lifecycle semantics. Shared calculation semantics remain provided through Amount Determination. No universal MonetaryClaim or EconomicEntitlement is created.

**Q. BUILD-97 PREDECESSOR DISPOSITION**

Build-97 remains a useful predecessor, particularly for: first-class CommissionRule and CommissionEntitlement separation; effective-dated historical rule rows; the contract-specific override concept; calculated snapshots; recalculation by supersession rather than overwrite; separate downstream finance facts.

Its limitations are not neutral-core invariants, including: client-scoped rule resolution despite RepresentationId; weak representation-authority anchoring; governing-date fallback to calculation date; one-obligation-only basis; SpecificTerm without executed source-term selection; post-quantification-only claim creation; adjustment/collection inconsistency; per-allocation cumulative over-recognition risk; weak commission-reversal linkage; currency/projection losses. These are predecessor findings only. This decision does not authorize their repair.

**R. EVIDENCE BOUNDARY**

- NG-8A: exact Build-97 source inspection.
- NG-8B: exactly two verticals — Film/TV representation; live music / artist booking.
- No third vertical was required. No NG-8B2 was required.

**S. CORRECTION CHAIN**

The complete NG-8 correction chain is preserved:
1. effective dating does not itself guarantee historically correct governing-rule selection;
2. Build-97 rule selection is client-scoped despite recorded RepresentationId;
3. adjustments are not fully integrated into downstream commission/revenue truth;
4. SpecificTerm lacks executed source-term resolution;
5. GrossCompensation executes against one obligation, not aggregate gross;
6. the per-allocation cap is not cumulative entitlement protection;
7. allocation reversal was not proved to reverse related commission revenue;
8. a claim may exist before the represented-party amount is finally quantified;
9. claimant/beneficiary does not imply the represented party is always the payer;
10. payment date is not a universal governing date;
11. one commission rule per client is not a neutral invariant;
12. the NG-8B proposition that "one claim tied to exactly one Amount Determination" was empirically falsified was too strong; the corrected rule permits one claim to depend on one or more represented-party Amount Determinations only where they form one source-defined basis under one independently meaningful commission treatment;
13. C0 rejection establishes independent claim semantics, not a mandatory persisted claim entity/table.

**T. TERMINAL STATUS**

NG-8 is CLOSED upon successful canonical publication of this decision. No Owner-reserved ambiguity remains within the NG-8 Scope Lock. No NG-8B2 is required.

Scope: Conceptual semantic architecture only. This decision does NOT authorize implementation; persistence design; schema; API; domain-code expansion; migrations; a claim-calculation engine; Receivable/payment/ledger implementation; or NG-9 research/design.

Evidence / provenance:
- `DECISION-20260930-002` as the Owner-approved NG-8 Scope Lock.
- `DECISION-20260930-003` as the recursive correspondence contract.
- `DECISION-20260930-001` as the NG-7 Amount Determination basis.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`; NG-8A exact source inspection.
- NG-8B bounded Film/TV + live-music evidence.
- Control Room terminal NG-8C adjudication on 2026-09-30.
- Claude reports are evidence inputs only. Claude is not architecture authority.

Consequences:
- Upon publication, NG-8 becomes CLOSED.
- C0 rejected; C1 rejected; C2 accepted with refinement; C3 rejected.
- Representation-Economics Claim becomes the accepted neutral-core semantic contract above.
- No implementation authority follows.
- Any post-NG-8 stage, including NG-9, requires explicit Owner authorization.

Supersedes: None

Unchanged:
- NG-0 through NG-7 terminal decisions remain unchanged.
- `DECISION-20260930-002` remains the Owner authorization / Scope Lock that permitted NG-8.
- `DECISION-20260930-003` remains the active recursive correspondence contract.
- Build-97 released product behavior is unchanged.
- No next-generation implementation exists.

Open:
- Implementation-open only: persisted vs derived/event/version representation; exact rule/claim placement and storage; representation-authority linkage mechanics; basis-set representation; adjustment/supersession mechanics; claim-to-Receivable cardinality; API/client/UI; downstream finance/ledger integration; evidence-triggered treatment of representation-level retainers not tied to a Commercial Arrangement.
- Post-NG-8 stage selection remains unauthorized.

Recorded by: Claude (AgencyOS executor), on Control Room instruction. Claude is recorder/executor only, not authority.

Publication receipt: 3672ed2fd127c36c855708cc0964739685d30c6d on origin/operational-regression-gate; remote readback verified 2026-09-30T15:51:47Z

## DECISION-20260930-005

Status: ACTIVE

Date: 2026-09-30

Authority: OWNER

Question: Approve NG-9 — Receivable Crystallization & Collectibility Architecture exactly under the complete bounded Control Room stage definition below, for research/design only, with no implementation, product/schema/API/domain expansion, migrations or NG-10 authority?

Decision: The Owner approves **NG-9 — Receivable Crystallization & Collectibility Architecture** exactly under the following bounded stage contract.

**TITLE**

NG-9 — Receivable Crystallization & Collectibility Architecture

**BOUNDED QUESTION**

What is the minimum neutral-core semantic contract for Receivable as a downstream crystallized/collectible fact: when economic truth or a Representation-Economics Claim becomes a Receivable; what a Receivable canonically asserts; how its source/provenance is preserved; what minimum cardinality is justified between upstream source facts and Receivable; how installments, partial or periodic crystallization, due dates, adjustments, credits, corrections, cancellation and settlement remain historically truthful; how Receivable relates to Invoice; and where its boundary lies relative to Payment, Allocation/Application and Ledger — without selecting persistence or implementing accounts receivable.

**IN SCOPE**

1. The semantic meaning of Receivable as a downstream crystallized/collectible fact.
2. The distinction among: upstream economic truth; Representation-Economics Claim; Receivable; Invoice; Payment; Allocation/Application; Ledger/accounting projection.
3. What source-defined fact or facts make an upstream economic position crystallized/collectible enough to constitute a Receivable.
4. Receivable source/provenance, including linkage where applicable to: Amount Determination; Representation-Economics Claim; Commercial Arrangement; Agreement Snapshot; Legal Instrument; external governing schedule/rule; later source-defined operating or crystallization event.
5. Source-to-Receivable cardinality.
6. Receivable-to-source cardinality.
7. Partial crystallization.
8. Installment-based crystallization.
9. Periodic or repeated Receivables arising from one governing upstream economic method or claim.
10. Whether several upstream economic components may legitimately form one Receivable only where one source-defined collectible position genuinely groups them.
11. Debtor/obligor identity.
12. Beneficiary/creditor identity.
13. Payer/source-of-funds identity.
14. Due-date semantics and provenance.
15. Collectibility semantics without asserting universal legal enforceability.
16. Receivable existing without an Invoice.
17. Invoice relationship where a billing artifact exists.
18. Adjustment, correction, credit, cancellation, settlement or dispute only insofar as they change Receivable truth.
19. Historical preservation: prior Receivable truth; later correction or adjustment; supersession or cancellation; no silent rewriting.
20. Currency and amount provenance inherited from NG-7 and NG-8.
21. Boundary to Payment and Allocation/Application.
22. Boundary to Ledger/accounting projection.
23. Film/TV and live-music vertical semantics only as falsification pressure against false neutrality.

**OUT OF SCOPE**

Do NOT solve or select: persistence/database/schema/FKs; EF mappings; API/classes/interfaces/modules; Receivable table/entity shape; invoice generation; invoice rendering; invoice numbering; invoice delivery; payment rails; payment processing implementation; bank reconciliation; allocation algorithm implementation; dunning workflow; collection workflow; trust-account implementation; ledger posting implementation; revenue-recognition policy; accounting write-off policy; bad-debt accounting; tax/withholding; FX engine; payroll; accounts-payable architecture; royalty-accounting implementation; legal-enforceability engine; a generic MonetaryClaim; a universal EconomicEntitlement; product code; migrations; NG-10.

**CLOSED DISTINCTIONS — DO NOT REOPEN WITHOUT CONTRADICTORY EVIDENCE**

- Commercial Arrangement ≠ Legal Instrument.
- Amount Determination ≠ Receivable.
- Representation-Economics Claim ≠ Receivable.
- Receivable ≠ Invoice.
- Payment ≠ Allocation/Application.
- Payment is cash movement.
- Allocation/Application is the application/association of cash to a downstream supported finance fact.
- Ledger/accounting is downstream projection.
- Historical truth must not be silently rewritten.
- Money preserves currency.
- beneficiary/claimant ≠ payer ≠ source of funds.
- No universal MonetaryClaim.
- No universal EconomicEntitlement.
- No generic Entity / Party / Transaction abstraction.
- No universal lifecycle merely because several downstream facts have statuses.

**HYPOTHESES**

No hypothesis is preselected.

- R0 — SOURCE-SINGULAR CURRENT: Receivable is semantically distinct, but each upstream economic source yields at most one mutable current Receivable, and one current amount/due state is sufficient.
- R1 — INVOICE-GATED: Receivable is distinct from Invoice, but cannot crystallize before an Invoice or equivalent billing artifact exists.
- R2 — HISTORICAL CRYSTALLIZATION: Receivable is a distinct historically anchored collectible fact. It may arise without an Invoice. One upstream source may crystallize into several Receivables over time. Partial, installment and periodic crystallization may be semantically meaningful. One Receivable may aggregate several upstream facts only where one source-defined collectible position actually groups them. Earlier Receivable truth is not silently overwritten by later adjustment/correction/cancellation/settlement.
- R3 — DEBTOR-ACCOUNT AGGREGATE: The neutral-core primitive is essentially the debtor/account running balance. Individual historically source-anchored Receivable identity is unnecessary.

**AUTHORIZED RESEARCH BOUNDARY**

Exactly two verticals:
- A. Film/TV representation — used only as relevant to Receivable falsification: compensation installments; residual/reuse or other periodic amounts where primary evidence supports them; due/payment mechanics; invoice or loan-out billing scenarios where authoritative evidence supports them; payer/debtor distinctions; representation-economics claim only as an upstream boundary test.
- B. Live music / artist booking — used only as relevant to Receivable falsification: deposits; balances; performance/completion timing; settlement; cancellation/refund consequences; purchaser/promoter obligations; invoice/billing mechanics where authoritative evidence supports them; agency commission only as an upstream boundary test.

Do NOT add a third vertical merely for confidence or breadth. If these two leave one concrete ambiguity that genuinely blocks terminal adjudication, return that exact ambiguity to the Control Room.

**SUBSTAGES**

- NG-9A — Build-97 Receivable Predecessor Baseline: exact Build-97 source inspection only. Inspect Receivable; Invoice; MonetaryObligation; CommissionEntitlement only where needed for source boundary; Payment; Allocation/Application; relevant ledger path; persistence/query truth; API/client projection only where semantically relevant. Establish what Receivable canonically means in Build-97; source linkage; amount/currency; due-date semantics; amount overrides; status/outstanding derivation; adjustment/correction/cancellation behavior; invoice relation; payment/allocation relation; history; cardinalities visible in source; persisted versus derived facts. No external research. No terminal architecture conclusion. No R0–R3 winner.
- NG-9B — Two-Vertical Falsification: use exactly Film/TV representation and live music / artist booking. Test R0–R3. Separate empirical source fact, source interpretation and architecture pressure. Do not make terminal architecture decisions.
- NG-9B2 — OPTIONAL ONLY IF REQUIRED: at most one focused evidence prompt, allowed only if NG-9B leaves one genuine blocking ambiguity that prevents Control Room adjudication. No third vertical. Not automatic.
- NG-9C — CONTROL ROOM TERMINAL ADJUDICATION: the Control Room decides Receivable semantic meaning; crystallization/collectibility semantics; source/provenance; source↔Receivable cardinality; partial/installment/periodic truth; debtor/beneficiary/payer distinctions; due-date semantics; Invoice boundary; correction/adjustment/cancellation/settlement history; Payment/Allocation boundary; accounting boundary; R0–R3 dispositions; whether any genuine Owner-reserved ambiguity remains. Claude is never the terminal architecture authority.
- NG-9D — CANONICAL PUBLICATION / CLOSURE: publish the accepted terminal architecture only after Control Room adjudication. No implementation authority follows.

**EXIT CRITERIA**

NG-9 is READY TO CLOSE only when evidence is sufficient to state semantically:
1. what a Receivable is;
2. what makes it exist/crystallize;
3. whether an Invoice is required;
4. minimum source-to-Receivable cardinality;
5. minimum Receivable-to-source cardinality;
6. how partial crystallization is represented semantically;
7. how installment and periodic Receivables remain truthful;
8. whether several upstream economic facts may form one Receivable and under what bounded semantic condition;
9. debtor/obligor identity;
10. beneficiary/creditor identity;
11. payer/source-of-funds identity;
12. due-date semantics and historical provenance;
13. collectible versus merely economically determined truth;
14. historical treatment of adjustment/correction/credit/cancellation/settlement/dispute without silent rewrite;
15. currency/amount provenance;
16. boundary to Invoice;
17. boundary to Payment;
18. boundary to Allocation/Application;
19. boundary to Ledger/accounting;
20. R0–R3 dispositions;
21. confirmation that persistence/schema/API/workflow mechanics remain unselected;
22. no unexplained conflict with NG-1 through NG-8 remains.

NG-9 becomes CLOSED only after terminal Control Room adjudication is canonically PUBLISHED. Green tests, Claude confidence or a research report cannot close NG-9.

**OWNER-DECISION RULE**

Do not send routine architecture questions to the Owner merely because several implementation shapes remain possible. Owner decision is required only if bounded evidence leaves a genuine surviving product/strategy choice within Owner-reserved authority. A contradiction with an earlier closed decision is a correction-chain event, not automatically an Owner preference question.

**PROMPT BUDGET**

Initial NG-9 substantive research budget: approximately 2–4 Claude prompts. Expected: NG-9A: 1; NG-9B: 1; NG-9B2: at most 1 if genuinely required. NG-9C is Control Room adjudication. Publication and seal prompts do not count as substantive research prompts.

**AUTHORITY BOUNDARY**

This authorization is for conceptual research/design only. It does NOT authorize: implementation; persistence; product code; schema/API/domain expansion; migrations; accounts-receivable implementation; invoice implementation; payment/allocation implementation; ledger implementation; NG-10.

Scope: Research/design and conceptual semantic architecture only. NO authority for implementation, product code, schema/API/domain expansion, migrations or NG-10.

Evidence / provenance:
- Explicit Owner approval in the Control Room conversation on 2026-09-30.
- `DECISION-20260930-004` / `DELTA-20260930-003` as the terminal NG-8 basis.
- `DECISION-20260930-003` as the active Recursive Control-Room Correspondence & Transition Contract.
- The complete Control Room NG-9 stage definition, approved by the Owner.
- Claude is recorder/executor only, not decision authority.

Consequences:
- Once this Owner decision is PUBLISHED, NG-9 is AUTHORIZED and SCOPE-LOCKED for research/design only.
- NG-9A becomes the exact next bounded substantive action.
- No implementation authority follows.
- NG-10 remains unauthorized.

Supersedes: The active current-state claim that no post-NG-8 stage is authorized. It supersedes no prior Decision ID.

Unchanged:
- Operational Closure remains COMPLETE.
- Self-Update V1 remains COMPLETE.
- NG-0 through NG-8 remain closed/approved exactly under their existing terminal decisions.
- `DECISION-20260930-003` remains the active recursive correspondence contract.
- Build-97 released product behavior remains unchanged.
- No next-generation implementation exists.
- No product/schema/API/domain/migration authority follows.

Open:
- NG-9A evidence.
- NG-9B evidence.
- The terminal R0–R3 disposition.
- The terminal NG-9 semantic architecture.
- All implementation mechanics.
- NG-10 and all later stages.

Recorded by: Claude (AgencyOS executor), on explicit Owner instruction transmitted by the Control Room. Claude is recorder/executor only, not authority.

Publication receipt: e5561e87ec6452eb48b0607d0dadbacf616d8c6f on origin/operational-regression-gate; remote readback verified 2026-09-30T17:31:14Z

## DECISION-20260930-006

Status: ACTIVE

Date: 2026-09-30

Authority: CONTROL_ROOM

Question: What is the minimum neutral-core semantic architecture for Receivable as a downstream crystallized/collectible fact: what makes one exist; how it relates to upstream Amount Determinations and Representation-Economics Claims; what source↔Receivable cardinalities are justified; how partial/installment/periodic crystallization, due/payability provenance and historical changes are preserved; how debtor/creditor/payer roles differ; and where Receivable ends relative to Invoice, Payment, Allocation/Application and accounting — under the exact NG-9 Scope Lock of DECISION-20260930-005?

Decision: NG-9 — Receivable Crystallization & Collectibility Architecture is terminally adjudicated as follows.

**A. RECEIVABLE**

A Receivable is a historically anchored, Commercial-Arrangement-scoped collectible position for one independently meaningful source-defined crystallization/due position. It preserves:
- the upstream economic/source provenance explaining why the position exists;
- the creditor/beneficiary;
- the obligor/debtor;
- the crystallized amount and currency;
- source-defined crystallization conditions;
- the source-defined due/payability rule and resolved date when known;
- historical facts that later adjust, transfer, reduce, satisfy, supersede or extinguish the position.

Receivable does not itself assert universal legal enforceability. This is semantic architecture only; it does not require a dedicated persisted table/entity or select persistence mechanics.

**B. UPSTREAM ECONOMIC TRUTH IS DISTINCT**

Amount Determination is not Receivable. Representation-Economics Claim is not Receivable. An upstream method, contingent economic entitlement, future installment or representation-economics claim may exist before any Receivable. Receivable arises only when source-defined facts are sufficient to identify one specific collectible position. A merely known method whose required realization facts remain unknown is not silently converted into a zero-valued Receivable.

**C. NO UNIVERSAL CRYSTALLIZATION TRIGGER**

There is no universal event that creates every Receivable. Source-defined crystallization facts may include: delivery; service/performance; completion; scheduled installment; statement/accounting cycle; settlement; receipt; cancellation consequence; invoice or another required document; another source-defined event. The neutral core does not impose contract execution, Invoice, Payment or accounting posting as universal creation triggers.

**D. INVOICE BOUNDARY**

Receivable is distinct from Invoice. An Invoice is not universally required for Receivable existence; a Receivable may exist without any Invoice. However, a source may make an Invoice, statement or other document a condition of crystallization; a condition of payability; a timing trigger; or merely an administrative/billing artifact. Where such a relation is known, its provenance must remain explicit. No universal Invoice gate is selected.

**E. SOURCE TO RECEIVABLE CARDINALITY**

One upstream economic source may produce zero or more Receivables over time. A Commercial Arrangement, economic component, Amount Determination or Representation-Economics Claim may yield multiple distinct collectible positions where source-defined installments, periods, milestones, performances, statements, settlements or similar events create independently meaningful crystallizations. Build-97's one-MonetaryObligation/one-Receivable lifecycle behavior is not a neutral invariant.

**F. RECEIVABLE TO SOURCE CARDINALITY**

A single Receivable may depend on one or more upstream economic facts only where those facts genuinely form one source-defined collectible position. Presentation together on an Invoice, statement or debtor balance does not by itself merge their semantic identity. Where obligor, creditor/beneficiary, currency, crystallization condition, due/payability rule or independently meaningful treatment differ, the positions remain distinct. Known source/component contribution must not be erased by untyped aggregation.

**G. COMMERCIAL ARRANGEMENT SCOPE**

In the present neutral-core contract, each Receivable is scoped to exactly one Commercial Arrangement. The bounded evidence did not justify one indivisible collectible position spanning several independently valid Commercial Arrangements. Reconsider only on concrete evidence of one indivisible real collectible position that genuinely spans two or more independently valid Commercial Arrangements.

**H. PARTIAL / INSTALLMENT / PERIODIC CRYSTALLIZATION**

Partial crystallization is semantically distinct from partial payment. When only part of upstream economic truth crystallizes into a collectible position while the remainder remains future, contingent or uncrystallized, the crystallized portion must remain separately identifiable. Installment and periodic source-defined due positions may therefore create multiple Receivables over time. By contrast, a partial Payment or Allocation against an already-existing Receivable does not itself create a new Receivable.

**I. AMOUNT TRUTH**

The Receivable must preserve its crystallized amount and the upstream provenance of that amount. If a Receivable represents only a portion of a larger upstream economic amount, that portion and its source relation must remain truthful. An unlabelled amount override that disconnects Receivable truth from upstream economic truth is not a neutral-core invariant. Where several upstream economic facts form one Receivable, known contribution/provenance remains identifiable.

**J. DUE / PAYABILITY TRUTH**

The source-defined due/payability rule is distinct from a resolved calendar date. A Receivable may carry a rule/trigger whose exact date is unresolved until another fact occurs. Unknown due date remains unknown. When a due/payability fact changes through amendment, extension, waiver, correction, rescheduling or another source-defined event, prior truth is not silently overwritten.

**K. OBLIGOR / CREDITOR / PAYER / RECIPIENT**

The neutral core distinguishes: obligor/debtor; creditor/beneficiary; observed payer/source of cash; recipient/conduit when source evidence makes that distinction material. These roles are not universally identical. Payment by a payroll house, agency, guild, manager, client account or other conduit does not by itself change who owed the Receivable.

**L. OBLIGOR CHANGE / ASSUMPTION**

Source-defined assumption, transfer or release may change which party bears the obligation. Preserve: prior obligor truth; assumption/transfer source; subsequent obligor; release or continuing-liability semantics where known. No silent debtor overwrite is allowed. Whether implementation represents this as one Receivable identity with obligor history or as a successor Receivable relation remains unselected.

**M. PAYMENT MAY PRECEDE RECEIVABLE**

Cash movement may occur before a later collectible position crystallizes. Deposit, advance, prepayment, escrow or trust semantics are source-defined and must not be collapsed into one universal category. The existence of Payment/cash does not prove a Receivable existed at that moment. Later Allocation/Application may associate previously received cash with a Receivable once the relevant collectible position exists. No trust-account implementation is selected.

**N. HISTORICAL CHANGE**

Receivable is not merely a mutable current balance. Source-defined cancellation, correction, credit, reduction, settlement, dispute, assumption, supersession or extinguishment must preserve historical truth. As applicable, preserve: the earlier collectible position; the later event/source; resulting amount or status truth; the relation between old and new facts. Correction is not original truth. Credit/reduction is not Payment. Cancellation does not universally mean the earlier position never existed. Dispute does not universally mean the amount is zero. No universal Receivable lifecycle enum is selected.

**O. ACCOUNTING WRITE-OFF**

An accounting or operational write-off is downstream policy/action and does not, by itself, prove that the source-defined Receivable never existed or was legally extinguished. Receivable truth is distinct from accounting recognition and collection policy. Build-97's ledger posting when a Receivable is raised is not promoted to a neutral-core invariant.

**P. DOCUMENT TYPES**

Invoice, residual statement, payroll statement, settlement sheet, ticket audit, remittance advice and booking confirmation are not universally the same semantic artifact. A source-defined document may determine an amount; trigger payability; establish timing; communicate an already-existing Receivable; or serve as evidence after the fact. No universal BillingDocument abstraction is selected.

**Q. PAYMENT / ALLOCATION BOUNDARY**

Payment is cash movement. Allocation/Application is the association/application of cash to one or more supported downstream positions. Receivable is distinct from both. A Receivable may be satisfied by several Payments. A Payment may be associated with several Receivables where supported. The debtor's aggregate balance is a projection across Receivables and applications, not a replacement for Receivable identity. Exact implementation mechanics remain unselected.

**R. CURRENCY**

Receivable preserves the currency and monetary provenance of the collectible position. Payment currency may be a distinct fact. Where conversion occurs, known conversion provenance must remain available. No FX engine or accounting conversion policy is selected.

**S. RECONSIDERATION TRIGGERS**

The current neutral core does not add a multi-Arrangement Receivable or a simultaneous multi-obligor Receivable. Reconsider only on concrete evidence of:
1. one indivisible real collectible position spanning two or more independently valid Commercial Arrangements; or
2. one indivisible collectible position with genuinely simultaneous co-obligors that cannot be represented truthfully by the current single-obligor contract.

**T. HYPOTHESIS DISPOSITIONS**

- R0 — SOURCE-SINGULAR CURRENT: REJECTED. One upstream source may generate several independently triggered collectible positions, and one mutable current amount/due state cannot preserve the necessary history.
- R1 — INVOICE-GATED: REJECTED. Invoice is not a universal prerequisite. Some sources may nevertheless make Invoice or another document a source-defined crystallization/payability condition.
- R2 — HISTORICAL CRYSTALLIZATION: ACCEPTED WITH REFINEMENT. Accepted form: a Receivable is a historically anchored, Commercial-Arrangement-scoped collectible position for one independently meaningful source-defined crystallization/due position. It preserves upstream economic provenance, creditor/beneficiary, obligor, amount and currency, source-defined crystallization and due/payability semantics, and later historical transformations. One upstream economic source may produce zero or more Receivables over time. One Receivable may depend on one or more upstream economic facts only where they genuinely form one source-defined collectible position. Invoice may be absent or participate as a source-defined condition. Payment may precede Receivable, and cash application remains distinct.
- R3 — DEBTOR-ACCOUNT AGGREGATE: REJECTED. A debtor/account running balance cannot preserve source identity, crystallization/due provenance, installment/periodic positions, disputes, assumptions, cancellation consequences or source-defined grouping. Shared balance arithmetic is a projection, not the neutral-core identity.

**U. BUILD-97 PREDECESSOR DISPOSITION**

Build-97 remains useful precedent for: first-class Receivable identity; explicit upstream MonetaryObligation linkage; immutable OriginalAmount; separate Payment and Allocation facts; persisted allocation/reversal and adjustment history; Receivable distinct from Invoice; closure reasons; currency discipline; downstream ledger attribution.

Its limitations are not neutral-core invariants, including: one source effectively yielding only one Receivable through the Raised lifecycle; unconstrained/unlabelled amount override; stranded remainder after partial override; due-date overwrite without provenance; Invoice/Receivable Contract and debtor consistency gaps; repeated Invoice-line overbilling exposure; Payment payer not checked against Receivable debtor; cancelled Receivable leaving the source obligation Raised; Build-97 ledger recognition timing. This decision does not authorize repair of those predecessor limitations.

**V. EVIDENCE BOUNDARY**

- NG-9A: exact Build-97 source inspection at `b3f41bfd68e81ab42da899671f58e01f0988d3d2`.
- NG-9B: exactly two verticals — Film/TV representation; live music / artist booking.
- Load-bearing primary evidence independently checked by the Control Room includes: the 2023 WGA Theatrical and Television Basic Agreement; the Musicians' Union Standard Live Engagement Contract L2 (2025 form); AFM Form T2C Travelling Engagement Contract; the UK Conduct of Employment Agencies and Employment Businesses Regulations 2003, regulation 25; the University of Memphis Performance Agreement.
- Historical/time-bounded sources are used as falsification evidence only and are not generalized as current universal industry rules.
- No third vertical was required. No NG-9B2 was required.

**W. CORRECTION CHAIN**

Inherited chains are preserved: NG-6 under `DECISION-20260929-008`; NG-7 under `DECISION-20260930-001`; NG-8 §S under `DECISION-20260930-004`.

NG-9A:
1. one-obligation/one-Receivable is application/domain lifecycle behavior, not persistence uniqueness;
2. the Receivable amount can override the source amount without structured provenance, and a partial override can strand the remainder;
3. Invoice lacks source Receivable Contract/Debtor consistency and global duplicate-billing protection;
4. due-date provenance is not historically preserved;
5. Build-97 does not enforce observed Payment payer = contractual Receivable debtor;
6. Build-97 gives mixed, not uniformly supporting, predecessor pressure on R0.

NG-9B:
7. installments may be separately triggered source-defined due positions, not merely partial cash payments;
8. the obligor may change by source-defined assumption;
9. cash may precede entitlement/collectibility;
10. Invoice may condition payability/timing in particular sources even though it is not a universal prerequisite;
11. source rules may group several facts into one due position or defer payability until an accumulation threshold.

NG-9C:
12. rejecting universal invoice-gating does not make Invoice semantically irrelevant; a source may make it a crystallization/payability condition;
13. one source having several events does not mean every event creates a Receivable; identity follows the independently meaningful source-defined collectible position;
14. evidence of obligor assumption proves historical obligor provenance is required, but does not decide same-identity versus successor-Receivable implementation;
15. cash preceding entitlement does not make every advance an unapplied Payment; deposit/prepayment/trust classification remains source-defined.

**X. TERMINAL STATUS**

NG-9 is CLOSED upon successful canonical publication of this decision. No Owner-reserved ambiguity remains within the NG-9 Scope Lock. No NG-9B2 is required.

Scope: Conceptual semantic architecture only. This decision does NOT authorize implementation; persistence selection; schema; API; domain-code expansion; migrations; AR workflow implementation; Invoice implementation; Payment/Allocation implementation; ledger implementation; trust-account implementation; FX implementation; or NG-10 research/design.

Evidence / provenance:
- `DECISION-20260930-005` as the Owner-approved NG-9 authorization and exact Scope Lock.
- `DECISION-20260930-003` as the recursive correspondence contract.
- `DECISION-20260930-004` as the NG-8 Representation-Economics Claim boundary.
- `DECISION-20260930-001` as the NG-7 Amount Determination boundary.
- Build-97 product commit `b3f41bfd68e81ab42da899671f58e01f0988d3d2`; NG-9A exact source inspection.
- NG-9B bounded Film/TV + live-music evidence.
- Control Room independent primary-source verification and terminal NG-9C adjudication on 2026-09-30.
- Claude reports are evidence inputs only. Claude is not architecture authority.

Consequences:
- Upon publication, NG-9 becomes CLOSED.
- R0 rejected; R1 rejected; R2 accepted with refinement; R3 rejected.
- Receivable Crystallization & Collectibility Architecture becomes terminal under the semantic contract above.
- No implementation authority follows.
- Any post-NG-9 stage, including NG-10, requires explicit Owner authorization.

Supersedes: None

Unchanged:
- Operational Closure remains COMPLETE.
- Self-Update V1 remains COMPLETE.
- NG-0 through NG-8 remain unchanged.
- `DECISION-20260930-005` remains the Owner authorization/Scope Lock that permitted NG-9.
- `DECISION-20260930-003` remains the recursive correspondence contract.
- Build-97 released product behavior remains unchanged.
- No next-generation implementation exists.

Open:
- Implementation-open only: persisted entity versus event/version/derived representation; exact source-contribution representation; same-identity versus successor representation for obligor assumption; Invoice linking mechanics; Payment/Allocation mechanics; trust/prepayment storage; dispute/correction mechanics; API/client/UI; downstream ledger integration.
- Evidence-triggered reconsideration only: multi-Arrangement Receivable; simultaneous multi-obligor Receivable.
- Post-NG-9 stage selection remains unauthorized.

Recorded by: Claude (AgencyOS executor), on Control Room instruction. Claude is recorder/executor only, not authority.

Publication receipt: Pending
