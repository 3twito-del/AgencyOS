# Audit 002 — Dialogs, Mutations and Workflow Completeness

**Product baseline:** `51541ea` — unchanged by this audit.
**Reviewer commit:** this commit — harness, reviewer tests and audit evidence only.
**Evidence:** `artifacts/reviewer/run-002-audit/`
**Date:** 2026-09-14

Audit 001, 001R and the run-002 repair evidence are untouched.

---

## What this audit was for

> What happens when a real operator actually opens every dialog, attempts real
> synthetic mutations, and performs the same workflows under different
> authorization roles?

Sixty-one dialogs had been inventoried by Audit 001 and **opened by nothing**.
Three of them are named by any test. This is the first time any of them was
opened from the real interface.

**It got part of the way.** 30 of 57 reachable dialogs were opened and worked;
mutations ran in one domain of eleven; and multi-role review turned out to be
impossible for a reason that is itself the second-biggest finding. §5 of the
coverage report says exactly where the line falls. The brief's §30 minimum is
**not met**, and this report says so rather than rounding up.

---

## 1. Headline findings

| Id | Sev | Confidence | What |
| --- | --- | --- | --- |
| `AOS-R002-001` | **S1** | ConfirmedDefect | A timestamp with a non-UTC offset makes the server answer **500**. Five dialogs send exactly that. |
| `AOS-R002-002` | **S2** | ConfirmedDefect | An organization cannot gain a second person. No path creates a user after bootstrap. |
| `AOS-R002-003` | S3 | ConfirmedDefect | 17 dialogs have no button anywhere; the command palette is the only way in. |
| `AOS-R002-004` | S2 | ConfirmedDefect | `RecordOfferDialog` does not take focus when it opens, and Tab walks the page behind it. |
| `AOS-R002-005` | S2 | ConfirmedDefect | 3 of 61 dialogs are named by any test. |

Severity counts: **S0 0, S1 1, S2 3, S3 1, S4 0.**
Confirmed defects 5, likely defects 0, design recommendations 0.
**Owner/design decision required: 2** (`AOS-R002-002`, `AOS-R002-003`).

### `AOS-R002-001` in one paragraph

`POST intelligence/predictions` with `resolvesBy` = `2026-12-31T00:00:00Z` returns
**201**. The same instant as `2026-12-31T00:00:00+02:00` returns **500**, with an
unhandled `DbUpdateException`: *Cannot write DateTimeOffset with Offset=02:00:00
to PostgreSQL type 'timestamp with time zone', only offset 0 (UTC) is supported*.
Reproduced on `intelligence/signals` with `observedAt`, so it is the offset and
not the field. `CreatePredictionDialog` sends `ResolvesPicker.Date`, and a WinUI
`CalendarDatePicker` carries the machine's local offset — so on any machine not
set to UTC, the Windows client sends precisely the value the server rejects, and
the operator sees an internal error with a trace id. Five dialogs do this; the
other eighteen date-carrying dialogs normalise and are unaffected.

---

## 2. The four findings this audit was asked to reclassify

### `AOS-R001-006` — typed GUID workflows → **CONFIRMED, and worse than reported**

**13 dialogs, 20 raw-identifier fields**, reconfirmed exactly. Per instance:

| Dialog | Fields | Classification |
| --- | --- | --- |
| `CreateDealDialog` | Opportunity, Target, Owner | **LIKELY_UI_DEFECT** |
| `CreateContractDialog` | Deal, Accepted offer, Owner | **LIKELY_UI_DEFECT** |
| `CreateOpportunityDialog` | Owner, Subject | **LIKELY_UI_DEFECT** |
| `CreatePackageDialog` | Project identifier, Lead | **LIKELY_UI_DEFECT** |
| `AddOpportunityTargetDialog` | Identifier, Contact | **LIKELY_UI_DEFECT** |
| `AddPackageElementDialog` | Identifier | **LIKELY_UI_DEFECT** |
| `AddProjectCompanyDialog` | Company identifier | **INTERNAL_REFERENCE_USER_SHOULD_SELECT** |
| `AttachToRoleDialog` | Identifier | **INTERNAL_REFERENCE_USER_SHOULD_SELECT** |
| `RecordPitchDialog` | Material shown | **INTERNAL_REFERENCE_USER_SHOULD_SELECT** |
| `RecordSubmissionDialog` | Material | **INTERNAL_REFERENCE_USER_SHOULD_SELECT** |
| `LinkRecordDialog` | Record identifier | **INTERNAL_REFERENCE_USER_SHOULD_SELECT** |
| `LinkResearchItemDialog` | Identifier | **INTERNAL_REFERENCE_USER_SHOULD_SELECT** |
| `AddIntelligenceSubjectDialog` | Record identifier | **DEBUG/ADMIN_ONLY** — nothing constructs this dialog |

