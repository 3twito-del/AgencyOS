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

## Future temporal modeling

Representation, employment, attachments, rights and contractual relationships should support effective-date history rather than destructive overwrite.
