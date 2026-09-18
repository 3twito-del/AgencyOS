# Local post-audit repair programme — 003C, 003D, 003E, 003F

```
LOCAL POST-AUDIT REPAIR PROGRAM = IMPLEMENTATION COMPLETE / AUTHORITATIVE VALIDATION / VERSIONING PENDING
```

**Waves:** 003C → 003D → 003E → 003F, cumulative, in one local working tree
**Baseline:** executable `ada2310` (003A.2, closed)
**Date:** 2026-09-18
**Git operations performed:** **none**
**Remote workflows dispatched:** **none**

| Wave | Theme | Status |
| --- | --- | --- |
| [003C](../repair-003c/REPAIR-003C-REPORT.md) | authorization and refusal UX | LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING |
| [003D](../repair-003d/REPAIR-003D-REPORT.md) | accessibility and validation association | LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING |
| [003E](../repair-003e/REPAIR-003E-REPORT.md) | representation completeness | LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING |
| [003F](../repair-003f/REPAIR-003F-REPORT.md) | navigation, discoverability, layout | LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING |

No wave is closed. Closure needs authoritative CI, `postgres:18.6` integration
and Nightly, none of which this programme was permitted to run.

---

## 1. What an operator gets that they did not have

| | Before | After |
| --- | --- | --- |
| A pitch on an unapproved target | "Invalid request" | "This target has not been approved yet. Approve it before recording a pitch." |
| A save against a changed record | `Contract '01a0a1ff-…' has changed since you last saw it (you had version 9, it is now 10).` | "This project has changed since you last saw it (you had version 6, it is now 7). Refresh to see the current version, then decide what to do." |
| A body the server could not read | `Failed to read parameter "CreateDealRequest request" from the request body as JSON.` | "'personId' could not be read. Expected an identifier." |
| A person the server refuses to create | headed **Could not load people** | headed **Could not create the person** |
| A company detail that cannot be read | headed **Could not load companies** | headed **Could not load this company** |
| A hint under a field | next to it, unreachable from it | the field declares what describes it |
| Representation scopes and team | readable, unchangeable | four commands, reachable by button and by palette |
| Two contract commands at 1600x1000 | enabled, offscreen, unreachable | reachable, with a visible overflow |
| The destination you are working in | pane stays put; can be offscreen, unmarked | brought into view when chosen |

## 2. Findings, and what happened to each

### Repaired

| Finding | Wave | What was done |
| --- | --- | --- |
| `AOS-R002-007` | 003C | The `409` sentence names the kind of record in words, keeps both versions, says what to do. The identifier stays on the exception and in the problem's `entityId`. |
| `AOS-R002-008` | 003C | A body that could not bind names the field the caller sent and, from a closed list of shapes, what it should have looked like. The request class never reaches the caller. |
| `AOS-R002-024` | 003C | Five client surfaces show the server's reason instead of the problem's title. Two were found by the structural guard, not by the finding. |
| `AOS-R002-012` | 003D | A refused create is headed by what was refused. A third site — a company detail reported as the directory failing — was found by survey. |
| `AOS-R002-011` | 003D | **Part.** Five field↔hint associations declared; the product now has the pattern it had none of. The refusal half waits on `AOS-R002-010`. |
| `AOS-R001-010` | 003E | Four endpoints with no client caller now have one, with two dialogs and two palette commands. |
| `AOS-R002-021` | 003F | Five contract commands moved into a `CommandBar`; the two that could not be clicked now can. |
| `AOS-R001-013` / `AOS-R001R-001` | 003F | **Part.** The pane follows the selection. The missing overflow affordance is a design question and was left. |

### Measured and deliberately not repaired

| Finding | Why |
| --- | --- |
| `AOS-R002-018` | **Does not reproduce.** 46 of 46 palette rows announce their command; 0 announce the record. The recorded evidence predates Repair Wave 002, which fixed it. Two structural tests pin the property instead. |
| The three unwired finance dialogs | Unreachable **on purpose**: the obligations capability was never built and M13 chose not to advertise a command that dispatches nowhere (ADR-0032). Wiring them is a milestone. |

### Not admitted — owner decisions

> **Superseded.** All five were subsequently decided by the owner and implemented
> locally — `AOS-R002-010` in §11, the remaining four in §12. The table is kept as
> the record of what was held, and why, at the time these waves ran.

