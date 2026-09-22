# AgencyOS ALPHA 0.1.0, build 86

Reality Closure wave 3: an operator can now start the two chains the product
already knew how to finish.

**Date:** 2026-09-22

---

## 1. What this version is

| | |
| --- | --- |
| Version | **0.1.0** |
| Channel | **alpha** (`AgencyOS.Alpha`) |
| Build id | **86** |
| Executable commit | **`6d32f755e6b72c0a3149e868c2cf457e3274e801`** |
| Tag | **`alpha-6d32f75`** |
| API contract version | **17**, unchanged |
| Minimum supported contract | **1**, unchanged |
| Expected schema | `20260909072201_AiResultClassification`, unchanged |
| Signing | **unsigned** |
| Predecessor | ALPHA 0.1.0 build 85, `alpha-32ad5e0` |

**No contract change, no migration, no new endpoint and no domain change.** One
page, one new dialog, one test added and one updated.

## 2. The executable commit is not the branch tip

```
git diff --stat 6d32f755e6b72c0a3149e868c2cf457e3274e801..repair-wave-001
```

Every path it reports should be under `docs/`.

## 3. What was repaired

Reality Closure recorded **F-13**: capability present in the domain, the API and
the client with no route a normal Windows operator could take. It classified the
absence as **UNMET_DEFINITION_OF_DONE** against CLAUDE.md §7 — *"Windows UI
supports the intended workflow"* — and found that both core chains break at their
**first** step.

| Chain | Where it broke |
| --- | --- |
| Contract | a signature is recorded *against a party*, and is refused outside `ApprovedForExecution`. Nothing could add a party or approve a contract, so the signature command had nobody to offer and no contract could ever be executed. |
| Money | a receivable is raised *from a monetary obligation*, and a payment allocates *against a receivable*. Nothing could record the obligation, so the whole delivered finance tail had nothing to attach to. |

Four commands now have routes:

- **Add party** and **Approve for signature** — in the contract command bar,
  beside the signature they unblock.
- **Record money owed** and **Raise receivable** — on the Obligations tab, with a
  list of what the contract obliges, because recording money the operator cannot
  then see would restore only half a chain. That list uses
  `ListMonetaryObligations`, which had no caller either.

**Two of the dialogs already existed.** `RecordMonetaryObligationDialog` and
`RaiseReceivableDialog` were written, styled and finished, and no page opened them.
Their constructors named their intended home — a contract title, a version and the
contract's parties; and a selected obligation — which is where they were put.

**One dialog is new.** It collects a role from the domain's own vocabulary and
names a party the three ways the domain accepts, and decides nothing: whether the
role is legal, whether the contract still takes parties and whether the transition
is allowed all remain on the server.

## 4. The refusals still refuse

The first live attempt to record money owed was made against a contract that was
only approved, and the server refused. The dialog's own notice says why:

> **From the contract, not from the negotiation** — A monetary obligation comes
> from a contract that is executed or in force.

No client-side rule was added to pre-empt it.

## 5. What did not change

**Five core actions remain API-only** and are still unmet against the Definition of
Done: record effective date, issue and void invoice, cancel receivable, reverse
allocation, quantify and release obligation, and calculate commission. None of them
blocks a chain, which is why they were left.

**F-13 is therefore partially closed, not closed.** Both load-bearing chains are
operable end to end by a normal Windows operator; eight D-class capabilities are
not, and calling them delivered would be untrue.

## 6. Validation

Validated against this exact commit on PostgreSQL 18.6 in authoritative CI run
`35751230608` and canonical Nightly run `35751247020`, both green.

| Gate | Result |
| --- | --- |
| Clean build | succeeded, 0 warnings, 0 errors |
| Unit | 3,950 |
| Windows | 1,122 |
| Reviewer | 163 |
| Integration (PostgreSQL 18.6) | 920 |
| OpenAPI | 3.1.1, 264 paths, 188 schemas |
| API contract | 17 |
| TLC | 4/4 |
| Release manifest | 111 artifacts, unsigned |

Live verification drove the real Windows client through a fresh tenant: a contract
created, a version recorded, both parties added and the contract approved through
Windows; those parties signed and the contract executed; money owed recorded and a
receivable raised through Windows; and the receivable arriving in Finance as
`1 receivable(s), 0 overdue. Outstanding: 310,000.00 USD.`

Two disclosures are recorded in full in
`artifacts/operational-alpha/reality-closure/WAVE-03-CORE-AUTHORING-HEADS.md`: the
two signature clicks were made through the API because the reviewer harness cannot
drive a date picker, and the existing LAB tenant could not be used at all because
its release policy was seeded at API contract 14 and refuses a client speaking 17.

## 7. Verdict

The product-hypothesis verdict **THESIS PARTIALLY DEMONSTRATED** is unchanged by
this build. It was not re-run, and no blind handoff was performed against build 86.
