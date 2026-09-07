# Roadmap

Status legend: **Done** · **In progress** · **Not started**.
Milestones with no status marker are Not started.

## M0 — Repository & Engineering Kernel — **Done** (2026-09-06)

Implemented: eight-project solution, pinned ALPHA SDK, central build properties
and central package management, build metadata pipeline, structured JSON
logging, `/health` and `/version` endpoints, unit and integration test harness,
GitHub Actions CI and nightly workflows, ADR-0001 through ADR-0004.

Decisions recorded: `docs/adr/ADR-0001-repository-structure.md`,
`ADR-0002-sdk-pinning-and-ring-defaults.md`, `ADR-0003-ci-and-nightly-artifact.md`,
`ADR-0004-structured-logging-without-telemetry-export.md` (its deferral of
OpenTelemetry is superseded by ADR-0016 in M3, on the condition it named; its
logging choices stand).

Deliver:
- monorepo/repository structure;
- pinned toolchains and version policy;
- build scripts;
- CI;
- logging;
- test foundations;
- ADR process;
- release-channel model;
- no business UI yet.

Exit criteria:
- clean clone builds; **met**
- tests run; **met**
- version metadata generated; **met**
- Nightly artifact can be produced. **met**

## M1 — Identity, Organization & Audit — **Done** (2026-09-07) · promoted to ALPHA

Implemented: Organization, User, Membership and the role/permission model;
development identity provider behind a replaceable seam, fenced to rings that
forbid real data; two-layer server-side authorization with the authoritative,
organization-scoped check in the command handlers; append-only AuditEvent
enforced by the domain type, a save interceptor and database triggers;
release-policy model with NONE/AVAILABLE/RECOMMENDED/MANDATORY/REVOKED; the
client handshake endpoint and unconditional server-side enforcement of it;
PostgreSQL persistence with migrations; integration tests against real
PostgreSQL.

Decisions recorded: `docs/adr/ADR-0005-release-handshake-contract.md`,
`ADR-0006-audit-append-only-enforcement.md`,
`ADR-0007-identity-and-authorization-model.md`.

Closure pass (2026-09-07): first-run bootstrap gated by an out-of-band token, an
uninitialized-system check and a database singleton; OpenAPI 3.1 document
generated and verified; liveness and readiness separated, with readiness bound to
canonical PostgreSQL; RC added to `config/release-channels.yaml` completing the
seven-ring model; CI PostgreSQL provisioning made explicit with a pinned
`postgres:18.6` service container.

Additional decisions: `docs/adr/ADR-0008-ci-topology-and-postgres-provisioning.md`,
`ADR-0009-first-run-bootstrap.md`.

Promotion pass (2026-09-07): first-run initialization now publishes the initial
release policy in the same transaction as the organization, owner and
initialization record, so a bootstrapped instance is immediately usable without
manual database seeding. The policy is the narrowest that works - the
bootstrapping client's own platform, ring and version, latest equal to minimum -
and compatibility and REVOKED enforcement are unchanged.

ALPHA promotion evidence (authoritative, remote CI):

