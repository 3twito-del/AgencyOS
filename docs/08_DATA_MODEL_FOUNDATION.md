# Data Model Foundation

## Principles

1. PostgreSQL is canonical.
2. IDs are opaque and immutable.
3. Historical truth is preserved.
4. Time is stored explicitly; use UTC for instants and preserve source timezone where semantically relevant.
5. Money includes amount + currency; never binary float.
6. Facts and notes/judgment are separate.
7. Domain commands validate state transitions.
8. External identifiers are modeled, not overloaded into primary keys.

## Initial M2 entities

### Organization
- Id
- Name
- LegalName?
- Type
- Status
- CreatedAt
- UpdatedAt
- CreatedBy

### Person
- Id
- DisplayName
- FirstName?
- LastName?
- PreferredName?
- Status
- PrimaryOrganizationId?
- Notes?
- CreatedAt
- UpdatedAt
- CreatedBy

### Relationship
- Id
- FromEntityId
- ToEntityId
- RelationshipType
- Strength?
- Status
- StartedAt?
- EndedAt?
- Notes?
- CreatedAt
- UpdatedAt

### Interaction
- Id
- Type
- OccurredAt
- Summary
- DetailedNotes?
- Source
- CreatedBy
- CreatedAt

Use join entities for Interaction participants rather than embedding arbitrary arrays.

### Task
- Id
- Title
- Status
- Priority
- DueAt?
- RelatedEntityId?
- AssignedTo?
- CreatedAt
- UpdatedAt
- CompletedAt?

### AuditEvent
Append-only.

## M4 entities — talent and representation

Implemented in M4. `docs/adr/ADR-0017-representation-model-and-note-sensitivity.md`
records why the shape is this one.

### TalentProfile
The agency's view of a person, separate from the person. One per person per
tenant. There are no discipline subclasses: a person who acts and writes has two
`TalentDiscipline` rows, not two records.
- Id, PersonId
- Summary?, PositioningNotes? (sensitive; requires `talent.notes.read`)
- CareerStage, BaseMarket?, Languages?
- Version, CreatedAt, UpdatedAt

### TalentDiscipline
Effective-dated child. Ending one closes the row rather than deleting it.
- Id, TalentProfileId, Discipline, StartsOn, EndsOn?
- Partial unique index: one open row per discipline.

### Prospect
A pursuit, with its own forward-only lifecycle (Identified → Contacted →
Courting → Declined | Lost | Converted). Distinct from `Representation` so that a
former client being courted again is an ordinary path.
- Id, PersonId, OwnerUserId, Stage
- Source?, StrategyNotes? (sensitive), IdentifiedOn, NextFollowUpOn?
- ConvertedToRepresentationId? — set with the terminal stage, in one transaction
- Version, CreatedAt, UpdatedAt
- Partial unique index: one open pursuit per person per tenant.

### ProspectEvent
Append-only. One row per stage change, with when it happened and why.

### Representation
The relationship. Client-ness is derived from it: somebody is a client exactly
when they hold an `Active` representation, so there is no `IsClient` column
anywhere.
- Id, PersonId, Status (Pending → Active ↔ Suspended → Terminated | Expired)
- StartsOn, EndsOn?
- Version, CreatedAt, UpdatedAt
- Partial unique index: at most one non-terminal representation per person per
  tenant. Also enforced by the handler and by the domain.

### RepresentationEvent
Append-only. One row per status change.

### RepresentationScope
Effective-dated. Which areas the agency represents, and when it did.
- Id, RepresentationId, Area, StartsOn, EndsOn?
- Partial unique index: one open scope per area.

### RepresentationTeamMember
Who works the relationship. References an AgencyOS `UserId` — never an arbitrary
`Person` row, because these are colleagues, not contacts.
- Id, RepresentationId, UserId, Role (Lead | Agent | Coordinator | Assistant)
- StartsOn, EndsOn?
- Partial unique indexes: one open `Lead`, and one open row per user.

