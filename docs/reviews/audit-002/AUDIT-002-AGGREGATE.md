# Audit 002 — aggregate report

**This is the current truth for Audit 002.** It does not replace Phase A's or
Phase B's documents, which stand exactly as issued. Where a phase's number or
claim has since been superseded, it is named here rather than left for a reader
to infer.

**Product baseline:** `6e9b66f`. **Product code changed by Audit 002: none, in
any phase.**

---

## 1. What each phase did

| Phase | Scope | Dialogs opened | Outcome |
| :-: | --- | ---: | --- |
| **A** | Inventory, first runtime pass, four reclassifications | 30 of 57 | complete; closed the audit prematurely, corrected in `AUDIT-002-STATUS.md` |
| **B** | Unblock the 27, mutations, roles, validation, idempotency, concurrency | 38 of 57 | complete; §30 still unmet, said so |
| **C** | Use the new role capability to close §30 | **51 of 59** | complete; §30 still unmet on two requirements |
| **D** | Closure only: settle the eight unopened, contain the capture | 51 of 59 | complete; §30 still unmet — the eight now all have a cause |

---

## 2. Historical claims that are no longer true

Listed explicitly, because forcing a reader to work out which old numbers are
obsolete is how an audit record rots.

| Claim | Where | Now |
| --- | --- | --- |
| "61 dialogs" | Audit 001, Phase A, Phase B | **63** — Repair Wave 003E-A added `AddMemberDialog` and `ChangeMemberRoleDialog` |
| "Focus restored on 0 of 30" | Phase A | **all 30** — reviewer defect 7 |
| "Six dialogs let focus escape" | Phase A | **none** — reviewer defects 7 and 23 |
| "30 dialogs show an unexpected second modal" | Phase A | **not measurable** — reviewer defect 8 |
| "19 tab-hosted dialogs blocked by fixture data" | Phase B | **harness** — reviewer defects 9, 16, 17, 21; most now open |
| "17 dialogs blocked" | Phase B | **8**, each with a named cause |
| `AOS-R002-004` — focus does not enter the dialog | Phase A, S2 | **CLOSED_AS_HARNESS_ERROR** — reviewer defect 23 |
| `AOS-R002-009` — the command runs and nothing appears | Phase B, S3 | **CLOSED_AS_HARNESS_ERROR** — reviewer defects 9, 16, 17, 21 |
| `AOS-R001-020` — F9 does nothing | Audit 001 | **NO_DEFECT** — navigation is intentional, acknowledgement observed |
| `AOS-R002-002`, `AOS-R002-006` — no second user, membership write-once | Phase A/B, S2 | **CLOSED** by Repair Wave 003E-A |
| "Twelve of fourteen §30 requirements met, two unmet" | Phase C | **Eleven of fourteen, three unmet.** Phase C's own table marked requirements 2, 3 **and 4** as NOT MET while its verdict counted two. An arithmetic slip in Phase C, corrected here rather than in that document |
| "2 harness limitations, 3 inconclusive" (5 of 8) | Phase C summary | **8 of 8**, partitioned: 4 product defect, 3 precondition, 1 harness |
| `ConnectMailboxDialog` — `PRECONDITION_NOT_MET` | Phase C | **wrong** — the server reports one provider; Phase C inferred it from the handler instead of asking |
| `IngestAttachmentDialog`, `ResolveParticipantDialog` — `HARNESS_LIMITATION` | Phase C | **`PRECONDITION_UNACHIEVABLE`** — no message exists and no route creates one |
| "approximately 23 reviewer defects" | Phase C | **26** |
| "the API emitted 235 GB of stdout" | Phase C closing note | **not established, and measurement contradicts it** — ~96 KB per request and ~1.4 KB/s idle; roughly 99% of that file was not product output |
| "Phase B's non-member was refused" | Phase B | **superseded** — that fixture subject was *unauthenticated*. An authenticated non-member is refused `403` on every route, which is the question §30 asked |

---

## 3. Findings — the current set

Fifteen open, four closed, across four phases — nineteen raised in all.

