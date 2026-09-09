# 14 — Operations: install, configure, upgrade

Written for an operator who did not build AgencyOS. Every command here is one you
can copy; every value that must be decided is called out as a decision rather than
left as an assumption.

If something in this document is wrong, the deployment is what is real. Fix the
document.

## Supported topology

One of each:

```
Windows client  →  ASP.NET Core API host  →  PostgreSQL 18.6
                          │
                          ├── blob root        (document content)
                          └── key ring         (Data Protection keys)
```

That is the whole of it. There is no broker, no cache server, no search cluster
and no second service, because M14 measured for them and found no need
(ADR-0037).

**One API instance is supported. Not two.** Two instances would hold separate
blob roots and separate Data Protection key rings, so each would serve documents
the other could not find and neither could read the other's stored mailbox
credentials. The host says so at startup when either path is left at its default.
Running a second instance requires shared storage for both, and that has not been
built or tested (ADR-0039).

## Prerequisites

| Component | Requirement |
|---|---|
| API host | .NET 10 runtime (`net10.0`). The published output is framework-dependent — the SDK is **not** required. |
| Database | PostgreSQL **18.6**. This is the ALPHA baseline and the version every gate runs against. |
| Client | Windows 10.0.26100 or later, x64. |
| Operator tools | PostgreSQL **18** client tools (`psql`, `pg_dump`, `pg_restore`) and PowerShell 7. A client older than the server cannot dump it. |

## Configuration

Supplied by environment variable, `appsettings.Production.json`, or any
configuration provider. **No secret belongs in a committed file.**

| Key | Required | What it is |
|---|---|---|
| `ConnectionStrings:AgencyOS` *(or `AGENCYOS_CONNECTION`)* | **yes** | Canonical database. On a ring that permits real data the host refuses to start without it. |
| `AgencyOS:BlobStore:RootPath` | **yes** | Directory the blob store owns entirely. Refused if absent or unwritable. |
| `AgencyOS:DataProtection:KeyPath` | **yes** | Directory holding the key ring. Refused if absent — see below. |
| `AgencyOS:Authentication:Mode` | **yes** | Must not be `Development` on a real-data ring; the host refuses. |
| `AgencyOS:Bootstrap:Token` | first run only | Enables first-run initialization. **Remove it afterwards.** |
| `AgencyOS:Limits:MaximumUploadBytes` | no | Largest accepted upload. Defaults to 256 MB. |
| `AgencyOS:Worker:Enabled` | no | Background communications worker. Defaults on. |
| `AgencyOS:Graph:ClientId` / `ClientSecret` / `TenantId` | no | Microsoft Graph mailbox provider. Absent means the provider is simply not offered. |
| `AgencyOS:Telemetry:OtlpEndpoint` | no | OpenTelemetry collector. |

### Why the key path is required rather than convenient

Without an explicit path, ASP.NET Core Data Protection falls back to whatever the
host offers. Under a service account with no profile that is an **in-memory key
ring**: every restart issues new keys, every stored mailbox credential stops
decrypting, and nothing in the logs names the cause. AgencyOS refuses to start in
that configuration rather than fail quietly weeks later.

The key ring protects exactly one thing — stored mailbox credentials, under the
purpose string `AgencyOS.Communications.MailboxCredential.v1`. Losing it means
re-authorizing each mailbox. **No canonical business data depends on it.**

## Filesystem permissions

The service identity needs:

| Path | Access |
|---|---|
| Blob root | read / write / create subdirectories |
| Key ring | read / write |
| Application directory | read / execute |
| Temp | read / write |

It does **not** need local administrator. If it appears to, something is
misconfigured — say so rather than granting it.

## Database privileges

The runtime principal needs `CONNECT`, and `SELECT`/`INSERT`/`UPDATE`/`DELETE`
plus sequence usage on the application schema. **It does not need superuser.**

Migration is a separate act with separate rights: applying `migrate.sql` needs
DDL on the schema. Running the application as the migrating principal is
convenient and not required; separating them is the safer arrangement where an
operator is willing to maintain two roles.

## Install

### 1 — Create the database

```bash
createdb --host <host> --username <admin> agencyos
```

### 2 — Apply the schema

`migrate.sql` ships in the release artifact and is idempotent: applying it twice
is safe, and applying it to an up-to-date database does nothing.

```bash
psql --host <host> --username <migrator> --dbname agencyos --file migrate.sql
```

AgencyOS does **not** migrate at startup. That is deliberate — schema evolution
under expand → migrate → verify → contract needs a hand on it, and a binary that
migrated on boot would apply a schema change during an unattended restart.

### 3 — Prepare the directories

```bash
mkdir -p /var/lib/agencyos/blobs /var/lib/agencyos/keys
chown <service-account> /var/lib/agencyos/blobs /var/lib/agencyos/keys
chmod 700 /var/lib/agencyos/keys
```

### 4 — Configure and start

```bash
export AGENCYOS_CONNECTION='Host=...;Port=5432;Database=agencyos;Username=...;Password=...'
export AgencyOS__BlobStore__RootPath=/var/lib/agencyos/blobs
export AgencyOS__DataProtection__KeyPath=/var/lib/agencyos/keys
export AgencyOS__Authentication__Mode=<your identity provider mode>

dotnet AgencyOS.Api.dll
```

