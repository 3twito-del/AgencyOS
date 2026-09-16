# Repair Wave 003B — admission

§0. The twenty fields, read from the authoritative Audit 002 evidence and
**re-derived from source before anything was written**. Nothing here is
reconstructed from memory.

**Source of record:** [`AUDIT-002B-GUID-FIELDS.md`](../audit-002/phase-b/AUDIT-002B-GUID-FIELDS.md)
(the per-field table) and [`AUDIT-002C-GUID-FIELDS.md`](../audit-002/phase-c/AUDIT-002C-GUID-FIELDS.md)
(the finalised counts).

**Re-derivation.** A field counts when its value is parsed with `Guid.TryParse`
or `Guid.Parse` in the dialog's own code-behind. Running that rule over
`src/AgencyOS.Windows/Dialogs/*.xaml.cs` today returns **13 dialogs** and the
same **20 controls**, name for name. The count has not moved.

---

## Reconciliation

| Classification | Audit 002 | Here | Admitted | Deferred |
| --- | ---: | ---: | ---: | ---: |
| `PICKER_REQUIRED_LIKELY` | 11 | 11 | **10** | 1 |
| `CONTEXT_DERIVABLE_LIKELY` | 4 | 4 | **4** | 0 |
| `OWNER_DESIGN_DECISION_REQUIRED` | 4 | 4 | 0 | **4** |
| `DEBUG_ONLY` | 1 | 1 | 0 | **1** |
| **Total** | **20** | **20** | **14** | **6** |

**13 dialogs.** **11** are changed. Four of those keep one deferred owner/lead
field (`CreateDealDialog`, `CreateContractDialog`, `CreateOpportunityDialog`,
`CreatePackageDialog`). Two are untouched (`LinkRecordDialog`,
`AddIntelligenceSubjectDialog`).

---

## The twenty

Abbreviations: **CD** = `CONTEXT_DERIVABLE_LIKELY`, **PR** =
`PICKER_REQUIRED_LIKELY`, **OD** = `OWNER_DESIGN_DECISION_REQUIRED`, **DB** =
`DEBUG_ONLY`.

### 1 · `CreateDealDialog` · `OpportunityIdBox`

| | |
| --- | --- |
| ID type | `OpportunityId` |
| Entity | Opportunity |
| Current control | `TextBox`, header "Opportunity id" |
| Workflow context | Opened from **Deals** by `deal.create`. **No opportunity is in context.** |
| Human representation | Yes — Pipeline lists opportunities by name |
| Lookup API | `GET /opportunities` → `ListOpportunitiesAsync` |
| Existing picker pattern | Yes — `AddRadarEntryDialog` |
| Audit classification | **CD** |
| Admitted | **Yes** |
| Planned repair | **HUMAN_ENTITY_SELECTION** |
| Reason | Audit 002 wrote *"a deal is opened from an opportunity"*. **That workflow does not exist.** `deal.create` is a Deals-workspace command and `DealsPage.CreateAsync` constructs the dialog with no opportunity. Context cannot derive what the product never put in context, so §3's proof fails and §2 requires a picker instead. Reclassified with evidence, not preference. |

### 2 · `CreateDealDialog` · `TargetIdBox`

| | |
| --- | --- |
| ID type | `OpportunityTargetId` |
| Entity | Opportunity target |
| Current control | `TextBox`, header "Target id" |
| Workflow context | Narrowed by field 1 |
| Human representation | Yes — `OpportunityTargetResponse.DisplayName`, `.Stage` |
| Lookup API | `GET /opportunities/{id}` → `.Targets` (bounded child collection) |
| Audit classification | **CD** |
| Admitted | **Yes** |
| Planned repair | **CONTEXT_DERIVED** (dependent on field 1) |
| Reason | Once an opportunity is chosen *in the dialog*, its targets are a bounded child collection. The operator chooses among them, but the **set** is derived — and it is revalidated whenever field 1 changes. |

