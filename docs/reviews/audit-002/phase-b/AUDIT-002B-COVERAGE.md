# Audit 002 Phase B — coverage

No percentage. A percentage would hide the shape of what is left.

**Product baseline:** `51541ea`, unchanged.

---

## 1. Dialogs

| | Phase A | Phase B |
| --- | ---: | ---: |
| Declared | 61 | **61** |
| Nothing constructs them | 4 | **4** |
| Reachable | 57 | **57** |
| **Opened from the real interface** | 30 | **38** |
| — by a button | 16 | 16 |
| — from the command palette | 14 | 22 |
| Still blocked | 27 | **19** |
| Constructed directly | 0 | **0** |

### The eight unblocked in Phase B

`AddPackageElementDialog`, `AttachToRoleDialog`, `ChangePackageStatusDialog`,
`ComposeMessageDialog`, `RecordAdjustmentDialog`, `RecordContractVersionDialog`,
`RecordInvoiceDialog`, `RecordNoticeDialog`.

Plus `AnswerOfferDialog` and `RecordOfferDialog`, which Phase B first *lost* —
accepting the offer moved the only deal to `TermsAgreed` and correctly left
nothing to answer — and then recovered by creating a second negotiation and
leaving it open. A fixture has to hold a record in each state a dialog needs, not
just one of each kind.

### The nineteen still blocked

| Classification | Count | Which |
| --- | ---: | --- |
| `HARNESS_BLOCKED` / `AOS-R002-009` | **12** | The Intelligence, Contracts and Finance dialogs whose handler guards on a loaded detail or a tab selection. The record exists; the dialog does not appear; the cause is not established. |
| `STILL_BLOCKED_FIXTURE` | **5** | Communications: `ConnectMailboxDialog`, `FinanceReasonDialog`, `IngestAttachmentDialog`, `LinkRecordDialog`, `MailboxVisibilityDialog`, `ResolveParticipantDialog` — a fake mailbox exists, but no message does, and the fake provider produces none. |
| `STILL_BLOCKED_FIXTURE` | **1** | `ApproveAiActionDialog` — the AI run reaches `AwaitingLocalExecution` and stops. An approval needs the device-local inference path and a model this audit will not provide. |
| `UNREACHABLE` | 4 | `AOS-R001-017`, reconfirmed. |

## 2. What the 38 opened dialogs showed

| Measurement | Result |
| --- | --- |
| Focus entered the dialog | **36 / 38** |
| Escape dismissed it | **38 / 38** |
| Tab stayed inside the modal | **36 / 38** |
| Focus after close | 38 / 38 landed on the page; none lost, none stuck in a dismissed dialog |
| Focus returned to the opener specifically | **0 / 38** |
| Accessibility detector hits | **0** |
| Row-speech detector hits | **0** |
| Unexpected second modal | **INCONCLUSIVE** — not measurable |

`RecordOfferDialog` and `RecordContractVersionDialog` fail both focus
measurements. They are `AOS-R002-004`.

The accessibility zero now covers **38 dialogs of 61**, with the corrected
detectors from Audit 001R. It is not a screen-reader certification and does not
extend to the 23 unopened.

## 3. Dialog states

| State | Coverage |
| --- | --- |
| `EMPTY` | **38** — every opened dialog inspected as it opens |
| `VALID` | **0** |
| `INVALID` | **0** |
| `PARTIALLY_FILLED` | **0** |
| `SERVER_REFUSAL` | **0** from a dialog; 9 kinds observed at the API |
| `PERMISSION_REFUSAL` | **0** — blocked, no second role exists |
| `STALE_VERSION` | **0** from a dialog; 2 observed at the API |
| `SUCCESS` | **0** from a dialog; 28 observed at the API |

**No dialog was filled in and submitted.** §6 is met only for `EMPTY`. That is the
single largest gap in Audit 002 and it is what a Phase C would start with.

## 4. §7 — validation-text association

**NOT_REVIEWED.** It requires submitting a dialog with a required field empty,
which was not done. Nothing here claims accessibility compliance from visual
proximity, and the harness was not asked a question it could not answer.

What the audit can say, from the API: every domain refusal names the valid values
(nine examples in the workflow matrix), and one class of refusal does not —
binding failures, `AOS-R002-008`.

## 5. Sections completed, partially completed and not attempted

| § | Subject | Status |
| --- | --- | --- |
| 2 | Blocker map for all 27 | **COMPLETE** — `blockers.json` |
| 3–4 | Fixture enrichment | **PARTIAL** — 6 domains built, Documents and Communications messages not |
| 5 | Reopen the 27 | **COMPLETE as an exercise** — 8 opened, 19 evidenced |
| 6 | Full dialog operation coverage | **PARTIAL** — `EMPTY` only |
| 7 | Validation-text association | **NOT_REVIEWED** |
| 8–9 | Multi-role, authorization UX | **BLOCKED** — `R002-002`, `R002-006` |
| 10 | Stale-permission screen | **BLOCKED** — same cause |
| 11 | Idempotency / double-submit | **PARTIAL** — API sampled, dialog not |
| 12 | Stale version / 409 | **PARTIAL** — API sampled, dialog not |
| 13 | Mutations across 11 domains | **PARTIAL** — 6 complete, 2 partial, 3 not reviewed |
| 14 | Finance safety | **PARTIAL** — recordkeeping exercised; no external rail touched; presentation and irreversibility affordances not reviewed |
| 15 | Communications | **PARTIAL** — fake mailbox connected; no message exists |
| 16 | Documents | **NOT_REVIEWED** |
| 17 | `AOS-R001-006` per field | **COMPLETE** |
| 18 | `AOS-R001-010` | **COMPLETE** |
| 19 | `AOS-R001-013` | **COMPLETE** |
| 20 | `AOS-R001-020` | **PARTIAL** — source settled, runtime acknowledgement not observed |
| 21 | Dead dialogs | **COMPLETE** |
| 22 | Reviewer reliability | **COMPLETE** — 2 defects found, fixed, tested |

## 6. Data

Synthetic throughout, in the disposable review organization on `agencyos_w2`.
28 objects created through canonical API commands. No direct database writes. Two
direct reads, identified. No real email, mailbox, AI provider, external party,
payment or private document. No screenshot contains real data.
