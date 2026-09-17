# Repair Wave 003A.1 — runtime evidence

Everything here was measured on a running client against the synthetic LAB
tenant. The full run is under `artifacts/reviewer/run-repair-003a1/` (not
tracked); the files cited below are copies in [`evidence/`](evidence/).

## Environment

| | |
| --- | --- |
| Client | `AgencyOS.Windows.exe`, Debug, Windows App SDK 2.4.0 (`Microsoft.UI.Xaml.dll` 3.2.3.0) |
| API | local, `http://127.0.0.1:5199`, Development, fake providers only |
| Database | local LAB cluster — **runtime evidence only**; integration is authoritative on CI `postgres:18.6` |
| Tenant | `01a0a015-c44e-7f0e-9c4c-380e118b5e2b`, subject `w2-owner` |
| Window | 1600×1000 |
| Client environment | **minimal** — only the variables Windows needs, so a provoked dump holds no credentials |
| Isolation | every probe case starts its own client process |

Fixture writes made by this wave, all through the API: 30 stage changes on
"A Project With An Unusually Long Title…" (for a long Activity list); two
materials, "Repair 003A.1 reel" *Cut one* and *Cut two*, for talent "A"; a
TalentEngagement pursuit "Repair 003A.1 pursuit" with A24 as target, moved to
Contacted; one attachment made through the interface; one pitch and one
submission replayed through the API (§6).

---

## 1. Before — the starting executable

| | `AttachToRoleDialog` | `RecordPitchDialog` | `RecordSubmissionDialog` |
| --- | --- | --- | --- |
| Workspace | Projects | Pipeline | Pipeline |
| Record selected | first project (no roles) | first pursuit (target not selected) | first pursuit (target not selected) |
| Opener | "Attach" button | `pitch.record` from the palette | `submission.record` from the palette |
| Precondition the guard wants | a selected role | a selected target | a selected target |
| First attempt | refused: "Select a role first…" | refused: "Select a target first…" | refused: "Select a target first…" |
| Step that ended the process | selecting **Attachments** | selecting **Pitches** | selecting **Pitches** |
| Dialog frame appeared | no | no | no |
| Focus change | none | none | none |
| Process exit | **`0xC000027B`**, pid 32008 | **`0xC000027B`**, pid 33664 | **`0xC000027B`**, pid 27088 |
| Event log / WER | none written | none written | none written |
| Faulting module | `Microsoft.UI.Xaml.dll` (from the one recorded event, pid 14920) | same | same |
| Application log | the client has none; the API log shows only the list reads | same | same |
| Reviewer record | `baseline-pass-AttachToRoleDialog.json` | `baseline-pass-RecordPitchDialog.json` | `baseline-pass-RecordSubmissionDialog.json` |
| Step-by-step record | `baseline-attach-bisect.json`, `baseline-attach-controls.json` | `baseline-pipeline-tabs.json` | `baseline-pipeline-tabs.json` |
| Screenshot and tree before the fatal step | `baseline-7d88659-before-step.json` (§1.1) | same | same |

**Classification: `CRASH_REPRODUCED` × 3.** The same three dialogs, reached
through their real path without an empty tab on the way, **open and survive**
on the same executable (`baseline-attach-paths.json`,
`baseline-pitch-opener.json`, `baseline-submission-opener.json`).

### 1.1 The step before

A clean build of exactly `7d88659`, in its own worktree, with a screenshot and
tree taken immediately before the fatal step (`baseline-7d88659-before-step.json`):

| Case | Last step that survived | Fatal step | Exit |
| --- | --- | --- | --- |
| Attach path | project selected, Roles tab (empty) | Attachments | `0xC000027B` |
| Pitch path | pursuit selected, Targets, Submissions | Pitches | `0xC000027B` |
| Submission path | the same | Pitches | `0xC000027B` |
| Deals | page open | Tasks | `0xC000027B` |
| Deals | page open | Activity | `0xC000027B` |
| Contracts | page open | Activity | `0xC000027B` |
| Contracts, control | — | Tasks (`StackPanel`-wrapped) | alive |

The last three rows repeat three cases taken earlier on the same markup, whose
own record was overwritten by a later run written to the same directory; the
earlier console output, the screenshots, and the unhandled-exception log for
those processes (`0x802B0014`) survive.

