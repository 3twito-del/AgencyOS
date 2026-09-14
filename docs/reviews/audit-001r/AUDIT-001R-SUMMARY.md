# Audit 001R — Reviewer Rebaseline

**Reviewer commit:** this commit — reviewer tooling and reviewer tests only.
**Product baseline:** `51541ea`, unchanged.
**Evidence:** `artifacts/reviewer/run-001r/`
**Date:** 2026-09-14

---

## The claim this rebaseline exists to settle

> Audit 001 negative evidence for detector classes
> `actionable-control-without-accessible-name`,
> `actionable-control-not-focusable`, `duplicate-accessible-name` and the
> `Clipped` clipping check was invalidated by a reviewer defect discovered during
> Repair Wave 002. Positive findings remain valid. Corrected evidence is supplied
> by Audit 001R.

Audit 001 is not wholly invalid. Every defect it reported, it reported correctly,
and Repair Waves 001 through 002 were built on those reports. What was not
evidence was its **silence** in four detector classes.

Audit 001's own record is unchanged. `REVIEW-001-SUMMARY.md`,
`REVIEW-001-FINDINGS.json`, `REVIEW-001-COVERAGE.md`,
`REVIEW-001-SURFACE-MAP.json` and the run-001 evidence pack were not written to.
This is an additive record.

---

## 1. The defect

`UiaTree.Patterns` trimmed a UI Automation pattern name with the wrong suffix. UI
Automation reports `InvokePatternIdentifiers.Pattern`; the harness removed
`Pattern.Pattern`, which appears in no pattern name. Every question of the form
*does this control support Invoke* therefore answered no, on every control, on
every surface.

| Detector | Pattern-dependent | Audit 001 evidence |
| --- | --- | --- |
| `actionable-control-without-accessible-name` | yes | **invalidated** |
| `actionable-control-not-focusable` | yes | **invalidated** |
| `duplicate-accessible-name` | yes | **invalidated** |
| `Clipped` — offscreen operable controls | yes | **invalidated** |
| `InteractiveCount` | partly | **degraded**, not zeroed |
| `decorative-element-is-focusable` | no | valid |
| `input-without-label` | no | valid — this is what found `AOS-R001-004` |
| `focus-not-moved-into-overlay` | no | valid |

One correction to Repair Wave 002's own report: it said "both clipping checks".
There is one clipping detector, `Clipped`, applied to two kinds of surface. Both
applications were dead. `Interactive` shares the pattern test but also accepts any
keyboard-focusable control, so it under-counted rather than returned nothing.

---

## 2. Detector self-verification

Every corrected detector was shown to fire on a known-bad hand-built tree and stay
silent on a known-good one **before** anything was scanned. 26 controls, kept as
permanent tests in `tests/AgencyOS.Tests.Reviewer/DetectorControlTests.cs`.

One of them rebuilds a tree the way the broken harness would have recorded it and
asserts the pattern-dependent detectors go quiet — so the original defect cannot
return unnoticed.

Details: `AUDIT-001R-DETECTOR-VALIDATION.md`.

---

## 3. What the corrected detectors say now

All 17 workspaces, 1,526 controls scanned, 583 of them interactive.

| Detector | Hits |
| --- | --- |
| `actionable-control-without-accessible-name` | **0** |
| `actionable-control-not-focusable` | **0** |
| `duplicate-accessible-name` | **0** |
| `decorative-element-is-focusable` | **0** |
| `input-without-label` | **0** |
| row speech — record, identifier, length, domain token | **0** across 370 named rows |

Clipping, 68 probes at four sizes, 480 control verdicts:

| Verdict | Count |
| --- | --- |
| `REACHABLE` | 461 |
| `SCROLL_REACHABLE` | 8 |
| `INCONCLUSIVE` | 11 |
| **`CLIPPED_UNREACHABLE`** | **0** |

These zeroes mean something, because the detectors that produced them have been
shown to produce non-zeroes.

---

## 4. What Audit 001 would have reported

The corrected harness was run against the **pre-Repair-Wave-002 product**
(`f5ff940`) during Wave 002, which makes it possible to say what the broken
detectors were hiding at the time rather than only what is true now.

