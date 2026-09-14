# Repair Wave 002 — Accessibility and obvious UI correctness

**Wave:** AgencyOS Review Repair Wave 002
**Scope approved by the owner:** `AOS-R001-003`, `AOS-R001-004`, `AOS-R001-007`,
`AOS-R001-012`, `AOS-R001-013`
**Explicitly out of scope:** `AOS-R001-006` (typed-GUID dialogs),
`AOS-R001-010` (representation maintenance), and any other workflow or
product-design gap
**Branch:** `repair-wave-001`
**Date:** 2026-09-14

Audit 001 is unchanged. `artifacts/reviewer/run-001/` and its evidence pack were
not written to. This wave's evidence is under `artifacts/reviewer/run-002/`, with
the matched pre-repair control under `artifacts/reviewer/run-002-before/`.

---

## 1. What was repaired

| Finding | Title | State |
| --- | --- | --- |
| `AOS-R001-003` | Every list row announces its raw C# record | **Repaired** |
| `AOS-R001-004` | Filter combo boxes expose no accessible name | **Repaired** |
| `AOS-R001-007` | At 900x700 the detail pane is off screen | **Repaired** |
| `AOS-R001-012` | Lists display raw PascalCase domain tokens | **Repaired** |
| `AOS-R001-013` | The pane shows eleven of seventeen destinations | **Partly repaired** — see §6 |

Two defects were found during the wave and are recorded in §8: one in the review
harness, which had been reporting zeroes from checks that could not run, and one
I introduced and then removed while repairing 007.

---

## 2. Measured result

Every number below is from the review harness driving a real window against
synthetic FORGE data. The before column is the same harness, same build of the
harness, driving the product at `f5ff940` — the tip this wave started from.

| Measure | Before | After |
| --- | --- | --- |
| Rows announcing a record dump or an identifier (003) | **93** | **0** |
| Longest row's accessible name (003) | **894 chars** | **159 chars** |
| Rows announcing a PascalCase token (012) | **93** | **0** |
| Accessibility observations across 19 surfaces (004) | **16** | **0** |
| Named actions unreachable at 900x700 / 1024x768 (007) | **3** | **0** |
| Navigation pane at 900x700 (007) | **expanded** | **compact** |
| Connection footer height (013) | **180 px** | **78 px** |
| Destinations fully visible at 1600x1000 (013) | **11 / 17** | **13 / 17** |
| Destinations covered by the footer (013) | 0 | 0 |
| Keyboard: passes / stops / offscreen / unnamed / trapped | 17 / 680 / 0 / 0 / 0 | 17 / 680 / 0 / 0 / 0 |
| Declared gestures matched | 34 / 35 | 34 / 35 |
| Overlays reached (palette, global search) | 2 / 2 | 2 / 2 |
| Keystrokes the harness refused to send | 0 | 0 |

The "11 / 17" in the before column is the same count Audit 001 reported by
reading a screenshot ("Eleven are visible, ending at Finance"). The geometry pass
arrived at it independently, which is the best evidence available that it is
measuring the thing the finding is about.

---

## 3. `AOS-R001-003` — what a row says

**Before.** A templated `ListView` row whose template sets no accessible name
falls back to `ToString()` on the bound object, and a C# record prints every
property it has. The deal row was 894 characters and six identifiers, beginning
`DealSummaryResponse { Id = 01a0a015-d0bc-709c-…`. The rows render correctly on
screen, so this was invisible to a sighted reviewer and total for anybody using a
screen reader.

**Repair.** One formatter rather than a hundred and four bindings:

- `src/AgencyOS.Client/Presentation/RowLabel.cs` — a pure function from a bound
  row to the phrase it should announce. It reads the same fields the row
  displays, drops `Guid`, `*Id` and `Version`, runs domain tokens through
  `DisplayLabel`, caps the result at 160 characters, and falls back to a friendly
  type name rather than `ToString()`.
- `src/AgencyOS.Windows/Presentation/PresentationConverters.cs` — `RowLabelConverter`.
- 104 `DataTemplate` roots across 25 XAML files now carry
  `AutomationProperties.Name="{Binding Converter={StaticResource RowLabel}}"`.

The deal row now announces:

```
Autumn slate — lead role, A24, Negotiating, Talent employment
```

61 characters, no identifiers, and it names the deal, the counterparty and the
state — which is what a sighted user sees.

