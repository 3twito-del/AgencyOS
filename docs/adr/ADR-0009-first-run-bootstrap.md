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

## Consequences

- A deployment that does not set a bootstrap token cannot be initialized over
  HTTP. That is the intended default.
- The token grants exactly one irreversible act. It should be rotated or removed
  after first run; nothing enforces that yet.
- Bootstrap does not seed release policy rows, so a freshly bootstrapped system
  still refuses ordinary mutations until a policy is published. That is correct
  but not obvious, and is a candidate for the operator tooling in M15.
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
