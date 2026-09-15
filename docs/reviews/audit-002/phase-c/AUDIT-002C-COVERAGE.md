# Audit 002 Phase C — coverage and the §30 verdict

Recomputed from evidence rather than carried forward. Every number here is
derived from the run files in `artifacts/reviewer/run-002-phase-c/`, and no
dialog appears in more than one category.

**Product baseline:** `6e9b66f`, unchanged. `git diff 6e9b66f -- src/` is empty.

---

## 1. Dialogs, finally counted

| | |
| --- | ---: |
| `TOTAL_DIALOGS` | **63** |
| `REACHABLE` | **59** |
| `OPENED_AUTOMATICALLY` | **51** |
| `OPENED_MANUALLY` | **0** |
| `OPENED_TOTAL` | **51** |
| `UNREACHABLE` (nothing constructs them) | **4** |
| `HARNESS_LIMITATION` | **2** |
| `PRODUCT_OPENER_DEFECT` | **0** |
| `BLOCKED_BY_PRECONDITION` | **3** |
| `INCONCLUSIVE` | **3** |
| `NOT_ACTUALLY_REACHABLE` | **0** |

### Why 63 and not 61

Audit 001 and Phase A inventoried **61**. Repair Wave 003E-A added
`AddMemberDialog` and `ChangeMemberRoleDialog`, which is the whole change. Both
were opened in this phase.

### The eight that did not open

No dialog is left as "blocked". Each carries the cause read from its handler and
from the server.

| Dialog | Classification | Why |
| --- | --- | --- |
| `ApproveAiActionDialog` | `PRECONDITION_NOT_MET` | `PolicyList` is on the Policy tab and empty. The API reports **0** AI policies and **0** approvals in this tenant. |
| `ConnectMailboxDialog` | `PRECONDITION_NOT_MET` | The guard is `providers.Count > 0`. No mail provider is registered, and **the product deliberately shows a notice instead**: *"No provider is configured. An administrator has to register the application with the mail provider before a mailbox can be connected."* |
| `RecordSignatureDialog` | `PRECONDITION_NOT_MET` | The guard is `OutstandingSignatories.Count > 0`. **Phase B's own mutations** recorded both signatures and took the contract to `Executed`, so the product answers *"Every required signature is already recorded."* |
| `IngestAttachmentDialog` | `HARNESS_LIMITATION` | `AttachmentList` lives on an inner tab inside a selected message. The walk reported `AttachmentList: not on the page` on all four outer tabs, not having expanded the inner tabs on that run. |
| `ResolveParticipantDialog` | `HARNESS_LIMITATION` | `ParticipantList`, same shape. |
| `CreatePredictionDialog` | `INCONCLUSIVE` | The guard is satisfied on page load. On the Predictions tab the palette ran the command and no dialog appeared. |
| `ResolvePredictionDialog` | `INCONCLUSIVE` | The guard was satisfied on both Predictions and Watchlists; the command ran on each and nothing appeared. |
| `RecordSourceDialog` | `INCONCLUSIVE` | The handler guards on nothing a tab can change. The palette ran the command on **all nine** Intelligence tabs and nothing appeared, and the independent tab probe agrees. |

**Three `PRECONDITION_NOT_MET` are not defects.** In all three the product
refuses deliberately and says why — two of them in sentences better than most of
the system's other refusals. A dialog that will not open because the thing it
operates on does not exist is the system working.

### Why the three `INCONCLUSIVE` are not filed as product defects

They are the strongest candidates for `PRODUCT_OPENER_DEFECT` and they are not
filed as one, because the evidence does not reach that far.

What is known: nine other palette commands on `IntelligencePage` — `thesis.revise`,
`signal.record`, `signal.verification`, `thesis.create`, `thesis.retire`,
`watchlist.create`, `research.open`, `prediction.forecast`, `radar.add` — do open
their dialogs, so palette delivery to that page demonstrably works. That
asymmetry is why these deserve a targeted follow-up.

