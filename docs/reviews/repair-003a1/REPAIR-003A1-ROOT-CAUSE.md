# Repair Wave 003A.1 — root cause of `AOS-R002-022`

**The three dialogs never crashed.** The client died on an empty detail tab that
the review harness walked into after the dialog's first attempt was refused, and
the harness recorded the dialog it was attempting. Every step below was measured
on a running client; nothing here is inferred from source alone.

---

## 1. Finding identifier

Repair Wave 003B observed the crash and deliberately did not number it
(`AUDIT-002-AGGREGATE.md` §7, "Not yet filed as a numbered finding"). The
register ended at `AOS-R002-021`, so this is **`AOS-R002-022`**. One identifier for
the crash class; the eleven markup sites are one defect, not eleven.

| | |
| --- | --- |
| **Finding** | `AOS-R002-022` |
| **Affected dialogs, as reported** | `AttachToRoleDialog`, `RecordPitchDialog`, `RecordSubmissionDialog` |
| **Affected surfaces, as measured** | Eleven detail tabs on Projects, Pipeline, Deals and Contracts |
| **First observed** | 2026-09-15 02:37Z, Audit 002 Phase C final pass, as "the application window disappeared" — wording that could not then separate a closed window from a dead process (Phase C reviewer defect 20) |
| **First Windows fault record** | 2026-09-15 21:42:46Z, pid 14920, during 003B |
| **First exit code** | 2026-09-16 21:48:55Z, `0xC000027B`, pid 32008, this wave |
| **Historical baseline** | Reproduced on `2301fe5` (before 003B) — `PREEXISTING_BEFORE_003B = true` |
| **Severity** | **S1** — an ordinary tab on an ordinary record ends the application; Attachments does it on every project. Not S0: nothing wrong is persisted. |
| **Confidence** | `ConfirmedDefect`, reproducibility `Always` |
| **Root-cause status** | **Established** (§5) |

## 2. What 003B had, and what it did not

| 003B had | 003B did not have |
| --- | --- |
| Eleven `APPLICATION_CLOSED` observations across five runs | An exit code for any of them |
| One Application Error event, `0xc000027b` in `Microsoft.UI.Xaml.dll` | A second one — the other ten closures left no event and no dump |
| A reproduction on the pre-003B source | Any record of *which step* the harness was on |

That last gap is the whole story. `DialogPass` selects a row, presses the opener,
and — when no dialog appears — walks every tab pressing it again. It captures
nothing until the end, so a process that died on the third tab was reported
against the dialog.

## 3. Reproduction on the starting executable

Product source at `b12dff9` is identical to `7d88659` (`git diff 7d88659 b12dff9
-- src` is empty); the client was rebuilt from it before any measurement.

### 3.1 Under the dialog pass, exactly as 003B ran it

`dialog-runtime --only <dialog> --minimal-env true`, one process per dialog:

| Dialog | Outcome | Exit code | pid | UTC |
| --- | --- | --- | --- | --- |
| `AttachToRoleDialog` | `APPLICATION_CLOSED` | `0xC000027B` | 32008 | 21:48:55 |
| `RecordPitchDialog` | `APPLICATION_CLOSED` | `0xC000027B` | 33664 | 21:57:38 |
| `RecordSubmissionDialog` | `APPLICATION_CLOSED` | `0xC000027B` | 27088 | 21:58:16 |

**3 of 3: `CRASH_REPRODUCED`.** No Application Error event and no WER dump were
written for any of the three — see §4.1.

### 3.2 Step by step, with a probe that checks the process after every step

A new Reviewer mode, `crash-probe`, walks an explicit path in a fresh client,
records whether the process is alive after each step, photographs the window
before the opener, and records the exit code. Selected results (each line is its
own process):

| Case | Path | Result |
| --- | --- | --- |
| Attach, real path | project with roles → Roles → role → **Attach** | **Dialog opened, process alive for the 10 s watch** |
| Pitch, real path | pursuit → Targets → target → **Record pitch** | **Dialog opened, alive** |
| Submission, real path | pursuit → Targets → target → **Record submission** | **Dialog opened, alive** |
| Attach, guard path | project with no roles → Roles → Attach | Refused ("Select a role first…"), alive |
| Bisect | … then → **Attachments** tab | **`0xC000027B` on the tab step** |
| C1: no opener at all | project → Roles → **Attachments** | **`0xC000027B`** |
| C3: project with roles | project → Roles → Attach (refused) → **Attachments** | **`0xC000027B`** |
| C4: other order | project → **Attachments** | **`0xC000027B`** |
| No project selected | **Attachments** | **`0xC000027B`** |

So the error bar, the guard, the dialog and the project data are all irrelevant:
**selecting the Attachments tab ends the process.**

