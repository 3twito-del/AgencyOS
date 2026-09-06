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
  "policy": "MANDATORY",
  "latestVersion": "0.4.0-alpha.14",
  "minimumSupportedVersion": "0.4.0-alpha.13",
  "apiContract": {
    "minimum": 9,
    "maximum": 10
  },
  "securityEpoch": 4,
  "mandatoryAfterUtc": null,
  "killSwitch": false,
  "rollbackTarget": null
}
```

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