**Not claimed.** No GUID is exposed anywhere a row announces, and no row uses the
default record `ToString()`. This is automated evidence about a running
automation tree. Nobody has operated AgencyOS with a screen reader; §12 says so.

---

## 4. `AOS-R001-004` — inputs that announce nothing

**Before.** Eight combo boxes exposed an empty `Name`. The accessibility suite
passed 521 tests while they did, because `NameSources` mapped `ComboBox` to
`["Header", "PlaceholderText"]` and WinUI does not promote `PlaceholderText` for
a `ComboBox`.

**Repair.**

- `tests/AgencyOS.Tests.Windows/Accessibility/XamlAccessibilityTests.cs` —
  `ComboBox` now maps to `["Header"]` only. `TextBox` and `AutoSuggestBox` keep
  `PlaceholderText`, because Audit 001 confirmed at run time that WinUI does
  promote it for those two. The rule was wrong for exactly one control type.
- 15 combo boxes across 13 files were given an `AutomationProperties.Name`:
  Receivable, Account, Debit or credit, Run status, Mailbox, Contract kind, Deal
  kind, Subject, Opportunity kind, Project stage, Missing role, Prospect stage,
  Discipline.

**Proof the corrected test fails against the pre-fix state.** Reverting the XAML
and running the suite produces 13 failures of
`EveryInteractiveControlHasAnAccessibleName`, for example:

```
src\AgencyOS.Windows\Pages\IntelligencePage.xaml has controls a screen reader
cannot announce: ComboBox 'RelationshipSubjectBox'
src\AgencyOS.Windows\Dialogs\RecordPaymentDialog.xaml has controls a screen
reader cannot announce: ComboBox 'ReceivableBox'
```

Independently, the runtime pass against the pre-fix build reports 16
observations — eight `input-without-label` and eight
`actionable-control-without-accessible-name` — and zero against the repaired
build.

---

## 5. `AOS-R001-007` — the detail pane at 900x700

**Before.** `PaneDisplayMode="Left"` pinned the 220-unit navigation pane at every
size. At 900x700 the list column's fixed 460 and the pane consumed the window,
and the detail pane — every action on the surface — was laid out past the window
edge. The harness measured which named actions a user could not get to, by asking
each candidate to scroll itself into view and checking whether it arrived:

| Size | Surface | Unreachable before | After |
| --- | --- | --- | --- |
| 900x700 | Deals | `Button NewButton` | none |
| 900x700 | Command Center | `Button "Complete selected task"` | none |
| 1024x768 | Deals | `Button NewButton` | none |
| 1280x720, 1600x1000 | all four | none | none |

**Repair.**

- `MainWindow.xaml` — `PaneDisplayMode="Auto"`. The pane now compacts below
  WinUI's own breakpoint, which is the behaviour the control was built with. At
  900x700 it is a hamburger and all seventeen destinations are in its flyout.
- `MainWindow.xaml` / `MainWindow.xaml.cs` — the content frame sits in a
  `ScrollViewer` that scrolls on both axes, and `OnContentHostSizeChanged` gives
  the page the host's own size, or a floor of 640 x 560 when the host is smaller
  than that. The rule itself is
  `src/AgencyOS.Client/Presentation/ContentExtent.cs`, where a test can reach it.

Both axes, because the defect is not only horizontal: at 1280x720 the Sync page's
"Discard selected change" was below the fold with nothing to scroll.

**Sizes tested:** 900x700, 1024x768, 1280x720 and 1600x1000, on four surfaces
each — Deals, Command Center, Intelligence, Sync and Offline. Screenshots and
automation trees for all sixteen are under
`artifacts/reviewer/run-002/evidence/layout.<size>.<surface>/`.

At 900x700 the Deals summary line now reads in full — "1 deal(s); 1 awaiting an
answer, 0 with terms agreed, 0 lapsing within a week." Audit 001 recorded it
clipped mid-sentence at "…0 lapsing wit".

---

## 6. `AOS-R001-013` — the navigation pane and its footer

**Repaired.** The footer was three captions holding a server address and a tenant
identifier, wrapping to five lines. It is now three trimmed single lines with an
opaque background and the full text still available on hover.

| Size | Destinations fully visible, before | after |
| --- | --- | --- |
| 1600x1000 | 11 / 17 | **13 / 17** |
| 1280x720 | 7 / 17 | **8 / 17** |
| 1024x768 | 8 / 17 | **9 / 17** |
| 900x700 | 6 / 17 | pane is compact; all 17 in the flyout |

