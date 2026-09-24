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
  12-character minimum, a yielding headline is dropped rather than reduced to an ellipsis.
  This is the rule task and prediction rows already follow. At `4d5b9b2` this drop also
  applied to D4 overflow fields. Section 10 records the D5 correction that stops it.
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

The chain of decisions, preserved in order:

1. The first bounded repair stopped because realistic Finance rows could not fit 160
   characters. Control Room adjudicated that STOP, and the owner approved **D4**: a long
   textual PRIMARY value keeps its role and a scan fragment in the name, with the whole value
   on a channel of the same row.
2. The D4 resume stopped on vocabulary (Search `Subtitle`, the link rows). Control Room
   adjudicated it.
3. The next resume stopped on this Receivable arithmetic. Control Room then approved the
   bounded operator vocabulary below.
4. `4d5b9b2` implemented it. Control Room's review of `4d5b9b2` then exposed an
   invariant-level drop path in D4. The owner approved **D5**, and section 10 records it.

The vocabulary Control Room approved:

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

Section 10 narrows this exemption.

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

## 10. D5 correction: truth before the budget

### The finding

Control Room reviewed the repair snapshot `4d5b9b2`. It found that `RowLabel.Profiled` could
still erase a PRIMARY value. The trigger was the other PRIMARY facts leaving fewer than
`MinimumHeadline` (12) characters for the yielding field. `Profiled` then returned only the
rest, and a D4 overflow field vanished from the name entirely.

### The reproduction, before any fix

Three controls were added first and run against the unchanged `4d5b9b2` product code. All
three failed on the missing "Contract" segment:

- **`ALongerPayerDoesNotEraseTheContract`**:
  - The payer is "Northgate Pictures LLC", four characters longer than the approved
    fixture. That leaves 11 characters for the title, one under the minimum.
  - Every compact fact was present and complete.
  - No "Contract" segment was present at all: `Payer: Northgate Pictures LLC, 240,000.00 GBP
    original, 90,000.00 GBP allocated, 150,000.00 GBP outstanding, Partially paid, Client
    money`.
- **`ALongClientDoesNotEraseTheCommissionContract`**:
  - The client is "Anastasia Reyes-Okafor de la Fuente Montgomery".
  - The client, the three amounts and the status were complete.
  - The "Contract" segment was absent.
- **`ALongLegalPayerKeepsEveryFactAndTheContract`**:
  - The payer is an 82-character legal name.
  - The contract was dropped.
  - The rest was then cut to 160: `…150,000.00 GBP…`, with the status and beneficiary gone.

### Owner decision D5

160 characters remains the normal scan target. PRIMARY truth outranks it where the two cannot
coexist:
- compact PRIMARY facts stay complete;
- a long textual PRIMARY keeps its role and a recognisable fragment;
- its whole value stays on its D4 channel;
- the name may exceed 160 only by the minimum needed.

D5 introduces no fixed larger cap, no Finance-specific cap and no reclassification.

### The correction

It is one branch in `RowLabel.Profiled`, and it applies only to a field whose profile marks it
`Overflow`:

```csharp
if (budget < MinimumHeadline)
{
    if (!yields.Field.Overflow)
    {
        return Shorten(rest);          // unchanged: a yielding headline, as task rows
    }

    budget = MinimumHeadline;          // D5: keep the minimum fragment, drop nothing
}
```

What changes and what does not:
- Where the budget holds, nothing changes.
- Where it does not, the overflow field keeps its role prefix and `Shorten(value, 12)`, the
  product's existing minimum fragment. Every other part is joined whole, and nothing is cut
  from the rest.
- The name exceeds 160 by exactly the fragment the normal budget could not hold. There is no
  other cap.
- Ordinary yielding headlines, generic `RowLabel.For(row)`, and task and prediction rows are
  untouched. The two overflow fields are the Receivable and Commission `ContractTitle`, the
  only fields marked `Overflow`.

### Boundary results

All strings below are exact outputs under en-GB formatting, asserted by the tests named.

