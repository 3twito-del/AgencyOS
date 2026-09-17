# Repair Wave 003A.1 — the three dialogs that ended the client

**Wave:** AgencyOS Review Repair Wave 003A.1
**Scope:** the crash Repair Wave 003B observed in `AttachToRoleDialog`,
`RecordPitchDialog` and `RecordSubmissionDialog`, and completing 003B's runtime
verification of those three. Nothing else.
**Explicitly out of scope:** 003C–003F, 003B's six deferred identifier fields,
`AOS-R002-020`, `AOS-R002-021`, the 218 fire-and-forget dispatch sites, broad
XAML cleanup.
**Branch:** `repair-wave-001`
**Date:** 2026-09-17

**The dialogs were never the fault.** Eleven detail tabs ended the process when
their list was empty, and the review harness — walking tabs after the dialogs'
first attempt was refused — recorded the dialog it was on. The tabs are
repaired, the three dialogs are verified live, and completing that verification
found and repaired a defect in 003B's own material pickers.

Detail: [`REPAIR-003A1-ROOT-CAUSE.md`](REPAIR-003A1-ROOT-CAUSE.md) ·
[`REPAIR-003A1-RUNTIME.md`](REPAIR-003A1-RUNTIME.md) ·
[`REPAIR-003A1-FINDINGS.json`](REPAIR-003A1-FINDINGS.json) ·
[`evidence/`](evidence/)

---

## 1. Starting product executable

`7d88659` — "Twenty fields asked for a value the product shows nowhere; fourteen
no longer do". The client was rebuilt from source before any measurement;
`git diff 7d88659 b12dff9 -- src` is empty.

## 2. Starting final tip

`b12dff9` — docs only. Authoritative CI `35030384303` on `7d88659`, both jobs
green.

## 3. Finding identifier

**`AOS-R002-022`**, S1, `ConfirmedDefect`, reproducibility `Always`. 003B had
deliberately not numbered it; the register ended at `AOS-R002-021`. One
identifier for the crash class — the eleven sites are one defect.

Two more were assigned in this wave: `AOS-R002-023` (S2, repaired, §37) and
`AOS-R002-024` (S3, deferred, §37).

## 4. The three affected dialogs

`AttachToRoleDialog`, `RecordPitchDialog`, `RecordSubmissionDialog` — as
reported. As measured, the affected surfaces are eleven tabs:

| Page | Tabs |
| --- | --- |
| Projects | Attachments, Materials, Packages, Activity |
| Pipeline | Pitches, Subjects, Tasks, Activity |
| Deals | Tasks, Activity |
| Contracts | Activity |

## 5. Reproduction on the current tip

| Dialog | Under 003B's dialog pass | Classification |
| --- | --- | --- |
| `AttachToRoleDialog` | exit `0xC000027B`, pid 32008 | `CRASH_REPRODUCED` |
| `RecordPitchDialog` | exit `0xC000027B`, pid 33664 | `CRASH_REPRODUCED` |
| `RecordSubmissionDialog` | exit `0xC000027B`, pid 27088 | `CRASH_REPRODUCED` |

Then step by step, one process per case, with the process checked after every
step: **each dialog opens and survives when reached directly**; the process
ends on selecting an empty tab, with no dialog involved. Runtime §1, Root cause
§3.

## 6. Historical pre-003B reproduction

`2301fe5`, built in an isolated worktree, source untouched: the same tabs exit
with `0xC000027B`, the three dialogs open directly, and the pass records all
three closures. **`PREEXISTING_BEFORE_003B = true`.** The markup at the eleven
sites dates from milestones M5–M8; older commits were not run and no claim is
made about them.

## 7. Event log and WER

One record in four days — `Application Error` 1000, pid 14920,
`Microsoft.UI.Xaml.dll` 3.2.3.0, `0xc000027b`, offset `0x3a9c5d` — and its WER
bucket event. **None of the 34 exits in this wave, and none of 003B's other ten,
wrote an event or a dump.** The process exit code, now recorded by the harness,
is the reliable signal. `evidence/event-log-agencyos-windows.json`.

## 8. Stowed-exception details

From the one minidump, read by a small parser over its streams (no debugger was
installed, and none was added):

| | |
| --- | --- |
| Exception | `0xC000027B`, UI thread, `Microsoft.UI.Xaml.dll+0x3a9c5d` |
| Stowed records | 52 — `0x88000FA8` (XAML), `0x80040111` (XAML), 50 XAML warnings |
| Stack memory | not captured in the dump; **no frame-level claim is made** |
| Managed exception | `Microsoft.UI.Xaml.LayoutCycleException`, `0x802B0014`, "Layout cycle detected. Layout could not complete.", unhandled — from a temporary, reverted diagnostic build |
| Phase | layout (measure/arrange) after a tab selection |

