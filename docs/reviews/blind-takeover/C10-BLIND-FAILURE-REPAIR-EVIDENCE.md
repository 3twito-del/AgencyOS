# C10 blind failure repair - evidence

A bounded record of the only product repair authorised from the genuine blind takeover (C10 / J12) of ALPHA 0.1.0 build 96. Feature freeze applies: three failures, plus the named same-mechanism Activity siblings, and nothing else.

This record does not say that C10 is proved, that Freeze Exit is complete, or that Control Room has accepted the repair. The repaired code is **not** build 96, and no build 97 was made.

## 1. Baseline

| Item | Value |
| --- | --- |
| Branch | `operational-regression-gate` |
| Starting HEAD | `0602bae0a17e3d36447f00994bfe407a4377c1de` (docs-only build-96 release record) |
| Build 96 product commit | `691e32fc545a13d28fd40b1fd6c6671dcd2e55ac` (tag `alpha-691e32f`) |
| `691e32f..0602bae` | `docs/releases/ALPHA-0.1.0-build-96.md` only (+141 lines) |
| master | `c85bf42c4ce1c120d68c2566134f3ec785c7ba1c` (untouched) |
| API contract / schema | 17 / `20260909072201_AiResultClassification` (both unchanged) |
| Working tree at start | clean |

## 2. The C10 defects

### BF-01 - People false empty state

- **What the operator saw.** A People search for "Joel", on a tenant holding 50 people, showed "No people yet - Create the first person to begin."
- **Cause.** `PeoplePage` opened its single `ListEmpty` bar from `PeopleListViewModel.IsEmpty`, which was `!IsLoading && !HasError && People.Count == 0`. That property knew only the rows just returned. A filtered answer with no rows and an empty directory therefore gave the same statement. So did a list that had never been loaded.

### BF-02 - Reconcile disabled exactly when the missing terms needed inspection

**The count of 3 is canonical and true, and it was not changed.**

- Three terms were negotiated and the latest draft records none.
- The domain classifies each as `MissingFromContract`, which is a reconciliation difference. `UnresolvedDifferenceCount = 3` is therefore correct.
- The defect is one of **actionability**, not a false count. The client offered Reconcile only when `HasTerms && LatestVersion is not null`.
- `HasTerms` means "some version has visible term rows". It is false both for a draft that records no terms and for a caller who may not read terms.
- As a result, the one comparison that names the three terms was unreachable.

### BF-03 - Activity chronology omitted; opaque ID in Deal Activity

The Deal, Pipeline (opportunity) and Contract Activity rows had no time.

- **The data exists.** `DealHistoryEntryResponse`, `OpportunityHistoryEntryResponse` and `ContractHistoryEntryResponse` each carry the authoritative `OccurredAt`, and the read side already orders them by it. The Windows templates never bound it, so this was a presentation omission, not missing data.
- **The opaque ID.** A superseded offer's Deal Activity row showed "Answered by offer <GUID>." as its secondary text.
- **Its exact source.** The domain writes the reason `$"Answered by offer {counter}."` in `Offer.NoteAnsweredByCounter` (`src/AgencyOS.Domain/Deals/Offer.cs`). The read side then maps it to `Detail` on the history kind `OfferAnsweredByCounter` (`DealQueries.cs`).
- **The canonical record was left as it is.**

## 3. Pre-fix reproductions (run before the production change)

All ran against the build-96 behaviour and **failed**, as they should.

- Unit tests (log `prefix-unit`): **5 of 5 failed**.
- Windows tests (log `prefix-win`): **6 of 6 failed**.

| Defect | Test | Pre-fix result |
| --- | --- | --- |
| BF-01 | `PeopleSearchAbsenceTests.ASearchThatMatchesNobodyDoesNotSayTheDirectoryIsEmpty` | `Assert.False()` failed: `IsEmpty` was true for a filtered, zero-row answer |
| BF-01 | `PeopleSearchAbsenceTests.NothingLoadedYetIsNotAnEmptyDirectory` | `Assert.False()` failed: `IsEmpty` was true before any load |
| BF-02 | `ReconciliationReachTests.ReportedDifferencesAgainstADraftWithNoTermsCanBeReconciled` | `Assert.True()` failed: the gate was closed with 3 differences, a latest version and 0 visible term rows |
| BF-02 | `ReconciliationReachTests.ARefusedReconciliationLeavesNoEarlierComparisonOnScreen` | `Assert.Null()` failed: the earlier comparison survived a refusal |
| BF-03 | `DealActivityDetailTests.ASupersededOfferIsNotExplainedByItsIdentifier` | `Assert.DoesNotContain()` failed: the raw GUID was on the row |
| BF-03 | `OperationalListParityTests.AnActivityRowShowsWhenItsEventOccurred` × Deals, Pipeline, Contracts | no `OccurredAt` binding in any of the three templates |
| BF-03 | `OperationalListParityTests.AnActivityRowAnnouncesTheInstantItShows` × Deals, Pipeline, Contracts | the announcements were, respectively, `Outbound offer recorded, By: Review Owner, Offer transition`, `Target moved to Passed, Target: Cresswell Media` and `Signature recorded, By: Review Owner, Signature`. None has a time. |

