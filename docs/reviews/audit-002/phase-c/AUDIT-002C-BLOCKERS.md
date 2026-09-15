# Audit 002 Phase C — what each unopened dialog is waiting for

§2. "Blocked" is not a classification, and Phase B was told not to leave one.

Every dialog that did not appear is constructed inside a method that returns
early unless something is true. That something is the blocker, and it is read out
of the handler rather than inferred from what the screen looked like.

---

## The guards, read from the handlers

Seventeen dialogs were unopened entering this phase. Their handlers say why:

| Dialog | Page | Returns early unless |
| --- | --- | --- |
| `AllocatePaymentDialog` | Finance | `PaymentList.SelectedItem is PaymentResponse` |
| `ApproveAiActionDialog` | AI | `_approvals?.Selected is { }` |
| `ChangeVerificationDialog` | Intelligence | `_signal?.Signal is { }` |
| `ConnectMailboxDialog` | Communications | `_mailboxes is not null` and `providers.Count > 0` |
| `CreatePredictionDialog` | Intelligence | `_api` and `_predictions` are loaded |
| `FinanceReasonDialog` | Communications | `MailboxList.SelectedItem is CommunicationAccountResponse` |
| `FinanceReasonDialog` | Documents | `_detail?.Document is { }` |
| `FinanceReasonDialog` | Finance | `PaymentList.SelectedItem is PaymentResponse` |
| `IngestAttachmentDialog` | Communications | `_messageDetail` loaded **and** `AttachmentList.SelectedItem` |
| `IntelligenceReasonDialog` | Intelligence | `_thesis?.Thesis is { }` |
| `LinkResearchItemDialog` | Intelligence | `_researchCase?.ResearchCase is { }` |
| `MailboxVisibilityDialog` | Communications | `MailboxList.SelectedItem` |
| `RecordForecastDialog` | Intelligence | `PredictionList.SelectedItem` |
| `RecordSignatureDialog` | Contracts | `_detail?.Contract` **and** `OutstandingSignatories.Count > 0` |
| `RecordSourceDialog` | Intelligence | `_api` and `_sources` are loaded |
| `ResolveObligationDialog` | Contracts | `_detail?.Contract` **and** `ObligationList.SelectedItem` |
| `ResolveOptionDialog` | Contracts | `_detail?.Contract` **and** `OptionList.SelectedItem` |
| `ResolveParticipantDialog` | Communications | `_messageDetail` **and** `ParticipantList.SelectedItem` |
| `ResolvePredictionDialog` | Intelligence | `_predictions` **and** `PredictionList.SelectedItem` |

Two shapes, and they compose:

1. **A named list must have a row selected.** Thirteen of the nineteen openings.
2. **A parent detail must have loaded**, which only happens once its own parent
   row is chosen. The lists in shape 1 do not exist until this has happened.

---

## None of it was a data problem

The obvious explanation — the fixture has nothing to select — was tested and is
wrong. Every list the guards name has records behind it:

```
mailboxes       1     predictions     3     contracts   1
payments        1     watchlists      1     ai runs     1
receivables     1     signals         2     ai policies 0
theses          1     research cases  1     sources    19
```

Only AI policies is empty, and `ApproveAiActionDialog` guards on approvals rather
than policies.

---

## What was actually stopping it: four harness defects

Each was found by the pass contradicting the page in front of it, and each is
listed with the false claim it was making.

### The tab walk never ran for a palette-opened dialog

It ran only `if (!invoked)`. The palette always runs the command it is handed and
reports success, so for every palette opener the walk was skipped and only
whichever tab happened to be showing was tried.

### The tab walk walked one level of a two-level page

Communications has outer tabs — Messages, Outbound, Mailboxes, Desk — and, inside
a selected message, inner tabs: Message, Participants, Attachments, Filed
against. `FindAll(TabItem)` returns all eight flattened together, so the walk
selected an inner tab while the outer tab was wrong.

### The scanner recorded only guards that gave up immediately

`Preconditions` matched `if (...) { return; }`. Most of these handlers explain
themselves first — `Error("Select a mailbox first."); return;` — so their guards
were never recorded and there was no list name for the pass to act on.

### Selecting "a row in every list" selected a tab

A `TabView`'s strip is a list and its rows are the tabs. Choosing the first row
in every list therefore undid whichever tab the walk had just selected. The saved
tree proves it: after the pass visited all seven Finance tabs, the only list in
the tree was `ReceivableList` — the **first** tab's.

This one was the most expensive. It is why `PaymentList: not on the page` was
reported about the Payments tab, and correcting it opened `AllocatePaymentDialog`
on the next run, from a button, with focus entering correctly.

---

## What the pass now reports instead of "blocked"

A dialog that does not appear now names the list it was looking for and what it
found there:

```
FinancePage: no tab made the opener available
  (tried Receivables, Invoices, Payments, Commissions, Ledger, Reconciliation, Activity);
  PaymentList: not on the page
```

Three distinguishable answers, none of them "blocked":

| Verdict | Meaning |
| --- | --- |
| `GUARDED_LIST_ABSENT` | The named list is not in the automation tree on any tab. |
| `GUARDED_LIST_EMPTY` | The list is there and has no rows. |
| `NO_TAB_SATISFIED_THE_GUARD` | Every tab was tried and the guard held on all of them. |
| `WORKSPACE_UNREACHABLE` | The page could not be navigated to at all — see `AOS-R002-015`. |

The final per-dialog state is in
[`AUDIT-002C-DIALOG-MATRIX.md`](AUDIT-002C-DIALOG-MATRIX.md) and
`artifacts/reviewer/run-002-phase-c/coverage.json`.

---

## Whose limitation this is

Stated plainly because §17 turns on it: **every remaining blocker is a limitation
of the reviewer, not of the product.** Each one is a handler guarding on a
selection that a person makes by clicking, on a page that has the record to
click. None is a missing product capability, and none is a defect.

That distinction matters in one direction only. It means these dialogs are not
evidence against the product — and it also means the audit cannot claim to have
operated them.
