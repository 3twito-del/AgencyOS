# Repair Wave 003D — local work ledger

Every file this wave touched, and why. Recorded here rather than left to be
reconstructed from history, because no Git operation was performed during this
programme.

Baseline for the wave: the working tree as 003C left it (executable `ada2310`
plus 003C's changes).

---

## Production code

| File | Finding | Purpose |
| --- | --- | --- |
| `src/AgencyOS.Windows/Pages/PeoplePage.xaml` | `AOS-R002-012` | Adds a bar with no title of its own, for whatever was refused. The list's bar keeps its load title and its load. |
| `src/AgencyOS.Windows/Pages/PeoplePage.xaml.cs` | `AOS-R002-012` | A refused create is shown under "Could not create the person" instead of "Could not load people". |
| `src/AgencyOS.Windows/Pages/CompaniesPage.xaml` | `AOS-R002-012` | The same bar, plus a detail bar titled "Could not load this company" — the page had none, which is why a single company that could not be read was reported as the directory failing. |
| `src/AgencyOS.Windows/Pages/CompaniesPage.xaml.cs` | `AOS-R002-012` | A refused create is shown under "Could not create the company"; a detail that cannot be read is shown in the detail bar. |
| `src/AgencyOS.Windows/Dialogs/AddOpportunityTargetDialog.xaml.cs` | `AOS-R002-011` | `ContactBox` declares that `ContactHint` describes it. |
| `src/AgencyOS.Windows/Dialogs/AddPackageElementDialog.xaml.cs` | `AOS-R002-011` | `TargetBox` → `TargetHint`. |
| `src/AgencyOS.Windows/Dialogs/CreateOpportunityDialog.xaml.cs` | `AOS-R002-011` | `SubjectBox` → `SubjectHint`. |
| `src/AgencyOS.Windows/Dialogs/LinkResearchItemDialog.xaml.cs` | `AOS-R002-011` | `ItemBox` → `ItemHint`. |
| `src/AgencyOS.Windows/Dialogs/RecordSubmissionDialog.xaml.cs` | `AOS-R002-011` | `MaterialBox` → `SnapshotHint`. |

**No message text was rewritten**, no permission, role, tenant rule or status code
was touched, and nothing was moved into or out of a dialog. `AOS-R002-001`'s
converter, its OpenAPI restoration and its temporal tests were not touched.

`MainWindow.xaml` was **not** changed: see the admission for why `AOS-R002-018`
needs no repair.

## Tests

| File | Covers |
| --- | --- |
| `tests/AgencyOS.Tests.Windows/Presentation/RefusalDestinationTests.cs` (new, 9) | `AOS-R002-012`: a bar titled for a load is written to only while loading; a bar markup leaves untitled is given a title where it is shown; the two repaired creates name the create; and a positive/negative control for the rule, including the delegation case. |
| `tests/AgencyOS.Tests.Windows/Presentation/RowNameStructureTests.cs` (new, 2) | `AOS-R002-018`: all 105 row templates name their row, and the palette is one of them. Pins the property that stops the finding recurring. |
| `tests/AgencyOS.Tests.Windows/Presentation/FieldDescriptionTests.cs` (new, 7) | `AOS-R002-011`: the five associations exist, both ends of every association are controls the markup declares, and the product declares the pattern at all. |

No unit, integration or contract test was added: nothing below the client
changed.

## Reviewer

| File | Purpose |
| --- | --- |
| `tools/AgencyOS.Reviewer/Runtime/CrashProbe.cs` | `read:` now reports the text beneath a control. An `InfoBar` announces nothing itself — its heading and its sentence are separate children — so reading the bar alone said the refusal was blank when it was not. |

The `palette:` step used for the `AOS-R002-018` measurement was added earlier in
this wave and is already recorded in this file's predecessor for 003A.1's probe.

## Documentation

| File | Purpose |
| --- | --- |
| `docs/reviews/repair-003d/REPAIR-003D-ADMISSION.md` | What is admitted, what is not, and the `AOS-R002-018` measurement. |
| `docs/reviews/repair-003d/REPAIR-003D-LOCAL-WORK.md` | This ledger. |
| `docs/reviews/repair-003d/REPAIR-003D-REPORT.md` | The wave's report. |

## Temporary probes

**None.** No file was created and deleted during this wave.

## Runtime evidence

`artifacts/reviewer/run-repair-003d-local/`

| Path | What |
| --- | --- |
| `palette-before/`, `palette-all/` | The `AOS-R002-018` measurement: one row, then all 46, with the full automation tree. |
| `refusal-people/` | A real create refusal through the client: a 129-character first name, refused by the server, shown under "Could not create the person". |
| `refusal-companies/` | The same on Companies: a 257-character name, shown under "Could not create the company". |
| `described/`, `described-2/` | The wired dialogs opening, with their hints read from the live tree. |
| `api.log` | Server log for the runs — the tail, because EF's command logging reached 2.1 GB. Contains no credential or connection string. |

## What was measured and left alone

- **`CompaniesPage.LoadDetailAsync`** now writes to a detail bar rather than the
  list bar. It could not be driven live: no persona is refused a company detail
  or its timeline (`review-restricted`, `review-observer`, `review-outsider` all
  answer `200`), and forcing one would have meant destroying LAB data. It is
  covered structurally, and this is recorded rather than implied.
- **`LinkResearchItemDialog`** was not opened live: it needs a research case
  selected first. The other four wired dialogs were opened and their hints read.

---

## Addendum — files changed for the `AOS-R002-010` decision

Added 2026-09-18, after the owner resolved `AOS-R002-010`. Additive: nothing above
was withdrawn.

### Production code

| File | Finding | Purpose |
| --- | --- | --- |
| `src/AgencyOS.Client/Presentation/DialogRefusal.cs` (new) | `AOS-R002-010`, `AOS-R002-011` | The whole decision: whether a refusal is recoverable, what to say, and which field it is about. In the client assembly so a test can execute it. |
| `src/AgencyOS.Windows/Dialogs/RefusalSurface.cs` (new) | `AOS-R002-010`, `AOS-R002-011` | The control wiring: put the message in the dialog's bar, declare that the field is described by it, move focus there, and take it all down when the entry changes. Decides nothing. |
| `src/AgencyOS.Windows/Dialogs/NewPersonDialog.xaml(.cs)` | both | Owns the create, cancels the close on a recoverable refusal, registers its six fields. |
| `src/AgencyOS.Windows/Dialogs/NewCompanyDialog.xaml(.cs)` | both | The same, five fields. |
| `src/AgencyOS.Windows/Dialogs/AddProjectRoleDialog.xaml(.cs)` | both | The same, three fields, carrying the project and the version the operator saw. |
| `src/AgencyOS.Windows/Pages/PeoplePage.xaml.cs` | `AOS-R002-010` | Reports only what the dialog handed back as terminal. |
| `src/AgencyOS.Windows/Pages/CompaniesPage.xaml.cs` | `AOS-R002-010` | The same. |
| `src/AgencyOS.Windows/Pages/ProjectsPage.xaml.cs` | `AOS-R002-010` | The same. |

**No server file was changed.** No endpoint, contract, migration, permission or
status code. Nothing about server-side validation or authorization was weakened,
and no server detail beyond the problem's own `detail` reaches the operator.

### Tests

| File | Covers |
| --- | --- |
| `tests/AgencyOS.Tests.Unit/Refusals/DialogRefusalTests.cs` (new, 23) | The decision, executed: no refusal, the four recoverable statuses, the five terminal ones, the reason rather than the title, the field named for both refusal shapes, and — the one that matters most — a refusal about the whole submission pinned to no field at all. |
| `tests/AgencyOS.Tests.Windows/Presentation/StayOpenRefusalTests.cs` (new, 22) | The wiring: each dialog owns its mutation, cancels the close, hands back what it cannot answer, clears on edit, has a titled place to say it, registers every field it offers, and is not second-guessed by its page. |

### Mutation checks

Both were applied to the real rule, run, and reverted.

| Mutation | Result |
| --- | --- |
| `KeepsTheDialogOpen` always `false` — the behaviour before the decision | **13 of 23 fail** |
| Every refusal pinned to the first field — the mistake the decision warns against | **4 of 23 fail** |

### Reviewer

| Step | Purpose |
| --- | --- |
| `describedby:` | Reads UI Automation property 30105. It reports, precisely, that this managed client has no such property, which is a limit of the instrument rather than a finding about the product. |
| `set:` | Replaces a control's value, which is how a correction is made; it raises the same change notification an operator's typing does. |
| `focused` | Where focus is, without moving it. |

### Runtime evidence

`artifacts/reviewer/run-repair-003d-local/`

| Path | What |
| --- | --- |
| `stay-open-person/` | A refused create, the dialog still open, values retained, focus on the offending field, the correction clearing the message, and a retry that succeeded. |
| `stay-open-company/` | The same, finished by keyboard. |
| `stay-open-role/` | The same on a project role. |
| `stay-open-focus/` | Focus before and after the refusal. |
