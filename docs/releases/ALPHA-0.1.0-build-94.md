# AgencyOS ALPHA 0.1.0, build 94

Reality Closure wave 11: a row shows the value it exists to show.

**Date:** 2026-09-23

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **94** |
| Executable commit | **`0abd53e6624292206dcd0f270d2cac242fbbf678`** |
| Tag | **`alpha-0abd53e`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 93, `alpha-47d8578` |

**No contract change, no migration, no endpoint, no domain change and no query
change.** One presentation type, one branch in the row announcer, two converters,
three bindings.

## 2. The executable commit is not the branch tip

```
git diff --stat 0abd53e6624292206dcd0f270d2cac242fbbf678..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What the rows said

A prediction row on the Intelligence page, at build 93, for a question owned by
one member and forecast by another:

| Channel | Build 93 |
| --- | --- |
| seen | `Vesper Kestrel casts the Marin lead…` / `Review Owner` / `Open` / `30/12/2026 22:00:00 +00:00` |
| spoken | `Vesper Kestrel casts the Marin lead…, Owner: Review member, Open` |

- **No probability in either channel.** A forecast of 80% was shown as a name.
- **The two channels named different people.** The screen showed the
  *forecaster*, bare, where an operator reads an owner; the announcement named
  the *owner*. Neither said which role the name on screen held.
- **A resolved prediction showed no outcome and no score**, and announced the
  raw token `Yes`. The Brier score was published and rendered nowhere — the
  calibration record invisible on the surface built to hold it (F-14).
- **The deadline was a UTC instant in day/month order** — for a deadline set at
  local midnight on 31 December, `30/12/2026 22:00:00 +00:00` (F-15).

A project with three `Actor` roles showed three identical rows reading
`Actor / Open`; the label that tells them apart was stored, published and
announced, and never shown (F-16).

## 4. What changed

`ForecastLine`, in the client presentation layer, owns the words. The visible
captions and `RowLabel` both read it, so the seen and spoken rows cannot say
different things about the forecast or whose it is.

| | Now |
| --- | --- |
| caption | `Forecast 80% by Review Owner` |
| resolved | `Forecast 80% by Review Owner · Happened, Brier score 0.040` |
| unresolvable | `Forecast 30% by Review Owner · Could not be resolved, not scored` |
| author unresolved | `Forecast 80%, forecaster name unavailable` |
| date | `Resolves by 2026-12-31` |
| role row | `Lead - Marin` / `Actor` / `Open` |

The forecaster is named only as the author of the figure. The owner keeps the
`Owner:` attribution the announcement already gave it and is not added to the
visible row. A forecast is never a bare figure: where the author's name did not
resolve the row says so, in the words a task row uses — the forecast and resolve
dialogs used to return the bare percentage there, which reads as the system's
estimate, and now share the row's words.

The date is the local calendar date, `yyyy-MM-dd`, as a task's due date already
is: the operator picks it from a local date picker, and that is the day they
meant. Two more places wrote the same field wrongly and are included — the
dialogs used the host's short date, and the prediction picker printed the UTC
date.

An open, overdue or cancelled prediction claims no result; its status already
says where it stands.

## 5. A defect the live reading found

The first candidate, `ed5af5b`, was green in CI and Nightly. Read live, its
announcement for a resolved row was
`The Harbour pilot is picked up to series, Owner: Review member,…, Forecast 30%…`
— the statement kept whole and **the status cut**, because it sat on the side of
the row the length budget shortens. The deterministic tests had used a statement
long enough to be cut itself, so they could not see it.

`0abd53e` makes the statement the only part that yields, as a task row's title
already does, and pins the exact live row in a test. That commit is the one
released.

## 6. What deliberately did not change

No value was added to the server; every figure now rendered was already
published. The Brier scale is not repeated per row — the calibration caveat
directly above the list already explains it. The owner was not added to the
visible prediction row. The watchlist's prediction list, which shows statements
only and makes no forecast claim, was not touched.

## 7. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35858271978` and canonical Nightly run `35858275422`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 4,053 |
| Windows | 1,384 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 944 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas — unchanged |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

Integration has nothing new to answer: nothing below the presentation layer
moved.

### Live, against a fixture where owner and forecaster differ

Release client built at this commit, against a synthetic FORGE tenant: three
predictions owned by one member and forecast by another, all resolving at local
midnight on 31 December; one project with three `Actor` roles, two labelled.
Read from the accessibility tree.

| | Build 93 | Build 94 |
| --- | --- | --- |
| open row, seen | `Review Owner` · `30/12/2026 22:00:00 +00:00` | `Forecast 80% by Review Owner` · `Resolves by 2026-12-31` |
| open row, spoken | `…, Owner: Review member, Open` | `…, Owner: Review member, Open, Forecast 80% by Review Owner, Resolves by 2026-12-31` |
| resolved row, spoken | `…, Resolved, Yes` | `…, Resolved, Forecast 80% by Review Owner, Happened, Brier score 0.040, Resolves by 2026-12-31` |
| role rows, seen | `Actor / Open` ×3 | `Lead - Marin`, `Harbourmaster`, and one untitled `Actor` |

Every value the visible row carries is in the announced row, and each of the two
people is named in their own role in both channels.

Not driven live: the Desk's overdue-prediction list, because the API refuses to
create a prediction whose deadline has passed; the forecast and resolve dialogs;
and the prediction picker. Each is covered deterministically.

## 8. Verdict

**F-14, F-15 and F-16 are closed.** Fifteen of the seventeen root findings are
now closed; F-07 (test reality) and F-12 (an unreachable API null guard, LOW)
remain. The API contract has not moved once across the programme.

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build
94.