The lead representative is **derived** from this table rather than stored on
`Representation`, so "the primary representative is on the team" is true by
construction. Team membership is not an authorization dimension.

### Credit
Work a person is credited with, as reported. Carries a nullable `ProjectId` with
no foreign key: an M5 seam, deliberately not a placeholder project row.
- Id, PersonId, ProjectId?, Title, Role?, Type, Status, Year?
- CompanyId?, Source?, Notes?
- Version, CreatedAt, UpdatedAt

### Material
Metadata about a piece of material, not the material itself. `ExternalUri` must
be an absolute HTTP or HTTPS address; a device-local path is refused rather than
stored. Document storage is M10.
- Id, PersonId, Title, Type, Status, ExternalUri?, Notes?
- Version, CreatedAt, UpdatedAt

## M5 entities — projects and packaging

Implemented in M5. `docs/adr/ADR-0018-project-status-stage-and-vocabularies.md` and
`ADR-0019-attachment-participation-and-packages.md` record why the shape is this
one.

### Project
The canonical work. Status and stage are separate columns because they are separate
facts: status says whether anybody is working it, stage says how far the work got.
- Id, Title, WorkingTitle?, Type, Status, Stage
- Logline?, Synopsis?, Year?, Notes?
- PrimaryCompanyId? — a display convenience, not the company structure
- LeadUserId? — internal owner
- Version, CreatedAt, UpdatedAt, CreatedBy

### ProjectEvent
Append-only. One row per status or stage change, with a reason.

### SourceProperty
What a project derives from, **as described**. Asserts nothing about ownership,
availability, options, territories or windows — those are M8, and the name is
chosen so nobody mistakes this for a rights position.
- Id, Title, Type, AttributedCreator? (free text), CreatorPersonId? (when known)
- SourceReference? (ISBN, URL), Provenance?, Year?, Notes?
- Version, CreatedAt, UpdatedAt

### ProjectSourceProperty
Many-to-many link. One book spawns a film and a series; one project draws on a book
and an article. Unique per (project, source property).

### ProjectRole
A position, which may or may not be occupied. A role is not the person in it, and
the occupant arrives only through an `Attachment`.
- Id, ProjectId, Type, Label? (character name or description)
- Status (Open | Filled | OnHold | Closed) — **derived from attachments, never set
  by hand**
- IsExclusive — opt-in; a convenience constraint should fail open
- Notes?, CreatedAt, UpdatedAt

### Attachment
A person's or company's commitment to a role. Every status is a claim about the
world; there is deliberately no `Targeted`.
- Id, ProjectId, ProjectRoleId
- PersonId? / CompanyId? — exactly one, enforced by CHECK
- Status (InDiscussion | Attached ↔ Conditional | Ended | Withdrawn)
- RoleIsExclusive — copied from the role; the only denormalization in M5, because
  PostgreSQL forbids a subquery in an index predicate
- StartsOn, EndsOn?, Source?, Notes?
- Version, CreatedAt, UpdatedAt, CreatedBy
- Partial unique index: one holder per exclusive role, where status occupies it.

### AttachmentEvent
Append-only. One row per status change.

### ProjectCompanyParticipation
A company's structural involvement — deliberately not an attachment, because a
studio is not a position anybody fills.
- Id, ProjectId, CompanyId, Capacity (Studio | Network | Streamer |
  ProductionCompany | Financier | Distributor | SalesCompany | Other)
- StartsOn, EndsOn?, Notes?
- Partial unique index: one open involvement per (project, company, capacity).

### Package
The agency's own assembly, allowed to contain hopes as well as facts.
- Id, ProjectId, Name, Status (Draft | Assembling ↔ Ready | Active ↔ Paused |
  Closed | Abandoned)
- Thesis?, StrategyNotes? (sensitive; requires `packages.strategy.read`)
- LeadUserId, Version, CreatedAt, UpdatedAt, CreatedBy

