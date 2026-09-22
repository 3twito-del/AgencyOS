# AgencyOS ALPHA 0.1.0, build 85

Reality Closure wave 2: nothing is counted late until the slate has been read.

**Date:** 2026-09-22

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **85** |
| Executable commit | **`32ad5e0e18f751af78e52ce885bc649267404617`** |
| Tag | **`alpha-32ad5e0`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 84, `alpha-905b1fa` |

**No contract change and no migration.** Four client view models, four pages, one
test added and one extended. Nothing under `src/AgencyOS.Contracts/`,
`src/AgencyOS.Api/`, `src/AgencyOS.Domain/`, `src/AgencyOS.Application/` or
`src/AgencyOS.Infrastructure/` was touched.

## 2. The executable commit is not the branch tip

```
git diff --stat 32ad5e0e18f751af78e52ce885bc649267404617..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

Build 84 closed the six CRITICAL money instances of F-01. These are the four HIGH
ones: the surfaces that tell an operator how much work is past its date.

With the server unreachable, build 84 said:

| Page | What it said |
| --- | --- |
| Pipeline | `0 pursuit(s); 0 overdue, 0 awaiting a reply.` |
| Contracts | `… 0 with a date within a week.` |
| Deals | `… 0 lapsing within a week.` |
| Communications | `Nothing needs attention.` |

Each built from whatever the local collection held, beneath the bar saying the load
had failed. The cost differs from the money case: a wrong figure can be re-read,
a missed date cannot.

### Three reuse what build 84 introduced

`OpportunityListViewModel`, `ContractListViewModel` and `DealListViewModel` each
already kept a `_loaded` flag that their empty notices used correctly and their
counts ignored. They now publish it through `IAuthoritativePopulation`, and their
sentences go through `SummaryAuthority` unchanged.

### The desk needed its own words

`CommunicationCommandCenterViewModel` keeps no flag — it keeps the projection, so
holding one is what having loaded means. Its claim is also a boolean rather than a
total, and `Totals unavailable.` would answer a question nobody asked. It uses the
same authority test with its own wording:

```
Whether anything needs attention is unavailable.
```

### The empty notices came too

All three count pages had the latent defect build 84 found on Finance: `IsEmpty` is
`_loaded && Count == 0` and stays true after a failed refresh, so the notice would
have opened *"No pursuits match these filters"* beside the error bar. All three now
wait on the same authority.

## 4. What did not change

**The overdue definitions.** Pipeline counts a pursuit whose next action is before
today in UTC; Contracts counts a date within a week; Deals counts an offer lapsing
within a week. This build repairs the authority over the answer, not the meaning of
the question.

Two observations were recorded and deliberately not acted on: the overdue basis is
UTC rather than the operator's local day, and every count describes the filtered
list its page is showing rather than the whole tenant. The second is documented
population semantics, not a defect.

**The seventeen MEDIUM sites**, which remain open. Thirteen look mechanically
compatible; four are materially different; and the Command Center's bare headline
numbers need a presentation decision rather than a gate, because `Totals
unavailable.` does not fit a slot that shows `46`.

## 5. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35742671074` and canonical Nightly run `35742687354`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 3,950 |
| Windows | 1,098 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 920 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

Live verification drove the real Windows client through every state: with the
server down all four declined to answer; filtering Pipeline to a status with no
rows produced an honest `0 pursuit(s); 0 overdue, 0 awaiting a reply.` with the
empty notice, because that load succeeded; a loaded slate read `22 pursuit(s);
1 overdue`; and a refresh after the server returned restored it in the same
session.

## 6. Verdict

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build 85.
