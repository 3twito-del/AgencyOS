# ADR-0022 — Contract model, reconciliation and legal honesty

- **Status:** Accepted
- **Date:** 2026-09-08
- **Milestone:** M8 — Contracts, Rights, Options & Obligations
- **Supersedes:** nothing
- **Builds on:** ADR-0011 (tenant isolation), ADR-0012 (curated history vs audit),
  ADR-0013 (synchronization), ADR-0014 (concurrency and idempotency),
  ADR-0017 (note sensitivity), ADR-0020 (recorded, not sent),
  ADR-0021 (deal rules kernel, offer immutability, agreed terms)

## Context

M7 stops at *Terms Agreed*: an accepted offer, a frozen commercial snapshot, and
no claim whatsoever about paper. M8 takes over from there and models the
instrument — the contract, its drafts, the rights it grants, the elections it
creates, the duties it imposes and the notices it requires.

Legal work is the part of an agency where a system's small dishonesties become
expensive. A date that was guessed, a status that was inferred, a difference that
was quietly judged, a document the system claims to hold and does not — each of
these reads as authoritative on a screen and is wrong in a place nobody checks
until it matters. Every decision below is a decision about which claims AgencyOS
is entitled to make.

## Decisions

### 1. A contract anchors to a deal and its accepted offer, and never moves

A contract carries `deal_id` and `accepted_offer_id`, both fixed at creation. The
accepted offer is the baseline reconciliation compares every draft against, so a
movable baseline would make "does the paper say what we agreed" unanswerable.

The pair is enforced structurally by a composite foreign key over
`(organization_id, deal_id, accepted_offer_id)` into the alternate key M7 already
carries on `offers`. That the offer is *accepted* is checked by the handler and
deliberately **not** by a constraint: acceptance is a status the offer row carries
and M7 can legitimately move it — reopening a negotiation unwinds an agreement —
so a key that pinned it would turn a lawful M7 act into a constraint violation on
an M8 row.

**A deal may carry several contracts.** A long form, a side letter and an
amendment are three instruments about one negotiation.

**Creating a contract mutates nothing in M7.** The deal stays *TermsAgreed*, the
accepted offer stays exactly what it said, and M6's target stays where the pipeline
left it.

### 2. Four dates, never collapsed

`signed_on` (per signature), `executed_on`, `effective_on` and `terminated_on` are
four separate facts and the schema, the API and the Windows surface keep them
apart.

- **Executed** is derived when the last required signatory signs, and dated from
  the day of that signature rather than the day it was typed in. Signatures are
  routinely recorded after the fact.
- **Effective** is recorded separately and may precede execution. An agreement
  effective as of January and signed in March is ordinary, so there is
  deliberately **no** constraint ordering the two.
- **Terminated** is recorded with its own date and cannot precede effectiveness.

### 3. Execution follows from signatures and from nothing else

`ContractTransition` has eight members. `SignatureRecorded` and
`ExecutionCompleted` are marked not caller-requestable in the F# kernel, and the
status route refuses them by name. The only way to reach `PartiallyExecuted` or
`Executed` is `RecordSignature`.

There is no arbitrary status PATCH anywhere in the milestone.

### 4. AgencyOS records a reference to a document, and says so

A `ContractVersion` carries an external reference, a source system, a display file
name and a media type. It carries **no content hash**, because AgencyOS has never
seen the bytes and a hash it did not compute would be a claim about identity it
cannot support.

`ContractVersion.HoldsDocument` is a property returning `false`, surfaced through
the API and shown on the Windows page, so a client states the truth rather than
letting a reader assume. Canonical document storage is M10; these fields are the
seam it will fill.

Nothing copies files into folders, persists local Windows paths as canonical data,
or builds a shadow blob store.

### 5. A party is somebody AgencyOS knows, or a name it was told

`contract_parties` carries `person_id`, `company_id` and `external_name`, with a
check constraint that exactly one is populated. Where the agency already has the
record, the identifier is used: a free-text name recorded beside a real company is
how a party silently stops being the same party.

**Role is not type.** The studio on one paper is the licensee on another, and both
are the same company record. `ContractPartyRole` describes what a party is doing in
this agreement and says nothing about what kind of record it is.

Every row that names a party — signatures, grants, options, obligations, notice
requirements, notice records — points at it through a composite foreign key over
`(organization_id, contract_id, party_id)`, so a row cannot reach a party on a
different instrument in the same tenant.

### 6. Reconciliation reports what differs, never whether it matters

`Reconciliation.compare` is built on the same F# comparison the M7 offer thread
uses, because it is the same operation: two sets of typed terms joined on code. It
produces five outcomes — `Matched`, `Changed`, `MissingFromContract`,
`AddedInContract`, `NotComparable` — and a direction that reports movement, never
merit.

