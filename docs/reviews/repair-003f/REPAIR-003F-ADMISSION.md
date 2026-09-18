# Repair Wave 003F — admission

**Theme:** navigation, discoverability, copy, layout
**Canonical contents:** `AOS-R001-013` / `AOS-R001R-001`, `AOS-R002-021`,
`AOS-R002-015`, `AOS-R002-016`
**Baseline:** the working tree as 003E left it
**Date:** 2026-09-18

---

## What this wave admits

| Finding | Disposition | Why |
| --- | --- | --- |
| `AOS-R002-021` | **Admitted and repaired** | `ownerDecisionRequired: false`, "layout only". Two commands could not be clicked at a common window size. |
| `AOS-R001-013` / `AOS-R001R-001` | **Admitted in part, and repaired** | The pane never followed the selection. Making it follow needs no decision; adding a new overflow affordance to the pane would. |
| `AOS-R002-015` | **Not admitted — owner decision** | `ownerDecisionRequired: true`. Whether the organization surface is a workspace or the settings destination is a design question, and no capability is blocked. |
| `AOS-R002-016` | **Not admitted — owner decision** | `ownerDecisionRequired: true`, and the risk runs the wrong way: changing a default commits what a habit used to discard. |

The user's brief asked whether `AOS-R002-015` and `AOS-R002-016` are canonically
assigned. **Both are**, to this wave — `AOS-R002-015` by its own record
(`suggestedRepairWave: 003F`) and `AOS-R002-016` via the wave map. **Both also
carry `ownerDecisionRequired: true`**, which §9 keeps outside this programme, so
assignment and admissibility part company here. They are named, not touched.

---

## `AOS-R001-013` — the half that needed no decision

The finding has two halves, and only one is a decision.

**Not a decision: the pane never followed the selection.** Audit 001R measured
all four questions and answered C with a plain no — selecting Intelligence, AI,
Saved Views or Sync and Offline left the pane where it was, showing a list that
did not contain the page being worked on, with nothing marked anywhere. The same
audit measured that **scrolling always worked**; nothing ever asked for it. A
product that can scroll to the current destination and does not is not expressing
a preference.

**A decision: the missing overflow affordance.** Seventeen destinations at 40
units each are 680; the pane's scroll region is 508 at 1600x1000. They do not fit
at any footer height, and Repair Wave 002 already spent the footer. What to do
about that — a different pane mode, grouping, fewer destinations — is a design
question, and inventing an affordance would be answering it. **Untouched.**

Fixing the first half is most of what `AOS-R001R-001` describes: not being able
to see which destination you are on. It is not all of it, because six of
seventeen are still not visible at once.

## `AOS-R002-015` — not admitted

The organization and membership surface is the `NavigationView` settings
destination rather than a workspace, so it has no accelerator and no palette
entry. Its own record says the expected behaviour is **not established as a
contract**, that the registry's completeness test is not violated, and that **no
operator capability is blocked** — the pane reaches it in the ordinary way.

Whether membership management should become a workspace is a product decision.
**Nothing was changed.**

## `AOS-R002-016` — not admitted

All 63 dialogs declare a `DefaultButton`: 26 commit on Enter, 36 cancel, one is
Secondary, and the split follows no rule an operator could learn. That is a real
defect and it is not this programme's to fix.

Choosing one convention means changing what Enter does in at least 26 dialogs,
and the direction that matters is the dangerous one: a dialog that used to
discard on Enter would commit. The finding's own risk note says exactly this, and
its `ownerDecisionRequired` is `true`.

**Nothing was changed.**
