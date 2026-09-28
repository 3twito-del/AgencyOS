# AgencyOS ALPHA 0.1.0, build 97

The genuine blind takeover (C10) is closed. The three product failures it exposed are repaired
and proved on the exact commit this build ships.

**Date:** 2026-09-28

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **97** |
| Executable commit | **`b3f41bfd68e81ab42da899671f58e01f0988d3d2`** |
| Tag | **`alpha-b3f41bf`** (annotated `3219b226f709c7dd784c6af52ce4869263a50085`, dereferences to `b3f41bf`) |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned**, the existing ALPHA policy |
| Predecessor | ALPHA 0.1.0 build 96, `alpha-691e32f` (`691e32fc545a13d28fd40b1fd6c6671dcd2e55ac`) |

- `AgencyOS.Windows.exe` SHA-256:
  `80d4e48127cc119ea52e02caae3b443e5ccd2eeb41a563daf36b99d67c9e95de`. It matches the build-97
  manifest. It is also byte-identical to the executable the targeted BF-02 operator proof ran
  (section 4).
- The manifest: 111 artifacts, every hash verified.

**No API, contract, schema, migration, endpoint, domain or permission change.** Every change since
build 96 is in the Windows client or its presentation layer.

## 2. The executable commit is not the branch tip

```
git diff --stat b3f41bfd68e81ab42da899671f58e01f0988d3d2..operational-regression-gate
```

Every path it reports should be under `docs/`. This record is part of that docs-only child.

## 3. What changed since build 96

Three commits, all on `operational-regression-gate`:

| Commit | What |
| --- | --- |
| `0602bae` | Docs only: the build-96 release record. |
| `967e8e4` | The C10 blind-failure repair: BF-01, BF-02 and BF-03. |
| `b3f41bf` | The BF-02 correction: the comparison command says it is read-only. |

- **BF-01, People false empty state.** A search that matches nobody says "No people match this
  search", not "No people yet". Nothing is stated while loading, after a failure or before any
  load.
- **BF-02, reconciliation unreachable.** The draft comparison is offered when the latest draft
  exists and either its terms are visible or the canonical summary reports differences. The
  count of 3 `MissingFromContract` differences was always true and is unchanged. The command is
  now **"Review differences"**, with accessible help and a tooltip saying "Read-only comparison.
  Shows how this draft differs from the agreed terms. Does not change the contract." The
  comparison tab says the same before and after use. The server stays the authority on who may
  read the comparison.
- **BF-03, Activity chronology.** Deal, Pipeline and Contract Activity rows show and announce the
  authoritative `OccurredAt`, in the server's order. A superseded offer's row reads, for example,
  "Answered by offer 2 (outbound)." instead of a raw identifier. The canonical history record is
  unchanged.

The full correction chain, with reproductions and negative controls, is in
`docs/reviews/blind-takeover/C10-BLIND-FAILURE-REPAIR-EVIDENCE.md`.

## 4. Validation

**Authoritative CI.** Run `36404049885` (#109, `workflow_dispatch`) on exactly `b3f41bf`: success.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 4,145 |
| Windows | 1,846 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6; `pg_dump` and `pg_restore` 18.6) | 952 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas, unchanged |
| API contract | 17 |
| TLC | 4/4 (OfflineWriteQueue, OutboundSend, AiApproval, LocalInferenceLease) |
| Release-validation manifest | 111 artifacts, unsigned, verified |

Every count had 0 failed and 0 skipped. The predecessor repair commit `967e8e4` passed CI run
`36371030038` (#108) with Unit 4,144, Windows 1,844, Reviewer 163 and Integration 952.

**Qualifying Nightly.** Run `36427576682` (#58, `.github/workflows/nightly.yml`,
`workflow_dispatch`) on exactly `b3f41bf`: success.

- **Integration:** 952 passed, 0 failed, 0 skipped, on server `postgres:18.6`, with
  `postgresql-client-18` 18.6; `pg_dump` and `pg_restore` 18.6.
- **Windows artifact:** built (0 warnings, 0 errors; Unit 4,145 passed) and uploaded as
  `agencyos-nightly-20260928.58`, artifact `10971953808`. It is nightly-channel, not this build.

**Live proof.** Recorded in section 5 and in
`docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`:

- the original genuine blind takeover on build 96, classified BLIND;
- the affected blind rerun on `967e8e4`, where BF-01 and BF-03 passed live and BF-02 was not
  demonstrated because the command did not say it was read-only;
- the targeted BF-02 operator proof on `b3f41bf`, which passed.

## 5. C10 closure

| | |
| --- | --- |
| BF-01 | **CLOSED** |
| BF-02 | **CLOSED** |
| BF-03 | **CLOSED** |

In the targeted proof, a fresh, isolated operator with no repository access:

- found Solenne Achterberg's executed contract;
- saw, before invoking it, that "Review differences" is a read-only comparison that does not
  change the contract;
- invoked it, and reported the three `MissingFromContract` differences: **140,000.00 GBP**,
  **First position**, **2027-02-01**.

The fixture database write counter was 686 before and after, and all 31 API requests during the
run were GETs. The sealed evidence:

- transcript SHA-256 `4a40d1fbb5f38cb807fab58e50b58eacd6f02da7292fd3fe4d62de566086dcba`;
- evidence manifest SHA-256 `b05f2f23c096195858bc87146bc29448266a29437090a992ecb7a613c16b3e51`;
- terminal evidence package `AgencyOS-b3f41bf-BF02-TERMINAL-EVIDENCE.zip`, SHA-256
  `e1304ecfb353e1c7f6847ea1734595e0cfee339c87547540b05540fd82bbd25d`.

## 6. Status

| | |
| --- | --- |
| C10, genuine blind takeover | **PROVED** |
| C12, residual limitations accepted | **OWNER ACCEPTED** |
| C14, every capability routed or excepted | **OWNER ACCEPTED** |
| C1–C9, C11, C13, C15, C16 | **PROVED** |
| M1, operational regression gate | **COMPLETE** |
| M2, genuine blind takeover | **COMPLETE** |
| M3, owner residual acceptance and feature-freeze exit | **COMPLETE** with this release gate |
| Feature freeze | **EXITED** |
| Product-hypothesis verdict | **THESIS DEMONSTRATED — OPERATIONAL-ALPHA CLOSURE SCOPE** |

C10 PROVED and the C12 and C14 acceptances are Control Room and owner dispositions, recorded here
on the owner's instruction of 2026-09-28. The current-state authority, with every criterion,
decision and accepted residual, is `docs/reviews/AGENCYOS-CANONICAL-CLOSURE-STATE.md`.
