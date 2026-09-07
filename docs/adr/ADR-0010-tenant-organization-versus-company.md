# ADR-0010: Tenant Organization and external Company are different things

Status: Accepted
Date: 2026-09-07

## Context

M1 shipped `Organization` as the tenant and security boundary. Memberships are
held within one, permissions are evaluated against one, and audit records are
scoped to one. It is the thing that answers "whose data is this, and who may
touch it".

M2 needs to record the entertainment industry: studios, management companies,
networks, streamers, production companies, law firms. These are subjects of
records, not owners of them.

Both are colloquially "organizations", and the M1 entity even carries an
`OrganizationType` with values like `Studio` and `Network`. Reusing it for
external companies would be the path of least immediate resistance.

## Decision

They stay separate entities.

- **`Organization`** (M1, unchanged) is the AgencyOS tenant and security
  boundary. Memberships, permissions and audit scope attach to it. Every M2
  business record carries an `OrganizationId` naming the tenant that owns it.
- **`Company`** (M2, new) is an external body AgencyOS holds records *about*. It
  is a relationship endpoint and an interaction participant. It owns nothing and
  grants no authority.

A `Company` is never a security boundary. An `Organization` is never a
relationship endpoint.

`Organization` is not renamed. It is promoted, its name is accurate for what it
means, and renaming a shipped concept for symmetry would be churn against a
milestone that is already at ALPHA. Its `OrganizationType` values also stay:
an AgencyOS instance operated by a studio is a coherent future, so `Studio` as a
*tenant* type is not wrong.

One documentation defect is corrected: `Organization`'s summary read "A company
or other body AgencyOS holds records about, including the agency itself", which
describes `Company`, not a tenant. That sentence becomes actively misleading the
moment `Company` exists, so it is rewritten.

## Why this is better than the alternatives

Overloading `Organization` would make the tenant boundary ambiguous at exactly
the place it must be unambiguous. Every authorization check asks "does this user
hold a permission in *this* organization"; if organizations were also studios,
that question would sometimes mean "does this user have authority over Netflix",
which is meaningless. Worse, it would be a silent ambiguity - the code would
compile and the tests would pass while the security model quietly lost its
meaning.

A shared supertype over tenant and company was considered and rejected for the
same reason: the two have no behavior in common worth unifying. One is a
subject, the other is a scope.

Renaming `Organization` to `Tenant` was considered. It would read better, and it
would rewrite a promoted schema, its migrations, its audit rows and its API
contract to buy a nicer word. The distinction is achievable with documentation
and a new type.

## Consequences

- Two similarly named concepts coexist, which is a real readability cost. It is
  mitigated by naming (`OrganizationId` always means tenant, `CompanyId` always
  means external body), by this ADR, and by `src/README.md`.
- Every M2 table carries `organization_id`, and tenant containment is enforced by
  composite foreign keys rather than by convention. See ADR-0011.
- If an agency ever needs to represent *itself* as a company in industry records
  - to model its own place in a relationship graph - that is a `Company` row that
  happens to correspond to the tenant. Nothing links them automatically, and
  nothing should: the tenant's identity is not industry data.

## Migration / rollback

Additive. `Company` is a new table; `Organization` is untouched apart from a
corrected doc comment.

## Evidence / metrics that would cause reconsideration

- A requirement to grant authority to an external company, which would mean it
  had become a tenant and should be modelled as one.
- Multi-tenant hosting where one agency's `Company` records must be shared with
  another, which would need a deliberate sharing model rather than a merge of the
  two concepts.
