# Repair Wave 003A — runtime evidence, before and after

§17. One instrument, two runs, four surfaces, nothing else touched between them.

**Instrument:** `reviewer opener-probe`, added by this wave as targeted
reproduction support (§14). It drives one named opener at a time — navigate, pick
the tab, select the row where the opener needs one, invoke — and writes a screen
capture and an automation tree from before and after the invocation, then names
what an operator would have seen.

**Environment for both runs**

| | |
| --- | --- |
| Client | `src/AgencyOS.Windows/bin/Debug/net10.0-windows10.0.26100.0/AgencyOS.Windows.exe` |
| API | `http://127.0.0.1:5199`, ring `forge`, Development authentication |
| Tenant | `01a0a015-c44e-7f0e-9c4c-380e118b5e2b` — "Review Agency (synthetic)" |
| Subject | `w2-owner` |
| Database | local PostgreSQL 19beta3, synthetic review tenant. **LAB evidence, not a gate.** |
| Provider | `FakeCommunicationProvider` only. No OAuth, no real mailbox, no send. |

**Run directories** — `artifacts/reviewer/run-repair-003a/`, which Git ignores;
the summaries are mirrored into [`evidence/`](evidence/).

| Run | Directory | Product |
| --- | --- | --- |
| Before | `before/` | product baseline `6e9b66f`, unmodified |
| After | `after/` | this wave's repair |
| Refusal | `after-refusal/` | this wave's repair, precondition deliberately absent |
| Canonical recheck | `after-dialog-runtime/` | this wave's repair, through the audit's own `dialog-runtime` pass |

---

## The four surfaces

### `ConnectMailboxDialog`

| | |
| --- | --- |
| **BEFORE** | Communications → Mailboxes, one row, "Test Mailbox, Review Owner, Error" selected. Clicked the product's own "Connect a mailbox" button. Invoked. **No dialog. No notice. Focus stayed on `ConnectButton`.** |
| **REPRODUCED_BEFORE** | `REPRODUCED` — `INVOKED_NO_OBSERVABLE_OUTCOME` |
| **ROOT_CAUSE** | `VisibilityBox` declares its default selection in markup; the parser raises `SelectionChanged` during `InitializeComponent()`; `Update()` dereferences `_providers`, which the constructor assigns afterwards. `XamlParseException` out of the constructor, discarded by `_ = ConnectMailboxAsync()`. |
| **FIX_APPLIED** | `Update()` returns when `_providers` is null. The constructor calls it again once the dialog is whole. |
| **AFTER** | Same path. **Dialog opened.** Focus landed on `ProviderBox`. Escape closed it, focus returned. |
| **DIALOG_OPENED** | `OPENED` |
| **REFUSAL_PATH_TESTED** | Partly. The "no provider configured" refusal cannot be driven here — `FakeCommunicationProvider` is registered unconditionally, so `providers.Count` is never 0 on this machine. The path is unchanged code and is asserted structurally by `OpenerRefusalTests`. Said rather than implied. |
| **SCREENSHOT** | `after/evidence/opener.ConnectMailboxDialog/01-before.png`, `02-after.png` |
| **UI_TREE** | `after/evidence/opener.ConnectMailboxDialog/01-before.json`, `02-after.json` |
| **LOG_EVIDENCE** | [`evidence/exception-trace-before.log`](evidence/exception-trace-before.log), [`evidence/exception-trace-connectmailbox.log`](evidence/exception-trace-connectmailbox.log) |
| **TEST_EVIDENCE** | `LoadTimeHandlerTests`, `OpenerRefusalTests` |

### `CreatePredictionDialog`

| | |
| --- | --- |
| **BEFORE** | Intelligence → Predictions, 3 rows. Ran `intelligence.prediction.create` from the command palette, which matched and activated. **No dialog. No notice.** |
| **REPRODUCED_BEFORE** | `REPRODUCED` — `INVOKED_NO_OBSERVABLE_OUTCOME` |
| **ROOT_CAUSE** | `ProbabilitySlider Value="50"` raises `ValueChanged` during parse; `ApplyProbability()` writes to `ProbabilityText`, declared four lines later and not yet created. |
| **FIX_APPLIED** | `ApplyProbability()` returns when `ProbabilityText` is null. |
| **AFTER** | **Dialog opened.** Focus landed on `StatementBox`. |
| **DIALOG_OPENED** | `OPENED` |
| **REFUSAL_PATH_TESTED** | No precondition beyond page load — there is no refusal to test, and inventing one would be a product change. Asserted as such by `OpenerRefusalTests`. |
| **SCREENSHOT** | `after/evidence/opener.CreatePredictionDialog/` |
| **UI_TREE** | same directory |
| **LOG_EVIDENCE** | `Failed to assign to property 'RangeBase.Value'. [Line: 37 Position: 97]` |
| **TEST_EVIDENCE** | `LoadTimeHandlerTests`, `OpenerRefusalTests` |

### `RecordSourceDialog`

