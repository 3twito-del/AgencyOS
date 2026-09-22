# AgencyOS ALPHA 0.1.0, build 90

Reality Closure wave 7: a row says how much, in both channels.

**Date:** 2026-09-23

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **90** |
| Executable commit | **`6d46dfab5e456f9cb58fa33f69d909ed2e37fd53`** |
| Tag | **`alpha-6d46dfa`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 89, `alpha-01c41ea` |

**No contract change, no migration, no new endpoint and no domain change.** One
formatter, one converter, one page's markup, one resource registration, one review
harness verb, two tests added.

## 2. The executable commit is not the branch tip

```
git diff --stat 6d46dfab5e456f9cb58fa33f69d909ed2e37fd53..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

Two findings, one loss, in the two channels a row speaks through.

**F-11 — the visible channel.** Every Finance row bound `Something.Amount`: the raw
decimal out of `MoneyResponse`, discarding `Currency`. Reality Closure recorded it
live — a receivable row reading `240000.0000 | 90000.0000 | 150000.0000` two lines
beneath a summary reading `Outstanding: 140,000.00 GBP   1,255,000.00 USD`. On a
screen holding two currencies there was nothing in the row to say which one it was
in. The product states the rule itself, in `RecordOfferDialog`: *"Money always
carries its currency; nothing here accepts a bare number."* The dialogs obeyed it;
the rows did not.

**F-02 — the announced channel.** `RowLabel` selected from three vocabularies —
names, states, related names — and had no entry for a value of any kind. Every
quantitative row announced *what it is* and *what state it is in*, and never *how
much*. A screen-reader operator could scan the agreed terms of a negotiation and
hear `Fee`, `Term`, `Territory` without a single number.

## 4. Normalizing the list moved both counts

The register recorded F-11 as **three bindings**. It is **seventeen**, across seven
lists on one page; the three were the receivable row alone.

**Three of those seventeen are not the defect and were deliberately left alone.**
The ledger's account rows carry a `Currency` column of their own, so all three of
their figures are already denominated, and repeating the code on each would be
noise rather than repair. A test records the decision.

Two entries in the accessibility set are reclassified rather than repaired.
`ObligationResponse` carries no monetary field at all, so description, kind and
status is complete for the type. `AuditEventResponse` is bound to no operator list
— it exists only on the API side, and the history surfaces bind purpose-built
history types.

## 5. Terms needed no formatter of their own

Both term types already carry `DisplayValue` — the value written once, on the
server, by whatever rule the term's kind demands — and the visible column binds
exactly that field. Announcing the same field gives the two channels one answer by
construction rather than by a second formatter that could disagree, and money,
percentages, dates, counts and text all arrive correct without the formatter
knowing about any of them.

Money is matched **by type**, not by name, so an unrelated decimal called `Amount`
can never be read out as a sum. Each figure keeps the name of the field it came
from: a row announcing three unlabelled sums tells an operator how much of
something without saying of what.

**An amount nobody recorded stays unannounced.** A contingent bonus nobody can
value yet is not worth nothing, and saying `0.00 USD` would put a number in the
operator's head that no one wrote down — the rule F-01 established, applied to a
figure. A *recorded* zero is a business result and is announced.

## 6. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35791595544` and canonical Nightly run `35791614826`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 4,003 |
| Windows | 1,336 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 920 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas — unchanged |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

Live verification used the adversarial case the census recorded: two receivables,
**identical amounts, different currencies**, on one screen.

| Channel | USD row | GBP row |
| --- | --- | --- |
| Visible | `240,000.00 USD \| 0.00 USD \| 240,000.00 USD` | `240,000.00 GBP \| 0.00 GBP \| 240,000.00 GBP` |
| Announced | `KR-USD, 240,000.00 USD original amount, … 240,000.00 USD outstanding, Open` | `KR-GBP, 240,000.00 GBP original amount, … 240,000.00 GBP outstanding, Open` |

Before this build both rows read `240000.0000` and announced `«Reference»,
«Status»`. Terms were confirmed live across three value kinds — `Fee,
185,000.00 USD`, `Backend points, 10%`, `Start date, 2027-03-01` — and the
representation scope rows, which every one of which used to announce the identical
words `Representation scope`, now announce `Television` and `Film`.

**A limitation the census recorded is gone.** Phase I could not confirm term or
money rows live because those lists are `SelectionMode="None"` and an announced
name was only readable through selection. Rather than change a correct product
control to suit the harness, the harness gained a verb that asks the accessibility
tree directly. The accessibility channel is now first-class evidence.

Full detail, including what remains recorded rather than closed, is in
`artifacts/operational-alpha/reality-closure/WAVE-07-BUSINESS-VALUE-ACCESSIBILITY-PARITY.md`.

## 7. Verdict

**F-02, F-08 and F-11 are closed.** No in-scope instance remains open, and no
sibling finding is marked resolved — the identity-role findings F-03, F-04 and
F-09 are untouched, because this wave was value semantics only.

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build 90.