There is deliberately no `Favourable`, `Unfavourable`, `Material` or `Risk`.
Whether a change is acceptable is a legal question about a document AgencyOS has
not read, and a system that answered it would be answering it wrongly some of the
time in a place nobody checks.

The comparison is a **stateless projection**. Nothing is written, nothing cached,
and the difference count on a contract summary is recomputed on every read from
the accepted offer's terms and the newest version's. A stored count would be right
until the next version was recorded and wrong afterwards, and nobody would know
which.

### 7. One vocabulary, one money system

`ContractTermCode` shares its integer values with `DealTermCode` for every
commercial term, and `ContractTermCatalog` derives its commercial half from
`DealTermCatalog` rather than retyping it. Two hand-maintained copies of one
vocabulary would disagree, and the disagreement would show up as a reconciliation
that quietly stopped matching.

`contract_terms` has the same column shape as `offer_terms`, the same value-kind
check constraint, and the same `numeric` money columns. A second money
representation would have made reconciliation a conversion.

### 8. A grant is what the contract says, not a finding about title

A `RightsGrant` row means an instrument used those words. It is not a chain of
title, not a verification that the grantor held what they purported to grant, and
not a clearance. The wording throughout the API and the UI says so.

Amendments **supersede** rather than overwrite. A grant that ran worldwide and
became United States only is two rows, each pointing at the version that created
it, so "what did we believe we had before the amendment" stays answerable.

`GrantPeriodKind` has an `Unstated` member, and a period that is unstated never
covers a date and never overlaps another. A contract that says nothing this build
can structure is recorded as saying nothing rather than as running forever.

**No geopolitical ontology.** `RightsTerritory` has four coarse members and a
`Specified` case whose detail is the clause's own words.

### 9. Deadlines resolve honestly or not at all

