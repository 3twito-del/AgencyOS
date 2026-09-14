# Audit 002 Phase B — summary

**Product baseline:** `51541ea` — unchanged.
**Reviewer commit:** this commit — harness, reviewer tests, fixture tooling and
audit evidence only.
**Evidence:** `artifacts/reviewer/run-002-phase-b/`
**Date:** 2026-09-15

Phase A's record is preserved. This is additive. The status correction is
`docs/reviews/audit-002/AUDIT-002-STATUS.md`.

---

## What Phase B was for

Unblock the 27 dialogs Phase A could not open, and complete the mutation, role,
validation, idempotency and concurrency evidence the §30 gate requires.

**It moved most of the numbers and did not close the gate.**

| | Phase A | Phase B |
| --- | ---: | ---: |
| Dialogs opened | 30 / 57 | **38 / 57** |
| Domains with a representative mutation | 1 / 11 | **6 / 11** |
| Synthetic mutations executed | 6 | **28** |
| Idempotency | NOT_REVIEWED | **sampled at the API** |
| Conflict / 409 | NOT_REVIEWED | **sampled at the API** |
| Multi-role | BLOCKED | **still BLOCKED, and now explained** |
| Dialog states beyond EMPTY | NOT_REVIEWED | **still NOT_REVIEWED** |

**§30 is not satisfied. Audit 002 remains open.** The unmet gates are listed in
the coverage report and repeated at the end of this summary.

---

## 1. New findings

| Id | Sev | Confidence | What |
| --- | --- | --- | --- |
| `AOS-R002-006` | S2 | ConfirmedDefect | Membership is write-once: one `POST`, no read, no change, no revoke, and the handler refuses a second grant. |
| `AOS-R002-007` | S3 | ConfirmedDefect | The 409 conflict message names the record by an identifier the product displays nowhere, and never says what to do next. |
| `AOS-R002-008` | S3 | ConfirmedDefect | A binding failure answers 400 naming an internal DTO class and not the field. |
| `AOS-R002-009` | S3 | LikelyDefect | Twelve palette-only dialogs on tabbed pages do not open even when the record they act on exists. Cause not established. |

Phase B severity counts: **S0 0, S1 0, S2 1, S3 3, S4 0.**
Audit 002 totals so far: **S0 0, S1 1, S2 4, S3 4, S4 0** — 9 findings.
Owner/design decisions: **3** (`R002-002`, `R002-003`, `R002-006`).

### Findings carried from Phase A

| Id | State after Phase B |
| --- | --- |
| `AOS-R002-001` (non-UTC timestamps → 500) | **CONFIRMED**, unchanged |
| `AOS-R002-002` (no second user) | **STRENGTHENED** by `R002-006` |
| `AOS-R002-003` (17 palette-only dialogs) | **STRENGTHENED** — twelve of them are also `R002-009` |
| `AOS-R002-004` (focus) | **STRENGTHENED** — two dialogs now, not one |
| `AOS-R002-005` (3 of 61 dialogs tested) | **CONFIRMED**, unchanged |

`AOS-R002-004` now names **`RecordOfferDialog` and `RecordContractVersionDialog`**.
Both fail both measurements — focus does not enter on opening, and Tab walks the
page behind — and both reproduced in the final pass. The other 36 opened dialogs
take focus correctly and keep it.

---

## 2. The four reclassified findings, after Phase B

### `AOS-R001-006` — **CONFIRMED**, classified per field

All 20 fields across 13 dialogs classified in `AUDIT-002B-GUID-FIELDS.md`:
**11 `PICKER_REQUIRED_LIKELY`**, **4 `CONTEXT_DERIVABLE_LIKELY`**,
**4 `OWNER_DESIGN_DECISION_REQUIRED`**, **1 `DEBUG_ONLY`**. None is an unavoidable
external identifier.

Phase B added the behavioural half: malformed identifiers **cannot be submitted**
from any of the 13 — all parse with `Guid.TryParse` and 12 of 13 keep the primary
button disabled. Nonexistent gives a clear 404; the wrong entity type gives the
same 404 and discloses nothing; another organization gives 403 before existence is
considered. **The handling is sound. The problem remains that a person has to know
the value, and the product displays it nowhere.**

### `AOS-R001-010` — **CONFIRMED_API_UI_GAP**, narrowed

Create, read, transition, history, credits and materials all exist and their
dialogs were opened in both phases. **Scopes (add/end) and team (add/remove) have
four server endpoints and nothing in the client that calls them** — no client
method, no command, no dialog in the 61. The credits/materials half of the
original finding stays **SUPERSEDED**.

### `AOS-R001-013` — **S2 / PARTIAL**, unchanged

Phase B navigated between workspaces roughly 180 more times across three full
dialog passes. Navigation never failed. The pane still does not follow the
selection. No new evidence warrants a severity change, and none warrants lowering
it either.

### `AOS-R001-020` — **INTENTIONAL_NO_OP**, residual **`ACKNOWLEDGEMENT_UX_GAP`**

Phase A settled the navigation question from source: `sync.now` is shell-owned and
deliberately does not navigate. Phase B did not produce the acknowledgement
observation it set out to: pressing F9 and watching the footer's "last updated"
caption change needs a controlled offline/online transition the audit did not set
up. The residual stays **S4** and is now explicitly **`INCONCLUSIVE` on evidence,
`ACKNOWLEDGEMENT_UX_GAP` on reading the source** — the shell calls
`SynchronizeAsync()` and nothing in that path raises a notice.

---

## 3. Dead dialogs — reconfirmed

