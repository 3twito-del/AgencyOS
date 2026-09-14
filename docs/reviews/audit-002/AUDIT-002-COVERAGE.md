# Audit 002 — coverage

What was looked at, and what was not. No percentage, because a percentage would
hide the shape of the gap.

**Product baseline:** `51541ea`, unchanged.
**Evidence:** `artifacts/reviewer/run-002-audit/`

---

## 1. The brief's own gate (§30)

| Requirement | Status |
| --- | --- |
| Every current dialog is inventoried | **COMPLETE** — 61 of 61 |
| Every reachable dialog is opened at least once | **PARTIAL** — 30 of 57 |
| Every reachable dialog has Cancel/close observed | **PARTIAL** — 30 of 57 |
| Every dialog gets accessibility/focus inspection | **PARTIAL** — 30 of 57 |
| Representative mutation for every major domain | **PARTIAL** — 1 domain of 11 |
| Multi-role evidence on permission-sensitive workflows | **BLOCKED** — see `AOS-R002-002` |
| `AOS-R001-006` reclassified per instance | **COMPLETE** — 20 fields across 13 dialogs |
| `AOS-R001-010` runtime-reclassified | **COMPLETE** |
| `AOS-R001-013` refined | **COMPLETE** |
| `AOS-R001-020` runtime-reclassified | **COMPLETE** |

**Audit 002 does not meet §30.** Three gates are partial and one is blocked. It is
reported as complete in the sense of "this pass is finished and here is exactly
where it got to", not in the sense of "the minimum coverage was reached".

## 2. Dialogs

| | Count |
| --- | ---: |
| Declared in source | **61** |
| Nothing constructs them (`AOS-R001-017`) | **4** |
| Reachable — a control is wired to the handler | **40** |
| Reachable only by a command (`AOS-R002-003`) | **17** |
| **Opened from the real interface** | **30** |
| — by pressing a button | 16 |
| — by running a command from the palette | 14 |
| Attempted and declined to open | **27** |
| Constructed directly by this audit | **0** |

### Why 27 did not open

| Reason | Count | What it means |
| --- | ---: | --- |
| `COMMAND_RAN_HANDLER_DECLINED` | 24 | The command ran and the page's handler returned early because its precondition was not satisfied — a contract, an obligation, an offer, a mailbox or a message had to be selected and the fixture has none. Not shown to be a defect. |
| Opener absent or disabled | 3 | `ApproveAiActionDialog` (needs a pending AI approval), `AttachToRoleDialog` (needs a project role), `ChangePackageStatusDialog` (needs a package). |

All 27 are **BLOCKED by absent synthetic state**, not shown to be unreachable. The
audit enriched the fixture for Intelligence — six records created through
canonical commands — which is what turned 11 Intelligence dialogs from blocked to
opened. The same work for Contracts, Finance, Communications and Packages was not
done.

## 3. Per-dialog columns

The full matrix is `AUDIT-002-DIALOG-MATRIX.md`. Summarised:

| Column | COMPLETE | BLOCKED | N/A |
| --- | ---: | ---: | ---: |
| INVENTORIED | 61 | 0 | 0 |
| REACHABLE (classified) | 61 | 0 | 0 |
| OPENED | 30 | 27 | 4 |
| CANCEL | 30 | 27 | 4 |
| ESCAPE | 30 | 27 | 4 |
| FOCUS | 30 | 27 | 4 |
| ACCESSIBILITY | 30 | 27 | 4 |
| EMPTY state | 30 | 27 | 4 |
| VALID / INVALID / SUCCESS / REFUSAL / CONFLICT | 0 | 57 | 4 |
| OWNER | 30 | 27 | 4 |
| MEMBER / OBSERVER / RESTRICTED | 0 | 61 | 0 |

Every opened dialog was inspected in its **EMPTY** state — as it appears when it
opens. No dialog was filled in and submitted, so `VALID`, `INVALID`,
`SUCCESSFUL_MUTATION`, `REFUSAL`, `CONFLICT` and `STALE_VERSION` are
**NOT_REVIEWED** across all 61. Section 6's state matrix is not satisfied.

