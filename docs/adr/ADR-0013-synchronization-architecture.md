# ADR-0013: Commit-ordered change feed, durable client queue, formally checked

Status: Accepted
Date: 2026-09-07
Formal specification: `specs/OfflineWriteQueue.tla`

## Context

M3 introduces the first replicated state in AgencyOS. Until now there was one
copy of every fact, in PostgreSQL. From M3 the Windows client holds a local
projection it can read offline and a queue of writes it will submit later.

That changes the class of defect the system can have. Nothing before M3 could
lose a write, duplicate a record, or overwrite newer data with older data,
because there was only ever one writer looking at one copy. All three become
possible the moment a client can act while disconnected, and all three are
silent: a duplicated interaction looks like a real interaction, and an overwrite
looks like an edit.

## Decision

### Pull: a change feed ordered by commit, not by clock

A `change_log` row is written in the same transaction as every business change,
so a change cannot be committed without its feed entry.

Positions come from a **per-tenant counter row** allocated with
`UPDATE change_sequence SET last_value = last_value + 1 … RETURNING`, which takes
the row lock. Sequence order therefore equals commit order.

Two alternatives were rejected:

`updated_at > lastSync` polling is unsound and the prompt is right to call it
out. Two transactions can stamp `updated_at` at T1 and T2 with T1 < T2 and commit
in the opposite order; a client that syncs between the commits records a cursor
past T1 and never sees it. The window is small, which makes it worse - it fails
rarely and silently.

A bare `BIGSERIAL` has the same hole for the same reason: the value is taken at
insert, not at commit. Sequence 5 and 6 can be allocated in that order and commit
in the other, and a reader that advances to 6 loses 5 permanently.

The cost of the counter row is that writes to one tenant serialize on it. At one
agency's write volume - a handful of writes per second at the absolute peak -
this is unmeasurable, and it buys a feed with no ordering holes at all. If it
ever matters, the migration is to commit timestamps
(`track_commit_timestamp`) or a per-entity-type counter, neither of which changes
the client protocol.

### Push: a durable client queue with server-enforced idempotency

Queued commands live in the client's SQLite database, not in memory, so a crash
loses nothing. Each carries an idempotency key generated **before its first
submission** and kept across every retry, restart and reconnect. Server-side
enforcement is described in ADR-0014.

Only four commands may be queued: record interaction, create task, complete task,
reopen task. All are reversible and all have defined conflict semantics. Nothing
touching authorization, release policy, bootstrap or administration is queueable,
because "what should happen if this was authorized when queued and forbidden when
submitted" has exactly one safe answer - refuse - and that is not a queue, it is a
rejection.

### Queued commands are re-authorized at execution

A queued command is authorized when the server runs it, never when it was queued.
Offline possession of a record confers nothing. If permissions changed while the
user was offline, the canonical answer wins and the command fails with a
permission error the user can see.

### The protocol is model-checked, not merely reasoned about

`specs/OfflineWriteQueue.tla` is a PlusCal model of the queue lifecycle over the
states the implementation uses - `LocalPending`, `Sending`, `Synced`, `Conflict`,
`FailedRetryable`, `FailedPermanent` - with the faults that make this hard:
acknowledgement lost after the server committed, client crash mid-flight,
consequent duplicate submission, stale version, and outright rejection.

TLC checks, exhaustively over two concurrent commands and two attempts:

- `AtMostOneEffect` - one key produces at most one canonical effect;
- `AcknowledgedIsCommitted` - nothing the client believes succeeded was invented;
- `CommittedNeverPermanentlyFailed` - a committed write is never reported as
  permanently failed, which is the trap in naive retry-then-give-up;
- `StaleNeverOverwrites` - a stale command performs no effect;
- `ConflictOnlyWhenStale` - a conflict is never shown for a delivery failure.

Result: **1024 distinct states, depth 15, no violations.**

The model taught the implementation one thing directly. The natural way to write
the client is "on the final attempt, give up and mark it failed", and that
violates `CommittedNeverPermanentlyFailed`: the last attempt may be the one whose
acknowledgement was lost after the server committed. The implementation therefore
returns an exhausted item to `LocalPending` for a bounded replay rather than
declaring failure, because only the server can say whether the effect happened.

Liveness is deliberately not checked. The model permits a network that loses
every acknowledgement for ever, under which nothing drains. Draining requires
eventual delivery, which is an assumption about the world rather than a property
of the protocol, and asserting it would be theatre.

## Why this is better than the alternatives

A general event-stream platform - Kafka, NATS, a broker - would give ordering and
replay out of the box and would violate `CLAUDE.md` section 5 twice over: it is
infrastructure with no current workload, and PostgreSQL already provides
transactional ordering for free when the sequence is allocated correctly.

A CRDT or last-write-wins merge would remove conflicts by making them invisible.
For contact and relationship data, silently discarding one of two edits is worse
than asking: an agent needs to know that their note lost to someone else's.

Implementing the sync engine in Rust was considered, since
`docs/02_ARCHITECTURE.md` names Rust as the eventual owner of a sync engine. It
is not adopted: there is no profiling or correctness evidence that C# is
inadequate, and adopting a second language for a first implementation would add a
native boundary to the least-understood part of the system. The boundary is
designed so that a future Rust engine could replace `IWriteQueue` and
`ISyncEngine` without touching the domain, the API contract or the UI.

## Consequences

- Writes to one tenant serialize briefly on its counter row.
- The client can be behind but never inconsistent: it either has a change or has
  not reached it in the feed.
- A conflict is a first-class user-visible state, not an error. It needs UI,
  which M3 provides.
- The change feed carries entity identity, and the same response carries the
  current state of everything it names. The two are read in one request, so the
  feed and the record cannot disagree, and a page costs one round trip rather
  than two. Hydration always observes a state at or after the page: returning
  content newer than the cursor is harmless, because the change that produced it
  has a higher sequence and the client is told to re-read it next page. The
  reverse - content older than the cursor implies - is what would lose data, and
  cannot happen.
- A first sync and an incremental sync are the same query. The migration
  backfills one feed entry per existing record, so "everything" is just the feed
  read from position zero. A separate snapshot endpoint would need its own
  consistency argument against the feed's, and two arguments are how a sync
  engine gets subtly wrong.
- The cost of that choice is that a cache rebuilt from zero replays a tenant's
  whole change history rather than its current state. At one agency's volume that
  is a bounded, rare operation. Feed compaction - collapsing superseded entries
  per entity - is the answer if rebuild time is ever measured to be a problem,
  and is deliberately not built before then.
- Cursors are per tenant. A client working in two tenants keeps two cursors.

## Migration / rollback

The change feed is additive: `change_log` and `change_sequence` are new tables and
nothing reads them unless a client asks. Dropping M3 means dropping those tables
and the client cache; no canonical data depends on them.

## Evidence / metrics that would cause reconsideration

- Contention on a tenant's counter row appearing in latency profiles.
- A second client type with different offline semantics, which would make the
  queue's command allow-list too narrow.
- Sustained conflict rates high enough that users want automatic merging, which
  would be a product decision before a technical one.
