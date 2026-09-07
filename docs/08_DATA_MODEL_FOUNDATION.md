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

Employment, attachments, rights and contractual relationships follow the same
pattern when their milestones arrive.
