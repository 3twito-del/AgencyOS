# AgencyOS ALPHA 0.1.0, build 87

Reality Closure wave 4: an operator can now correct what the product already knew
how to correct.

**Date:** 2026-09-22

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **87** |
| Executable commit | **`01a1fd4d3d18f692b48c9c9fd48046e1278bfaf7`** |
| Tag | **`alpha-01a1fd4`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 86, `alpha-6d32f75` |

**No contract change, no migration, no new endpoint and no domain change.** Two
pages, four new dialogs, one shared lookup extracted, the command registry, two
tests added and two updated. Nothing under `src/AgencyOS.Contracts/`,
`src/AgencyOS.Api/`, `src/AgencyOS.Domain/`, `src/AgencyOS.Application/` or
`src/AgencyOS.Infrastructure/` was touched.

## 2. The executable commit is not the branch tip

```
git diff --stat 01a1fd4d3d18f692b48c9c9fd48046e1278bfaf7..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

Reality Closure recorded **F-13**: capability present in the domain, the API and the
client, with no route a normal Windows operator could take. Twelve capabilities.
Build 86 closed the four that were chain heads. **This build closes the remaining
eight.**

| Where | Acts |
| --- | --- |
| Contract detail, beside the dates line | **Record effective date** |
| Contract → Obligations → *Money owed* | **Quantify** · **Release** · **Calculate commission** |
| Finance → Invoices | **Issue** · **Void** |
| Finance → Reconciliation, beside write-off | **Cancel receivable** |
| Finance → Payments, beside reverse payment | **Reverse allocation** |

None of the eight blocked a chain — that is why they were left until last. The loss
was narrower and worse: **an operator who made a mistake had no way to say so.** A
receivable raised against the wrong instalment stayed on the books. An invoice
billed under the wrong purchase order stayed issued. A payment applied to the wrong
receivable stayed applied. Every correction existed, was audited and was tested, and
none of them was reachable.

**Four reuse dialogs that already existed.** One of those,
`CalculateCommissionDialog`, had been written, styled and opened by nothing since
M9; it now opens from the obligation its own constructor asks for.

## 4. The verbs are the domain's, and deliberately not each other's

Cancelling a receivable is not writing one off. The first says the claim was wrong
and posts nothing; the second says the money was and posts a loss. Reversing an
allocation is not reversing the payment. The first says it answered the wrong
receivable; the second says it never arrived. A single control for either pair would
put the wrong fact in the books, and tests now assert the pairs stay apart.

Nothing here deletes anything, and no control is named as though it might.

**Execution and effectiveness stay separate.** Recording an effective date changes
no status, and the dialog says so. The control is deliberately *not* gated on
execution: a contract in force from January and signed in March is ordinary.

## 5. The refusals still refuse — and now they stay on screen

Issuing an invoice dated a week in the future was refused live:

> **That did not happen** — An invoice cannot have been issued in the future.

and the invoice stayed a draft.

That test also found a defect in this wave's own work, fixed in this build. Every
restored act refreshed as soon as it returned, and the refresh cleared the message
area on its way in — so a refused act showed its explanation and erased it before
anybody could read it. The refresh is now gated on the act going through, and a read
no longer clears what the last act said. The same erasure applied to build 86's four
commands and no longer does.

## 6. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35770427987` and canonical Nightly run `35770452960`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 3,966 |
| Windows | 1,247 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 920 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas — the same counts as build 86 |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

**All eight acts were performed through the real Windows client**, against a fresh
tenant at contract 17: an effective date recorded beside an unchanged execution date;
a contingent bonus quantified at 125,000.00 USD; a second bonus released; commission
calculated at 45,000.00 USD with the act naming where the entitlement lives; a
receivable cancelled with its original amount intact; an invoice issued and then
voided with its issue date preserved; and an allocation reversed, returning
450,000.00 USD to unapplied and its receivable from `Paid` to `Open` without touching
the payment.

Two disclosures are recorded in full in
`artifacts/operational-alpha/reality-closure/WAVE-04-REMAINING-OPERATOR-ROUTES.md`:
the API calls used to build the fixture, none of which is an act this build claims;
and a LAB provisioning fault — a release policy published for platform `Windows`
while the client identifies as `windows-x64` — which is the product refusing
correctly against a tenant provisioned wrongly, not a defect.

That document also corrects a wave-3 disclosure. The reviewer harness **can** drive
a `CalendarDatePicker`: three tab presses from the opened flyout reach the day grid.
Build 86's signature evidence is not retrospectively upgraded — it was not re-run —
but the explanation given for it was more pessimistic than the facts.

## 7. Verdict

**F-13 is closed.** All twelve orphaned operator capabilities named by the census
now have routes, each verified by an observed state transition.

One unrouted action remains in the core authoring matrix and is not one of the
twelve: recording an M8 obligation, which the product can resolve but not create.
It was classified B/D rather than D, no wave has adjudicated it, and F-13 closing
says nothing about it.

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by this
build. It was not re-run, and no blind handoff was performed against build 87.
