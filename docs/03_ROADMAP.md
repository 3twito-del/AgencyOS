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

## M5 — Projects & Packaging — **Done** (2026-09-07) · promoted to ALPHA

Implemented: the canonical project, with operational status and creative stage
modelled separately; source properties; roles that exist independently of whoever
fills them; attachments; company participation; packages; and the closing of M4's
credit seam.

A project's **status** says whether anybody is working it; its **stage** says where
the work has got to. Merging them would make "cancelled" and "in pre-production"
mutually exclusive, when the useful fact is usually that a project was cancelled
*during* pre-production. Stage moves in both directions, because projects genuinely
fall out of pre-production back into development and a forward-only ladder would
only teach people to lie to the system. The rule that does bite is that stage
freezes once the status is terminal, so the record still says how far the work got.

**A role is not the person in it,** and that separation is structural. It is what
lets a project say "we need a director" - a sentence with no person in it - and
what lets one role record survive one director leaving and another arriving. Role
occupancy is *derived* from the attachments that actually hold it, never set by
hand.

**Attachment records claims about the world and has no `Targeted` state.** Wanting
somebody for a role is the agency's intention, not a fact about the project;
recording it as an attachment would make a roster a mixture of fact and hope that
nothing downstream could separate. Wanting somebody lives as a proposed package
element and becomes an opportunity in M6. `InDiscussion` is included because it is
reported fact - and deliberately does not occupy a role, since several directors
can be in talks for one job at once.

One holder of an exclusive role is enforced by a partial unique index, and an
integration test races eight clients at a single showrunner job and asserts exactly
one wins. That required copying the role's exclusivity onto the attachment row -
the only denormalization in M5 - because PostgreSQL forbids a subquery in an index
predicate.

**Company participation is a distinct model, not an attachment.** A studio is not a
position anybody fills, and one company can be the producer *and* the studio on the
same project; a single table would collapse those into a nullable-role shape.

**A package holds facts and hopes side by side, and keeps them apart.** Elements
carry an explicit kind, `IsAttached` is computed from whether the referenced
attachment currently holds its role, and the Windows surface renders attached and
proposed in separate columns rather than one list with a badge. Package lifecycle
is about internal readiness only - there is no submitted or pitched state, because
the moment a package goes to a buyer that is M6.

`packages.strategy.read` is the one fine-grained permission added, separate from
`talent.notes.read` because the populations differ. Strategy is excluded from the
package search vector too: otherwise a caller could confirm what a note says by
searching a phrase and watching the package surface.

**Source properties describe and assert nothing legal.** The name is chosen so
nobody mistakes the record for a rights position; chain of title and options are
M8. `Credit.ProjectId` becomes a real tenant-qualified foreign key, still nullable,
and linking is always explicit - a credit is never matched to a project because the
titles look alike.

API contract 4 -> 5, additive. Saved view definitions 2 -> 3, adding the Projects
and Packages targets; versions 1 and 2 are still read. Each target now records
which definition version introduced it, so a document claiming version 2 cannot
name a target that arrived in version 3.

**No project caching, and that is a policy answer rather than an omission.**
Talent was cacheable because "the client list" is a bounded set; "all projects" is
not, and the obvious candidate rules are three different answers to a question
nobody has asked. The cache schema therefore stays at version 2 and M5 adds no
migration. `docs/13_OFFLINE_CLASSIFICATION.md` records the reasoning and classifies
every M5 command.

No TLA+. Nothing here is a distributed protocol; the concurrency hazards are
exclusive-role filling and duplicate package elements, both answered by database
constraints and demonstrated against PostgreSQL.

Decisions recorded:
`docs/adr/ADR-0018-project-status-stage-and-vocabularies.md`,
`ADR-0019-attachment-participation-and-packages.md`.

Three defects found by tests written for this milestone:

- **The saved-view filter mapping dropped seven fields** - the same defect M4 had,
  in the same place. M4's fix was named arguments, which stop a value landing in
  the wrong field and do nothing about one left out, since every filter is
  optional. Two identical record shapes mapped by hand cannot be made safe by
  care, so `SavedViewFilterMappingTests` now walks the properties by reflection.
  ADR-0017 has been corrected, because its claim about that fix was wrong.
- **Attaching somebody in discussion marked the role filled.** The create path
  asserted occupancy instead of deriving it, which would have made every "missing a
  director" view wrong. Occupancy is now computed in one place.
- **EF took three computed collection properties for navigations**, silently adding
  stray `ProjectId1` foreign keys to the first generated migration. Caught before
  the migration was committed.

ALPHA promotion evidence (authoritative, remote CI):

