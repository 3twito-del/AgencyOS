# ADR-0002: Pin the repository to the ALPHA SDK and default builds to FORGE

Status: Accepted
Date: 2026-09-06

## Context

`config/` carries two SDK pins as templates - `global.alpha.json` (10.0.400) and
`global.lab.json` (11.0.100-preview.7.26381.103) - with no rule for which one the
repository root uses. Before M0 there was no root `global.json` at all, so
`dotnet --version` resolved to whatever was newest on the machine, which was the
.NET 11 preview.

`docs/04_TECHNOLOGY_MATRIX.md` states the governing principle directly: "newest"
is not equivalent to "best for canonical data". `config/version-policy.yaml` sets
`canonical_data_allowed_from: alpha`.

The Windows client compounds the choice. Windows App SDK 2.4.0 is the ALPHA
baseline, and it is the version verified to build here.

## Decision

1. The root `global.json` pins the ALPHA baseline: SDK 10.0.400,
   `allowPrerelease: false`. It is a copy of `config/global.alpha.json`.
2. LAB builds swap the file rather than editing it, keeping both pins intact as
   templates.
3. `build/Version.props` defaults `AgencyOSChannel` to `forge`. Forge is the one
   ring that forbids real data outright in `config/release-channels.yaml`, so an
   unconfigured build cannot present itself to the release authority as entitled
   to canonical data. CI and release pipelines set the ring explicitly.
4. `AgencyOS.Windows` targets `net10.0-windows10.0.26100.0`.

## Why this is better than the alternatives

Pinning LAB at the root would put the preview toolchain under every build,
including the ones that will eventually touch real data, and would exercise
WinUI on a combination outside the ALPHA baseline. The rings exist precisely so
that aggression lives in FORGE and LAB rather than in the default path.

Defaulting the channel to `alpha` would be the opposite failure: a developer
build that has never been through a promotion gate would claim a ring where real
data is permitted. Defaulting to the most restricted ring fails safe.

## Consequences

- `dotnet --version` at the repository root now reports 10.0.400 rather than the
  machine default.
- Exercising .NET 11 requires an explicit, visible act.
- Local builds report `channel: forge`, which is accurate: they have passed no
  promotion gate.

## Migration / rollback

Swap `global.json` for `config/global.lab.json` to move the repository to LAB.
No data or contract implications; the pin governs compilation only.

## Known deviation from the pinned baseline

`CLAUDE.md` section 4 and `docs/04_TECHNOLOGY_MATRIX.md` name Windows SDK
10.0.28000.2705 as the frontier SDK, but only 10.0.26100.0 is installed on this
machine, so that is what `AgencyOS.Windows` targets. This is a machine
provisioning gap, not a decision to diverge. Either install the pinned SDK and
retarget, or amend the matrix - the two should not stay out of step.

## Evidence / metrics that would cause reconsideration

- Windows App SDK requiring a newer .NET than the ALPHA baseline provides.
- A promotion of .NET 11 through the gates in `config/version-policy.yaml`.
- A capability needed before M1 that exists only in the preview toolchain.
