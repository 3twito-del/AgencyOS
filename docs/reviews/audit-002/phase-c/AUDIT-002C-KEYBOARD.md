# Audit 002 Phase C — operating a dialog from the keyboard

§3. Phase A measured forward tab traversal, focus entry, Escape and focus
restoration. Two gestures were missing: going *back* a field, and committing
without reaching for the mouse.

---

## A harness error, found by the application closing

The first attempt measured the default action by pressing Shift+Tab twenty times
and then Enter, inside `Inspect`, before the Escape test.

On dialogs where Enter commits, this closed the dialog. The Escape test then
pressed Escape at the shell and recorded `escape-closed=NO` about a dialog that
was no longer there — and on the full pass the keystrokes after it reached the
title bar and closed the application, ending the run at the tenth dialog.

```
Unhandled exception. System.Windows.Automation.ElementNotAvailableException:
The target element corresponds to UI that is no longer available
```

**The Windows client did not crash.** The Application event log records one
crash in the period and it is `AgencyOS.Reviewer.exe`. The client was closed, by
the reviewer, through its own title bar.

Two corrections:

1. **A closed application is now an observation**, recorded against the dialog
   being attempted as `APPLICATION_CLOSED`, and the pass stops rather than
   reporting every later dialog against a window that is not there.
2. **The Enter probe is gone.** A dialog's default action is *declared* — WinUI's
   `ContentDialog.DefaultButton` — so it is read from the markup, which answers
   the question exactly and perturbs nothing.

Every `escape-closed=NO` from the crashed run is an artefact of the probe and
none of them is reported. Re-run without it, the first affected dialog
(`AddCreditDialog`) reports `escape-closed=yes`.

---

## Shift+Tab

Reverse traversal is now measured the same way as forward traversal: fourteen
presses, reading the focused node after each, and checking it against the
dialog's own nodes by identity rather than by position.

Results are in the pass output alongside forward order
(`ReverseTabOrder`, `ReverseTabEscaped`).

---

## Enter — what the markup declares

All 63 dialogs declare a `DefaultButton`. None leaves it unset.

| Declared default | Dialogs | What Enter does |
| --- | ---: | --- |
| `Primary` | **26** | commits |
| `Close` | **36** | cancels |
| `Secondary` | **1** | `ApproveAiActionDialog` — Enter does **not** run the AI action |

`ApproveAiActionDialog` is the one that is clearly right. Its primary is
*"Approve and run"*, and Enter deliberately does not reach it. A dialog that
starts an AI action should not be committable by a stray keypress.

### The 26 and the 36 are not divided by anything

The split does not follow destructiveness, or reversibility, or whether the
dialog creates something. The same verb behaves both ways:

| Enter commits | Enter cancels |
| --- | --- |
| `CreateProjectDialog` | `CreateThesisDialog` |
| `CreateOpportunityDialog` | `CreateWatchlistDialog` |
| `NewPersonDialog` | `OpenResearchCaseDialog` |
| `RecordInteractionDialog` | `RecordSignalDialog` |
| `RecordOfferDialog` | `RecordSourceDialog` |
| `RecordPitchDialog` | `RecordAdjustmentDialog` |

An operator who learns that Enter records an interaction will find that Enter
discards a signal. Neither behaviour is wrong on its own; there is no rule to
learn.

Recorded as `AOS-R002-016` (S3). **No default is proposed.** "Enter always
commits" and "Enter never commits, except where the primary is safe" are both
defensible, and §11's instruction not to design applies equally here.

---

## Evidence

- `artifacts/reviewer/run-002-phase-c/default-buttons.json`
- `artifacts/reviewer/run-002-phase-c/final/dialog-runtime.json`