| Finding | What the decision is |
| --- | --- |
| `AOS-R002-014` | Whether a `403` quotes the permission string, describes the capability, or names who could grant it. |
| `AOS-R002-010` | Whether a dialog stays open on refusal. Changes how every dialog in the product reports one, and blocks half of `AOS-R002-011`. |
| `AOS-R002-013` | Whether an email address is format-checked. Its expected behaviour is explicitly not established. |
| `AOS-R002-015` | Whether the organization surface becomes a workspace. No capability is blocked. |
| `AOS-R002-016` | Which way Enter should go in 63 dialogs. Any convention makes some dialog commit what it used to discard. |

Also untouched, per §9: `AOS-R002-020` (mailbox visibility), `AOS-R002-026`
(date pickers for instant fields), and the six remaining `AOS-R001-006` fields.

### Deferred correctness findings, per §10 — untouched

`AOS-R002-025` (a missing nested object answers `500`), the 218 fire-and-forget
dispatch sites, and the `MailboxSynchronizer` timestamp observation. None was
touched. `AOS-R002-025` was met again during this programme's own regression pass
and left alone.

## 3. New observations recorded by this programme

Found while working, reported rather than repaired.

1. **A representation no workspace lists.** Converting a prospect creates a
   representation for a *person*; the Talent workspace lists *talent profiles*. A
   person with no talent profile leaves Prospects on conversion and appears
   nowhere. Measured in 003E.
2. **A `404` names the identifier the caller supplied.** Same family as
   `AOS-R002-007` in wording, but the identifier is the caller's own and the
   sentence is load-bearing for existence hiding. Left alone, noted in 003C.
3. **A missing required query parameter** still answers with the framework's own
   sentence, naming the parameter and its .NET type. Noted in 003C.
4. **The `contract` gate is not idempotent.** It deletes `artifacts/openapi` and
   then runs an *incremental* build, so a second run with nothing to rebuild
   produces no document and fails. Regenerating with `--no-incremental` twice
   gives byte-identical output.

## 4. Local gates

| Gate | Baseline (`ada2310`) | After 003C | After 003D | After 003E | After 003F |
| --- | --- | --- | --- | --- | --- |
| build | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,755 | 3,763 | 3,763 | 3,773 | **3,773** |
| Windows | 942 | 950 | 968 | 995 | **1,002** |
| Reviewer | 163 | 163 | 163 | 163 | **163** |
| contract | 263 / 187 | 263 / 187 | 263 / 187 | 263 / 187 | **263 paths / 187 schemas** |
| integration (LAB PG19, §6) | not run | not run | not run | not run | **864 / 864** |

**+91 test cases**, all passing, no test weakened or removed: **75 written by this
programme** (003C: 8 unit, 13 integration, 8 Windows · 003D: 18 Windows · 003E:
10 unit, 11 Windows · 003F: 7 Windows) and **16 from existing suites** picking up
the two dialogs 003E added.

The Windows gate grew by more than the tests written: two new dialogs were swept
automatically by eight existing structural suites — accessible names, no
hard-coded colours, no fixed heights, notices have titles, row templates say what
they show, domain tokens read as words, load-time handlers guard what the parser
has not built yet, and **no dialog asks for a canonical identifier**. All pass
without an exception being added for them.

## 5. Schema, contract and data

- **No migration.** No table, column, type or index changed.
- **No endpoint, contract type, status code or permission changed.**
- **OpenAPI regenerates byte-identically** — `942d0e74…` across two forced
  regenerations.
- **`AOS-R002-001` was not touched**: `UtcInstantConverter`, its two
  registrations in `Program.cs`, the OpenAPI schema transformer and the temporal
  tests are as 003A.2 left them, and the behaviour was re-verified live (below).
- **Canonical ALPHA/STABLE data was not touched.** Every fixture was written
  through the API against the LAB database, never into PostgreSQL directly.

## 6. Integrated regression pass

Run over the cumulative tree, not per wave.

| Check | Result |
| --- | --- |
| `003A.2` — an instant sent at UTC+3 | `2026-09-18T09:30:00+03:00` → stored `2026-09-18 06:30:00` UTC. Accepted, normalized, no `500`. |
| `003A.2` — frozen code | Converter and both registrations present and unchanged. |
| OpenAPI determinism | Two forced regenerations, identical hash. |
| `003B` selectors | 65 dialogs pass the raw-identifier guard, including both new ones. |
| `003A.1` — dialogs still open | **65 dialogs, 54 opened, client never closed.** |
| Integration against LAB PostgreSQL 19 | **864 of 864 pass.** |
| All local gates | green, above. |

