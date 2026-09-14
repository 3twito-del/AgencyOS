# Audit 001R — coverage

What this rebaseline actually looked at, and what it did not.

**Product baseline:** `51541ea` — unchanged by this audit.
**Display:** 150% scaling. UI Automation reports physical pixels; one effective
unit is 1.5 of them. A window asked for at 1600x1000 is 1067x667 to the layout
system, and every WinUI breakpoint is written in those units.

---

## 1. Workspaces

`COMPLETE` means the pass ran and produced a verdict for every operable control
on the surface. `PARTIAL` means at least one control could not be classified
either way and was recorded `INCONCLUSIVE` rather than guessed.

| Workspace | Accessibility | Clip 900x700 | Clip 1024x768 | Clip 1280x720 | Clip 1600x1000 | Keyboard/gesture | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Command Center | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| People | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Companies | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Talent | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Prospects | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Projects | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Packages | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Pipeline | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Deals | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Contracts | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Finance | COMPLETE | PARTIAL | PARTIAL | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Documents | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Communications | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Intelligence | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| AI | COMPLETE | PARTIAL | PARTIAL | PARTIAL | COMPLETE | COMPLETE | COMPLETE |
| Saved Views | COMPLETE | PARTIAL | PARTIAL | COMPLETE | COMPLETE | COMPLETE | COMPLETE |
| Sync and Offline | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE | COMPLETE |

**17 of 17 workspaces reached**, at all four sizes.

### Why three surfaces are PARTIAL

| Surface | Control | Why |
| --- | --- | --- |
| Finance | `Button "Apply"`, `Edit ReceivableCurrencyBox` | Inside an unselected tab. Present in the tree, not shown, and correctly not reachable until the user opens that tab. The pass does not click tabs. **Not a defect.** |
| AI | `Button StartButton` | Same — inside an unselected tab. **Not a defect.** |
| Saved Views | `Button SaveButton`, `Button "Refresh"` | No tabs on this page. Genuinely below the fold and not brought back by scrolling. **`AOS-R001R-002`.** |

`INCONCLUSIVE` is recorded where the pass could not establish an answer. It is
not a softer way of saying "fine".

---

## 2. What was measured

| | |
| --- | --- |
| Controls scanned by the accessibility detectors | **1,526** |
| Of which interactive | **583** |
| Named list rows scanned by the row-speech detector | **370** |
| Layout probes (17 workspaces x 4 sizes) | **68** |
| Control reachability verdicts | **480** |
| Destinations probed | **17** |
| Declared gestures pressed | **35** |
| Overlays probed | **2** |
| Screenshots | **85** |
| UI trees | **85** |

---

## 3. Deliberately out of scope

Audit 001R re-runs the detector classes whose negative evidence was invalidated.
It is not a second full audit. The following were **not** covered and must not be
read as covered:

| Not covered | Status |
| --- | --- |
| **61 dialogs** | Not opened, not operated, not scanned. Outside 001R scope and still unreviewed since Audit 001. |
| **Roles other than owner** | Only `w2-owner` was used. |
| **Mutation workflows** | Nothing was invoked, submitted, saved or sent. The pass navigates, photographs, reads the tree, and scrolls. |
| **Real screen-reader audio** | MANUAL_GATE. Nothing here is a screen-reader certification. |
| **200% display scaling** | MANUAL_GATE. Measured at 150% only. |
| **High-contrast theme** | MANUAL_GATE. |
| **Validation-text association** | No detector exists for it. Not claimed. |
| **Minimum supported window size** | Still not declared by the product. 900x700 remains an allowed size nothing warns about. |
| **Tab contents behind unselected tabs** | Reachable by clicking the tab; the pass does not click. Recorded `INCONCLUSIVE`. |

---

## 4. Evidence layout

```
artifacts/reviewer/run-001r/
  AUDIT-001R-SUMMARY.md
  AUDIT-001R-FINDINGS.json
  AUDIT-001R-COVERAGE.md
  AUDIT-001R-DETECTOR-VALIDATION.md
  runtime.json                        the whole pass, machine-readable
  evidence/workspace.<tag>/           17 surfaces: screenshot + ui-tree
  evidence/layout.<size>.<tag>/       68 probes: screenshot + ui-tree
  evidence/overlay.<name>/            palette and global search
  pre-wave-002/                       the same layout probe against f5ff940,
                                      used to establish that AOS-R001R-002
                                      pre-dates Repair Wave 002
```

`run-001`, `run-002` and `run-002-before` are untouched.

All data is synthetic FORGE fixture data in the `agencyos_w2` review database. No
real agency data was read, and no screenshot contains any.
