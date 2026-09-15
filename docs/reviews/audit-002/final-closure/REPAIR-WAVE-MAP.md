# Repair wave map, after Audit 002 closed

§22. The open findings, grouped by what a reviewer would have to re-run to verify
them. **Nothing here is executed, and nothing here is admitted.**

---

## The families

| Wave | Theme | Contents |
| --- | --- | --- |
| **003B** | raw/internal identifiers, entity selection | `AOS-R001-006` — 20 typed-identifier fields across 13 dialogs. Needs a design decision first: eleven want a picker, four are derivable from context, four are owner/lead fields, one is debug-only. |
| **003C** | authorization / refusal UX | `AOS-R002-007`, `AOS-R002-008`, `AOS-R002-014` — three shapes of a refusal written for the log rather than for the operator. |
| **003D** | accessibility / validation association | `AOS-R002-011` as one product-wide root, plus `AOS-R002-010`, `AOS-R002-012`, `AOS-R002-018`. Needs the owner decision about where a refusal is shown before the association can point at anything. |
| **003E** | representation completeness, and confirmed capability gaps | `AOS-R001-010` (scopes/team: four server endpoints with no client caller), the three unwired finance dialogs, `AOS-R002-013`. **No new entry from the closure slice** — it found no missing current product capability. |
| **003F** | navigation / discoverability / copy / layout | `AOS-R001-013` / `AOS-R001R-001`, `AOS-R002-015`, `AOS-R002-016`, and **new: `AOS-R002-021`**. |

## New from the closure slice

| Finding | Wave | Why there |
| --- | --- | --- |
| `AOS-R002-020` — `ConnectMailboxDialog` offers a mailbox visibility the server refuses | **003B or owner decision** | Client-layer and one line to change, but which way is a product question: remove the option, or add `Organization` to `MailboxVisibility` and to the access rules that read it. The second is a permission-model change and is not a small edit. |
| `AOS-R002-021` — two contract commands unclickable at 1600x1000 | **003F** | Layout and overflow, the same family as the navigation pane finding, though a different surface and a worse symptom: the pane scrolls and these do not. |

## Recorded separately — not a wave

### Async dispatch reliability

**Status: observation, not an admitted repair.**

Repair Wave 003A recorded **218 fire-and-forget dispatch sites** in the Windows
client (`_ = SomethingAsync()`), each of which discards an unexpected exception
without a word. That pattern was the second necessary condition for
`AOS-R002-019`: four dialogs threw inside `InitializeComponent`, and the operator
saw nothing because the throw had nowhere to go.

003A removed the *cause* at those four sites and added `LoadTimeHandlerTests`, so
the specific defect cannot return anywhere in the client. **The class remains.**

It is not admitted as a wave because repairing it means touching every page, and
because the right answer is a design decision rather than a mechanical edit —
whether an unexpected failure belongs in the page's error surface, in a toast, in
telemetry, or in a shared dispatch helper that every opener goes through. That
decision has not been made.

Nothing in this closure slice touched any of the 218 sites.

---

## Sequencing

Unchanged from Phase C's proposal except that 003A is done:

1. ~~**003A**~~ — closed. `AOS-R002-019`, `AOS-R002-017`.
2. **003F** next — smallest and independent, and now carries `AOS-R002-021`.
3. **003D** after — real user cost, and its owner decision blocks the most.
4. **003C** and **003B** — copy and design decisions, made deliberately rather
   than as a side effect.
5. **003E** last, or never as a wave — mostly roadmap rather than repair.

`AOS-R002-020` should be decided before it is scheduled, because the two possible
answers land in different waves.