### Integration, corroborated locally

The integration suite takes `AGENCYOS_TEST_POSTGRES` instead of a container, so
all 864 tests were run against the LAB PostgreSQL 19 on port 5433. **864 passed.**

It creates and uses its own database (`agencyos_test_…`); `agencyos_w2` was not
touched, and no canonical ALPHA or STABLE database was involved.

Three tests failed on the first attempt — `BackupRestoreDrillTests` — with
*"pg_dump is not on PATH. AgencyOS backup requires the PostgreSQL 18 client
tools."* That is this machine's provisioning, not the product: with the installed
client tools on `PATH` all three pass. Worth knowing because the drill demands a
**major version 18** minimum and this machine has only 19.

**This is corroboration, not the gate.** `postgres:18.6` under canonical CI is
what validates these; PostgreSQL 19 beta is LAB (§11).

### The dialog sweep, against the last known-good pass

| | 003A.1 final | This programme |
| --- | --- | --- |
| dialogs | 63 | **65** (003E added two) |
| opened | 52 | **54** |
| did not appear | 7 | **7** |
| nothing constructs them | 4 | **4** |
| **application closed** | **none** | **none** |
| refused keystrokes | 0 | **0** |

**Compared dialog by dialog, zero outcomes changed.** Every one of the 63 dialogs
the 003A.1 pass saw behaves exactly as it did then, and the two dialogs 003E
added both open.

Of the 54 that opened: focus entered every one, Escape closed every one, focus
escaped none of them, and there were **0 accessibility findings**.
`AttachToRoleDialog` — one of the three that crashed the client before 003A.1 —
opened cleanly.

The four that nothing constructs are `AddIntelligenceSubjectDialog` and the three
finance dialogs, which is exactly the canonical inventory's list. That is
independent corroboration of 003E's disposition, arrived at from the running
product rather than from reading source.

**A correction.** The first attempt at this sweep was run without `--subject`, so
it ran as `review-owner`, which answers `401` on every endpoint of this
organization. Every list was empty and only 13 dialogs opened. That is a mistake
in how the harness was invoked, not a product result, and the numbers above come
from the corrected run. The invalid run is kept at
`run-repair-local-regression/dialogs/` rather than deleted, because it is still
evidence of something: with every list failing to load, the client did not close
and the 13 dialogs that did open all behaved correctly.

## 7. What authoritative validation must still do

Nothing in this programme has been validated by anything but this machine.

1. **Canonical CI** on `postgres:18.6`. All 864 integration tests pass here
   against LAB PostgreSQL 19 (§6), which corroborates but does not validate:
   19 is a beta and the ALPHA baseline is 18.6. A behaviour that differs between
   the two would not have been caught by anything done here.
2. **Nightly** — last dispatched during 003A.2 and refused five times for an
   account billing state, not for anything in the code.
3. **Versioning** — nothing was committed, tagged or pushed. The working tree is
   the only copy of this work.

## 8. Risks worth an operator's attention

1. **`CommandBar` is used nowhere else in the product.** The contracts page now
   looks slightly different from the other sixteen, and three of its five
   commands sit behind an overflow. Deliberate, and the alternative was two
   commands that could not be clicked at all.
2. **The pane now scrolls on selection.** Choosing a destination near the bottom
   scrolls the top ones out of view. That is the point, but it is a visible
   behaviour change for anyone used to the pane never moving.
3. **Five `Detail ?? Message` sites and three refusal bars** changed what an
   operator reads. No authority, status code or rule changed with them.
4. **The work is uncommitted.** A lost working tree loses all four waves.

## 9. Evidence

| Wave | Directory |
| --- | --- |
| 003C | `artifacts/reviewer/run-repair-003c-local/` |
| 003D | `artifacts/reviewer/run-repair-003d-local/` |
| 003E | `artifacts/reviewer/run-repair-003e-local/` |
| 003F | `artifacts/reviewer/run-repair-003f-local/` |
| Regression | `artifacts/reviewer/run-repair-local-regression/` |

No prior audit directory was overwritten. Server logs are kept as tails — EF
command logging reached 2.1 GB on one run — and contain no credential,
connection string or parameter value; EF's sensitive-data logging is off, so
values appear as `'?'`.

