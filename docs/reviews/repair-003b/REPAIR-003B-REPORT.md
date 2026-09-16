# Repair Wave 003B — entity selection and raw identifier elimination

**Wave:** AgencyOS Review Repair Wave 003B
**Finding:** `AOS-R001-006` — 20 typed-identifier fields across 13 dialogs
**Scope:** those twenty fields. Nothing else.
**Branch:** `repair-wave-001`
**Date:** 2026-09-16

No surface in the Windows client displays an identifier. Twenty fields asked for
one anyway. Fourteen of them no longer do.

---

## 1. Starting repo tip

`2301fe5` — docs-only, on the Audit 002 final closure slice.

## 2. Starting product baseline

`ba077a3` — the Repair Wave 003A executable commit.

## 3. The authoritative twenty-field inventory

Read from Audit 002's own evidence and **re-derived from source before any code
was written**: a field counts when its value is parsed with `Guid.TryParse` or
`Guid.Parse` in the dialog's own code-behind. That rule returns the same 20
controls, name for name.

Per-field table with referent, source, classification and decision:
[`REPAIR-003B-ADMISSION.md`](REPAIR-003B-ADMISSION.md).

| Classification | Audit 002 | Admitted | Deferred |
| --- | ---: | ---: | ---: |
| `PICKER_REQUIRED_LIKELY` | 11 | 10 | 1 |
| `CONTEXT_DERIVABLE_LIKELY` | 4 | 4 | 0 |
| `OWNER_DESIGN_DECISION_REQUIRED` | 4 | 0 | 4 |
| `DEBUG_ONLY` | 1 | 0 | 1 |
| **Total** | **20** | **14** | **6** |

## 4. The thirteen dialogs

| Dialog | Fields | Repaired | Deferred |
| --- | ---: | ---: | ---: |
| `CreateDealDialog` | 3 | 2 | 1 owner |
| `CreateContractDialog` | 3 | 2 | 1 owner |
| `CreateOpportunityDialog` | 2 | 1 | 1 owner |
| `CreatePackageDialog` | 2 | 1 | 1 lead |
| `AddOpportunityTargetDialog` | 2 | 2 | — |
| `AddPackageElementDialog` | 1 | 1 | — |
| `AddProjectCompanyDialog` | 1 | 1 | — |
| `AttachToRoleDialog` | 1 | 1 | — |
| `RecordPitchDialog` | 1 | 1 | — |
| `RecordSubmissionDialog` | 1 | 1 | — |
| `LinkResearchItemDialog` | 1 | 1 | — |
| `LinkRecordDialog` | 1 | 0 | 1 |
| `AddIntelligenceSubjectDialog` | 1 | 0 | 1 |
| **13** | **20** | **14** | **6** |

**11 dialogs changed.** Two are untouched.

## 5. Fields admitted — 14

Ten pickers, two derived, and two reclassified from derived to picker on
evidence.

## 6. Fields deferred — 6

Four owner/lead (owner decision), `LinkRecordDialog.TargetIdBox` (design open),
`AddIntelligenceSubjectDialog.IdBox` (unreachable). Notes:
[`REPAIR-003B-DEFERRED-DECISIONS.md`](REPAIR-003B-DEFERRED-DECISIONS.md).

## 7. Context-derived repairs — 2

**`CreateDealDialog.TargetIdBox`.** A target belongs to exactly one opportunity.
Choosing the opportunity in the dialog fetches its targets; changing it clears
the selection, because carrying one across would send the server a pair it
refuses and the operator would meet a refusal rather than a cleared field.

**`CreateContractDialog.OfferIdBox`.** `DealDetailResponse.AcceptedOffer` is a
single nullable field, so there is nothing to choose. The control is gone.

The dialog's own documentation says the accepted offer must never be inferred —
it is the baseline every later draft is reconciled against (ADR-0022) — so it is
**shown, not silently used**: choosing a deal displays which offer will be
papered, and a deal with nothing accepted is refused with that reason instead of
a validation failure about an empty identifier. What is asked has not changed,
only how it is answered.

## 8. Picker repairs — 12

