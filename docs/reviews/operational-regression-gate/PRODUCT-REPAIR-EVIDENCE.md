# Operational regression gate: product repair evidence

This is work in progress on `operational-regression-gate`, starting from `b340a9c`. It is
**not a release candidate**. CI closure, RC, C7, C10, C12, C14, Build 96, tags and releases
have not been started. Wired secondary channels and D4 overflow channels are **live proof
pending** until the release-candidate client run shows an operator finding and reading them.

The authority is the frozen population in `PRE-REPAIR-POPULATION.md`: 112 templates and 346
bindings, of which 328 are primary and 18 secondary. This repair changed no population, no
classification and no methodology beyond the adaptations recorded in section 5. Every figure
below comes from running the tests named.

## 1. Result

| | Frozen at `b340a9c` | Now |
| --- | --- | --- |
| PRIMARY values not covered | 110, on 70 templates | **0** |
| Date / Instant / Number / Flag / qualifier budget | 11 / 9 / 5 / 1 / 3 | **0 / 0 / 0 / 0 / 0** |
| T1 / T2 / T3 / T4 text | 25 / 52 / 2 / 2 | **0 / 0 / 0 / 0** |
| SECONDARY values unwired | 17 | **0** |
| SECONDARY values in total | 18 | 18, all **LIVE PROOF PENDING** |
| PRIMARY overflow channels (D4) | none | 2, both **PRIMARY OVERFLOW — LIVE PROOF PENDING** |
| Templates executed / excluded | 112 / 0 | 112 / 0 |

`EveryPrimaryValueIsAnnounced` and `EverySecondaryValueIsWiredToItsChannel` pass on all 112
templates. `BalanceList` is not profiled and still passes on the inferred phrase: its
template, its announcement and its coverage are unchanged (`TheBalanceRowIsUnchanged`).

## 2. The mechanism

- **Profiles.** `src/AgencyOS.Client/Presentation/RowProfiles.cs` declares 66 profiles, used by 70
  templates. Three dialogs share `Allocation`, both link lists share `Link` and both package
  element lists share `PackageElement`. A profile is the ordered list of the scan facts its
  template shows, each with a kind (text, token, money, party or suffixed) and, where the
  field's name is not the operator's word, a role word. The approved vocabulary lives here and
  nowhere else.
- **One entry point.** `RowLabel.For(row, profile)` speaks exactly the profile's fields and
  nothing else, and reuses the existing helpers:
  - `DisplayLabel` for tokens;
  - `RowLabel`'s own money text, where each amount keeps its own currency and there is no
    shared-currency compression;
  - `PartyLine` for people;
  - `IsoDate` for dates.

  An instant is spoken to the second with its offset (`yyyy-MM-dd HH:mm:ss zzz`). An absent
  value is not said.
- **Unchanged paths.** `RowLabel.For(row)` is unchanged, and so is every template that is not
  profiled. The generic `ValueParityTests` are unchanged.
- **Markup.** Each template passes a bare id: `{Binding Converter={StaticResource RowLabel},
  ConverterParameter=Receivable}`. `RowLabelConverter` passes the parameter through. No role
  words are in the XAML.
- **Budget.** A row that would exceed 160 characters shortens the one field its profile lets
  yield, and keeps every other field whole. If the rest leaves less than the existing
  12-character minimum, the yielding field is dropped rather than reduced to an ellipsis. This
  is the rule task and prediction rows already follow.
- **Secondary channels.** The 17 unwired secondary values are now bound directly:
  `AutomationProperties.HelpText="{Binding X}"`. No converter was invented. The 18th,
  `InteractionTemplate` `DetailedNotes`, was already a wired descendant.
- **D4 overflow.** `ContractTitle` on `FinancePage#ReceivableList` and on
  `FinancePage#CommissionList` is the yielding field. It is spoken as "Contract: " followed by
  a recognisable fragment, and the full title is bound to the same row's help text.

## 3. The Receivable STOP and its resolution

The previous resume stopped on arithmetic. On a realistic receivable row, every scan fact
except the contract title took 159 of the 160 characters. That left one character for the
title, which is not a recognisable fragment. Two things caused this:
- the money role written in full as "original amount";
- the beneficiary written as a bare "Client" that the role rule required to be labelled.

The owner resolved it:

| Field | Before | Now |
| --- | --- | --- |
| `OriginalAmount`, in the `Receivable` profile only | `240,000.00 GBP original amount` | `240,000.00 GBP original` |
| `Beneficiary` = `Client` | not announced | `Client money` |
| `Beneficiary` = `Agency` | not announced | `Agency money` |

The generic `RowLabel.For(row)` still says "original amount", and `ValueParityTests` is
untouched. Each amount keeps its own currency.

