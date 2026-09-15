# Audit 002 — the reviewer defect ledger

§15. Twenty-six defects in the reviewer, found across four phases. This is audit
provenance, not an apology: twenty of the twenty-six would have produced a false
claim against AgencyOS, and the only reason none of them did is that each was
caught by the reviewer contradicting the server, the source, or itself.

The governing rule this ledger exists to enforce:

> **A harness limitation must not be turned into a product finding.**

Columns: **FP** — could produce a false positive against the product. **FN** —
could hide a real defect. **Test** — a deterministic regression test now pins it.

---

## Phase A

| # | Bad assumption | Affected measurement | FP | FN | Test |
| ---: | --- | --- | :-: | :-: | :-: |
| 1 | A UIA pattern's `ProgrammaticName` is `"Pattern"`, so `"Pattern.Pattern"` could be trimmed | Three accessibility detectors and the clipping detector could never fire | ✓ | ✓ | `DetectorControlTests` |
| 2 | `GetDpiForWindow` reports the real scale | Every effective-unit measurement on a 150% display | ✓ | ✓ | `GeometryTests` |
| 3 | Any command carrying a workspace should navigate | `sync.now` / F9 | ✓ | | `ScannerTests` |
| 4 | Virtual-key codes type the characters they name | Palette input, mapped through the Hebrew layout | ✓ | ✓ | `KeyboardTests` |
| 5 | A dialog's own code-behind is its opener | All 61 dialogs reported unopenable | ✓ | | `DialogScannerTests` |

**Invalidated historical evidence:** Audit 001's three accessibility detectors
reporting zero, its clipping analysis, and its record of F9 as broken.
**Disposition:** all five corrected in Audit 001R; `AOS-R001-020` reclassified;
Audit 001 preserved unedited with an additive amendment.

Defect 3 is the one that reached a filed finding. **Audit 001 recorded F9 as
broken because of it**, and `sync.now` was working as designed the whole time.

---

## Phase B

| # | Bad assumption | Affected measurement | FP | FN | Test |
| ---: | --- | --- | :-: | :-: | :-: |
| 6 | A missing `x:Name` means a missing control | 24 reachable dialogs reported unreachable | ✓ | | `DialogScannerTests` |
| 7 | Two nodes are the same only if their rectangles match | Focus containment and restoration | ✓ | | `DetectorControlTests` |
| 8 | A `ContentDialog` and its popup host are distinguishable | "Extra modal" count — 30 false positives | ✓ | | marked not measurable |
| 9 | "The palette ran the command" means "the dialog appeared" | 19 tab-hosted dialogs blamed on the fixture | ✓ | | verified at runtime |
| 10 | The idempotency header is `Idempotency-Key` | Double-submission safety across every domain | ✓ | ✓ | corrected before reporting |

**Invalidated historical evidence:** Phase A's "focus restored on 0 of 30" (real
answer: all 30), its six "focus escaped" readings (real answer: one or two), its
30 extra-modal readings, and its attribution of 19 blocked dialogs to fixture
data.

Defect 10 is the starkest. It would have reported that AgencyOS deduplicates
nothing — a confident S2 against a system whose idempotency works correctly on
every shape since sampled.

---

## Phase C

| # | Bad assumption | Affected measurement | FP | FN | Test |
| ---: | --- | --- | :-: | :-: | :-: |
| 11 | A list item on the window is a record on the page | Row selection picked the navigation pane's **settings item**, added by 003E-A, and navigated away | ✓ | | scoped to `ContentHost` |
| 12 | A short value in one field is an incomplete form | Six dialogs recorded as failing silently; the server had answered `201` | ✓ | | probe reproduces at the API first |
| 13 | An `InfoBar`'s message arrives as a `Group` | Whether a refusal was shown at all | ✓ | | `ValidationDetectorTests` |
| 14 | A dialog that closed was refused | Two dialogs classified `UNASSOCIATED`; the server had answered `204` | ✓ | | `Validation_ADialogThatClosedWithoutARefusalSimplyWorked` |
| 15 | A listing's length is the record count | Idempotency deduplication on People | ✓ | ✓ | marker-based counting |
| 16 | The tab walk is only needed when the opener cannot be invoked | Every palette-opened dialog: only the showing tab was tried | ✓ | | verified by the modal |
| 17 | `FindAll(TabItem)` returns one level of tabs | Two-level pages: an inner tab selected under the wrong outer tab | ✓ | | two-level walk |
| 18 | A guard is `if (...) { return; }` | Guards that explain themselves first were never recorded | ✓ | | rescanned inventory |
| 19 | Pressing Enter measures the default action without changing anything | Escape readings, and **the application's own lifetime** | ✓ | | default read from markup |
| 20 | `ElementNotAvailableException` means the window is gone | "The application closed" — while the process was still running | ✓ | | `ReviewApp.IsRunning` |
| 21 | A `TabView`'s strip is not a list | "Select a row in every list" undid the tab just selected | ✓ | | strip excluded |
| 22 | The guard reading before the walk describes the walk | Blocker text named the wrong tab's list | | ✓ | per-tab detail kept |
| 23 | Any popup carrying the dialog's name is the dialog | **`AOS-R002-004` in its entirety** — focus entry, Tab containment, Shift+Tab containment | ✓ | ✓ | `DialogRoot_IgnoresTheEmptyHostEvenWhenItCarriesTheName` |

