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

Promotion pass (2026-09-07): first-run initialization now publishes the initial
release policy in the same transaction as the organization, owner and
initialization record, so a bootstrapped instance is immediately usable without
manual database seeding. The policy is the narrowest that works - the
bootstrapping client's own platform, ring and version, latest equal to minimum -
and compatibility and REVOKED enforcement are unchanged.

Outstanding for ALPHA promotion - the single remaining gate:

The suite passes against PostgreSQL 19 Beta 3, which is LAB evidence only. The
18.6 ALPHA-baseline run via Testcontainers cannot be performed on this machine.
Docker Desktop starts but its Linux engine does not:

```
wsl -d Ubuntu -e uname -r
  WSL2 is not supported with your current machine configuration.
  Please enable the "Virtual Machine Platform" optional component and ensure
  virtualization is enabled in the BIOS.
  Error code: Wsl/Service/CreateInstance/CreateVm/HCS/HCS_E_HYPERV_NOT_INSTALLED

(Get-CimInstance Win32_ComputerSystem).HypervisorPresent  ->  False
Get-Service com.docker.service                            ->  Stopped (needs admin)
docker pull postgres:18.6                                 ->  500 from the daemon
```

Prerequisites, all requiring Administrator and a reboot: enable virtualization in
firmware, enable the Virtual Machine Platform Windows feature, and allow
`com.docker.service` to start. Once `docker info` succeeds, the existing
Testcontainers path pins `postgres:18.6` automatically and the gate is closed by
running `verify` with `AGENCYOS_TEST_POSTGRES` unset.

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
