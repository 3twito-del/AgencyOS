# ADR-0006: Enforce audit immutability in three independent layers

Status: Accepted
Date: 2026-09-07

## Context

`CLAUDE.md` principle 3 requires every consequential business fact to be
auditable, and `docs/07_SECURITY_AND_AUDIT.md` requires consequential changes to
create immutable audit records.

"Immutable" needs an enforcement point. The obvious one is application code, but
an audit trail's value comes from being trustworthy when something has gone
wrong - which includes the case where the application is the thing that went
wrong, and the case where nobody used the application at all.

## Decision

Audit immutability is enforced three times, independently:

1. **The domain type.** `AuditEvent` has a private constructor, no public
   setters and no mutating methods. Application code has no vocabulary for
   changing a record, so it cannot ask for one by accident. A unit test asserts
   by reflection that no public mutator exists, so a later convenience setter
   fails the build rather than quietly removing this layer.
2. **A save interceptor.** `AuditAppendOnlyInterceptor` inspects the change
   tracker on every save and throws if any audit entry is Modified or Deleted.
   This catches what the type cannot: a detached entity reattached in the wrong
   state, a bulk operation, a future mapping mistake.
3. **Database triggers.** `audit_events_append_only` refuses UPDATE and DELETE;
   `audit_events_no_truncate` refuses TRUNCATE, which does not fire row-level
   triggers. Both are installed by the same migration that creates the table.

The audit table also carries no foreign keys, so no delete elsewhere can cascade
into it.

Reads are untracked, so a record cannot enter the change tracker and later be
marked modified.

## Why this is better than the alternatives

Relying on the domain type alone protects against mistakes, not against
determination, and not at all against a direct database session. Relying on the
interceptor alone means the guarantee evaporates for anything that does not go
through this application: a maintenance script, a later migration, an operator
with psql.

The database trigger alone would be sufficient for integrity but produces a late,
opaque failure. The upper layers fail early and say why.

Revoking UPDATE and DELETE privileges from the application role was considered.
It is a good addition later, but it protects only against the role it constrains
and is undone by a superuser connection, whereas a trigger binds every caller. It
is complementary rather than an alternative, and belongs with the operational
hardening in M15.

Putting the trigger in a separate migration was considered and rejected: it would
leave a window, however brief, in which the audit table existed without its
guarantee.

## Consequences

- Correcting a mistaken audit record is impossible by design. A correction is a
  new record that supersedes an old one, which is the intended shape.
- Test databases cannot be reset by truncating the audit table. Integration tests
  use a fresh database per run instead.
- A trigger violation surfaces as SQLSTATE `0A000`. Reaching it means a defense
  fired that should never have been reached, so the API maps it to a 500 and logs
  it as a defect rather than as a user error.

## Migration / rollback

The triggers are created and dropped by the initial migration. Removing them
would be a deliberate, reviewable schema change - which is the point.

## Evidence / metrics that would cause reconsideration

- A regulatory or operational requirement to redact audit content, which would
  need a designed redaction path rather than an UPDATE.
- Retention pressure at a volume where partition-level archival is required;
  detaching an old partition is not an UPDATE or DELETE and would need explicit
  design.
