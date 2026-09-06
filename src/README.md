# AgencyOS source layout

Each project states the documents that govern it. When behavior and document
disagree, the document is the specification and the code is the defect - unless
an ADR in `docs/adr/` records a deliberate change.

| Project | Role | Governing documents |
|---|---|---|
| `AgencyOS.Domain` | Entities, value objects, domain commands | `docs/08_DATA_MODEL_FOUNDATION.md`, `docs/02_ARCHITECTURE.md` |
| `AgencyOS.Application` | Command/query orchestration, authorization and audit decisions | `docs/02_ARCHITECTURE.md`, `docs/07_SECURITY_AND_AUDIT.md` |
| `AgencyOS.Contracts` | Versioned API contracts, build identity | `docs/06_FORCED_UPDATE_PROTOCOL.md`, `docs/05_RELEASE_ENGINEERING.md` |
| `AgencyOS.Infrastructure` | Persistence, adapters, logging foundation | `docs/02_ARCHITECTURE.md`, `docs/10_ENGINEERING_STANDARDS.md` |
| `AgencyOS.Api` | ASP.NET Core host, server-side enforcement | `docs/02_ARCHITECTURE.md`, `docs/07_SECURITY_AND_AUDIT.md` |
| `AgencyOS.Windows` | WinUI 3 native client | `docs/12_WINDOWS_NATIVE.md`, `CLAUDE.md` principle 2 |
| `AgencyOS.Tests.Unit` | Unit and invariant tests | `docs/11_TESTING_AND_FORMAL_METHODS.md` |
| `AgencyOS.Tests.Integration` | API contract and host tests | `docs/11_TESTING_AND_FORMAL_METHODS.md` |

## Reference direction

```
Windows  ->  Contracts
Api      ->  Application, Contracts, Infrastructure
Infrastructure -> Application, Domain
Application    -> Domain
Domain         -> (nothing)
```

`AgencyOS.Windows` referencing only `AgencyOS.Contracts` is what makes
"the Windows client never depends on the DB schema"
(`docs/02_ARCHITECTURE.md` section 4) a compile-time fact.

## Modules inside layers

Modules named in `docs/02_ARCHITECTURE.md` are folders and namespaces within the
layer projects, not separate assemblies. See `docs/adr/ADR-0001-repository-structure.md`
for the reasoning and the promotion path.

## What M0 deliberately does not contain

No business entities, no persistence, no authentication, no authorization, no
audit, no AI, no message broker, no container orchestration. Those begin at M1
(Identity, Organization, Audit) and M2 (People vertical slice).
