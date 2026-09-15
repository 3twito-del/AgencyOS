# Audit 002 — narrow closure recheck after Repair Wave 003A

§18. **This is not a new audit phase.** Four dialog rows are rechecked because a
product repair changed them, and nothing else was re-run, re-measured or
re-opened. Audit 002's phases, findings, matrices, reviewer-defect ledger and
supersession records stand exactly as issued.

---

## What was rechecked

The four `PRODUCT_OPENER_DEFECT` rows, through the audit's own `dialog-runtime`
pass and through this wave's `opener-probe`, on the repaired client against the
same synthetic tenant Phase D used.

| Dialog | Phase D | After 003A | Evidence |
| --- | --- | --- | --- |
| `ConnectMailboxDialog` | `NEVER_OPENED_PRODUCT_DEFECT` | **`OPENED`** | `dialog-runtime-after.json`, `opener-probe-after.json` |
| `CreatePredictionDialog` | `NEVER_OPENED_PRODUCT_DEFECT` | **`OPENED`** | same |
| `RecordSourceDialog` | `NEVER_OPENED_PRODUCT_DEFECT` | **`OPENED`** | same |
| `ResolvePredictionDialog` | `NEVER_OPENED_PRODUCT_DEFECT` | **`OPENED`** | same |

All four report focus entering the dialog, Escape closing it, and zero
accessibility observations — which are the same three §30 gates, so all three move
together.

## Coverage

| | Phase D | After 003A |
| --- | ---: | ---: |
| Dialogs total | 63 | 63 |
| Reachable | 59 | 59 |
| **Opened** | **51** | **55** |
| Unreachable | 4 | 4 |
| Reachable but unopened | 8 | **4** |

The denominator does not move. No dialog was reclassified, none was moved out of
the reachable set, and the four unreachable dialogs
(`AddIntelligenceSubjectDialog`, `CalculateCommissionDialog`,
`RaiseReceivableDialog`, `RecordMonetaryObligationDialog`) are untouched by this
wave.

The reachable-but-unopened partition after the recheck:

| Cause | Phase D | Now |
| --- | ---: | ---: |
| `PRODUCT_OPENER_DEFECT` | 4 | **0** |
| `PRECONDITION_UNACHIEVABLE` | 3 | 3 |
| `HARNESS_LIMITATION` | 1 | 1 |
| `INCONCLUSIVE` | 0 | 0 |

**4 = 0 + 3 + 1.** No remainder.

## Audit 002 remains OPEN

Three §30 gates are still unmet, all of them the same four dialogs:

1. Every reachable dialog opened at least once — **55 of 59**.
2. Cancel/close behaviour observed for every reachable dialog — **55 of 59**.
3. Accessibility/focus inspection for every reachable dialog — **55 of 59**.

The four that remain:

| Dialog | Classification | Why it is not in this wave |
| --- | --- | --- |
| `ApproveAiActionDialog` | `PRECONDITION_UNACHIEVABLE` | No pending AI approval exists and none can be created without running an AI action that requests one. A product-completeness question, not an opener defect. |
| `IngestAttachmentDialog` | `PRECONDITION_UNACHIEVABLE` | No route creates an inbound message. Messages arrive only by mailbox synchronisation, and the one registered provider reports `isConfigured: false`. |
| `ResolveParticipantDialog` | `PRECONDITION_UNACHIEVABLE` | Same missing inbound-message state. |
| `RecordSignatureDialog` | `HARNESS_LIMITATION` | Precondition created and verified; `SignatureButton` is present in the tree and offscreen, and the pass does not scroll a detail pane into view. Not a product defect, and product layout was not changed to accommodate the harness. |

**No §30 closure is claimed.** `AUDIT 002 REMAINS OPEN`.
