# Audit 002 — §30 re-evaluated on final current truth

§14. The original fourteen requirements, scored against what is true now. The
gate is not relaxed and the denominator is not inflated: requirements 2, 3 and 4
are measured against **currently reachable** dialogs, which is what they always
said.

---

## The fourteen

| # | Requirement | At Phase D | Now |
| ---: | --- | --- | --- |
| 1 | All dialogs inventoried | MET — 63 of 63 | **MET** — 63 of 63 |
| 2 | Every reachable dialog opened at least once | NOT MET — 51 of 59 | **MET** — **57 of 57** |
| 3 | Cancel/close behaviour observed | NOT MET — 51 of 59 | **MET** — 57 of 57 |
| 4 | Accessibility/focus inspection completed | NOT MET — 51 of 59 | **MET** — 57 of 57 |
| 5 | Representative mutation for every major domain | MET — 11 of 11 | **MET** |
| 6 | Multi-role evidence completed | MET | **MET** |
| 7 | Role/auth UX completed | MET | **MET** |
| 8 | Validation association adequately characterised | MET | **MET** |
| 9 | `AOS-R001-006` finalised | MET | **MET** |
| 10 | `AOS-R001-010` finalised | MET | **MET** |
| 11 | `AOS-R001-013` refined | MET | **MET** |
| 12 | `AOS-R001-020` refined | MET — `NO_DEFECT` | **MET** |
| 13 | Idempotency sampled | MET | **MET** |
| 14 | Stale conflict sampled | MET | **MET** |

**Fourteen of fourteen met.**

Requirements 5 through 14 were not re-run. They were settled before this slice
and §0 freezes them; nothing done here touches their evidence.

---

## How 2, 3 and 4 moved

They were one gap counted three times, and it closed in three steps.

| Step | Dialogs | Effect |
| --- | ---: | --- |
| Phase D's position | 51 of 59 | eight unopened: 4 product defect, 3 precondition, 1 harness |
| Repair Wave 003A | +4 opened | `AOS-R002-019` repaired — 55 of 59 |
| This slice: `RecordSignatureDialog` | +1 opened | never blocked; the palette opener was not tried |
| This slice: `ResolveParticipantDialog` | +1 opened | precondition created through canonical routes |
| This slice: two reclassified | denominator 59 → 57 | state that only an external source can create |

`57 opened / 57 currently reachable`.

---

## Why the denominator moved, and why that is not relaxation

§7 permits removing a dialog from the current reachable denominator **only** when
a real current operator cannot lawfully reach the required state. Two qualify,
and the test was applied strictly:

**`ApproveAiActionDialog`.** A pending AI approval is created by `AgentRuntime`
when a model's response requests a non-read-only tool. There is no route that
creates one — deliberately, because a route that accepted a tool name and
arguments from a client is exactly what M12's design excludes. The only
`IModelProvider` registered in any environment is `FakeModelProvider`, which
answers only what a test queued in-process; the API does not reference it. No real
provider adapter exists, and the roadmap says so and defers connecting one.

**`IngestAttachmentDialog`.** Message attachment rows are created by
`MailboxSynchronizer` and nowhere else. The outbound path was tested rather than
assumed: a real document, attached, queued and sent produced `hasAttachments:
true` and `attachmentCount: 0`. `GraphCommunicationProvider` is a complete real
adapter, registered only when a Microsoft tenant is configured, which this
environment does not have and which this audit may not use.

Both were kept in the denominator through every earlier phase, and both are
removed now only because this slice established — by tracing every layer and by
measuring, not by inference — that no operator can create the state.

**What was not done to reach this score:** no creation route was added, no fixture
shortcut was taken, no dialog was reclassified for convenience, and no product
code was changed. Had either dialog turned out to be a missing current capability,
it would have stayed a blocker and Audit 002 would have stayed open.

---

## What this score does not claim

- **`EXTERNAL_PRECONDITION_ONLY` is not "tested".** Two dialogs have never been
  opened by anyone. They are outside the current reachable surface, not proven
  good.
- **No human operated any of it.** Every dialog was reached by UI Automation
  against the real interface. Manual gates remain manual.
- **Runtime evidence is LAB evidence.** It ran against local PostgreSQL 19beta3
  and a synthetic tenant. The CI gate is `postgres:18.6` and is separate.
- **Closing §30 is not a statement that the product is correct.** Four findings
  remain open against it, and this slice added two more.
