# Audit 002 Phase C — proposed repair waves

§23. A proposal, not a plan, and nothing here has been applied. Audit 002 applied
no product repairs in any phase.

Waves are grouped by what a reviewer would have to re-run to verify them, not by
severity, because that is what decides whether they can be done together.

---

## The groups

Per §24's scheme. **Nothing here has been applied, and no wave is executed.**

| Wave | Theme | Contents |
| --- | --- | --- |
| **003A** | correctness / broken workflows | `AOS-R002-017` (documents `500` on a non-multipart content type). `AOS-R002-001` stays under review — its repairs landed in an earlier wave and its symptom shape is re-verified. |
| **003B** | entity selection / raw identifiers | **`AOS-R001-006`** — 20 typed-identifier fields across 13 dialogs. The largest open finding and the one least suited to a mechanical wave. |
| **003C** | authorization / refusal UX | `AOS-R002-007`, `AOS-R002-008`, `AOS-R002-014` — three shapes of the same problem: a refusal written for the log rather than for the operator. |
| **003D** | accessibility / focus / validation | **`AOS-R002-011`** as one product-wide root, plus `AOS-R002-010` and `AOS-R002-012`. |
| **003E** | product completeness / owner decisions | **`AOS-R001-010`** (representation scopes and team), the three unwired finance dialogs, `AOS-R002-013`. |
| **003F** | navigation / discoverability / copy / polish | `AOS-R002-015`, `AOS-R002-016`. |

---

## 003D is one root, not sixty edits

The most important grouping decision here, and §24 names it explicitly.

`AOS-R002-011` is that **no dialog declares a programmatic association between a
message and the field it concerns** — 0 of 63. That is one missing pattern, not
sixty missing attributes, and repairing it dialog by dialog would produce sixty
hand-edited files and no shared guarantee that the sixty-first gets it too.

What the wave needs first is a decision about where a refusal is shown at all
(`AOS-R002-010`), because the association has to point at something:

1. The dialog stays open on refusal and shows the message itself.
2. The dialog closes and the page reopens it with the entry restored.
3. The client validates what it can before submitting, and the server's refusal
   remains the page-level fallback.

All three are defensible and the audit does not choose. Once chosen, the
association is a shared pattern applied once, and a test can assert that every
dialog with inputs has it — which is the thing that makes it stay true.

Three of the 63 dialogs are runtime-confirmed. **25 are a manual gate** — their
commit button stays disabled until the form is complete, so nothing could be
driven to a refusal, and their behaviour under a screen reader is unmeasured
rather than defective. A real screen-reader session remains the only way to
settle those, and it stays MANUAL_GATE.

---

## 003B needs a design decision before it needs an implementation

Eleven fields want a picker, four are derivable from context, four are
owner/lead fields and one is debug-only — four different answers to what looks
like one problem, and §11 forbids designing any of them during an audit.

One thing did change: the four owner/lead fields were classified
`OWNER_DESIGN_DECISION_REQUIRED` partly because the client had no way to list
internal users. It does now (`GET /organizations/{id}/members`, closed by Repair
Wave 003E-A). The blocker is no longer "there is no directory"; it is "these four
dialogs do not use it", which is a cheaper question.

---

## 003E is mostly not repair

`AOS-R001-010` — four server endpoints for representation scopes and team with no
client caller, no command and no dialog — is a feature that was never finished,
not a defect that can be fixed. The same is true of `CalculateCommissionDialog`,
`RaiseReceivableDialog` and `RecordMonetaryObligationDialog`: three built
interfaces for capability that exists on the server and has no other client
surface.

These belong on a roadmap, not in a bug-fix wave, and they are grouped here so
that is visible rather than buried.

---

## Sequencing

**003F first.** It is the smallest and entirely independent, and making the
membership surface reachable the ordinary way also makes it testable by the
ordinary means.

**003D next**, because it is the one with a real user cost — work is being lost —
and because its owner decision blocks the most.

**003A alongside**, since `AOS-R002-017` is a content-type check with no
interaction with anything else. Repair Wave 001.5's rule applies to whoever takes
it: *do not catch the exception and relabel it.* The content type should be
refused before the form binder is asked to bind.

**003C and 003B after**, both being copy and design decisions that should be made
once and deliberately rather than as a side effect.

**003E last**, or never as a wave — see above.
