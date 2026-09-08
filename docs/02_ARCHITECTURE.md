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

Two assemblies exist: `AgencyOS.Deals.Rules` (M7, extended in M8) and
`AgencyOS.Finance.Rules` (M9). Each is behind a single C# static facade.

**Adopted in M7** as `src/AgencyOS.Deals.Rules`: the deal and offer state machines,
negotiation-chain validation, term-value parsing and offer comparison. Pure - no
package references but FSharp.Core, and no EF Core, HTTP, logging, clock or
filesystem. C# owns the domain, application, API and client; F# owns rules, not
architecture.

**Extended in M8**, in the same assembly rather than a second one. The contract,
option and obligation state machines, grant periods, deadline resolution and
negotiated-against-drafted reconciliation live beside the M7 rules because they are
the same kind of thing and share the comparison the offer thread already used. A
second F# project for aesthetic separation would have split one coherent kernel and
duplicated the term vocabulary across the seam (ADR-0022).

The boundary is one class (`DealRules`). Everything crossing it is a primitive, a
plain array or a `[<CLIMutable>]` record; discriminated unions, options and F# lists
stay inside, and states cross as their persisted integers so C# keeps its own
enums. Tests walk every state, trigger, direction and value kind in both
directions, because the two vocabularies agree by convention rather than by
compilation.

**A second assembly in M9**: `src/AgencyOS.Finance.Rules`. Money arithmetic, one
rounding policy, allocation and residuals, double-entry validation, commission
selection and reconciliation outcomes.

The M8 argument for staying in one assembly does not carry over. Deal comparison
and contract deadlines are the same kind of thing and share the term vocabulary;
money arithmetic shares nothing with either. A change to the rounding policy should
not rebuild the reconciliation kernel, and the finance kernel has its own
vocabulary — sides, currencies, minor units — that would have to be kept apart
inside a shared assembly anyway. Two kernels, two facades, no shared types
(ADR-0023).

The reason for choosing F# again is the same and is not aesthetic: the rounding
policy is stated **once**, as a total function over a closed set of currencies,
instead of being written five times in five handlers and drifting. `Amount` carries
its currency inside the value, which makes cross-currency arithmetic
unrepresentable rather than merely discouraged.

Two integration facts worth knowing before adding another F# project:

- `LangVersion` must be scoped to `.csproj` in `Directory.Build.props`; FSC rejects
  a langversion of 14.0 outright.
- The SDK's implicit FSharp.Core reference resolves to the compiler's own copy
  inside the SDK directory. It compiles and then fails at run time in every
  consuming project, because the file is never copied. Set
  `DisableImplicitFSharpCoreReference` and reference the package.

One further gotcha, found in M9: a `[<Literal>]` decimal inside a module compiles
and then throws `InvalidProgramException` at run time, because F# emits decimal
literals through `DecimalConstantAttribute` and the resulting module initializer is
rejected by the CLR. A plain `let` binding is correct.

See `docs/adr/ADR-0021-deal-rules-kernel-offer-immutability-and-agreed-terms.md`
and `docs/adr/ADR-0023-finance-money-commission-and-the-ledger.md`.

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

**Still not introduced as of M10.** Document text extraction was the first
plausible occasion and did not justify it: plain-text formats are extracted
natively in .NET, and PDF and DOCX report `Unsupported` rather than being handed
to a parser this build has not fuzzed. No OCR, no embeddings and no model runs
anywhere in M10 (ADR-0024).

### TypeScript
Use only for required web ecosystems:
- Outlook/Office web add-ins;
- lightweight web/admin surfaces.

**Still not introduced as of M10.** The milestone that would have justified an
Outlook add-in built the integration boundary instead: mailboxes connect
server-side, messages synchronize server-side, and sending goes through the
AgencyOS API. A task pane would be a second client with its own authentication,
deployment, update and version story, added before anybody has used the first
one, and it would not make the server-side work any more correct.

The decision is recorded rather than deferred silently: TypeScript is not added to
satisfy the polyglot roadmap. It arrives when a real Office surface is asked for
and the ecosystem genuinely requires web technologies to deliver it.

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

Content:
- Stored file bytes live behind `IBlobStore`, never in a database column. The
  ALPHA implementation is a local filesystem and is not distributed object
  storage; a two-server deployment would not share it, which is a stated limit
  rather than a discovered one. Identity is the SHA-256 of the bytes, and
  deduplication is per tenant so the store cannot be asked whether another
  organization holds a given file. See
  `docs/adr/ADR-0024-blob-storage-content-addressing-and-ingestion.md`.
- A filesystem and PostgreSQL do not share a transaction and nothing pretends they
  do. Ingestion writes a ledger row, then the bytes, then the database rows, so
  the only reachable failure is bytes nobody references.

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

**M10 is the first milestone with background work, and it did not introduce
Temporal.** The mailbox synchronizer and the outbound send worker are a
`BackgroundService` in the API host, over a work queue that is the domain tables
themselves. Canonical work state is a row in PostgreSQL — never an in-memory list
— and a claim is one atomic
`UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING id`, so two
instances during a rolling deployment is ordinary rather than a hazard.

What the send protocol needs is recovery, and recovery here is "read the row and
reconcile" rather than deterministic replay of a workflow history. Adopting a
workflow engine would add a server, a worker fleet and a second definition of what
a workflow is, in exchange for a scheduler this milestone already has in twenty
lines of SQL. See `docs/adr/ADR-0029-background-work-leasing-and-not-temporal.md`
for the conditions under which that answer changes.

## Intelligence adds no infrastructure (M11)

M11 records sources, signals, theses, predictions and the work built on them, and
adds nothing to the stack: no Python service, no F# project, no model of any kind,
no vector store, no search engine and no new specification.

There is no distributed protocol to specify. Every M11 operation is a single
request against a single database inside one transaction; the only sequencing that
matters is the radar conversion, which is ordered so a failure leaves the entry
open rather than orphaning a pursuit. Brier scoring is a dozen lines of pure
decimal arithmetic with no state machine in it, so it lives in the application
layer beside the other read models rather than in F#.

The one schema mechanism M11 introduces is a deferred constraint: the seven
foreign keys on `intelligence_events` are `DEFERRABLE INITIALLY DEFERRED`, because
an event is written in the same unit of work as the object it happened to. See
`docs/adr/ADR-0030-intelligence-provenance-judgment-and-no-scores.md`.