What is not known: whether the command reached the handler at all. Distinguishing
"the palette did not deliver it" from "the handler ran and the dialog did not
show" needs either instrumentation of the product, which §23 forbids, or a human
at the keyboard, which the reviewer currently occupies.

Per §19 this is case **C**: the harness is the only remaining blocker and the
reviewer is the only interface controlling the machine. It is recorded as a
limitation, not converted into a product failure.

---

## 2. Behaviour of the 51 opened

From the latest reading of each, after reviewer defect 23 was corrected.

| | |
| --- | ---: |
| Focus entered the dialog | **51 / 51** |
| Tab stayed inside | **51 / 51** |
| Shift+Tab stayed inside | **51 / 51** |
| Escape closed it | **51 / 51** |
| Focus restored to the opener | **51 / 51** |
| Accessibility detector hits | **0** |
| Row-speech detector hits | **0** |

The two zeroes are trusted only because the detectors behind them are held to
positive controls — `DetectorControlTests` runs them against 26 known-bad
controls, and they fire. Audit 001R exists because that was once not true.

**Unexpected-second-modal detection remains `NOT_MEASURABLE_WITH_CURRENT_UIA_MODEL`.**
WinUI produces two popup objects per dialog and neither carries a `ContentDialog`
class name, so counting them measures the framework rather than the product. It
is recorded as unmeasurable, not as zero.

---

## 3. The original §30 gate

| # | Requirement | Status |
| ---: | --- | --- |
| 1 | All dialogs inventoried | **MET** — 63 of 63 |
| 2 | Every reachable dialog opened at least once | **NOT MET** — 51 of 59 |
| 3 | Cancel/close behaviour observed | **NOT MET** — 51 of 59 (met for every dialog opened) |
| 4 | Accessibility/focus inspection completed | **NOT MET** — 51 of 59 (met for every dialog opened) |
| 5 | Representative mutation for every major domain | **MET** — 11 of 11 |
| 6 | Multi-role evidence completed | **MET** — five personas, four distinct authority shapes |
| 7 | Role/auth UX completed | **MET** — ADR-0038's four cases each observed |
| 8 | Validation association adequately characterised | **MET** — structural, runtime and manual-gate scopes stated separately |
| 9 | `AOS-R001-006` finalised | **MET** — 20 fields, 13 dialogs, per-field table |
| 10 | `AOS-R001-010` finalised | **MET** — `CONFIRMED_API_UI_GAP` for scopes/team |
| 11 | `AOS-R001-013` refined | **MET** |
| 12 | `AOS-R001-020` refined | **MET** — and now closed outright |
| 13 | Idempotency sampled | **MET** — five domain shapes |
| 14 | Stale conflict sampled | **MET** — three aggregates |

**Twelve of fourteen met. Two unmet, and they are the same gap counted twice:
eight reachable dialogs have never been opened.**

Requirements 3 and 4 are satisfied for every dialog that was opened; they fail
only because requirement 2 does. Nothing is waived.

---

## 4. What this phase cost and what it bought

Phase A opened 30 of 57. Phase B opened 38. Phase C opened **51 of 59**, on an
inventory that grew by two.

Almost all of that came from correcting the reviewer rather than from doing
anything new to the product: the two-level tab walk, the tab strip no longer
being mistaken for a business list, guards being read from handlers that explain
themselves, and the tab walk running for palette openers at all.

The same corrections closed two filed findings — `AOS-R002-004` and
`AOS-R002-009` — as harness errors.

## Evidence

`artifacts/reviewer/run-002-phase-c/` — `final/`, `last-eight/`,
`focus-recheck/`, `sync/`, `coverage.json`, and per-dialog trees and captures
under `evidence/`.

**This is LAB evidence.** It was produced by driving the real application on a
developer machine. It is not hosted-CI evidence and is not labelled as such.
