# Frontier Architecture

## 1. Logical topology

Windows Native Client
    |
    | HTTPS / OpenAPI
    | SignalR
    v
AgencyOS Core (ASP.NET Core)
    |
    +-- PostgreSQL canonical data
    +-- Object storage
    +-- Outbox / event dispatch
    +-- Search projections (later)
    +-- Workflow engine (later)
    +-- AI Tool Gateway
            |
            +-- Python Intelligence
            +-- Cloud model providers
            +-- Windows-local AI
            +-- Specialist native services

## 2. Modular monolith first

Initial modules:
- Identity
- Organization
- People
- Relationships
- Activity
- Tasks
- Search
- Audit
- ReleasePolicy

Later:
- Talent
- Projects
- Opportunities
- Submissions
- Deals
- Contracts
- Rights
- Finance
- Documents
- Intelligence
- Automation
- AI

Each module should expose:
- Domain
- Application
- Contracts
- Infrastructure/Persistence
- API endpoints/commands
- Tests

## 3. Polyglot boundaries

### C#
Primary language for the domain, application, API and Windows client.

### F#
Use only where algebraic modeling materially reduces illegal states:
- deal/offer state machines;
- financial allocation rules;
- contract/rights windows;
- validation engines.

### Rust
Candidate owner of:
- local sync engine;
- filesystem watchers;
- hashing/content-addressing;
- high-throughput native indexing;
- safe binary parsers.

### C++
Candidate owner of:
- Win32/WinRT/DirectX integrations;
- specialized rendering/media kernels;
- SIMD/GPU/NPU adapters.

### Python
Candidate owner of:
- NLP;
- extraction;
- OCR orchestration;
- embeddings;
- model evaluation;
- research pipelines;
- statistical/forecasting jobs.

### TypeScript
Use only for required web ecosystems:
- Outlook/Office web add-ins;
- lightweight web/admin surfaces.

## 4. Contracts

- REST + OpenAPI 3.1: external/domain API.
- gRPC + Protobuf: justified internal service boundaries.
- SignalR/WebSockets: realtime client events.
- Domain events: versioned event contracts.
- Never make the Windows client depend directly on DB schema.

## 5. Data

Canonical:
- PostgreSQL.

Local:
- SQLite cache, encrypted. "Where appropriate" was resolved in M3: the client
  cache holds contact data, so it is always encrypted with SQLCipher under a
  DPAPI-protected key, and isolated per channel, tenant and user. It is never
  canonical and can always be rebuilt from the change feed. See
  `docs/adr/ADR-0015-local-cache-encryption.md`.

Projections later:
- pgvector inside PostgreSQL first;
- OpenSearch only if search scale/behavior justifies it;
- graph store only for specialized graph analytics, never canonical truth;
- Redis only for ephemeral/cache needs.

## 6. Eventing

Start:
- Domain Events
- Transactional Outbox
- background dispatcher

Promote later only if required:
- NATS JetStream / Azure Service Bus
- Kafka-compatible stream at truly large event-log scale

## 7. Durable workflows

Use simple background workers first.

Introduce Temporal when workflows are long-lived, resumable and business-critical:
- option deadlines;
- approvals;
- contract execution;
- collections;
- payment schedules;
- multi-step AI/research workflows.