| Case | Test | Announced name | Length |
| --- | --- | --- | --- |
| A. Approved receivable | `TheRealisticReceivableRowKeepsEveryScanFact` | `Contract: Autumn slate…, Payer: Northgate Pictures, 240,000.00 GBP original, 90,000.00 GBP allocated, 150,000.00 GBP outstanding, Partially paid, Client money` | **158**, unchanged |
| B. "Northgate Pictures LLC" | `ALongerPayerDoesNotEraseTheContract` | `Contract: Autumn…, Payer: Northgate Pictures LLC, 240,000.00 GBP original, 90,000.00 GBP allocated, 150,000.00 GBP outstanding, Partially paid, Client money` | **156**, within 160 |
| C. Long legal payer (82 characters), agency money | `ALongLegalPayerKeepsEveryFactAndTheContract` | `Contract: Autumn…, Payer: Northgate Pictures International Film Distribution and Production Holdings Limited, 240,000.00 GBP original, 90,000.00 GBP allocated, 150,000.00 GBP outstanding, Partially paid, Agency money` | **216** |
| Commission, long client | `ALongClientDoesNotEraseTheCommissionContract` | `Client: Anastasia Reyes-Okafor de la Fuente Montgomery, Contract: Autumn…, 24,000.00 GBP entitled, 9,000.00 GBP collected, 15,000.00 GBP outstanding, Partially collected` | **169** |
| D. Short title | `AShortContractTitleIsSaidWhole` | `Contract: Autumn slate`, said whole | n/a |
| E. Fits, title "Autumn slate" | `AReceivableThatFitsDoesNotUseTheSafetyValve` | whole, no ellipsis | **157** |

How the lengths come about:
- **B** falls back to the 12-character minimum, and the result still fits within 160. The
  word-boundary rule yields "Autumn…", which is 7 characters.
- **C** needs 198 characters for its compact facts alone. It exceeds 160 only because those
  facts are complete, plus 19 characters for ", Contract: Autumn…".
- **Commission** needs 150 characters for its compact facts, which leaves 10 for a segment that
  needs 19. The excess is exactly 9.

`AssertExcessIsOnlyWhatTruthForces` checks every name past 160. It holds three things:
- the fragment is at most the 12-character minimum;
- the compact facts left no room for that minimum within 160;
- the length is exactly the compact facts plus the contract segment.

There is no fixed larger cap in the code or the tests.

Every PRIMARY value survives, each checked by role:
- the "Contract" role with a recognisable prefix of the title of at least one whole word;
- the complete payer or client;
- all three amounts, each with its currency and role;
- the status;
- the beneficiary.

In C, the name has exactly its seven fields. The hidden reference, adjusted amount and client
name are absent. The call is deterministic, and the full title is on the same row's help text.

Both overflow channels remain **PRIMARY OVERFLOW — LIVE PROOF PENDING**, and all 18 secondary
channels remain **LIVE PROOF PENDING**. The PRIMARY and SECONDARY results are unchanged: 0
uncovered and 0 unwired. The frozen 112 / 346 / 328 / 18 population and the 110-on-70
pre-repair count are unchanged.

### The hidden-value exemption, narrowed

The exemption no longer excuses a hidden value merely because the same token appears
somewhere on screen. `SaysHidden` lets each shown value's rendering account for one occurrence,
and only where the announcement says that rendering itself. Anything left over is a leak.
`TheShownValueExceptionIsNarrow` holds it with seven controls:

| Control | Result |
| --- | --- |
| Currency inside a spoken amount | allowed |
| The same currency said a second time on its own | leak |
| Version numbers inside the spoken conflict sentence | allowed |
| A version said as its own segment | leak |
| A hidden value equal to a shown one, said twice | leak |
| Said once, as that shown value | allowed |
| A token absent from every shown rendering | leak |

The two observed cases, the ledger currency and the sync versions, still pass under the
narrowed rule, and nothing else needed exempting.

### Documentation corrections

- The account in section 3 of who decided what is corrected. The earlier STOPs are kept.
- The remarks in `IsoDate` now say that it writes a calendar date only. Given an instant, it
  writes that instant's day in the instant's own offset, with no time and no offset. A
  profiled row's raw instants are announced by `RowLabel`'s own formatting, not by `IsoDate`.
  Visible dates are unchanged.

### Negative control

With the gate green, one local, uncommitted mutation was made: the overflow condition in
`RowLabel.Profiled` was forced to take the old drop path. The focused suite then failed 3 of
413. They are exactly the three D5 controls, and each diagnostic lists the complete compact
facts with no "Contract" segment.

The file was restored from the copy taken before the mutation. The copy is byte-identical
(SHA-256 `932532391d043e3dff2da709f3283a095c5e3e9f7db19e76acb1314b2408711c`). The rerun was
413 of 413, and `git status` shows only the intended changes.

