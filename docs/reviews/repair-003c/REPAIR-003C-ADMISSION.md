# Repair Wave 003C — admission

**Authorization and refusal UX.** Written before any code changed.

Canonical scope from
[`REPAIR-WAVE-MAP.md`](../audit-002/final-closure/REPAIR-WAVE-MAP.md): *"003C —
authorization / refusal UX: `AOS-R002-007`, `AOS-R002-008`, `AOS-R002-014` —
three shapes of a refusal written for the log rather than for the operator."*
`AOS-R002-024` was filed later by Repair Wave 003A.1 with `suggestedRepairWave:
003C` and is admitted here too.

**Nobody's authority changes in this wave.** No permission, role, membership,
tenant rule, existence-hiding behaviour, idempotency contract or concurrency
check is touched. What changes is what the product *says* when the server has
already refused.

---

## Admission table

| Finding | Symptom | Surface | Server result today | Windows behaviour today | Intended behaviour | Authorization / existence-hiding implication | Admitted | Root class | Planned repair |
| --- | --- | --- | --- | --- | --- | --- | :-: | --- | --- |
| **`AOS-R002-024`** | The operator is told "Invalid request" instead of the reason the server gave | Projects, Pipeline (and one dialog found by survey, below) | `400` with `detail` carrying the domain's sentence | `DetailError(failure.Message)` — the *title*, not the reason | show the server's own sentence, as seven other surfaces already do | none — the same bytes the server already sent to this caller | **yes** | client dropped `Detail` | use `Detail ?? Message`, the established pattern |
| **`AOS-R002-007`** | A `409` names the record by an identifier the product displays nowhere, and says nothing about what to do next | every versioned aggregate | `409`, `detail`: `Contract '01a0a1ff-…' has changed since you last saw it (you had version 9, it is now 10).` | shows that sentence | name the *kind* of record, keep both versions, add the next step | none — the identifier is removed from prose; `entityId` stays in the machine-readable extension | **yes** | server message written for a log | rewrite the exception's sentence |
| **`AOS-R002-008`** | A body that cannot be bound is refused with an internal DTO class name and no field | every endpoint taking a body | `400`, `detail`: `Failed to read parameter "CreateDealRequest request" from the request body as JSON.` | not surfaced today | name the field that could not be read, and what was expected; never name a DTO or a .NET type | none — the field name is the caller's own | **yes** | framework message passed through verbatim | translate body-binding failures in the existing handler |
| **`AOS-R002-014`** | A `403` names an internal permission string, does not say what was attempted, and does not say who could grant it | all workspaces | `403`, `detail`: `Permission 'finance.payments.read' is required.` | shows that sentence | *undecided* | the wording decides how much of the authorization model is described to someone who was refused | **NO** | owner decision | **not repaired — see below** |

## `AOS-R002-014` is not admitted

Its own record says `ownerDecisionRequired: true`, and: *"Low, but the wording is
a design decision and several are reasonable."*

The choice is real and it is the owner's:

- **A** — keep the permission string. Precise, and an operator can quote it to
  whoever administers the organization.
- **B** — describe the capability instead ("You cannot read payments"), naming no
  internal string.
- **C** — add who can grant it, which means the refusal tells the reader
  something about the organization's roles.

Each says a different amount about the authorization model to somebody who has
just been refused, so it is not a copy tweak. Nothing about this finding is
changed in this wave, on the server or in the client. It stays open.

## One site admitted beyond the finding's own list

`AOS-R002-024` names `ProjectsPage` and `PipelinePage`. A survey of every
`catch (AgencyOsApiException …)` in the client found **one more site with the
identical defect**: `LinkResearchItemDialog.xaml.cs:108` sets a hint from
`failure.Message`. `ViewModelBase` and the other seven surfaces already use
`Detail ?? Message` and are correct.

It is admitted: same root, same one-line change, and leaving it would mean this
wave knowingly left the last instance of the thing it repaired.

## Explicitly excluded

`AOS-R002-020`, `AOS-R002-021`, `AOS-R002-025`, `AOS-R002-026`, the remaining
`AOS-R001-006` fields, `AOS-R001-010`, `AOS-R001-013`, and the 218
fire-and-forget sites.

**`AOS-R002-025` in particular**: a request whose required nested object is
absent answers `500`. Making that `500` prettier is not this wave's business —
the status itself is wrong, and that is a correctness question, not a copy one.
This wave does not touch it, and does not let the binding repair drift into it.

## Status classes this wave must keep distinct

`400` validation · `401` unauthenticated · `403` authorization · `404`
not-found and existence-hiding · `409` conflict · `413` payload · `415` media
type · domain refusals. The client shows whatever the server said for the
status it sent; nothing here collapses them into one sentence.

## What must not appear in any refusal

Exception type names, stack frames, SQL or Npgsql text, tenant identifiers,
internal record identifiers, route templates, secrets.
