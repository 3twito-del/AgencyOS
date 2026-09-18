# Repair Wave 003E — representation completeness

```
REPAIR WAVE 003E — LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING
```

**Wave:** 003E, third of the local post-audit programme
**Admitted:** `AOS-R001-010`
**Not admitted:** `AOS-R002-013` — owner decision · the three unwired finance dialogs — an unbuilt capability
**Baseline:** the working tree as 003D left it
**Date:** 2026-09-18

[Admission](REPAIR-003E-ADMISSION.md) · [Local work ledger](REPAIR-003E-LOCAL-WORK.md)

---

## What an operator could not do, and can now

The Talent workspace has always shown what the agency represents and who works
the relationship. Four server commands have existed since M4 to change them.
**Nothing in the product called any of them**, so both lists were read-only
facts an operator could see and not maintain.

| Command | Server | Client before | Client now |
| --- | --- | --- | --- |
| Begin representing an area | `POST /representations/{id}/scopes` | ❌ | ✅ |
| Stop representing an area | `POST /representations/{id}/scopes/end` | ❌ | ✅ |
| Put somebody on the team | `POST /representations/{id}/team` | ❌ | ✅ |
| Take somebody off | `POST /representations/{id}/team/remove` | ❌ | ✅ |

## Proved against a running server

One representation, driven entirely through the real Windows client, with the
server read back after each step:

| Step | Through the client | Server afterwards |
| --- | --- | --- |
| Start | — | v4 · scopes `Television` · team `Review Owner (Lead)` |
| Begin an area | dialog offered **Film** — Television was not offered, because it is already represented | **v5** · scopes `Television, Film` |
| Assign somebody | chose `Review member`, role `Agent` | **v6** · team `Review Owner (Lead), Review member (Agent)` |
| End an area | chose `Television` | **v7** · `Television` ends 2026-09-18, **kept in the record** |
| Take somebody off | chose `Review member — Agent` | **v8** · their assignment ends 2026-09-18, **kept in the record** |

The dialog's own sentence — *"Ending an area keeps it in the record with the date
it ended. It is not deleted, because it was true until then."* — is what the
server did.

The client also read back what it should: the scope dialog announced
*"Represented now: Television."* and the team dialog *"Working it now: Review
Owner — Lead."* before either change.

## Why this was a repair and not a product decision

Audit 002 proposed this wave as "completeness, **owner decision required**" and
called `AOS-R001-010` a product-shape question. That reading was made when the
gap looked like a whole absent capability. Checked before writing any code:

- the four endpoints exist, are permission-gated on `representations.write`, and
  are version-checked;
- the workspace that shows their result already exists;
- an area is a closed domain enum, and so is a team role;
- who may be assigned comes from an endpoint the client already calls.

Nothing was left to choose. The one judgment in this area — one lead at a time,
and assigning a new one ends the old assignment — is the domain's, and the dialog
states it rather than deciding it.

## What the dialogs refuse to do

- **An area already represented is not offered.** Adding it again would earn a
  refusal the operator could not act on.
- **An area this client has no name for can still be ended.** The endable list is
  read from what the server sent, so a server that knows more than this build
  does not strand somebody with a scope they cannot close.
- **Somebody already on the team stays in the list**, labelled with what they do,
  because assigning them again is how a role changes. Hiding them would make a
  role change unreachable.
- **Neither dialog asks for an identifier.** Both pick from lists — which is why
  `AOS-R001-006`'s structural guard passes on them without an exception.
- **Every call carries the version the operator was looking at**, so a stale
  change is refused rather than applied silently.

## Local gates

| Gate | Before 003E | After 003E |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,763 | **3,773** (+10) |
| Windows | 968 | **995** (+27) |
| Reviewer | 163 | **163** |
| contract | 263 paths / 187 schemas | **263 paths / 187 schemas** |
| integration (database-free, local) | 13 | **13** |

The Windows gate grew by more than the 11 tests this wave wrote: the two new
dialogs were swept automatically by eight existing structural suites, 2 cases
each. They pass without an exception being added for them.

Integration against `postgres:18.6` is **pending**: it runs in CI, which is not
dispatched during this programme.

## Schema, OpenAPI, contract

No server file was changed. No migration, endpoint, contract type, permission or
status code. The regenerated OpenAPI document is identical.

## Recorded, not repaired

- **The three unwired finance dialogs** — `CalculateCommissionDialog`,
  `RaiseReceivableDialog`, `RecordMonetaryObligationDialog` — are unreachable
  **on purpose**. The obligations capability they belong to was never built, and
  M13 chose to stop advertising the command rather than dispatch nowhere
  (ADR-0032, recorded in `FinanceViewModelTests`). Wiring them is a milestone.
- **`AOS-R002-013`** — an email address stored as typed — is an owner decision,
  recorded as an observation whose expected behaviour is explicitly not
  established.
- **A representation no workspace lists.** Converting a prospect creates a
  representation for a *person*; the Talent workspace lists *talent profiles*. A
  person with no talent profile leaves the Prospects list on conversion and never
  appears in Talent. Found while building this wave's fixture, measured, and left
  alone because the fix is a product decision.

## Status

```
REPAIR WAVE 003E — LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING
```

Not closed: authoritative CI, `postgres:18.6` integration and Nightly are not run
during this programme.
