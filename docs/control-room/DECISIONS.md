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
