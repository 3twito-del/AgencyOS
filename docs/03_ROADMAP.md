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
`ADR-0004-structured-logging-without-telemetry-export.md`.

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

## M3 — Search, Views & Local Cache
Deliver:
- PostgreSQL full-text/trigram;
- saved views;
- local SQLite cache;
- offline read;
- queued safe writes;
- conflict handling design.

## M4 — Talent & Representation
Deliver:
- Client/Prospect;
- Representation;
- Credit;
- Material;
- team/representation history.

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