### Local gates

| Gate | Result |
| --- | --- |
| Focused `OperationalListParityTests` | 413 passed, 0 failed |
| `build` | 0 warnings, 0 errors |
| `test-unit` | 4088 passed, including `ValueParityTests` and the source-reading rule |
| `test-windows` | 1797 passed |
| `test-reviewer` | 163 passed |

These are local evidence only. C9 is not proved, and authoritative CI and the PostgreSQL 18.6
evidence come next. There was no API, domain, contract, schema, migration, persistence, OpenAPI,
workflow or Reviewer-infrastructure change. The contract stays at 17, the schema stays at
`20260909072201_AiResultClassification`, and there is no Build 96.

## 11. D5 yielding-PRIMARY siblings

### Control Room review of `4245c7a`

Control Room accepted the D5 correction for the two explicit `Overflow` Finance fields as
candidate source work. It then took up the residual observation recorded at the end of that
correction: an ordinary yielding PRIMARY could still take the old path, which dropped the
field and cut the rest to 160. It proved one concrete canonical case, the payment row.

D5 already governs this; it is not a new owner decision and not a census. Section 10 said
ordinary yielding headlines and task and prediction rows were untouched. That statement held
at `4245c7a` and is superseded here.

### The Payment finding

- The `Payment` profile shows and speaks, in this order:
  - `PayerDisplayName`;
  - a yielding `ExternalReference`, spoken as "External reference: …";
  - `Amount`, `Allocated` and `Unapplied`;
  - `Status`;
  - `ReceivedOn`.
- **Canonical bounds.** Control Room gives 300 for the payer name and 200 for the external
  reference. In the persistence mappings, `payer_name` is 300 and the `external_reference`
  columns are 500.

**Pre-fix, reproduced against the unchanged `4245c7a` product code.** Every case failed on a
missing PRIMARY value, not on length:

| Case | Announced at `4245c7a` | Lost |
| --- | --- | --- |
| `ALongLegalPayerDoesNotEraseThePaymentReference`: 82-character legal payer, reference "BACS-2026-0917-NORTHGATE" | `Payer: Northgate Pictures International Film Distribution and Production Holdings Limited, 240,000.00 GBP, 90,000.00 GBP allocated, 150,000.00 GBP unapplied,…` | the reference; the status and the received date were cut |
| `TheRealisticPaymentKeepsItsReference`: the ordinary payer "Northgate Pictures", the same reference | `Payer: Northgate Pictures, 240,000.00 GBP, 90,000.00 GBP allocated, 150,000.00 GBP unapplied, Partially allocated, Received: 2026-09-01` (135) | the reference: the other facts left 3 characters for it behind its role |

The second row shows the defect was not confined to long names. At `4245c7a`, an ordinary
"Partially allocated" payment dropped its bank reference.

### The finite mechanism scope inspected

Only the existing ways `RowLabel` lets a PRIMARY value yield were inspected:

- **A.** The yielding field of each of the 66 `RowProfiles` profiles: exactly one per profile,
  which is now a ratchet (`EveryProfileLetsExactlyOneFieldYield`). There are 64 ordinary ones
  and 2 `Overflow` ones.
- **B.** `WithEssentials`, in its two families: the task title and the prediction statement.

No XAML was recatalogued and no binding reclassified.

### Which instances are affected

**Proved affected by a legal-input reproduction.** Each case failed on `4245c7a`:

| Instance | Reproduction and legal bounds | Before, at `4245c7a` | After |
| --- | --- | --- | --- |
| `Payment.ExternalReference` | above | reference lost; status and date cut | see below |
| Task title, generic `WithEssentials`: `LegalLongNamesKeepTheTitleAndEveryAnswer` | subject is a company `name` (256), here 82 characters; assignee is a user `display_name` (256), here 46 | the answers took 164 characters. Observed: the name began at "About …", so the title and priority were dropped. By the old rule's arithmetic, the due date was then cut to "Due…". | `Send…, Open, High, About Northgate Pictures International Film Distribution and Production Holdings Limited, Assigned to Anastasia Reyes-Okafor de la Fuente Montgomery, Due 2026-10-09` (**183**) |
| Task priority, same mechanism: `ALongTitleDoesNotCostThePriority` | the task title (512), here 149 characters | the qualifiers after the title yielded with it, so the visible **Priority** was cut from a long-titled task | `chase the completion bond paperwork through legal chase the…, Open, High, About Marisol Thorneycroft-Bassey, Assigned to Review member, Due 2026-10-09` (**150**) |
| Prediction statement: `LegalLongNamesKeepTheStatement` | owner and forecaster are user `display_name` (256), each here 46 | the forecast facts took 147 characters and the statement was dropped | `Vesper…, Owner: Anastasia Reyes-Okafor de la Fuente Montgomery, Open, Forecast 80% by Anastasia Reyes-Okafor de la Fuente Montgomery, Resolves by 2026-12-31` (**156**) |

