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

**Adopted in M7** as `src/AgencyOS.Deals.Rules`: the deal and offer state machines,
negotiation-chain validation, term-value parsing and offer comparison. Pure - no
package references but FSharp.Core, and no EF Core, HTTP, logging, clock or
filesystem. C# owns the domain, application, API and client; F# owns rules, not
architecture.

The boundary is one class (`DealRules`). Everything crossing it is a primitive, a
plain array or a `[<CLIMutable>]` record; discriminated unions, options and F# lists
stay inside, and states cross as their persisted integers so C# keeps its own
enums. Tests walk every state, trigger, direction and value kind in both
directions, because the two vocabularies agree by convention rather than by
compilation.

Two integration facts worth knowing before adding another F# project:

- `LangVersion` must be scoped to `.csproj` in `Directory.Build.props`; FSC rejects
  a langversion of 14.0 outright.
- The SDK's implicit FSharp.Core reference resolves to the compiler's own copy
  inside the SDK directory. It compiles and then fails at run time in every
  consuming project, because the file is never copied. Set
  `DisableImplicitFSharpCoreReference` and reference the package.

See `docs/adr/ADR-0021-deal-rules-kernel-offer-immutability-and-agreed-terms.md`.

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