**How the BF-02 gate was reproduced.** The page's gate was first moved, with identical logic, into `ContractDetailViewModel.CanReconcile` (`HasTerms && LatestVersion is not null`), and the page was pointed at that property. The reproduction therefore ran through the real client path. The negative control M5 below re-applies exactly that pre-repair gate and fails.

## 4. Production changes

### BF-01 - `PeopleListViewModel` (client) and `PeoplePage` (Windows)

- The view model now records which search a *successful* load answered. It is cleared at the start of each load and set only on success.
- `IsEmpty` means that the directory holds nobody. It is true only after a successful **unfiltered** load with zero rows. As before, the client treats blank text as unfiltered.
- The new `HasNoMatches` means that a successful **filtered** load matched nobody.
- Neither is true while loading, after a failure, after a cancellation, or before any load.
- The statement belongs to the search that was run, not to text typed since.
- `PeoplePage` keeps "No people yet / Create the first person to begin." for the empty directory only. It adds `ListNoMatches`, with the title "No people match this search" and the message "Nobody in the directory matches this search. Try a different name, email or title." That wording follows the product's existing "No contracts match these filters." pattern.
- No count endpoint was added.

### BF-02 - `ContractDetailViewModel.CanReconcile`, `ReconciliationViewModel` (client) and `ContractsPage` (Windows)

- **The gate.** `CanReconcile = LatestVersion is not null && (HasTerms || Contract.UnresolvedDifferenceCount > 0)`, and the page reads it. The count is the canonical summary that is already shown to the same caller in the "Draft against agreed terms" bar. The gate therefore leaks nothing new.
- **Authorization.** No permission logic was added to the client. The server's reconciliation endpoint remains the authority: it enforces `contracts.read`, `contracts.terms.read` and `deals.economics.read`, and its source is unchanged.
- **Stale comparisons.** `ReconciliationViewModel.LoadAsync` now clears the previous comparison before it asks. A refusal is then shown through the existing "Could not reconcile" error bar with the server's own message, and no earlier or partial comparison stays under it.
- **Nothing else changed.** `UnresolvedDifferenceCount` was not touched, no term was synthesised, and the reconciliation semantics are as they were.

### BF-03 - three Activity templates, their row profiles, and `DealActivityLine` (client)

- **The time is now shown.** `DealsPage`, `PipelinePage` and `ContractsPage` `HistoryList` rows show `{Binding OccurredAt}` in a leading column, and the summary and context are kept. This is the existing Document and Finance history presentation: the same binding and the same layout.
- **The time is now announced.** The row profiles `DealHistory` and `ContractHistory` (new) and `OpportunityHistory` (extended) add `Text("OccurredAt")`, so the same row announces the same instant. The accessible form is `RowLabel`'s existing one, `yyyy-MM-dd HH:mm:ss zzz`.
- **Actor and kind are now visible, as a consequence.** Deal and Contract history rows already *announced* the actor and kind through inferred labels ("By: Review Owner, Offer transition") and never showed them. The product's parity gate (`EveryProfileFieldIsAScanFactItsTemplateShows`) requires every announced profile field to be visible. Dropping them from the accessible row would have removed information a screen-reader user already had. Both rows therefore now show the actor, through the existing `Party` converter, and the kind, through `DisplayLabel`, exactly as the Finance history row does. This adds no new data.
- **The order is unchanged.** The server's order is kept; the view model replaces the items in the order received.
- **The raw ID is replaced with a readable offer reference.** `DealActivityLine.Detail` applies only to the history kind `OfferAnsweredByCounter`, and only when the detail exactly matches the domain's recorded form `Answered by offer <guid>.`:
  - when the answering offer is among the deal's loaded offers, the row reads, for example, "Answered by offer 2 (outbound).", naming it by sequence and direction as the Offers tab does;
  - otherwise the detail is left out rather than shown raw.