| Dialog · field | Source | Label |
| --- | --- | --- |
| `CreateDealDialog.OpportunityIdBox` | `ListOpportunitiesAsync` | name · kind · status |
| `CreateContractDialog.DealIdBox` | `ListDealsAsync` | name · counterparty · status |
| `CreateOpportunityDialog.SubjectIdBox` | talent / packages / projects, or a project's roles for staffing | by kind |
| `CreatePackageDialog.ProjectIdBox` | `ListProjectsAsync` | title · type · year |
| `AddOpportunityTargetDialog.TargetIdBox` | companies or people, by the kind box | name · type |
| `AddOpportunityTargetDialog.ContactIdBox` | `ListPeopleAsync` | name · company |
| `AddPackageElementDialog.TargetIdBox` | the package's own project, plus people and companies | by kind |
| `AddProjectCompanyDialog.CompanyIdBox` | `ListCompaniesAsync` | name · type |
| `AttachToRoleDialog.PartyIdBox` | people or companies, by the kind box | name · company |
| `RecordPitchDialog.MaterialIdBox` | materials of the pursuit's subjects | title · type · version |
| `RecordSubmissionDialog.MaterialIdBox` | same | same |
| `LinkResearchItemDialog.IdBox` | the chosen kind's own list, five of them | by kind |

**Two reclassifications, on evidence.** Audit 002 marked
`CreateDealDialog.OpportunityIdBox` and `CreateContractDialog.DealIdBox`
`CONTEXT_DERIVABLE_LIKELY`, reading that *"a deal is opened from an opportunity"*
and a contract from a deal. **Neither workflow exists.** `deal.create` is a
Deals-workspace command and `contract.create` a Contracts one; both dialogs are
constructed with nothing selected. §3 requires the context to be *proved*, and it
is not there, so §2 makes them pickers. Adding the missing navigation would be
product-completeness work and belongs to another wave.

## 9. Owner-decision fields — 4, not decided

See §17 note. A member directory now exists, so the question is answerable; it is
still a question.

## 10. Debug-only field — 1, unchanged

`AddIntelligenceSubjectDialog` is constructed by nothing. Verified by grep and
corroborated by Audit 002's unreachable list. Not productised.

## 11. Existing selection patterns reused

`AddRadarEntryDialog` already does this: `ComboBox` + `Header` +
`DisplayMemberPath`, the list passed in by the page, `Chosen()` returning the
record and `ToRequest()` taking its `Id`. `RecordPaymentDialog`,
`RecordSignalDialog`, `RecordSignatureDialog` and `RecordNoticeDialog` follow it.
**Every repaired field uses that shape.** `ConnectCompanyDialog` was the
precedent for a dialog that holds `IAgencyOsApi` and loads as the operator
chooses, which is what the four dependent pickers needed.

Nothing new was invented at the control level.

## 12. Shared abstraction introduced

Two small records in `AgencyOS.Client`:

- **`EntityChoice(Guid Id, string Label)`** — one factory per entity kind.
- **`PackageElementSources`** — the six sources a package element can point at,
  because they travel together and come from the same two reads.

## 13. Why the abstraction is justified — and what it is not

§6 asks for the case to be written down.

**Repeated requirement.** Twelve fields across eleven dialogs need the same four
things: a query, results the caller may see, a human label, and a canonical id.

**Entity differences.** Eleven entity kinds, each told apart by different fields.
A person by where they work; a material by its version; a deal by who is on the
other side; a role by what it is for.

**What it does not do.** There is no way to hand `EntityChoice` an endpoint, a
display function or an untyped object. Every kind has its own typed factory
naming its own fields. "Which columns identify a person" is a decision written
once and reviewable, not a lambda repeated at eleven call sites. §6's forbidden
shape — *"arbitrary endpoint + arbitrary display function + arbitrary object"* —
is not expressible against this type.

`LinkResearchItemDialog`'s five kinds and `AddPackageElementDialog`'s six are
explicit `switch` arms in their own dialogs, each naming its own call. Verbose on
purpose, and the same choice the repository already makes when it registers AI
tools one line each rather than by scanning.