| Id | Sev | Phase | Status | What |
| --- | :-: | :-: | --- | --- |
| `AOS-R002-001` | S1 | A | CONFIRMED | Query-translation defects surfacing as `500` with a trace id |
| `AOS-R002-002` | S2 | A | **CLOSED** | No way to create a second user — 003E-A |
| `AOS-R002-003` | S3 | A | CONFIRMED | 17 dialogs with no opening control; 4 with no path at all |
| `AOS-R002-004` | S2 | A | **CLOSED_AS_HARNESS_ERROR** | Focus entry — reviewer defect 23 |
| `AOS-R002-005` | S2 | A | CONFIRMED | 3 of 63 dialogs named by any test |
| `AOS-R002-006` | S2 | B | **CLOSED** | Membership write-once — 003E-A |
| `AOS-R002-007` | S3 | B | CONFIRMED | `409` names an identifier the operator has never seen |
| `AOS-R002-008` | S3 | B | CONFIRMED | `400` names an internal DTO |
| `AOS-R002-009` | S3 | B | **CLOSED_AS_HARNESS_ERROR** | Palette command "does nothing" |
| `AOS-R002-010` | S3 | C | NEW | A refused entry is reported after the dialog has closed, and the entry is lost |
| `AOS-R002-011` | S3 | C | NEW | No dialog declares a programmatic association between a message and its field (0 of 63) |
| `AOS-R002-012` | S4 | C | NEW | A refused **create** is titled *"Could not load people"* |
| `AOS-R002-013` | S4 | C | NEW / observation | `POST /people` accepts `not-an-email` |
| `AOS-R002-014` | S4 | C | NEW | Refusals name internal permission strings |
| `AOS-R002-015` | S4 | C | NEW | Organization/Members is outside the palette — `COMMAND_SURFACE_INCONSISTENCY` |
| `AOS-R002-016` | S3 | C | NEW | Enter commits in 26 dialogs and cancels in 36, by no rule |
| `AOS-R002-017` | S3 | C | NEW | `POST /documents` answers `500` to any non-multipart content type |
| `AOS-R002-018` | S3 | D | NEW | The command palette announces `PaletteCommand { Id = …, Title = … }` instead of the command |
| `AOS-R002-019` | S3 | D | NEW | Four dialogs: the opener is invoked, the guards are satisfied, and nothing appears and nothing is said |

Carried from Audit 001: `AOS-R001-006` **CONFIRMED** (20 fields, 13 dialogs),
`AOS-R001-010` **CONFIRMED_API_UI_GAP** (scopes/team), `AOS-R001-013`
**S2 / PARTIAL**, `AOS-R001-020` **NO_DEFECT, closed**.

### Severity totals, open findings only

| S0 | S1 | S2 | S3 | S4 |
| :-: | :-: | :-: | :-: | :-: |
| 0 | 1 | 1 | 9 | 4 |

---

## 4. Provenance

Twenty-six reviewer defects across the audit. **Twenty would have produced a
false claim against the product.** Three filed findings were closed as harness
errors on that evidence — two from Audit 002 and one from Audit 001.

The full ledger, with each defect's bad assumption, affected measurement,
false-positive potential, invalidated evidence and regression test, is
[`phase-c/AUDIT-002C-REVIEWER-DEFECTS.md`](phase-c/AUDIT-002C-REVIEWER-DEFECTS.md).

This is not incidental. An audit that had not been checking itself this hard
would have shipped an S2 accessibility defect, an S3 workflow defect, a
silent-failure finding and a claim that the client can be closed by keyboard —
all against a product that was behaving correctly.

---

## 5. The §30 verdict

Eleven of fourteen requirements met. Three unmet, and they are one gap counted
three times: **eight reachable dialogs have never been opened** — four because
the product does not open them (`AOS-R002-019`), three because their precondition
cannot be built without writing to the database directly, one because the
reviewer does not scroll a pane into view.

Full detail: [`phase-d/AUDIT-002D-COVERAGE.md`](phase-d/AUDIT-002D-COVERAGE.md).

```
AUDIT 002 PHASE D COMPLETE — AUDIT 002 REMAINS OPEN — NO PRODUCT REPAIRS APPLIED
```

Unmet gates:

1. **Every reachable dialog opened at least once** — 51 of 59.
2. **Cancel/close behaviour observed for every reachable dialog** — 51 of 59.
3. **Accessibility/focus inspection for every reachable dialog** — 51 of 59.

