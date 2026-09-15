# Audit 002 — final dialog inventory

§13. Every one of the 63 dialogs carries exactly one current disposition. No
Phase D category is carried forward unexamined, and the denominator was not moved
to make anything close.

---

## The counts

| | Count |
| --- | ---: |
| `TOTAL_DIALOGS` | **63** |
| `CURRENTLY_REACHABLE` | **57** |
| `OPENED` | **57** |
| `UNREACHABLE` | **4** |
| `EXTERNAL_PRECONDITION_ONLY` | **2** |
| `FUTURE_SEAM` | 0 |
| `DEAD_OR_UNWIRED` | 0 |
| `PRODUCT_CAPABILITY_MISSING` | **0** |
| `HARNESS_LIMITATION` | **0** |

`57 + 4 + 2 = 63.` No remainder, and no dialog with two dispositions.

**`OPENED` equals `CURRENTLY_REACHABLE`: 57 of 57.**

---

## How the 57 were opened

| Where | Count |
| --- | ---: |
| Audit 002 Phases A–D | 51 |
| Repair Wave 003A — `AOS-R002-019` repaired | 4 |
| This closure slice | **2** |

The two:

| Dialog | Was | Now | By |
| --- | --- | --- | --- |
| `RecordSignatureDialog` | `HARNESS_LIMITATION` | **`OPENED`** | palette `contract.signature.record` |
| `ResolveParticipantDialog` | `PRECONDITION_UNACHIEVABLE` | **`OPENED`** | palette `message.participant.resolve`, after the precondition was created through canonical routes |

Both were opened by the audit's own unmodified `dialog-runtime` pass, and both
report `focus-in=yes`, `escape-closed=yes` and zero accessibility observations.

---

## The 4 unreachable

Unchanged from Phase D. Nothing in the client constructs them, so no operator
path exists to be blocked.

| Dialog | Why |
| --- | --- |
| `AddIntelligenceSubjectDialog` | nothing constructs it |
| `CalculateCommissionDialog` | nothing constructs it — one of the three unwired finance dialogs (`AOS-R001-010`, wave 003E) |
| `RaiseReceivableDialog` | nothing constructs it — same |
| `RecordMonetaryObligationDialog` | nothing constructs it — same |

These were never in the reachable denominator and are not moved by this slice.

---

## The 2 external-precondition-only

Removed from the **current reachable** denominator under §7, because a real
operator cannot lawfully reach the required state today. Full reasoning in
[`AUDIT-002-FINAL-PRECONDITION-DECISIONS.md`](AUDIT-002-FINAL-PRECONDITION-DECISIONS.md).

| Dialog | Classification | The state, and where it comes from |
| --- | --- | --- |
| `ApproveAiActionDialog` | `INTENTIONALLY_EXTERNAL_PRECONDITION` | A pending approval exists only when a model provider's response requests a write tool. No route creates one, deliberately. The only registered `IModelProvider` is `FakeModelProvider`, scriptable in-process only; no real adapter exists and the roadmap defers connecting one. |
| `IngestAttachmentDialog` | `INTENTIONALLY_EXTERNAL_PRECONDITION` | Attachment rows are created only by `MailboxSynchronizer`. `GraphCommunicationProvider` is a complete real adapter and is not configured here; the fake's inbox is populated in-process only. Measured: an outbound send with a real attachment yields `attachmentCount: 0`. |

**Historical dispositions are preserved, not overwritten.** Both were
`REACHABLE / PRECONDITION_UNACHIEVABLE` at Phase D, and that record stands as
issued.

**Neither is a product capability gap.** Both openers were verified healthy — each
produces a visible refusal when its precondition is absent, so neither is the
`AOS-R002-019` class:

- `IngestAttachmentDialog` → *"That did not happen — Select an attachment first."*
- `ApproveAiActionDialog` → its opener is the AI page's `ApproveButton`, which the
  page disables while the approvals list is empty.

---

## What would change these two

Neither needs product work. Each needs its external source connected, which is an
operational act:

| Dialog | What would make it reachable |
| --- | --- |
| `ApproveAiActionDialog` | a real model provider adapter behind the existing gateway seam — explicitly a roadmap item, to be connected first in FORGE against synthetic data |
| `IngestAttachmentDialog` | `GraphOptions` configured with a Microsoft tenant, and a synchronised message carrying an attachment |

Until then, both are outside the current reachable surface and the §30 gate is
measured without them — which is what §14 requires, not a relaxation of it.
