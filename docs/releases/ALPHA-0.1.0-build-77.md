# AgencyOS ALPHA 0.1.0, build 77

The first ALPHA version of AgencyOS, and the release of the cumulative
post-audit repair programme.

**Date:** 2026-09-18

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **77** |
| Executable commit | **`d8a8bc56058e4ea5891f866222330db21abf5ad8`** |
| Tag | **`alpha-d8a8bc5`** |
| Branch | `repair-wave-001` |
| API contract version | **14** |
| Expected schema | `20260909072201_AiResultClassification` |
| Signing | **unsigned** |

`signing = unsigned` is the correct state for this ring, not an omission. Signed
artifacts, SBOM provenance and staged rollout are STABLE requirements
(`docs/05_RELEASE_ENGINEERING.md`). ALPHA's gates are migration, backup and
data-integrity gates, and those are the ones that ran.

## 2. The executable commit is not the branch tip

**This matters more than anything else in this document.**

Every gate below ran against `d8a8bc56`. Commits after it on `repair-wave-001`
are **documentation only** — this file and the ledger updates that record the
result. They change no source, no test, no script and no workflow.

To build, install or audit what was validated, use the tag `alpha-d8a8bc5` or the
commit `d8a8bc56058e4ea5891f866222330db21abf5ad8`. Do not assume the branch tip
is the executable: it is not, by construction.

To confirm that claim rather than accept it:

```
git diff --stat d8a8bc56058e4ea5891f866222330db21abf5ad8..repair-wave-001
```

Every path it reports should be under `docs/`. If any is not, the tip is a
different executable and this document does not describe it.

## 3. What validated it

Two independent workflow runs, both against the exact commit above.

### CI — run [35380367444](https://github.com/3twito-del/AgencyOS/actions/runs/35380367444), build number 77

| Gate | Result |
| --- | --- |
| Build | **0 warnings, 0 errors** |
| Unit tests | **3,832 / 3,832** |
| Windows tests | **1,043 / 1,043** |
| Reviewer tests | **163 / 163** |
| Integration tests, **PostgreSQL 18.6** | **871 / 871** |
| API contract | OpenAPI **3.1.1**, **263 paths, 187 schemas** |
| Formal (TLC) | **4 / 4** — `OfflineWriteQueue`, `OutboundSend`, `AiApproval`, `LocalInferenceLease` |
| Release artifact | **111 artifacts**, manifest verified |

PostgreSQL **18.6** is the ALPHA-pinned database (`CLAUDE.md` §4). It is the
version that confers authority here; local corroboration against LAB PostgreSQL
19 is not a substitute and is not claimed as one.

### Nightly — run [35381444761](https://github.com/3twito-del/AgencyOS/actions/runs/35381444761), tag `nightly-d8a8bc5`

| Gate | Result |
| --- | --- |
| Integration tests, **PostgreSQL 18.6** | **871 / 871** |
| Unit tests | **3,832 / 3,832** |
| Nightly artifact (Windows) | **published** |

Nightly gates its Windows artifact on the integration job, so an artifact is
never published from a commit whose schema or API behaviour is broken.

### Release manifest

Read from the downloaded artifact, not inferred:

```
formatVersion = 1          version = 0.1.0        channel = alpha
buildId = 77               ciRun = 35380367444    signing = unsigned
gitCommit = d8a8bc56058e4ea5891f866222330db21abf5ad8
apiContractVersion = 14
expectedSchema = 20260909072201_AiResultClassification
artifacts = 111
```

The manifest binds the artifact set to this commit and carries a SHA-256 for
every file, and `Test-ReleaseManifest.ps1` verified it inside the run.

## 4. What is in it

The cumulative repair of Audits 001, 001R and 002: waves 003C through 003F, six
owner decisions, the final remaining-findings pass and `AOS-R002-003`. Thirty-five
findings, each with exactly one disposition — 24 repaired, 4 accepted as current
behaviour, 2 intentionally unsupported, 1 deferred with no reproduced defect, 4
closed by the audit itself as harness error, and **none** left as an open design
decision. `docs/reviews/local-post-audit-repair/FINAL-FINDING-LEDGER.md` is the
authoritative list.

The behavioural changes an operator will notice:

- **A recoverable refusal keeps the work.** A 400, 422, 409 or 403 from a
  mutation started in a dialog leaves the dialog open with every entered value,
  every selected entity and the operator's context intact, names the field the
  server objected to, and puts focus there.
- **A refusal is in human language.** "Reading people is not part of your role."
  rather than a permission identifier — with the machine identifier still in the
  problem document, where support can read it.
- **Authoring is discoverable.** The Intelligence workspace has one visible
  launcher built from the command registry. The palette remains an accelerator
  and is no longer the only route to an ordinary authoring capability.
- **`Enter` commits the ordinary primary action**, with four classified
  exceptions where it must not, because it was observed discarding a half-filled
  entry.
- Nineteen typed-identifier fields became pickers; an instant field offers an
  editable date **and** time; a missing required nested object answers 400 naming
  it rather than 500.

## 5. What this version does not claim

Stated so the validation is not read as broader than it is.

1. **Unsigned.** No code signing, no provenance attestation. Not required at
   ALPHA; required before STABLE.
2. **Not a security audit.** The public-readiness pass
   (`docs/reviews/public-readiness/`) was a secret and history scan — 0 findings
   across 1,879 historical blobs — not a penetration test or a threat model.
3. **One known authorization gap is recorded, not fixed.** A refusal raised by
   the endpoint authorization policy returns an empty 403, so the client shows
   "Forbidden" rather than the capability sentence. Application-guard refusals do
   carry it. This is observation 4 in the final ledger.
4. **217 fire-and-forget dispatch sites remain**, classified and not refactored.
   One reproduced sub-class of thirteen was repaired; no defect is attributable
   to the remaining pattern itself.
5. **Two `LinkRecordDialog` kinds still take an identifier** — a material belongs
   to a person and a version to a contract, and neither has a list of its own.
   The dialog says so.
6. **Three finance dialogs are deliberately unwired** — the obligations
   capability was never built (ADR-0032).
7. **Real data is permitted on this ring** (`config/release-channels.yaml`), so
   backup and restore are the operator's responsibility before installing it over
   anything that matters. FORGE and LAB clients must never point at this ring's
   database.

## 6. Reproducing the validation

```
git fetch --tags
git checkout alpha-d8a8bc5
pwsh -NoProfile -File scripts/Invoke-AgencyOS.ps1 verify
```

The integration suite needs PostgreSQL 18.6 and the PostgreSQL 18 client tools
on `PATH`; without the client tools the three backup/restore drills fail on a
missing `pg_dump`, which is an environment result and not a product one.