### PackageEvent
Append-only. One row per status change.

### PackageElement
One item in the assembly.
- Id, PackageId, Kind (AttachedParty | ProposedPerson | ProposedCompany | OpenRole
  | Material | SourceProperty), TargetId, Note?, Position
- Unique per (package, kind, target).

`TargetId` is a raw identifier interpreted by kind. A foreign key per kind would be
six mostly-null columns and an unreadable check constraint; instead every target is
resolved against the tenant before it is stored.

### ProjectMaterialLink
Join between a project and an M4 material. A column on `Material` would force 1:1
and would express a project fact by mutating the talent's own record.

### Credit (M4, seam closed here)
`Credit.ProjectId` becomes a real composite, tenant-qualified foreign key. Still
nullable, because most historical credits describe work the agency had nothing to
do with. Linking is always an explicit command — never inferred from a title.

## M6 entities — opportunities and market activity

Implemented in M6.
`docs/adr/ADR-0020-opportunity-pipeline-and-market-activity.md` records why the
shape is this one.

### Opportunity
A pursuit: something the agency is trying to make happen. Distinct from what it is
about, which is where M5's records come in.
- Id, Name, Kind (TalentEngagement | ProjectMarket | PackageMarket | Staffing |
  Partnership | Other)
- Status (Draft → Active ↔ Paused → Closed → Active | Cancelled) — whether the
  agency is working it at all
- Outcome? (Placed | NoInterest | Withdrawn | Superseded | NotPursued) — required
  exactly when closed, enforced by CHECK. **No Won, Lost or DealClosed**: those
  are M7 facts and this model has no commercial vocabulary.
- Priority (Low | Normal | High) — set by a person; nothing computes it
- OwnerUserId, OpenedOn, ClosedOn?
- Description?, StrategyNotes? — strategy behind `opportunities.strategy.read`,
  and excluded from the search vector
- Version, CreatedAt, UpdatedAt, CreatedBy

Nothing here counts submissions, tracks whether a reply is overdue, or stores a
probability. Every such figure is projected at read time from the event rows.

### OpportunityEvent
Append-only. One row per status change, with a reason.

### OpportunitySubject
What the pursuit is about. An **exclusive arc of typed, tenant-qualified foreign
keys** rather than a kind plus a raw GUID.
- Id, OpportunityId, Kind (TalentProfile | Project | Package | ProjectRole)
- TalentProfileId? / ProjectId? / PackageId? / ProjectRoleId? — exactly one,
  enforced by CHECK, with a second CHECK asserting it matches the declared kind
- Role (Primary | Context | Supporting), Note?
- Unique per (opportunity, kind, target).

The opportunity's kind determines which subject it requires. `PrimarySubject` is
derived from that rule, never stored, so it cannot disagree with the subject rows.

### OpportunityTarget
One market conversation: a party being approached about this pursuit. Its stage is
a **different axis** from the opportunity's status.
- Id, OpportunityId
- CompanyId? / PersonId? — exactly one, enforced by CHECK
- ContactPersonId? — valid only for a company target
- Stage (Identified → Approved → Contacted → Engaged → Interested → Advanced,
  leaving to Passed | Withdrawn | Exhausted). **Deliberately no `Submitted`**: a
  submission is an event at an instant, not a place a conversation rests.
- OwnerUserId?, NextActionOn?, ClosedOn?, Notes?
- Version, CreatedAt, UpdatedAt
- One open target per party per opportunity, enforced by partial unique indexes.

### OpportunityTargetEvent
Append-only. Stage moves, responses received and outreach recorded, each with when
it happened and who recorded it. This is where "they came back asking for more
material" lives.