**Proved unreachable by construction: 15 profiles, 16 templates.** Their other facts are only
domain tokens, dates, instants and numbers:
- `TimelineEntry`, `Relationship`, `Link` (both link lists), `AgentRun`, `AgentStep`;
- `ContractVersion`, `ContractOption`, `MoneyObligation`, `NoticeRequirement`, `Notice`,
  `Offer`;
- `Project`, `RepresentationScope`, `TalentHistory`, `Pitch`.

To reach the old path, the other facts must take more than 146 characters, less the yielding
field's role. A date is spoken in at most 10 characters plus its role, an instant in 25 plus
its role, and a number and each domain token in a few words. No combination on these rows
approaches 146; the largest, `ContractOption`, is about 76. For these rows the old branch
cannot run, so the new rule changes nothing they say.

**Reachable in principle: the other 49 profiles.** Each has free text among its other facts
(names, addresses, subjects, titles, notes) whose canonical bounds run from 128 to 512
characters. Only `Payment` was individually reproduced.

**One rule, no per-profile exceptions.** A single budget rule covers all of them, and its
output is identical wherever the budget holds. `NoProfiledRowDropsAScanFactToKeepItsBudget`
drives every one of the 70 profiled templates past the budget with long values. It proves that
every other scan fact keeps its semantic coverage and the yielding value stays recognisable.

### The implementation

A single helper is now the one budget rule for every yielding value:

```csharp
private static string Fragment(string value, int taken) =>
    Shorten(value, Math.Max(MaximumLength - taken, MinimumHeadline));
```

- **`Profiled`.** The drop-and-shorten branch is gone, for `Overflow` fields and ordinary ones
  alike. The yielding field keeps its role prefix and at least the 12-character minimum
  fragment, and every other part is joined whole.
- **`WithEssentials`.**
  - A row whose whole phrase fits is returned unchanged.
  - Otherwise **only the title** yields. What follows it (the priority and status a task row
    shows) and the essential answers stay whole, and the title keeps at least its minimum
    fragment. It used to be dropped when the answers filled the budget, which cut the answers
    as well.
- **The 160 target.** 160 remains the target, and every row that fits is identical to
  `4245c7a`. A name exceeds 160 only by the fragment the other, complete facts left no room
  for. There is no fixed or per-page cap.
- **Role words.** The yielding value keeps its role where it has one, as in "External
  reference:". A headline that has no role gets none added.

One existing test encoded the prohibited behaviour and was rewritten to D5:
`RolesTooLongForTheBudgetStillProduceAWellFormedRow` required a task row to stay at or under
160 even with 200-character names, which it could only do by dropping the title and cutting
the answers. It keeps its well-formedness checks, with no leading comma and no empty segment.
It now also requires:
- the subject, assignee, due date and priority whole;
- a recognisable title fragment;
- when the row exceeds 160, that the title is at most the minimum fragment and the answers
  alone left no room for it.

### Post-fix Payment

| Case | Announced name | Length |
| --- | --- | --- |
| Long legal payer | `Payer: Northgate Pictures International Film Distribution and Production Holdings Limited, External reference: BACS-2026-0…, 240,000.00 GBP, 90,000.00 GBP allocated, 150,000.00 GBP unapplied, Partially allocated, Received: 2026-09-01` | **233** |
| Realistic payment | the ordinary payer "Northgate Pictures" with `External reference: BACS-2026-0…` and every other fact whole | **169** |
| Fits (`APaymentThatFitsIsSaidWhole`) | reference "BACS-0917", status "Received": everything whole, no ellipsis | **155** |

How the lengths come about:
- **Long legal payer.** The other facts alone take 199 characters, so the name is 199 + 2 +
  "External reference: " (20) + the 12-character minimum fragment = 233.