Each wave carries an **admission**, a **local work ledger** and a **report** under
`docs/reviews/repair-003{c,d,e,f}/`.

## 10. Status

```
LOCAL POST-AUDIT REPAIR PROGRAM = IMPLEMENTATION COMPLETE / AUTHORITATIVE VALIDATION / VERSIONING PENDING
```

---

## 11. Addendum — `AOS-R002-010` resolved, `AOS-R002-011` completed

**Date:** 2026-09-18, after §1–§10 were written. Additive: nothing above is
withdrawn, and the account of why `AOS-R002-011` was previously blocked stands as
written.

```
AOS-R002-010 = OWNER DECISION RESOLVED / LOCAL IMPLEMENTATION COMPLETE
AOS-R002-011 = LOCAL IMPLEMENTATION COMPLETE
```

### The decision

> For a recoverable server refusal arising from a mutation initiated inside a
> dialog, **the dialog stays open**, preserving entered values, selected entities,
> operator context and keyboard/focus usability. The message is shown inside the
> dialog and, where it concerns a specific field, associated with that field
> through the product's accessible validation mechanism. The operator can correct
> and retry without reconstructing the dialog.

### The boundary

Not every failure keeps the dialog, and the difference is whether the operator
could do anything about it from where they are.

| | Stays open | Closes |
| --- | --- | --- |
| Status | `400`, `422`, `409`, `403` | `401`, `404`, `426`, any `5xx` |
| Why | the entry is wrong, somebody moved first, or the work is still worth keeping | the session is gone, the parent is gone, this build may not talk to this server, or the server failed |
| Where the operator reads it | in the dialog, on the field where possible | on the page, under a heading naming what was refused |

`403` stays open deliberately. It will not start succeeding, but the dialog holds
work the operator may want to copy or reroute, and discarding it teaches them to
distrust the form.

### Surfaces changed

The three the findings named, and no others.

| Dialog | Fields registered | Page that used to report the refusal |
| --- | --- | --- |
| `NewPersonDialog` | 6 | `PeoplePage` |
| `NewCompanyDialog` | 5 | `CompaniesPage` |
| `AddProjectRoleDialog` | 3 | `ProjectsPage` |

Each now owns its own mutation — the only way a dialog can decline to close — and
each hands back the refusals it cannot answer, which the page still reports under
the heading `AOS-R002-012` gave it.

### Measured live, on all three

Same sequence each time, through the real client against the LAB database:

```
submit a value the server refuses
  -> ErrorBar says [Could not create the person | firstName must be at most 128 characters.]
  -> dialog still open; FirstNameBox, LastNameBox, TitleBox, NotesBox all still readable
  -> other entries intact: NotesBox value='typed elsewhere'
  -> focus moved from NotesBox to FirstNameBox — the field the refusal is about
correct the value
  -> ErrorBar gone; other entries still intact
retry
  -> dialog closed, and the record exists on the server
```

Confirmed on the server afterwards: person *Corrected*, company *Corrected
Company*, project role *Corrected Role*. The company retry was submitted **by
keyboard** (`Enter` on the default button), which is the keyboard-only path.

### Accessibility, and the limit of this instrument

The association is declared with `AutomationProperties.GetDescribedBy(field)
.Add(bar)` — the platform API that maps to UI Automation's `DescribedBy` — and is
removed when the entry changes, so a screen reader never reads a complaint about a
value already corrected.

**It could not be read back from the harness.** `System.Windows.Automation`, the
managed client the reviewer uses, has no `DescribedBy` property (30105) at all, so
the relationship cannot be verified from there. Said plainly because "cannot read
it" and "it is not set" are different findings: what is verified is the declaration
(structurally, which is how the audit established the defect — 0 of 63), which
field is chosen (executably, in unit tests), and that focus lands on that field
(live). **What is spoken remains a manual screen-reader gate**, as it was for
every accessibility finding in Audit 002.

### Tests

**+45 test cases.** 23 unit executing the decision itself, 22 Windows over the
wiring.

Both were mutation-checked against the behaviours they exist to prevent:

| Mutation | Tests that failed |
| --- | --- |
| the dialog always closes — the behaviour before this decision | **13 of 23** |
| every refusal pinned to the first field — the mistake the decision warns against | **4 of 23** |

### Gates after this work

| Gate | Before | After |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,773 | **3,796** (+23) |
| Windows | 1,002 | **1,024** (+22) |
| Reviewer | 163 | **163** |
| integration (LAB PG19) | 864 / 864 | **864 / 864** |