### Submission
An assertion that material went to a target. **AgencyOS records this; it did not
send anything and cannot confirm delivery.**
- Id, OpportunityId, OpportunityTargetId
- SentAt — *when the agent says it went*, freely backdated
- SentByUserId, Channel (Email | Portal | Courier | InPerson | Phone | Other)
- Subject?, Notes?
- ResponseExpectedBy? — the only thing that makes silence visible. Nothing is
  stored to represent a non-response; it is derived from this date.
- ExternalReference? — opaque; AgencyOS assigns it no meaning. The M10 seam.
- Version, CreatedAt, UpdatedAt

### SubmissionMaterial
What went, **as it read at the time**: TitleAtSubmission, TypeAtSubmission,
VersionLabelAtSubmission, alongside the live `MaterialId`. A material renamed since
does not rewrite what was submitted, and the current title is returned beside the
snapshot so the change is visible.

### OpportunityPitch
The commercial reading of one meeting or call.
- Id, OpportunityId, OpportunityTargetId
- InteractionId — **required and unique**. The pitch and its M2 interaction are
  created in one command and one transaction, so one meeting cannot enter the
  system twice.
- Kind (Introductory | Formal | FollowUp | Incidental)
- Outcome (NoDecision | FollowUpRequested | MoreMaterialRequested | Interested |
  Passed) — **no offer-shaped outcomes**; an offer is a document with terms and
  belongs to M7
- Subject?, Notes?, Version, CreatedAt, UpdatedAt

Participants, timestamp, type and detailed notes live on the interaction. The pitch
does not restate them.

### PitchMaterial
What was shown, snapshotted exactly as `SubmissionMaterial` is.

### OpportunityTaskLink
Joins an ordinary M2 task to a pursuit, and optionally to a target.
- Id, TaskItemId (unique — a task belongs to at most one pursuit)
- OpportunityId, OpportunityTargetId?, LinkedAt

A join table rather than another nullable id column on `TaskItem`: every milestone
that adds a linkable thing would otherwise add another nullable column and another
combination nothing validates.

## M7 entities — deals, offers and commercial terms

Implemented in M7.
`docs/adr/ADR-0021-deal-rules-kernel-offer-immutability-and-agreed-terms.md`
records why the shape is this one.

### Deal
One commercial negotiation, anchored to the market conversation that produced it.
- Id, Name, Reference?, Kind (TalentEmployment | Writing | Directing | Producing |
  ProjectSale | ProjectLicense | Package | Services | Partnership | Other)
- Status (Draft → Negotiating → TermsAgreed | NoDeal | Cancelled, with reopening
  from the last two). **No Signed, Executed, Paid or Commissioned**: M7 cannot
  substantiate any of them.
- OpportunityId + OpportunityTargetId — **required**, as one composite foreign key
  over (organization, opportunity, target), so a deal cannot anchor to a target
  from another pursuit or another tenant
- OwnerUserId, OpenedOn, ClosedOn?
- Summary?, StrategyNotes? — strategy behind `deals.strategy.read`, excluded from
  the search vector
- Version, CreatedAt, UpdatedAt, CreatedBy

The counterparty is **not** a column: it is whoever the target names, read by join.
Neither is the accepted offer: it is the offer that says it is the agreement,
unique by partial index. Both would be the same fact written twice.

One live deal per (target, kind), by partial unique index. One buyer can genuinely
negotiate a project sale and a producing deal at once; two of the same kind means
two colleagues working the same thing.

### DealEvent
Append-only. One row per status change, naming the transition that caused it -
`OfferRecorded`, `OfferAccepted`, `NegotiationReopened`, `ClosedNoDeal`,
`Cancelled`. The first two are consequences of offer acts and are not requestable.

### Offer
One concrete proposal of commercial terms, at one moment.
- Id, DealId, Direction (Inbound | Outbound)
- Status (Draft → Open → Accepted | Rejected | Withdrawn | Expired | Superseded)
- RespondsToOfferId? — the offer this answers. A foreign key over
  (organization, deal, answered offer), so an offer can only answer one in the same
  negotiation.