Gates 2 and 3 are satisfied for every dialog that was opened; they fail only
because (1) does.

### 5.1 Amendment — after Repair Wave 003A

**Added, not substituted. Everything above is the position Audit 002 itself
reached, and it stands.** Repair Wave 003A repaired `AOS-R002-019` and rechecked
only the four dialog rows that finding named. It also repaired `AOS-R002-017`.

| | At Phase D | After 003A |
| --- | ---: | ---: |
| Every reachable dialog opened | 51 of 59 | **55 of 59** |
| Cancel/close observed | 51 of 59 | **55 of 59** |
| Accessibility/focus inspected | 51 of 59 | **55 of 59** |

| Finding | At Phase D | After 003A |
| --- | --- | --- |
| `AOS-R002-019` | NEW, S3 | **REPAIRED** — all four dialogs open; regression tests fail on `6e9b66f` |
| `AOS-R002-017` | NEW, S3 | **REPAIRED** — the upload routes answer `415` before the form binder runs |

The four dialogs still unopened are `ApproveAiActionDialog`,
`IngestAttachmentDialog` and `ResolveParticipantDialog`
(`PRECONDITION_UNACHIEVABLE`) and `RecordSignatureDialog`
(`HARNESS_LIMITATION`). The §30 verdict is unchanged in kind: three requirements
unmet, one gap counted three times.

```
AUDIT 002 REMAINS OPEN
```

Detail: [`../repair-003a/AUDIT-002-NARROW-RECHECK.md`](../repair-003a/AUDIT-002-NARROW-RECHECK.md).

### 5.2 Amendment — final closure slice

**Added, not substituted.** The closure slice settled the final four dialogs and
touched nothing else. No product code changed.

| | At Phase D | After 003A | After closure |
| --- | ---: | ---: | ---: |
| Every reachable dialog opened | 51 of 59 | 55 of 59 | **57 of 57** |
| Cancel/close observed | 51 of 59 | 55 of 59 | **57 of 57** |
| Accessibility/focus inspected | 51 of 59 | 55 of 59 | **57 of 57** |

The denominator moved from 59 to 57 because two dialogs were reclassified
`INTENTIONALLY_EXTERNAL_PRECONDITION` — state only an external actor can create,
established by tracing every layer and by measurement, per §7.

| Dialog | Final | Why |
| --- | --- | --- |
| `RecordSignatureDialog` | **OPENED** | its palette opener was never tried; `SignatureButton` is a separate finding |
| `ResolveParticipantDialog` | **OPENED** | needs a participant, not an inbound message; the outbound send path creates one |
| `ApproveAiActionDialog` | external-only | an approval comes from a model's tool call; no route creates one, and no real provider exists |
| `IngestAttachmentDialog` | external-only | attachment rows come only from `MailboxSynchronizer`; Graph is not configured |

**Two new findings**, both filed and unrepaired: `AOS-R002-020` (the connect
dialog offers a mailbox visibility the server refuses) and `AOS-R002-021` (two
contract commands are unclickable at 1600x1000).

**Fourteen of fourteen §30 requirements met.**

```
AUDIT 002 COMPLETE — NO ADDITIONAL PRODUCT REPAIRS APPLIED IN FINAL CLOSURE
```

Detail: [`final-closure/AUDIT-002-FINAL-CLOSURE-SUMMARY.md`](final-closure/AUDIT-002-FINAL-CLOSURE-SUMMARY.md).

## 6. Where the documents are

| Phase | Location |
| :-: | --- |
| A | `docs/reviews/audit-002/AUDIT-002-*.md` |
| status correction | `docs/reviews/audit-002/AUDIT-002-STATUS.md` |
| B | `docs/reviews/audit-002/phase-b/` |
| C | `docs/reviews/audit-002/phase-c/` |
| D | `docs/reviews/audit-002/phase-d/` |
| evidence | `artifacts/reviewer/run-002-audit/`, `run-002-phase-b/`, `run-002-phase-c/` |

---

## 7. Finding dispositions after Audit 002 closed

**Audit 002 is COMPLETE and is not reopened by this section.** Findings it filed
are being repaired by later waves; their dispositions are recorded here as they
change, so the register stays in one place. Nothing above is rewritten.