No server file was changed. No endpoint, contract, migration, permission or status
code; no server-side validation or authorization was weakened; nothing beyond the
problem's own `detail` reaches the operator.

`postgres:18.6` authoritative validation, CI, Nightly and versioning remain
**pending**. No Git operation was performed.

---

## 12. The remaining four owner decisions, resolved

**Date:** 2026-09-18, after §11. Additive.

```
OWNER DECISION SET = LOCALLY RESOLVED
```

All five decisions this programme raised are now taken. The options weighed before
each are kept in [OWNER-DECISIONS-RESOLVED.md](OWNER-DECISIONS-RESOLVED.md); what
follows is what was done.

### `AOS-R002-013` — accept current behaviour, no format validation yet

**No product change.** An address stays bounded by length and unvalidated in
shape, at all five sites (`Person`, `User`, `CommunicationAccount`,
`CommunicationMessage`, `OutboundDispatch`), which is what every other string in
this domain does.

**No test was added, deliberately.** A test asserting that `"not-an-email"` is
stored would pin accidental behaviour and make the eventual mail integration
harder: whoever adds deliverable-address requirements would first have to delete
something that looks like an invariant.

This is **accepted current product semantics, not a defect left unfixed**, and it
may be revisited when a real mail-sending capability depends on deliverable
addresses — with that capability's own requirements, rather than a guess made
before it existed.

### `AOS-R002-014` — human capability language

| | |
| --- | --- |
| Before | `detail: "Permission 'people.read' is required."` |
| After | `detail: "Reading people is not part of your role."` |
| `requiredPermission` | `"people.read"` — unchanged, exact |
| Status | `403`, unchanged · title `Permission denied`, unchanged |

Measured live against the running server as `review-elsewhere`, an authenticated
non-member.

**Files:** `src/AgencyOS.Domain/Authorization/PermissionCapability.cs` (new) and
one line of `PermissionDeniedException.cs`. A plain table of **78 capabilities**,
one per permission, rather than anything derived from the identifier — deriving
would put the identifier's own words into the sentence ("Posting finance ledger"),
which is neither what an operator calls it nor what the finding asked for.

**Unknown permissions** fall back to *"That is not part of your role."* — an older
client refused a permission a newer server added says less, not more.

**Guarded by 11 unit tests**, including the completeness rule: every member of
`Permission.All` must have words, so a new permission cannot quietly start
speaking the fallback. Also asserted: no sentence contains a dotted identifier, or
claims the capability does not exist, or promises who could grant it.

**Authorization is untouched.** No grant, role, evaluation or scope changed.

### `AOS-R002-015` — keep Organization in settings, add palette access

`OrganizationPage` remains the navigation pane's settings destination.
**`AgencyOsWorkspaces.All` still holds 17**, and Organization is still not among
them.

The palette gained one command, `organization.open`, titled **"Go to Organization
settings"**. It is an `Invoke` rather than a `Navigate`, because `Navigate` means
"select a workspace" in this registry and the settings destination is not one; the
shell answers it by selecting the settings item, which is the same path the pane
uses, so there is one navigation state rather than two.

Measured live: querying the palette for "organization" returns one row announcing
**`Go to Organization settings | Navigate`**, and `Enter` lands on the Organization
page (`Add someone`, `Change role`).

**Guarded by 7 unit tests**, including that the workspace count is still 17 and
that the settings destination still works.

### `AOS-R002-016` — Enter commits, with explicit safety exceptions

**65 dialogs classified. 61 commit on Enter. 4 are explicit exceptions. 36
defaults changed.** The full table is in
[the inventory](../repair-003g/AOS-R002-016-DIALOG-INVENTORY.md).

| Dialog | Class | Enter now | Why |
| --- | --- | --- | --- |
| `ApproveAiActionDialog` | `APPROVAL` | rejects | Primary is "Approve and run", which executes a tool. The product's own considered choice, which the finding named. |
| `PostJournalEntryDialog` | `IRREVERSIBLE_OR_EXTERNAL` | nothing | The domain: *"a posted entry is immutable and counts."* It can only be answered by a further entry. |
| `ConnectMailboxDialog` | `IRREVERSIBLE_OR_EXTERNAL` | nothing | Hands an authorization code to an external provider and opens a live mailbox connection. |
| `ConvertProspectDialog` | `IRREVERSIBLE_OR_EXTERNAL` | nothing | "Sign" creates the representation and closes the pursuit in one transaction. |