**The dump was not copied and must not be.** Its environment block contains the
credentials of the shell that launched the harness. It remains only in
`%LOCALAPPDATA%\CrashDumps`. The harness now starts a client it expects to crash
with a minimal environment.

## 9. Root cause per dialog

The same for all three, because none of them is the cause. Each site was a
`ListView` with `Padding="4"` as the whole content of a `ScrollViewer`. Empty,
its desired height alternates between two values on successive layout passes;
WinUI raises `LayoutCycleException`; nothing handles it; the framework ends the
process. The harness reached those tabs because the dialogs' guards refused its
first attempt (no role selected; no target selected).

## 10. Shared-root determination

**`ONE_SHARED_ROOT`** — by direct evidence, not by the shared exit code: six
single-variable variants, eleven tabs each tested alone, and the layout trace.
Root cause §5–§6.

## 11. Working-sibling comparison

The dialogs have no meaningful difference from `AddProjectCompanyDialog` or
`AddOpportunityTargetDialog`, which never "crashed": those openers have no
selection guard, so the harness never walked the tabs. The comparison that
explains the defect is between tabs — lists inside a `StackPanel` survive empty,
lists directly in the viewer do not. Contracts › Tasks is the negative control.

## 12. XAML initialization analysis

The 003A class — markup raising an event into a handler that touches a
later-declared control — was checked in all three dialogs and is **absent**. No
markup-initialized selection feeds a handler; `ApplyPartyKind` is guarded;
`OnMaterialChanged` is empty. Each dialog constructs, shows and survives.

## 13. Layout hypothesis disposition

The window-size hypothesis stays rejected, and was not reopened: the cycle is
inside one tab, at the same 362-pixel width in both states. It is **not**
`AOS-R002-021`, which clips a command bar and never ends the process.

## 14. Fire-and-forget relevance

**Unrelated.** The three openers are `_ = XAsync()`, which would swallow a
managed exception — none is thrown. The termination comes from a layout pass,
not from an application task. No dispatch site was changed; the 218-site
observation stands as recorded.

## 15. Exact files changed

**Product — the crash (`AOS-R002-022`):**

| File | Change |
| --- | --- |
| `src/AgencyOS.Windows/Pages/ProjectsPage.xaml` | outer `ScrollViewer` removed from Attachments, Materials, Packages, Activity |
| `src/AgencyOS.Windows/Pages/PipelinePage.xaml` | … from Pitches, Subjects, Tasks, Activity |
| `src/AgencyOS.Windows/Pages/DealsPage.xaml` | … from Tasks, Activity |
| `src/AgencyOS.Windows/Pages/ContractsPage.xaml` | … from Activity |

**Product — 003B's material pickers (`AOS-R002-023`):**

| File | Change |
| --- | --- |
| `src/AgencyOS.Client/ViewModels/SubjectMaterials.cs` | new: which people a pursuit's talent subjects are |
| `src/AgencyOS.Windows/Pages/PipelinePage.xaml.cs` | `SubjectMaterialsAsync` asks by person; a misplaced doc comment restored to `Guarded` |

**Tests:** `tests/AgencyOS.Tests.Windows/Layout/SelfScrollingListTests.cs`,
`tests/AgencyOS.Tests.Windows/Dialogs/SubjectMaterialsTests.cs`,
`tests/AgencyOS.Tests.Reviewer/CrashProbeTests.cs`.

**Reviewer — narrow evidence capture only:** `Runtime/CrashProbe.cs` (new),
`Runtime/ReviewApp.cs` (exit code; opt-in minimal environment),
`Runtime/DialogPass.cs` (exit code in the closure record; `Describe` for a
dialog opened elsewhere), `Runtime/OpenerProbe.cs` (palette routine made
internal), `Program.cs` (`crash-probe` mode, `--minimal-env`). The harness
observes the crash; it does not restart, retry or rescue.

## 16. Minimal repair rationale

Three isolating variants stopped the crash. Removing the outer viewer is the one
that is also right on its own terms: a list scrolls itself, and a list measured
at infinite height cannot virtualize. Removing only the padding, or wrapping in a
`StackPanel`, would leave nested scrollers and a trap one style change away.
**No exception handling was added.** A handler for `UnhandledException` would
have turned a layout failure into a blank tab, which §12–§13 of the brief rule
out. No dialog was changed for the crash, no capability was removed, no window
size requirement changed.

## 17. Runtime before and after

| | Before (`7d88659`) | After (final build) |
| --- | --- | --- |
| Eleven tabs, empty | 11 × exit `0xC000027B` | 11 × alive |
| 31-row list | scrolls (outer viewer) | scrolls (the list itself) |
| Dialog pass on the three | 3 × `APPLICATION_CLOSED` | no exit |
| Three dialogs through their buttons | open (when no empty tab is on the way) | open after visiting every formerly fatal tab |
| Full dialog pass, every dialog | — | **63 dialogs, 0 exits** — 52 opened, all with focus inside, Escape closing and 0 accessibility findings; 4 unreachable (nothing constructs them); 7 not opened by the pass (below) |