## 14. APIs reused

`ListPeopleAsync`, `ListCompaniesAsync`, `ListProjectsAsync`, `ListTalentAsync`,
`ListPackagesAsync`, `ListOpportunitiesAsync`, `ListDealsAsync`,
`ListMaterialsAsync`, `ListIntelligenceSourcesAsync`, `ListSignalsAsync`,
`ListThesesAsync`, `ListPredictionsAsync`, `ListTasksAsync`, `GetProjectAsync`,
`GetOpportunityAsync`, `GetDealAsync`.

**Smallest correct source** (§9): a package element reads the package's own
project rather than six tenant-wide lists; a pitch reads the materials of the
pursuit's subjects rather than every material; a deal's targets come from the
chosen opportunity.

## 15. APIs added

**None.** Every picker had a source already.

## 16. Schema impact

**NONE.**

## 17. OpenAPI impact

**NONE.** Regenerated and compared byte for byte after normalisation: identical.
263 paths, 187 schemas, OpenAPI 3.1.1.

## 18. Contract impact

**NONE.** API contract stays `14`.

## 19. Keyboard result

Every picker is a `ComboBox`, which is what the client already uses for selection
and what its keyboard behaviour is already built around: Tab reaches it, Up/Down
and type-ahead move through it, Enter commits, Escape closes the drop-down
without closing the dialog. The dialog pass records **14 forward and 14 reverse
Tab stops** on the repaired dialogs it reached, with focus never escaping the
dialog and Escape closing it every time.

No `CommandRegistry` gesture changed.

**What is not claimed.** Nobody has operated these dialogs by keyboard alone. The
Tab traversal is the harness's, and that is what the evidence says.

## 20. Accessibility result

Every picker carries a `Header`, which is where WinUI takes a combo box's
accessible name from — the rule Repair Wave 002 established when it found fifteen
combo boxes announcing nothing (`AOS-R001-004`). Read back from the running tree:

```
CompanyBox -> "Company"      DealBox    -> "Deal"
OpportunityBox -> "Opportunity"   TargetBox -> "Target" / "Which company"
SubjectBox -> "Project"      MaterialBox -> "Material"
```

`EntityChoice.ToString()` returns the label, so a combo box that falls back to
`ToString()` announces the label and never the record — the failure mode that put
identifiers into row announcements in the first place. Asserted by
`EntityChoiceTests`.

**Zero accessibility observations** on all eight dialogs the pass reached.

## 21. Cross-tenant and security result

Measured against six personas. `expectedVersion: 99` is deliberately stale, so a
`409` proves authorization passed and the server still enforced its own checks.

| Subject | people | companies | projects | write |
| --- | --- | --- | --- | --- |
| `review-elsewhere` — owner of the **other** tenant | 403 | 403 | 403 | **403** |
| `review-observer` — read-only | 200 | 200 | 200 | **403** |
| `review-member` | 200 | 200 | 200 | 409 |
| `review-restricted` | 200 | 200 | **403** | 403 |
| `w2-owner` — owner | 200 | 200 | 200 | 409 |
| `nobody-at-all` — unauthenticated | 401 | 401 | 401 | 401 |

An invented identifier answers `404 "Company '…ff' was not found."`

**Picker visibility is convenience, not permission.** `review-observer` can
populate every picker and cannot write through any of them. No cross-tenant
record is reachable through any picker source, and no server check was weakened
because "the picker only shows valid choices".

One note on method: `w2-owner` is a member of **both** organizations in this
fixture — a Phase C artefact — so it is useless as a cross-tenant probe. That is
why `review-elsewhere` exists in the table. Checked rather than assumed.

## 22. Stale-context result

- **Stale aggregate version** — `expectedVersion: 99` → `409`, for both roles that
  are authorized to write. Unchanged semantics; no new concurrency behaviour was
  invented.
- **Stale selection inside a dialog** — changing the opportunity clears the
  target; changing the party kind clears the party; changing the element kind
  clears the element; changing the research kind clears the item and drops a
  slower in-flight response rather than letting it overwrite a newer one.