## 4. What the 30 opened dialogs showed

| Measurement | Result |
| --- | --- |
| Focus entered the dialog on opening | **29 / 30** |
| Escape dismissed it | **30 / 30** |
| Tab stayed inside the modal | **29 / 30** |
| Focus after close | 30 / 30 landed on the page; none lost, none stuck inside a dismissed dialog |
| Focus returned to the opener specifically | **0 / 30** — always elsewhere on the page |
| Accessibility detector hits inside dialogs | **0** |
| Row-speech detector hits inside dialogs | **0** |
| Unexpected second modal | **not measurable** — see §6 |
| Keystrokes the harness refused | 0 |

`RecordOfferDialog` is the one dialog that fails both focus measurements
(`AOS-R002-004`).

The accessibility zero covers **30 dialogs, not 61**, and it is the corrected
detector set from Audit 001R with positive and negative controls. It is not a
screen-reader certification and does not extend to the 31 dialogs that were not
opened. Audit 001R's "0 current hits" never covered any dialog; now it covers 30.

## 5. Sections of the brief not performed

| § | Subject | Status |
| --- | --- | --- |
| 6 | Dialog state coverage beyond EMPTY | **NOT_REVIEWED** |
| 7 | Validation audit | **PARTIAL** — server refusal quality observed at the API; in-dialog validation not exercised |
| 11 | Mutation result verification | **PARTIAL** — the six creates were read back; no UI-staleness or duplicate-submit checks |
| 12 | Idempotency / double-submit | **NOT_REVIEWED** — static only: 36 of 61 dialogs send a key |
| 13 | Stale version / conflict UX | **NOT_REVIEWED** |
| 14 | Multi-role review | **BLOCKED** — `AOS-R002-002` |
| 15 | Permission-change / stale screen | **BLOCKED** — same cause |
| 17 | Accessibility inside dialogs | **PARTIAL** — 30 of 61 |
| 18 | Copy / workflow language | **PARTIAL** — captions inventoried, not judged in use |
| 19 | Date / money / enum input UX | **PARTIAL** — the date defect found is `AOS-R002-001`; money and enum input not exercised |
| 20 | Finance mutation safety | **NOT_REVIEWED** — no finance record exists to act on |
| 21 | Legal / contract workflow | **NOT_REVIEWED** — no contract exists |
| 22 | Document / communication mutations | **NOT_REVIEWED** |
| 23 | AI / approval workflows | **NOT_REVIEWED** — no run exists to approve |
| 26 | Performance observation | **OBSERVED only** — no timing was instrumented, so no milliseconds are reported |

## 6. Measurements the harness could not make

Per §2, these are marked rather than asserted:

| Measurement | Status | Why |
| --- | --- | --- |
| Unexpected second modal | **INCONCLUSIVE** | WinUI hosts every `ContentDialog` behind two visible popup windows — measured at exactly two on all 30 — and no node carries a `ContentDialog` class name. Counting popups cannot answer the question. Manual check. |
| Default button | **MANUAL** | The markup declares `DefaultButton`; UI Automation does not expose which button Enter would press, and the audit did not press Enter on a filled form. |
| Missing required field / validation message | **NOT_REVIEWED** | Requires submitting, which was not done. |
| Mutation success / refusal from the dialog | **NOT_REVIEWED** | Same. |
| Stale dialog after mutation | **NOT_REVIEWED** | Same. |
| Inaccessible field inside a dialog | **COMPLETE** | The reachability classifier from Audit 001R ran over each open dialog; no field was unreachable. |

## 7. Data

Synthetic throughout, in the disposable review organization on `agencyos_w2`. Six
records created through canonical API commands, listed in `fixture-additions.json`.
No real email, mailbox, AI provider, external party, payment or private document.
No screenshot contains real data.

The fixture was **not** destroyed at the end: it is the state the evidence
describes, and the next audit slice needs it. `agencyos_w2` is a disposable review
database and can be dropped whenever the owner wants.