| | |
| --- | --- |
| **BEFORE** | Intelligence → Sources, 23 rows. Ran `intelligence.source.record` from the palette. **No dialog. No notice.** |
| **REPRODUCED_BEFORE** | `REPRODUCED` — `INVOKED_NO_OBSERVABLE_OUTCOME` |
| **ROOT_CAUSE** | `KindBox`'s default selection raises `SelectionChanged` during parse; `ApplyKind()` reaches `UrlBox`, `CustodyBar` and `PublishedPicker`, all declared below it, and `UpdatePrimary()` reaches `TitleBox` and `UrlBox`. |
| **FIX_APPLIED** | `ApplyKind()` and `UpdatePrimary()` return while those controls are null. |
| **AFTER** | **Dialog opened.** Focus landed on `KindBox`. |
| **DIALOG_OPENED** | `OPENED` |
| **REFUSAL_PATH_TESTED** | No precondition beyond page load. |
| **SCREENSHOT** | `after/evidence/opener.RecordSourceDialog/` |
| **UI_TREE** | same directory |
| **LOG_EVIDENCE** | `[Line: 24 Position: 70]` — the combo box whose default selection fires |
| **TEST_EVIDENCE** | `LoadTimeHandlerTests`, `OpenerRefusalTests` |

### `ResolvePredictionDialog`

| | |
| --- | --- |
| **BEFORE** | Intelligence → Predictions, "Synthetic probe., Review Owner, Open" selected. Ran `intelligence.prediction.resolve` from the palette. **No dialog. No notice.** |
| **REPRODUCED_BEFORE** | `REPRODUCED` — `INVOKED_NO_OBSERVABLE_OUTCOME` |
| **ROOT_CAUSE** | `OutcomeBox`'s default selection raises `SelectionChanged` during parse; `ApplyScoring()` writes to `ScoringBar`, declared below it. |
| **FIX_APPLIED** | `ApplyScoring()` returns while `ScoringBar` is null. Separately, the opener's "no prediction selected" guard now says so instead of returning in silence. |
| **AFTER** | **Dialog opened** with the prediction selected. Focus landed on `OutcomeBox`. |
| **DIALOG_OPENED** | `OPENED` |
| **REFUSAL_PATH_TESTED** | **Yes, at run time.** Ran from the Sources tab with no prediction selected: a dialog titled **"Choose a prediction first"** appeared with the reason, focus landed on its Close button, and `ResolvePredictionDialog` itself did not open — `OutcomeBox`, `ScoringBar` and the title "Resolve this prediction" are all absent from the captured tree. |
| **SCREENSHOT** | `after/evidence/opener.ResolvePredictionDialog/`, `after-refusal/evidence/opener.ResolvePredictionDialog/` |
| **UI_TREE** | same directories |
| **LOG_EVIDENCE** | `[Line: 24 Position: 83]` |
| **TEST_EVIDENCE** | `LoadTimeHandlerTests`, `OpenerRefusalTests` |

---

## The canonical instrument agrees

The audit's own pass, re-run against the four and nothing else:

```
reviewer dialog-runtime --only ConnectMailboxDialog,CreatePredictionDialog,
                               RecordSourceDialog,ResolvePredictionDialog

  OPENED   ConnectMailboxDialog      PALETTE  focus-in=yes escape-closed=yes a11y=0
  OPENED   CreatePredictionDialog    PALETTE  focus-in=yes escape-closed=yes a11y=0
  OPENED   RecordSourceDialog        PALETTE  focus-in=yes escape-closed=yes a11y=0
  OPENED   ResolvePredictionDialog   PALETTE  focus-in=yes escape-closed=yes a11y=0
```

[`evidence/dialog-runtime-after.json`](evidence/dialog-runtime-after.json). Focus
enters every dialog, Escape closes every dialog, and the pass found no
accessibility observation in any of the four.

**One correction was needed to get that answer, and it is a harness change, not a
product one.** On the first attempt the pass reported three of the four as
`DID_NOT_APPEAR` with the detail *"typing 'State a prediction' left 'State a
prediction' (1 shown) at the top"* — it had typed the label, the palette had
narrowed to exactly one result, and the pass then **refused to press Enter**,
because it confirms the row by the bound record's identifier and the row had
announced the plain label instead. The command never ran, and a command that never
ran was being recorded in the same words as a product that did nothing.

The row's accessible name is `AOS-R002-018`'s territory and not this wave's. What
this wave changed is the pass's match: it now also accepts the command's exact
label, **and only when no other command in the registry shares it** — one label is
shared by two commands today, and the rule declines both. `PaletteMatchTests`
holds the controls for it.

---

## What the operator sees now

| Dialog | Before | After |
| --- | --- | --- |
| `ConnectMailboxDialog` | nothing | the dialog, focus in `ProviderBox` |
| `CreatePredictionDialog` | nothing | the dialog, focus in `StatementBox` |
| `RecordSourceDialog` | nothing | the dialog, focus in `KindBox` |
| `ResolvePredictionDialog` (selected) | nothing | the dialog, focus in `OutcomeBox` |
| `ResolvePredictionDialog` (nothing selected) | nothing | **"Choose a prediction first"**, with the reason |
