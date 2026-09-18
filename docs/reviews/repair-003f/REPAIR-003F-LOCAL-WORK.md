# Repair Wave 003F — local work ledger

Every file this wave touched, and why. Recorded here rather than left to be
reconstructed from history, because no Git operation was performed during this
programme.

Baseline for the wave: the working tree as 003E left it.

---

## Production code

| File | Finding | Purpose |
| --- | --- | --- |
| `src/AgencyOS.Windows/Pages/ContractsPage.xaml` | `AOS-R002-021` | The five detail commands move from a horizontal `StackPanel` with no overflow into a `CommandBar`, which puts what does not fit into its own overflow instead of off the window. |
| `src/AgencyOS.Windows/MainWindow.xaml.cs` | `AOS-R001-013`, `AOS-R001R-001` | One call, where every selection passes: the chosen destination is brought into view. |

**No server file was changed**, no contract, no migration, no permission. Both
changes are layout and shell behaviour.

### What changed about the contract commands, exactly

`Button` became `AppBarButton` and the row became a `CommandBar`. Names, click
handlers and enabled-state logic are unchanged, so every automation id the audit
and the harness use still resolves, and `ContractsPage.xaml.cs` was not touched.

**`CommandBar` is used nowhere else in the product.** That is a real cost and it
is recorded rather than glossed: this page now looks slightly different from the
other sixteen. It was chosen over the alternatives because the SDK has no
wrapping panel, adding a package is forbidden without approval
(`CLAUDE.md` §6), and a horizontally scrolling command row is worse for an
operator than an overflow menu. It is also what the finding suggested.

## Tests

| File | Covers |
| --- | --- |
| `tests/AgencyOS.Tests.Windows/Layout/CommandReachTests.cs` (new, 7) | That the contract commands live in something that can overflow, that each of the five is inside it, and that the pane is brought into view in the handler every selection passes through. |

## Reviewer

| File | Purpose |
| --- | --- |
| `tools/AgencyOS.Reviewer/Runtime/CrashProbe.cs` | A `see:` step: whether something *named* is on the window, and its rectangle. By name because the navigation pane's destinations carry no automation id, and adding one to measure them would be changing the product to suit the harness. |

## Documentation

| File | Purpose |
| --- | --- |
| `docs/reviews/repair-003f/REPAIR-003F-ADMISSION.md` | What is admitted, and why `AOS-R002-015` and `AOS-R002-016` are assigned here but not admissible. |
| `docs/reviews/repair-003f/REPAIR-003F-LOCAL-WORK.md` | This ledger. |
| `docs/reviews/repair-003f/REPAIR-003F-REPORT.md` | The wave's report. |

## Temporary changes

| Change | Purpose | Reverted |
| --- | --- | --- |
| `MainWindow.xaml.cs` — `StartBringIntoView()` disabled for one build | To measure the *before* with the same instrument as the after, rather than citing an older audit's number against a new measurement. | **yes** — restored and rebuilt; all gates re-run afterwards. |

## Runtime evidence

`artifacts/reviewer/run-repair-003f-local/`

| Path | What |
| --- | --- |
| `contract-commands/` | The five commands at 1600x1000 after the repair. |
| `contract-overflow/` | The overflow opened, and the three commands behind it read from the live tree. |
| `pane-before/` | Sync and Offline and AI, selected, **offscreen with no rectangle** — the fix disabled. |
| `pane/` | The same two, selected, **on screen at 53,780,330,57** — the fix enabled. |
