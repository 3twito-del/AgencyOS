# ADR-0003: GitHub Actions on Windows runners, driven by one script

Status: Partially superseded by ADR-0008
Date: 2026-09-06

> **Revision, 2026-09-07.** The single-`windows-latest`-job topology decided here
> is superseded by `ADR-0008-ci-topology-and-postgres-provisioning.md`. M1's
> database suite requires a pinned PostgreSQL 18.6 server, which a Windows runner
> cannot host as a service container. The decision that CI and local engineering
> share one PowerShell entrypoint stands, and is carried forward.

## Context

`docs/03_ROADMAP.md` requires CI in M0 and a producible Nightly artifact as an
exit criterion, but no document names a CI provider.

`AgencyOS.Windows` is a WinUI 3 client and cannot build on a non-Windows agent,
so "a clean clone builds" is only true on Windows for the full solution.

`CLAUDE.md` section 8 names `scripts/Invoke-AgencyOS.ps1` and `.vscode/tasks.json`
as the canonical local engineering entrypoints.

## Decision

1. CI is GitHub Actions. `.github/workflows/ci.yml` runs on `windows-latest`
   for push, pull request and manual dispatch.
2. `.github/workflows/nightly.yml` runs the NIGHTLY ring build on a schedule and
   uploads the artifact.
3. Both call `scripts/Invoke-AgencyOS.ps1`, which gains `ci`, `nightly` and
   `version` targets. CI runs the same commands an engineer runs locally.
4. Both check out with `fetch-depth: 0` so build metadata resolves a real commit.
5. The nightly artifact contains the published API, the published Windows client
   and `build-metadata.json` carrying version, ring, build id, commit and API
   contract version.

## Why this is better than the alternatives

Encoding the build steps in workflow YAML would produce two definitions of the
build that drift, and would leave engineers unable to reproduce a CI failure
locally. One script keeps a single definition.

Linux runners are cheaper and faster but cannot build the primary client. A
split matrix that builds everything except the Windows client on Linux would
report green while the client is broken, which is the failure mode most worth
avoiding.

## Consequences

- CI requires Windows runners, which are billed at a higher rate.
- The workflows are inert until a remote exists; the underlying script targets
  are usable immediately.
- `artifacts/` is generated output and is git-ignored.

## Migration / rollback

Moving to another provider means writing a workflow that invokes the same script
targets. No repository restructuring is involved.

## Evidence / metrics that would cause reconsideration

- Windows runner minutes becoming a material cost.
- A pull-request feedback loop slow enough to change behavior, which a Linux
  fast lane for the non-Windows projects could address.