### 3 · `CreateDealDialog` · `OwnerIdBox`

| | |
| --- | --- |
| Entity | Internal user |
| Audit classification | **OD** |
| Admitted | **No — deferred** |
| Reason | §17. A directory now exists (`GET /organizations/{id}/members`, closed by 003E-A), so the question is answerable — but whether an owner field is a picker over members, a default to the acting user, or something else has more than one defensible answer. Not decided here. |

### 4 · `CreateContractDialog` · `DealIdBox`

| | |
| --- | --- |
| ID type | `DealId` |
| Entity | Deal |
| Current control | `TextBox`, header "Deal id" |
| Workflow context | Opened from **Contracts** by `contract.create`. **No deal is in context.** |
| Human representation | Yes — `DealSummaryResponse.Name`, `.CounterpartyDisplayName` |
| Lookup API | `GET /deals` → `ListDealsAsync` |
| Audit classification | **CD** |
| Admitted | **Yes** |
| Planned repair | **HUMAN_ENTITY_SELECTION** |
| Reason | Same as field 1. `ContractsPage.CreateAsync` constructs the dialog with no deal. The assumed context does not exist. |

### 5 · `CreateContractDialog` · `OfferIdBox`

| | |
| --- | --- |
| ID type | `OfferId` |
| Entity | Accepted offer |
| Current control | `TextBox`, header "Accepted offer id" |
| Workflow context | Determined by field 4 |
| Human representation | Yes — on the deal's detail |
| Lookup API | `GET /deals/{id}` → `.AcceptedOffer` — **a single nullable field** |
| Audit classification | **CD** |
| Admitted | **Yes** |
| Planned repair | **CONTEXT_DERIVED** — no operator choice at all |
| Reason | `DealDetailResponse.AcceptedOffer` is one nullable offer, so a deal has exactly one or none. There is nothing to choose: the value follows from field 4. The control is removed from the dialog and replaced by a statement of what was found. |

### 6 · `CreateContractDialog` · `OwnerIdBox` — **OD, deferred.** As field 3.

### 7 · `CreateOpportunityDialog` · `OwnerIdBox` — **OD, deferred.** As field 3.

### 8 · `CreateOpportunityDialog` · `SubjectIdBox`

| | |
| --- | --- |
| ID type | `TalentProfileId`, `PackageId`, `ProjectRoleId` or `ProjectId`, by the opportunity kind |
| Entity | **Not "person or company"** as Audit 002 recorded — see correction below |
| Current control | `TextBox`, header "Subject" |
| Workflow context | Opened from Pipeline with no subject in context |
| Human representation | Yes — each kind lists by name |
| Lookup API | `ListTalentAsync`, `ListPackagesAsync`, `ListProjectsAsync`; for staffing, `GetProjectAsync(id).Roles` |
| Audit classification | **PR** |
| Admitted | **Yes** |
| Planned repair | **HUMAN_ENTITY_SELECTION**, source chosen by the existing kind box |
| Reason | The dialog already asks which kind of pursuit this is, and `SubjectKindFor` maps that kind to the subject's record type. The picker follows it; changing the kind invalidates the selection (§12). |

**Correction found during repair.** Audit 002's table says this field references
"Person or company". The code says otherwise: `SubjectKindFor` maps
`TalentEngagement` → TalentProfile, `PackageMarket` → Package, `Staffing` →
ProjectRole, and everything else → Project. Staffing is the awkward one: project
roles have **no tenant-wide list**, only `ProjectDetailResponse.Roles`, so the
project is chosen first and its roles fetched. Still admitted — four kinds, all
meaningful, one bounded cascade — which is a different answer from field 18's
fourteen.

### 9 · `CreatePackageDialog` · `ProjectIdBox`