**A live run changed this design.** The three irreversible cases were first left
on `Close`, which is what their markup already said — and `Enter` in a half-filled
journal entry then *dismissed the dialog and threw the entry away*. Not committing
was the requirement; discarding is not a way to meet it. They are `None` instead,
so `Enter` does nothing and the primary action is reached deliberately.

**Runtime sample, all measured:**

| | Case | Result |
| --- | --- | --- |
| A | ordinary create | `Enter` committed — person *EnterCommits* on the server |
| B | ordinary edit | `Enter` committed — project stage Development → Packaging, v10 → v11 |
| C | refusal path | `Enter` submitted, refusal shown **in** the dialog, dialog open, `https://kept.example` retained, focus on the offending field |
| D | multiline field | `Enter` edited text; dialog stayed open with values intact |
| E | picker | `Enter` selected *Director* in the open list; dialog did not commit |
| F | approval exception | not openable without a pending AI action; structurally `Secondary`, primary "Approve and run" |
| G | irreversible exception | `Enter` in `PostJournalEntryDialog` left the dialog open and posted nothing |

B and E also show the convention does not fight the controls: a closed `ComboBox`
opens its list on `Enter` and a `TextBox` beside it commits, which is ordinary
WinUI default-button behaviour rather than anything special-cased.

**Guarded by 8 Windows tests**, including a completeness rule — a 66th dialog must
be classified or the suite fails — and a rule that an exception's reason must be a
decision rather than a restatement of the markup.

**Escape and cancel are unchanged**, and every dialog still has a close button.

### Server impact

**None, for any of the four.** No endpoint, contract, migration, permission, role,
grant, tenant rule, concurrency or idempotency behaviour changed. `AOS-R002-014`
changed one sentence the server sends; the status, title and machine extension are
as they were.

### Gates after the four decisions

| Gate | Before | After |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,796 | **3,814** (+18) |
| Windows | 1,024 | **1,031** (+7) |
| Reviewer | 163 | **163** |
| integration (LAB PG19) | 864 / 864 | **864 / 864** |
| OpenAPI | 263 / 187 | **263 / 187**, hash `942d0e74…`, **byte-identical** |
| API contract version | 14 | **14** |

### Full dialog sweep after the keyboard convention

Because `AOS-R002-016` changes what `Enter` does in 36 dialogs, the whole sweep
was run again and compared to the cumulative baseline:

| | Baseline | After |
| --- | --- | --- |
| dialogs | 65 | **65** |
| opened | 54 | **54** |
| context/precondition outcomes | 7 | **7** |
| intentionally unconstructable | 4 | **4** |
| application closed | none | **none** |
| refused keystrokes | 0 | **0** |
| accessibility findings | 0 | **0** |

**Zero changed outcomes, dialog by dialog.** Every one of the 54 that opened took
focus and closed on Escape, as before. No `LayoutCycleException` recurrence.

### One thing the guards caught

`organization.open` failed the reviewer's own dead-command check
(`DEAD-CMD-001`): the scanner reads dispatch from a page's `Execute` body, and the
shell answers this one in `MainWindow.Dispatch`. The harness keeps an explicit
list of shell-owned commands — `search.open`, `sync.now` — and the new command
had to join it. **The guard was right**: a palette entry the scanner cannot see
being answered is exactly what ADR-0032 was written about, and the fix was to
teach the scanner, not to weaken the check.

## 13. What remains

**No owner decision is outstanding.** All five raised by this programme are
decided and implemented.

Still deferred, unchanged, and none of them an owner decision:

| | |
| --- | --- |
| `AOS-R002-020` | mailbox visibility — held per §9 of the programme brief |
| `AOS-R002-025` | a missing nested object answers `500` |
| `AOS-R002-026` | date pickers for instant fields |
| `AOS-R001-006` | the six remaining owner/lead/link/debug identifier fields |
| — | the 218 fire-and-forget dispatch sites |
| — | the `MailboxSynchronizer` timestamp observation |
| — | a representation no workspace lists (found in 003E) |
| — | the `contract` gate's non-idempotence (found in the regression pass) |

**Authoritative validation and versioning remain pending**: `postgres:18.6` under
canonical CI, Nightly, and any commit or tag. Nothing has been committed, and no
remote workflow has been run.