**Invalidated historical evidence:**

- Phase A/B's `AOS-R002-009` ("the command runs and nothing appears") —
  **CLOSED_AS_HARNESS_ERROR**, defects 9, 16, 17 and 21.
- Phase B's seventeen "blocked" dialogs — **INVALIDATED_BY_HARNESS_DEFECT,
  RETESTED**; most opened once 16, 17, 18 and 21 were corrected.
- The `escape-closed=NO` readings from the crashed Phase C pass — defect 19,
  **not reported**.
- The first Phase C validation pass's six silent-failure readings — defects 12,
  13 and 14, **not reported**.

---

## Phase D

| # | Bad assumption | Affected measurement | FP | FN | Test |
| ---: | --- | --- | :-: | :-: | :-: |
| 24 | A palette row can be confirmed by its label | Which command Enter ran. `Contains` cannot tell `Connect mailbox` from **Dis**`connect mailbox`, so the pass could confirm one and run the other | ✓ | ✓ | `MatchingOnTheIdentifierCan` |
| 25 | A guard that names no satisfied list means the opener is not here | Button openers. The guard names are read by a regex over a window of source and pick up neighbouring methods' guards, so `RecordSignatureDialog` acquired `OptionList` and the skip then refused to press `SignatureButton` on the tab it lives on | ✓ | | re-run of the five button-opened dialogs |
| 26 | "Ran but nothing appeared" is an adequate account of a failure | Every unopened dialog's blocker. The same sentence covered a disabled button, a missing button, and a command that genuinely produced nothing | | ✓ | the evidence in `AOS-R002-019` is the restored reason |

**Invalidated historical evidence:** Phase C's classification of
`ConnectMailboxDialog`, `IngestAttachmentDialog`, `ResolveParticipantDialog` and
`RecordSignatureDialog` — four of the eight — all **RETESTED** in Phase D and
three reclassified. Phase C's `PRECONDITION_NOT_MET` for `ConnectMailboxDialog`
was an inference from reading the handler rather than a measurement, and it was
wrong: the server reports one provider.

Defect 26 is the one that mattered. It was not making a false claim — it was
making *no* claim, and three dialogs sat in `INCONCLUSIVE` for a phase because of
it. Restoring the opener's own words turned them into a product finding in a
single run.

---

## The five that mattered most

Called out because each was one step from a filed finding against working
product behaviour.

### Defect 12 — the probe's invalid input was valid

The probe typed a short value into one field, submitted, and six dialogs closed
without a word. The obvious reading is a silent failure. The server disagreed:

```
people      records containing 'review-': 1   review-012956
companies   records containing 'review-': 1   review-012945
```

`NewPersonDialog` shows six fields and the domain requires one. The dialogs
closed because the work had been done.

### Defect 14 — a closed dialog is not a refusal

`ChangeProjectStageDialog` closed on 607 characters of reason text. Reproduced
against the API:

```
POST projects/{id}/stage    reason of 607 characters    ->  204
```

A reason has no length limit. Two dialogs would have been reported as putting
their error message in the wrong place, for refusing nothing.

### Defect 19 — the reviewer closed the client, then blamed the client

The Enter probe closed the dialog, the Escape test then pressed Escape at the
shell, and the keystrokes after it reached the title bar. The Application event
log records exactly one crash in that period:

