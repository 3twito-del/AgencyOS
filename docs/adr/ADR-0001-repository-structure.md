# ADR-0001: Layer projects with module folders for the M0 skeleton

Status: Accepted
Date: 2026-09-06

## Context

`prompts/001_M0_REPOSITORY.md` prescribes eight layer-oriented projects
(Domain, Application, Infrastructure, Api, Windows, Contracts, and two test
projects). `docs/02_ARCHITECTURE.md` section 2 says each module exposes its own
Domain, Application, Contracts, Infrastructure, API endpoints and Tests.

Read literally, the second implies one project set per module, which at the
eighteen modules named in `docs/02_ARCHITECTURE.md` would mean well over a
hundred projects before a single business entity exists.

`docs/10_ENGINEERING_STANDARDS.md` also asks for warnings-as-errors
"progressively for owned projects", while `config/Directory.Build.props.example`
enables `TreatWarningsAsErrors` unconditionally for every project at once.

## Decision

1. Layers are the assembly boundary. Modules are folder and namespace
   boundaries inside those assemblies: `src/AgencyOS.Domain/Identity/`,
   `src/AgencyOS.Domain/People/`, and so on from M1 onward.
2. A module is promoted to its own assembly only when it needs an independent
   dependency set, an independent lifecycle, or enforcement of a boundary that
   namespaces cannot express. That promotion requires its own ADR.
3. `AgencyOS.Windows` references `AgencyOS.Contracts` and nothing else.
4. Warnings-as-errors is opt-in per project through `AgencyOSStrict`. M0 enables
   it for Domain, Application and Contracts. Infrastructure, Api and Windows
   follow as their surfaces stabilize.
5. The solution uses the classic `.sln` format. The .NET 10 SDK now defaults
   `dotnet new sln` to `.slnx`, but `.vscode/settings.json` and
   `scripts/Invoke-AgencyOS.ps1` already name `AgencyOS.sln`.

## Why this is better than the alternatives

A project per module per layer buys enforced boundaries at the cost of build
time, IDE load time and eighteen-fold ceremony for entities that do not exist
yet. Namespaces give the same conceptual separation now, and the promotion path
stays open because layer dependencies already run one way.

Turning warnings-as-errors on everywhere at once means the first WinUI XAML
generation warning blocks the build. Opting in per project delivers strictness
where the domain rules live, which is where `docs/10_ENGINEERING_STANDARDS.md`
cares most.

## Consequences

- Module isolation is a convention inside Domain and Application, not a
  compiler-enforced boundary. Cross-module references must be caught in review
  or by an architecture test.
- Adding an architecture test that asserts module folders do not reference one
  another is a natural M1 or M2 addition.
- The one boundary that is compiler-enforced today is the important one: the
  Windows client cannot reach persistence.

## Migration / rollback

Extracting a module to its own assembly is a file move plus a project
reference; no data or contract migration is involved. Rolling the solution back
to `.slnx` is a regeneration plus two configuration edits.

## Evidence / metrics that would cause reconsideration

- Cross-module references appearing in review that namespaces failed to prevent.
- Build times where incremental compilation of one large Domain assembly becomes
  the dominant cost.
- A module acquiring a dependency the rest of the domain must not carry.
