# CLAUDE.md — AgencyOS Engineering Constitution

You are working on **AgencyOS**, a private, long-lived, Windows-first operating system/ERP for an entertainment representation agency.

This is not a demo, not a generic SaaS, and not a disposable prototype. The owner intends to evolve it for years and ultimately use it as the canonical operational system of a real agency.

## 1. Non-negotiable product principles

1. **Canonical truth lives in the server/domain/data layer, never in the Windows UI.**
2. **Windows is the primary client and must be genuinely native.**
3. **Every consequential business fact must be auditable.**
4. **Structured facts and unstructured judgment are distinct.**
5. **Business invariants are enforced server-side.**
6. **API contracts are versioned and explicit.**
7. **AI operates through tools/capabilities, never by bypassing permissions or directly mutating the database.**
8. **Extreme capability is allowed; uncontrolled complexity is not.**
9. **No technology is added solely for novelty.**
10. **No irreversible migration without backup, verification, and rollback/recovery strategy.**

## 2. Architecture baseline

Primary:
- Windows native client: C# + WinUI 3 + Windows App SDK + XAML.
- Core/domain/API: C# + ASP.NET Core.
- Data: PostgreSQL + SQL; EF Core/Npgsql by default, Dapper/manual SQL when justified.
- Contracts: OpenAPI 3.1 external/domain APIs; Protobuf/gRPC for justified internal high-performance boundaries.
- Realtime: SignalR/WebSockets when needed.
- Local cache: SQLite, encrypted where appropriate, never canonical.
- Packaging: MSIX/App Installer + signed artifacts + AgencyOS release authority.
- Observability: OpenTelemetry + structured logging + Windows-native diagnostics where useful.

Specialist languages:
- F#: correctness-heavy state machines, financial/deal rules, domain modeling where discriminated unions add clear value.
- Rust: sync engine, high-performance parsers, local native infrastructure, safe systems components.
- C++: Win32/WinRT/DirectX/native interop and performance-critical kernels only.
- Python: AI/ML/NLP/document/research/data science services.
- TypeScript: Office/Outlook/web surfaces only when those ecosystems require web technologies.
- PowerShell: Windows build/bootstrap/release engineering.
- TLA+/PlusCal: formal specification for critical distributed/stateful protocols.

## 3. Release rings

FORGE -> LAB -> NIGHTLY -> ALPHA -> BETA/RC -> STABLE

- FORGE: upstream main/HEAD/daily/custom forks; synthetic data only.
- LAB: official Experimental/Insider/Beta/RC; isolated data only.
- NIGHTLY: automated daily build; basic test gates.
- ALPHA: newest promoted technology proven against integration/migration/data-integrity gates; real private data allowed.
- BETA/RC: production-candidate behavior and compatibility.
- STABLE: organization-critical use.

Never connect FORGE/LAB clients to canonical ALPHA/STABLE databases.

## 4. Current pinned baseline (2026-09-06)

Do not assume these remain current forever. `config/version-policy.yaml` defines the tracking policy.

- .NET 11 public edge: SDK 11.0.100-preview.7 / runtime 11.0.0-preview.7; C# 15; F# 10.
- .NET real-data baseline: .NET 10.0.11 LTS, SDK 10.0.400, C# 14, F# 10.
- Windows App SDK: 2.4.1-experimental (LAB); 2.4.0 stable (ALPHA baseline).
- Windows SDK: 10.0.28000.2705 (FORGE/LAB frontier target, enabled when provisioned); 10.0.26100.0 (ALPHA validated target).
- PostgreSQL: development snapshot/HEAD (FORGE), 19 Beta 3 (LAB), 18.6 (ALPHA).
- Python: CPython main (FORGE), 3.15.0rc2 (LAB), latest supported stable for ALPHA.
- Rust: nightly for FORGE/LAB; stable for promoted real-data components unless a nightly-only capability is explicitly justified.
- TypeScript 6.0 for required web/Office surfaces.
- Node.js 26.8.1 Current for LAB tooling; use supported LTS where long-lived production tooling benefits.
- pgvector 0.8.6 when vector retrieval is introduced.
- Temporal .NET SDK 1.18.0 when durable workflows are introduced.
- Visual Studio 2026 Insiders 18.10 branch for edge; Visual Studio 2026 18.9.2 stable side-by-side.

## 5. Hard engineering rules

- Prefer a modular monolith until metrics and operational needs justify extraction.
- Do not create a microservice without an ADR describing the failure mode solved by extraction.
- Do not add Kafka, RabbitMQ, Redis, OpenSearch, Neo4j, Temporal, a vector DB, or Kubernetes before the capability is required.
- PostgreSQL remains canonical even when projections exist.
- Use Transactional Outbox before introducing a broker.
- Do not use full event sourcing by default.
- Money: never use binary floating point. Use decimal/integer minor units plus currency, and later a double-entry ledger.
- Domain status transitions must be commands, not arbitrary field edits.
- Every external side effect must be idempotent or guarded by an idempotency key.
- Every destructive or privileged operation must be audited.
- Prefer expand -> migrate -> verify -> contract database evolution.

## 6. Claude behavior

Before implementing a milestone:
1. Read the relevant docs.
2. State the exact milestone and acceptance criteria.
3. Identify only blockers that make implementation impossible; do not reopen settled architecture.
4. Implement the smallest coherent vertical slice.
5. Add tests before declaring the milestone complete.
6. Run build/test/static analysis.
7. Record meaningful architecture changes as ADRs.
8. Update roadmap status.

Never:
- redesign the entire repository because a local issue is inconvenient;
- replace a pinned technology without explicit approval;
- add speculative infrastructure "for later";
- generate a huge codebase in one shot;
- silently weaken authorization, auditability, data integrity, or update enforcement;
- put business logic in XAML code-behind;
- put provider-specific AI calls throughout domain code;
- allow old clients to bypass server-enforced minimum-version policy.

## 7. Definition of done

A feature is done only when:
- domain behavior is explicit;
- persistence is migrated and tested;
- API contract is versioned;
- authorization is enforced server-side;
- audit behavior is correct;
- Windows UI supports the intended workflow;
- tests cover invariants and integration;
- telemetry exists for consequential failure paths;
- documentation reflects the implemented behavior.


## 8. VS Code / Claude Code project workflow

This repository is configured for Claude Code inside VS Code.

- Treat `.claude/settings.json` as shared project policy.
- Treat `.claude/settings.local.json` as personal/uncommitted overrides.
- Use `.claude/skills/` for repeatable procedures.
- Use `scripts/Invoke-AgencyOS.ps1` and `.vscode/tasks.json` as canonical local engineering entrypoints.
- Do not change the user's VS Code `initialPermissionMode`; workspace values are not authoritative for that setting.
- Do not enable bypassPermissions on the user's behalf.
- Do not weaken deny rules for secrets/destructive Git operations without explicit approval.
- Use VS Code checkpoints before broad refactors, migrations, or release-configuration changes when available.