| | |
| --- | --- |
| Entity | Project |
| Lookup API | `ListProjectsAsync` |
| Human representation | `ProjectSummaryResponse.Title`, `.Type`, `.Year` |
| Audit classification | **PR** · Admitted **Yes** · **HUMAN_ENTITY_SELECTION** |
| Reason | Packages is its own workspace, so no project is in context. A flat tenant-scoped list exists. |

### 10 · `CreatePackageDialog` · `LeadIdBox` — **OD, deferred.** As field 3.

### 11 · `AddOpportunityTargetDialog` · `TargetIdBox`

| | |
| --- | --- |
| Entity | Company or person, by the dialog's kind box |
| Lookup API | `ListCompaniesAsync`, `ListPeopleAsync` |
| Audit classification | **PR** · Admitted **Yes** · **HUMAN_ENTITY_SELECTION** |

### 12 · `AddOpportunityTargetDialog` · `ContactIdBox`

| | |
| --- | --- |
| Entity | Person — the named contact at the target |
| Lookup API | `ListPeopleAsync` |
| Audit classification | **PR** · Admitted **Yes** · **HUMAN_ENTITY_SELECTION** |
| Reason | Optional field; the picker must therefore offer "nobody in particular" as a real choice rather than forcing one. |

### 13 · `AddPackageElementDialog` · `TargetIdBox`

| | |
| --- | --- |
| Entity | **Six kinds**, not three — see correction below |
| Lookup API | `GetProjectAsync(package.ProjectId)` for four kinds, `ListPeopleAsync` and `ListCompaniesAsync` for two |
| Audit classification | **PR** · Admitted **Yes** · **HUMAN_ENTITY_SELECTION** |

**Correction found during repair.** Audit 002 recorded "person, company or
project". The dialog offers six kinds, and the server checks each against a
different table (`M5Repositories`):

| Kind | Checked against | Source |
| --- | --- | --- |
| Attached party | attachments **on this package's project** | the project's roles |
| Proposed person | people in the organization | `ListPeopleAsync` |
| Proposed company | companies in the organization | `ListCompaniesAsync` |
| Open role | roles **on this package's project** | the project's unfilled roles |
| Material | materials in the organization | the project's materials |
| Source property | source properties in the organization | the project's source properties |

All six resolve from one read of the package's own project plus two
organization lists, which is why this field is admitted where field 18 is not.

### 14 · `AddProjectCompanyDialog` · `CompanyIdBox`

| | |
| --- | --- |
| Entity | Company |
| Lookup API | `ListCompaniesAsync` |
| Audit classification | **PR** · Admitted **Yes** · **HUMAN_ENTITY_SELECTION** |

### 15 · `AttachToRoleDialog` · `PartyIdBox`

| | |
| --- | --- |
| Entity | Person or company, by the dialog's own party-kind box |
| Workflow context | `ProjectsPage` passes `role.Type` and `role.Label` for the heading |
| Lookup API | `ListPeopleAsync`, `ListCompaniesAsync` |
| Audit classification | **PR** · Admitted **Yes** · **HUMAN_ENTITY_SELECTION** |
| Reason | The dialog asks whether the party is a person or a company; the picker follows that answer and clears when it changes (§12). |

**Correction.** An earlier draft of this note said the role's own type decides
the kind. It does not — the dialog has its own `PartyKindBox`. Corrected on
reading the markup.

### 16 · `RecordPitchDialog` · `MaterialIdBox`

| | |
| --- | --- |
| Entity | Material — **talent-scoped**: `GET /talent/{personId}/materials` |
| Workflow context | Opened from an opportunity **target**, not from a talent |
| Lookup API | `ListMaterialsAsync(personId)`, over the opportunity's subjects |
| Audit classification | **PR** · Admitted **Yes** · **HUMAN_ENTITY_SELECTION** |
| Reason | Materials hang off a person, and the opportunity's `Subjects` are exactly the people the pursuit is about. The page gathers their materials and the dialog offers them, labelled title · type · version. |