None is `UNAVOIDABLE_EXTERNAL_IDENTIFIER`: every one names a record AgencyOS owns
and displays under a human name elsewhere.

**The material new fact:** since Repair Wave 002, **no surface in the Windows
client displays an identifier at all**. Wave 002 removed them from row
announcements — correctly, they were being read aloud to screen-reader users — and
no page binds an id to any visible element. So these twenty fields ask for values
the product no longer shows anywhere. Before Wave 002 an operator could at least
have read one out of a row's accessible name. That is not an argument against
Wave 002; it is the reason 006 is now more urgent than when it was filed.

`CreateDealDialog` does gate its primary button until the required fields are
filled, and its placeholders name what is wanted ("Opportunity identifier"). It
explains what, never where from.

**Design decision required.** A searchable picker, a context-derived value, a
navigate-from-the-record-first flow, and deliberate power-user identifiers are all
reasonable and this audit chooses none of them.

### `AOS-R001-010` — representation maintenance → **PARTIAL_WORKFLOW_GAP**

Verified against the published contract and the client:

| Capability | Server | Client |
| --- | --- | --- |
| Create (by converting a prospect) | `POST /prospects/{id}/convert` | ✅ `ConvertProspectDialog` |
| Read | `GET /representations/{id}` | ✅ |
| Transition | `POST /representations/{id}/transition` | ✅ |
| History | ✅ | ✅ |
| Credits | ✅ | ✅ `AddCreditDialog` — **opened and operated in this audit** |
| Materials | ✅ | ✅ `AddMaterialDialog` — **opened and operated in this audit** |
| **Scopes — add** | `POST /representations/{id}/scopes` | ❌ no client method |
| **Scopes — end** | `POST /representations/{id}/scopes/end` | ❌ no client method |
| **Team — add** | `POST /representations/{id}/team` | ❌ no client method |
| **Team — remove** | `POST /representations/{id}/team/remove` | ❌ no client method |

Audit 001 said scope, team, disciplines, profile, credits and materials all lacked
a client method. **Credits and materials do have one**, and this audit opened both
dialogs, so that part of 010 is **SUPERSEDED**. Scopes and team are confirmed:
four server endpoints with nothing in the client that calls them.

Not CRUD-completeness envy: a representation's lifecycle is deliberately
transition-driven, and the absence of a general edit is consistent with that. The
gap is specific — a representation's scope cannot be widened or ended, and nobody
can be added to or removed from its team, from the Windows client.

### `AOS-R001-013` — navigation overflow → **refined, still PARTIAL / OPEN**

Audit 001R established the mechanism. Audit 002 adds what it looks like under
real workflow switching: the dialog pass navigated between workspaces **57 times**,
once per dialog attempt, across nine workspaces.

- Navigation never failed. Every workspace was reachable every time.
- The pane never followed the selection, exactly as 001R found. Every time the
  audit worked in Intelligence, Finance, Contracts or Sync — four of the nine
  workspaces it used — the pane showed a list that did not include the page being
  worked on.
- Keyboard navigation does not make it worse: the destinations are reachable by
  their access keys regardless of whether they are visible.
- **It did not disrupt multi-step work.** The audit's own workflows completed.
  The cost is orientation, not capability.

Severity stays **S2**. The evidence does not support raising it, and `AOS-R001R-001`
already carries the "no indicator of the current destination" half.

### `AOS-R001-020` — F9 / sync.now → **INTENTIONAL_NO_OP (navigation), and the audit's expectation was wrong**

Read from source:

```csharp
// Shell-owned actions: they belong to the window rather than to any
// workspace, so no page can answer them.
case "sync.now":
    _ = SynchronizeAsync();
    return;
```

`sync.now` is declared `CommandActionKind.Invoke` with `Workspace: "sync"`. The
shell intercepts it **before** the workspace routing and returns, deliberately not
navigating. The `Workspace` on the definition is a grouping hint, not a
destination.

Audit 001 recorded F9 as failing because the harness expected the selection to
change to "Sync and Offline". For an `Invoke` command that is the wrong
expectation — and the harness derived it from `command.Workspace` regardless of
`CommandActionKind`. That is a **reviewer defect**, corrected in this audit's
gesture verdicts.

So: **F9 is not a broken command.** It runs the shell's synchronise path and
deliberately leaves the current page alone. What remains true, and is worth
keeping, is that it produces **no visible acknowledgement** at the moment of
pressing — the only feedback is the connection footer's "last updated" caption
changing. Reclassified from `BROKEN_COMMAND` to **`INTENTIONAL_NO_OP`**, with a
residual S4 observation about the missing acknowledgement.