The host refuses to start on a misconfiguration rather than running in a state
that loses data later. Each refusal names what to fix.

### 5 — Confirm it is up

```bash
curl -fsS http://<host>:<port>/health          # process alive
curl -fsS http://<host>:<port>/health/ready    # can reach PostgreSQL
curl -fsS http://<host>:<port>/version         # build identity
```

`/version` reports version, channel, build id, commit and API contract. Compare it
against `release-manifest.json` to confirm what is actually running.

### 6 — First-run initialization

Set `AgencyOS:Bootstrap:Token`, restart, and post it to the bootstrap endpoint to
create the first organization and owner.

**Then remove the token and restart.** It cannot re-initialize an initialized
system — that is tested — but a secret that has outlived its single use should not
remain configured.

### 7 — Take a backup before anybody uses it

The first backup should happen before the first real record, so the restore path
is exercised while nothing is at stake.

## Upgrade

### Preflight

1. Read the release manifest and verify it describes the artifacts you have:
   ```bash
   pwsh ./scripts/Test-ReleaseManifest.ps1 -ReleasePath <release>
   ```
2. Note `expectedSchema` and `apiContractVersion`.
3. Confirm the client version policy: a forced-update boundary will refuse older
   clients after deployment. Know which clients are in use before you create that
   situation.

### Sequence

1. **Back up, and verify the backup.** Not optional; this is the rollback plan.
   ```bash
   pwsh ./scripts/Backup-AgencyOS.ps1 -ConnectionString "$AGENCYOS_CONNECTION" \
       -BlobRoot /var/lib/agencyos/blobs -Destination /var/backups/agencyos/pre-upgrade
   ```
2. **Stop the API.** Brief downtime is the supported model; there is no rolling
   upgrade, because there is one instance.
3. **Apply the migration.**
   ```bash
   psql --host <host> --username <migrator> --dbname agencyos --file migrate.sql
   ```
4. **Deploy the new binaries** over the application directory. Leave the blob root
   and key ring alone — they are state, not application.
5. **Start the API** and check `/health/ready` and `/version`.
6. **Publish the client** and let the release authority handle the rest.

### Rollback is asymmetric

| Component | Rolls back by |
|---|---|
| API binaries | redeploying the previous artifact |
| Windows client | publishing the previous client release |
| Configuration | restoring the previous values |
| **Database schema** | **it does not** |

Schema moves forward. Migrations are written expand → migrate → verify →
contract, so the previous binaries keep working against the newer schema — that
is what makes a binary rollback safe without a schema rollback.

If a release must be undone **and** the schema genuinely cannot support the old
binaries, that is a restore, not a rollback: use the pre-upgrade backup and accept
the loss of everything since. Reach for that only when the alternative is worse.

## Backup

```bash
pwsh ./scripts/Backup-AgencyOS.ps1 \
    -ConnectionString "$AGENCYOS_CONNECTION" \
    -BlobRoot /var/lib/agencyos/blobs \
    -Destination /var/backups/agencyos/$(date +%Y-%m-%d)
```

The script emits `backup.start`, `backup.database.done`, `backup.blobs.done` and
`backup.success` or `backup.failure`, so a scheduler can tell what happened.

**What must be backed up**

| Item | In the script | Why |
|---|---|---|
| PostgreSQL | yes | canonical business truth |
| Blob root | yes | document content lives outside the database |
| Key ring | **no — copy it yourself** | small, changes rarely, and without it stored mailbox credentials are unreadable |
| Configuration | **no — copy it yourself** | contains secrets; belongs wherever your secrets live |

**Scheduling is yours.** AgencyOS does not schedule backups; use cron, a systemd
timer or Task Scheduler. Frequency is a business decision about acceptable loss,
and this document will not invent one.

**Storage is yours, and it must be off this machine.** A backup on the same disk
as the database survives a mistake and not a disk. It is also the most portable
copy of everything the agency knows — treat it as such, and encrypt it at rest.
AgencyOS does not encrypt backup files; `pg_dump` custom format is compressed,
not encrypted.

**Verify by restoring.** A backup nobody has restored is a hypothesis. Restore to
a scratch database periodically; the drill in CI proves the mechanism, not your
particular backup.

## Integrity check

Reconciles the database against the bytes on disk:

```bash
pwsh ./scripts/Test-AgencyOSIntegrity.ps1 \
    -ConnectionString "$AGENCYOS_CONNECTION" -BlobRoot /var/lib/agencyos/blobs
```

Reports four kinds of disagreement — missing, orphan, mismatch, foreign — and
**never repairs**. Exit code `3` means anomalies were found and listed.

An orphan is usually a backup taken while an upload was in flight. A missing file
may be a restore still running. Investigate before removing anything.

## What is monitored, and what is not

Available now: `/health`, `/health/ready`, `/version`, OpenTelemetry traces and
counters (search queries, sync pages, idempotent replays), and structured logs
carrying identifiers and categories rather than content.

**Not built:** alerting integration, dashboards and SLO measurement. Candidate
alert conditions are listed in `docs/15_RECOVERY.md`; wiring them to a paging
system is an operator decision AgencyOS does not make for you.
