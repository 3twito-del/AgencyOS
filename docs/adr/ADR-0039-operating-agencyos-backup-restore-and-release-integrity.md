# ADR-0039 — Operating AgencyOS: backup, restore and release integrity

- **Status:** Accepted
- **Date:** 2026-09-09
- **Milestone:** M15 — Production hardening, operational readiness and release
  integrity
- **Supersedes:** nothing
- **Builds on:** ADR-0024 (blob storage and content addressing), ADR-0027 (OAuth
  token protection), ADR-0036 (pinning a model checker), ADR-0037 (the monolith
  holds), ADR-0038 (existence disclosure)

## Context

M14 established that AgencyOS is architecturally sound and measurable. M15 asked a
different question: can it be *operated* — recovered, upgraded, audited and
trusted under real failure?

The review found the engineering in far better shape than the operations. Every
domain guarantee from M0 to M14 was intact and tested. What did not exist was the
ability to lose something and get it back: **no backup script, no restore script,
no drill, no reconciliation between the database and the document bytes, and no
runbook an operator who did not build AgencyOS could follow.**

"The agency can recover its data" was a belief. It is now a finding.

## Decisions

### 1. Backup covers both halves, and restore proves it

PostgreSQL is canonical business truth, but it is not all of the truth. M10 put
document content in a content-addressed store outside the relational rows, so a
database-only restore returns every document's metadata, hash and version history
and **none of its content** — a system that looks healthy while every file is
gone.

`Backup-AgencyOS.ps1` dumps the database and copies the blob tree, and writes a
manifest binding them. `Restore-AgencyOS.ps1` restores both.

**Order is deliberate: database first, blobs second.** Because blobs are immutable
and content-addressed, that order can only produce a blob newer than the dump,
which reconciliation reports as a harmless orphan. The reverse order could produce
metadata referring to bytes nobody copied, which is data loss wearing the
appearance of health.

### 2. Restore verifies before it destroys

The dump is checked against the manifest hash **before** the target database is
dropped, and `-Force` is required to proceed at all.

A restore that dropped a working database and then discovered a truncated dump
would have converted a recoverable situation into an unrecoverable one — the worst
thing a recovery tool can do. The drill proves this with a deliberately corrupted
backup: one flipped byte, refused, database untouched.

### 3. The drill runs the operator's own scripts

The CI drill invokes `Backup-AgencyOS.ps1` and `Restore-AgencyOS.ps1` rather than
reimplementing them. What is tested is what a person runs. A drill that exercised
a parallel implementation would prove the wrong thing and would drift.

Three cases: the full round trip with records, audit continuity and every blob
hash verified; restore refusing without `-Force` and changing nothing; and a
damaged backup refused before anything is destroyed.

### 4. Reconciliation reports and never repairs

`Test-AgencyOSIntegrity.ps1` reports four disagreements — missing bytes, orphaned
bytes, hash mismatch, foreign ownership — and changes nothing.

Two of those have innocent explanations. An orphan is the ordinary result of a
backup taken while an upload was in flight; a missing file may be a restore still
running. A tool that deleted on sight would turn a diagnostic into an incident.

### 5. The release is traceable, and says what it is not

The release artifact carries a manifest binding every file's SHA-256 to the
commit, branch, channel, build id, API contract and expected schema. Filenames are
not evidence; the hashes are, and `Test-ReleaseManifest.ps1` verifies the claim
rather than trusting it — including that **nothing is present the manifest does
not account for**, which is the check that catches a file dropped in beside the
release.

`signing` records `unsigned`, because it is. A manifest that omitted the field
would read as though the question had not come up.

### 6. An operator can migrate a database without a developer machine

AgencyOS does not migrate at startup, deliberately: schema evolution under
expand → migrate → verify → contract needs a hand on it, and a binary that
migrated on boot would apply a schema change during an unattended restart.

But until M15, migrating a production database required the SDK **and the source
tree**, neither of which exists on a server. The release now ships an idempotent
`migrate.sql` an operator applies with `psql` alone.

### 7. The configuration that fails silently is refused at startup

On a ring that permits real data, the host refuses to start without an explicit
blob root and Data Protection key path, and refuses a blob root it cannot write
to.

