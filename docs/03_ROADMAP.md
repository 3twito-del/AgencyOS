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

## M1 — Identity, Organization & Audit — **Done** (2026-09-07); ALPHA promotion pending PostgreSQL 18.6 verification

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

Outstanding for ALPHA promotion: the suite has been verified against PostgreSQL
19 Beta 3 (LAB evidence). Verification against the 18.6 ALPHA baseline requires a
running Docker daemon and has not yet been performed.

Deliver:
- Organization, User, Membership, Role/Permission foundations;
- authentication placeholder suitable for later Entra/OIDC;
- immutable audit model;
- server-side policy authorization;
- client/server version handshake.

Exit criteria:
- privileged mutations are authorized and audited; **met**
- unsupported client version can be rejected. **met**

## M2 — People Vertical Slice
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
- create/edit/search people;
- connect organizations;
- record interaction;
- create next task;
- see task on Command Center;
- full audit trail.

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
