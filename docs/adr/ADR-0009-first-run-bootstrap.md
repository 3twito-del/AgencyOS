# ADR-0009: First-run bootstrap creates authority once, under three constraints

Status: Accepted
Date: 2026-09-07

## Context

M1 made every protected operation require a permission, held through an active
membership in an organization. That is correct, and it is also a closed loop: on
an empty database nobody holds a membership, so nobody can create an
organization, so nobody can ever hold one. The system as shipped could not be
started.

Breaking that loop means having exactly one path that produces authority without
already having it. That path is, by construction, the most dangerous endpoint in
the system, so the question is not whether to add it but how tightly it can be
constrained while still working.

## Decision

`POST /api/v1/system/bootstrap` creates the first organization, the first user
and an owner membership binding them, in one transaction, and records a
`system.bootstrapped` audit event plus the three records describing what it
created.

It is constrained three ways, independently:

1. **An out-of-band token.** `AgencyOS:Bootstrap:Token` is supplied by deployment
   configuration. When it is absent the route is *not mapped at all* - not
   disabled, not returning 403, simply absent. The token must be at least 32
   characters or the host refuses to start. Comparison is over SHA-256 digests
   with `CryptographicOperations.FixedTimeEquals`, so it is constant time in both
   content and length.
2. **An uninitialized system.** The handler refuses when an initialization record
   exists, answering 409 Conflict.
3. **A database singleton.** `system_initialization` has a fixed primary key and
   a `CHECK (id = 1)` constraint. Two callers that both pass the application
   check still cannot both insert.

A failed token check returns 401 without revealing whether the system is already
initialized.

**Initialization also publishes the initial release policy.** Without it the
instance is initialized but unusable: an uninitialized release-policy table means
every client evaluates as ungoverned, so every protected mutation is refused. The
owner bootstrap just created could not create anything. That is a first-run
correctness defect, not an operator-tooling gap, so the policy is part of
initialization rather than a follow-up step.

The policy is derived from the client performing the bootstrap, because that is
the one build known to be intended for this instance:

- platform and ring: exactly the ones the client presented;
- `latestVersion` and `minimumSupportedVersion`: both set to the bootstrapping
  client's own version, so nothing older is admitted and no range exists to widen
  by accident;
- contract range: the server's own supported range, never the caller's claim;
- no revocations, no kill switch, no deadline.

Because the policy is derived from the client identity, bootstrap requires the
ordinary client identity headers and refuses without them. It also refuses a
client whose contract version lies outside the server's supported range, since
initializing would publish a policy that locks out the very client that just
created the system.

**Atomicity.** The user, organization, membership, initialization record, release
policy and all five audit records are added to one unit of work and committed by
a single `SaveChangesAsync`. EF Core wraps a single save in one transaction, so
the outcome is all or nothing; a partially initialized system is not a state this
handler can produce. No explicit transaction is opened, because introducing one
would mean widening `IUnitOfWork` with transaction control for a guarantee the
single save already provides.

Bootstrap is exempt from client-compatibility enforcement. It has to be: an
uninitialized system holds no release policy rows, so every client evaluates as
ungoverned and every mutation is refused - including the one that would make the
system usable.

`GET /api/v1/system/status` is anonymous and always available, returning a single
boolean, so a client can tell whether to offer first-run setup.

## Why this is better than the alternatives

Inferring "uninitialized" from an empty organizations table would work today and
is a coincidence of the current rules rather than a statement of intent. It also
cannot be made atomic: two concurrent callers both see an empty table. An
explicit singleton row makes the fact explicit and the race impossible.

A seeding migration was considered. Migrations are the wrong place for it: the
first owner's identity is deployment data, not schema, and putting a credential
subject into a migration would commit it to the repository.

A CLI that writes directly to the database avoids exposing an endpoint at all,
but it needs database credentials in an operator's hands and it bypasses the
audit path - the operation that creates all authority would be the one operation
with no server-side record.

Leaving bootstrap subject to compatibility enforcement would be strictly more
consistent and would make the system unbootstrappable. The exemption is narrow -
one route, gated by a token, on a system with no data to endanger.

A permissive initial policy - a wildcard version range, or a minimum of `0.0.0` -
would have made first run easier and would have put the system in the least
governed state of its entire life at the exact moment it holds its first data.
Deriving the policy from the bootstrapping client costs nothing and produces the
narrowest possible admission set.

## Consequences

- A deployment that does not set a bootstrap token cannot be initialized over
  HTTP. That is the intended default.
- The token grants exactly one irreversible act, and **the deployment secret
  should be removed once initialization has succeeded.** Nothing enforces that,
  by choice: token rotation infrastructure is not an M1 concern. What does exist
  is a loud signal - a repeat attempt on an initialized system logs a warning
  saying the token is still configured and should be removed. The attempt itself
  changes nothing.
- A bootstrapped instance is immediately usable by the client that bootstrapped
  it, and by no other build. A second platform, ring or version needs a policy
  published through the ordinary authorized path.
- Because the initial policy pins one exact version, the first client update
  requires publishing a policy first. That is the conservative direction to fail
  in: a stale build is refused rather than silently admitted.
- No session or credential is issued. The owner authenticates through the
  configured identity provider like anyone else.

## Migration / rollback

Added by the `SystemInitialization` migration. There is deliberately no way to
un-initialize: discarding the record would orphan the authority chain the audit
trail explains. A fresh system is a fresh database.

## Evidence / metrics that would cause reconsideration

- A need to re-run bootstrap during recovery, which would need a designed and
  audited re-initialization path rather than a delete.
- Multi-tenant operation, where "the system is initialized" stops being a single
  global fact.
- Token rotation becoming an operational requirement, which would move the token
  from configuration into a secret store.