The key path is the one that matters. Without it, ASP.NET Core falls back to
whatever the host offers, which under a service account with no profile is an
**in-memory key ring**: every restart issues new keys, every stored mailbox
credential stops decrypting, and nothing in the logs names the cause. Refusing to
start is louder and kinder than failing three weeks later.

### 8. What the key ring protects, stated exactly

`ISecretProtector` is used only by Communications, under the purpose string
`AgencyOS.Communications.MailboxCredential.v1`. So the consequence of losing the
key ring is precise:

> **Stored mailbox credentials stop decrypting and each mailbox must be
> re-authorized. No canonical business data is affected.**

That is a much smaller disaster than it sounds — and a much larger one if the key
ring is left out of the backup, which is why the runbook says to copy it.

### 9. Secret scanning is a test, not a service

GitHub's own secret scanning is plan-dependent on a private repository, and a gate
that might not be running is not a gate. The scan runs wherever the tests run.

Entropy heuristics are deliberately absent: they find base64 fixtures and Git
hashes, and a scanner that cries wolf gets disabled, which is the worst outcome
available. False positives are allowed one file at a time, with a second test that
fails when an exception outlives the file it excused.

### 10. Actions are pinned by commit SHA

M14 moved `tla2tools` off a rolling tag after it changed three times in five days.
A major-version action tag is mutable by exactly the same argument, and these run
in the workflow that decides whether a build may be promoted.

Pinned to what `v4` resolved to on 2026-09-09 rather than upgraded to the current
major — a security change should not smuggle in a version bump.

### 11. The release gate reports PARTIAL rather than offering a skip

One command runs every gate a promotion depends on and names each result. There is
deliberately **no switch to skip one**: a gate that can be turned off is a gate
somebody turns off on the night it matters.

What it does instead is refuse to overstate. Any gate that could not run in the
environment makes the whole result `PARTIAL`, exit code 4, with the missing gates
listed. PARTIAL is not a promotion.

## What the drills found

**A restore terminates every database connection.** `DROP DATABASE ... WITH
(FORCE)` kills every session, and pooled connections come back dead without
knowing it. An application running during a restore does not recover on its own —
it must reconnect. The recovery runbook therefore says restart the API rather than
assume it heals. This was learned from a drill rather than an outage, which is the
entire argument for drills.

**A version guard earns its keep immediately.** The backup script refuses a
PostgreSQL client older than the server, naming both versions. On its first CI run
`postgresql-client-18` installed correctly and `pg_dump` still reported 16.15,
because `/usr/bin/pg_dump` is Debian's `pg_wrapper` and resolved to the runner's
pre-existing 16. Without the guard that would have surfaced as an opaque dump
failure against an 18.6 server.

## Why this is better than the alternatives

**Backup as an API endpoint.** A restore is a decision about a deployment, not an
action inside one, and exposing it to a tenant-facing surface would make the most
destructive operation available reachable by a permission bug.

**`pg_dump` alone.** Restores metadata for documents whose content is gone, and
reports success.

**A drill that reimplements the scripts.** Tests something nobody runs.

**Trusting `pg_dump`'s exit code.** A backup is not proven until it is restored.

**Repairing anomalies automatically.** Turns a diagnostic into an incident on the
most likely-innocent findings.

## Consequences

**What this buys.** AgencyOS can be backed up, destroyed and brought back with
every document byte verified against the digest the database recorded — proven in
CI against `postgres:18.6`, not asserted. A release can be traced to a commit. An
operator who did not build it can install, upgrade and recover it.

**What it costs.** Backup scheduling, off-machine storage and backup encryption
are operator responsibilities AgencyOS does not own. The drill runs on a synthetic
dataset, so it proves correctness and says nothing about recovery time at real
volumes.

**What is deliberately not here.** Production code signing (no certificate
exists), staged rollout, automated failover, backup encryption, alerting
integration, SLO numbers, and multi-instance support. Each is recorded as an
external prerequisite or a residual risk rather than quietly assumed.

**What the next milestone inherits.** Operator tooling that is inspectable rather
than automated away, a drill that will keep proving the recovery path, and a
residual-risk register naming what is still owed.