---

## 3. Dialog results

| | |
| --- | ---: |
| Declared | **61** |
| Nothing constructs them — `AOS-R001-017` reconfirmed | **4** |
| Reachable by a control | 40 |
| Reachable only by a command | 17 |
| **Opened from the real interface** | **30** (16 by button, 14 by palette) |
| Declined to open — precondition or fixture | 27 |
| Constructed directly by the audit | **0** |

Of the 30 opened: focus entered 29, Escape dismissed 30, Tab stayed inside 29,
focus was never lost and never trapped in a dismissed dialog, and the corrected
accessibility and row-speech detectors found **nothing** inside any of them.

The 27 that declined were each attempted down every declared path. Twenty-four ran
their command and had the page's handler return early because the record it needed
— a contract, an obligation, an offer, a mailbox — does not exist in the fixture.
Three had an opener that was absent or disabled for the same reason. **None is
shown to be unreachable.**

---

## 4. Reviewer defects found during Audit 002 (§27)

Five, all in code written for this audit, all fixed before any result was
recorded. **No earlier audit's evidence is invalidated by any of them** — the
affected code did not exist in Audit 001, 001R or the repair waves. The one
exception is noted below.

| # | What was wrong | What it would have said | Status |
| --- | --- | --- | --- |
| 1 | `Keyboard.Type` sent virtual-key codes, which Windows maps through the active keyboard layout. On this machine that is Hebrew. | 17 palette-only dialogs unreachable. | Fixed — characters are now sent literally. **Permanent test**: `KeyboardTests`. |
| 2 | The dialog scanner treated a dialog's own code-behind as the page that opens it. | All 61 dialogs unopenable. | Fixed before any result. |
| 3 | The dialog scanner read a missing `x:Name` as a missing control. | 24 reachable dialogs reported unreachable. | Fixed before any result. |
| 4 | The dialog pass compared screen rectangles as well as identity when asking where focus was. | 6 dialogs with escaping focus (real answer: 1) and focus restored on 0 of 30 (real answer: 30 landed safely). | Fixed — identity only. |
| 5 | `ProbeGestures` expected navigation from any command carrying a `Workspace`, including `Invoke` commands the shell owns. | `sync.now` broken — which is what Audit 001 recorded. | Fixed in Audit 001R's verdicts; **this is the one that did affect an earlier record**, and `AOS-R001-020` is reclassified above. |

`Type` had never been called by any previous audit, which is why defect 1
invalidates nothing historical. Defect 5 is the exception and is handled by
reclassifying the finding rather than by editing Audit 001.

**One measurement is marked unmeasurable rather than guessed:** WinUI hosts every
`ContentDialog` behind two visible popup windows, so "is there an unexpected second
modal" cannot be answered by counting them. Recorded as `INCONCLUSIVE`, not as 30
defects.

---

## 5. Gates

| Gate | Result |
| --- | --- |
| Product code changed | **none** |
| `dotnet build AgencyOS.sln` | 0 warnings, 0 errors |
| Unit | 3,749 |
| Windows | 715 |
| Reviewer | 83 |

Integration was not re-run: no product code, contract, migration or endpoint was
touched.

---

## 6. Proposed repair waves — a proposal only

Nothing here is executed.

**WAVE 003A — correctness**
`AOS-R002-001` (S1, non-UTC timestamps → 500) and `AOS-R002-005` (dialogs no test
opens). 003A should come first: a whole create path is broken for most operators.

**WAVE 003B — dialog usability and entity selection**
`AOS-R001-006` (20 typed-identifier fields) and `AOS-R002-003` (17 palette-only
dialogs). Both need an owner decision before any code.

**WAVE 003C — authorization and refusal UX**
Nothing to schedule. Multi-role review is blocked by `AOS-R002-002`, which is 003E.

**WAVE 003D — accessibility, focus, validation**
`AOS-R002-004` (`RecordOfferDialog`). Small, self-contained.

**WAVE 003E — completeness, owner decision required**
`AOS-R002-002` (no second user) and `AOS-R001-010` (representation scopes and
team). Both are product-shape questions, not bugs.

**POLISH LATER**
`AOS-R001-020`'s residual — F9 gives no acknowledgement (S4). Focus after a dialog
closes lands on the page rather than the control that opened it — consistent across
all 30, no user harm demonstrated.

**Before 003A, the audit slice that was not finished is worth more than any of
them**: enrich the fixture for Contracts, Finance, Communications and Packages,
and the remaining 27 dialogs become openable. Everything §6, §11, §12 and §13 asked
for is waiting behind that.