## 23. Runtime dialogs verified

**Eight of the eleven changed dialogs**, opened through the real interface
against a fixture of 50 people, 16 companies, 20 projects, 3 opportunities, 2
deals, 1 package and 7 talent profiles:

`AddOpportunityTargetDialog` · `AddPackageElementDialog` ·
`AddProjectCompanyDialog` · `CreateContractDialog` · `CreateDealDialog` ·
`CreateOpportunityDialog` · `CreatePackageDialog` · `LinkResearchItemDialog`

All eight: opened, focus entered, Escape closed, focus restored, zero
accessibility observations, no raw-identifier input present.

**Three could not be verified at runtime**, and this is reported rather than
worked around:

`AttachToRoleDialog` · `RecordPitchDialog` · `RecordSubmissionDialog`

The WinUI client **exits** while the pass reaches them — a stowed exception,
`0xc000027b` in `Microsoft.UI.Xaml.dll`, from the Windows event log.

**It is not caused by this wave.** Reproduced identically with `src/` stashed to
the pre-repair baseline, and at both 1600×1000 and 1920×1080 — so it is neither
the repair nor the clipped layout of `AOS-R002-021`. Audit 002's closure slice
recorded all three as opened against a different fixture state, so something in
the current data reaches a path that crashes the framework.

Recorded as a new observation. Not diagnosed further, because doing so is the
scope expansion §20 forbids; the three dialogs' repairs are covered by the
structural and unit tests, and by `AddPackageElementDialog` and
`AddOpportunityTargetDialog`, which exercise the same two patterns at runtime.

## 24. Representative mutation and read-back

`AddProjectCompanyDialog` — the operator reads **"A24 · Studio"**:

```
POST …/projects/{id}/companies
  {"companyId":"01a0a015-c5eb-7113-8db8-1888e1b4febb", "capacity":"Studio", …}
→ 201  {"id":"01a0a71e-48f0-7af0-9d4f-1865a61767d8"}

GET …/projects/{id}
  companyId = 01a0a015-c5eb-7113-8db8-1888e1b4febb
  name      = A24
```

The identifier persisted is exactly the one behind the label. The label is
presentation only.

## 25. Structural regression guard

`RawIdentifierEntryTests`, over all 63 dialog markup files:

1. **No dialog asks for a canonical identifier.** A text input whose own label
   contains "identifier", "id", "guid" or "uuid" as a **whole word** fails,
   unless it is one of the six named exemptions or asks for something external.
2. **Every exemption still exists.** A dead exemption stops protecting anything.
3. **Each repaired field is a `ComboBox` with a `Header`**, named one by one —
   because "there is no TextBox" is also satisfied by deleting the field.
4. **The accepted offer is shown, not asked.**

**Proved to fire, twice.** Against the pre-repair baseline it names all fourteen
fields. Against a mutation that put a single `TextBox x:Name="CompanyIdBox"` back
into a repaired dialog, it failed on both rules.

**One honest correction.** The first version matched "id" as a substring and
flagged two dialogs for the word *said* — the noisy heuristic §23 warns against,
caught by running it. The second version used a `\b` regex that was written into
the file as a literal backspace byte, so it matched nothing and passed while
dead. It is now word tokens, and the mutation test above is what proves it fires.

`DialogScannerTests` pinned the finding at 20/13 so it could not shrink
unnoticed. It shrank deliberately; the pin moved with it and now **names the six
remaining fields** rather than counting them, so one deferred field cannot be
swapped for a newly introduced one without the test noticing.

## 26. Unit count

**3,755** — unchanged. No domain behaviour changed.

## 27. Windows count

**911**, from 824. `RawIdentifierEntryTests` 79, `EntityChoiceTests` 8.

## 28. Reviewer count

**152** — unchanged.

## 29. Integration count

**821** — unchanged. No API, schema or contract change.

## 30. `postgres:18.6` evidence

CI, on the exact tip. See the closing block.

Local runtime evidence used PostgreSQL 19beta3 and is LAB evidence only.

## 31. TLA+ result

