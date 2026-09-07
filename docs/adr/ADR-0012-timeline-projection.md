# ADR-0012: The timeline is a curated projection, not the audit trail

Status: Accepted
Date: 2026-09-07

## Context

M2 needs a unified timeline on Person and Company: what happened with this
person, in order. `docs/01_PRODUCT_SPEC.md` describes it as linking interactions,
status changes, tasks and audit events.

AgencyOS already has an append-only, complete, ordered record of everything
consequential - the audit trail. Rendering it directly would be one query and no
new code.

## Decision

The timeline is a separate, curated projection composed at query time from:

- interactions the party took part in;
- relationships the party is an endpoint of, as created and ended events;
- tasks concerning the party, as created and completed events;
- selected record-change facts for the party, read from audit rows whose action
  is on a small allow-list.

It is not the audit table rendered to a user, and it deliberately carries none of
audit's security metadata: no actor identity, no client version, no permission,
no correlation identifier.

No timeline table is written. There is no second write path, so nothing can
diverge from the records it describes.

## Why this is better than the alternatives

**Rendering audit directly** conflates two things with different jobs. Audit
answers "who did what, from which build, under which permission, and when" - it
exists for forensics and must be complete, so it records things a user does not
want on a contact's history: a failed permission check, a policy publish, a
membership grant. It also names actors, which turns a colleague's activity into
ambient surveillance in a screen meant for client history. Filtering audit down to
the interesting rows at render time would put product judgment inside the security
record, and every new audit action would silently change what users see.

**A written timeline table** is the other common choice. It reads fast and it
introduces a dual write: every command would have to remember to append its
timeline row, and the day one forgets, the history is quietly wrong with nothing
to detect it. At one agency's scale the composition query is cheap, and the source
records are already indexed by party.

Reading *some* audit rows as a data source is not the same as exposing the table.
The trail is append-only and stable, which makes it a sound source for "this
record was edited on Tuesday", and using it avoids inventing a parallel
record-change log purely to avoid touching audit.

## Consequences

- Adding a timeline event kind is a change to one projection, and the new event
  appears for historical data too, because it is derived rather than stored.
- The projection reads several tables per party. Indexed on the party columns; if
  it ever costs enough to matter, a materialized view is the next step and needs
  no domain change.
- The allow-list of audit actions surfaced as record changes must be maintained
  deliberately. That is the intended cost: what users see is a product decision,
  made explicitly, not a side effect of what the security layer happens to log.
- Timeline entries carry no actor. If "who recorded this" becomes a product
  requirement it is an additive field, and it should be decided as a product
  question rather than inherited from audit by accident.

## Migration / rollback

Nothing is persisted, so there is nothing to migrate. Replacing the projection
with a materialized table later is additive.

## Evidence / metrics that would cause reconsideration

- Timeline queries appearing in latency profiles at real data volumes.
- A requirement for cross-party timelines - "everything that happened this week" -
  which composes less naturally and may justify a projection table.
- A product decision that timelines should attribute actions to colleagues, which
  would narrow the gap between timeline and audit and deserve a fresh look.