Workflow **CI**, run
[34133029249](https://github.com/3twito-del/AgencyOS/actions/runs/34133029249),
commit `a59d4f0`, conclusion **success**.

- `Integration tests (PostgreSQL 18.6)` on ubuntu-latest: service container
  `postgres:18.6`, server banner
  `starting PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)`. 276 passed, 0 failed,
  0 skipped - including migrations from a clean database through M0 + M1 + M2 +
  M3 + M4 + M5, the exclusive-role index under eight concurrent attachments to
  one showrunner job, cross-tenant refusals on package elements and credit links,
  saved views over the new targets, and the filter round-trip that the mapping
  defect escaped twice.
- `Build and unit tests (Windows)` on windows-latest: whole solution including the
  WinUI 3 client, **0 warnings / 0 errors**; 376 unit tests passed - among them the
  exhaustive project, attachment and package transition enumerations and the full
  5 x 7 x 7 stage matrix; OpenAPI 3.1.1 generated and verified (76 paths, 56
  schemas).

Local runs continue to use PostgreSQL 19 Beta 3, which remains LAB evidence only.

Known limitations, recorded rather than implied:

- No offline projections at all for the slate, by the policy above. Project and
  package reads are refused offline and say so.
- The additive M5 commands (create project, source property, package) would
  qualify as offline-safe but have no offline capture surface, so admitting them
  would widen the queue for a workflow that does not exist.
- A package element's target is a raw identifier validated by one component rather
  than by a foreign key per kind. The trade is deliberate; that validator is the
  thing to watch.
- `role_is_exclusive` is denormalized onto the attachment row and could in
  principle drift. It is written only by the aggregate that owns both sides.
- Project history is per project and limit-bounded. It is a curated timeline, not
  a general audit browser.
- Relationship views are derived from typed relational queries. No graph database,
  and PostgreSQL stays canonical.

Deliver:
- Project/IP; **met** (status and stage separate; source properties describe only)
- Role; **met** (independent of its occupant; occupancy derived)
- Attachment; **met** (claims about the world; exclusivity enforced in the database)
- Package; **met** (facts and hopes held apart)
- company participation. **met** (a distinct model, with the reasoning recorded)

## M6 — Opportunities, Targets, Submissions & Pitches — **Done** (2026-09-07) · promoted to ALPHA

Implemented: the pursuit as a first-class record, distinct from what it is about;
targets as individual market conversations; submissions and pitches as recorded
events; responses; follow-up tasks linked rather than embedded; a pipeline board
and a market command centre.

An **opportunity's status** says whether the agency is working the pursuit at all.
A **target's stage** says where one conversation stands. Collapsing them is the
defining CRM mistake: twelve buyers on one project are twelve simultaneous,
unrelated positions, and a single field forces the pursuit to pretend it is at
whichever stage the loudest target is at. Eleven passes and one live read is not
"passed" and is not "interested" - it is one conversation inside a pursuit that is
still open.

There is deliberately **no `Submitted` stage**. A submission is an event at an
instant; a stage is where a conversation stands. `Submitted` as a stage would
strand a target there forever after one email and leave a second submission six
weeks later nowhere to go. Recording a submission moves a target Contacted →
Engaged and leaves a row carrying the date, the channel and a material snapshot.

Outcomes stop short of the deal boundary. `Placed`, `NoInterest`, `Withdrawn`,
`Superseded`, `NotPursued` - no `Won`, no `Lost`, no `DealClosed`, and no
offer-shaped pitch outcome. Whether money changed hands and on what terms is M7,
and a `Won` here would be a commercial claim made by a model with no commercial
vocabulary.

**AgencyOS records that a submission happened; it does not send anything.** There
is no outbound transport, no credential store and no delivery status anywhere in
M6. `SentAt` is when the agent says it went and is freely backdated, because most
submissions are recorded after the fact. `ExternalReference` is an opaque string
AgencyOS assigns no meaning to - the seam an M10 mail integration would fill. The
Windows submissions surface states this permanently and non-dismissibly, because a
system that implied it had verified delivery would be worse than one that said
nothing.

**Silence is derived, never stored.** Nothing records a non-response. A submission
carries `ResponseExpectedBy`; a target is awaiting a reply when that date has
passed with no response event since. The same rule removes every convenience
boolean: no `HasSubmission`, no `HasBeenPitched`, no stored counts. Two facts that
can disagree are one fact too many.

A **pitch is one interaction, read commercially** - a required, unique
`InteractionId`, both created in one command and one transaction. Two steps is how
one meeting ends up in the system twice with the same participants and the same
timestamp, and the unique index makes the duplicate impossible rather than
unlikely.

Subjects use **typed, tenant-qualified foreign keys** - an exclusive arc over
talent profile, project, package and project role, with check constraints asserting
exactly one is set and that it matches the declared kind. ADR-0019 accepted a
validator-only raw identifier for package elements and named it as the thing to
watch; M6 does not repeat it.

Follow-ups are ordinary M2 tasks joined through `OpportunityTaskLink` rather than
another nullable column on `TaskItem`. One open target per party per opportunity is
enforced by a partial unique index, because two colleagues working the same buyer
without knowing about each other is the situation this part of the system exists to
prevent.

`opportunities.strategy.read` is its own permission, redacted through the shared
`SensitiveNotes` from M4 so detail, lists, saved views and search apply one rule -
and strategy is excluded from the search vector, since confirming a note by
searching a phrase would defeat the redaction entirely.

Saved-view filters moved from reject-lists to a declared **accept-list** per
target, with set and known filter names derived by reflection. A new filter field
is therefore covered by the completeness test automatically. Definition version
moves to 4.

Everything in M6 is **online-only**. No read is cached, no write is queued, and the
local cache schema stays at version 2. A stale pipeline is worse than no pipeline:
an agent who submits again because their cached copy missed yesterday's submission
has done damage in the world that no later sync repairs.

Decisions recorded:
`docs/adr/ADR-0020-opportunity-pipeline-and-market-activity.md`.

Defects found by tests written for this milestone:

- **`OpportunityTaskLink.TaskItemId` was a raw `Guid`** while `TaskItem.Id` is a
  typed `TaskItemId`. It compiled, and it would have let a task from any aggregate
  be linked to a pursuit. Now typed, with the empty guard on the typed value.
- **A LINQ join on `SubmissionId` against `Nullable<SubmissionId>` was
  untranslatable**, and would have thrown at runtime on the response-derivation
  path rather than at build. The key list is now nullable and the grouping keys on
  it.
- **The first generated migration placed its raw SQL after `Up`'s closing brace.**
  Caught before it was committed; the migration was regenerated from a restored
  snapshot.

ALPHA promotion evidence (authoritative, remote CI):

Workflow **CI**, run
[34145263389](https://github.com/3twito-del/AgencyOS/actions/runs/34145263389),
commit `6bc1338`, conclusion **success**.

- `Integration tests (PostgreSQL 18.6)` on ubuntu-latest: service container
  `postgres:18.6`, server banner
  `starting PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)`. 310 passed, 0 failed,
  0 skipped - including migrations from a clean database through M0 + M1 + M2 +
  M3 + M4 + M5 + M6, the one-open-target-per-party index under eight concurrent
  clients racing the same buyer, cross-tenant refusals on opportunity subjects
  and targets, strategy redaction across detail, saved views and search, and the
  filter round-trip against the new accept-list.
- `Build and unit tests (Windows)` on windows-latest: whole solution including the
  WinUI 3 client, **0 warnings / 0 errors**; 459 unit tests passed - among them
  the exhaustive 25-pair opportunity status enumeration and the full 81-pair
  target stage matrix; OpenAPI 3.1.1 generated and verified (93 paths, 71
  schemas).

Local runs continue to use PostgreSQL 19 Beta 3, which remains LAB evidence only.

Known limitations, recorded rather than implied:

- AgencyOS cannot tell the user whether a submission arrived, and says so on the
  screen. That is a real limitation, deliberately visible.
- Backdated `SentAt` means the submission timeline reflects when things happened
  rather than when they were typed, so it is not an audit trail. The audit log is.
- Every pipeline figure is computed at read time from event rows. That cannot be
  stale, and it is more work per read than a column would be.
- No offline projections for the pipeline at all, by the policy in
  `docs/13_OFFLINE_CLASSIFICATION.md`. Opportunity reads are refused offline and
  say so.
- `CreateOpportunity` would qualify as offline-safe but has no offline capture
  surface, so admitting it would widen the queue for a workflow that does not
  exist.
- Opportunity history is per pursuit and limit-bounded. It is a curated timeline
  composed from domain events, not a raw audit browser.
- No `DealId` anywhere. M7 attaches an offer to a target, a submission or a pitch
  without M6 having pre-judged which.

Deliver:
- Opportunity; **met** (pursuit status distinct from per-target stage)
- Pitch; **met** (one interaction, read commercially; uniqueness enforced)
- Meeting; **met** (the M2 interaction, created with the pitch in one transaction)
- Submission; **met** (recorded, never sent; material snapshotted at the time)
- outcomes; **met** (restrained; stops before the deal boundary)
- follow-up workflows. **met** (linked M2 tasks, created with the activity)

## M7 — Deal Engine: Offers, Negotiation & Agreed Terms — **Done** (2026-09-07) · promoted to ALPHA

Implemented: the deal as a negotiation container anchored to an M6 target; offers
as immutable commercial snapshots; counters as offers rather than edits; structured
commercial terms with a controlled vocabulary; deterministic offer comparison; a
pure F# rules kernel; agreed terms that say nothing about a contract.

**A counter is another offer, never an edit.** `RespondsToOfferId` links it to what
it answers, and the earlier offer is superseded rather than rewritten. What the
other side put on the table stays exactly what they put on the table, through any
number of rounds and through a reopening. A correction to a mis-recorded number is
also another offer, so both the mistake and the fix survive.

**Immutability is enforced twice, and it is not the audit trail.** The audit log
answers who did what; this stops a recorded commercial snapshot being rewritten at
all. The domain refuses term changes unless the offer is a draft, and PostgreSQL
refuses independently through triggers on `offer_terms` and on the frozen `offers`
columns. Those triggers are deliberately dumb - the rule lives in the domain, the
guardrail lives in the database - which is how the milestone brief's two competing
instructions on that point were reconciled.

There is consequently no factory producing an already-frozen offer: every offer
starts as a draft and is recorded, because terms may only be added while it is
editable and a second entry point would duplicate the term rules or bypass them.

**`TermsAgreed` is a commercial fact and says nothing about a contract.** There is
no Signed, Executed, Paid or Commissioned status, and the Windows surface states
plainly that no contract has been drafted, signed or executed. Two transitions are
not requestable at all: a deal reaches `Negotiating` because an offer was recorded
and `TermsAgreed` because one was accepted, so it cannot claim agreed terms with
nothing behind it. That is the invariant the milestone exists to protect, and it is
enforced in the domain, in the rules kernel and by a partial unique index.

**The accepted offer is derived, not stored.** So is the counterparty, read through
the M6 target, and the subject, read through the opportunity. A column for any of
them would be the same fact written twice.

**Money is an amount and a currency, in `decimal`.** No binary floating point
anywhere - a test walks `Money`'s public surface to prove it. Amounts are held to
each currency's own minor units, so JPY is not rounded to two places and KWD is not
rounded to two either. Currencies validate against a curated ISO 4217 table; an
unknown code is refused rather than stored. Cross-currency amounts are never
converted and never compared for direction, because M7 holds no exchange rates.

**The rules are an F# kernel** (`src/AgencyOS.Deals.Rules`): pure, no package
references but FSharp.Core, no EF, HTTP, logging, clock or filesystem. It owns
transition legality, chain validation, term-value parsing and comparison.
Discriminated unions remove states rather than describing them - a money value
without a currency cannot be constructed - and incomplete-match warnings are errors,
so an added state breaks the build. The boundary is one class over primitives and
flat records, with fifteen tests walking every state, trigger, direction and value
kind in both directions.

**Comparison answers "what changed", never "is this good."** Added, removed,
changed or unchanged per term, with a direction only where the values are
comparable. No better, worse or score, asserted by test.

**Economics is its own grant.** Without `deals.economics.read`, every money and
percentage term is removed from every read; structural terms survive, so an
assistant can schedule around a start date without seeing the fee. The catalog
makes that safe by classifying every money-bearing term as economic, enforced by
test. Comparison is the one deliberate refusal rather than redaction: an empty diff
would say nothing changed when the guarantee doubled, and a false answer is worse
than an absence. Nothing economic is indexed, filterable or sortable, and telemetry
carries counts and identifiers but never an amount.

**Quote is deferred explicitly.** No M7 workflow consumes one, and a
provenance-carrying quote ledger with no reader is the speculative infrastructure
`CLAUDE.md` section 5 forbids.

Everything in M7 is **online-only**. Nothing cached, nothing queued, cache schema
still version 2. A queued offer replays against a negotiation that may have closed;
a queued acceptance agrees terms the agency may no longer be offering.

Decisions recorded:
`docs/adr/ADR-0021-deal-rules-kernel-offer-immutability-and-agreed-terms.md`.

Defects found by tests written for this milestone:

- **The SDK's implicit FSharp.Core reference compiles and then fails at run time.**
  It resolves to the compiler's own copy inside the SDK directory, which is never
  copied to a consuming project's output. Every call into the kernel threw
  `FileNotFoundException`. Fixed with `DisableImplicitFSharpCoreReference` and an
  ordinary pinned package reference.
- **The saved-view target/permission map had no completeness test**, unlike the
  two tables beside it. Adding the Deals target surfaced it as a 500 from the
  create endpoint. The entry is added and the table now has the same structural
  test the others do - it was the last parallel shape depending on somebody
  remembering every entry.
- **`Offer.Record` produced an already-frozen offer** that terms could then not be
  added to, so the first recorded offer was refused by its own immutability rule.
  Removed; every offer now goes through the draft path.
- **Two navigation shortcuts collided** on Ctrl+9. The palette uniqueness tests
  written in M4 and M5 caught it.

ALPHA promotion evidence (authoritative, remote CI):

Workflow **CI**, run
[34154128075](https://github.com/3twito-del/AgencyOS/actions/runs/34154128075),
commit `1dc6b43`, conclusion **success**.

- `Integration tests (PostgreSQL 18.6)` on ubuntu-latest: service container
  `postgres:18.6`, server banner
  `starting PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)`. 356 passed, 0 failed,
  0 skipped - including migrations from a clean database through M0 + M1 + M2 +
  M3 + M4 + M5 + M6 + M7, the one-open-offer index under eight concurrent
  counters, the accept-versus-counter race, eight clients racing to open one
  negotiation, offer immutability through every editing path, economics
  redaction across detail, offers, saved views and search, and the refusal of
  comparison without `deals.economics.read`.
- `Build and unit tests (Windows)` on windows-latest: whole solution including
  the F# rules kernel and the WinUI 3 client, **0 warnings / 0 errors**; 1404
  unit tests passed - among them the exhaustive 25-pair deal status enumeration,
  the 49-pair offer matrix, 610 generated offer-comparison property cases, the
  chain-validity properties and the fifteen C#-to-F# boundary mappings; OpenAPI
  3.1.1 generated and verified (110 paths, 84 schemas).

Local runs continue to use PostgreSQL 19 Beta 3, which remains LAB evidence only.

Known limitations, recorded rather than implied:

- The immutability triggers are the first business-adjacent logic in the database
  outside the audit guards. They are narrow and tested, and they are the thing to
  watch as the schema evolves.
- Term codes are unique per offer, so two bonuses need two codes or a labelled
  `OtherTerm`. If that proves restrictive, the diff's join key is what changes.
- The currency table is curated rather than the whole of ISO 4217. An agency
  transacting outside it is refused until a row is added; refusing is the safe
  direction.
- No exchange rates, so cross-currency offers are never compared numerically.
- One canonical negotiation thread per deal. Genuinely parallel proposals to one
  counterparty are not supported and are refused rather than silently tolerated.
- Nothing about contracts, rights, options, obligations, invoices, payments or
  commissions, and no `DealId` on any M6 row.

Deliver:
- Offer; **met** (immutable once recorded; frozen in domain and database)
- CounterOffer; **met** (an offer answering another, not a separate aggregate)
- Negotiation; **met** (one canonical ordered thread per deal)
- Deal; **met** (anchored to an M6 opportunity and target by composite key)
- term model; **met** (controlled vocabulary, typed values, decimal money)
- state-machine invariants; **met** (25 and 49 pairs enumerated; TermsAgreed
  reachable only by accepting an offer)
- F# pilot where advantageous. **met** (adopted; pure kernel behind one boundary)

## M8 — Contracts, Rights, Options & Obligations — **Done** (2026-09-08) · promoted to ALPHA

Implemented: the contract as a legal instrument anchored to an accepted offer;
drafting versions carrying a reference to a document AgencyOS has never seen;
transcribed contract terms in the same vocabulary M7 negotiates in;
negotiated-against-drafted reconciliation; rights grants that supersede rather than
overwrite; options, obligations and notice requirements with deadline rules that
resolve honestly or not at all; signatures as the only route to execution.

**Four dates, never collapsed.** Signature, execution, effectiveness and
termination are four separate facts. Executed is derived from the last required
signature and dated from the day it was given, not the day it was typed in.
Effective is recorded separately and may precede execution, so there is
deliberately no constraint ordering the two: an agreement effective as of January
and signed in March is ordinary, and a check forbidding it would force somebody to
record a date the contract does not state.

**Execution follows from signatures and from nothing else.** `SignatureRecorded`
and `ExecutionCompleted` are marked not caller-requestable in the F# kernel, the
status route refuses them by name, and there is no arbitrary status PATCH anywhere
in the milestone. A contract cannot claim execution with nobody's signature behind
it.

**AgencyOS records where a document is, and says so.** A drafting version carries
an external reference, a source system and a filename, and **no content hash**,
because the system has never seen the bytes and a hash it did not compute would be
a claim about identity it cannot support. `HoldsDocument` is published as `false`
through the API and shown on the Windows page, so a client states the truth rather
than letting a reader assume. Nothing copies files, persists local paths as
canonical data, or builds a shadow blob store.

**Reconciliation reports what differs and never whether it matters.** It is built
on the same F# comparison the M7 offer thread uses, because it is the same
operation, and it produces five factual outcomes — Matched, Changed,
MissingFromContract, AddedInContract, NotComparable — with a direction that reports
movement, never merit. There is no Favourable, Unfavourable, Material or Risk:
whether a change is acceptable is a legal question about a document AgencyOS has
not read. The comparison is a stateless projection and the difference count is
recomputed on every read, because a stored count would be right until the next
version was recorded and wrong afterwards.

**One vocabulary and one money system.** `ContractTermCode` shares its integer
values with `DealTermCode`, and the commercial half of the catalog is derived from
`DealTermCatalog` rather than retyped. `contract_terms` has the same column shape,
the same value-kind CHECK and the same `numeric` money columns as `offer_terms`. A
second money representation would have made reconciliation a conversion.

**Deadlines resolve honestly or not at all.** A rule is a stated date, an offset
from an anchor event, or the clause's own words. Resolution returns nothing in
three cases the system can name: no structured rule, an anchor that has not
happened, or **business days**, for which AgencyOS holds no calendar. Business-day
rules are stored faithfully and then decline to produce a date, because counting
them as calendar days would put a deadline in a lawyer's calendar that looks
authoritative and is wrong. A rule that did not resolve is absent from every work
queue rather than guessed onto a day, and the client shows which of the three
reasons applies.

**Nothing lapses, exercises or breaches on its own.** An option past its deadline
stays Available until somebody records that it lapsed — `IsPastDeadline` is derived
and shown, `Expired` is an act. An exercise is never inferred from a payment. An
obligation past its date is past due, which is a fact about a date; **breach is a
determination a person makes**, requires a stated reason, and is refused without
one. That distinction is carried through the domain, the API, the client and the
dialogs, and is tested at each level.

**Privilege is assigned, never inferred.** The default is Ordinary, so nothing
becomes privileged by accident. Three redaction rules apply separately — terms need
`contracts.terms.read`, their figures need `deals.economics.read`, and analysis,
strategy and privileged rows need `contracts.privileged.read` — and everything is
removed rather than marked, with no count of what was withheld. **The search vector
indexes title, reference and factual summary only**, so a phrase appearing solely
in privileged content surfaces nothing: a redaction that leaves a search hit behind
is not a redaction.

**An amendment is a separate instrument**, linked through
`contract_relationships`, with its own parties, drafts and execution state. It is
not version five of the paper it changes.

**Temporal is not adopted.** M8 has no durable multi-step process, nothing to retry
or compensate, and performs no scheduled action; the four questions and their
answers are recorded in ADR-0022. **No TLA+ either**: the state machines are finite
and small, and are proved by enumerating every state and trigger pair, which is a
stronger artifact than a model of the same table.

Everything in M8 is **online-only**. Nothing cached, nothing queued, cache schema
unchanged at version 2. `docs/13_OFFLINE_CLASSIFICATION.md` records why, including
what a cached privileged field would mean after the permission behind it is
revoked.

ALPHA promotion evidence (authoritative, remote CI):

Workflow **CI**, run
[34170253067](https://github.com/3twito-del/AgencyOS/actions/runs/34170253067),
commit `6a29198`, conclusion **success**.

- `Integration tests (PostgreSQL 18.6)` on ubuntu-latest: service container
  `postgres:18.6`, server banner
  `starting PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)`. 400 passed, 0 failed,
  0 skipped - including migrations from a clean database through M0 + M1 + M2 +
  M3 + M4 + M5 + M6 + M7 + M8, the whole contract workflow from agreed terms to
  an executed instrument, execution unreachable by any status command, the
  frozen terms of a recorded version refused by both the domain and the trigger,
  a breach refused without a determination, an option past its deadline that
  stays available, a business-day rule stored and absent from the deadline list,
  a superseded grant that still says what it said, and the proof that a phrase
  appearing only in privileged content surfaces nothing in search.
- `Build and unit tests (Windows)` on windows-latest: whole solution including
  the F# rules kernel and the WinUI 3 client, **0 warnings / 0 errors**; 2254
  unit tests passed - among them the exhaustive 64-pair contract status
  enumeration, the 30-pair option matrix and the 25-pair obligation matrix, the
  grant-period and deadline-resolution properties, the reconciliation outcomes,
  and the proof that every commercial contract term code matches its negotiated
  counterpart; OpenAPI 3.1.1 generated and verified (138 paths, 106 schemas).

The migration was also applied, rolled back and re-applied cleanly before the
commit, so the expand path has a proven reverse.

Local runs continue to use PostgreSQL 19 Beta 3, which remains LAB evidence only.

Known limitations, recorded rather than implied:

- Business-day deadlines are stored and unusable until a holiday calendar exists.
  The clause keeps its stated intent and produces no date; that is the honest
  state, and it is visible on the surface rather than hidden.
- AgencyOS holds no document. Every version is a reference to a file somebody else
  stores, and the system cannot confirm the reference still points at the same
  thing. M10 brings the repository.
- No electronic signature and no verification of any kind. A signature row is an
  assertion that a party signed.
- Territory is four coarse values plus the clause's own words. There is no
  geopolitical model and no attempt at one.
- Reconciliation compares terms that were transcribed. A term nobody typed is a
  term the comparison cannot see, and the count is only as complete as the
  transcription.
- Nothing about commissions, invoices, receivables, payments, allocations or the
  ledger, and no money reconciliation of any kind — all M9.

Deliver:
- Contract; **met** (anchored to a deal and its accepted offer by composite key;
  several per deal)
- clauses/terms; **met** (transcribed per version, frozen once recorded, in the M7
  vocabulary)
- options; **met** (never exercised or lapsed automatically)
- rights; **met** (recorded as the contract states them; superseded, never
  overwritten)
- expirations; **met** (deadline rules that resolve honestly or refuse)
- reminders/approvals; **met** (derived legal deadlines and a work queue; tasks
  linked deliberately, never automatically)
- document versioning. **met as reference versioning** (drafting versions with
  external references; AgencyOS holds no document until M10, and says so)

## M9 — Finance, Commissions, Receivables, Payments & Ledger — **Done** (2026-09-08) · promoted to ALPHA

Implemented: the whole chain from an operative contract to a balanced ledger —
monetary obligations a contract says are payable, receivables the agency expects
to collect, optional invoices recording something issued elsewhere, payments that
say money moved, allocations that say what it was for, commission entitlements
worked out under the rule that governed at the time, and double-entry journal
entries behind all of it.

**Agreed commercial terms are not a collectible legal amount.** A monetary
obligation may only be recorded against a contract that is executed or carries an
effective date, and is not abandoned or superseded. M6 interest, an M7 offer and a
deal at *TermsAgreed* are none of them a source of money anybody can be asked for.
There is no flag that overrides the gate.

**Money is an amount and a currency, and never a float.** `decimal` in C#,
`numeric` in PostgreSQL, and an `Amount` in the F# kernel that carries its currency
*inside the value*, so cross-currency arithmetic is unrepresentable rather than
merely discouraged. No monetary value crosses the wire as a bare number, and the
OpenAPI gate fails the build if a schema grows an amount with no currency beside
it. Two currencies are never added: balances and totals are reported per currency,
because AgencyOS holds no exchange rate and a single "total outstanding" over a
mixed book would be a number that does not exist.

**One rounding policy, applied once.** Intermediate arithmetic at six decimal
places, rounded exactly once at storage to the currency's own minor units — two for
most, zero for JPY and KRW, three for KWD and BHD — with banker's rounding stated
once rather than chosen per handler. A handler cannot round differently, because
handlers do not round.

**Unknown is a real amount, and zero is not a synonym for it.** A backend
participation nobody can value is an obligation with no figure. It produces no
receivable and no commission, and the refusal says why: a percentage of an unknown
is not zero, and recording it as zero would put a false number into every total
that touched it. It can be quantified later, by somebody who knows, as a recorded
act with its own reason.

**Entitled, collected and outstanding are three numbers.** An agency entitled to a
hundred thousand against a million-dollar contract that has paid four hundred
thousand has collected forty thousand. The three are kept apart in the domain, the
projection, the API and the UI, and `Collected` is computed from applied
allocations on every read rather than stored.

**Commission comes from a dated rule, and there is no default rate.** Rules are
effective-dated and hang off a representation, because a rate is a term of a
relationship. The kernel selects by the date the obligation fell due, not by what
is current, so recalculating a 2027 commission in 2029 gives the 2027 answer.
Contract-specific beats general; two rules in force at once is refused when the
overlap is created; no governing rule produces no entitlement and an explanation.
The rate is snapshotted at calculation, and recalculating supersedes rather than
overwrites.

**Client funds are not agency revenue.** Money collected against a client
receivable posts to a liability and is owed onward; only the commission earned on
it moves into revenue, and only in proportion to what actually arrived.
Recognising the gross receipt as revenue would overstate agency income by an order
of magnitude on a typical deal.

**Nothing is auto-matched.** Anything unallocated stays unapplied, is reported back
in the response, and appears on the work queue. Assigning a residual to whichever
receivable looks closest would be the system guessing at a payer's intent and then
acting on the guess. Bank references are not assumed unique either: a repeated
remittance text is reported as a possible duplicate and never refuses a record.

**An unexplained gap stays unexplained.** Reconciliation reports NotStarted,
Reconciled, Shortfall or Excess, states the arithmetic and stops. Whether a
shortfall is withholding, a bank charge, a dispute or a mistake is something a
person finds out. There is no tax engine: inferring one would produce a reconciled
receivable and a fabricated tax record, and nobody would look at either again.

**Balances are projections, never columns.** Outstanding, status, overdue,
unapplied and collected commission are all derived from the rows that recorded the
events, so no two facts can disagree. A stored `IsOverdue` would be wrong every
midnight. A receivable with no due date is never overdue: the contract did not say
when.

**Double entry, with sides and positive amounts.** No signed-number folklore, no
single-amount "transactions" table. The balance invariant is enforced three times
on purpose — by the aggregate, by the F# kernel, and again by PostgreSQL through a
**deferred constraint trigger** at commit, because the invariant spans rows that
arrive in one transaction and a row-level check could not see them.

**Nothing posted is edited or deleted.** Triggers freeze posted entries, their
lines, and a payment's amount, currency and received date. A correction is a
reversing entry beside the original and both stay readable. Write-off is a
financial act with a reason and a posting, not data cleanup: the receivable keeps
its original amount and stays on the books. Cancelling a receivable raised in error
reverses the *original recognition entry* instead, because the money was never
owed.

**AgencyOS records an invoice. It does not send one.** No document store, no
transport, no email, no attachment. `HoldsDocument` is published as `false` rather
than omitted, and the palette test fails if a finance command says send, collect,
chase or match. AgencyOS assigns no invoice numbers either: numbering carries
statutory weight that varies by jurisdiction, so the operator supplies the number
they actually used.

**Finance refuses rather than redacts** — the one place M9 departs from the M7 and
M8 pattern. Removing rows from a list of contract terms leaves a shorter list,
which is honest. Removing rows from an arithmetic report leaves a wrong answer
presented as a right one. The single exception is the command centre, which drops
the whole commission section for a reader without the grant rather than refusing
the page: a section, not rows inside an arithmetic.

**Nine new permissions, disjoint from the commercial ones.** Holding
`deals.economics.read` — which lets somebody see what a deal pays — confers no
finance access whatsoever. `finance.ledger.post` is narrower still and absent from
the Member role: every other posting is a consequence of an act already authorized,
and writing an entry nothing else produced is the one act that needs its own grant.

**A second F# kernel**, `AgencyOS.Finance.Rules`, holding the rounding policy,
allocation and residual arithmetic, double-entry validation, commission selection
and reconciliation outcomes. The M8 argument for staying in one assembly does not
carry over: money arithmetic shares no vocabulary with deal comparison, and the
rounding policy needed to be stated once rather than five times in five handlers
(ADR-0023).

**No revenue recognition policy is implemented, and none is claimed.** M9 records
cash movements and commission earned on collected funds. Having double entry is
not compliance with GAAP or IFRS, the accounts are named neutrally, and cash
collected is never silently equated with recognized revenue.

Workflow **CI**, run
[34206656953](https://github.com/3twito-del/AgencyOS/actions/runs/34206656953),
commit `ce3931d`, conclusion **success**.

- `Integration tests (PostgreSQL 18.6)` on ubuntu-latest: service container
  `postgres:18.6`, server banner
  `starting PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)`. 465 passed, 0 failed,
  0 skipped — including migrations from a clean database through M0 + M1 + M2 +
  M3 + M4 + M5 + M6 + M7 + M8 + M9, the whole finance chain from an executed
  contract to a balanced ledger, an obligation refused against a contract that is
  not yet operative, a commission worked out under the rule that governed sixty
  days ago rather than the one in force today, gross receipts landing in client
  funds payable while only the earned commission reaches revenue, a residual left
  unapplied against two receivables that would each have fitted, a shortfall that
  stays unexplained until somebody records the deduction, an unbalanced and a
  single-sided journal entry both refused, a written-off receivable that keeps its
  original amount, a reversed payment whose original still says what it said, and
  a retried payment that took effect exactly once.
- `Build and unit tests (Windows)` on windows-latest: whole solution including
  both F# rules kernels and the WinUI 3 client, **0 warnings / 0 errors**; 3309
  unit tests passed — among them the money arithmetic and rounding properties
  across zero-, two- and three-decimal currencies, the apportionment residual, the
  allocation and receivable-state matrices, the journal validation ordering, the
  governing-rule selection over overlapping and contract-specific rules, the
  reconciliation outcomes, and the proof that no finance palette command claims an
  act AgencyOS does not perform; OpenAPI 3.1.1 generated and verified (173 paths,
  131 schemas).

The migration was also applied, rolled back and re-applied cleanly before the
commit, so the expand path has a proven reverse.

Local runs continue to use PostgreSQL 19 Beta 3, which remains LAB evidence only.

Known limitations, recorded rather than implied:

- No exchange rate, no FX conversion and no cross-currency total. Multi-currency
  reporting is a list of figures rather than one figure, which is less comfortable
  and more honest.
- No tax engine and no tax inference. Every deduction is a fact somebody entered
  from a remittance advice or a statement, and a gap with none against it stays a
  gap.
- No revenue recognition policy. M9 records cash movements and commission earned;
  it makes no GAAP or IFRS claim.
- No participation waterfall, breakeven or Hollywood accounting model. A backend
  participation is an obligation with an unknown amount until somebody values it.
- No invoice document, no PDF and no sending of anything. AgencyOS records that an
  invoice exists and what number it carries. M10 brings documents and
  communications.
- No statement or remittance ingestion. Payments and deductions are entered by a
  person; the `SourceSystem` field is the seam a future importer fills.
- No forecast, prediction, valuation or score of any kind. Every one would be a
  claim about the future, and M9 records what happened.
- Commission rules cover a percentage of gross, a percentage of one named term and
  a fixed sum. There is no universal entertainment-industry formula and M9 invents
  none.

Deliver:
- Invoice; **met** (optional, records something issued elsewhere, holds no
  document, and says nothing about payment)
- Receivable; **met** (beneficiary-aware, with no stored balance or status)
- Payment; **met** (frozen once recorded; corrected by reversal, never by edit)
- Commission; **met** (dated rules, snapshotted rate, entitled and collected kept
  apart)
- allocations; **met** (rows rather than a column; residuals never auto-assigned)
- immutable journal / double-entry ledger; **met** (sides and positive amounts,
  balance enforced three times including a deferred constraint trigger)
- reconciliation and forecasts. **met as reconciliation; forecasts deliberately
  not built** — a forecast is a claim about the future, and the milestone records
  what happened. The condition for revisiting is a stated question somebody
  actually needs answered, and an accountant in the room.

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