- Sequence — the canonical order, never inferred from identifiers
- RecordedAt, CommunicatedAt? (freely backdated), RecordedByUserId
- Summary?, Notes?, ExpiresAt?
- Version, CreatedAt, UpdatedAt

At most one `Open` and at most one `Accepted` per deal, both by partial unique
index. A counter is another offer, never an edit. Nothing expires because time
passed: `ExpiresAt` must have been stated and must have arrived.

### OfferTerm
One negotiated term. Structured so offers can be compared and later reconciled
against a contract.
- Id, OfferId, Code (controlled `DealTermCode`), ValueKind
- An exclusive arc of typed columns — `amount_value numeric(19,4)` +
  `currency_code`, `numeric_value numeric(19,6)`, `integer_value`, `text_value`,
  `boolean_value`, `date_value` — with a CHECK asserting the shape matches the kind
  and that money carries a currency
- Unit? (Day | Week | Month | Year | Episode | Season | Draft | Step)
- Sequence, Label?, Notes?
- Unique per (offer, code), so comparison joins unambiguously.

**No binary floating point anywhere.** Money is an amount and a currency, held to
that currency's own minor units.

Terms are immutable once the offer leaves draft: the aggregate refuses to change
them, and a PostgreSQL trigger refuses independently.

### OfferEvent
Append-only. One row per offer status change, with its transition and cause.

### DealTaskLink
Joins an ordinary M2 task to a negotiation, and optionally to an offer. Unique on
the task. A join table rather than another nullable column on `TaskItem`, on the M6
precedent.

### Deliberately absent
No Contract, signature status, executed date, document, rights grant, territory,
option exercise, obligation or notice period — those are M8. No invoice,
receivable, payment, commission or ledger row — those are M9. And no `DealId` on
any M6 row: the link runs the other way.

## Temporal modeling

Effective-date history rather than destructive overwrite, applied from M4 onward.

`RepresentationScope`, `RepresentationTeamMember` and `TalentDiscipline` are
effective-dated rows with `StartsOn` and a nullable `EndsOn`, guarded by a check
constraint (`EndsOn IS NULL OR EndsOn >= StartsOn`) and by partial unique indexes
on the open rows. Ending something closes its row; nothing is erased, so "we
picked up their literary representation in 2027 and dropped it in 2029" survives.

Status and stage changes are recorded as append-only events alongside the current
value, so the current state is cheap to read and the history is not reconstructed
by inference.

Business dates use `DateOnly`, mapping to PostgreSQL `date`. A representation that
started on 3 March did not start at a time of day anybody agreed, and storing one
would invent a fact.

M5 follows the same pattern: `Attachment` and `ProjectCompanyParticipation` are
effective-dated with period check constraints, and status changes on projects,
attachments and packages are append-only events beside the current value.

M6 follows it again. `OpportunityEvent` and `OpportunityTargetEvent` are
append-only beside the current status and stage, and `Opportunity.OpenedOn` /
`ClosedOn` and `OpportunityTarget.ClosedOn` are `DateOnly` periods guarded by check
constraints. `Submission.SentAt` is deliberately an instant rather than a business
date, because it records a specific reported act — and it is freely backdated, so
the submission timeline says when things happened rather than when they were
typed. That makes it a business record and not an audit trail; the audit log
remains the record of when each row was actually written.

M7 continues both patterns and adds a third. `DealEvent` and `OfferEvent` are
append-only beside the current status, and `Offer.CommunicatedAt` is freely
backdated for the same reason `Submission.SentAt` is. The new pattern is
**historical immutability**, which is not the same thing as append-only audit: a
recorded offer's commercial snapshot cannot be changed at all, enforced by the
aggregate and independently by a PostgreSQL trigger. A correction is another offer
superseding it, so both readings survive.

Rights and contractual relationships follow the same pattern when their milestones
arrive.