### 17 · `RecordSubmissionDialog` · `MaterialIdBox` — as field 16. Admitted **Yes**.

Optional here rather than required: `RecordSubmissionDialog` treats an
unparseable value as "none", so the picker must keep "nothing attached" as a real
choice.

### 18 · `LinkRecordDialog` · `TargetIdBox`

| | |
| --- | --- |
| Entity | **Any of fourteen kinds** — Person, Company, TalentProfile, Material, Project, Package, Opportunity, Submission, Deal, Offer, Contract, ContractVersion, Invoice, Payment |
| Audit classification | **PR** |
| Admitted | **No — deferred** |
| Reason | See below. |

**Why this one is deferred, on evidence rather than convenience.**

1. **Thirteen of the fourteen kinds have a flat tenant-scoped list. One does
   not.** `ContractVersion` exists only nested under `GET /contracts/{id}` →
   `.Versions`, so it needs a two-level cascade that no other kind needs.
2. **Supporting all fourteen uniformly is precisely the shape §6 forbids** — a
   kind-keyed table of endpoint plus display function plus object, which is
   architecture-by-configuration by another name.
3. **Supporting thirteen and leaving `ContractVersion` on a raw GUID violates
   §7** for an admitted field.
4. **Dropping `ContractVersion` from the kind list reduces product capability**,
   which is a product-design decision Audit 002 did not settle.
5. **The dialog's own documentation says the list is not a workflow decision**:

   > The fourteen targets are the ones the database can actually enforce a
   > foreign key against.

   The kinds exist because the schema can enforce them, not because an operator
   files documents against all fourteen. Which of them deserve a picker — and
   whether filing against an Offer or a Payment is a real workflow at all — is an
   open design question.

§1: *"If multiple reasonable designs remain: DEFER."* They do. It is deferred,
and the decision note is in the report.

### 19 · `LinkResearchItemDialog` · `IdBox`

| | |
| --- | --- |
| Entity | Source, Signal, Thesis, Prediction or Task — five kinds, by the dialog's existing kind box |
| Lookup API | `ListIntelligenceSourcesAsync`, `ListSignalsAsync`, `ListThesesAsync`, `ListPredictionsAsync`, `ListTasksAsync` |
| Audit classification | **PR** · Admitted **Yes** · **HUMAN_ENTITY_SELECTION** |
| Reason | Unlike field 18, **every one of the five kinds has a flat tenant-scoped list**, the kinds are a closed set the dialog already declares, and each is a first-class intelligence record an operator works with by name. Five explicit arms, not a configuration table. |

### 20 · `AddIntelligenceSubjectDialog` · `IdBox`

| | |
| --- | --- |
| Entity | Person, company or project |
| Audit classification | **DB** |
| Admitted | **No — deferred, unchanged** |
| Reason | §18. **Nothing in the client constructs this dialog** — Audit 002 counts it among the four unreachable dialogs, and the closure slice confirmed that disposition. An ordinary operator cannot reach it, so there is no operator-facing raw-identifier defect to repair. Turning it into a picker would be productising a surface nobody can open. Verified rather than assumed, below. |

---

## Verification that field 20 is genuinely unreachable

```
$ grep -rn "AddIntelligenceSubjectDialog" src/AgencyOS.Windows --include=*.cs
    (declaration only — no construction site)
```

Audit 002's `coverage.json` lists it under `unreachableDialogs`. The closure
slice did not move it. If that ever changes, §18 requires stopping and
reclassifying before altering it — not silently productising it.

---

## What this wave will not touch

Per §19 and §20: `AOS-R002-020` (mailbox visibility), `AOS-R001-010`,
`AOS-R001-013`, `AOS-R002-021`, the 003C refusal-UX family, the 003D validation
family beyond the accessibility of selectors this wave touches, 003E and 003F,
and the 218 fire-and-forget dispatch sites.