The seven the pass did not open are not exits. `ApproveAiActionDialog` and
`IngestAttachmentDialog` need externally originated state and
`ResolveParticipantDialog` a fixture gap, as Audit 002's closure recorded.
`RecordPitchDialog`, `RecordSubmissionDialog`, `MoveTargetDialog` and
`AnswerOfferDialog` were refused by their guards, because the pass selects one
row and not the target or offer they need; the first three were then opened
through their real path on the same build, and no deal currently has an offer on
the table for the fourth. `AnswerOfferDialog` and `MoveTargetDialog` were the
other two dialogs Phase C's final pass recorded as closures.

## 18. 003B UX preserved

Not reverted, and now verified live: `PartyBox` ("Who" / "Which company") and
both `MaterialBox` pickers are combo boxes with no typed identifier. The
material pickers only became useful in this wave (§37).

## 19. Representative mutation

| Workflow | Through the interface | Identifier persisted |
| --- | --- | --- |
| Attach to role | **saved** — attachment `01a0ac60-1490-…` | `personId` = the person chosen by keyboard |
| Record pitch | `500` — `AOS-R002-001` | request body carried *Cut one*; replayed with UTC timestamps → `201`, read back identical |
| Record submission | `500` — `AOS-R002-001` | request body carried *Cut two*; replayed with UTC timestamps → `201`, read back identical |

`AOS-R002-001` (S1, open) is the only reason the last two could not be saved
through the interface on this UTC+3 machine. It was not repaired here.

## 20. Keyboard and focus

All three: focus enters the dialog, Tab and Shift+Tab stay inside, Escape
closes, focus lands on the opener button. Each picker was operated with
Alt+Down, Down and Enter.

## 21. Accessibility

Zero findings from the dialog pass's detectors on all three. Accessible names:
"Who", "Material shown", "Material".

## 22. Security and tenant regression

For all three writes: unauthenticated 401, read-only observer 403,
authenticated non-member 403, cross-tenant route 404, stale version 409,
invented target or record 404 (an invented role on a real project is 400 with a
message that reveals nothing). Every picker source, including the talent roster
this wave now reads: observer 200, non-member 403, unauthenticated 401. A
cross-tenant materials read returns `[]`. Runtime §7.

## 23. Structural guard

`SelfScrollingListTests` — **added**, because the root is a reusable markup
shape:

- no `ListView`/`GridView` is the whole content of a `ScrollViewer`, directly or
  as the only child of a `Grid` (the variant that also crashed);
- the eleven repaired lists are their tab's own content;
- the rule, on hand-built markup, flags the three shapes that crashed and passes
  the four that did not;
- five real lists in surviving shapes — `StackPanel`-wrapped, and the many-row
  page grids on Organization and Sync — are not flagged.

It pins the markup. The crash itself needs a live WinUI window; the
process-level proof is the probe evidence.

## 24. Mutation-test evidence

| Faulty code put back | Result |
| --- | --- |
| The four pages' original markup | **12 failures** — the guard names all eleven sites, and each of the eleven pins fails |
| `SubjectMaterials.People` returning the profile identifier | **2 failures** |
| Both restored | 31 / 31 pass |

And at process level: the same probe cases exit `0xC000027B` on `7d88659` and
survive on the final build.

## 25. Schema impact

**NO CHANGE.** No migration, no entity, no configuration.

## 26. OpenAPI impact

**NO CHANGE.** Regenerated by the contract gate: OpenAPI 3.1.1, **263 paths /
187 schemas**, byte-identical to the document saved before Repair Wave 003B's
changes were built (sha256 `942d0e74…` both). No file under `AgencyOS.Api`,
`.Contracts`, `.Application`, `.Domain` or `.Infrastructure` differs from
`7d88659`.

## 27. Contract impact

**NO CHANGE.** API contract **14**.

## 28. Unit count

**3,755** — unchanged. The new client helper is tested in the Windows suite,
which is the one that references `AgencyOS.Client` alongside the markup.

## 29. Windows count

**942** (was 911): `SelfScrollingListTests` 24, `SubjectMaterialsTests` 7.

## 30. Reviewer count

**163** (was 152): `CrashProbeTests` 11.

## 31. Integration count

**821 / 821** — unchanged. No server code changed; the suite was run anyway.

## 32. `postgres:18.6` evidence

CI run `35242439491`, job "Integration tests (PostgreSQL 18.6)": service image
`postgres:18.6` pulled and started, **821 passed, 0 failed**. Nightly run
`35242443163` ran the same suite against the same image as its gate. The local
cluster used for runtime evidence is not authoritative and no integration result
here comes from it.

