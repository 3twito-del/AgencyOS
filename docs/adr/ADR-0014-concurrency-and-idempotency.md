# ADR-0014: Explicit version tokens and server-enforced idempotency

Status: Accepted
Date: 2026-09-07

## Context

M2 had no concurrency control. Every mutation was last-write-wins, which was
harmless while the only writer was an online client acting on state it had just
read. M3 breaks that assumption: a client can read a task on Monday, lose
connectivity, complete it on Tuesday, and submit on Wednesday against a record
somebody else has since changed.

Separately, an offline queue must retry, and retry over an unreliable channel
means the same command can arrive twice - most dangerously when the first attempt
committed and only the acknowledgement was lost.

## Decision

### Concurrency: an explicit `version` integer

Every mutable M2 record carries a `version` column that the domain increments on
each mutation. Guarded mutations take a required `expectedVersion`; a mismatch
raises `ConcurrencyConflictException` and answers **409** with problem details
carrying both the expected and the actual version.

`expectedVersion` is a **required field**, not an optional one. An optional token
that defaults to "don't check" is last-write-wins with extra steps, and it fails
exactly when a careless client omits it. Being required also means the supported
API contract range can stay wide: a client that does not send it gets a clear
400 rather than a silent overwrite, so older contracts remain safe rather than
being locked out.

**PostgreSQL's `xmin` was rejected.** EF Core supports it and it needs no column,
but a concurrency token is part of the client contract: it is returned to the
Windows client, stored in its cache, persisted in its queue, and sent back days
later. Binding that to a storage-engine internal means the contract cannot
survive a storage change, and it leaks something the client has no business
knowing. `docs/02_ARCHITECTURE.md` is explicit that the client never depends on
the DB schema. An explicit integer is portable, readable in a log, and trivially
comparable.

An ETag header was considered. It is more HTTP-idiomatic and it splits the token
across two mechanisms - a header for online calls, a field for queued ones -
which is worse for the case that matters here.

#### The check has to happen in the database as well (added M10)

The paragraphs above describe a check the domain makes against the row as it was
read. That is exactly right for the case it was written for - a client working
from a stale copy is refused - and it is not enough on its own.

Two requests that both read version N are both current when they check. The check
passes for both, and without a database guard both writes land: the second simply
overwrites the first. M7's `AcceptingWhileCountering` test reproduced it at
roughly one run in twelve, with an offer accepted and countered at the same
moment, and CI caught it during M10.

So the `version` property is declared `IsConcurrencyToken()` on every versioned
entity. EF Core then writes `WHERE ... AND version = @original` and checks the
affected row count, so the loser of a race gets `DbUpdateConcurrencyException`
instead of a silent overwrite. That is mapped to the same **409** with the same
`version_conflict` code as the checked conflict, because a client cannot act on
the difference between "you were stale when you asked" and "you were current and
somebody beat you".

This is not a substitute for `expectedVersion`, and does not replace it. The
application check gives a client a precise answer with both version numbers in it
before any work is done; the database guard catches the interval the application
check cannot see. Both are required.

**It costs something.** A concurrency token forces EF to verify affected rows per
statement, which limits how it batches writes: the integration suite went from
about 3m25s to about 5m55s on the same machine. That is a write-path cost paid by
a suite that does little but write, and it is worth paying for an invariant like
"an offer is not both accepted and countered".

`ConcurrencyTokenTests` asserts over the EF model that every entity carrying a
`version` column declares it a token, and that the token is the explicit integer
rather than `xmin`. It needs no database and catches the next aggregate that
forgets, at build time rather than one run in twelve.

### Idempotency: reserve, then execute, then store

`idempotency_keys` is keyed on `(organization_id, key)`.

1. **Reserve.** `INSERT … ON CONFLICT DO NOTHING` before any work. Losing the
   insert means another submission of this key already exists.
2. **Execute** only if the reservation was won.
3. **Store** the status and body against the key, in the same transaction as the
   business effect, so a key can never be marked complete for work that rolled
   back.

On replay:

- same key, same fingerprint, complete → the stored response is returned. No
  second business effect, and no second audit event, because no handler runs.
- same key, same fingerprint, still in flight → **409**, retry shortly.
- same key, different fingerprint → **409** idempotency conflict. Two different
  commands wearing one key is a client defect; serving either answer would be
  wrong.

The fingerprint is a SHA-256 of the command type and canonical payload. It is
compared, never read back - the payload contains contact data that does not need
a second copy in a second table.

Scope is the tenant, so two tenants cannot collide and a key can never replay one
tenant's answer to another. Retention is 30 days: longer than any realistic
offline period, short enough to bound the table. **Nothing prunes it in M3** -
that is a documented limitation, not an oversight.

Enforcement is server-side. A client-side "have I sent this?" check is worthless
precisely when it is needed, which is after the client crashed.

## Why this is better than the alternatives

Checking `updated_at` instead of a version has the same clock problems that made
it wrong for the change feed, plus lower resolution: two edits in the same
millisecond are indistinguishable.

Making the server merge rather than refuse - taking the newer field values and
keeping the rest - looks helpful and quietly loses data. For a note or a title
there is no safe automatic merge, only a guess about intent.

Idempotency by natural key - "an interaction with this summary at this time
already exists" - avoids a table and is wrong: two genuinely separate calls with
the same summary at the same minute are a real thing, and refusing the second is
data loss. An explicit key says what the client meant.

## What the fingerprint is computed over

The stored fingerprint is a SHA-256 of the request method, the request path and a
canonical serialization of the **bound command arguments** - not of the raw
request body.

This is not a detail. The enforcement point is an endpoint filter, which runs
after model binding has already consumed the body; reading the body there yields
an empty string for every request. A fingerprint that is constant across all
requests makes two genuinely different commands sharing one key
indistinguishable, and the second is then answered with the first's stored
result. That is the precise failure the mechanism exists to prevent, and it fails
silently. It was caught by the test that submits two different payloads under one
key, which is why that test exists.

Fingerprinting the bound arguments is also the more faithful comparison.
Whitespace and property order are not differences in intent, so a retry
serialized by a different client build is still recognized as the same command.

## Consequences

- Every guarded mutation now needs the version the caller observed, including the
  Windows UI's own online edits. That is the intended cost.
- Conflicts are visible and need resolution UI, which M3 provides.
- The idempotency table grows until a reaper exists. At one agency's volume this
  is small, and it is recorded as deferred work.
- A replayed command produces no audit event, which is correct: nothing happened
  the second time. The original event already records what did.

## Migration / rollback

`version` columns default to 1 for existing rows, so no data is invalid.
`idempotency_keys` is a new table nothing else references.

## Evidence / metrics that would cause reconsideration

- Idempotency table growth becoming material, which would bring the reaper
  forward.
- A mutation whose conflict genuinely can be resolved automatically, which would
  justify a per-command merge rule rather than a blanket refusal.
