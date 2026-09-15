# Audit 002 Phase C — the status of every finding carried in

§12, §13, §14 and the disposition of every `AOS-R002-*` raised so far. Nothing is
rewritten; each entry says what changed and on what evidence.

---

## 1. Findings carried from Audit 001

### `AOS-R001-006` — twenty typed-identifier fields → **CONFIRMED**

Re-derived from source rather than carried forward: still **20 fields across 13
dialogs**. Repair Wave 003E-A added none, which was one of its constraints and is
now verified rather than assumed.

Narrowed in cause, not in scope: four of the twenty name an internal user, and
Phase B classified them `OWNER_DESIGN_DECISION_REQUIRED` partly because the
client had no way to list users. It does now
(`GET /organizations/{id}/members`). The classification stands — a picker, a
default, or something else are all defensible and §11 forbids choosing — but the
question is answerable without inventing a directory first.

Detail: [`AUDIT-002C-GUID-FIELDS.md`](AUDIT-002C-GUID-FIELDS.md).

### `AOS-R001-010` — representation maintenance → **CONFIRMED_API_UI_GAP**, unchanged

Re-verified at both ends this phase:

```
server:  POST /representations/{id}/scopes
         POST /representations/{id}/scopes/end
         POST /representations/{id}/team
         POST /representations/{id}/team/remove
client:  0 methods calling any of them
```

Four endpoints, no caller, no command, no dialog among the 63. The
credits/materials half of the original finding stays **SUPERSEDED** — those
dialogs exist and were opened.

Untouched by Repair Wave 003E-A, which was explicitly forbidden from it.

### `AOS-R001-013` — navigation overflow → **S2 / PARTIAL**, unchanged

Phase C's two-level tab walk navigates far more than the earlier passes did:
every dialog attempt visits each outer tab and, inside it, each inner tab, with
the strip re-read after every selection. Navigation itself never failed in any of
it.

The finding was never that navigation breaks. It is that the pane does not follow
the selection, and nothing this phase observed changes that in either direction.
**No severity change, and none downward either.**

### `AOS-R001-020` — F9 / `sync.now` → **NO_DEFECT**, closed

Both halves are now settled.

**Navigation.** `sync.now` is shell-owned and deliberately does not navigate.
Audit 001 recorded it as broken because the reviewer's own gesture probe expected
navigation from any command carrying a workspace — reviewer defect 3. The
expectation was wrong, not the product.

**Acknowledgement.** Phase B left this `INCONCLUSIVE` because it set out to
observe a controlled offline/online transition and did not set one up. It does
not need one: `SynchronizeAsync` ends in `RenderSync`, which writes
`_sync.StatusLine` into the always-visible footer, so the question is simply
whether that line changes. Observed directly:

```
status line before:  Online - last updated 13 hours ago, nothing waiting to send.
F9 accepted:         yes
status line after:   Online - last updated just now, nothing waiting to send.
```

Pressing F9 produces a visible acknowledgement in the shell's standing status
line. **NO_DEFECT.** The `ACKNOWLEDGEMENT_UX_GAP` residual is withdrawn, and it
is withdrawn on evidence rather than on absence.

---

## 2. Findings raised by Audit 002