## 33. TLA+ result

**4 / 4** — `OfflineWriteQueue`, `OutboundSend`, `AiApproval`, `LocalInferenceLease`; locally and in CI.

## 34. Executable commit

**`a728324`** — "The pitch and submission material pickers ask for a person's
materials", on top of **`5d4fe06`** — "An empty list on eleven detail tabs ended
the client; those tabs no longer do". Two commits so that the `AOS-R002-023`
repair can be taken or left separately.

## 35. Final tip

The documentation commit that adds this report, directly on `a728324`. It changes
nothing outside `docs/`, so the executable tip above is the one CI measured.

## 36. Authoritative CI

**CI `35242439491` on `a728324` — success, both jobs.**

| Gate | Before (`7d88659`) | After (`a728324`) |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,755 | **3,755** |
| Windows | 911 | **942** |
| reviewer | 152 | **163** |
| integration | 821 / 821 | **821 / 821 vs `postgres:18.6`** |
| OpenAPI | 263 / 187 | **263 / 187** |
| API contract | 14 | **14** (release manifest) |
| schema | `20260909072201_AiResultClassification` | **unchanged** |
| TLA+ | 4 / 4 | **4 / 4** |

**Nightly `35242443163` on `a728324` — success, both jobs**: integration against `postgres:18.6`, and "Nightly artifact (Windows)", the packaged client. Dispatched rather than
reasoned about: the NIGHTLY ring packages the Windows client, and this wave
changes what that client does.

## 37. New findings recorded

| Finding | Severity | Disposition | What |
| --- | --- | --- | --- |
| `AOS-R002-022` | S1 | **REPAIRED** | this wave's crash |
| `AOS-R002-023` | S2 | **REPAIRED** | 003B's material pickers asked for materials with a talent *profile* identifier; the server lists them by *person*, so both only ever offered "nothing". Found by completing 003B's runtime verification, which §15 of the brief requires, and repaired because that verification cannot pass without it. Client only, one roster read the create dialog already makes, no contract change. In its own commit, so it can be separated. |
| `AOS-R002-024` | S3 | **DEFERRED** (suggested 003C) | Projects and Pipeline show "Invalid request" where six other pages show the server's reason. A different root, and it neither causes nor blocks the crash. |

Also recorded, not new: `RecordSubmissionDialog` is affected by `AOS-R002-001`
as well as `RecordPitchDialog`. Added to `AUDIT-002-AGGREGATE.md` §7.

**On scope.** The Deals and Contracts sites were repaired with the other eight.
They are not on the three dialogs' paths, but they are the same crash class
under the same identifier, the change is the same one line of markup, and
leaving three known process terminations behind a guard that has to exempt them
would have been worse. Each was reproduced before it was changed.

## 38. Confirmation: no 003C–003F work occurred

None. No file belonging to `AOS-R002-020`, `AOS-R002-021`, 003B's six deferred
identifier fields, or any 003C–003F finding was edited. `AOS-R002-001` and
`AOS-R002-024` were recorded and not touched. No dispatch site, no dialog
framework, no navigation, no product logging. Phase D's `BoundedCapture` and
disk-safety gate are untouched. Audit 002 and 003B history were not rewritten;
both have additive notes.

---

## Closing

```
REPAIR WAVE 003A.1 = CLOSED
```

| | |
| --- | --- |
| Finding | `AOS-R002-022` — S1 — **REPAIRED** |
| Executable commit | `a728324` (with `5d4fe06`) |
| Authoritative CI | `35242439491`, success, both jobs |
| Nightly | `35242443163`, success, both jobs |
| Root cause | an empty, padded `ListView` as the whole content of a `ScrollViewer` → `LayoutCycleException` → fail-fast `0xC000027B` |
| Shared root | `ONE_SHARED_ROOT`; the three dialogs are not part of it |
| Historical baseline | `PREEXISTING_BEFORE_003B = true` (`2301fe5`) |
| 003B runtime verification | complete for all three |
| Schema / OpenAPI / contract | no change / no change (byte-identical) / no change (14) |
| New findings | `AOS-R002-023` repaired, `AOS-R002-024` deferred; `AOS-R002-001` scope note |
| Audit 002 | remains **COMPLETE** |
| 003C–003F | not begun |

### Left for the owner

- **The minidump.** `%LOCALAPPDATA%\CrashDumps\AgencyOS.Windows.exe.14920.dmp`
  holds the environment of the shell that launched the harness during 003B,
  including credentials. It was read for exception metadata only and copied
  nowhere. Deleting it, and rotating whatever credentials that shell carried, is
  the owner's decision.
- **`AOS-R002-001`** blocks saving a pitch or a submission on any machine outside
  UTC.