| Detector | Audit 001 reported | Corrected harness, same-era product | Audit 001R, today |
| --- | --- | --- | --- |
| `actionable-control-without-accessible-name` | 0 | **8** | 0 |
| `actionable-control-not-focusable` | 0 | 0 | 0 |
| `duplicate-accessible-name` | 0 | 0 | 0 |
| `Clipped` on workspaces | 0 | **2** | 0 |
| `Clipped` on overlays | 0 | 36 | 36 |
| `input-without-label` | 8 | 8 | 0 |

The 8 were the same combo boxes `input-without-label` had already found and
reported as `AOS-R001-004`. The 2 were `Button NewButton` on Deals and
`Button "Complete selected task"` on Command Center at 900x700 — the controls
`AOS-R001-007` describes. **In both classes the underlying defect was reported by
a detector that still worked.** The broken detectors would have corroborated, not
revealed.

The 36 on the palette are command rows scrolled out of a filtering list. They are
not defects, and `Clipped` cannot tell them from real ones — which is why the
authoritative clipping evidence here is the scroll-aware reachability classifier,
not `Clipped`.

---

## 5. Invalidated negative claims, classified

| Audit 001 negative claim | Classification | Basis |
| --- | --- | --- |
| `actionable-control-without-accessible-name` = 0 on 17 workspaces | **FIXED_BY_WAVE_002_BEFORE_REBASELINE** | It was 8 at the time. All 8 were the `AOS-R001-004` combo boxes, named in Wave 002. Now 0. |
| `actionable-control-not-focusable` = 0 on 17 workspaces | **RECONFIRMED_BY_001R** | 0 both on the same-era product and today. |
| `duplicate-accessible-name` = 0 on 17 workspaces | **RECONFIRMED_BY_001R** | 0 both on the same-era product and today. |
| `Clipped` = 0 on 17 workspaces at desktop size | **RECONFIRMED_BY_001R** | 0 then and now at 1600x1000. |
| `Clipped` = 0 on the narrow-window probe | **FIXED_BY_WAVE_002_BEFORE_REBASELINE** | It was 2 at the time, both `AOS-R001-007` controls, repaired in Wave 002. Now 0. |
| `Clipped` = 0 at small sizes on workspaces Audit 001 never probed | **SUPERSEDED_BY_NEW_FINDING** | Audit 001's narrow probe covered 2 of 17 workspaces. Sweeping all 17 found `AOS-R001R-002` and `AOS-R001R-004`. |
| "Selecting Intelligence scrolls it into view" (`AOS-R001-013`) | **SUPERSEDED_BY_NEW_FINDING** | It does not, and never did. See §6 and `AOS-R001R-001`. |
| `InteractiveCount` per surface | **STILL_UNVERIFIED** as a historical series | Audit 001's counts were produced by the degraded predicate and cannot be compared to anything. 001R measures 583 interactive of 1,526 scanned, on its own scoping. |
| Accessibility = 0 inside the 61 dialogs | **STILL_UNVERIFIED** | Audit 001 never opened them and neither did 001R. Out of scope, and not covered. |

No historical zero is left ambiguous.

---

## 6. `AOS-R001-013` — refined, not repaired

Status stays **PARTIAL / OPEN**. Nothing was changed. The four questions:

**A. Is any destination actually unreachable?** No. All 17 open and select
correctly at every size. At 1600x1000, 13 are visible at rest and 4 are not.

**B. Does scrolling work reliably?** Yes. All 4 below-the-fold destinations come
onto the window when asked, landing at `53,855,330,60` every time, in three
independent passes.

**C. Is the selected item brought into view?** **No.** Selecting Intelligence, AI,
Saved Views or Sync and Offline leaves the pane where it was, and nothing in the
pane indicates which destination is current. The screenshot
`evidence/workspace.sync/01-loaded.png` shows the Sync and Offline page with a
navigation pane reading Command Center through Communications and no selection
marked anywhere.

Audit 001 stated the opposite. That statement came from snapshots taken after the
harness's own keyboard pass had called `SetFocus` on the previous workspace's
navigation item, which scrolls the pane. The product has never followed the
selection. Repair Wave 002's report inherited the error; both are corrected here.

**D. Is the missing overflow affordance a defect or a recommendation?** On the
evidence of (C), a **UX defect**, recorded as `AOS-R001R-001`. Not being able to
see six of seventeen destinations is a discoverability complaint on its own; not
being able to see which one you are currently on is a different and worse thing,
and the two compound.

