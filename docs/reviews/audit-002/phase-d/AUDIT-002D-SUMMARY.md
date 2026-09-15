# Audit 002 Phase D — closure pass

Phase D existed to settle eight reachable dialogs that had never opened, and to
contain the capture problem that filled a disk during Phase C. It did not re-run
any settled section.

**Product baseline:** `6e9b66f`, unchanged. No product code was touched.

---

## What it settled

**The arithmetic.** Phase C's summary explained five of eight. The other three
were `PRECONDITION_NOT_MET` in its coverage document but absent from its summary
list. Reconciled in
[`AUDIT-002D-UNOPENED-DIALOGS.md`](AUDIT-002D-UNOPENED-DIALOGS.md), and two of
those three classifications turned out to be wrong.

**The eight.** All now carry a cause, and the partition is exact:

| Disposition | Count | Dialogs |
| --- | ---: | --- |
| `PRODUCT_OPENER_DEFECT` | **4** | `ConnectMailboxDialog`, `CreatePredictionDialog`, `RecordSourceDialog`, `ResolvePredictionDialog` |
| `PRECONDITION_UNACHIEVABLE` | **3** | `ApproveAiActionDialog`, `IngestAttachmentDialog`, `ResolveParticipantDialog` |
| `HARNESS_LIMITATION` | **1** | `RecordSignatureDialog` |
| `INCONCLUSIVE` | **0** | — |

**A product finding.** `AOS-R002-019` — four dialogs whose opener is invoked,
whose handler guards are satisfied, and where nothing appears and nothing is
said. For `ConnectMailboxDialog` the product's own button was located on the
right tab with the right row selected and clicked, and a separate probe confirmed
the page's notice bar is absent afterwards — so the handler passed its guard and
did not reach its own refusal.

**A second product finding.** `AOS-R002-018` — the command palette announces
`PaletteCommand { Id = go.people, Title = Go to People, … }` rather than "Go to
People". Found while looking for a way to confirm which command the palette had
selected. Page lists are correct; the palette is the exception Repair Wave 002
missed, because the converter sits on the template's inner grid rather than on
the item container.

**The disk.** Measured rather than assumed. The API emits ~96 KB per request at
`Default: Debug` and ~1.4 KB/s idle; a real four-dialog pass over 25 tabs cost
9 MB. **The product did not emit 235 GB** — roughly 99% of that file was not
product output. Detail and containment in
[`AUDIT-002D-STDOUT-INCIDENT.md`](AUDIT-002D-STDOUT-INCIDENT.md). No product
logging finding is filed, and no product logging was changed.

---

## What it did not do

- No settled section was re-run: not the domain mutations, the role matrix, the
  idempotency or conflict suites, the identifier analysis, the representation
  analysis, F9, the dead dialogs, or the full accessibility and focus traversals.
- No dialog was constructed directly, no private constructor called, no internal
  method invoked and counted as coverage.
- No generalized navigation rewrite. Three reviewer corrections, each with the
  mechanism understood first, each with a test, each re-run only against the
  dialogs it affected.
- No manual operator opening, because none was available — the reviewer is the
  only interface driving this machine. Recorded as §19 case C rather than
  simulated.

---

## The verdict

```
AUDIT 002 PHASE D COMPLETE — AUDIT 002 REMAINS OPEN — NO PRODUCT REPAIRS APPLIED
```

**Unmet §30 gates:**

1. Every reachable dialog opened at least once — **51 of 59**.
2. Cancel/close behaviour observed for every reachable dialog — **51 of 59**.
3. Accessibility/focus inspection for every reachable dialog — **51 of 59**.

All three are the same eight dialogs. Gates 2 and 3 are satisfied for every
dialog that was opened.

Four of the eight are now a filed product defect, which is progress of a kind —
the audit knows why they do not open — but §18 is explicit that a reachable
dialog which has never opened keeps the audit open, and four of them have not.

## Documents

| | |
| --- | --- |
| [`AUDIT-002D-UNOPENED-DIALOGS.md`](AUDIT-002D-UNOPENED-DIALOGS.md) | the eight, reconciled and disposed |
| [`AUDIT-002D-DIALOG-CLOSURE.md`](AUDIT-002D-DIALOG-CLOSURE.md) | per-dialog closure evidence |
| [`AUDIT-002D-COVERAGE.md`](AUDIT-002D-COVERAGE.md) | the final partition and the §30 table |
| [`AUDIT-002D-STDOUT-INCIDENT.md`](AUDIT-002D-STDOUT-INCIDENT.md) | attribution and containment |
| [`AUDIT-002D-FINDINGS.json`](AUDIT-002D-FINDINGS.json) | `AOS-R002-018`, `AOS-R002-019` |
| [`../phase-c/AUDIT-002C-REVIEWER-DEFECTS.md`](../phase-c/AUDIT-002C-REVIEWER-DEFECTS.md) | the ledger, extended to 26 |
