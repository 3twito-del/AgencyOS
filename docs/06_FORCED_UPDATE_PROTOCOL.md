# Forced Update & Compatibility Protocol

Update enforcement is a security/data-integrity mechanism, not merely UX.

## Layer 1 — MSIX/App Installer

Use signed MSIX packages and an App Installer feed.

Nightly policy concept:
- check on launch;
- block activation when update is required;
- allow forced rollback/downgrade in emergencies.

## Layer 2 — AgencyOS Release Authority

Client sends before loading business data:

```json
{
  "platform": "windows-x64",
  "channel": "alpha",
  "clientVersion": "0.4.0-alpha.12",
  "buildId": "20260906.1842",
  "gitCommit": "example",
  "apiContractVersion": 9,
  "localSchemaVersion": 21,
  "windowsBuild": "example"
}
```

Server returns:

```json
{
  "platform": "windows-x64",
  "channel": "alpha",
  "policy": "MANDATORY",
  "reason": "Version 0.4.0-alpha.12 is below the minimum supported version 0.4.0-alpha.13.",
  "blocksProtectedMutations": true,
  "latestVersion": "0.4.0-alpha.14",
  "minimumSupportedVersion": "0.4.0-alpha.13",
  "apiContract": {
    "minimum": 9,
    "maximum": 10
  },
  "securityEpoch": 4,
  "mandatoryAfterUtc": null,
  "killSwitch": false,
  "rollbackTarget": null,
  "artifact": {
    "sha256": null,
    "signature": null
  }
}
```

`blocksProtectedMutations` states the operative outcome so a client does not have
to reimplement the severity table. It is informational only: the server recomputes
the same decision on every protected request, so a client that ignores it, never
performs a handshake, or has been modified is refused identically.

This shape reconciles the earlier narrower response with
`config/release-policy.example.json`. See
`docs/adr/ADR-0005-release-handshake-contract.md`.

## Enforcement

The handshake informs. Enforcement is separate and unconditional.

Every request carries client identity headers:

```
X-AgencyOS-Platform        windows-x64
X-AgencyOS-Channel         alpha
X-AgencyOS-Client-Version  0.4.0-alpha.14
X-AgencyOS-Api-Contract    9
X-AgencyOS-Build-Id        20260907.1842
```

Mutating requests under `/api` are evaluated against the release policy before
authentication. A blocked client receives problem details:

- `REVOKED` -> 403 Forbidden
- `MANDATORY` -> 426 Upgrade Required

Reads are not blocked, and the handshake route itself is always reachable, so a
revoked build can still explain itself and fetch its update. A client that
presents no usable identity headers cannot be evaluated, and is therefore refused
for mutations.

Severity order, most severe first: kill switch or revoked version; API contract
outside the supported range; below minimum supported version; past an update
deadline; behind but supported; current.

## Policies

- NONE
- AVAILABLE
- RECOMMENDED
- MANDATORY
- REVOKED

`REVOKED` must be enforced server-side; the old client cannot bypass it by altering the UI.

## Channel policy

### NIGHTLY
minimumSupportedVersion = latest
Always update on launch.

### ALPHA
Usually support latest and previous build.
Security/data/schema incompatibility may force latest immediately.

### BETA/RC
Defined compatibility range.

### STABLE
Force only for:
- security;
- data integrity;
- incompatible protocol/schema;
- revoked signing/security epoch;
- emergency rollback.

## Kill switch

A revoked build may be placed into:
- update-only mode;
- recovery mode;
- read-only mode.

Never permit a known-dangerous build to write canonical data.

## Crash-loop rollback

Bootstrapper records recent launch failures. If a newly installed build repeatedly fails before healthy startup:
- stop relaunch loop;
- consult release authority;
- allow signed rollback target;
- preserve logs and local recovery state.
