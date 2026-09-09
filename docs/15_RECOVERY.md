# 15 — Recovery: disaster scenarios and incident response

For the day something is already wrong. Read the scenario that matches, not the
whole document.

**First, three things that are true in every scenario:**

1. **PostgreSQL is canonical.** If it is intact, the agency's record is intact.
2. **A workstation is never canonical.** Losing one loses no business data. Even a
   device-local AI run persisted its result server-side before the client saw it.
3. **Restore terminates every database connection.** The API will not recover on
   its own — restart it. This was learned from a drill rather than an outage.

## Restore

```bash
# Verify first. Without -Force this reports what it would do and stops.
pwsh ./scripts/Restore-AgencyOS.ps1 \
    -BackupPath /var/backups/agencyos/<date> \
    -ConnectionString "$AGENCYOS_CONNECTION" \
    -BlobRoot /var/lib/agencyos/blobs

# Then, having read that:
pwsh ./scripts/Restore-AgencyOS.ps1 ... -Force
```

The script checks the dump against the manifest hash **before** it drops anything.
A restore that destroyed a working database and then discovered a truncated dump
would turn a recoverable situation into an unrecoverable one.

Afterwards, always:

```bash
# 1. restart the API — its connections were killed by the restore
# 2. confirm it is back
curl -fsS http://<host>:<port>/health/ready
curl -fsS http://<host>:<port>/version

# 3. reconcile the database against the bytes
pwsh ./scripts/Test-AgencyOSIntegrity.ps1 \
    -ConnectionString "$AGENCYOS_CONNECTION" -BlobRoot /var/lib/agencyos/blobs
```

## Scenarios

### PostgreSQL lost, blobs intact

**Lost:** every business record since the last backup.
**Recoverable:** everything up to it.

1. Rebuild or replace the server; install PostgreSQL 18.6.
2. Restore with `-Force`.
3. Restart the API; check readiness.
4. Run the integrity check. Expect **orphans** — blobs written after the dump.
   They are harmless: content nothing references. Do not delete them; a later
   re-upload of the same document will deduplicate onto them.

### Blob root lost, database intact

**Lost:** document *content* since the last backup. Metadata, hashes, versions,
links and audit remain.
**Recoverable:** every document in the backup, byte-identical.

1. Recreate the directory with correct ownership.
2. Restore blobs — `Restore-AgencyOS.ps1` with `-BlobRoot`, which refuses if the
   file count disagrees with the manifest.
3. Run the integrity check. **Missing** entries are documents whose bytes are
   genuinely gone: the record shows they existed, who filed them and when, and
   they must be re-supplied from the source.

### Both lost

Restore both from the same backup — they were taken together and the manifest
binds them. Then integrity-check; a consistent backup should report nothing.

### Data Protection key ring lost

**Lost:** the ability to decrypt stored mailbox credentials.
**Not lost:** any canonical business data. People, deals, contracts, finance,
documents and audit do not depend on these keys.

Recovery is to **re-authorize each mailbox**. Nothing needs restoring and nothing
is corrupt; the stored ciphertext is simply unreadable and will be replaced.

If the key ring is restored from backup while the database is *not*, they still
match — keys and credentials are independent.

### API host lost

**Lost:** nothing. The host is stateless apart from the two directories.

Deploy the release artifact to a new host, point it at the same database, blob
root and key ring, and start it. Verify `/version` against the release manifest so
you know which build you just brought up.

### Windows workstation lost

**Lost:** nothing canonical. Locally cached data is a convenience copy; the
encrypted local cache is not business truth.

Install the client on the replacement and sign in. If the machine may be in
someone else's hands, treat it as a security incident as well — see below.

### Provider credential compromised

**Lost:** trust in one mailbox connection.

1. Revoke the credential at the provider first. Nothing you do in AgencyOS
   revokes anything at Microsoft.