```
Application: AgencyOS.Reviewer.exe
Description: The process was terminated due to an unhandled exception.
```

**AgencyOS.Windows.exe does not appear in the log at all.** Had this been
reported, it would have said the client can be closed by keyboard operation of a
dialog.

### Defect 23 — the pass inspected the empty half of four dialogs

WinUI hosts a `ContentDialog` behind popup windows, and more than one can carry
the dialog's name while only one carries its content. `Modal()` matched on the
name, and for four dialogs it matched the empty one.

The consequence was not a missing inventory, which would have been obvious. It
was a *focus finding*: with no nodes to compare against, every containment check
failed, and the pass reported that focus had not entered the dialog and that Tab
and Shift+Tab escaped it.

The evidence contradicted itself on its own face:

```
FinanceReasonDialog
  initial focus:   Edit ReasonBox
  tab cycle:       CloseButton -> ReasonBox -> CloseButton -> ReasonBox
  buttons: []   fields: []
```

Focus was inside the dialog the whole time. The four dialogs flagged were
**exactly** the four whose control inventory came back empty — a perfect
correlation across 46 observations.

**Would have claimed:** `AOS-R002-004` — "focus does not enter the dialog when it
opens; it stays on the page behind" — an S2 accessibility defect, filed in Phase
A, carried through Phase B, and resting on nothing.

**Corrected** in `Detectors.DialogRoot`: content decides which candidate is the
dialog, and the title only breaks ties between candidates that have some. The
re-run flips exactly the four affected dialogs and leaves the controls alone:

```
                                before    after
FinanceReasonDialog             focus-in=NO   focus-in=yes, 2 buttons, 1 field
IntelligenceReasonDialog        focus-in=NO   focus-in=yes, 2 buttons, 1 field
RecordContractVersionDialog     focus-in=NO   focus-in=yes, 3 buttons, 6 fields
RecordOfferDialog               focus-in=NO   focus-in=yes, 6 buttons, 8 fields
NewPersonDialog    (control)    focus-in=yes  focus-in=yes, unchanged
CreateProjectDialog (control)   focus-in=yes  focus-in=yes, unchanged
```

Across all 51 opened dialogs the corrected pass now reports focus entering
**51 of 51**, Tab and Shift+Tab staying inside **51 of 51**, Escape closing
**51 of 51**, and focus restored **51 of 51**.

### Defect 21 — the tab strip is a list

A `TabView`'s strip is a list and its rows are the tabs, so selecting the first
row in every list undid whichever tab had just been chosen. The saved tree is
unambiguous: after visiting all seven Finance tabs, the only list in the tree was
`ReceivableList` — the **first** tab's.

This one defect accounts for most of what Phase B called blocked. Correcting it
opened `AllocatePaymentDialog` on the next run, from a button, with focus
entering correctly.

---

## What this adds up to

Twenty-six reviewer defects across Audit 002: five in Phase A, five in Phase B,
thirteen in Phase C, three in Phase D. **Twenty of the twenty-six would have
produced a false claim against the product**, several of them confident and severe — a
silent-failure finding, two workflow findings, one that would have said the
client can be closed by keyboard operation of a dialog, and one S2 accessibility
finding that survived two phases before the evidence under it was read properly.

Two filed findings were closed as harness errors on that evidence:
`AOS-R002-004` and `AOS-R002-009`. One more, `AOS-R001-020`, was filed by Audit
001 for the same reason and is now closed outright.

Phase C found more defects than the two earlier phases combined. That is what
should happen: the phases before it could not reach most of the product, so most
of the reviewer had never been exercised against anything that could contradict
it.

## What the ledger is for

Three rules came out of it, and they are the audit's main methodological result:

1. **Reproduce at a layer the reviewer does not control.** Defects 12, 14, 15 and
   20 were all caught by asking the server or the operating system what had
   actually happened.
2. **Prove a detector against known-positive input before trusting a zero.**
   Every detector added in Phase C ships with controls in both directions; three
   of them fail against the version that existed an hour earlier.
3. **A negative result must name what it looked for.** "The dialog did not
   appear" is not evidence. `MailboxList: not on the page` is — and it is what
   exposed defects 17 and 21.

## Evidence

Each correction is a commit on this branch with a test that fails without it.
The control suite is `tests/AgencyOS.Tests.Reviewer/`.