### 3.3 Every detail tab, one process each

| Page | Tab | List | Empty? | Result |
| --- | --- | --- | --- | --- |
| Projects | Attachments | `AttachmentList` | always — nothing sets its `ItemsSource` | **exit `0xC000027B`** |
| Projects | Materials | `MaterialList` | yes | **exit** |
| Projects | Packages | `PackageList` | yes | **exit** |
| Projects | Activity | `HistoryList` | no project selected | **exit** |
| Projects | Activity | `HistoryList` | project with history | alive |
| Projects | Packages | `PackageList` | project with one package | alive |
| Projects | Companies, Source | inside a `StackPanel` | yes | alive |
| Pipeline | Pitches, Tasks | direct | yes | **exit** |
| Pipeline | Subjects, Activity | direct | no pursuit selected | **exit** |
| Pipeline | Subjects, Activity | direct | pursuit with rows | alive |
| Pipeline | Targets, Submissions | inside a `StackPanel` | — | alive |
| Deals | Tasks, Activity | direct | yes | **exit** |
| Contracts | Activity | direct | yes | **exit** |
| Contracts | Tasks | inside a `StackPanel` | yes | alive — negative control |

**Eleven sites, all the same markup, all fatal when empty.**

### 3.4 Why the pass reached those tabs

The pass's own detail lists the tabs it tries, in order (the repaired run shows
it: `Roles … | Attachments … | Companies …`). For Attach, Roles comes first and
the fixture's first project has no roles, so the opener is refused and the next
tab is **Attachments**. For both Pipeline dialogs the pass selects one row and
not the target, so the opener is refused, and after the two
`StackPanel`-wrapped tabs comes **Pitches**, which is empty.

Phase B opened all three at the first attempt (its detail carries no
"(on <tab>)" suffix) and never walked the tabs. Phase C added projects without
roles and pursuits without targets at the top of both lists. From then on, the
first attempt was refused.

## 4. Native and framework evidence

### 4.1 Windows Event Log and WER

One `Application Error` record for the client in four days:

```
2026-09-15T21:42:46Z  Event 1000  AgencyOS.Windows.exe 0.1.0.0
  faulting module  Microsoft.UI.Xaml.dll 3.2.3.0
  exception code   0xc000027b
  fault offset     0x00000000003a9c5d
  process id       14920
  runtime package  Microsoft.WindowsAppRuntime.2_2.4.0.0_x64
2026-09-15T21:42:54Z  Event 1001  APPCRASH  bucket 1299969890301595351  StackHash12_75e
```

That is the only one. Ten further closures in 003B and all thirty-four exits
with `0xC000027B` in this wave wrote no event and no dump. The exit code, read
from the process, is the reliable signal; the event log is not.

### 4.2 The minidump

`%LOCALAPPDATA%\CrashDumps\AgencyOS.Windows.exe.14920.dmp`, 16 MB, read with a
small parser over the minidump streams (no debugger is installed and none was):

```
exception code     0xC000027B  (STATUS_STOWED_EXCEPTION)
exception address  Microsoft.UI.Xaml.dll+0x3a9c5d, UI thread 31956
stowed exceptions  52
  [00] HRESULT 0x88000FA8  nested XAML
  [01] HRESULT 0x80040111  nested XAML
  [02..51] 50 × nested XAMW (XAML warning records), 76–196 stack words each
stack memory for the stowed records: not captured in the dump
```

**The dump is not copied into the repository and must not be.** Its environment
block holds every variable of the shell that launched the harness, including
credentials. `crash-probe` and `dialog-runtime --minimal-env true` now start the
client with only the variables Windows needs.

### 4.3 The managed exception

A temporary diagnostic build — reverted, preserved as
`evidence/DIAGNOSTIC-ONLY.patch` — added an `Application.UnhandledException`
handler that wrote to `%TEMP%`, and enabled
`DebugSettings.LayoutCycleTracingLevel = High`. Every crashing process wrote:

```
hr 0x802B0014 handled=False message=Layout cycle detected.  Layout could not complete.
Microsoft.UI.Xaml.LayoutCycleException
```

Eight processes across Projects, Pipeline, Deals and Contracts, all identical.

### 4.4 The cycle

WinUI's layout-cycle trace, read through the `OutputDebugString` protocol, for
Projects › Activity with nothing selected (`evidence/layout-cycle-summary.txt`):

