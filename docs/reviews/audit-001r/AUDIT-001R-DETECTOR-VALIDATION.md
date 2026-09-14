# Audit 001R — detector validation

Before anything was scanned, every corrected detector was shown to fire on a
sample known to be bad and to stay silent on one known to be good. Nothing in
this rebaseline is trusted on the strength of a zero.

The samples are hand-built UI Automation trees, not captures. A detector only
ever exercised against a real window is a detector whose silence nobody can tell
apart from good news, which is precisely how the original defect survived a whole
audit.

They are permanent tests:
`tests/AgencyOS.Tests.Reviewer/DetectorControlTests.cs`.

---

## 1. The defect being corrected

`UiaTree.Patterns` trimmed a UI Automation pattern name with the wrong suffix.
UI Automation reports `InvokePatternIdentifiers.Pattern`; the harness removed
`Pattern.Pattern`, which never appears in any pattern name. Every question of the
form *does this control support Invoke* therefore answered no.

```
reported by UI Automation          Audit 001 stored        detectors asked for
---------------------------------  ----------------------  -------------------
InvokePatternIdentifiers.Pattern   (unchanged, 31 chars)   "Invoke"
TogglePatternIdentifiers.Pattern   (unchanged)             "Toggle"
ValuePatternIdentifiers.Pattern    (unchanged)             "Value"
```

`DetectorControlTests.TheOriginalDefectWouldHaveSilencedTheseDetectors` builds a
tree the old way and asserts the pattern-dependent detectors go quiet, then
builds the same tree the new way and asserts they fire. If the trimming breaks
again, that test fails.

---

## 2. Which detectors were affected

| Detector | Pattern-dependent | Audit 001 evidence |
| --- | --- | --- |
| `actionable-control-without-accessible-name` | yes | **invalidated** |
| `actionable-control-not-focusable` | yes | **invalidated** |
| `duplicate-accessible-name` | yes | **invalidated** |
| `Clipped` — offscreen operable controls | yes | **invalidated** |
| `InteractiveCount` — how much was interactive | partly | **degraded**, not zeroed |
| `decorative-element-is-focusable` | no — counts patterns, does not name them | valid |
| `input-without-label` | no — control type and name only | valid |
| `focus-not-moved-into-overlay` | no — compares automation ids | valid |

`input-without-label` is the check that found `AOS-R001-004`. It is controlled
here too, so that correcting the other three cannot quietly break the one that
worked.

There is **one** clipping detector, not two. Repair Wave 002's report said "both
clipping checks", which was loose: `Clipped` is a single detector applied to two
kinds of surface — workspaces and the narrow-window probe. Both applications were
dead. `Interactive`, which shares the same pattern test, degraded rather than
died because it also accepts any keyboard-focusable control.

---

## 3. Controls

Every row below is a test. Positive means the detector must fire; negative means
it must stay silent.

### Accessible name

| Control | Sample | Required |
| --- | --- | --- |
| positive | `Button`, Invoke, no name | fires |
| negative | `Button "Record an offer"`, Invoke | silent |

### Keyboard reachability

| Control | Sample | Required |
| --- | --- | --- |
| positive | `Button "Record an offer"`, Invoke, not focusable | fires |
| negative | `ListItem`, SelectionItem, not focusable | silent |

A list row is not expected to be its own Tab stop; the list is.

### Duplicate accessible name — the third disabled detector

| Control | Sample | Required |
| --- | --- | --- |
| positive | two `Button "Open"`, both Invoke | fires once, naming `Open` |
| negative | `Button "Open deal"` and `Button "Open contract"` | silent |

### Decorative focus

| Control | Sample | Required |
| --- | --- | --- |
| positive | `Text "Waiting to send"`, focusable, no patterns | fires |
| negative | `Text "Waiting to send"`, not focusable | silent |

### Input label

| Control | Sample | Required |
| --- | --- | --- |
| positive | `ComboBox`, focusable, no name | fires |
| negative | `ComboBox "Status"` | silent |

### Row speech — a detector Audit 001 did not have

`AOS-R001-003` and `AOS-R001-012` were found by reading captured trees by hand.
That is why neither could be re-run and neither could visibly regress. They are
now a check with four faults, reported separately because their repairs differ.

| Control | Sample | Required |
| --- | --- | --- |
| positive | `DealSummaryResponse { Id = 01a0a015-…, Kind = TalentEmployment }` | `row-announces-its-record`, 1 identifier |
| positive | `Autumn slate 01a0a015-d0bc-709c-…` | `row-announces-an-identifier` |
| positive | 240 characters | `row-announces-too-much` |
| positive | `Autumn slate, TalentEmployment` | `row-announces-a-domain-token` |
| negative | `Autumn slate — lead role, A24, Negotiating, Talent employment` | silent |
| negative | `Priya Raghunathan, Pinewood Streaming Group, Active` | silent |
| negative | `brief-writer` | silent |
| negative | `Sent materials — recorded, not sent, Email` | silent |
| negative | `Text "TalentEmployment"` — not a row | silent |

The last negative matters: only rows are judged as rows.

### Clipping and reachability

*Off screen* and *cannot be reached* are different claims, and only the second is
a defect. A naive replacement detector that counted everything off screen would
report a page of false defects, which is no better than a check that cannot fire.

| Control | Sample | Required |
| --- | --- | --- |
| positive | rectangle past the window edge, nothing scrolls it | `CLIPPED_UNREACHABLE` |
| positive | offered a scroll, arrived still off the window | `CLIPPED_UNREACHABLE` |
| negative | offered a scroll, arrived on the window | `SCROLL_REACHABLE` |
| negative | rectangle on the window, not offscreen | `REACHABLE` |
| negative | rectangle on the window, tree says offscreen | **not** `REACHABLE` |
| neither | offered a scroll, never reported a rectangle | `INCONCLUSIVE` |

The fifth stops a collapsed panel — which keeps its last rectangle — being called
visible on geometry alone. The sixth is the honest answer where there is not one:
`INCONCLUSIVE` is recorded rather than guessed either way.

### Scoping

| Control | Sample | Required |
| --- | --- | --- |
| negative | Minimize / Maximize / Close under `TitleBar` | silent |
| negative | unnamed `Group` holding a named focusable `Edit` | silent |
| positive | unnamed `Group` holding only `Text` | fires |

The window frame is drawn by Windows, not by this repository, and is reached
through the system menu rather than the Tab order. WinUI builds an
`AutoSuggestBox` as an unnamed `Group` around a named `Edit`, and a screen reader
lands on the `Edit`. The third control keeps that exclusion narrow: an exclusion
that swallows the real case trades one silent check for another.

---

## 4. Known limitation of the legacy clipping detector

`Clipped` reports operable controls the tree calls offscreen. On a scrolling list
that is ordinary: Audit 001R measured **36** such controls in the command palette,
all of them command rows below the visible part of a list that scrolls and
filters. They are not defects.

`Clipped` cannot tell those from a genuinely unreachable control, because
answering that needs the live scroll test. The authoritative clipping evidence in
this rebaseline is therefore the reachability classifier used by the layout pass,
which asks each candidate to scroll itself into view before recording a verdict.
`Clipped`'s output is retained as a pointer, not as a finding.

---

## 5. Result

74 reviewer tests pass, of which 26 are the controls above.

No detector in this rebaseline reported a zero that had not first been shown
capable of reporting something else.