The footer went from 180 px to 78 px and gave that space back to the destination
list. No destination's rectangle intersects the footer's at any size, before or
after.

**Not repaired, and why.** Four destinations at 1600x1000 still need scrolling,
and the pane still has no persistent overflow affordance. This is not something
the markup can be corrected into:

- A `NavigationViewItem` is 40 effective units tall, which is WinUI's own metric —
  the client does not override it.
- Seventeen destinations are therefore 680 units of list.
- On the 150%-scaled display this was measured on, a 1600x1000 window is 1067x667
  effective units, and the pane's scroll region is 508 of them even with the
  footer at zero.

Seventeen top-level destinations do not fit, at any footer height. Closing that
half means either fewer top-level destinations, a different item metric, or an
overflow affordance the framework does not provide by default — all three are
information-architecture or visual-system decisions, which §10 of the wave brief
puts out of scope, and none of them is "removing destinations to make the
measurement pass". **It is carried forward as an open item, not closed.**

The audit's wording — "the footer overlaps the selected item" — was not quite
what was happening, and the correction matters for whoever picks this up.
Measured on both builds, the footer's rectangle never intersects a destination's.
What happened is that the pane's scroll region ended mid-row: before the repair
the last visible destination was 21 of its 54 pixels, its label cut in half,
immediately above a 180-pixel transparent footer. It reads as occlusion and is
not occlusion, and a repair aimed at the overlap would have fixed nothing.

---

## 7. `AOS-R001-012` — domain tokens on screen

**Before.** The Kind filter offered "Talent employment". The deal row two inches
below it read `TalentEmployment`. 208 bindings across the client bound an
enum-valued field straight to `Text` with no converter.

**Repair.** `src/AgencyOS.Client/Presentation/DisplayLabel.cs` — a pure function
that splits PascalCase into sentence case and changes nothing else. 58 bindings
across 17 files now go through `DisplayLabelConverter`.

**No domain member was renamed.** The formatter is applied on the way to a screen
and nothing parses it back. `PresentationLabelTests.MeaningIsNotSimplifiedAway`
asserts that the distinctions the product depends on survive it:

```csharp
Assert.NotEqual(DisplayLabel.For("Status"),     DisplayLabel.For("Stage"));
Assert.NotEqual(DisplayLabel.For("Obligation"), DisplayLabel.For("Task"));
Assert.NotEqual(DisplayLabel.For("Prediction"), DisplayLabel.For("Fact"));
Assert.NotEqual(DisplayLabel.For("Recorded"),   DisplayLabel.For("Sent"));
Assert.NotEqual(DisplayLabel.For("Source"),     DisplayLabel.For("Truth"));
```

Terms the business writes its own way are preserved rather than split: `AI` stays
`AI`, `ProjectLicense` becomes "Project licence", `NoDeal` becomes "No deal".
Prose passes through untouched, because several of these bindings carry a
sentence somebody wrote rather than a token.

---

## 8. Two defects found during the wave

### 8.1 The harness had three checks that could not fire

`UiaTree.Patterns` trimmed a UI Automation pattern name with the wrong suffix. UI
Automation reports `InvokePatternIdentifiers.Pattern`; the code removed
`Pattern.Pattern`, which never appears. Every question of the form "does this
control support Invoke" therefore answered no.

Three of the five accessibility checks —
`actionable-control-without-accessible-name`, `actionable-control-not-focusable`
and `duplicate-accessible-name` — and both clipping checks could not fire at all.

**Audit 001 read their zeroes as findings of nothing. They were findings of a
check that could not run.** The one accessibility check that did work is the one
that found `AOS-R001-004`.

Fixed in `UiaTree.ShortName`, with `GeometryTests` pinning the exact strings and
`AccessibilityCheckTests` proving each check fires against a hand-built tree — a
check only ever exercised against a real window is a check whose silence nobody
can distinguish from good news, which is how this survived a whole audit.

Two false positives surfaced once the checks started working, and both were
narrowed rather than silenced:

- The window's caption buttons — Minimize, Maximize, Close — are drawn by the
  window frame and reached through the system menu. Three identical observations
  on each of nineteen surfaces, saying nothing about AgencyOS. The pass now skips
  the `TitleBar` subtree.
