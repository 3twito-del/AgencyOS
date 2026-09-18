# Final remaining-item register

Every known item still open before this pass, reconciled. Written before any code
was changed, so the "reproduced" column is what was observed rather than what was
expected.

**Date:** 2026-09-18 · **Tree:** the cumulative local repair tree after 003C–003F
and the five resolved owner decisions

| Item | Kind | Sev | Reproduced | Root cause known | Precedent | Repairable without new semantics | Admitted | Target |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `AOS-R002-020` | UX / false affordance | S3 | **yes** — `400 "Visibility 'Organization' is not valid"` | yes | yes | yes | **yes** | `REPAIR_NOW` |
| `AOS-R002-025` | **correctness** | S3 | **yes** — `500`, `ArgumentNullException('party')` | yes | yes | yes | **yes** | `REPAIR_NOW` |
| `AOS-R002-026` | UX / temporal | S3 | **yes** — instant stored with dialog-open time | yes | yes | yes | **yes** | `REPAIR_NOW` |
| `AOS-R001-006` ×4 owner/lead | UX / raw identifier | S3 | **yes** — typed GUID fields | yes | yes | yes | **yes** | `REPAIR_NOW` |
| `AOS-R001-006` `LinkRecordDialog` | UX / raw identifier | S3 | **yes** | yes | partial | **see below** | **yes** | `REPAIR_NOW` for the kinds that can be selected |
| `AOS-R001-006` `AddIntelligenceSubjectDialog` | debug-only | S4 | **not reachable** — nothing constructs it | yes | n/a | n/a | no | `ACCEPT_CURRENT_BEHAVIOR` |
| 218 → **234** fire-and-forget sites | observation | — | **no defect attributed** | n/a | n/a | n/a | classification only | `DEFER_NO_REPRODUCED_DEFECT` |
| `MailboxSynchronizer` timestamps | observation | — | **to determine** | partly | yes (`AOS-R002-001`) | to determine | investigate | to be set by evidence |
| Representation with no list | observation | — | **yes** — converted prospect vanishes | to determine | to determine | to determine | investigate | to be set by evidence |
| `contract` gate non-idempotence | tooling correctness | — | **yes** — second run produces no document | yes | n/a | yes | **yes** | `REPAIR_NOW` |

---

## Detail

### `AOS-R002-020` — mailbox visibility

**Current.** `ConnectMailboxDialog` offers three options — `Private`, `Shared`,
`Organization` — and `MailboxVisibility` declares two: `Private = 1`,
`Shared = 2`. Choosing the third earns
`400 "Visibility 'Organization' is not valid. Expected one of: Private, Shared."`

**Owner decision, this pass:** remove the unsupported option. Not latent
capability — a false affordance. Organization-wide mailbox visibility would be a
new authorization capability, and nothing in the product has one.

**Surface:** `ConnectMailboxDialog.xaml`, one `ComboBoxItem`.

### `AOS-R002-025` — `500` for a missing required nested object

**Current.** `POST /interactions` with `participants: [{ "role": "Contact" }]` and
no `party` answers **500** with a trace id. The exception is
`ArgumentNullException ('party')` thrown by `EndpointParsing.ToEndpoint`, which is
**application mapping**, not model binding: the body binds fine, `Party` is simply
`null`, and the first thing that dereferences it throws.

**Boundary:** `APPLICATION_MAPPING`.

**Expected.** A structurally incomplete request is a `400`, naming what is
missing — which is exactly what `AOS-R002-008` established for a body that could
not be read.

### `AOS-R002-026` — an instant the operator cannot see

**Current.** `RecordPitchRequest.OccurredAt` and `RecordSubmissionRequest.SentAt`
are `DateTimeOffset?` — instants, per `AOS-R002-001`'s temporal map. Both dialogs
offer a `DatePicker` only, so the time of day stored is whenever the dialog was
constructed. A meeting recorded on Friday for Tuesday is stored as Tuesday at
Friday's time of day.

**Not in scope:** `RecordSubmissionRequest.ResponseExpectedBy` is `DateOnly?` — a
calendar date, correctly a date picker, and it stays one.

**Owner decision, this pass:** make the whole instant editable — date **and**
time, in local time, both visible and changeable before submit.

### `AOS-R001-006` — the six remaining fields

| Field | Dialog | Selector source available today |
| --- | --- | --- |
| `OwnerIdBox` | `CreateDealDialog` | `ListOrganizationMembersAsync` |
| `OwnerIdBox` | `CreateContractDialog` | `ListOrganizationMembersAsync` |
| `OwnerIdBox` | `CreateOpportunityDialog` | `ListOrganizationMembersAsync` |
| `LeadIdBox` | `CreatePackageDialog` | `ListOrganizationMembersAsync` |
| `TargetIdBox` | `LinkRecordDialog` | 14 kinds — see below |
| `IdBox` | `AddIntelligenceSubjectDialog` | **nothing constructs this dialog** |

`ListOrganizationMembersAsync` is the same capability 003E used for representation
teams, and the picker built there (`RepresentationMaintenance.MembersToAssign`) is
the precedent.