| Finding | Filed as | Now | By |
| --- | --- | --- | --- |
| `AOS-R002-019` | NEW, S3 | **REPAIRED** | Repair Wave 003A |
| `AOS-R002-017` | NEW, S3 | **REPAIRED** | Repair Wave 003A |
| `AOS-R001-006` | CONFIRMED | **PARTIALLY_REPAIRED** | Repair Wave 003B |

**`AOS-R001-006`.** Fourteen of the twenty typed-identifier fields became pickers
or derived context. Six remain, deliberately: four owner/lead fields awaiting an
owner decision, `LinkRecordDialog.TargetIdBox` awaiting a design decision, and
`AddIntelligenceSubjectDialog.IdBox` in a dialog nothing constructs. The finding
is not called repaired while three create dialogs still require an owner nobody
can name. See
[`../repair-003b/REPAIR-003B-REPORT.md`](../repair-003b/REPAIR-003B-REPORT.md).

Three of Audit 002's per-field readings were corrected during that repair, with
evidence, and are recorded there rather than edited into Phase B:

- `CreateDealDialog.OpportunityIdBox` and `CreateContractDialog.DealIdBox` were
  classified derivable on the reading that a deal is opened from an opportunity
  and a contract from a deal. Neither workflow exists in the client.
- `CreateOpportunityDialog.SubjectIdBox` references a talent profile, package,
  project role or project — not a person or company.
- `AddPackageElementDialog.TargetIdBox` has six kinds, not three.

### A new observation

**`AttachToRoleDialog`, `RecordPitchDialog` and `RecordSubmissionDialog` crash
the Windows client** in the current synthetic fixture — a stowed exception,
`0xc000027b`, in `Microsoft.UI.Xaml.dll`. Repair Wave 003B found it while
re-verifying its own work, reproduced it on the pre-repair baseline, and ruled out
the window size. Audit 002's closure slice opened all three against earlier data,
so it is a data-dependent framework crash rather than a regression of anything
Audit 002 measured. **Not yet filed as a numbered finding and not diagnosed** —
it needs its own investigation, starting from the Windows Error Reporting
minidump.

### After Repair Wave 003A.1

Added by Repair Wave 003A.1. Nothing above is edited.

| Finding | Filed as | Now | By |
| --- | --- | --- | --- |
| `AOS-R002-022` | NEW, **S1** — the observation above, numbered | **REPAIRED** | Repair Wave 003A.1 |
| `AOS-R002-023` | NEW, S2 — 003B's material pickers never listed a material | **REPAIRED** | Repair Wave 003A.1 |
| `AOS-R002-024` | NEW, S3 — Projects and Pipeline show "Invalid request" instead of the server's reason | **DEFERRED** (suggested 003C) | — |

**`AOS-R002-022`.** The three dialogs were never the fault. Eleven detail tabs on
Projects, Pipeline, Deals and Contracts held a padded `ListView` as the whole
content of a `ScrollViewer`; shown empty, each raised `LayoutCycleException`
(`0x802B0014`), which nothing handles, and the framework ended the process with
`0xC000027B`. The review harness walked into those tabs after the dialogs' first
attempt was refused and recorded the dialog it was attempting. See
[`../repair-003a1/REPAIR-003A1-ROOT-CAUSE.md`](../repair-003a1/REPAIR-003A1-ROOT-CAUSE.md).

**Two corrections to the observation above**, made here rather than there:

- "Audit 002's closure slice opened all three against earlier data" is not
  right. The closure slice did not attempt them. They were last opened in
  **Phase B** (2026-09-14), and **Phase C's final pass** (2026-09-15 02:37Z)
  already recorded all three — with `AnswerOfferDialog` and `MoveTargetDialog` —
  as "the application window disappeared".
- "A data-dependent framework crash" is right in effect but not in mechanism. The
  data decided whether the harness reached an empty tab; the tab itself crashes
  with or without data elsewhere, and Projects › Attachments crashes always.

**`AOS-R002-001`, scope.** 003A.1 observed the same `500` —
`Cannot write DateTimeOffset with Offset=03:00:00` — from
`RecordSubmissionDialog`, which the finding does not list. Not repaired here; it
is why 003A.1 could not save a pitch or a submission through the interface on a
UTC+3 machine.
