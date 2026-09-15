# Audit 002 Phase D — final coverage and the §30 verdict

Phase D was a closure pass. It settled the eight reachable dialogs that had never
opened and touched nothing else.

**Product baseline:** `6e9b66f`, unchanged. `git diff 6e9b66f -- src/` is empty.

---

## 1. The final partition (§17)

Every dialog appears exactly once.

```
TOTAL_DIALOGS                     63
├── UNREACHABLE                    4   nothing constructs them
└── REACHABLE                     59
    ├── OPENED_AUTOMATICALLY      51
    ├── OPENED_MANUALLY            0   no human operator was available
    ├── NEVER_OPENED_PRODUCT_DEFECT        4
    ├── NEVER_OPENED_PRECONDITION          3
    ├── NEVER_OPENED_HARNESS_LIMITATION    1
    └── NEVER_OPENED_INCONCLUSIVE          0
```

51 + 0 + 4 + 3 + 1 + 0 = **59**. 59 + 4 = **63**.

**Reachability did not change from 59.** All eight were re-validated against a
real operator path and all eight kept it; none was moved out of the denominator.

### Nothing remains inconclusive

Phase C left three. Phase D resolved all three into
`NEVER_OPENED_PRODUCT_DEFECT`, on evidence set out in
[`AUDIT-002D-UNOPENED-DIALOGS.md`](AUDIT-002D-UNOPENED-DIALOGS.md).

---

## 2. Cancel/close coverage

| | |
| --- | ---: |
| Reachable dialogs opened | **51** |
| Of those, Escape observed closing them | **51** |
| Of those, focus restored to the opener | **51** |

Cancel/close is observed for **every dialog that was opened** and for none of the
eight that were not — which is the whole of the shortfall.

---

## 3. The original §30 gate

| # | Requirement | Status |
| ---: | --- | --- |
| 1 | All dialogs inventoried | **MET** — 63 of 63 |
| 2 | Every reachable dialog opened at least once | **NOT MET** — 51 of 59 |
| 3 | Cancel/close behaviour observed | **NOT MET** — 51 of 59 |
| 4 | Accessibility/focus inspection completed | **NOT MET** — 51 of 59 |
| 5 | Representative mutation for every major domain | **MET** — 11 of 11 |
| 6 | Multi-role evidence completed | **MET** |
| 7 | Role/auth UX completed | **MET** |
| 8 | Validation association adequately characterised | **MET** |
| 9 | `AOS-R001-006` finalised | **MET** |
| 10 | `AOS-R001-010` finalised | **MET** |
| 11 | `AOS-R001-013` refined | **MET** |
| 12 | `AOS-R001-020` refined | **MET** — closed as `NO_DEFECT` |
| 13 | Idempotency sampled | **MET** |
| 14 | Stale conflict sampled | **MET** |

**Eleven of fourteen met**, and the three unmet are one gap counted three times.

Phase C reported *"twelve of fourteen met, two unmet"* while its own table marked
requirements 2, 3 **and** 4 as NOT MET. That was an arithmetic slip; the correct
figure then was eleven of fourteen, and it is unchanged now. Phase C's documents
are left as issued and the correction is recorded in the aggregate report.

Requirements 3 and 4 are satisfied for every dialog that was opened; they fail
only because 2 does.

---

## 4. Why the gap did not close, stated plainly

Phase D improved the *explanation* of the eight without opening any of them.

- **Four are now a product finding.** `AOS-R002-019`: the opener is invoked, the
  handler's guards are satisfied, and nothing appears and nothing is said. These
  cannot be opened because the product does not open them. Per §19 the finding is
  filed and Audit 002 stays open, because the reachable dialog still has not
  opened.
- **Three cannot have their precondition built.** No route creates an inbound
  message; no route creates a pending AI approval without running an AI action
  that requests one. Building either by writing to PostgreSQL directly is
  forbidden by §3, and rightly.
- **One is the reviewer's fault.** `RecordSignatureDialog`'s precondition was
  created and verified — outstanding signatories went 0 → 1 through
  `POST contracts/{id}/parties` — and `SignatureButton` is present in the tree on
  every tab and offscreen on every tab. The pass does not scroll a detail pane
  into view. That is a harness limitation and it is not converted into a product
  failure.

Level 3 of the attempt hierarchy — a human operator opening the dialog through
the real UI — was not available, because the reviewer is the only interface
driving this machine. That is §19 case C, and it is why one of the eight stays
open on tooling rather than on the product.

---

## 5. What Phase D changed in the reviewer

Three corrections, each narrowly understood before it was made, each with a
deterministic test, and each re-run only against the dialogs it affected.

| # | Correction | Test |
| ---: | --- | --- |
| 24 | Palette confirmation matched the *label* with `Contains`, which cannot tell `Connect mailbox` from **Dis**`connect mailbox` | `MatchingOnTheIdentifierCan` |
| 25 | The guard short-circuit suppressed *button* openers when the guard list had been over-captured from a neighbouring method | re-run of the five button-opened dialogs |
| 26 | The opener's own reason was replaced with a generic "ran but nothing appeared" | the evidence in `AOS-R002-019` is that reason |

Defect 26 is the one that made the product finding possible. "Ran but nothing
appeared" is the same sentence whether a button was disabled, a button was
missing and the palette was used instead, or the command genuinely produced
nothing — three different findings collapsed into one non-answer.

## Evidence

`artifacts/reviewer/run-002-phase-d/` — `detail/`, `mailbox-probe/`, `buttons/`,
`signature/`, `palette-fixed/`, `logs/`, `coverage.json`.

**LAB evidence.** Produced by driving the real application on a developer
machine. Not hosted-CI evidence and not labelled as such.