A `DeadlineRule` is absolute (a stated date), relative (an anchor, an offset and a
unit) or unstructured (the clause's own wording). Resolution is a pure function in
the F# kernel and returns nothing in three cases, each of which the system can
name:

- the contract states no structured rule;
- the anchor event has not happened;
- the rule counts **business days**, and AgencyOS holds no holiday calendar.

The third is the important one. Business-day rules are accepted and stored
faithfully, and then decline to produce a date. Counting them as calendar days
would put a legal deadline in a lawyer's calendar that looks authoritative and is
wrong. A row whose deadline did not resolve is **absent** from every work queue
rather than guessed onto a day, and the client surfaces the reason rather than a
blank column.

### 10. Nothing lapses, exercises or breaches on its own

- An option past its deadline stays `Available` until somebody records that it
  lapsed. `IsPastDeadline` is derived and shown; `Expired` is an act.
- An exercise is never inferred from a payment. Money is M9's subject.
- An obligation past its date is `IsPastDue`, which is a fact about a date.
  `Breached` is a determination a person makes, requires a stated reason, and is
  refused without one.

The distinction between the derived fact and the recorded judgement is carried
through the domain, the API, the client view models and the dialogs, and is
covered by tests at each level.

### 11. Obligations are not tasks

An obligation is a contractual duty. A task is something somebody has to do this
week. Turning every obligation into a task would fill the list with entries nobody
has to act on, and the list would stop being read.

`contract_task_links` connects the two when a person asks for it, on the M6 and M7
precedent, and never automatically.

### 12. Notices are recorded, not sent

AgencyOS has no outbound transport and cannot confirm delivery. Every route, every
counter and every screen says *record*: `RecordNotice`, `agencyos.notice.recorded`,
"Recorded, not sent". A `NoticeRecord` is an assertion that a notice passed
between the parties, exactly as an M6 submission is an assertion that material went
out. There is no delivery-status column, because the system has no way to know one.

### 13. No electronic signature, and no verification claim

`RecordSignature` stores who signed, on what date, by what method, and optionally
where the signed copy lives. There is no certificate, no key, no hash and no
verification. The dialog and the API documentation both say so.

### 14. Privilege is assigned, never inferred

`PrivilegeClass` defaults to `Ordinary`, so nothing becomes privileged by accident.
A person classifies a contract, a term or an obligation as `Confidential`,
`LegalStrategy` or `AttorneyClientPrivileged`, and AgencyOS never decides for
itself. A term containing the word "privileged" is not privileged; a term somebody
marked as privileged is.

Getting this wrong is asymmetric: withholding what could be shared is an
inconvenience, and disclosing what could not is not.

### 15. Three redaction rules, and no count of what was withheld

`ContractRedaction` holds the only implementation:

| Content | Requires |
|---|---|
| Drafted terms at all | `contracts.terms.read` |
| The figures inside them | `deals.economics.read` (the same grant M7 uses) |
| Legal analysis, strategy, privileged terms and obligations | `contracts.privileged.read` |

Everything is **removed** rather than marked. A redacted list is simply shorter and
carries no count, because "3 terms withheld" tells a reader that three sensitive
terms exist, which is most of what they wanted to know.

Reconciliation is the one place that **refuses** rather than redacts, on the M7
precedent: a comparison with the terms stripped out would report that a draft
matched what was agreed when it did not, and somebody would sign on that.

**Search indexes title, reference and factual summary only.** Legal analysis,
strategy notes and every term value are absent from the `tsvector`. A redaction
that leaves a search hit behind is not a redaction, and an integration test proves
a phrase appearing only in privileged content surfaces nothing.

Telemetry carries identifiers, operation types and counts, and never a term value,
a clause or anything a person classified as privileged.

### 16. Derived, not stored

Nothing beside the aggregate duplicates what the aggregate decides. Execution
state, outstanding signatures, effectiveness today, overdue obligations, options
past their deadline, the next legal date, the difference count and the whole legal
deadline list are all computed at read time from the rows that recorded the events.

There is no deadlines table. One would need a source identifier no foreign key
could constrain, and it would be a second copy of dates the option, obligation and
notice rows already hold.

### 17. An amendment is a separate instrument

An amendment is not version five of the paper it changes. It is its own contract,
with its own parties, its own drafting versions and its own execution and effective
dates, connected through `contract_relationships`. The original learns that an
amendment points at it without the amendment becoming one of its drafts.

### 18. One immutability trigger

`contract_terms` carries a `BEFORE UPDATE OR DELETE` trigger refusing changes once
the parent version is no longer a draft. The domain refuses first; the trigger
exists so a future code path that forgets fails loudly instead of quietly
rewriting what a draft is recorded as having said. It is the only trigger M8 adds.

### 19. Temporal is not introduced

The four questions M8 was asked to answer before adopting a durable workflow
engine:

1. **What durable, multi-step, long-running process exists?** None. Every M8
   operation is a single transaction. Deadlines are dates on rows, not timers.
2. **What is being retried or compensated?** Nothing. There is no external system
   to call, no notice to send, no document to fetch.
3. **What would fail without it?** Nothing in M8. The work queues are queries over
   resolved dates, and the milestone deliberately performs no scheduled action.
4. **What is the operational cost?** A server, a worker, a second failure domain
   and a second source of truth about what has happened — against no capability.

**Decision: not adopted.** The question reopens when something must happen on a
date without a person triggering it, which M8 explicitly does not do.

### 20. No TLA+ specification

M8 introduces no new distributed or concurrent protocol. Its state machines are
finite and small — contract 8×8, option 6×5, obligation 5×5 — and are proved
exhaustively by enumerating every state and trigger pair in unit tests, which is a
stronger and more readable artifact than a model of the same table.

The existing `specs/OfflineWriteQueue.tla` continues to govern synchronization,
which M8 does not change.

### 21. All M8 mutations are ONLINE_ONLY

See `docs/13_OFFLINE_CLASSIFICATION.md`. Nothing is cached and nothing is queued.

## Consequences

**Good.**

- The single most valuable question a legal desk asks — "does the paper say what we
  agreed" — is answerable, provably, from the same kernel that answers "what
  changed between these two offers".
- Every date on a screen is either real or explicitly unknown, with the reason
  stated.
- The four dates that legal work actually turns on stay four dates.
- Two facts that can disagree do not exist: everything derived is derived.

**Costs, accepted.**

- Reconciliation and the difference counts are recomputed on every read. The batch
  loader keeps this to a fixed number of queries per page, and correctness is worth
  more than the saving a stored count would give.
- Business-day deadlines are stored and unusable until a calendar exists. That is
  the honest state, and it is visible rather than hidden.
- A lawyer must classify privilege deliberately. The alternative is guessing.

**Explicitly out of scope.** Commissions, invoices, receivables, payments, the
ledger, revenue recognition and any money reconciliation — all M9. Document
storage, ingestion, Outlook and Gmail sending, and e-signature — all M10.

## References

- `prompts/007_M8_M9_CONTRACTS_FINANCE.md`
- `src/AgencyOS.Deals.Rules/Contracts.fs`, `Rights.fs`, `Deadlines.fs`,
  `Reconciliation.fs`
- `src/AgencyOS.Domain/Legal/`
- `src/AgencyOS.Application/Legal/ContractRedaction.cs`
- `src/AgencyOS.Infrastructure/Persistence/Migrations/20260907201254_ContractsRightsAndObligations.cs`
- `tests/AgencyOS.Tests.Integration/ContractTests.cs`
