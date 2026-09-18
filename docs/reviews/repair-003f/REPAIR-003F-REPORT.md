# Repair Wave 003F — navigation, discoverability, layout

```
REPAIR WAVE 003F — LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING
```

**Wave:** 003F, last of the local post-audit programme
**Admitted:** `AOS-R002-021`; `AOS-R001-013` / `AOS-R001R-001` in part
**Not admitted:** `AOS-R002-015`, `AOS-R002-016` — both owner decisions
**Baseline:** the working tree as 003E left it
**Date:** 2026-09-18

[Admission](REPAIR-003F-ADMISSION.md) · [Local work ledger](REPAIR-003F-LOCAL-WORK.md)

---

## Two things an operator could see and not use

| | Before | After |
| --- | --- | --- |
| Two contract commands at 1600x1000 | enabled, **offscreen, no rectangle, nothing scrollable to bring them back** | reachable, with a visible overflow that says they exist |
| The destination you are working in | pane stays put; the current one can be **offscreen with nothing marked** | brought into view when chosen |

## `AOS-R002-021` — commands off the window

Five commands sat in a horizontal `StackPanel` with no overflow. At 1600x1000 the
detail column starts at x≈1122 and the window's right edge is 1642: three buttons
fit, two did not, and the two that did not were *enabled*, reported
`IsOffscreen`, had **no bounding rectangle at all**, and had no scrollable
ancestor — so nothing could bring them into view and nothing indicated they
existed.

They are now a `CommandBar`. Measured live at 1600x1000 after the repair:

```
read:NewButton        -> name='New contract'
read:VersionButton    -> name='Record version'
read:ReconcileButton  -> not in the tree
read:SignatureButton  -> not in the tree
read:NoticeButton     -> not in the tree

read:MoreButton       -> present
click:MoreButton      -> invoked
read:ReconcileButton  -> name='Reconcile'
read:SignatureButton  -> name='Record signature'
read:NoticeButton     -> name='Record notice'
```

**Three commands are now one click further away**, behind an overflow button —
and that is the trade this makes. Before, two were *zero clicks away and
impossible to reach*. The overflow is visible, it is what the platform uses for
this, and it is what the finding suggested.

**`CommandBar` is used nowhere else in the product**, so this page now looks
slightly different from the other sixteen. Recorded rather than glossed: the SDK
has no wrapping panel, adding a package needs approval, and a horizontally
scrolling command row would be worse for an operator.

## `AOS-R001-013` / `AOS-R001R-001` — the pane now follows

Seventeen destinations do not fit the pane at 1600x1000, so four sit below the
fold. Audit 001R established that scrolling always worked and that **the product
never asked for it** — selecting one of those four left the pane where it was,
showing a list that did not contain the page being worked on, with nothing marked
anywhere. Audit 001 had said the opposite; that came from the harness's own
`SetFocus`, which scrolls.

Measured here with one instrument, on the same build, the repair off and then on:

| Selected | Fix disabled | Fix enabled |
| --- | --- | --- |
| Sync and Offline | **OFFSCREEN**, no rectangle | on screen at `53,780,330,57` |
| AI | **OFFSCREEN**, no rectangle | on screen at `53,780,330,60` |
| Intelligence | — | on screen at `53,780,330,60` |

One call, in the one handler every selection passes through — including the
keyboard accelerators, which select through the same property.

**What this does not fix:** six of seventeen destinations are still not visible
at once. The missing overflow affordance for the pane is a design question, and
answering it by invention would be deciding something that is not mine to decide.
`AOS-R001R-001` is therefore reduced, not closed.

## `AOS-R002-015` and `AOS-R002-016` — assigned here, not admitted

Both are canonically assigned to this wave. **Both carry
`ownerDecisionRequired: true`**, which §9 keeps outside this programme.

- **`AOS-R002-015`** — the organization surface is the settings destination
  rather than a workspace, so it has no accelerator and no palette entry. Its own
  record says no operator capability is blocked and that the expected behaviour
  is not established as a contract.
- **`AOS-R002-016`** — 26 dialogs commit on Enter and 36 cancel, following no
  rule. Real, and the repair runs in the dangerous direction: any convention
  makes some dialog commit what it used to discard.

**Neither was changed.**

## Local gates

| Gate | Before 003F | After 003F |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,773 | **3,773** |
| Windows | 995 | **1,002** (+7) |
| Reviewer | 163 | **163** |
| integration (database-free, local) | 13 | **13** |

Integration against `postgres:18.6` is **pending**: it runs in CI, which is not
dispatched during this programme.

## Schema, OpenAPI, contract

Nothing. Both changes are in the Windows client.

## Status

```
REPAIR WAVE 003F — LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING
```

Not closed: authoritative CI, `postgres:18.6` integration and Nightly are not run
during this programme.