- **Everything else is untouched.** Every other detail, including any human-written reason, is returned unchanged. The server record, domain history and audit are not changed; only the client copy bound to the row is written. This is not a generic GUID scrubber.

## 5. Negative controls (executed; each mutation temporary and restored byte-identically)

- Each mutation was applied, and the named tests were run with `dotnet test --filter …`.
- The source was then restored, and its SHA-256 was checked.
- The whole working-tree diff hash was the same before and after the run.
- **All 10 were caught.**

| # | Mutation (what a wrong repair would do) | Caught by |
| --- | --- | --- |
| M1 | Conflate: any successful zero answer is an empty directory | `ASearchThatMatchesNobody…`, `TheNoMatchStatementBelongsToTheSearchThatWasRun`, `NothingLoadedYet…` |
| M2 | Hide truth: never say the directory is empty | `AnUnfilteredAnswerWithNobodyIsAnEmptyDirectory` (×2), `WhileLoadingNoAbsenceIsStated` |
| M3 | Unknown as absent: no answer counted as an unfiltered one | `NothingLoadedYetIsNotAnEmptyDirectory` |
| M4 | Offer Reconcile for any draft | `NoVisibleTermsAndNoReportedDifferenceOffersNothing` (0, null) |
| M5 | Restore the pre-repair gate `HasTerms` | `ReportedDifferencesAgainstADraftWithNoTermsCanBeReconciled` |
| M6 | A refusal leaves the earlier comparison on screen | `ARefusedReconciliationLeavesNoEarlierComparisonOnScreen` |
| M7 | Hide truth: drop every Deal Activity detail | `HumanDetailIsKept` |
| M8 | Generic GUID scrubber on every history kind | `HumanDetailIsKept` (an `OfferRejected` reason of the same text is kept) |
| M9 | Time shown but not announced (Deal profile without `OccurredAt`) | `AnActivityRowAnnouncesTheInstantItShows` (Deals) |
| M10 | Visible date without the time, disagreeing with the announced instant | `AnActivityRowShowsWhenItsEventOccurred` (Deals) |

M3 as first written was an **equivalent mutant**, and survived: the `_answered is not null` guard inside `Absent` was redundant. The redundancy was removed, M3 was rewritten to mutate the decision itself, and all ten were rerun on the final code.

## 6. Proof sets after the repair

**BF-01** (`PeopleSearchAbsenceTests`, 10 cases):

1. An unfiltered, successful zero-row answer is an empty directory. This holds for blank and whitespace search, and `HasNoMatches` is false.
2. A filtered, successful zero-row answer states that nothing matched, and `IsEmpty` is false.
3. A populated answer states neither, filtered or not.
4. A failed load states neither, filtered or not.
5. Before any load, and during a reload, neither is stated.
6. The statement belongs to the search that was run.

**BF-02** (`ReconciliationReachTests`, 10 cases):

1. With 3 reported differences, a latest version and 0 visible term rows, `CanReconcile` is true, and the standing still reads "3 term(s) differ…".
2. The authorised comparison renders three `MissingFromContract` lines:
   - each with its negotiated value (`140,000.00 GBP`, `First position`, `2027-02-01`) and no draft side;
   - the summary is "3 not carried into the draft.";
   - the fee row announces `GuaranteedCompensation, Agreed: 140,000.00 GBP, Result: MissingFromContract`.
3. With no drafting version, nothing is offered.
4. **Authorization negative control.** A 403 from the reconciliation endpoint, with "Permission 'deals.economics.read' is required.", is surfaced as that refusal: no lines, no summary, not faithful, `DifferenceCount` 0 and not empty. After an earlier success, nothing from that comparison remains, so no economic or hidden term is disclosed.
5. With no visible terms and a count of 0 or null, nothing is offered, as before.
6. Visible terms still offer the comparison, and an ordinary `Changed` comparison still renders.

**BF-03** runs through the real row templates, their bindings and their row profiles (`OperationalListParityTests.Chronology`, 9 cases), plus `DealActivityDetailTests` (4 cases):

- **Chronology.** Each of the three rows shows `OccurredAt`, and the test compares it *as an instant* with the event's time. Each announces the same instant in the same row's accessible name. Each keeps its summary as the headline and its human context: the Detail visible and on HelpText for Deal and Contract, and "Target: …" for Pipeline. Deal and Contract still announce "By: Review Owner".
- **The offer reference.** The raw GUID is replaced by "Answered by offer 2 (outbound).". An answering offer the page does not hold gives no detail, rather than a raw one.
- **Human detail is kept, the order is unchanged, and the canonical record is unchanged.**

