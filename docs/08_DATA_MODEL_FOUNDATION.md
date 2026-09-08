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

## M8 entities - contracts, rights, options and obligations

Implemented in M8.
`docs/adr/ADR-0022-contract-model-reconciliation-and-legal-honesty.md` records why
the shape is this one.

### Contract
One legal instrument, papering a negotiation whose commercial terms are agreed.
- Id, Title, Reference?, Kind (LongForm | ShortForm | SideLetter | Amendment |
  Rider | LoanOut | ServicesAgreement | Other)
- Status (Draft -> UnderReview -> ApprovedForExecution -> PartiallyExecuted ->
  Executed, with Abandoned, Superseded and Terminated as terminal states).
  **PartiallyExecuted and Executed are unreachable by any status command**: they
  follow from recording signatures and nothing else.
- DealId + AcceptedOfferId - **required and fixed at creation**, as one composite
  foreign key over (organization, deal, accepted offer), so a contract cannot paper
  an offer from another negotiation. The offer being *accepted* is a handler check
  rather than a constraint, because M7 can lawfully unwind an acceptance.
- OwnerUserId, Summary?, LegalAnalysis?, StrategyNotes?
- Privilege (Ordinary | Confidential | LegalStrategy | AttorneyClientPrivileged) -
  **assigned by a person, never inferred**
- ExecutedOn?, EffectiveOn?, TerminatedOn? - **three distinct dates**, plus a
  fourth per signature. Effectiveness may precede execution and there is no
  constraint ordering them, because retroactive effectiveness is ordinary.
- Version, CreatedAt, UpdatedAt, CreatedBy

Several contracts may paper one deal: a long form, a side letter and an amendment
are three instruments about one negotiation.

Execution state, outstanding signatures, effectiveness today and the difference
count are **not** columns. Each is derived from the rows that decide it.

### ContractEvent
Append-only. One row per contract change, naming the transition that caused it.
Distinct from the audit trail: the audit log answers who did what under which
permission, and this answers what happened to the instrument (ADR-0012).

### ContractParty
Who is party to the instrument.
- Id, ContractId, Role (Artist | Producer | Studio | Network | Employer | Lender |
  LoanOut | Licensor | Licensee | Guarantor | Agency | Other)
- PersonId? | CompanyId? | ExternalName? - **exactly one**, by check constraint.
  Where AgencyOS knows the party, the identifier is used.
- Provenance?, IsRequiredSignatory, Notes?

**Role is not type.** The studio on one paper is the licensee on another, and both
are the same company record.

Every row that names a party points at it through a foreign key over
(organization, contract, party), so nothing can reach a party on another
instrument.

### ContractSignature
A record that a party signed: ContractPartyId, SignedOn, Method (Wet | Electronic |
Counterpart | Other), ExternalReference?, RecordedAt, RecordedBy. Unique per
(contract, party).

**No certificate, no key, no hash, no verification.** AgencyOS implements no
electronic signature; this stores a person's assertion.

### ContractVersion
One drafting state of a contract.
- Id, ContractId, VersionNumber (unique per contract), Label, Direction (Inbound |
  Outbound | Internal), Status (Draft -> Recorded -> Superseded)
- ReceivedOn?, SentOn?, RecordedAt, RecordedBy
- ExternalReference?, SourceSystem?, DisplayFileName?, MediaType? - **a reference
  to where the document lives, and no content hash**, because AgencyOS has never
  seen the bytes. `HoldsDocument` is a property returning false, surfaced through
  the API so a client states the truth. M10 brings the repository these fields
  point into.
- Notes?, Version, UpdatedAt

### ContractTerm
One term read out of a drafting version.
- Id, ContractVersionId, Code (`ContractTermCode`), ValueKind
- The identical typed-column arc `OfferTerm` uses, with the identical CHECK, the
  identical `numeric` money columns and the identical currency rule. A second money
  representation would have made reconciliation a conversion.
- ClauseReference?, Label?, Notes?, Sequence
- Privilege - assigned, never inferred
- Unique per (version, code), so reconciliation joins unambiguously.

The commercial half of `ContractTermCode` shares its integer values with
`DealTermCode`, and `ContractTermCatalog` derives that half from `DealTermCatalog`
rather than retyping it.

Terms are immutable once the version is recorded: the aggregate refuses, and a
PostgreSQL trigger refuses independently. It is the only trigger M8 adds.