- WinUI builds an `AutoSuggestBox` as an unnamed `Group` holding a named,
  focusable `Edit`. A screen reader lands on the `Edit`. A container whose
  focusable child does the work is no longer reported —
  `AContainerWithNothingReachableInsideItIsStillReported` keeps that exclusion
  narrow.

### 8.2 A regression I introduced repairing 007, and removed

The first attempt at 007 put the content frame in a `ScrollViewer` and left it
unconstrained. A `ScrollViewer` offers its content unlimited width, and a page
with star-sized columns takes all of it: Command Center's Refresh and "Complete
selected task" buttons then sat outside a 1600x1000 window that had had room for
them. That is a worse defect than the one being fixed.

It was caught by the layout pass comparing against the pre-repair control, not by
looking at a screenshot. The second attempt binds nothing and sizes the page from
`SizeChanged`, because WinUI's `ActualWidth` raises no change notification for a
binding to follow — the reason the first attempt's floor never applied.

---

## 9. Tests

| Suite | Before the wave | After |
| --- | --- | --- |
| Unit | 3,749 | **3,749** |
| Windows | 521 | **715** |
| Reviewer | 22 | **48** |
| Integration | 790 | **790** |

New test files:

- `tests/AgencyOS.Tests.Windows/Presentation/PresentationLabelTests.cs` — 23
  tests over `RowLabel` and `DisplayLabel`.
- `tests/AgencyOS.Tests.Windows/Accessibility/XamlRowAndTokenTests.cs` — the two
  markup rules Audit 001 asked for: every `DataTemplate` root has an accessible
  name (003), and no `TextBlock` shows a domain token without a converter (012).
- `tests/AgencyOS.Tests.Windows/Layout/ShellLayoutTests.cs` — the four shell
  markup decisions behind 007 and 013, plus the content-sizing rule.
- `tests/AgencyOS.Tests.Reviewer/GeometryTests.cs` — the rectangle arithmetic the
  layout pass reasons with, and the pattern names it reasons about.
- `tests/AgencyOS.Tests.Reviewer/AccessibilityCheckTests.cs` — that each
  accessibility check fires, and on the right things.

**Every new markup test fails against the pre-repair markup.** Reverting the XAML
and running the suite:

| Test | Failures against the pre-fix state |
| --- | --- |
| `EveryRowTemplateSaysWhatItShows` | 25 files |
| `EveryDomainTokenIsShownAsWords` | 17 files |
| `EveryInteractiveControlHasAnAccessibleName` | 13 files |
| `TheNavigationPaneIsNotPinnedAtEverySize` | `Expected: Not "Left" / Actual: "Left"` |
| `ThePageCanBeScrolledToWhenItDoesNotFit` | no such host in the markup |
| `TheConnectionFooterDoesNotGrowIntoTheDestinationList` | `SyncText` wraps |
| `TheConnectionFooterIsOpaque` | no background |

59 failures in total. The XAML was restored from a saved copy afterwards and the
suite is green.

---

## 10. Gates

| Gate | Result |
| --- | --- |
| `dotnet build AgencyOS.sln` | **0 warnings, 0 errors** |
| Unit | **3,749 passed** |
| Windows | **715 passed** |
| Reviewer | **48 passed** |
| Integration | **790 passed** (local, PostgreSQL 19beta3 — LAB, not promotion evidence) |
| OpenAPI | **3.1.1; 260 paths; 185 schemas** — unchanged |
| API contract version | **13** — unchanged |
| TLA+ | **4 / 4**: OfflineWriteQueue, OutboundSend, AiApproval, LocalInferenceLease |

Neither the path count, the schema count nor the contract version moved. Nothing
in this wave touched an endpoint, a request shape or a response shape.

### Hosted CI

The authoritative evidence is the GitHub Actions run dispatched against the exact
commit, not the local run above. The local database is PostgreSQL 19beta3, which
is LAB and is not promotion evidence.