2. Remove the mailbox connection in AgencyOS.
3. Review the audit trail for outbound sends in the exposure window.
4. Re-authorize with fresh credentials.

The stored copy is encrypted at rest with the Data Protection key ring, so a
database backup alone does not disclose it — but a backup *plus* the key ring
does. Keep them separate.

## Incident response

AgencyOS is not an incident-management platform. This is a runbook.

**Every incident, in order:** contain → preserve evidence → restore service →
review. Restoring service before preserving evidence destroys the only account of
what happened, and the audit trail is append-only precisely so it survives the
part of an incident where people are in a hurry.

### Security incident

*Stolen workstation, stolen credential, suspected intrusion.*

- **Contain:** disable the affected user, revoke provider credentials at the
  provider, and where a build is implicated use the release authority to revoke
  it — a revoked client is refused by the server, which is tested.
- **Preserve:** the audit trail is append-only and enforced by a database trigger.
  Do not "clean up" anything. Take a backup now; it is evidence as well as a copy.
- **Restore:** re-issue access to the people who need it.
- **Review:** the audit trail records actor, action, entity and time for every
  consequential act.

### Suspected tenant-boundary violation

The most serious claim available, because tenant isolation is the guarantee
everything else rests on.

- Preserve first: back up before changing anything.
- Establish the fact. Cross-tenant references return `404` by rule and there are
  regression tests for it (ADR-0038); a report of one is a claim about a defect,
  not a configuration question.
- If real, treat it as a defect with the highest priority and a permanent
  regression test before the fix ships.

### Data-loss incident

- Stop writing. Every minute of continued use is a minute of divergence from the
  last good backup.
- Establish the boundary: what was lost, and since when.
- Restore per the scenarios above.
- Reconcile, and expect to re-supply anything the integrity check reports as
  missing.

### Release regression

- Roll the binaries back; **the schema stays forward** (`docs/14_OPERATIONS.md`).
- If the schema genuinely cannot support the old binaries, that is a restore with
  data loss — a much larger decision, and rarely the right one.

### Database outage

- `/health/ready` fails while `/health` succeeds: the process is alive and cannot
  reach PostgreSQL.
- No business data is at risk from an outage alone. Restore the database service;
  the API reconnects.

### Provider outage

- **Not an AgencyOS outage.** A failed mailbox provider does not take readiness
  down, deliberately: the CRM keeps working while mail does not.
- Outbound sends whose outcome is genuinely unknown are recorded as
  `UnknownOutcome` rather than guessed. They are not retried automatically,
  because sending a client the same message twice is worse than sending it late.
  Resolve them by checking the mailbox.

## Candidate alert conditions

Defined, not wired. There is no paging system to wire them to, and inventing one
would be infrastructure for its own sake.

| Condition | Why it is actionable |
|---|---|
| `/health/ready` failing | the API cannot reach canonical data |
| `backup.failure` | the recovery plan silently stopped existing |
| Restore verification failure | a backup is damaged; find out before you need it |
| Integrity check exit `3` | database and bytes have diverged |
| Repeated migration failure | an upgrade is half-applied |
| Rising `UnknownOutcome` count | outbound sends of uncertain fate accumulating |
| Revoked client still connecting | either a stale deployment or something worth looking at |
| Repeated authentication failure | credential stuffing, or a broken integration |
| Startup configuration refusal | a deployment came up misconfigured and stopped |

## What is not here, and why

- **RPO and RTO targets.** These require a business decision about acceptable
  loss. The drill measures a synthetic dataset in seconds; publishing that as an
  objective would be dressing a test timing as a commitment.
- **Off-machine backup infrastructure.** An operator responsibility. AgencyOS
  writes a backup where it is told and does not own where that lives.
- **Automated failover.** There is one instance by design (ADR-0037).
- **Backup encryption.** `pg_dump` custom format is compressed, not encrypted.
  Encrypting backups at rest is an operator responsibility today and a real
  residual risk (ADR-0039).