**4 / 4.** `OfflineWriteQueue`, `OutboundSend`, `AiApproval`,
`LocalInferenceLease`.

## 32–34. Commit, tip and CI

In the closing block.

## 35. Final `AOS-R001-006` disposition

**`PARTIALLY_REPAIRED`.**

Fourteen of twenty fields no longer ask for an identifier. Six remain, all
deliberately: four awaiting an owner decision, one awaiting a product-design
decision, one in a dialog nothing constructs. §30 says not to call the finding
repaired while user-facing defect scope remains, and four required owner fields
still make three create dialogs unusable without a value nobody can obtain. That
is user-facing, so the finding stays open.

## 36. Exact unresolved identifier fields

| Dialog | Field | Why |
| --- | --- | --- |
| `CreateDealDialog` | `OwnerIdBox` | `OWNER_DESIGN_DECISION_REQUIRED` |
| `CreateContractDialog` | `OwnerIdBox` | `OWNER_DESIGN_DECISION_REQUIRED` |
| `CreateOpportunityDialog` | `OwnerIdBox` | `OWNER_DESIGN_DECISION_REQUIRED` |
| `CreatePackageDialog` | `LeadIdBox` | `OWNER_DESIGN_DECISION_REQUIRED` |
| `LinkRecordDialog` | `TargetIdBox` | fourteen kinds, one without a flat list; design open |
| `AddIntelligenceSubjectDialog` | `IdBox` | `DEBUG_ONLY` — nothing constructs the dialog |

## 37. `AOS-R002-020` untouched

Confirmed. `ConnectMailboxDialog` was not opened, edited or referenced by this
wave. `git diff` touches no file belonging to it. The pending
remove-the-option / add-the-visibility decision is recorded in the deferred note
and **not chosen**.

## 38. No 003C–003F work occurred

Confirmed. Nothing belonging to `AOS-R001-010`, `AOS-R001-013`, `AOS-R002-007`,
`AOS-R002-008`, `AOS-R002-010` – `AOS-R002-016`, `AOS-R002-018`, `AOS-R002-021`
or the 218 fire-and-forget dispatch sites was edited. The only accessibility work
is the `Header` on selectors this wave created, which §20 permits explicitly.

The Reviewer changed in one place: `SelectTabByName` now treats a transient
`COMException` as "tab not found" instead of ending the run. It cost eight
dialogs' evidence in the first pass. Audit tooling, allowed by §20.

---

## Closing

| | |
| --- | --- |
| **Executable commit** | `7d88659` |
| **Authoritative CI** | run `35030384303` on `7d88659` — **success, both jobs** |
| **Integration** | **821 / 821** against `postgres:18.6` |

| Gate | Before | After |
| --- | --- | --- |
| build | 0 / 0 | **0 warnings / 0 errors** |
| unit | 3,755 | **3,755** |
| Windows | 824 | **911** |
| reviewer | 152 | **152** |
| integration | 821 | **821 / 821 vs `postgres:18.6`** |
| OpenAPI | 263 / 187 | **263 / 187** |
| API contract | 14 | **14** |
| TLA+ | 4 / 4 | **4 / 4** |

Nightly was not dispatched: this wave changes the Windows client and its tests
only. No API, schema, contract, packaging or database path moved, and the CI
run above already builds the release artifact.

## A new observation, filed and not repaired

**`AttachToRoleDialog`, `RecordPitchDialog` and `RecordSubmissionDialog` crash the
client** in the current fixture state — `0xc000027b`, a stowed exception in
`Microsoft.UI.Xaml.dll`. It reproduces on the pre-repair baseline and at two
window sizes, so it predates this wave and is not the clipped layout of
`AOS-R002-021`. Audit 002's final closure slice opened all three against earlier
fixture state, so a change in the data — the closure slice added a connected
mailbox, sent messages and an uploaded document, and this wave added one project
company — reaches a path that brings the framework down.

A client that exits is more serious than anything else in this report, and it is
not this wave's to fix. It needs its own diagnosis, starting from the minidump
Windows Error Reporting wrote.