On the same build, the pitch dialog's material picker for "Repair 003A.1
pursuit" offered **only "Nothing was shown"** — `AOS-R002-023` before its repair.
On the final build it offers "Nothing was shown", *Cut one* and *Cut two*
(`final-build-picker-and-movetarget.json`).

## 2. Historical — `2301fe5`, before 003B

Built in an isolated worktree, no source changed:

| Case | Result |
| --- | --- |
| Projects › Attachments | **exit `0xC000027B`** |
| Pipeline › Pitches | **exit `0xC000027B`** |
| Pipeline › Tasks | **exit `0xC000027B`** |
| Attach, real path (raw-identifier dialog) | opened, alive |
| Record pitch, real path | opened, alive |
| Record submission, real path | opened, alive |
| Dialog pass, each of the three | **`APPLICATION_CLOSED`, `0xC000027B`** |

**`PREEXISTING_BEFORE_003B = true`** (`historical-2301fe5-*.json`).

## 3. After — the eleven tabs

The final build, each tab empty, its own process (`final-build-probe.json`):

| Page | Tabs | Result |
| --- | --- | --- |
| Projects | Attachments, Materials, Packages, Activity | alive × 4 |
| Pipeline | Pitches, Subjects, Tasks, Activity | alive × 4 |
| Deals | Tasks, Activity | alive × 2 |
| Contracts | Activity | alive |

And a list that needs to scroll, 31 rows on Projects › Activity:

| | Original markup | Repaired |
| --- | --- | --- |
| Scrolls | the outer viewer, 27 % in view | the list itself, 27 % in view |
| Scrolled to the end | last row visible | last row visible |

## 4. After — the three dialogs

Each was reached through its real button **after first visiting every tab that
used to end the process**, then handed to the dialog pass's own inspection
(`final-build-probe.json`). Some of those tabs had rows by then, because this
wave's own writes landed on the same records; each tab was shown **empty** in its
own process in §3.

| | `AttachToRoleDialog` | `RecordPitchDialog` | `RecordSubmissionDialog` |
| --- | --- | --- | --- |
| Tabs visited first | Attachments, Materials, Packages, Activity | Pitches, Subjects, Tasks, Activity | Pitches, Subjects, Tasks, Activity |
| Process | alive | alive | alive |
| Title | Attach to role | Record pitch | Record submission |
| Focus enters the dialog | yes — `PartyKindBox` | yes — `SummaryBox` | yes — the Sent date |
| Tab stays inside | yes | yes | yes |
| Shift+Tab stays inside | yes | yes | yes |
| Escape closes | yes | yes | yes |
| Focus afterwards | the Attach button | the Record pitch button | the Record submission button |
| Accessibility findings | 0 | 0 | 0 |

The pass classifies the focus result as `ELSEWHERE_ON_PAGE` rather than
`RESTORED_TO_OPENER`, because the probe presses the button through UI Automation,
which does not move focus to it first. Focus lands on the button itself.

### 4.1 The whole dialog inventory

The dialog pass over all 63 dialogs on the final build
(`final-build-full-dialog-pass.json`): **no exit**. 52 opened, every one with
focus inside, Escape closing it and no accessibility findings; 4 are unreachable
because nothing constructs them; 7 were not opened by the pass — three for
preconditions Audit 002 already recorded, and four refused by their own guard
because the pass does not select the target or offer they need.

`MoveTargetDialog` — the other Pipeline dialog Phase C recorded as a closure —
was opened through its button on the final build after visiting Pitches and
Tasks, and passed the same inspection. `AnswerOfferDialog` needs an offer on the
table and no deal in the fixture has one; its guard says so.

## 5. 003B's work on these three dialogs

The runtime verification 003B could not do.

