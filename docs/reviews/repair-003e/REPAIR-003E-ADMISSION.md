# Repair Wave 003E — admission

**Theme:** representation completeness
**Canonical contents:** `AOS-R001-010`, the three unwired finance dialogs,
`AOS-R002-013`
**Baseline:** the working tree as 003D left it
**Date:** 2026-09-18

---

## What this wave admits

| Finding | Disposition | Why |
| --- | --- | --- |
| `AOS-R001-010` | **Admitted and repaired** | Four server commands had no client caller. The endpoints exist, the workspace already shows their result, and every value they take is a closed domain enum. Nothing was left to decide. |
| `AOS-R002-013` | **Not admitted — owner decision** | `ownerDecisionRequired: true`, recorded as an Observation with `"expected": "Not established"`. |
| The three unwired finance dialogs | **Not admitted — deliberately unreachable** | Still unwired, and on purpose: the capability they belong to was never built, and M13 chose not to advertise it. Wiring them is a milestone, not a repair. |

---

## `AOS-R001-010` — why it was admitted rather than deferred

Audit 002's own proposal put this under "**WAVE 003E — completeness, owner
decision required**" and called it a product-shape question. That reading was
made when the gap looked like a whole absent capability area. The closure slice
had already narrowed it — credits and materials turned out to have callers — and
what remains is four commands, not a capability.

Checked before writing any code:

| Question that could have needed an owner | Answer |
| --- | --- |
| Do the endpoints exist? | Yes, all four, in `M4Endpoints.cs`, permission-gated on `representations.write` and version-checked. |
| Where would this live? | The Talent workspace already shows scopes and team. The surface is decided; only the commands were absent. |
| What values may an area take? | `RepresentationScopeArea` — a closed domain enum. |
| What roles may somebody hold? | `RepresentationTeamRole` — Lead, Agent, Coordinator, Assistant. |
| Who may be assigned? | A member of the organization, which `ListOrganizationMembersAsync` already returns. |

Nothing there is a product-shape question. The one judgment the product does make
— one lead at a time, and assigning a new one ends the old assignment — is the
domain's, stated in `AssignRepresentationTeamMemberHandler`, and the dialog says
so rather than deciding it.

Per §1 this wave's stop condition was "a required endpoint does not exist". All
four exist, so the wave proceeded.

## `AOS-R002-013` — not admitted

An email address the server stores as typed. Its record says
`ownerDecisionRequired: true`, its confidence is `Observation` rather than a
defect, and its expected behaviour is explicitly **not established**. Its own
risk note is the reason: a format check added later refuses data already stored,
and agency contact records legitimately hold partial and unusual values.

**Nothing about it was changed.**

## The three unwired finance dialogs — not admitted

Re-measured. The inventory's three are still unwired, and no other finance dialog
is:

| Dialog | Constructed by |
| --- | --- |
| `RecordInvoiceDialog` | `FinancePage` |
| `RecordPaymentDialog` | `FinancePage` |
| `AllocatePaymentDialog` | `FinancePage` |
| `PostJournalEntryDialog` | `FinancePage` |
| `FinanceReasonDialog` | `FinancePage`, `DocumentsPage`, `CommunicationsPage` |
| **`CalculateCommissionDialog`** | **nothing** |
| **`RaiseReceivableDialog`** | **nothing** |
| **`RecordMonetaryObligationDialog`** | **nothing** |

**They are unreachable on purpose**, and the reason is recorded in the codebase
rather than inferred:

> `commission.calculate` is deliberately absent. `CalculateCommissionDialog`
> exists and is complete, but nothing can reach it: the obligations list it needs
> was never built, so the palette entry dispatched nowhere. M13 stopped
> advertising it rather than pretending (ADR-0032).
>
> — `tests/AgencyOS.Tests.Unit/Client/FinanceViewModelTests.cs`

The three are one unbuilt capability, not three loose ends: an obligation is
recorded, a commission is calculated from it, a receivable is raised against it.
ADR-0032's decision was that the palette lists only what it can dispatch, and
M13 chose the honest half of that rather than a command that does nothing.

Wiring them means building the obligations capability, which is a milestone and
not a repair. §1 stops on exactly this, so it is recorded and left.

---

## Recorded, not repaired — a representation no workspace lists

Found while building the fixture for this wave's live evidence, and reported
because it is a real gap rather than because it was in scope.

Converting a prospect creates a representation for that **person**. The Talent
workspace lists **talent profiles**. A person with a prospect record but no
talent profile therefore gains a representation that no list in the product
shows: they leave the Prospects list on conversion and never appear in Talent.

Measured:

```
prospect Zoë Ångström  -> converted -> representation 01a0b2de-f782-7106-9ea3-52e214389d65
prospects afterwards   -> 2 rows, she is not among them
talent afterwards      -> 7 rows, she is not among them
```

This is not `AOS-R001-010`: that one is about commands with no caller, and this
is a record with no list. It is not repaired here because the fix is a product
decision — whether conversion should create a talent profile, or whether the
Talent list should include represented people who have none — and §1 stops on
exactly that.