- **Realistic payment.** The other facts take 135, leaving 3 characters where the minimum
  fragment needs 12. The excess is exactly 9.

In each case the payer, the amount, allocated and unapplied, each with its currency and role,
the status and the received date are complete. No hidden payment field is announced: the
payee, the method, the source system, who recorded it, the notes, the direction and the
recorded time are all absent. The Receivable and Commission D5 controls are unchanged at
158, 156, 216 and 169.

### Full-value channels

- **The two Finance overflow fields.** `ContractTitle` on the Receivable and Commission rows
  keeps its full-value `HelpText` binding. Both remain **PRIMARY OVERFLOW — LIVE PROOF PENDING**.
- **The 64 ordinary yielding profile fields.** The candidate channel for each is the row's own
  visible element bound exactly to that field (`Text="{Binding <field>}"`), in the same row
  and not hidden from accessibility in markup; no template uses `AccessibilityView`. That is
  structural evidence only: **PRIMARY FULL-VALUE DESCENDANT — LIVE PROOF PENDING**.
- **The task titles and prediction statements.** Their channel is the visible `Title` or
  `Statement` element in the same row, with the same status: **PRIMARY FULL-VALUE DESCENDANT —
  LIVE PROOF PENDING**.

`EveryYieldingValueIsWholeOnItsRow` holds all of these. Some of those elements trim visually
(`TextTrimming`) while binding the whole value. Whether a screen reader reaches the whole text
is for the release-candidate Windows/UIA run to show.

No XAML changed, no secondary help text was overwritten, and all 18 secondary channels remain
**LIVE PROOF PENDING**. A visible descendant does not replace the PRIMARY name requirement:
every PRIMARY is still in the name, whole where it fits and as a recognisable fragment where
it yields.

### Unchanged results

PRIMARY uncovered is 0 and SECONDARY unwired is 0. The frozen population is unchanged: 112
templates and 346 bindings (328 primary, 18 secondary), with the pre-repair count of 110
values on 70 templates.

The hidden-value rule is unchanged from `4245c7a`, and its seven collision controls pass.

### Negative control

With the gate green, one local, uncommitted mutation was made in `RowLabel.cs`. It restored
the old path in both mechanisms: in `Profiled`, an ordinary yielding field below the minimum
was dropped and the rest cut; in `WithEssentials`, the title was dropped and the rest cut.

The failures were exactly the intended ones:

- **Windows: 3 of 419 failed.**
  - `ALongLegalPayerDoesNotEraseThePaymentReference`: no reference; the rest cut to
    `…150,000.00 GBP unapplied,…`.
  - `TheRealisticPaymentKeepsItsReference`: no "External reference" segment.
  - `NoProfiledRowDropsAScanFactToKeepItsBudget`: it named each profiled row that lost a scan
    fact or its yielding value.
- **Unit: 5 of 662 client tests failed.**
  - `LegalLongNamesKeepTheTitleAndEveryAnswer`: the title missing.
  - `LegalLongNamesKeepTheStatement`: the statement missing.
  - `RolesTooLongForTheBudgetStillProduceAWellFormedRow`, all three over-budget cases.

`ALongTitleDoesNotCostThePriority` stayed green under this mutation, because the mutation does
not restore the old joint yielding of the title and its qualifiers. It failed on the unchanged
`4245c7a` code, as the reproduction above shows.

`RowLabel.cs` was restored from the copy taken before the mutation. It is byte-identical
(SHA-256 `95851b24c1d8be2da23c6b4a88db2109619a71ea79eb5e46aff58cdfc0f23bb6`), and the gates
below ran on the restored tree.

Two throwaway test files were used to print the classification and the exact names above.
Both were deleted before any gate ran and are not committed.

### Local gates

| Gate | Result |
| --- | --- |
| Focused `OperationalListParityTests` | 419 passed |
| `build` | 0 warnings, 0 errors |
| `test-unit` | 4091 passed, including `ValueParityTests`, the task and forecast row tests, and the source-reading rule |
| `test-windows` | 1803 passed |
| `test-reviewer` | 163 passed |

These are local evidence only. C3 and C9 are not claimed. There was no API, domain, contract,
schema, migration, persistence, OpenAPI, workflow or Reviewer-infrastructure change. The
contract stays at 17, the schema stays at `20260909072201_AiResultClassification`, and there
is no Build 96.