The arithmetic behind the pane, now measured rather than assumed: a
`NavigationViewItem` is 40 effective units, seventeen of them are 680, and the
pane's scroll region is 508 at 1600x1000 on a 150% display. They do not fit at any
footer height. Repair Wave 002 halved the footer, 180px to 78px, which bought two
destinations and could not buy more.

---

## 7. Keyboard and gesture sanity

All 35 declared gestures pressed, from a known starting workspace, three times.

| Verdict | Count | Detail |
| --- | --- | --- |
| `LANDED` | **32** | Navigation gestures that selected the workspace the registry declares. |
| `NO_OP` | **1** | `sync.now` / F9. Delivered; selection unchanged. `AOS-R001-020`, still deferred. |
| `CONTEXT_DEPENDENT` | **2** | `search.open` / Ctrl+K and `view.refresh` / F5. Neither declares a destination, so this pass cannot judge them by which workspace is selected. |
| `WRONG_TARGET` | 0 | |
| `BLOCKED_BY_STATE` | 0 | |

Keystrokes the harness refused to send: **0**.

Repair Wave 002 reported "34/35 matched". That count treated the two
non-navigation commands as landed because *something* was selected afterwards,
which is a question the pass never asked. The honest figure is 32 verifiable
landings, 1 no-op, 2 unverifiable by this method — the same product behaviour,
described accurately.

`AOS-R001-020` is **unchanged and still deferred**. The rebaseline produces no
stronger evidence than Audit 001 had: F9 is delivered and does nothing.

**Command palette:** opens on Ctrl+P with focus in `PaletteQuery`, in all three
passes, and closes on Escape.

**Global search:** did **not** open on Ctrl+K in any of the three passes — the
overlay's elements are absent from the tree entirely. The identical probe against
the identical binary succeeded in Audit 001 and in the Repair Wave 002 run. That
makes it state-dependent, not a code difference, and it is recorded as
`AOS-R001R-003` with confidence `LikelyDefect` and reproducibility `Sometimes`
rather than presented as confirmed.

---

## 8. New findings

| Id | Severity | Confidence | Title |
| --- | --- | --- | --- |
| `AOS-R001R-001` | S2 | ConfirmedDefect | The navigation pane does not follow the selection, so the current destination is unmarked and invisible for 4 of 17 |
| `AOS-R001R-002` | S2 | ConfirmedDefect | The Saved View editor's actions are unreachable below 1600x1000 |
| `AOS-R001R-003` | S2 | LikelyDefect | Ctrl+K sometimes does not open global search |
| `AOS-R001R-004` | S3 | ConfirmedDefect | The Finance tab strip overflows at 900x700 with no affordance of its own |

Severity counts: **S0 0, S1 0, S2 3, S3 1, S4 0.**

New identifiers, not recycled. `AOS-R001R-002` and `AOS-R001R-004` are the same
class as `AOS-R001-007` on workspaces Audit 001 never probed; `AOS-R001R-002` was
measured against `f5ff940` and **pre-dates Repair Wave 002**, which improved that
page at every size and introduced nothing.

Full records: `AUDIT-001R-FINDINGS.json`.

---

## 9. Validation

No product code changed. `git status` on `src/` is empty; the product baseline is
`51541ea` throughout.

| Gate | Result |
| --- | --- |
| `dotnet build AgencyOS.sln` | **0 warnings, 0 errors** |
| Unit | **3,749 passed** |
| Windows | **715 passed** |
| Reviewer | **74 passed** (was 48; +26 detector controls) |

Integration was not re-run. No product code, contract, migration or endpoint was
touched, and the reviewer is not in the integration suite's dependency graph.

---

## 10. What this does not claim

- Not a screen-reader certification. Real screen-reader audio remains a
  MANUAL_GATE, and so do 200% scaling and a high-contrast theme.
- Not coverage of the 61 dialogs. They were not opened in Audit 001 and were not
  opened here.
- Not a second full audit. Only the invalidated detector classes were re-run.
- Not evidence about roles other than owner.
- LAB evidence from an interactive desktop session on synthetic FORGE data. It is
  not hosted-CI evidence and must not be cited as such.