| Iteration | Pass | What changed |
| --- | --- | --- |
| 7 | Measure | a `Grid` in the list's template becomes Visible; its desired size goes **0×0 → 14×26** |
| 6 | Arrange | the tab content, the outer `ScrollViewer` and `HistoryList` are arranged **362×26** |
| 5 | Measure | the same `Grid` becomes Collapsed; desired size **14×26 → 8×8** |
| 4 | Arrange | the same three arranged **362×14** |
| 3 | Measure | Visible again, **0×0 → 14×26** |
| 2 | Arrange | **362×26** |
| 1 | Measure | Collapsed, **→ 8×8** |
| 0 | Arrange | **362×14** — countdown exhausted |

Two states, alternating, until the countdown runs out. `8×8` matches the list's
`Padding="4"` on each side. The element that toggles is an unnamed `Grid` measured
alongside the `VerticalScrollBar` and `ScrollBarSeparator` template parts; the
trace does not say which of the two nested scroll viewers owns it, and this
document does not guess.

### 4.5 Where it stops

| Question | Answer |
| --- | --- |
| Exception code | `0xC000027B` |
| Stowed exception | XAML error `0x88000FA8`, nested `XAML`, plus 50 `XAMW` warnings |
| HRESULT to managed code | `0x802B0014` — `LayoutCycleException` |
| Faulting module | `Microsoft.UI.Xaml.dll` 3.2.3.0 (Windows App SDK 2.4.0) |
| Managed/native boundary | The layout pass is native. The exception is offered to `Application.UnhandledException`, which the client does not handle, and the framework fails fast. |
| Top relevant frames | Not recoverable: the dump holds no stack memory for the stowed records, and no symbols or debugger are installed. **No frame-level claim is made.** |
| XAML file and control | The eleven lists above; `HistoryList` by name in the trace |
| Last application code frame | None. No application code runs in the cycle. |
| Phase | **Layout — measure/arrange**, after a tab selection. Not construction, `InitializeComponent`, event hookup, binding, `XamlRoot`, `ShowAsync`, focus, rendering or teardown. |

## 5. Root cause

Each of the eleven sites was:

```xml
<TabViewItem Header="…">
    <ScrollViewer>
        <ListView x:Name="…" Padding="4"> … </ListView>
    </ScrollViewer>
</TabViewItem>
```

A `ListView` already has a `ScrollViewer` in its template. Wrapped in a second
one it is measured at infinite height. **Empty and padded**, its desired height
alternates between two values on successive passes, as a scroll-bar template
part is shown in one pass and hidden in the next. The tab content follows it —
26, 14, 26, 14 — until WinUI raises
`LayoutCycleException`. Nothing handles that, so the framework ends the process
with a stowed exception.

### 5.1 The variables, isolated

A temporary diagnostic build changed one factor per tab, all lists empty:

| Variant | Markup | Result |
| --- | --- | --- |
| V1 | `ScrollViewer > ListView` **without** `Padding` | alive |
| V2 | `ScrollViewer > StackPanel > ListView Padding="4"` | alive |
| V3 | `ListView Padding="4"` **with no outer** `ScrollViewer` | alive |
| V4 | original markup (control) | **exit, `LayoutCycleException`** |
| V5 | `ScrollViewer > Grid > ListView Padding="4"` | **exit, `LayoutCycleException`** |
| V6 | original markup (control) | **exit, `LayoutCycleException`** |

Padding is necessary; the outer viewer is necessary; emptiness is necessary; a
`StackPanel` in between prevents it and a one-child `Grid` does not.

## 6. Shared root

**`ONE_SHARED_ROOT`**, and the three dialogs are not part of it.

The assumption the brief warned against — that one exit code means one root —
turned out to be true here, but not for the reason it looked like: the three
dialogs share a root because **none of them is the root**. All three open and
survive on their real paths (§3.2), before and after the repair. What they share
is a harness that walks into the same eleven tabs.

## 7. Each opener, end to end

The same answers for all three; where they differ it says so.

| # | Question | Answer |
| --- | --- | --- |
| 1 | Does the handler execute? | Yes, on the real path. On the pass's first attempt it runs and its guard refuses (Attach: no role; Pitch/Submission: no target). |
| 2 | Does the constructor begin? | On the real path, yes. **At the moment of the crash, no dialog is being constructed** — the process dies on a tab selection. |
| 3 | Does it complete? | Yes. |
| 4 | Does `InitializeComponent` complete? | Yes. |
| 5 | Does a XAML-initialized property raise an event during construction? | `AttachToRoleDialog.PartyKindBox`, `RecordSubmissionDialog.MaterialBox` and the pitch combo boxes have no markup-initialized selection. Selection is set after `InitializeComponent`. §9. |
| 6 | Does a handler touch a later-declared control? | `ApplyPartyKind` guards `PartyBox is null` and runs after `InitializeComponent`. |
| 7 | A field before assignment? | `RecordPitchDialog` sets `MaterialBox.SelectedItem` before `_target`; the material box has no `SelectionChanged` handler, so nothing reads `_target` early. |
| 8 | Bindings or converters throwing? | None in the dialogs. The crash involves no binding. |
| 9 | Is `XamlRoot` valid? | Yes — the page's own. |
| 10 | Does `ShowAsync` begin? | On the real path, yes. |
| 11 | Another `ContentDialog` active? | No. |
| 12 | Before or after `ShowAsync`? | **Neither** — no dialog is involved. |
| 13 | Is focus involved? | No. The crash follows a selection made through UI Automation, with focus unchanged. |
| 14 | A control given invalid state? | No. Valid markup whose layout does not converge. |
| 15 | A COM object used after disconnect? | No evidence of it. |
| 16 | A collection modified during layout? | No. The failing lists are empty and unchanging. |
| 17 | An exception escaping an `async void` or native boundary? | Yes, in the framework's sense: `LayoutCycleException` reaches `Application.UnhandledException` unhandled. Not from application code. |