The realistic row is produced by the mechanism, not written into it. Its title is "Autumn
slate feature agreement with Northgate Pictures (three pictures)" and it reads, at 158
characters:

> Contract: Autumn slate…, Payer: Northgate Pictures, 240,000.00 GBP original, 90,000.00 GBP allocated, 150,000.00 GBP outstanding, Partially paid, Client money

Tests prove each requirement on it:
- `TheRealisticReceivableRowKeepsEveryScanFact`:
  - the row is at most 160 characters;
  - the "Contract" role is present with the "Autumn slate…" fragment;
  - the payer is complete;
  - all three amounts are complete, each with its currency and role;
  - the status is complete;
  - the beneficiary reads "Client money".
- `TheReceivableRowSaysWhoseMoneyItIs`: the "Agency money" case.
- `TheReceivableTitleIsWholeOnTheSameRow`: the full title is on the row's help text and is not
  in its name.
- `TheReceivableRowSaysNothingItHides`: the reference, the adjusted amount, the client name,
  the notes, the due date, the identifiers and the version are all silent.
- `SwappingAnyTwoReceivableAmountsFails` is the negative control. All three pairs of the three
  amounts are swapped, and each swapped row fails the role check.

The Commission row keeps the client, the three amounts with their currencies and roles, and
the status whole. It shortens only the contract title, whose full value is on the same row's
help text (`TheRealisticCommissionRowKeepsEveryScanFact`). A short title is said whole
(`AShortContractTitleIsSaidWhole`).

## 4. Approved vocabulary, as declared

| Area | Field | Spoken as |
| --- | --- | --- |
| Contracts reconcile | Negotiated / Contracted / Result | Agreed / In the draft / Result |
| Deals comparison | Previous / Current / Change | Previous / Current / Change |
| Commission, Commission rule | ClientDisplayName | Client |
| Receivable, Commission | ContractTitle | Contract (D4 overflow) |
| Receivable | OriginalAmount | original |
| Receivable | Beneficiary | Client money / Agency money |
| Commission rule | Basis | Basis |
| Post journal line | Account / Side / AmountDisplay | Account / Side / Amount |
| AI policy | ProviderKey / MaximumSensitivity | Provider / Maximum sensitivity |
| AI model | Key / ProviderKey | Model / Provider |
| Rights | RightType / Medium / Territory / PeriodKind | Right / Medium / Territory / Period |
| Attachments, document versions | FileName / MediaType / HoldsContent / ContentHash | File name / Media type / Holds content / Content hash |
| Thesis evidence | SignalTitle / Stance | Signal / Stance |
| Signal, source | Verification, Reliability, Sensitivity | as named |
| Member | Email, Note | as named |
| Talent | CareerStage / RepresentationStatus | Career stage / Representation |
| Palette | Category, Shortcut | as named |
| Search | Subtitle / MatchedOn | Context / Matched |
| Document and message links | Target + TargetLabel | "<kind>: <label>", as in "Deal: Autumn slate" |

In the accounting, each of these fields is recorded with its word via `PrimaryAs(...)`.
`EveryAccountedRoleIsTheWordItsProfileSays` requires the profile to say the same word. Other
roles are the field's own name as `DisplayLabel` writes it, for example "Due", "Signed",
"Last synced" and "Attempt count".

## 5. The comparator adaptations: all narrow, all authorised

1. **Help text evaluator.** A `HelpText` channel now accepts a direct `{Binding X}`, rendered
   the way the framework renders it, as well as a binding through a converter the gate can
   evaluate. Nothing else about the channel changed.
2. **Link composition (role source plus dependent value).** A classification may name
   `RoleFrom`, meaning another binding whose value is this one's role. The two bindings are
   covered together: the dependent value must share a segment with the source's value, as
   `DisplayLabel` writes it. Only the two link rows use it.

   | Control | Proves |
   | --- | --- |
   | `ALinkIsCoveredByItsKindAsTheLabelsRole` | "Deal: Autumn slate" covers both bindings |
   | `ALinkUnderTheWrongKindFails` | the same name under the wrong kind fails |
   | `ALinkWithTheWrongLabelFails` | the right kind with the wrong label fails |
   | `ALinkSaidBareFails` | bare "Deal, Autumn slate" fails |
   | `AnOrdinaryRowStillUsesFixedRoles` | an ordinary two-text row still takes fixed roles from its own fields |
   | `BothLinkListsUseOneComposition` | both link lists name the one `Link` profile and announce identically |

3. **Role words.** `EveryAccountedRoleWordIsVisibleOnItsPage` accepts one of two sources: a
   visible label on the page, or the word the row's own profile declares for that field.

