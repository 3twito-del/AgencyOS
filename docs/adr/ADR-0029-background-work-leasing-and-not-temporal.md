# ADR-0029 — Background work: a leased queue in PostgreSQL, and not Temporal

- **Status:** Accepted
- **Date:** 2026-09-08
- **Milestone:** M10 — Canonical Documents, Communications, Outlook & Office Integration
- **Supersedes:** nothing
- **Builds on:** ADR-0001 (modular monolith), ADR-0014 (concurrency and
  idempotency), ADR-0028 (the outbound send protocol)

## Context

M10 is the first milestone with work that happens without somebody waiting for it:
synchronizing mailboxes, advancing sends, reconciling unknown outcomes, sweeping
orphaned bytes.

`CLAUDE.md` says not to add Temporal, a broker or a microservice before the
capability is required. It also says not to use an in-memory queue as canonical
work state. Both apply here.

## Decisions

### 1. Canonical work state is a row in PostgreSQL

The work queue is the `outbound_dispatches` and `communication_accounts` tables
themselves. There is no separate queue, no in-memory list of pending items and no
scheduler holding state.

A restart loses nothing, because there was nothing in memory to lose. This matters
more here than in an ordinary job runner: an item in this queue may be a message
that has already reached somebody's inbox (ADR-0028), and an in-memory queue that
vanished on deploy would take the only record of it with it.

### 2. A claim is an atomic lease, taken in one statement

Claiming is a single `UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED)
RETURNING id`. It selects the next claimable row and writes the lease in the same
statement.

Written as two statements — a `SELECT ... FOR UPDATE SKIP LOCKED` followed by a
separate write — the row lock lasts only as long as the select's own implicit
transaction. Two workers then read the same row, both believe they hold it, and
the second discovers otherwise as an optimistic-concurrency failure when it saves.
That is *safe* — only one lease is written — but it is wrong: a worker that loses
a race should take the next row, not fail. This was found by a test that ran two
claims concurrently, and the test remains.

Leases expire. A worker that dies holding one blocks nothing beyond the lease
duration, and the row is picked up in whatever state it was left — which is
exactly what the send protocol is built to survive.

### 3. One step per claim, committed before returning

A worker claims, takes exactly one protocol step, releases, and saves.

A loop that ran the whole protocol in one pass would hold a lease across two
external calls and would have to decide what to do when the second failed with the
first already committed. Stepping means every intermediate state is durable, and
recovery never has to reconstruct what a partially-completed pass intended.

### 4. Two instances running at once is ordinary

Nothing about the worker assumes it is alone. The lease is what makes concurrency
safe, and the design point is deliberate: a rolling deployment runs two instances
for a minute, and that must not send anything twice.

### 5. Temporal is not adopted

The .NET SDK is pinned in `config/version-policy.yaml` and is not referenced.

Temporal earns its place when workflows are long-lived, span services, need
versioned deterministic replay, or coordinate compensations across systems. What
M10 has is a single-step state machine over rows in the same database as
everything else, with recovery expressed as "read the row and reconcile".

Adopting Temporal here would add a server, a worker fleet, a second definition of
what a workflow is, and a deployment story — in exchange for a scheduler this
milestone already has in twenty lines of SQL. The rule in `CLAUDE.md` is not to add
it *because there is an asynchronous operation*, and that is the only argument
available today.

The point at which it becomes right: multi-step workflows spanning several
external systems with real compensation logic, or human-in-the-loop steps
measured in days. Neither exists yet. That will be its own ADR.

### 6. No broker, no microservice, no Kubernetes

No Kafka, RabbitMQ, Redis or outbox-fed bus. The worker is a `BackgroundService`
inside the same host as the API, for the same reason the rest of AgencyOS is a
modular monolith: extraction needs a failure mode it solves, and "this code runs
on a timer" is not one.

`AgencyOS:Worker:Enabled` turns it off, which is how a host that only serves
requests, or a test that drives the processor directly, avoids running it.

### 7. Tests drive the same processor the worker drives

The worker is disabled under test. Tests claim and step using the production
repository and processor, which is what allows an assertion to sit between "the
provider was called" and "the row was written" — the interval the whole send
protocol is about.

Substituting a different implementation for tests would test the substitute.

## Consequences

- Deploying twice does not send twice.
- A killed worker loses nothing and blocks nothing past its lease.
- Background work is limited to what one process can do, which is correct at this
  scale and will not stay correct for ever.
- When durable workflow orchestration is genuinely needed, the decision will be
  made against a real requirement rather than in anticipation of one.