#### `LinkRecordDialog` — the fourteen kinds

| Kind | List source | Tenant-wide |
| --- | --- | --- |
| Person | `ListPeopleAsync` | yes |
| Company | `ListCompaniesAsync` | yes |
| TalentProfile | `ListTalentAsync` | yes |
| Project | `ListProjectsAsync` | yes |
| Package | `ListPackagesAsync` | yes |
| Opportunity | `ListOpportunitiesAsync` | yes |
| Submission | `ListSubmissionsAsync` | yes (filters optional) |
| Deal | `ListDealsAsync` | yes |
| Offer | `ListOffersAsync` | yes (`dealId` optional) |
| Contract | `ListContractsAsync` | yes |
| Invoice | `ListInvoicesAsync` | yes (filters optional) |
| Payment | `ListPaymentsAsync` | yes (filters optional) |
| **Material** | `ListMaterialsAsync(personId)` | **no — belongs to a person** |
| **ContractVersion** | **no list method** | **no — belongs to a contract** |

**Twelve of fourteen can be selected directly.** Two are children of a parent
record and would need the operator to choose the parent first. Neither is removed.

### `AddIntelligenceSubjectDialog` — reconfirmed

The full dialog sweep on this tree reports it
`UNREACHABLE_NOTHING_CONSTRUCTS`, alongside the three finance dialogs. Nothing in
the product opens it. Left exactly as it is; productising an unreachable dialog
for consistency would be work nobody can use.

### Fire-and-forget

**234 sites** on this tree (the recorded figure was 218; the count has grown with
the product, and 47 of them are `_ = LoadAsync(`). To be classified statically,
not investigated one by one, and checked against the real failures this programme
has actually diagnosed.

### `MailboxSynchronizer`

`ProviderMessage.SentAt` and `ReceivedAt` are `DateTimeOffset?` with **no
documented UTC requirement**, and `MailboxSynchronizer` passes them to persistence
untouched. Whether that is a defect depends on whether a non-zero offset is
within the provider contract and whether it actually fails.

### Representation with no list

A prospect converted without a talent profile leaves the Prospects list and never
appears in Talent. Reproduced in 003E. Whether the state is valid by design, and
which existing surface should own it, is what this pass has to answer.

### `contract` gate non-idempotence

The gate deletes `artifacts/openapi` and then runs an **incremental** build. On a
second run with nothing to rebuild, the generation target does not execute and no
document is produced, so the gate throws *"No OpenAPI document was produced"*.

The generated bytes themselves are deterministic — two forced regenerations give
the identical hash `942d0e74…`. The defect is the gate, not the output.

**Classification:** `REPOSITORY_MUTATION` — the gate destroys its own output and
then depends on a build that has no reason to run.

---

## Final dispositions

Set by evidence, after the work.

| Item | Target | Outcome |
| --- | --- | --- |
| `AOS-R002-020` | `REPAIR_NOW` | **REPAIRED** — the unsupported option is gone; the two the domain has remain. |
| `AOS-R002-025` | `REPAIR_NOW` | **REPAIRED** — `500` → `400 "Participant is required."`, proved live. Three sibling sites sharing the same root were repaired with it. |
| `AOS-R002-026` | `REPAIR_NOW` | **REPAIRED** — date and time, both editable, composed with the offset in force on the chosen date. |
| `AOS-R001-006` ×4 owner/lead | `REPAIR_NOW` | **REPAIRED** — member pickers, defaulting to whoever is signed in by the directory's own `IsSelf`. |
| `AOS-R001-006` `LinkRecordDialog` | partial | **REPAIRED for 12 of 14**; two kinds belong to a parent record and keep the identifier box with a stated reason. |
| `AOS-R001-006` `AddIntelligenceSubjectDialog` | `ACCEPT_CURRENT_BEHAVIOR` | **ACCEPTED** — reconfirmed unreachable by the runtime sweep. |
| Fire-and-forget (230 sites) | `DEFER_NO_REPRODUCED_DEFECT` | **DEFERRED as a pattern**, and **one sub-class reproduced and repaired**: 13 page commands whose refusal was lost entirely. |
| `MailboxSynchronizer` | to be set by evidence | **REPAIRED** — a `+03:00` provider timestamp reproduced `Cannot write DateTimeOffset with Offset=03:00:00`; normalized at the ingestion boundary. |
| Representation with no list | to be set by evidence | **ACCEPTED** — not unreachable. The person stays in People, and the converted prospect lists when "Open only" is cleared (measured: 2 → 4 rows). |
| `contract` gate | `REPAIR_NOW` | **REPAIRED** — `--no-incremental`; two consecutive runs now both produce the identical document. |

### One item this pass surfaced rather than closed

`AOS-R002-003` — seventeen dialogs with no opening control — was **not** in the
list this pass was asked to reconcile, and is still open with
`ownerDecisionRequired: true`. Re-measured here: **15** on this tree, 13 of them
the Intelligence workspace's authoring surface. It is the one thing standing
between this tree and the local release-candidate criteria.
