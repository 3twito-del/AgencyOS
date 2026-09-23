# AgencyOS ALPHA 0.1.0, build 95

Reality Closure wave 12: each test says what it proves, and a malformed allocation
is refused as a request.

**Date:** 2026-09-23

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **95** |
| Executable commit | **`901ba19a73c1d2dc48e18c28bcbc01b81cf604d6`** |
| Tag | **`alpha-901ba19`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 94, `alpha-0abd53e` |

**No contract change, no migration, no new endpoint and no domain change.** One
guard in one endpoint; the rest is tests.

## 2. The executable commit is not the branch tip

```
git diff --stat 901ba19a73c1d2dc48e18c28bcbc01b81cf604d6..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. F-12 — a malformed allocation answered 500

`POST /payments/{id}/allocations` with the `allocations` collection absent, or
null, answered `500` with no reason. The endpoint guarded the null on its way
into the handler and then read `request.Allocations.Count` for telemetry on the
way out.

It is now refused **before the handler runs**, in the shape the API already uses
for a missing required field (`AOS-R002-025`):

```
400  Invalid request  —  Allocations is required.
```

Proved against PostgreSQL for both shapes: the payment's version is unchanged,
nothing is allocated, the receivable is untouched, and the well-formed request at
the version the caller already held still applies afterwards. The published
contract already declared the field required — a test now asserts that against
the served document — so the server was wrong and contract 17 does not move.

No Windows workflow sends this request. It was a correctness defect with no
operator path.

## 4. F-07 — tests named for more than they proved

Twenty tests read a source file and were named for what an operator receives:
*never states*, *is reachable*, *offers the same*, *announces*. A string in a file
proves a binding exists, not that the meaning arrives.

| Disposition | Count |
| --- | --- |
| Renamed to what the assertion checks | 10 |
| Renamed, with an existing executed proof cited | 3 |
| Renamed, with a new executed proof added | 4 |
| Restructured into an executed test | 3 |

Where the stronger claim is still one the product makes, it now runs:

- **The Deals page never says a contract both exists and does not**, and never
  says there is none while one is recorded — the real deal view model, every
  deal status, with and without an open offer, against all 256 sets of contract
  statuses. It used a hand-written copy of the deal's state line and eleven
  sampled cases. A control shows the absence check does fire on the one sentence
  that states absence.
- **A call's detailed notes reach the person it was with**, and nobody else —
  the overview the Talent page binds, read from PostgreSQL.
- **The palette offers the representation commands**, and **the authoring menu
  announces titles** — the real palette and the real command registry.

No product code changed for F-07, and none of the new proofs found a product
failure.

## 5. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35875325062` and canonical Nightly run `35875329217`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 4,073 (+20) |
| Windows | 1,384 (one source slice removed, one executed test added) |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 949 (+5) |
| OpenAPI | 3.1.1, 264 paths, 188 schemas — unchanged |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned, verified |

No live Windows proof: nothing an operator sees changed, and the malformed
request is not reachable from the client.

## 6. Recorded, not repaired

- No test exercises the server refusing a stale version on the four
  representation scope and team routes; the existing stale-version test covers
  status transitions only.
- By reading the code, an allocation line with no `amount` would fail the same
  way F-12 did. Outside the canonical findings; untested.
- The next-action banner's wording is still checked through a copy, because it
  writes directly onto a Windows control.

## 7. Verdict

**F-07 and F-12 are closed. All seventeen root findings in the Reality Closure
gap register are now closed**, and the API contract has not moved once across
the programme.

That is a statement about the register, not about the product. The
product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged: it was
not re-run, and no blind handoff was performed against build 95. The operational
regression gate, a blind takeover and the default-branch validation signal remain
open.
