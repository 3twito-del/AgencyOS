# AgencyOS ALPHA 0.1.0, build 96

The operational regression gate is closed: C3, C7 and C9 are proved on the exact
candidate, and this is the build the genuine blind takeover will receive.

**Date:** 2026-09-28

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **96** |
| Executable commit | **`691e32fc545a13d28fd40b1fd6c6671dcd2e55ac`** |
| Tag | **`alpha-691e32f`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 95, `alpha-901ba19` |

**No contract change, no migration, no new endpoint and no domain change.** Every
repair on this line is in the Windows client or its presentation layer.

- `AgencyOS.Windows.exe` SHA-256:
  `25e2e693a80e927328fbddcaf1542a82b173603f4334f0fa1c74541bad619691`. It matches
  the build-96 manifest.
- The manifest: 111 artifacts, every hash verified.

## 2. The executable commit is not the branch tip

```
git diff --stat 691e32fc545a13d28fd40b1fd6c6671dcd2e55ac..operational-regression-gate
```

Every path it reports should be under `docs/`. This record is that child.

## 3. What the operational regression gate repaired

This is a summary. The repair and correction chain, with its reproductions, negative
controls and every stopped release candidate, is in
`docs/reviews/operational-regression-gate/PRODUCT-REPAIR-EVIDENCE.md`.

- **D4/D5 (row truth under the length budget).** Every operational row keeps its
  primary facts whole. A long value yields, and is offered whole on the same row.
- **Decision C.** Representation and the talent profile stay separate. After signing,
  the page says a talent profile is still needed and offers to create one; the
  profile is created only when the operator chooses. Its state is shown truthfully:
  not checked, checking, absent, present or unavailable.
- **Pipeline.** A new pursuit is a Draft. After creation the list widens and selects
  it automatically. "Activate opportunity" makes it Active, which is what market
  activity requires. An accepted activation whose refresh fails is reported as
  exactly that. A missed reveal is not called a load failure.
- **The final release-candidate findings.**
  - Long contract rows on the Contracts and Projects lists keep counterparty, status
    and (where shown) kind whole; the title yields and is offered whole on the row.
  - A payment's first amount now says "amount", beside "allocated" and "unapplied".

## 4. Validation

**Authoritative CI.** Run `36146914412` (#107, `workflow_dispatch`) on exactly
`691e32f`: success.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 4,120 |
| Windows | 1,832 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6; `pg_dump` and `pg_restore` 18.6) | 952 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas, unchanged |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned, verified |

Every count had 0 failed and 0 skipped.

**Canonical Nightly.** Run `36349771984` (#56, `workflow_dispatch`) on exactly
`691e32f`: success.

- **Integration:** 952 passed, 0 failed, 0 skipped, on server `postgres:18.6`, with
  `postgresql-client-18` 18.6.
- **Nightly artifact:** `agencyos-nightly-20260927.56`. It is nightly-channel, not
  this build.

## 5. Final-candidate Windows release candidate

The real-Windows release candidate ran on exactly `691e32f`:
- the shipped executable above, alpha build 107 metadata, the same binary SHA-256;
- an isolated synthetic tenant.

It passed and was accepted by Control Room. On the operator's own Windows desktop it proved:

- Decision C;
- the original C3 scope row;
- the automatic post-create reveal;
- explicit activation;
- the complete five-step C7 story, as one synthetic commercial story around the same
  newly represented client:
  - represent;
  - pursue and negotiate;
  - paper and execute;
  - originate and collect money;
  - know what to do next.

Accessibility was proved with Narrator's own captured speech:
- **A:** an ordinary row.
- **B:** complete Notes.
- **C:** a complete ContractTitle.
- **D:** the complete external reference, and the three money roles.
- **E:** complete detailed notes.
- **Contracts row:** the long-title Contracts row with its counterparty, status and
  kind, and the complete title on the same row.

**An environmental interruption.** Windows locked the session during C7 step 4.
- Nothing was written while it was locked.
- A person physically unlocked the machine two days later, and the run resumed.
- A durable continuity audit showed that both segments used the same binary, API,
  tenant, database and story, and that every business write came from the Windows
  client.
- No product failure was established.

## 6. Status

| | |
| --- | --- |
| C3 | **PROVED** |
| C7 | **PROVED** |
| C9 | **PROVED** |
| M1, operational regression gate | **COMPLETE** |
| C10, genuine blind takeover | **NOT YET PROVED**; it has not started |
| C11 | PROVED; Validation Infrastructure CLOSED |
| C12, C14 | OWNER ACCEPTANCE REQUIRED |
| Feature freeze | ACTIVE |
| Product-hypothesis verdict | **THESIS PARTIALLY DEMONSTRATED**, unchanged |

Build 96 is the candidate for the genuine blind takeover. It does not demonstrate
the thesis by itself, and nothing here exits the feature freeze.