| | |
| --- | --- |
| Run | [34858183704](https://github.com/3twito-del/AgencyOS/actions/runs/34858183704) |
| Commit | `93c7a012a2546c7bc728c51a6490e024ec634010` |
| Trigger | `workflow_dispatch` |
| Conclusion | **success** |
| Build and unit tests (Windows) | success |
| Integration tests (PostgreSQL 18.6) | success |

Counts read from that run's log:

```
Unit           Failed: 0, Passed: 3749, Total: 3749
Windows        Failed: 0, Passed:  715, Total:  715
Reviewer       Failed: 0, Passed:   48, Total:   48
Integration    Failed: 0, Passed:  790, Total:  790   (postgres:18.6)
OpenAPI        3.1.1; 260 paths; 185 schemas
TLC            OfflineWriteQueue, OutboundSend, AiApproval, LocalInferenceLease
```

The reviewer tests ran rather than merely building — the step added in Wave 001.5
is still doing its job, and `48` is the count this wave leaves behind.

---

## 11. What this wave did not do

- `AOS-R001-006` (typed-GUID dialogs) and `AOS-R001-010` (representation
  maintenance) were not touched, and no similar workflow or product-design gap
  was addressed.
- The overflow half of `AOS-R001-013` is open — §6.
- `sync.now` / F9 still does not reach Sync and Offline. It is
  `AOS-R001-020` and was not in scope; it is the one gesture of thirty-five that
  does not match, before and after.
- No colour, typography or spacing system was introduced or changed. No page was
  redesigned. The only visual changes are: the pane compacts on a small window,
  the footer trims instead of wrapping and has a background, and domain tokens
  read as words.
- Audit 001's unreviewed dialogs and roles are still unreviewed. This wave
  changed markup in nine dialogs, but no dialog was opened, operated or observed
  at run time, and nothing here should be read as coverage of them.
- The screenshots under `artifacts/reviewer/run-002/` are LAB evidence from an
  interactive desktop session on synthetic FORGE data. They are not hosted-CI
  evidence and must not be cited as such.

---

## 12. Manual gates still outstanding

1. **Operate AgencyOS with a real screen reader.** Everything here is about what
   the automation tree exposes. Narrator or NVDA reading a deal list is a
   different claim and nobody has made it.
2. **200% display scaling.** The markup rule against fixed heights passes; whether
   the result is usable at 200% is unverified.
3. **A high-contrast theme.** The markup hard-codes no colours; nobody has looked
   at the product under one.
4. **The dialogs.** 61 dialogs are reachable and none was operated in Run 001 or
   in this wave.
5. **A stated minimum window size.** The client still declares no `MinWidth` or
   `MinHeight`, so 900x700 remains an allowed size that nothing warns about. The
   repair makes it usable; it does not make it a decision anybody has taken.
6. **Validation-text association.** The runtime pass does not check that an error
   message is associated with the input it belongs to. It is not claimed.

---

## 13. Files changed

```
src/AgencyOS.Client/Presentation/ContentExtent.cs        new
src/AgencyOS.Client/Presentation/DisplayLabel.cs         new
src/AgencyOS.Client/Presentation/RowLabel.cs             new
src/AgencyOS.Windows/Presentation/PresentationConverters.cs  new
src/AgencyOS.Windows/App.xaml                            converters, 5 keyed templates
src/AgencyOS.Windows/MainWindow.xaml                     pane mode, footer, content host
src/AgencyOS.Windows/MainWindow.xaml.cs                  content sizing
src/AgencyOS.Windows/Pages/*.xaml        (16 files)      row names, combo names, tokens
src/AgencyOS.Windows/Dialogs/*.xaml      (8 files)       row names, combo names, tokens

tests/AgencyOS.Tests.Windows/Accessibility/XamlAccessibilityTests.cs   ComboBox rule
tests/AgencyOS.Tests.Windows/Accessibility/XamlRowAndTokenTests.cs     new
tests/AgencyOS.Tests.Windows/Layout/ShellLayoutTests.cs                new
tests/AgencyOS.Tests.Windows/Presentation/PresentationLabelTests.cs    new
tests/AgencyOS.Tests.Reviewer/AccessibilityCheckTests.cs               new
tests/AgencyOS.Tests.Reviewer/GeometryTests.cs                         new

tools/AgencyOS.Reviewer/Runtime/UiaTree.cs        pattern names (§8.1)
tools/AgencyOS.Reviewer/Runtime/AuditPass.cs      layout pass, scoping, live reachability
tools/AgencyOS.Reviewer/Runtime/Rectangle.cs      new
tools/AgencyOS.Reviewer/Runtime/RuntimeEvidence.cs  layout records
tools/AgencyOS.Reviewer/Program.cs                `layout` mode
tools/AgencyOS.Reviewer/AgencyOS.Reviewer.csproj  InternalsVisibleTo for the tests
```

No product code outside the Windows client was changed. No endpoint, no domain
type, no migration, no authorization rule.
