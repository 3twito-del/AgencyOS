# Validation infrastructure closure

**Date:** 2026-09-24 · **Status:** CLOSED · **C11:** PROVED

The default branch produces a readable, green, genuine scheduled validation
signal. This closes the validation-infrastructure stage. It does **not** end the
feature freeze.

---

## 1. Baseline

| | |
| --- | --- |
| Release | ALPHA 0.1.0 **build 95** |
| Executable commit | `901ba19a73c1d2dc48e18c28bcbc01b81cf604d6` |
| Release tag | `alpha-901ba19` (annotated, dereferences to the executable) |
| API contract | **17** |
| Expected schema | `20260909072201_AiResultClassification` |
| Default-branch SHA proved | `e92a7e7d8a07c3e61fdaebea92d2b6eb7ed4c924` — the documentation-only child of `901ba19` |

No product code, test, contract, schema, migration, workflow, tag or build number
changed in this stage. There is no build 96.

## 2. The historical problem

From 2026-09-17 the **scheduled** Nightly on `master` failed every night on
`2c366cd`, then the tip of `master`. The integration job failed in
`BackupRestoreDrillTests`: the runner image's `pg_dump` was version 16, older than
the PostgreSQL 18.6 service, and AgencyOS's backup guard correctly refused to dump
a newer server with an older client. The downstream Windows artifact job was
therefore skipped.

The product behaved correctly. `master`'s Nightly workflow lacked the PostgreSQL
18 client provisioning that the working branch already had. The consequence was
worse than the failure: with the default branch red every night, **a genuine
regression on `master` would have been indistinguishable from the noise.** That
is exit criterion C11.

The Reality Closure observation that recorded it is kept, with its terminal
section, in the evidence area (`30-VALIDATION-INFRASTRUCTURE-OBSERVATION.md`).

## 3. Promotion

`master` was **fast-forwarded** from `2c366cdf83a53f266758fd676d035961b9171940` to
`e92a7e7d8a07c3e61fdaebea92d2b6eb7ed4c924`: 77 commits, `master` an ancestor of
the validated branch, no merge commit, no rebase, no cherry-pick, no force push.
`master` remains the repository's default branch. `repair-wave-001` was not
renamed or deleted, and no tag was created or moved.

## 4. CI on the promoted branch

| | |
| --- | --- |
| Run | CI `35885898666` |
| Event / branch / SHA | `push` / `master` / `e92a7e7` |
| Conclusion | **success** — build 0 warnings; unit 4,073; Windows 1,384; reviewer 163; integration 949 on PostgreSQL 18.6; OpenAPI 264 paths, 188 schemas; contract 17; TLC 4/4; release manifest verified |

## 5. The scheduled proof

| | |
| --- | --- |
| Workflow | Nightly |
| Run | `35972891307`, run number **52**, attempt 1 |
| Event | **`schedule`** — the cron, not a dispatch |
| Branch / SHA | `master` / `e92a7e7d8a07c3e61fdaebea92d2b6eb7ed4c924` |
| Created → completed | 2026-09-24T08:02:04Z → 2026-09-24T08:13:20Z |
| Conclusion | **success** |

### PostgreSQL

| | |
| --- | --- |
| Integration job | `107546480646`, *Integration tests (PostgreSQL 18.6)* — **success** |
| Service image | `postgres:18.6` |
| Client provisioning step | success; `postgresql-client-18` 18.6 installed |
| Tool output | `pg_dump (PostgreSQL) 18.6` · `pg_restore (PostgreSQL) 18.6` |
| Integration suite | **949 passed, 0 failed, 0 skipped, 949 total** |

### The backup and restore drill — proved by inference, not by name

The runner output reports the integration suite as a total and does not name
`BackupRestoreDrillTests` individually. Their success is **inferred** from the
suite:

- the three drill tests are in the integration assembly
  (`tests/AgencyOS.Tests.Integration/Operations/BackupRestoreDrillTests.cs`);
- none carries a `Skip`, and the run skipped nothing;
- they fail, rather than skip, when the client is older than the server — which
  is exactly what made the historical nights red.

So a 949/949 run with nothing skipped includes them passing. No log line in this
run names them, and this record does not claim one does.

### Windows

| | |
| --- | --- |
| Job | `107548071015`, *Nightly artifact (Windows)* — **success**, no longer skipped |
| Steps | *Produce nightly artifact* — success · *Upload artifact* — success |
| Artifact | `agencyos-nightly-20260924.52`, id `10797197858` |
| Built from | `e92a7e7d8a07c3e61fdaebea92d2b6eb7ed4c924` |
| Digest | `sha256:edec68ef207ba6f10c0e9c80b30124ea1e29e1a396ea5b8806adbb8858284571` |
| State at capture | not expired |

## 6. Environment authority

No environment or database was changed by this closure. These are dispositions.

1. **PostgreSQL 18.6 is the authoritative ALPHA validation baseline.**
2. **Local PostgreSQL 19 beta is LAB corroboration only**, never authoritative
   ALPHA evidence.
3. **A local machine with an older default `pg_dump` is not a feature-freeze
   blocker.** Authoritative CI and the default-branch Nightly must provision the
   matching PostgreSQL 18 client, and now demonstrably do.
4. **Historical LAB state carrying contract 14 is non-authoritative** for ALPHA
   and must not be used as release evidence. It is treated as retired unless a
   later explicit decision reprovisions it.
5. **The canonical Windows provisioning platform is `windows-x64`.** A
   provisioning attempt refused because it used a generic `Windows` identifier is
   an environment/provisioning error, not a product failure.

## 7. Disposition

**C11 = PROVED**, by scheduled Nightly `35972891307`. The historical red
default-branch signal is superseded for this criterion, not erased.

**Validation infrastructure = CLOSED.**

This commit moves `master` past `e92a7e7`. That does not reopen C11: the proof is
anchored to `e92a7e7`, whose executable parent is build 95, and everything after
it is documentation. Requiring a fresh scheduled run for every documentation
commit would be a proof that can never finish.

## 8. What remains

**Feature freeze remains ACTIVE.** Validation infrastructure closing is not
feature-freeze exit.

| Criterion | Status |
| --- | --- |
| C3 — every displayed value is announced | **Known unsatisfied residual**: the representation-scope row shows its effective-from date and the announcement omits it. Not a new F-ID. |
| C7 — core workflows without developer knowledge | **Not yet proved**: a final-candidate whole-core-journey proof is still required |
| C9 — operational regression gate | **Not yet proved**: designed, not implemented |
| C10 — genuinely blind takeover | **Not yet proved** |
| C12 — residual limitations accepted | **Owner acceptance required** |
| C14 — every capability routed or excepted | **Owner acceptance required** |

C1, C2, C4, C5, C6, C8, C13, C15 and C16 are proved; C11 is proved here.

All seventeen Reality Closure F-ID entries remain terminal; F-08 is folded into
F-02, so they are sixteen independent root mechanisms. The product-hypothesis
verdict remains **THESIS PARTIALLY DEMONSTRATED** and was not reassessed.

## 9. Next permitted stage

**OPERATIONAL REGRESSION GATE** (C9).
