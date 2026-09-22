# AgencyOS ALPHA 0.1.0, build 84

Reality Closure wave 1: Finance no longer states a total it could not count.

**Date:** 2026-09-22

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **84** |
| Executable commit | **`905b1fa739fed1422aca670248ff8fb98fcc512d`** |
| Tag | **`alpha-905b1fa`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 83, `alpha-7397378` |

**No contract change and no migration.** Nothing under `src/AgencyOS.Contracts/`,
`src/AgencyOS.Api/`, `src/AgencyOS.Domain/`, `src/AgencyOS.Application/` or
`src/AgencyOS.Infrastructure/` was touched. Two client files changed, one added,
two test files added.

## 2. The executable commit is not the branch tip

```
git diff --stat 905b1fa739fed1422aca670248ff8fb98fcc512d..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

The Reality Closure census recorded one CRITICAL finding: **F-01**, operator-facing
summaries stating authoritative totals over populations that had not loaded. Six of
the twenty-seven sites were money, and those six are this build.

With the server unreachable, build 83 showed this:

```
[error]  That did not happen
         Cannot reach the AgencyOS server …
         0 receivable(s), 0 overdue. Outstanding: . Unapplied: .
```

A confident financial zero asserted from an empty local list, printed directly
beneath the bar saying the load had failed. An operator reading it concludes a
client owes nothing and does not chase a real debt.

### The repair used what was already there

`ViewModelBase` keeps loading, error and empty apart and says in its own comment
why: the three look identical to a binding, and somebody who cannot tell them
apart will assume the worst one. The five Finance list view models even kept a
private `_loaded` flag, which their empty notices used correctly.

Only the totals ignored all of it.

`SummaryAuthority` now answers with the sentence, or with `Still loading.` while a
load is in flight, or `Totals unavailable.` when it failed or never happened — and
it does not compute the total at all in those cases, so no figure exists to leak.

The six: the page-wide line, receivables, invoices, payments, commissions and the
ledger. The page-wide line spans receivables **and** payments and waits for both:
a balance built from loaded receivables and failed payments is as wrong as one
built from neither.

### One sibling control came with it

Writing the whole-surface test found a case the wave had not anticipated. A book
that loaded **empty** and then failed to refresh still opened *"no receivables"*
beside the error bar, because `IsEmpty` is `_loaded && Count == 0` and stays true
through a later failure. The same assertion, from the same missing question, one
control along. It now waits on the same authority.

## 4. What did not change

**Rows that survive a failed refresh stay on screen**, as they did before. They are
last known rather than current, and the total no longer restates them as though it
had just counted them. Changing what the list shows was out of scope.

**The twenty-one remaining F-01 sites are untouched** — four HIGH overdue counts
and seventeen MEDIUM counts across eleven other pages. The mechanism looks
applicable, and applying it needs a per-page check that each view model tracks a
loaded state and that its empty notice is gated the same way. That is a later,
bounded decision.

## 5. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35734826543` and canonical Nightly run `35734841378`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 3,936 |
| Windows | 1,094 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 920 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

Live verification drove the real Windows client through all four states: the server
down, with every one of the six reading `Totals unavailable.` and no zero anywhere;
a successful load carrying counts and currencies with GBP and USD side by side and
never added; commissions stating an honest `0 entitlement(s)` because that load
succeeded; and a refresh after the server returned restoring the real figures in
the same session.

## 6. Verdict

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build 84.
