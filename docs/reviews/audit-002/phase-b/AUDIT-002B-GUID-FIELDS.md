# Audit 002 Phase B — the twenty typed-identifier fields

`AOS-R001-006`, completed per field as §17 requires.

**20 fields across 13 dialogs**, unchanged from Audit 001's count and from
Phase A's recount.

---

## How each field behaves

Tested at the API boundary and read from each dialog's code-behind.

| Case | Result |
| --- | --- |
| **Malformed identifier** | **Cannot be submitted.** All 13 dialogs parse with `Guid.TryParse`, and 12 of 13 keep the primary button disabled until every identifier parses. The thirteenth, `RecordSubmissionDialog`, treats an unparseable value as "none" for a field that is optional. No dialog can send a malformed identifier. |
| **Well-formed, nonexistent** | `404` — *"Opportunity '482486f7-…' was not found."* Plain, names the type, does not guess. |
| **Well-formed, wrong entity type** | `404`, identical message. Passing an owner's identifier where an opportunity is wanted says only that no opportunity has it — so the type is not used to disclose that the record exists as something else. |
| **Another organization** | `403` — *"Permission 'deals.write' is required."* Refused on permission before existence is considered, which is ADR-0038's rule. |
| **Malformed, sent directly to the API** | `400` — *"Failed to read parameter \"CreateDealRequest request\" from the request body as JSON."* Technical, names an internal DTO, and does not say which field. Not reachable from any dialog, because of the gating above. Recorded as `AOS-R002-008`. |

**The client's handling of these fields is sound.** The finding is not that the
values are mishandled. It is that a person has to know them.

---

## The material fact, restated

**No surface in the Windows client displays an identifier.** No page binds an id
to any visible element. Repair Wave 002 removed identifiers from row
announcements — correctly; they were being read aloud to screen-reader users — and
nothing replaced them.

So the twenty fields below ask for values the product shows nowhere. Before Wave
002 an operator could have heard one in a row's accessible name. That is not an
argument against Wave 002; it is why this finding is more urgent than when it was
filed.

---

## The twenty fields

| # | Dialog | Field | References | Shown by name elsewhere? | Derivable from context? | Classification |
| ---: | --- | --- | --- | --- | --- | --- |
| 1 | `CreateDealDialog` | `OpportunityIdBox` | Opportunity | Yes — Pipeline lists them by name | **Yes** — a deal is opened from an opportunity | `CONTEXT_DERIVABLE_LIKELY` |
| 2 | `CreateDealDialog` | `TargetIdBox` | Opportunity target | Yes — under its opportunity | **Yes** — narrowed by field 1 | `CONTEXT_DERIVABLE_LIKELY` |
| 3 | `CreateDealDialog` | `OwnerIdBox` | Internal user | **No** — the client has no user directory | No | `OWNER_DESIGN_DECISION_REQUIRED` |
| 4 | `CreateContractDialog` | `DealIdBox` | Deal | Yes — Deals lists them by name | **Yes** — opened from the deal | `CONTEXT_DERIVABLE_LIKELY` |
| 5 | `CreateContractDialog` | `OfferIdBox` | Accepted offer | Yes — on the deal's detail | **Yes** — a deal has exactly one accepted offer | `CONTEXT_DERIVABLE_LIKELY` |
| 6 | `CreateContractDialog` | `OwnerIdBox` | Internal user | **No** | No | `OWNER_DESIGN_DECISION_REQUIRED` |
| 7 | `CreateOpportunityDialog` | `OwnerIdBox` | Internal user | **No** | No | `OWNER_DESIGN_DECISION_REQUIRED` |
| 8 | `CreateOpportunityDialog` | `SubjectIdBox` | Person or company | Yes — People and Companies | No — the dialog is not opened from one | `PICKER_REQUIRED_LIKELY` |
| 9 | `CreatePackageDialog` | `ProjectIdBox` | Project | Yes — Projects lists them | Partly — Packages is its own workspace | `PICKER_REQUIRED_LIKELY` |
| 10 | `CreatePackageDialog` | `LeadIdBox` | Internal user | **No** | No | `OWNER_DESIGN_DECISION_REQUIRED` |
| 11 | `AddOpportunityTargetDialog` | `TargetIdBox` | Company or person | Yes | No | `PICKER_REQUIRED_LIKELY` |
| 12 | `AddOpportunityTargetDialog` | `ContactIdBox` | Person | Yes — People | No | `PICKER_REQUIRED_LIKELY` |
| 13 | `AddPackageElementDialog` | `TargetIdBox` | Person, company or project | Yes | No | `PICKER_REQUIRED_LIKELY` |
| 14 | `AddProjectCompanyDialog` | `CompanyIdBox` | Company | Yes — Companies | No | `PICKER_REQUIRED_LIKELY` |
| 15 | `AttachToRoleDialog` | `PartyIdBox` | Person or company | Yes | Partly — the role is selected | `PICKER_REQUIRED_LIKELY` |
| 16 | `RecordPitchDialog` | `MaterialIdBox` | Material | Yes — on the talent's materials | No | `PICKER_REQUIRED_LIKELY` |
| 17 | `RecordSubmissionDialog` | `MaterialIdBox` | Material | Yes | No | `PICKER_REQUIRED_LIKELY` |
| 18 | `LinkRecordDialog` | `TargetIdBox` | Any linkable record | Yes, by kind | No | `PICKER_REQUIRED_LIKELY` |
| 19 | `LinkResearchItemDialog` | `IdBox` | Any intelligence record | Yes, by kind | Partly — the case is selected | `PICKER_REQUIRED_LIKELY` |
| 20 | `AddIntelligenceSubjectDialog` | `IdBox` | Person, company or project | Yes | n/a — **nothing constructs this dialog** | `DEBUG_ONLY` |

### Counts

| Classification | Fields |
| --- | ---: |
| `PICKER_REQUIRED_LIKELY` | **11** |
| `CONTEXT_DERIVABLE_LIKELY` | **4** |
| `OWNER_DESIGN_DECISION_REQUIRED` | **4** |
| `DEBUG_ONLY` | **1** |
| `POWER_USER_ID_INTENTIONAL` | 0 |
| `EXTERNAL_ID_INTENTIONAL` | 0 |

**None is an unavoidable external identifier.** Every one names a record AgencyOS
owns and already displays under a human name — except the four owner/lead fields,
which name an internal user the client has no way to list at all. Those four are
entangled with `AOS-R002-002`: there is no user directory because there is no way
to create a second user.

---

## What this audit did not do

It did not design a picker, and it does not recommend one. Four shapes are
reasonable for the eleven `PICKER_REQUIRED_LIKELY` fields — a searchable picker, a
context-derived value, navigating from the record first, or keeping the
identifier for power users — and the four `CONTEXT_DERIVABLE_LIKELY` fields may
want a different answer from the eleven. That is a repair-wave decision.

`AOS-R001-006` stays **CONFIRMED** and is now classified per field.