**Structural guards** (`C10BlindFailureSurfaceTests`, 3 cases):

- the People page's two absence statements are distinct, each opened from its own view-model answer;
- the no-match text says nothing about "yet", "first person" or "Create";
- the Reconcile gate is exactly `loaded && _detail.CanReconcile`.

## 7. Local regression (Windows 11, `pwsh scripts/Invoke-AgencyOS.ps1`)

| Gate | Baseline (`0602bae`) | After the repair |
| --- | --- | --- |
| `build` (Debug) | pass, 0 warnings | pass, 0 warnings, 0 errors |
| `test-unit` | 4120 passed / 0 failed / 0 skipped | **4144** passed / 0 failed / 0 skipped (+24 new) |
| `test-windows` | 1832 / 0 / 0 | **1844** / 0 / 0 (+12 new) |
| `test-reviewer` | 163 / 0 / 0 | **163** / 0 / 0 |
| `contract` (OpenAPI) | - | pass; the API still reports `apiContract=17` |

- No established count decreased.
- No test was skipped, quarantined, renamed or deleted.
- One existing test file was extended: `OperationalListParityTests.cs` classifies the seven new visible bindings, each as Primary, which obliges the row to announce it.
- Integration tests were not run: no server, domain, persistence or contract file changed.

## 8. Scope

**Production files changed:**

- `src/AgencyOS.Client/ViewModels/PeopleSliceViewModels.cs`
- `src/AgencyOS.Client/ViewModels/ContractViewModels.cs`
- `src/AgencyOS.Client/ViewModels/DealViewModels.cs`
- `src/AgencyOS.Client/Presentation/RowProfiles.cs`
- `src/AgencyOS.Client/Presentation/DealActivityLine.cs` (new)
- `src/AgencyOS.Windows/Pages/PeoplePage.xaml`, `PeoplePage.xaml.cs`
- `src/AgencyOS.Windows/Pages/ContractsPage.xaml`, `ContractsPage.xaml.cs`
- `src/AgencyOS.Windows/Pages/DealsPage.xaml`
- `src/AgencyOS.Windows/Pages/PipelinePage.xaml`

**Test files:**

- `tests/AgencyOS.Tests.Unit/Client/C10BlindFailureTests.cs` (new)
- `tests/AgencyOS.Tests.Windows/Accessibility/OperationalListParityTests.Chronology.cs` (new)
- `tests/AgencyOS.Tests.Windows/Presentation/C10BlindFailureSurfaceTests.cs` (new)
- `tests/AgencyOS.Tests.Windows/Accessibility/OperationalListParityTests.cs` (7 accounting entries)

**What did not change:**

- No API endpoint, API contract, OpenAPI document, schema or migration.
- No domain rule, read query or permission.
- No change to `UnresolvedDifferenceCount` or to reconciliation semantics.
- No C11 or validation-infrastructure change.

**Explicit non-scope, untouched:**

- Project → Commercial not listing the contract (finding 4).
- The project's primary company visibility, "Disciplines: None recorded", and the discoverability of Joel Maddox's internal membership (finding 5 observations).
- Every other C10 observation.
- Every other history or empty-state surface (Projects, Talent, Documents, Finance and the rest). Their templates are unchanged.

## 9. Evidence limitations

- **The proof is local and in-process.** View models run against the unit-test fake API. Row templates are proved through the parity gate's XAML reading and client formatters, not by a rendered WinUI window or a live UI Automation tree.
- **The visible time format is not proved live.** The visible `OccurredAt` text is WinUI's default rendering of a `DateTimeOffset` under the operator's culture, the same as the Document and Finance history rows. The gate compares it with the announced instant as an instant. Its exact on-screen format was not observed live.
- **Server authorization is not re-proved here.** The refusal was simulated as the endpoint's 403. The server enforcement itself is unchanged and was not re-exercised in this repair.
- **The local toolchain is not the canonical one.** The .NET SDK and the other tools are this workstation's, not CI's.

## 10. Not yet proved

- **Authoritative CI** (GitHub Actions, PostgreSQL 18.6) has not run on the repair commit. It was not dispatched.
- **A fresh blind retest** of the affected journeys (People search, contract reconciliation, Activity chronology) has not run.
- No release candidate, build 97, C12, C14 or Freeze Exit work has been done.