| Id | Phase | Status after Phase C | Why |
| --- | --- | --- | --- |
| `AOS-R002-001` | A | **CONFIRMED** | Two S1 query-translation defects, repaired in an earlier wave and re-verified; the 500-with-a-trace-id shape was the symptom. |
| `AOS-R002-002` | A | **CLOSED** | Repair Wave 003E-A. A second user can be registered and granted a role through `POST /organizations/{id}/members`. |
| `AOS-R002-003` | A | **CONFIRMED** | Seventeen dialogs have no opening control in markup; four have no opening path at all. See [`AUDIT-002C-DEAD-DIALOGS.md`](AUDIT-002C-DEAD-DIALOGS.md). |
| `AOS-R002-004` | A | **CLOSED_AS_HARNESS_ERROR** | Reviewer defect 23 — see below. |
| `AOS-R002-005` | A | **CONFIRMED** | Three of sixty-three dialogs are named by any test. Unchanged; 003E-A's two new dialogs are covered by integration tests of the endpoints beneath them, not of the dialogs. |
| `AOS-R002-006` | B | **CLOSED** | Repair Wave 003E-A. Membership can be listed, granted, revoked and re-roled, and the last owner is protected. |
| `AOS-R002-007` | B | **CONFIRMED** | The 409 names an identifier the operator has never seen. See also `AOS-R002-014`, which is the same shape for 403. |
| `AOS-R002-008` | B | **CONFIRMED** | `400 Failed to read parameter "CreateDealRequest request"…` names an internal DTO. Not reachable from any dialog, because the identifier fields gate on `Guid.TryParse` first. |
| `AOS-R002-009` | B | **CLOSED_AS_HARNESS_ERROR** | "The command runs and nothing appears." The tab probe opened `ReviseThesisDialog` by selecting the tab, selecting the record and then running the command. The dialog was never broken; the pass was not reaching the state its handler guards on. Reviewer defects 9, 16, 17 and 21. |

---

## 3. `AOS-R002-004`, stated precisely

It is the one that moved most, and it ends nowhere near where it started.

**As filed (Phase A):** focus does not enter the dialog when it opens; it stays
on the page behind. S2, accessibility.

**Correction one (Phase B, defect 7).** `Same()` compared screen rectangles as
well as identity, so a node that had moved by a pixel was a different node. That
produced six false "focus escaped" readings and "focus restored on 0 of 30" — the
real answer was all thirty. The finding survived, reduced to two dialogs.

**Correction two (Phase C, defect 23).** The two survivors were
`RecordContractVersionDialog` and `RecordOfferDialog`, and Phase C's wider pass
added two more: `FinanceReasonDialog` and `IntelligenceReasonDialog`. Reading
their evidence properly rather than counting it:

```
FinanceReasonDialog
  initial focus:   Edit ReasonBox
  tab cycle:       CloseButton -> ReasonBox -> CloseButton -> ReasonBox
  buttons: []   fields: []
```

Focus was on the dialog's own text box, and every tab stop was one of its own
controls. The four dialogs flagged were **exactly** the four whose control
inventory came back empty, across 46 observations — the pass had been inspecting
the empty popup that WinUI hosts the dialog behind, and an empty subtree makes
every containment check fail.

**Verified by correction, not by assertion.** `Detectors.DialogRoot` now requires
a candidate to have content, and the re-run flips exactly the four affected
dialogs while leaving two controls untouched:

| | before | after |
| --- | --- | --- |
| `FinanceReasonDialog` | focus-in=NO | focus-in=yes, 2 buttons, 1 field |
| `IntelligenceReasonDialog` | focus-in=NO | focus-in=yes, 2 buttons, 1 field |
| `RecordContractVersionDialog` | focus-in=NO | focus-in=yes, 3 buttons, 6 fields |
| `RecordOfferDialog` | focus-in=NO | focus-in=yes, 6 buttons, 8 fields |
| `NewPersonDialog` *(control)* | focus-in=yes | unchanged |
| `CreateProjectDialog` *(control)* | focus-in=yes | unchanged |

Across all 51 opened dialogs: focus entered **51 of 51**, Tab and Shift+Tab
stayed inside **51 of 51**, Escape closed **51 of 51**, focus restored
**51 of 51**.

**Status: CLOSED_AS_HARNESS_ERROR.** The product never had this defect. The
historical claim is preserved in the Phase A and Phase B records and is superseded
here rather than deleted.

---

## 4. What is not claimed

Nothing here closes a finding on the basis of not having seen it. Every closure
above rests on contradicting evidence plus an identified mechanism:

- `AOS-R002-004` — the four affected dialogs are exactly the four with an empty
  inventory, and correcting the cause flips exactly those four.
- `AOS-R002-009` — the dialog was opened, by a probe that selects the tab and the
  record before running the command.
- `AOS-R001-020` — the status line was read before and after the keystroke.
- `AOS-R002-002` and `AOS-R002-006` — closed by Repair Wave 003E-A, with
  integration tests covering the capability that closed them.

None is closed because a later pass was quieter.