| Check | `AttachToRoleDialog` | `RecordPitchDialog` | `RecordSubmissionDialog` |
| --- | --- | --- | --- |
| No typed identifier | `PartyBox` is a `ComboBox`; no `Edit` identifier field | `MaterialBox` is a `ComboBox` | `MaterialBox` is a `ComboBox` |
| Accessible name | "Who" (becomes "Which company" for companies) | "Material shown" | "Material" |
| Offers real choices | people and companies | **only after `AOS-R002-023` was repaired** — *Cut one*, *Cut two* | same |
| Default | nothing selected; Attach disabled | "Nothing was shown" | "Nothing attached" |
| Keyboard selection | Alt+Down, Down, Enter → "Bartholomew-Fitzgerald Pemberton-Featherstonehaugh III · …" | Alt+Down, Down, Enter → *Cut one* | Alt+Down, Down, Down, Enter → *Cut two* |
| Canonical identifier sent | `personId 01a0a015-c783-…` | `materialId 01a0ac56-9aa9-…` (*Cut one*) | `materialId 01a0ac56-9bbe-…` (*Cut two*) |
| How it was confirmed | read back from the saved attachment | from the request body (§6) | from the request body (§6) |
| Saved through the interface | **yes** — attachment `01a0ac60-1490-…` | **no — `500`, `AOS-R002-001`** | **no — `500`, `AOS-R002-001`** |

### 5.1 `AOS-R002-023` — found here

Completing this verification turned up a defect in 003B's own work. A pursuit
names talent by **profile**; materials are listed by **person**; 003B asked with
the profile's identifier:

```
GET /talent/01a0a015-cb53-…/materials   (the subject's target: a profile)   →  0 items
GET /talent/01a0a015-c71e-…/materials   (the same talent's person)          →  2 items
```

So both pickers offered only "nothing" for every real pursuit. After the repair
they list the subject's materials, and the keyboard selections above are drawn
from them. The `7d88659` picker, on the same pursuit, is in §1.1.

## 6. Why the pitch and submission were not saved through the interface

Both requests reached the server and failed with `500`:

```
Cannot write DateTimeOffset with Offset=03:00:00 to PostgreSQL type
'timestamp with time zone', only offset 0 (UTC) is supported.
```

That is **`AOS-R002-001`** (S1, open, not this wave's). It already lists
`RecordPitchDialog`; `RecordSubmissionDialog` is the same. This machine is
UTC+3, so neither dialog can save.

To confirm the identifier the pickers send without repairing that finding, the
client was pointed at a local recording proxy for one run
(`request-bodies.jsonl`). The captured bodies were then replayed to the API with
**only the timestamps converted to UTC** (`utc-replay.json`):

| | Material chosen | `materialId` in the body | Replay | Material read back |
| --- | --- | --- | --- | --- |
| Pitch | *Cut one* | `01a0ac56-9aa9-7b2d-8e02-99d574103c31` | `201` | `01a0ac56-9aa9-7b2d-8e02-99d574103c31` |
| Submission | *Cut two* | `01a0ac56-9bbe-77ad-a2fc-2e9a432d84d7` | `201` | `01a0ac56-9bbe-77ad-a2fc-2e9a432d84d7` |

An earlier attempt was refused by the domain, correctly — the new target had
not been approved or approached (`repaired-keyboard-saves-first-attempt.json`).
The page showed "Invalid request" rather than the reason, which is
`AOS-R002-024`.

## 7. Security and tenant semantics

`security-matrix.json`. The repair changes markup and one client lookup; the
server was re-tested anyway.

| Case | Attach | Pitch | Submission |
| --- | --- | --- | --- |
| Unauthenticated | 401 | 401 | 401 |
| Read-only observer | 403 | 403 | 403 |
| Authenticated non-member (`review-elsewhere`) | 403 | 403 | 403 |
| Cross-tenant route (member of both, other org's path) | 404 | 404 | 404 |
| Stale `expectedVersion` | 409 | 409 | 409 |
| Invented target | 400 "That role does not belong to this project." | 404 | 404 |
| Invented person / material | 404 | 404 | 404 |

| Picker source | Unauthenticated | Observer | Non-member | Owner |
| --- | --- | --- | --- | --- |
| people | 401 | 200 | 403 | 200 |
| companies | 401 | 200 | 403 | 200 |
| talent roster (new use) | 401 | 200 | 403 | 200 |
| talent materials | 401 | 200 | 403 | 200 |

Reading organization A's talent materials through organization B's route
returns `[]` — nothing leaks. Organization B holds no people, so "a person from
another tenant" could not be tried; the cross-tenant route cases cover that
boundary. The observer can load every picker and can save nothing: a list that
loads is not a permission.