### RightsGrant
What the contract records as granted. **Not** a chain of title, not a
verification that the grantor held it, and not a clearance.
- Id, ContractId, ContractVersionId, ClauseReference?
- GrantorPartyId, GranteePartyId (distinct, by check constraint)
- RightType, Medium, Territory (Worldwide | UnitedStates | NorthAmerica |
  Specified, with the clause's own words for the last) - **no geopolitical
  ontology**
- Exclusivity (Exclusive | SoleExclusive | NonExclusive)
- PeriodKind (Perpetual | Fixed | OpenEnded | **Unstated**), StartsOn?, EndsOn?,
  shape-checked per kind. An unstated period never covers a date and never overlaps
  another: a contract that says nothing is recorded as saying nothing rather than
  as running forever.
- SourcePropertyId?, ProjectId?, Reservations?, Notes?
- Status (Active | Superseded | Ended), SupersededByGrantId?

Amendments **supersede** rather than overwrite, so what the agency believed it held
before the amendment stays answerable.

### DeadlineRule
A value object carried by options, obligations and notice requirements, mapped into
the owner's own row as a prefixed column group.
- Kind (Absolute | Relative | Unstructured), On?, Anchor?, Offset?, Unit?, Before,
  Basis (CalendarDays | **BusinessDays**), Description?

Resolution is a pure function and returns nothing in three cases the system can
name: no structured rule, an anchor event that has not happened, or business days,
for which AgencyOS holds no calendar. **A rule that did not resolve has no date**,
is absent from every work queue, and the surface says which of the three applies.

### ContractOption
An election the contract creates: Kind (Employment | Renewal | Sequel | Extension |
Purchase | Rights | Other), HolderPartyId, Subject, Deadline (a `DeadlineRule`),
ResolvedDeadlineOn? (a cache of the pure function), WindowOpensOn?, ExerciseMethod?,
EconomicsTermId?, Status (Available | Exercised | Declined | Expired | Waived |
Cancelled), ResolvedOn?.

**Nothing lapses on its own.** An option past its deadline stays Available until
somebody records that it lapsed; `IsPastDeadline` is derived and shown, `Expired`
is an act. An exercise is never inferred from a payment.

### Obligation
What a party must do: ObligorPartyId, ObligeePartyId (distinct), Kind, Description,
Due (a `DeadlineRule`), ResolvedDueOn?, Status (Pending | Satisfied | Waived |
Breached | Cancelled), ResolvedOn?, RelatedOptionId?, RelatedRightsGrantId?,
Privilege.

**Past due is a date; breach is a determination.** `IsPastDue` is derived.
`Breached` requires a stated reason and is refused without one.

### NoticeRequirement and NoticeRecord
A requirement is what the contract demands: parties, description, a `DeadlineRule`,
a method and a reference to where the notice address is recorded (never a copy of
the address).

A record is an assertion that a notice passed: direction, parties, OccurredOn,
method, an optional requirement it answers, and an optional external reference.
**AgencyOS does not send notices** and there is no delivery-status column, because
the system has no way to know one.

### ContractRelationship
How one instrument relates to another: AmendmentOf | Supersedes | SideLetterTo |
Restates | RelatedTo. **An amendment is a separate executed agreement**, not
version five of the paper it changes.

### ContractTaskLink
Joins an ordinary M2 task to a contract, and optionally to an obligation or an
option. Unique on the task, on the M6 and M7 precedent. Obligations do **not**
become tasks automatically.

### Deliberately absent
No deadlines table - every date is read from the row that carries it, and one would
need a source identifier no foreign key could constrain. No stored difference
count, execution flag or effectiveness flag. No document bytes, no content hash and
no local file path. No commission, invoice, receivable, payment, allocation or
ledger row - **those arrived in M9, below**. No document repository, ingestion, mail transport or
e-signature - those are M10.

## M9 entities - money, receivables, payments, commissions and the ledger

Implemented in M9.
`docs/adr/ADR-0023-finance-money-commission-and-the-ledger.md` records why the
shape is this one.

**Every monetary column is `numeric` with an explicit currency column beside it.**
There is no `double precision` anywhere in the milestone, no amount without a
currency, and no column that adds two currencies together.

### MonetaryObligation
A sum an operative contract says is payable.
- Id, ContractId, ContractVersionId, SourceObligationId?, SourceTermCode?
- PayerPartyId, PayeePartyId - **both must be parties to the contract**
- Category (Compensation | Instalment | Episodic | SigningPayment | Bonus |
  Deferred | OptionPayment | Expense | Participation | Other)
- AmountKind (Fixed | Formula | Contingent | Unknown) - **Unknown is a real
  answer.** A participation nobody can value is an obligation with no figure, and
  recording it as zero would put a false number into every total that touches it.
- AmountValue? + CurrencyCode?, Quantity?, UnitAmountValue?, Unit?, Condition?
- Due (the M8 `DeadlineRule` column group, reused unchanged), AnchorDate?
- Status (Expected | Raised | Released | Cancelled), Version

The row may only be written against a contract that is **operative**: executed, or
carrying an effective date, and not abandoned or superseded. Agreed commercial
terms are not a collectible legal amount.

`IsQuantified`, the resolved due date and whether anything has been raised are
**not** columns. Each is derived.

### Receivable
A sum the agency expects to collect.
- Id, MonetaryObligationId, ContractId, PayerPartyId
- Beneficiary (Client | Agency) - **the most consequential field in the
  milestone.** It decides whether collected money becomes agency revenue or a
  liability owed onward.
- ClientPersonId?, RepresentationId? - composite foreign keys into the tenant's
  own people and representations
- OriginalAmountValue + CurrencyCode, AllocatedAmountValue, AdjustedAmountValue -
  running totals of what has been applied, guarded by check constraints that keep
  them non-negative and no greater than the original
- DueOn?, Reference?, ClosureReason?, Version

**There is no balance column and no status column.** Outstanding, status and
overdue are computed from the rows, through the F# kernel, on every read. A stored
`IsOverdue` would be wrong every midnight; a stored balance would drift from its
allocations under exactly the concurrency the system will see.

A receivable with no due date is **never** overdue. The contract did not say when.

### Invoice and InvoiceLine
A billing instrument, when one is used. Not every receivable needs one.
- Reference - the operator's own number, **unique per organization**. AgencyOS
  assigns none: invoice numbering carries statutory weight that varies by
  jurisdiction.
- Status (Draft | Issued | Void) - **nothing here says anything about payment.**
  There is no "Paid" status, because an invoice does not know.
- IssuedOn?, DueOn?, DebtorPartyId, ExternalReference?, VoidReason?, Version
- Lines each name a receivable and an amount, never more than is outstanding

`HoldsDocument` is returned as `false` rather than omitted. AgencyOS holds no
invoice document and sends nothing.

### Payment and PaymentAllocation
Money that actually moved, and what it was for.
- Direction (Incoming | Outgoing), Method, AmountValue + CurrencyCode
- ReceivedOn (a `DateOnly`, freely backdated - the day the money moved as
  reported) and RecordedAt (when AgencyOS was told). **Two dates, never merged.**
- PayerPartyId? / PayerName?, PayeePartyId? / PayeeName?
- ExternalReference? - the bank's or payer's own handle, **not assumed unique**.
  Two genuinely different payments can carry the same remittance text, so a match
  is reported as a possible duplicate and never refuses a record.
- Status (Recorded | Reversed), ReversedByPaymentId?, ReversalOfPaymentId?

A `BEFORE UPDATE` trigger freezes amount, currency and received date. They are
what somebody observed on a statement, and observations are not edited: a typo is
corrected by a reversing payment, and both survive.

**Allocations are rows, not a column.** One payment may settle several receivables
and one receivable may be settled by several payments, so there is no
`Payment.ReceivableId`. What is not allocated stays **unapplied**, is reported
back, and is never assigned to whatever looks closest.

### PaymentAdjustment
A deduction that reduces what will ever arrive.
- Kind (Withholding | BankFee | WireFee | AgreedReduction | WriteOff | Other)
- AmountValue + CurrencyCode, Description, OccurredOn, ExternalReference?

**A fact somebody entered, never an inference.** AgencyOS implements no tax engine
and infers no liability: a gap with no adjustment against it stays a gap.

### CommissionRule
The rule that decides what the agency is entitled to.
- RepresentationId, ClientPersonId, ContractId? (null governs broadly)
- Basis (GrossCompensation | SpecificTerm | FixedAmount), RatePercent?,
  FixedAmountValue? + CurrencyCode?, TermCode?
- EffectiveFrom, EffectiveTo? - **effective-dated**, because a rate is a term of a
  relationship and relationships are renegotiated
- Provenance?, Version

Two rules in force at once is refused when the second is created. There is **no
default rate** anywhere in AgencyOS.

### CommissionEntitlement and CommissionAdjustment
What the agency is entitled to, and what it has actually earned.
- MonetaryObligationId, ContractId, ClientPersonId, RepresentationId,
  CommissionRuleId, ClientReceivableId?
- Basis, **RatePercentSnapshot** - the rate as it stood at calculation, never
  re-read, so a later correction to the rule does not restate what was acted on
- BasisAmountValue, EntitledAmountValue + CurrencyCode
- GoverningOn - the date the rule was read at, defaulting to when the obligation
  fell due rather than to today
- Status (Calculated | Superseded | Cancelled) - recalculating **supersedes**
  rather than overwrites

**Collected is not a column.** It is the snapshotted rate applied to what has
actually arrived against the client receivable, computed on every read. Entitled,
collected and outstanding are three numbers and stay three numbers.

### Account, JournalEntry and JournalLine
Double entry, in the only shape that means anything.
- `Account` - eight system accounts seeded per organization: Cash (1000),
  AccountsReceivable (1100), UnappliedCash (1900), Suspense (1990),
  ClientFundsPayable (2000), CommissionRevenue (4000), Deductions (5000) and
  Write-offs (5100), each with a Category (Asset | Liability | Equity | Revenue |
  Expense | Clearing)
- `JournalEntry` - Status (Draft | Posted | Reversed), Source, Memo, Currency, and
  **three dates**: OccurredOn (the economic event), PostingDate (the accounting
  period) and RecordedAt (when AgencyOS was told). Never merged.
- `JournalLine` - AccountId, **Side (Debit | Credit)** and a **positive** amount

There is no signed-number folklore and no single-amount "transactions" table. A
minus sign means one thing on an asset and the opposite on a liability.

The balance invariant spans rows that arrive in one transaction, so it is enforced
by a **deferred constraint trigger** at commit, in addition to the aggregate and
the F# kernel. `BEFORE UPDATE OR DELETE` triggers on both tables refuse changes to
a posted entry: a correction is a reversing entry beside the original, and both
stay readable.

**No account balance is stored.** Balances are computed from posted lines, per
currency, every time they are asked for.

### FinanceEvent and FinanceTaskLink
`FinanceEvent` is the curated financial history: one business act, one entry,
however many rows it wrote. Distinct from the audit trail, which answers a security
question in a security vocabulary (ADR-0012), and never used as a substitute for
the journal.

`FinanceTaskLink` joins an ordinary M2 task to a receivable, invoice or payment, on
the M6, M7 and M8 precedent. Receivables do **not** become tasks automatically.

### Deliberately absent
No balance, status, overdue, unapplied or collected column anywhere - every one is
derived, so no two facts can disagree. No exchange rate table, no FX conversion and
no cross-currency total. No tax table and no tax engine. No revenue-recognition
schedule: M9 records cash movements and commission earned, and claims no GAAP or
IFRS compliance. No participation waterfall, breakeven or Hollywood accounting
model. No invoice document, PDF, mail transport or statement ingestion - those are
M10. No forecast, prediction, valuation or score of any kind.

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

M8 follows all three. `ContractEvent`, `OptionEvent` and `ObligationEvent` are
append-only beside the current status; the four contract dates are `DateOnly`
business dates and every one of them is freely backdated, because a signature is
routinely recorded days after it was given; and historical immutability applies
again to `contract_terms`, whose values are frozen once the version they were read
out of is recorded.

M8 adds a fourth idea: a date that **does not exist yet**. An effective-dated row
says when something started; a `DeadlineRule` says how a date would be worked out,
and admits when it cannot be. The two are different, and the second is what keeps a
legal calendar honest.

M9 follows all four and sharpens the third. `FinanceEvent` is append-only beside
the current figures; `Payment.ReceivedOn` and `MonetaryObligation.Due` are business
dates, freely backdated, because money moves days before anybody records it; and
historical immutability now covers **posted journal entries and recorded payments**,
enforced by triggers as well as by the aggregates. A posted entry is what somebody
relied on when they reported a figure, so it is never edited: the correction is
another entry saying the opposite, and both survive.

M9 also adds a fifth idea, which is really the milestone's whole argument: a
figure that **must not be stored at all**. An effective-dated row records what was
true then. A derived balance records nothing, because it is recomputed from the
rows that decided it every time somebody asks. The distinction matters because a
stored balance and its allocations are two facts that can disagree, and in finance
they eventually do.
