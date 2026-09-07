# ADR-0011: Exclusive-arc relationship endpoints, composite-key tenant integrity

Status: Accepted
Date: 2026-09-07

## Context

M2 must record professional relationships whose endpoints can be a `Person` or a
`Company`: "Sarah is an agent at CAA" (person to company), "Sarah introduced me
to Tom" (person to person). Interaction participants have the same shape.

Every such record also belongs to a tenant `Organization` (ADR-0010), and
`docs/07_SECURITY_AND_AUDIT.md` requires that data cannot cross tenants.

Three modelling questions had to be answered together: how an endpoint that may
be one of two types is stored, how referential integrity survives that, and how
tenant containment is enforced.

## Decision

### Endpoints: an exclusive arc, not a polymorphic reference

`professional_relationships` carries four nullable foreign keys -
`from_person_id`, `from_company_id`, `to_person_id`, `to_company_id` - with a
check constraint per side:

```sql
CHECK (num_nonnulls(from_person_id, from_company_id) = 1)
CHECK (num_nonnulls(to_person_id,   to_company_id)   = 1)
```

Every endpoint is therefore a real foreign key to a real table.
`interaction_participants` and `tasks` use the same pattern.

The domain does not expose four columns. It exposes a `RelationshipEndpoint`
value object carrying a kind and an identifier, and the persistence layer maps it
onto the arc. Genericity lives in the domain, where it costs nothing; specificity
lives in the schema, where it buys integrity.

### No Party supertype

A `parties` table with `people` and `companies` as subtypes was evaluated
seriously, because `docs/01_PRODUCT_SPEC.md` anticipates more party kinds (Team,
Office) and it would give one uniform endpoint column.

It is not adopted, because it is not needed for the thing it is usually adopted
for. The exclusive arc already has full referential integrity. What the supertype
would add is uniform *addressing*, and the price is a second row written and
maintained on every `Person` and `Company` insert, an extra join on every
endpoint read, and a subtype-consistency problem of its own (nothing stops two
subtype rows pointing at one party without further constraints).

Adding a third party kind later is an additive migration: two columns, a widened
check, no backfill, because existing rows keep their nulls. That is the
expand-migrate-verify-contract shape `CLAUDE.md` section 5 already requires.

### Tenant containment by composite foreign key

`people` and `companies` each carry `UNIQUE (organization_id, id)`, and every
reference to them is a composite foreign key:

```sql
FOREIGN KEY (organization_id, from_person_id)
    REFERENCES people (organization_id, id)
```

A relationship in tenant A therefore *cannot* reference a person in tenant B. The
database refuses it. This is the difference between an invariant and a habit: a
handler that forgets to filter by tenant produces a foreign key violation rather
than a data leak.

Application-level scoping still exists - every query filters by the caller's
tenant, and the API is routed under `/api/v1/organizations/{organizationId}/…`
so authorization is organization-scoped exactly as M1 built it. The composite key
is the layer underneath that, for the same reason the audit trail has three
layers: the property is worth more than one defense.

### Where each invariant lives

Structural facts are enforced by the database: the arc, `ended_at >= started_at`,
task completion consistency, tenant containment, and identical-endpoint
self-reference.

Semantic facts are enforced by the domain: whether a *particular relationship
type* permits self-reference, whether an interaction has at least one
participant, and whether a task transition is legal. These need context the
database does not have.

## Why this is better than the alternatives

A polymorphic `(endpoint_kind, endpoint_id)` pair is the common shortcut. It
gives one column pair and no foreign key at all - the database can no longer tell
whether an endpoint exists. Every read becomes a conditional join, orphans become
possible, and `ON DELETE` semantics stop existing. `docs/10_ENGINEERING_STANDARDS.md`
asks for explicit constraints rather than application-only enforcement, and this
pattern is the opposite of that.

Separate tables per endpoint pairing - `person_person_relationships`,
`person_company_relationships` - also preserve integrity, but duplicate the
relationship lifecycle, its status model and its history across tables, and force
a union in every query that asks "what relationships touch this person". A third
party kind would mean two more tables.

## Consequences

- "All relationships touching person X" is an `OR` across two columns rather than
  one equality. Indexed on both, and the cost is a query-shape inconvenience
  rather than a correctness one.
- The check constraints must be widened when a party kind is added. That is a
  visible, reviewable migration, which is the point.
- Composite foreign keys mean `people` and `companies` carry a redundant unique
  index on `(organization_id, id)`. Cheap, and it doubles as the tenant-scoped
  lookup index.
- A relationship row is heavier by two nullable uuid columns per endpoint. At the
  scale of one agency this is not a consideration.

## Migration / rollback

Introduced by the M2 migration. Reverting would mean dropping the relationship,
interaction and task tables; nothing in M0 or M1 depends on them.

## Evidence / metrics that would cause reconsideration

- A fourth or fifth party kind arriving, at which point the arc's column count
  starts to argue for the supertype after all.
- A need to address endpoints uniformly across kinds in many queries - for
  example a relationship graph projection - which the supertype serves better.
- Measured cost from the two-column `OR` at a scale that matters.
