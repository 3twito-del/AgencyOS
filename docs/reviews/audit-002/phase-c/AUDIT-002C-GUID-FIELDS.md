# Audit 002 Phase C — the typed-identifier fields, finalised

§11. `AOS-R001-006`, re-counted after Repair Wave 003E-A and closed as a
finding of record.

---

## The count did not move

Re-derived from source, not carried forward: a field counts when its value is
parsed with `Guid.TryParse` or `Guid.Parse` in the dialog's own code-behind.

```
typed-identifier fields: 20 across 13 dialogs
```

Identical to Audit 001, Phase A and Phase B. The full per-field table with each
field's referent, whether that referent is shown by name anywhere, and whether it
is derivable from context is in
[`AUDIT-002B-GUID-FIELDS.md`](../phase-b/AUDIT-002B-GUID-FIELDS.md) and is not
restated here — nothing about it changed.

| Classification | Fields |
| --- | ---: |
| `PICKER_REQUIRED_LIKELY` | 11 |
| `CONTEXT_DERIVABLE_LIKELY` | 4 |
| `OWNER_DESIGN_DECISION_REQUIRED` | 4 |
| `DEBUG_ONLY` | 1 |

---

## What Phase C adds

### Repair Wave 003E-A introduced none

The wave that created the membership surface was explicitly forbidden from asking
an operator to type a `UserId`, `OrganizationId`, `MembershipId` or `RoleId`.
Verified rather than assumed:

| Surface | Asks for a typed identifier |
| --- | --- |
| `AddMemberDialog` | no |
| `ChangeMemberRoleDialog` | no |
| `OrganizationPage` | no |

`AddMemberDialog` identifies a person by the subject and display name their
identity provider already knows, and `ChangeMemberRoleDialog` offers the four
roles by what each one means. Neither shows or accepts an identifier.

### The four owner/lead fields are now partly answerable

Phase B classified `OwnerIdBox` and `LeadIdBox` — four fields across four dialogs
— as `OWNER_DESIGN_DECISION_REQUIRED`, and noted the reason was entangled with
`AOS-R002-002`: the client had no way to list internal users because there was no
way to create a second one.

`AOS-R002-002` is closed. `GET /organizations/{id}/members` now returns every
active member with their display name and role, and every persona in the role
matrix reads it at `200`.

So the blocker has changed shape. It is no longer *"there is no directory to
draw from"* — there is one. It is now *"these four dialogs do not use it"*.

**The classification stays `OWNER_DESIGN_DECISION_REQUIRED` all the same**, and
deliberately. Whether an owner field should be a picker over the member list, a
default to the person doing the work, or something else is a design question with
more than one defensible answer, and §11 forbids designing one here. What Phase C
establishes is only that the question is now answerable without inventing a user
directory first.

---

## Status of the finding

`AOS-R001-006` — **CONFIRMED, unchanged in scope, narrowed in cause.**

Twenty fields still ask a person to type a value the product displays nowhere.
The severity is unchanged. What changed is that four of the twenty are no longer
blocked on missing capability, which makes them cheaper to resolve than the
other sixteen and is worth saying when the repair wave is planned.

## Evidence

- `artifacts/reviewer/run-002-phase-c/guid-fields.json`