The four remain dead after fixture enrichment: `AddIntelligenceSubjectDialog`,
`CalculateCommissionDialog`, `RaiseReceivableDialog`,
`RecordMonetaryObligationDialog`. Nothing constructs them; no command, no context
menu, no deep link. Classification **`DEAD_UI`**, `AOS-R001-017` reconfirmed, and
now pinned by a reviewer test so the list cannot shrink unnoticed.

Notably, Phase B created a monetary obligation and a receivable **through the
API** — the two things `RecordMonetaryObligationDialog` and `RaiseReceivableDialog`
exist to create. The capability is real; the dialogs for it are unreachable.

---

## 4. Reviewer defects found in Phase B (§22)

Two, both fixed before any result was recorded, both now covered by tests.

| # | What was wrong | What it would have said | Status |
| --- | --- | --- | --- |
| 6 | `OpenViaTabs` treated "the command ran" as "the dialog appeared". The palette always runs the command it is given, so a palette opener reported success on the first tab and the loop never reached the right one. | 19 tab-hosted dialogs blocked, blamed on missing fixture data. | Fixed — the dialog itself is now the evidence. |
| 7 | The idempotency probe sent `Idempotency-Key`; the product's header is `X-AgencyOS-Idempotency-Key`. | A confident S2: "the idempotency key is ignored". | Fixed before reporting. Recorded in `AUDIT-002B-IDEMPOTENCY.md`. |

Phase A's five plus these two is **seven reviewer defects across Audit 002**. Every
one of them would have produced a wrong finding, and six of the seven would have
produced a *false positive against the product*. That ratio is the argument for
the positive-control discipline, not against it.

Two harness limitations are recorded rather than fixed:

- **Unexpected second modal** — not measurable. WinUI hosts every `ContentDialog`
  behind two popup windows and no node carries a `ContentDialog` class name.
- **`AOS-R002-009`'s cause** — the audit could not separate "the page loses its
  tab state when a palette command runs" from "the harness failed to establish
  that state". The finding says so.

---

## 5. Fixture

28 objects created through canonical API commands, listed in `fixture.json`, each
tied to a blocked dialog. Deterministic names, all prefixed `Audit 002B` or
`(synthetic)`. No direct database writes. Two direct database **reads**, both for
verification and both identified: the release-policy row, and nothing else.

The chain built: project → role → package; offer → accept → contract → version →
parties → review → approval → two signatures → **Executed** → effective date →
obligation → option → monetary obligation → receivable → invoice → payment. Plus
a second negotiation deliberately left open, a fake mailbox, and an AI run.

**Not destroyed.** It is the state this evidence describes and the state the next
slice needs. `agencyos_w2` is disposable and can be dropped whenever the owner
wants.

---

## 6. Gates

| Gate | Result |
| --- | --- |
| Product code changed | **none** — `git status` on `src/` empty throughout |
| `dotnet build AgencyOS.sln` | 0 warnings, 0 errors |
| Unit | 3,749 |
| Windows | 715 |
| Reviewer | **89** (was 83) |

Integration was not re-run: no product code, contract, migration or endpoint was
touched. Interactive dialog coverage is LAB evidence and hosted CI cannot produce
it — the runner has no interactive desktop.

---

## 7. §30 — what is still unmet

| Gate | Status |
| --- | --- |
| All dialogs inventoried | ✅ 61 / 61 |
| Every reachable dialog opened | ❌ **38 / 57** |
| Cancel/close observed for every reachable dialog | ❌ 38 / 57 |
| Accessibility/focus for every opened dialog | ✅ 38 / 38 |
| Representative mutation per major domain | ❌ **6 / 11** |
| Multi-role evidence | ❌ **BLOCKED** — `R002-002`, `R002-006` |
| `AOS-R001-006` classified per instance | ✅ |
| `AOS-R001-010` runtime-classified | ✅ |
| `AOS-R001-013` refined | ✅ |
| `AOS-R001-020` refined | ✅ |
| Idempotency sampled | ✅ at the API; ❌ at the dialog |
| Conflict / 409 sampled | ✅ at the API; ❌ at the dialog |
| Validation association sampled | ❌ **NOT_REVIEWED** |
| Role/authorization UX no longer blocked | ❌ **still BLOCKED** |

**Six unmet. Audit 002 remains open.**

Two of the six cannot be closed by any amount of audit work: multi-role and
role/authorization UX are blocked by the product, not by the fixture. Those two
gates can only be satisfied after `AOS-R002-002` and `AOS-R002-006` are repaired,
or the gate is consciously amended by the owner to exclude them.

---

## 8. Proposed repair waves — a proposal only

Nothing executed.

- **003A — correctness:** `AOS-R002-001` (S1), `AOS-R002-005`, and `AOS-R002-009`
  if its cause proves to be the product.
- **003B — dialog usability and raw identifiers:** `AOS-R001-006` (20 fields),
  `AOS-R002-003` (17 palette-only dialogs).
- **003C — authorization and refusal UX:** nothing schedulable. Blocked behind
  003E.
- **003D — accessibility, focus, validation:** `AOS-R002-004` (two dialogs),
  `AOS-R002-008`.
- **003E — completeness, owner decision required:** `AOS-R002-002`,
  `AOS-R002-006`, `AOS-R001-010`.
- **003F — navigation, copy, polish:** `AOS-R002-007`, `AOS-R001-013`,
  `AOS-R001-020`'s residual, `AOS-R001-017`.

**003E unblocks more audit coverage than any other wave.** Until an organization
can hold two people, four §30 gates cannot be attempted at all.
