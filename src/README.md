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

Modules present after M1:

| Module | Domain | Application | Infrastructure |
|---|---|---|---|
| Identity | `Identity/` | `Abstractions/` | `Persistence/`, `Authorization/` |
| Organization | `Organizations/` | `Organizations/` | `Persistence/` |
| Membership | `Memberships/` | `Memberships/` | `Persistence/` |
| Authorization | `Authorization/` | `Authorization/` | `Authorization/` |
| Audit | `Audit/` | `Audit/` | `Persistence/` |
| ReleasePolicy | `Releases/` | `Releases/` | `Persistence/` |
| Provisioning | `Provisioning/` | `Provisioning/` | `Persistence/` |

## Where enforcement lives

Three properties are enforced in more than one place on purpose. Removing any
single layer leaves the system still correct, which is the test of whether the
duplication was worth it.

| Property | Layers |
|---|---|
| Audit immutability | domain type with no mutator; save interceptor; database triggers (`ADR-0006`) |
| Authorization | endpoint policy as an early gate; scoped check inside the command handler, which is authoritative (`ADR-0007`) |
| Client compatibility | handshake response tells the client; middleware refuses the mutation regardless (`ADR-0005`) |
| First-run bootstrap | out-of-band token; uninitialized-system check; database singleton with a CHECK constraint (`ADR-0009`) |

## Operational surface

| Endpoint | Purpose |
|---|---|
| `GET /health` | Liveness. Runs no checks; a database outage must not cause instance restarts. |
| `GET /health/ready` | Readiness. Fails when canonical PostgreSQL is unreachable. |
| `GET /version` | Build identity presented to the release authority. |
| `GET /openapi/v1.json` | The versioned OpenAPI 3.1 contract. |

`scripts/Invoke-AgencyOS.ps1 contract` regenerates the contract into
`artifacts/openapi/` and fails if it is not OpenAPI 3.1 or does not describe the
expected surface.

## What is deliberately absent

No business verticals yet: no talent, projects, opportunities, deals, contracts
or finance. No AI, message broker, cache, search index or container
orchestration. Those arrive at the milestones that need them, and not before -
`CLAUDE.md` section 5.

Identity is a development scheme that trusts a header. The host refuses to start
if it is configured on a ring that permits real data.

First-run initialization exists but is inert unless a bootstrap token is
configured: with none, the route is not mapped at all.

## First run

`POST /api/v1/system/bootstrap` initializes an empty system in one transaction:
the first organization, the first user, an owner membership, the initialization
singleton, and the initial release policy that makes the instance usable. All of
it is audited. Nothing partial can be left behind.

The initial policy is the narrowest one that works - the bootstrapping client's
own platform, ring and version, with latest equal to minimum - so only that build
is admitted. Publishing a policy for anything else is an ordinary authorized
operation.

**Remove the bootstrap token from the deployment once initialization has
succeeded.** It grants exactly one irreversible act and has no further use. A
repeat attempt is refused and changes nothing, and the server logs a warning
saying the token is still configured. See
`docs/adr/ADR-0009-first-run-bootstrap.md`.
