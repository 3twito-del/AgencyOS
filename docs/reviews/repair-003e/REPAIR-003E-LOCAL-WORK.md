# Repair Wave 003E — local work ledger

Every file this wave touched, and why. Recorded here rather than left to be
reconstructed from history, because no Git operation was performed during this
programme.

Baseline for the wave: the working tree as 003D left it.

---

## Production code

| File | Finding | Purpose |
| --- | --- | --- |
| `src/AgencyOS.Client/AgencyOsApiClient.cs` | `AOS-R001-010` | Four methods for the four endpoints that had no caller: add a scope, end one, assign a team member, remove one. Interface and implementation. |
| `src/AgencyOS.Client/ViewModels/RepresentationMaintenance.cs` (new) | `AOS-R001-010` | What may be offered, given what is already true: an area already represented cannot begin again, only a current area can end, somebody already on the team is a role change. Lives in the client layer so it can be tested. |
| `src/AgencyOS.Client/Commands/AgencyOsCommands.cs` | `AOS-R001-010` | Two palette commands, so both are reachable by keyboard and not only by button. |
| `src/AgencyOS.Windows/Dialogs/ChangeRepresentationScopeDialog.xaml` (new) | `AOS-R001-010` | Begin or end an area. |
| `src/AgencyOS.Windows/Dialogs/ChangeRepresentationScopeDialog.xaml.cs` (new) | `AOS-R001-010` | The same, thin: it displays what `RepresentationMaintenance` returns. |
| `src/AgencyOS.Windows/Dialogs/ChangeRepresentationTeamDialog.xaml` (new) | `AOS-R001-010` | Assign somebody, change what they do, or take them off. |
| `src/AgencyOS.Windows/Dialogs/ChangeRepresentationTeamDialog.xaml.cs` (new) | `AOS-R001-010` | The same, thin. |
| `src/AgencyOS.Windows/Pages/TalentPage.xaml` | `AOS-R001-010` | Two buttons, disabled until the person has a representation, and a refusal bar titled by what was refused. |
| `src/AgencyOS.Windows/Pages/TalentPage.xaml.cs` | `AOS-R001-010` | The four calls, each carrying the version the operator was looking at, each showing the server's own reason if refused. |

**No server file was changed.** No endpoint, migration, contract type, permission
or status code. The OpenAPI document is unchanged: 263 paths, 187 schemas.

Both new dialogs follow 003D's conventions: the hint under a field declares that
it describes the field (`AOS-R002-011`), and a refusal is shown under the name of
what was attempted rather than in a bar titled for a load (`AOS-R002-012`).

## Tests

| File | Covers |
| --- | --- |
| `tests/AgencyOS.Tests.Unit/Client/RepresentationMaintenanceTests.cs` (new, 10) | The rules: the mirrored area and role lists are exactly the domain's enums, an area already represented cannot begin, one that ended can begin again, only a current area can end, an area this client has no name for can still end, somebody on the team stays offered with what they do, only a current assignment can be removed, and a member who left and returned reads as their current role. |
| `tests/AgencyOS.Tests.Windows/Presentation/RepresentationReachTests.cs` (new, 11) | That each of the four routes has a caller, the workspace invokes both commands, both are in the palette and dispatched by the page, the buttons wait for a relationship, and every change carries the observed version. |
| `tests/AgencyOS.Tests.Unit/Client/ClientTests.cs` | The fake API implements the four new members. |

The two new dialogs were also picked up automatically by eight existing
structural suites — accessible names, no hard-coded colours, no fixed heights,
notices have titles, row templates say what they show, domain tokens read as
words, load-time handlers guard what the parser has not built yet, and **no
dialog asks for a canonical identifier** (`AOS-R001-006`'s guard). All pass; that
last one is why both dialogs pick from lists rather than taking a typed
identifier.

## Reviewer

**None.** No harness file was changed by this wave.

## Documentation

| File | Purpose |
| --- | --- |
| `docs/reviews/repair-003e/REPAIR-003E-ADMISSION.md` | What is admitted and why, including why the audit's "owner decision required" no longer applies and why the three finance dialogs still do not. |
| `docs/reviews/repair-003e/REPAIR-003E-LOCAL-WORK.md` | This ledger. |
| `docs/reviews/repair-003e/REPAIR-003E-REPORT.md` | The wave's report. |

## Temporary probes

**None.**

## Fixture written for this wave

Through the API only, never into PostgreSQL, on the LAB database:

| Step | Result |
| --- | --- |
| `POST /prospects` for person "A" | prospect `01a0b2e1-350d-…` |
| `POST /prospects/{id}/convert` | representation `01a0b2e1-36b0-…`, scope Television, lead Review Owner |

An earlier conversion of the prospect Zoë Ångström created representation
`01a0b2de-f782-…`; it is what surfaced the observation in the admission about a
representation no workspace lists.

## Runtime evidence

`artifacts/reviewer/run-repair-003e-local/`

| Path | What |
| --- | --- |
| `scope/` | Adding an area through the real client. |
| `team/` | Assigning somebody through the real client. |
| `end-and-remove/` | Ending an area and taking somebody off the team. |
