# Repair Wave 003C — authorization and refusal UX

```
REPAIR WAVE 003C — LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING
```

**Wave:** 003C, first of the local post-audit programme
**Admitted:** `AOS-R002-007`, `AOS-R002-008`, `AOS-R002-024`
**Not admitted:** `AOS-R002-014` — owner decision
**Baseline:** executable `ada2310`
**Date:** 2026-09-18

Three shapes of a refusal written for a log rather than for the person reading
it. **Nobody's authority changed**: no permission, role, membership, tenant rule
or existence-hiding behaviour was touched, and the statuses the server sends are
exactly the statuses it sent before.

[Admission](REPAIR-003C-ADMISSION.md) · [Local work ledger](REPAIR-003C-LOCAL-WORK.md)

---

## What an operator saw, and sees now

| | Before | After |
| --- | --- | --- |
| A pitch on a target that has not been approved | **"Invalid request"** | **"This target has not been approved yet. Approve it before recording a pitch."** |
| A save against a record somebody else changed | "Contract '01a0a1ff-de3a-7c5d-8d7f-c7ee9deaa6e9' has changed since you last saw it (you had version 9, it is now 10)." | **"This project has changed since you last saw it (you had version 6, it is now 7). Refresh to see the current version, then decide what to do."** |
| A body the server could not read | "Failed to read parameter \"CreateDealRequest request\" from the request body as JSON." | **"'personId' could not be read. Expected an identifier."** |

All three were captured live, the first two through the real Windows client at
UTC+3 against the LAB database.

## `AOS-R002-024` — the client showed the title, not the reason

`AgencyOsApiException.Message` is the problem's title; `Detail` is the sentence
the domain wrote. Seven surfaces already showed the detail; **five showed the
title**, so the rule that would have let the operator proceed was hidden from
them.

The finding named two. A survey of every `catch (AgencyOsApiException …)` in the
client found a third, and the structural guard written for this wave found two
more that the survey had missed — `PackagesPage` and `CreateContractDialog`.
All five now use the established pattern, and the guard fails if a sixth appears.

**Runtime:** Pipeline, a target at `Identified`, Record pitch → the InfoBar reads
*"That did not happen — This target has not been approved yet. Approve it before
recording a pitch."* (`refusal/`).

## `AOS-R002-007` — a conflict that named a record nobody can see

The sentence quoted an identifier the product displays nowhere and stopped
without saying what to do. It now names the kind of record in words, keeps both
versions — the reader needs both — and ends with the next step. The identifier
remains on the exception and in the problem's `entityId` extension, so nothing
reading this by machine lost anything.

**Runtime:** a dialog was opened, the project was changed from the API while it
sat there, and the save was submitted stale. The client showed the new sentence
(`conflict/`).

## `AOS-R002-008` — a refusal that named an internal class

Now: the field the caller sent, and what it should have looked like.

| Sent | Refusal |
| --- | --- |
| `personId: "not-a-guid"` | `'personId' could not be read. Expected an identifier.` |
| `startsOn: "last Tuesday"` | `'startsOn' could not be read. Expected a date, such as 2026-09-18.` |
| `expectedVersion: "many"` | `'expectedVersion' could not be read. Expected a whole number.` |

The request class name never appears. The description comes from a closed list
of shapes; anything outside it is left undescribed rather than guessed at, and a
body too broken to have a field at all is still a plain `400`.

**This needed measuring, not assuming.** The serializer reports what it was
building, and for a record request that is the *request type*, whichever property
failed — so the first implementation produced a field name and no expectation.
The member's type is now looked up on the contract. A temporary probe established
that; it is recorded and removed in the ledger.

## `AOS-R002-014` — not admitted

Its record says `ownerDecisionRequired: true`, and the choice is real: keep the
permission string (precise, quotable to an administrator), describe the
capability instead (names nothing internal), or say who could grant it (which
tells someone who was just refused something about the organization's roles).
Each says a different amount to a refused caller, so it is not copy polish.

**Nothing about it was changed**, on the server or in the client. The 403 still
reads *"Permission 'projects.write' is required."* It stays open.

## Status classes, live

| Case | Result | Detail |
| --- | --- | --- |
| Binding failure | `400` | names the field |
| Unauthenticated | `401` | unchanged |
| Read-only observer | `403` | unchanged |
| Authenticated non-member | `403` | `Permission 'projects.write' is required.` — `AOS-R002-014`, untouched |
| Cross-tenant route | `404` | unchanged |
| Stale version | `409` | the new sentence |
| Authorized | `201` | the success path still works |

Nothing was collapsed into a generic message, and no refusal carries an exception
type, stack frame, SQL text, tenant identifier, route template or secret.

## Local gates

| Gate | Before 003C | After 003C |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,755 | **3,763** (+8) |
| Windows | 942 | **950** (+8) |
| Reviewer | 163 | **163** |
| integration (database-free subset, local) | 17 | **30** (+13) |

Integration against `postgres:18.6` is **pending**: it runs in CI, which is not
dispatched during this programme.

## Schema, OpenAPI, contract

No migration, no endpoint, no contract type, no status code. The changes are one
domain sentence, one handler's `detail` string, and five client call sites.
`AOS-R002-001`'s converter, its OpenAPI restoration and its temporal tests were
not touched.

## Recorded, not repaired

- **`AOS-R002-025`** — a missing required nested object still answers `500`. Its
  status is wrong, which is a correctness question and not this wave's.
- **A `404` names the identifier the caller supplied** — *"Person '01a0…' was not
  found."* The same family as `AOS-R002-007` in wording, but the identifier there
  is the caller's own and the sentence is load-bearing for existence hiding, so
  it is left alone and noted here.
- **A missing required query parameter** still answers with the framework's own
  sentence, which names the parameter and its .NET type. `AOS-R002-008`'s record
  is about the request body; this is the same handler arm and a smaller leak.

## Status

```
REPAIR WAVE 003C — LOCAL IMPLEMENTATION COMPLETE, AUTHORITATIVE VALIDATION PENDING
```

Not closed: authoritative CI, `postgres:18.6` integration and Nightly are not run
during this programme.

---

## Addendum — where a refusal is shown, after `AOS-R002-010`

**Date:** 2026-09-18, after this wave's report was written. Additive: this wave's
findings and their repairs are unchanged.

The owner decided `AOS-R002-010`: a **recoverable** refusal keeps the dialog that
caused it, with its values, its context and its focus. That changes *where* some
of this wave's sentences are read, not what they say.

- **`AOS-R002-007`** — the conflict sentence is unchanged. For a mutation started
  in a dialog it is now read inside that dialog, which is the surface that can
  offer a reload and a retry.
- **`AOS-R002-008`** — a body that could not be read is unchanged, and the field
  it names is now what the dialog associates the message with. The work
  `AOS-R002-008` did to name the field is what makes that association possible.
- **`AOS-R002-024`** — unchanged and extended: the dialog shows
  `Detail ?? Message` for the same reason the pages do.

Nothing about authorization, status codes or existence hiding changed with it.
`AOS-R002-014` remains an open owner decision.