Repository `3twito-del/AgencyOS` (private). Workflow **CI**, run
[34082661680](https://github.com/3twito-del/AgencyOS/actions/runs/34082661680),
commit `bfa8204`, conclusion **success**.

- `Integration tests (PostgreSQL 18.6)` on ubuntu-latest: service container
  `docker.io/library/postgres:18.6`, reported healthy by `pg_isready`; server
  banner `starting PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)`. 75 passed,
  0 failed, 0 skipped - including the five `MigrationTests`, each of which
  creates a fresh database and migrates from zero.
- `Build and unit tests (Windows)` on windows-latest: full solution including the
  WinUI 3 client, 0 warnings / 0 errors; 103 unit tests passed; OpenAPI 3.1.1
  generated and verified (8 paths, 5 schemas); version metadata generated.

The PostgreSQL 18.6 ALPHA baseline gate is therefore satisfied, and remote CI is
verified for the workflows that actually executed.

Developer-environment limitation (not a promotion blocker): the Windows machine
used for local development cannot run Docker/Testcontainers, because WSL2 needs
the Virtual Machine Platform feature and firmware virtualization
(`HCS_E_HYPERV_NOT_INSTALLED`, `HypervisorPresent=False`). Local runs therefore
use PostgreSQL 19 Beta 3, which remains LAB evidence only. Authoritative
PostgreSQL 18.6 verification happens in CI.

## M2 — People Vertical Slice — **Done** (2026-09-07) · promoted to ALPHA

Implemented: Person, Company, ProfessionalRelationship, Interaction with
participants, and Task; command-oriented API under
`/api/v1/organizations/{organizationId}` so the tenant is structural; unified
timeline projection for people and companies; the first Command Center; a WinUI 3
client with People, Companies, Command Center, interaction capture with inline
follow-up, and a keyboard-first command palette.

Tenant `Organization` and external `Company` are separate concepts (ADR-0010).
Relationship endpoints are an exclusive arc of real foreign keys, and tenant
containment is enforced by composite foreign keys carrying `organization_id`
(ADR-0011). The timeline is a curated projection, not the audit trail (ADR-0012).

API contract version 1 -> 2. The change is additive, so the server declares the
supported range 1-2 and a contract-1 client is still served.

Decisions recorded: `docs/adr/ADR-0010-tenant-organization-versus-company.md`,
`ADR-0011-relationship-endpoints-and-tenant-integrity.md`,
`ADR-0012-timeline-projection.md`.

ALPHA promotion evidence (authoritative, remote CI):

Workflow **CI**, run
[34100134241](https://github.com/3twito-del/AgencyOS/actions/runs/34100134241),
commit `c900555`, conclusion **success**.

- `Integration tests (PostgreSQL 18.6)` on ubuntu-latest: service container
  `docker.io/library/postgres:18.6`, reported healthy by `pg_isready`; server
  banner `starting PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)`. 110 passed,
  0 failed, 0 skipped - including migrations from a clean database through
  M0 + M1 + M2 and the arc and tenant-containment constraints.
- `Build and unit tests (Windows)` on windows-latest: whole solution including
  the WinUI 3 client, 0 warnings / 0 errors; 178 unit tests passed; OpenAPI 3.1.1
  generated and verified (21 paths, 16 schemas).

Local runs continue to use PostgreSQL 19 Beta 3, which remains LAB evidence only.

Deliver:
Person -> Organization -> Relationship -> Interaction -> Task -> Command Center.

Windows:
- native shell;
- command palette;
- entity navigation;
- dense list/detail views;
- unified timeline;
- quick capture.

Exit criteria:
- create/edit/search people; **met**
- connect organizations; **met** (as external companies, per ADR-0010)
- record interaction; **met**
- create next task; **met** (in the same transaction as the interaction)
- see task on Command Center; **met**
- full audit trail. **met**

## M3 — Search, Views & Local Cache — **Done** (2026-09-07) · promoted to ALPHA

Implemented: ranked tenant-scoped search over PostgreSQL full text and trigram
similarity; per-user saved views with a validated, versioned definition document;
an encrypted local SQLite cache with an explicit schema version; a durable
offline write queue over a closed allow-list of reversible commands; and a
commit-ordered change feed the cache synchronizes from.

Search combines an exact, prefix, full-text and trigram arm into one comparable
banded score, and every hit says which arm matched it. User text reaches the
database only as a parameter, and the prefix `tsquery` is built by a database
function that quotes every token, so a query containing `&` or `!` is words
rather than operators.

Concurrency is an explicit `version` integer on each record, required on guarded
mutations rather than optional - an optional concurrency token is last-write-wins
with extra steps. Idempotency is enforced server-side by reserve-execute-store,
so a retry after a lost response replays the original answer instead of acting
twice. Both properties are modelled in `specs/OfflineWriteQueue.tla` and checked
by TLC (1024 distinct states, depth 15, no invariant violated), and the same
invariants are asserted against the implementation over all 780 failure
interleavings of length four.

The change feed takes positions from a per-tenant counter row held under its row
lock until commit, so sequence order equals commit order and a client that has
read to position N cannot later discover a gap below it. A first sync and an
incremental sync are the same query; the migration backfills one entry per
existing record.

The local cache is never canonical. It is encrypted with SQLCipher under a
DPAPI-protected key, isolated per channel, tenant and user, and its operations are
deliberately not audited - the commands its queue submits are audited by the
server when they actually run.

API contract version 2 -> 3. The change is additive, so the supported range stays
open at 1. The concurrency guarantee does not depend on the contract version: the
version token is a required field, so a client that omits it is refused rather
than silently privileged.

Decisions recorded: `docs/adr/ADR-0013-synchronization-architecture.md`,
`ADR-0014-concurrency-and-idempotency.md`,
`ADR-0015-local-cache-encryption.md`,
`ADR-0016-observability-supersedes-0004.md` (which supersedes ADR-0004's deferral
of OpenTelemetry, on the condition ADR-0004 itself named).

ALPHA promotion evidence (authoritative, remote CI):

Workflow **CI**, run
[34113378373](https://github.com/3twito-del/AgencyOS/actions/runs/34113378373),
commit `ca9d1ed`, conclusion **success**.

- `Integration tests (PostgreSQL 18.6)` on ubuntu-latest: service container
  `postgres:18.6`, server banner
  `starting PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)`. 166 passed, 0 failed,
  0 skipped - including migrations from a clean database through M0 + M1 + M2 +
  M3, the search ranking against real full-text and trigram indexes, server-side
  idempotency, version conflicts, and the change feed's ordering guarantee under
  eight concurrent writers.
- `Build and unit tests (Windows)` on windows-latest: whole solution including the
  WinUI 3 client, 0 warnings / 0 errors; 241 unit tests passed - among them the
  780-interleaving offline-queue simulation and the DPAPI key provider, which is
  Windows-only and therefore genuinely exercised here; OpenAPI 3.1.1 generated and
  verified (27 paths, 22 schemas).

Four earlier M3 commits also completed CI green (`2c13942`, `fb0ce09`, `59abd1f`,
`1435256`), so the milestone is green across its whole series rather than only at
its tip.

Local runs continue to use PostgreSQL 19 Beta 3, which remains LAB evidence only.

Known limitations, recorded rather than implied:

- Expired idempotency keys are not pruned. Retention is 30 days and the column is
  indexed for a future reaper; ADR-0014 states this as a limitation rather than an
  oversight.
- A cache rebuilt from position zero replays a tenant's whole change history.
  Feed compaction is the answer if rebuild time is ever measured to be a problem,
  and is deliberately not built before then.
- Relationships and interactions are not cached; they are read online. Caching
  them would mean answering what an offline timeline means when half its inputs
  are stale.
- `ChangeKind.Removed` is defined, carried by the protocol and handled by the
  client, but never emitted: AgencyOS archives rather than deletes. It exists so
  that a future emitter does not break clients shipped before it.

Deliver:
- PostgreSQL full-text/trigram; **met**
- saved views; **met**
- local SQLite cache; **met** (encrypted, versioned, never canonical)
- offline read; **met**
- queued safe writes; **met** (closed allow-list, server-enforced idempotency)
- conflict handling design. **met** (a first-class user-visible state, not an error)

## M4 — Talent & Representation — **Done** (2026-09-07) · promoted to ALPHA

Implemented: a talent profile distinct from the person it describes; prospecting
as a pursuit with its own forward-only lifecycle; representation as an
effective-dated relationship with scopes and a team; credits and materials;
representation history; and a client overview that composes them.

Person stays the human. There are no `Actor` or `Writer` subclasses and no
`IsClient` column: somebody is a client exactly when they hold an active
representation, so the answer is derived and cannot drift. The lead representative
is likewise derived - the open `Lead` row on the team - rather than stored beside
it.

Prospecting and representation are two state machines, not one enum. That is what
makes "a former client we are courting again" an ordinary path rather than a
special case. Both transition tables are published as data and a unit test
enumerates every state against every target: 25 pairs for representation, 36 for
prospects. For machines this size that is the entire state space, so it is a proof
rather than a sample.

At most one live representation per person is enforced by the handler, by the
domain and by a partial unique index. An integration test converts one prospect
from eight simultaneous clients and asserts exactly one representation results, so
the index is shown to hold when the first two layers are bypassed.

Scope, team and discipline are effective-dated rows and status changes are
append-only events, so ending something closes it rather than erasing it. Business
dates are `DateOnly`, because a representation that started on 3 March did not
start at a time of day anybody agreed.

Credits carry a nullable `ProjectId` with no foreign key - an M5 seam, not a fake
project. Materials store an absolute HTTP or HTTPS link and refuse a device-local
path outright, because a path that resolves on one machine is not a document
store; M10 owns storage.

`talent.notes.read` is the one fine-grained permission the milestone adds.
Positioning and strategy notes are returned **absent** rather than refused, and
absent is indistinguishable from empty so that no caller learns a note exists.
Representation-team membership is deliberately *not* an authorization dimension:
making it one would turn assignment into privilege escalation. Relationship-aware
policy is deferred until a stated requirement needs it.

API contract version 3 -> 4, additive. Saved view definitions move 1 -> 2, adding
the Talent and Prospects targets; version 1 documents are still read rather than
refused, because version 2 only adds. The local cache schema moves 1 -> 2 with a
real migration that preserves the write queue - the only thing in that file the
server has never seen. M3 had left `Prepare` throwing for any older version, which
was harmless until there was an older version.

No M4 command is offline-queueable. Each is classified and justified in
`docs/13_OFFLINE_CLASSIFICATION.md`; talent is cached for offline reading and the
change feed carries it, keyed by person so a representation change refreshes it.

No TLA+ model. M3 earned one because its queue was genuinely distributed; these
lifecycles are small and synchronous, and their only real hazard - concurrent
conversion - is answered by a database constraint and demonstrated against
PostgreSQL. Writing a model because the last milestone had one would be ceremony.

Decision recorded:
`docs/adr/ADR-0017-representation-model-and-note-sensitivity.md`.

Three defects found by tests written for this milestone, recorded because none was
visible to the compiler:

- **Saved-view filters were never carried.** The endpoint mapped
  `SavedViewFiltersModel` onto `SavedViewFilters` positionally; the two records
  have the same shape, so appending eight M4 filters compiled cleanly and
  defaulted every one. The domain validated them and the query layer applied
  them, but the API passed none, so a view for "my television clients" returned
  every talent record in the tenant. Both mappings now name every field.
- **Saved views leaked strategy notes.** Saved-view results run the projection
  directly rather than through `RepresentationQueryService`, so redaction placed
  in that service did not apply to them. Redaction now lives in `SensitiveNotes`,
  which both paths call, and a test asserts an observer sees the same thing by
  either route.
- **Two API paths differed only by parameter name.** `/talent/{personId}` and
  `/talent/{talentProfileId}` route correctly and are the same path in OpenAPI
  3.1, which forbids the pair. Profile mutations moved to
  `/talent-profiles/{talentProfileId}`, and the contract test now fails on any
  such collision.

ALPHA promotion evidence (authoritative, remote CI):

Workflow **CI**, run
[34123653516](https://github.com/3twito-del/AgencyOS/actions/runs/34123653516),
commit `86b7d10`, conclusion **success**.

- `Integration tests (PostgreSQL 18.6)` on ubuntu-latest: service container
  `postgres:18.6`, server banner
  `starting PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)`. 214 passed, 0 failed,
  0 skipped - including migrations from a clean database through M0 + M1 + M2 +
  M3 + M4, the one-live-representation index under eight concurrent conversions
  of the same prospect, the partial unique indexes and period check constraints,
  saved views over the new targets, and the note redaction agreeing across both
  read routes.
- `Build and unit tests (Windows)` on windows-latest: whole solution including
  the WinUI 3 client, 0 warnings / 0 errors; 315 unit tests passed - among them
  the exhaustive lifecycle enumeration and the cache v1 -> v2 migration; OpenAPI
  3.1.1 generated and verified (51 paths, 37 schemas).

Local runs continue to use PostgreSQL 19 Beta 3, which remains LAB evidence only.

Known limitations, recorded rather than implied:

- Credits and materials are `ONLINE_ONLY` although they would qualify as
  offline-safe. There is no offline capture surface for them yet, and a queue
  entry nothing enqueues is speculative infrastructure.
- Nothing forces a future read path to call `SensitiveNotes`. The type system
  cannot express it, so it is asserted by test; a new path that returns talent or
  prospect models without it is the regression to watch for.
- Representation history is per person and unpaginated beyond a limit. That is
  adequate at one agency's volume and is not a general audit browser.
- The talent cache stores a denormalized summary only. Overview, credits,
  materials and prospects are read online, because a stale composition of five
  differently-aged things presented as one answer is worse than saying so.

Deliver:
- Client/Prospect; **met** (separate lifecycles; client-ness derived)
- Representation; **met** (effective-dated, scoped, one live per person)
- Credit; **met** (with an M5 project seam, not a placeholder)
- Material; **met** (metadata only; local paths refused)
- team/representation history. **met** (append-only events; team is the lead)

## M5 — Projects & Packaging
Deliver:
- Project/IP;
- Role;
- Attachment;
- Package;
- buyer/seller/company relationships.

## M6 — Opportunities & Submissions
Deliver:
- Opportunity;
- Pitch;
- Meeting;
- Submission;
- outcomes;
- follow-up workflows.

## M7 — Deal Engine
Deliver:
- Offer;
- CounterOffer;
- Negotiation;
- Deal;
- term model;
- state-machine invariants;
- F# pilot where advantageous.

## M8 — Contracts, Rights & Obligations
Deliver:
- Contract;
- clauses/terms;
- options;
- rights;
- expirations;
- reminders/approvals;
- document versioning.

## M9 — Finance
Deliver:
- Invoice;
- Receivable;
- Payment;
- Commission;
- allocations;
- immutable journal / double-entry ledger;
- reconciliation and forecasts.

## M10 — Documents & Communications
Deliver:
- object storage;
- document metadata/versioning;
- Outlook/Office integration;
- attachment ingestion;
- previews;
- relationship-linked correspondence.

## M11 — Intelligence
Deliver:
- Signal;
- Source;
- Thesis;
- Prediction;
- Watchlist;
- relationship intelligence;
- talent radar;
- research workflows.

## M12 — AI Runtime
Deliver:
- ModelGateway;
- tool registry;
- retrieval;
- provenance;
- eval harness;
- approval levels;
- local/cloud model routing.

## M13 — Advanced Native Windows
Deliver:
- multi-window;
- global capture hotkey;
- Explorer integration;
- Jump Lists;
- notifications/actions;
- deep links;
- Windows Search;
- Windows Hello/passkeys;
- local AI/NPU experiments.

## M14 — Scale & Specialized Services
Only when justified:
- service extraction;
- Temporal;
- event broker;
- OpenSearch;
- graph projection;
- Redis;
- Rust/C++ specialized services.

## M15 — Production Hardening
Deliver:
- BETA/RC/STABLE pipeline;
- staged rollout;
- SBOM/provenance/signing;
- recovery/read-only/forensic builds;
- disaster recovery;
- backup/restore drills;
- chaos tests;
- security review.