## 8. Working siblings

| Crashing (as reported) | Structurally similar and fine | Difference that matters |
| --- | --- | --- |
| `AttachToRoleDialog` | `AddProjectCompanyDialog` — same `InfoBar`, picker, `DatePicker` set to `UtcNow` | **None in the dialog.** Both are fine. The difference is the opener's guard: Add Company has none, so the pass never walks the tabs. |
| `RecordPitchDialog` | `AddOpportunityTargetDialog` | Same. Add Target's opener needs only a pursuit. |
| `RecordSubmissionDialog` | `AddOpportunityTargetDialog` | Same. |

The meaningful sibling comparison is between tabs, and §3.3 is that comparison:
`StackPanel`-wrapped lists survive empty, directly wrapped ones do not.

## 9. XAML initialization hazards

The 003A class — markup that raises an event during `InitializeComponent` into a
handler that touches a later-declared control — was checked in all three
dialogs:

| Dialog | Markup-initialized selection or value | Handler that could run early | Verdict |
| --- | --- | --- | --- |
| `AttachToRoleDialog` | none | `OnPartyKindChanged → ApplyPartyKind`, guarded | **absent** |
| `RecordPitchDialog` | none | `OnRequiredChanged` (TextChanged on `SummaryBox`) touches only `SummaryBox` | **absent** |
| `RecordSubmissionDialog` | `FollowUpBox IsChecked="True"`, no handler | `OnMaterialChanged` is empty | **absent** |

And empirically: each dialog constructs, shows and survives. **This class is
absent here.**

## 10. The window-size hypothesis

003B tested and rejected it; it was not reopened casually. The native evidence
points to a layout cycle inside one tab, whose trace shows the tab content at 362
pixels wide in both states — the window does not change. It is **not**
`AOS-R002-021`, which is a clipped command bar that never ends the process.

## 11. Fire-and-forget

| Path | Fire-and-forget? | Hides a managed exception? | Contributes to termination? |
| --- | --- | --- | --- |
| `OnAttachClick → _ = AttachAsync()` | yes | Would, if one were thrown — none is | **No** |
| `OnRecordPitchClick → _ = RecordPitchAsync()` | yes | same | **No** |
| `OnRecordSubmissionClick → _ = RecordSubmissionAsync()` | yes | same | **No** |
| Tab selection | not application code | — | The cycle is in WinUI layout; no application task is involved |

**Unrelated.** An exception inside those handlers is captured by the discarded
task and cannot end the process, which is itself why the termination had to be
native. No dispatch site was changed. The broader observation — 218 sites that
would swallow a real exception — stands as recorded by Audit 002.

## 12. The repair, and why it is the smallest correct one

All three isolating variants survived. Only one is correct on its own terms:

| Candidate | Stops the crash | Also right? |
| --- | --- | --- |
| Remove `Padding` (V1) | yes | Leaves a list measured at infinite height, so it cannot virtualize; one style change re-arms it |
| Wrap in `StackPanel` (V2) | yes | Same, and keeps two nested scrollers |
| **Remove the outer `ScrollViewer` (V3)** | **yes** | **Yes — the list scrolls itself and virtualizes again** |
| Handle `UnhandledException` | would hide it | **Forbidden by §13** — it would turn a layout failure into a blank tab |

Measured, not assumed, with 31 rows (`evidence/`):

| | Original markup | Repaired |
| --- | --- | --- |
| Who scrolls | the outer `ScrollViewer`, 27 % in view | `HistoryList` itself, 27 % in view |
| Scroll to end | last row visible | last row visible |
| Screenshot | — | no visible difference (the files differ at byte level; they were compared by eye, not by pixel) |

No exception handling was added anywhere. No dialog was changed for the crash.
003B's pickers were not reverted.
