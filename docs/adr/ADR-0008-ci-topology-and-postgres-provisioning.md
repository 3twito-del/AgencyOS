# ADR-0008: Split CI by capability, and provision PostgreSQL explicitly

Status: Accepted
Date: 2026-09-07
Supersedes: the single-job CI topology in ADR-0003 (its script-as-single-entrypoint decision stands)

## Context

ADR-0003 chose one `windows-latest` job on the reasoning that a Linux lane would
"report green while the client is broken". That reasoning was sound when the only
question was whether the solution compiled.

M1 changed the question. The suite now requires a real PostgreSQL server, pinned
to the ALPHA baseline 18.6 (`config/version-policy.yaml`), and fails rather than
skips without one. That created a provisioning problem the original topology
could not solve honestly:

- Linux service containers are not available to a Windows runner.
- Docker on `windows-latest` runs Windows containers; `postgres:18.6` is a Linux
  image.
- The first attempt started whatever `postgresql*` service the GitHub Windows
  image happened to ship, guessing the service name and the `postgres`/`root`
  credentials. That is an assumption about a runner image, not a decision. It
  could break on an image update, and it pins no PostgreSQL version at all -
  which for a ring model built on exact version pinning is the wrong failure.

## Decision

Two jobs, each proving what it can actually prove.

1. **`windows-build` on `windows-latest`** builds the entire solution including
   the WinUI 3 client, and runs everything that needs no database: unit tests,
   contract generation, version metadata.
2. **`integration-tests` on `ubuntu-latest`** runs the database suite against a
   `postgres:18.6` service container - the exact ALPHA baseline image, pinned,
   with explicit credentials and a `pg_isready` health gate.

`scripts/Invoke-AgencyOS.ps1` gains `test-unit`, `test-integration` and
`contract`, so both jobs still call the one canonical entrypoint. `pwsh` is
preinstalled on both runner images.

In the nightly workflow the artifact job declares `needs: integration-tests`, so
no artifact is published from a commit whose schema or API behavior is broken.

## Why this is better than the alternatives

Installing PostgreSQL on the Windows runner - by Chocolatey, or by unpacking
EnterpriseDB binaries - would preserve one job at the cost of a slower, more
fragile step, and would still not guarantee 18.6 specifically. A service
container pins the exact image the ALPHA baseline names.

The original objection to a Linux lane does not apply to this split. It was about
a lane that builds *less* of the product and reports success. Here the Windows
job still builds the whole solution, client included; the Linux job runs tests
that touch the API host and the schema, neither of which involves the Windows
client. Nothing is proven on Linux that Windows was supposed to prove.

Running the database suite on Windows against a locally installed server was the
third option. It works for a developer - it is how this repository is verified
locally - but as a CI strategy it reintroduces the version drift the ring model
exists to prevent.

## Consequences

- CI needs both a Windows and a Linux runner. Windows minutes are billed higher;
  the Linux job is the cheap one and carries the slowest tests.
- The database suite does not execute on Windows in CI. The API host is
  platform-neutral, so this is a real but small gap; it is closed locally, where
  the suite runs on Windows on every `verify`.
- `Invoke-AgencyOS.ps1 nightly` runs unit tests only, because the database gate
  is a separate job. `verify` remains the full local check.

## Migration / rollback

Both workflows are data. Reverting to one job means deleting the Linux job and
restoring a Windows provisioning step; no repository structure depends on the
split.

## Verification status

The **CI** workflow has executed on GitHub against
`3twito-del/AgencyOS` (private): run 34082661680 on commit `bfa8204`, both jobs
green, with `docker.io/library/postgres:18.6` pulled and reported healthy. That
run is the authoritative evidence for the PostgreSQL 18.6 ALPHA baseline gate.

The **Nightly** workflow has not executed. It is triggered by schedule and manual
dispatch only, so a push does not exercise it, and it was not dispatched manually
because doing so would consume Windows runner minutes without adding evidence M1
needs. Its artifact path therefore remains verified statically and locally only.
That distinction should continue to be reported precisely.

## Evidence / metrics that would cause reconsideration

- Windows runners gaining usable Linux service containers, which would collapse
  the split back to one job.
- A platform-specific defect in the API host that only a Windows integration run
  would catch.
- Runner minutes becoming a material cost.