The kinds, the date and instant rules, the money rule, the party rule and the role-identity
rule are unchanged. `ASearchHitsContextAndMatchCannotAnswerForEachOther` is the Search
regression: with Subtitle and MatchedOn swapped, each fails, and with both said bare, each
fails.

## 6. Profile ratchets

| Test | Holds |
| --- | --- |
| `EveryProfileATemplateNamesExists` | every referenced profile exists |
| `EveryProfileIsUsed` | no stale profile |
| `ATemplateNamesItsProfileAndNothingElse` | the parameter is a bare id; roles stay central |
| `EveryProfileFieldIsAScanFactItsTemplateShows` | each field is a primary-visible binding of every template that uses it, rendered through the matching converter; a role source is shown too |
| `EveryScanFactOfAProfiledRowIsInItsProfile` | no shown scan fact is left out of a profile |
| `EveryAccountedRoleIsTheWordItsProfileSays` | the accounting and the profile agree on every role |
| `AProfileLetsOneFieldYieldAndOnlyTextOverflows` | at most one yielding field; overflow is text that has a role |
| `EveryOverflowIsOfferedWholeOnTheSameRow` | both overflow fields are bound whole to the same row's help text |
| `AProfiledRowSaysNothingItsTemplateHides` | no value of a field the profile does not read is announced, unless the row shows it inside a shown value |

**Anti-overannouncement.** The inferred phrase used to announce fields these rows do not show.
It no longer does:

| Row | Previously announced, though hidden |
| --- | --- |
| `ReceivableList` | the reference (as its headline) and the adjusted amount |
| `CommissionList` | the basis and adjusted amounts |
| `CommissionRuleList` | the fixed amount |
| `MoneyObligationList` | the payer and two amounts |
| `NoticeRequirementList` | the obligor |
| `FinancePage#HistoryList` | an amount |
| `PipelinePage#HistoryList` | the actor and the kind |
| `MailboxList` | a hidden headline |
| `IntelligencePage#SourceList` | a hidden context field |
| `PersonRowTemplate`, `RelationshipRowTemplate`, the package element lists and `SubjectList` | hidden qualifiers |

Identifiers and `Version` stay silent.

`AProfiledRowSaysNothingItsTemplateHides` exempts a value the row shows inside a shown field.
Its first run flagged two such values, and neither is a leak:
- the ledger `Currency` that every amount on `JournalList` carries;
- the version numbers inside the visible conflict sentence on `QueueList`.

## 7. Negative control after the repair

With the repair green, one local, uncommitted mutation was made in
`src/AgencyOS.Client/Presentation/RowProfiles.cs`: the `Reconciliation` profile's roles were
swapped, so the agreed value was said as "In the draft" and the draft value as "Agreed".

The parity suite then failed 2 of 402, both for the intended reason:
- `EveryPrimaryValueIsAnnounced(ContractsPage.xaml, ReconcileList)`: announced `zqac, In the
  draft: zqap, Agreed: zqbd, Result: zqad` and did not cover `Negotiated.DisplayValue` or
  `Contracted.DisplayValue`.
- `EveryAccountedRoleIsTheWordItsProfileSays`: `Negotiated.DisplayValue` is 'Agreed', its
  profile says 'In the draft', and the reverse for `Contracted.DisplayValue`.

The file was then restored from a copy taken before the mutation. The restored file is
byte-identical to that copy (`cmp`; SHA-256
`015a1624894865baf08a9f0f1fb8b2009912c679eaa6d42ca0136f452dbd0d5c`). The rerun was 402 of
402 green, and no mutation remains in the tree.

## 8. Gates

| Gate | Result |
| --- | --- |
| `OperationalListParityTests`, focused | 402 passed, 0 failed |
| `build` | succeeded, 0 warnings, 0 errors |
| `test-unit` | 4088 passed, 0 failed |
| `test-windows` | 1786 passed, 0 failed |
| `test-reviewer` | 163 passed, 0 failed |

All gates were run through `pwsh -NoProfile -File scripts/Invoke-AgencyOS.ps1`. CI was not
dispatched, as CI closure is out of scope for this step.

## 9. Scope

- **Changed:**
  - client presentation (`RowLabel`, `RowProfiles`, and a remark in `IsoDate`);
  - the Windows row converter;
  - 81 row templates' accessible name and help text attributes;
  - the gate's tests;
  - this document.
- **Not changed:** the API, domain, contracts, schema, migrations, persistence, OpenAPI,
  workflows and Reviewer infrastructure. The API contract stays at 17, and the latest
  migration stays `20260909072201_AiResultClassification`.
- **What remains:**
  - live proof for the 18 secondary channels and the 2 overflow channels;
  - CI closure and everything after it, none of which was started.
