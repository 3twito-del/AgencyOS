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
- **Canonical bounds.** The business input limits are set by the domain write path in
  `Payment`: 300 for the payer name (`Ensure.OptionalMax(payerName, …, 300)`) and 200 for
  the external reference (`Ensure.OptionalMax(externalReference, …, 200)`). The wider
  `external_reference` storage columns (500) are capacity only. They do not replace the
  domain invariant and are not the business limit.

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

## 12. D5 absent-yielding-value closure

### Control Room review of `ae9017e`

Control Room accepted the following from `ae9017e` as candidate work:
- the unified `Fragment` rule;
- the task and prediction D5 behaviour;
- the rewrite of `RolesTooLongForTheBudgetStillProduceAWellFormedRow`, which is not a
  weakening. It still proves a well-formed name, the subject, assignee, priority and due date
  whole, a recognisable title, and excess over 160 only where required. It is unchanged here.

It found one remaining sibling in the same mechanism. This is not a census and not a new
decision: D5 governs it. Section 11 also presented the payment bounds as storage widths; that
is corrected in place. The business limits are the domain's: 300 for the payer name and 200
for the reference.

### The path

`RowLabel.Profiled` speaks only the fields that hold a value: `Say` returns null for an absent
one, and `OfType<Spoken>()` drops it. When the row's yielding field is absent, nothing among
the spoken parts is marked `Yields`. So `parts.FindIndex(x => x.Field.Yields)` returned -1,
and the old guard `whole.Length <= MaximumLength || yielding < 0` sent an over-budget row to
`Shorten(whole)`. That cut the last of the remaining facts, every one of which is a scan fact
that may not yield.

### Reproduction, before any fix, against the unchanged `ae9017e` product code

`AnAbsentReferenceCostsNoOtherPaymentFact` has the 82-character legal payer, no external
reference (`PaymentResponse.ExternalReference` is nullable), the usual amounts, "Partially
allocated" and 2026-09-01. At `ae9017e` it announced:

> `Payer: Northgate Pictures International Film Distribution and Production Holdings Limited, 240,000.00 GBP, 90,000.00 GBP allocated, 150,000.00 GBP unapplied,…`

The status and the received date were lost to the 160 cut.

The mechanism guard `AnAbsentYieldingValueCutsNoOtherScanFact` failed on the same code for 5
templates:

| Template | Absent yielding field |
| --- | --- |
| `FinancePage#PaymentList` | `ExternalReference` |
| `CommunicationsPage#MessageList` | `Subject` |
| `ContractsPage#MoneyObligationList` | `Description` |
| `ContractsPage#NoticeList` | `Summary` |
| `DealsPage#OfferList` | `Summary` |

How far those failures reach:
- Payment is the canonical legal case.
- For the three contract, deal and notice rows, the guard reaches the budget only by
  lengthening their domain tokens, so it proves the mechanism, not a legal reproduction.
- The guard blanks only yielding fields declared nullable, so it builds no invalid domain
  state. It lengthens the other text fields synthetically; it is not a census of optional
  fields.

### The fix

This is the only change to `RowLabel.Profiled`:

```csharp
if (whole.Length <= MaximumLength)
{
    return Shorten(whole);   // A: fits - unchanged
}

if (yielding < 0)
{
    return whole.Trim();     // C: nothing may yield - every remaining fact whole (D5)
}

// B: the yielding value is present - the unchanged ae9017e Fragment rule
```

The D5 basis: with the yielding value absent, every remaining value is a non-yielding PRIMARY,
and none may be cut to meet the 160 target. So the name keeps them all. That is the direct
application of D5, not a new exception, and there is no fixed cap. Nothing is said for the
absent value: no placeholder and no reference is invented. No channel is added for a value
that does not exist.

### Results

| Case | Announced name | Length |
| --- | --- | --- |
| A. Absent reference, over budget (`AnAbsentReferenceCostsNoOtherPaymentFact`) | `Payer: Northgate Pictures International Film Distribution and Production Holdings Limited, 240,000.00 GBP, 90,000.00 GBP allocated, 150,000.00 GBP unapplied, Partially allocated, Received: 2026-09-01` | **199** |
| B. Absent reference, fits (`AnAbsentReferenceThatFitsIsSaidWhole`) | `Payer: Northgate Pictures, 240,000.00 GBP, 90,000.00 GBP allocated, 150,000.00 GBP unapplied, Partially allocated, Received: 2026-09-01` | **135**, within 160 |

In case A:
- There are exactly six segments, all whole, with no ellipsis.
- No "External reference" segment is invented.
- No hidden payment field is announced.
- The name runs past 160 only because those six facts are complete.

`AnAbsentYieldingValueCutsNoOtherScanFact` now passes for every profiled template whose
yielding field is nullable.

What is unchanged:
- The Payment names with a reference present: 155, 169 and 233.
- The Receivable and Commission D4/D5 controls: 158, 156, 216 and 169.
- The task and prediction D5 controls.

The live-proof accounting is unchanged:
- the two Finance overflow channels remain **PRIMARY OVERFLOW — LIVE PROOF PENDING**;
- the ordinary yielding values remain **PRIMARY FULL-VALUE DESCENDANT — LIVE PROOF PENDING**
  where they are truncated;
- all 18 secondary channels remain **LIVE PROOF PENDING**.

PRIMARY uncovered is 0 and SECONDARY unwired is 0. The frozen population is unchanged, and
the hidden-value collision controls pass.

### Negative control

With the gate green, one local, uncommitted mutation was made: case C was restored to
`return Shorten(whole);`, the old behaviour for `yielding < 0`. The focused suite then failed
2 of 422:
- `AnAbsentReferenceCostsNoOtherPaymentFact`: the row cut to `…150,000.00 GBP unapplied,…`,
  losing the status and the received date.
- `AnAbsentYieldingValueCutsNoOtherScanFact`: it named the rows cut with their yielding value
  absent.

`RowLabel.cs` was restored from the copy taken before the mutation. It is byte-identical
(SHA-256 `142616f55317fc624439b36f5081913d691143c9444e7df4df241252ffdecc25`), and the gates
below ran on the restored tree.

### Local gates

| Gate | Result |
| --- | --- |
| Focused `OperationalListParityTests` | 422 passed |
| `build` | 0 warnings, 0 errors |
| `test-unit` | 4091 passed, including the task and forecast tests, `ValueParityTests` and the source-reading rule |
| `test-windows` | 1806 passed |
| `test-reviewer` | 163 passed |

These are local evidence only. C3 and C9 are not claimed. No XAML and no `RowProfiles` changed.
There was no API, domain, contract, schema, migration, persistence, OpenAPI, workflow or
Reviewer-infrastructure change. The contract stays at 17, the schema stays at
`20260909072201_AiResultClassification`, and there is no Build 96.

## 13. The failed final-candidate RC, and owner decision C

### What stood before the RC

Authoritative CI run `36049271786` (#103, `workflow_dispatch`) passed on `f29a2ee`. Its
totals were:
- Unit 4091, Windows 1806, Reviewer 163;
- Integration 949 passed, 0 failed and 0 skipped, against the `postgres:18.6` service with
  `pg_dump` and `pg_restore` at 18.6;
- contract 17, schema `20260909072201_AiResultClassification`.

That run remains the authoritative CI result for `f29a2ee`.

### The sealed real-client RC on `f29a2ee`

- **Candidate:** `release -Channel alpha -BuildId 103` from exactly `f29a2ee`. The manifest
  was verified: commit `f29a2ee…`, contract 17, schema as above, 111 artifacts with every
  hash checked. `AgencyOS.Windows.exe` SHA-256
  `01d640b536064ca84eef5dce82f67cd3ae633546e1341e578f5837a469278f5d`.
- **Backend:** a private PostgreSQL cluster on 127.0.0.1:5434. `show data_directory`
  returned the run's own `pgdata`. It was PostgreSQL 19 beta 3, which is operator evidence
  only: CI run 103 remains the 18.6 authority.
- **API:** from source at `f29a2ee` (contract 17, Development authentication).
- **Tenant:** a synthetic tenant, `01a0d511-2ba2-7c75-9032-ff6a7858f3bd`.
- **Evidence:** all of it is under `artifacts/reviewer/rc-f29a2ee-20260924/`, which is not
  committed.

**C3, original Talent scope probe: passed live.** The visible row shows "Acting" and
"2026-09-24". The keyboard reaches it by Tab → Change status → Change scopes → the row. The
focused row's accessible name is "Acting, Starts: 2026-09-24". No identifier or version is
announced.

**C7 step 1, REPRESENT: passed live.** The route was Prospects → O'Brien D'Angelo-Smith →
"Convert to client…" → Film → Sign. The product said "Signed. O'Brien D'Angelo-Smith is now a
client, represented for Film."

**C7 step 2, PURSUE/NEGOTIATE: stopped.**
- The Talent page after signing listed only A, Rivka, Zoë Ångström and 千尋, with
  "1 client(s)". O'Brien was absent.
- New opportunity → Kind "Talent engagement" → Subject offered the same four people only.
- The signed client could not enter Talent or a talent pursuit, so the same story could not
  continue. The RC stopped under its failure rule, with no repair and no fixture writes after
  the handoff. The stop stands as historical evidence.

### Source proof of the cause

- Prospect conversion creates and activates a `Representation`, and nothing else.
- `ListTalentAsync` is rooted in `TalentProfiles`.
- The talent-engagement subject picker (`CreateOpportunityDialog`) is built from that
  roster, through `EntityChoice.ForTalent(ListTalentAsync())`.
- The Windows client had no route that creates a talent profile.

### Owner decision C

`Representation` and `TalentProfile` remain separate concepts:
- "Convert to client" does not create a talent profile.
- Creating one is a separate, explicit operator action.
- Client-ness, the Talent query and the meaning of a Talent engagement are unchanged.

### The repair

**The capability already existed in contract 17.** It is
`POST /api/v1/organizations/{organizationId}/talent`: `CreateTalentProfileRequest`, handled
by `CreateTalentProfileHandler`, and authorized by `Permission.TalentWrite`.
- A second profile for the same person is refused with 409.
- `GET …/talent/{personId}` answers 404 for a person without a profile.
- The client wrapper, `IAgencyOsApi.CreateTalentProfileAsync`, already existed.

No endpoint, DTO, OpenAPI path, contract version, schema or migration changed.

**Client and Windows only:**

- **`ProspectsViewModel`.** It remembers the client just signed, or a converted pursuit the
  operator selects. It reads whether that person has a talent profile from the talent read.
  - A 404 is said as "absent".
  - A refusal or failure is said as "unknown", never as missing.
  - It offers `CreateTalentProfileAsync(careerStage)` until a profile is known to exist.
  - A 403 refusal says "You do not have permission to create talent profiles. … is still a
    client." It creates nothing and does not borrow the list's error state.
  - A 409 conflict is said as "already has a talent profile, so none was created".
- **Prospects page.** An InfoBar sits directly beneath the "Signed. … is now a client" line.
  Before any attempt it reads "Next: create a talent profile — … is a client, but does not
  appear in Talent or in talent pursuits until they have a talent profile." Its action
  button is "Create talent profile…", with an explicit accessible name.
- **`CreateTalentProfileDialog`.**
  - Title "Create talent profile", buttons Create and Cancel, and Enter commits (an ordinary
    affirmative dialog under AOS-R002-016).
  - The person is fixed; there is no picker.
  - Career stage is a labelled ComboBox offering exactly the domain's values, with Unknown
    ("not assessed yet") as the default.

**Not changed:**
- `ConvertProspectHandler`, which keeps ProspectsWrite and RepresentationWrite and does not
  need TalentWrite;
- `ListTalentAsync`;
- the subject semantics of a Talent engagement;
- permissions.

### Tests

| Suite | Test | Proves |
| --- | --- | --- |
| Integration, `SignedClientTalentProfileTests` | `AConvertedClient_EntersTalentOnlyThroughTheExplicitProfile` | prospect → convert gives an active representation, zero profiles, 404 from the talent read and no roster entry; then the explicit POST gives exactly one profile, and the person is in the roster and in the clients-only roster with IsClient, Active and scope Film, still under the single representation conversion created |
| Integration | `ASecondProfile_IsRefused` | 409, and one profile remains |
| Integration | `WithoutTheTalentPermission_TheProfileIsRefused_AndTheClientStaysSigned` | an Observer in the same tenant gets 403; no profile is created; the representation is unchanged, same status and same version |
| Client (Unit), `SignedClientTalentProfileTests` | 8 tests | signing offers the step and creates no profile; the step makes exactly one profile for that person with the chosen stage; afterwards the person is in the Talent list with the right client count, and in `EntityChoice.ForTalent`, the talent-engagement subject source; refusal; duplicate; unknown versus absent; selecting a converted pursuit; a refreshed list keeps the signed client |
| Windows, `SignedClientProfileSurfaceTests` | 6 tests | the step sits directly after the "Signed" line; it is an ordinary named button that is not taken out of the tab order; the profile is created only by the confirmed dialog, and `ConvertAsync` never creates one; the dialog is labelled and commits on Enter; there is no person picker; the stages are the domain's, with Unknown as the default |

The unit fake now answers a missing talent profile with 404 and a duplicate with 409, as the
server does.

Live discoverability is **not** claimed from these tests. It is for the fresh
final-candidate RC.

### Negative control

With the gate green, one local, uncommitted mutation was made:
`CanCreateTalentProfile => false`, which disables the explicit step.
- All 8 client tests failed.
- The C7 story test failed at the Talent roster with "Assert.Single() Failure: The collection
  was empty": the signed client never became a Talent member or a talent-pursuit subject.
- The file was restored byte-identical (SHA-256
  `042ef331a78dcf8545771a702e106e235a5e10bd7f4bfcc47617b0cf3af92551`) and the rerun was 8 of
  8.

### Local gates

| Gate | Result |
| --- | --- |
| `build` | 0 warnings, 0 errors |
| `test-unit` | 4099 passed |
| `test-windows` | 1820 passed |
| `test-reviewer` | 163 passed |
| Integration: representation and the new tests, on the LAB PostgreSQL 19 cluster | 17 passed (corroboration only; CI remains the 18.6 authority) |
| Full integration suite, on the LAB PostgreSQL 19 cluster | 952 passed, 0 failed, 0 skipped (the 949 of CI run 103, plus the 3 new tests; corroboration only) |

### What happens next

This changes production Windows and client code. The old RC is not resumed, and its step 1 is
not spliced into a new chain. The new candidate needs, in order:
1. Control Room source review;
2. fresh authoritative CI and PostgreSQL 18.6 on the new SHA;
3. a fresh final-candidate RC from the beginning: C3, the channels B–E, and C7 steps 1–5 as
   one story.

If it passes, the next ALPHA would be Build 96. That build has not been created.

## 14. Decision C: profile-state truth correction

### Control Room review of `218c6fe`

Control Room accepted the core of the decision-C repair at `218c6fe` as candidate work:
- conversion creates only the representation;
- the talent profile is an explicit step, on the existing contract-17 endpoint under
  `Permission.TalentWrite`;
- the step is offered through the ProfileBar, directly under the "Signed" line;
- the "Create talent profile…" dialog has a fixed person, Career stage defaulting to Unknown,
  and Create and Cancel;
- conversion authorization, `ListTalentAsync` and Talent-engagement semantics are unchanged.

It found one same-surface defect. Section 13 is kept, and so is the failed `f29a2ee` RC.

### The finding

`TalentProfileState.Unknown` meant both "not read yet" and "the read failed or was refused".
`SetSignedClient` set that state before `ReadProfileStateAsync` finished. `ProfileNotice`
rendered Unknown as "could not be checked", so the surface could announce a failed check
while the check was still running.

### Reproduction, before any fix, against the unchanged `218c6fe` product code

`AReadInFlight_IsNotSaidAsFailed` holds the talent read in flight on a
`TaskCompletionSource` gate in the fake API, with no timing involved. With the read
outstanding, it failed with:

> Assert.DoesNotContain() Failure: Sub-string found … "…as a talent profile could not be checked."

The full message said at that moment was "Whether Ada Reyes has a talent profile could not be
checked. Create one if they need to appear in Talent."

### The state model

| State | Meaning | Title / message | Create offered |
| --- | --- | --- | --- |
| `NotChecked` | a signed client is known; no read has started | "Talent profile" / "<name> is a client. Whether they have a talent profile has not been checked yet." | no |
| `Checking` | the talent read is in progress | "Talent profile" / **"Checking whether <name> already has a talent profile."** | no, so it cannot race the read |
| `Absent` | the talent read answered 404 | "Next: create a talent profile" / "<name> is a client, but does not appear in Talent or in talent pursuits until they have a talent profile." | yes |
| `Present` | a profile is known to exist | "Talent profile" / "<name> has a talent profile and appears in Talent." | no |
| `Unavailable` | a read was attempted and failed or was refused | "Talent profile" / **"Whether <name> has a talent profile could not be checked. Create one if they need to appear in Talent."** | yes; the server refuses a duplicate |

How the state moves:

- **Reads.**
  - Assigning a signed client gives `NotChecked`, and starting the read sets `Checking`.
  - A read that finds the profile gives `Present`; a 404 gives `Absent`.
  - A 403, any other API failure or a transport failure gives `Unavailable`. It is never
    `Absent`.
- **Stale reads.** Each read carries a generation number. A result is applied only if it is
  still the latest read and the signed client is unchanged. So an older read cannot overwrite
  a client the operator selected after it.
- **Creates.**
  - Success, or a 409 conflict, gives `Present`, and the create also retires any read still
    outstanding.
  - A 403 or any other failure keeps whatever state the client had.
  - The representation is never touched.

The change is to `RepresentationViewModels.cs` only. There is no XAML, API, contract, schema,
domain, query, permission or server change.

### Tests (client, deterministic)

| Test | Proves |
| --- | --- |
| `AReadInFlight_IsNotSaidAsFailed` | during an in-flight read the state is `Checking`, the message is "Checking whether Ada Reyes already has a talent profile.", it is not an error, and Create is not offered; releasing the read on a 404 gives `Absent` with Create offered |
| `ANewlySignedClient_IsNotChecked_UntilARead` | `NotChecked` before any read, with the "has not been checked yet" wording and no Create |
| `ASuccessfulRead_IsPresent` | `Present`, no Create, and "has a talent profile and appears in Talent." |
| `AnUnansweredRead_IsUnavailable_NotAbsent` | a refused read gives `Unavailable` with the "could not be checked" wording, and Create is still offered |
| `FromUnavailable_AConflictResolvesToPresent_WithoutADuplicate` | from `Unavailable`, a 409 gives `Present`, and one profile remains |
| `ARefusedCreate_KeepsTheStateItHad` | a refused create from `Absent` stays `Absent`; a failed create from `Unavailable` stays `Unavailable` |
| `AnOlderRead_CannotOverwriteANewerClient` | a read held for client A, then client B selected and read `Present`, then A's read released as 404: the state stays B's, `Present` |

The existing decision-C story still passes: convert, no profile, explicit creation, the Talent
roster, then the talent-engagement subject source. So do refusal, duplicate and selection. The
Windows structural tests and the three decision-C integration tests are unchanged and pass.

### Negative control

With the gate green, one local, uncommitted mutation was made: `Checking` was rendered with the
`Unavailable` wording ("… could not be checked …"). Exactly `AReadInFlight_IsNotSaidAsFailed`
then failed:

> Expected "Checking whether Ada Reyes already has a…", actual "Whether Ada Reyes has a talent profile co…"

The file was restored byte-identical (SHA-256
`406417c4314a10f11a7e9cdbba68a8da7a7d7b6edad2b53277b23698732aeda5`) and the rerun was green.

### Local gates

| Gate | Result |
| --- | --- |
| `build` | 0 warnings, 0 errors |
| `test-unit` | 4105 passed |
| `test-windows` | 1820 passed |
| `test-reviewer` | 163 passed |
| Decision-C integration tests, on the LAB PostgreSQL 19 cluster | 3 passed (corroboration; not 18.6 authority) |

C3, C7 and C9 are not claimed. The next stage is fresh authoritative CI and PostgreSQL 18.6 on
the new SHA, and then a fresh RC from the beginning.

## 15. Decision C: stale profile-notice correction

### Control Room review of `488e1d4`

Control Room accepted the five-state profile model and the generation guard against stale
reads. It found one remaining same-surface defect. Sections 13 and 14 are kept.

### The finding

`ProfileNotice` gives an earlier create outcome (`_profileMessage` / `_profileFailed`)
precedence over the current profile state. A later `CheckTalentProfileAsync()` set the state
to `Checking` but did not clear that outcome. So the surface could have
`ProfileState == Checking` while still showing "Talent profile not created", and the stale
outcome went on masking whatever the fresh read established.

### Reproduction, before any fix, against the unchanged `488e1d4` product code

A client is signed as `Absent`, and the explicit create is refused with 403. A fresh talent
read is then started and held in flight on the existing `TalentReadGates` gate, with no timing
involved. On that code all five new tests failed:

| Test | Expected | Shown at `488e1d4` |
| --- | --- | --- |
| A. read in flight | "Checking whether O'Brien D'Angelo-Smith a…" | "You do not have permission to create tale…" |
| B. read finds a profile | "O'Brien D'Angelo-Smith has a talent profi…" | "You do not have permission to create tale…" |
| C. read finds none | title "Next: create a talent profile" | title "Talent profile not created" |
| D. read fails | "Whether O'Brien D'Angelo-Smith has a tale…" | "You do not have permission to create tale…" |
| after a successful create, a deliberate re-read in flight | "Checking whether O'Brien D'Angelo-Smith a…" | "Talent profile created. O'Brien D'Angelo-…" |

### The fix

The change is in `RepresentationViewModels.cs` only. When `ReadProfileStateAsync` starts a
fresh read for the current signed client, it now clears `_profileMessage` and resets
`_profileFailed`, then sets `Checking`. The read owns the status from the moment it starts.

The accepted mechanism is unchanged:
- the five states and their meanings;
- 404 only gives `Absent`, and a failed or refused read gives `Unavailable`;
- Create is offered only for `Absent` or `Unavailable`;
- a successful create or a 409 gives `Present`, and a failed create keeps the prior state;
- the generation guard, and a successful create retiring an older read.

The immediate feedback after a create stays. It is superseded only when a later read actually
starts.

### Results

- **A.** With the read in flight after a refused create, the state is `Checking`, the message
  is "Checking whether O'Brien D'Angelo-Smith already has a talent profile.", it is not an
  error, the old refusal text is gone, and Create is not offered.
- **B, C, D.** The fresh read's own wording replaces the refusal:
  - Present gives "O'Brien D'Angelo-Smith has a talent profile and appears in Talent."
  - Absent gives "Next: create a talent profile" and "O'Brien D'Angelo-Smith is a client, but
    does not appear in Talent or in talent pursuits until they have a talent profile."
  - Unavailable gives "Whether O'Brien D'Angelo-Smith has a talent profile could not be
    checked. Create one if they need to appear in Talent."
- **A successful create, then a re-read.** "Talent profile created. … now appears in Talent."
  shows at once. A deliberate later read shows `Checking`, and then `Present` with its
  wording.

All earlier decision-C tests still pass: NotChecked, Checking, Absent, Present, Unavailable,
Unavailable → 409 → Present, a failed create keeping its state, the stale read across two
clients, and the full story.

### Negative control

With the gate green, one local, uncommitted mutation was made: a new read no longer cleared
the prior outcome. Exactly the five new tests failed; the other 14 decision-C client tests
passed. The file was restored byte-identical (SHA-256
`ad361379798a8c45c911e8a8b5ea357955afe543e1f05df15116ac920c119d37`) and the rerun was 19 of
19.

### Local gates

| Gate | Result |
| --- | --- |
| `build` | 0 warnings, 0 errors |
| `test-unit` | 4110 passed |
| `test-windows` | 1820 passed |
| `test-reviewer` | 163 passed |
| Decision-C integration tests, on the LAB PostgreSQL 19 cluster | 3 passed (corroboration only; not 18.6 authority) |

There was no XAML, API, contract, schema, migration, domain, query, permission or workflow
change. C3, C7 and C9 are not claimed. The next stage is fresh authoritative CI and
PostgreSQL 18.6 on the new SHA.

## 16. The stopped RC on `2d2b46a`, and the Draft → Active operator route

### What stood before the RC

Authoritative CI run `36073239552` (#104, `workflow_dispatch`) passed on exactly `2d2b46a`.
Its totals were:
- Unit 4110, Windows 1820, Reviewer 163;
- Integration 952 passed, 0 failed and 0 skipped, on PostgreSQL 18.6 with `pg_dump` and
  `pg_restore` at 18.6;
- contract 17, schema `20260909072201_AiResultClassification`, OpenAPI 3.1.1 with 264 paths
  and 188 schemas;
- TLC green, and a release manifest of 111 artifacts.

The 8 xUnit duplicate-ID discovery messages in `Finance.AllocationTests` were recorded
separately from the official skip count of 0.

### The fresh final-candidate RC: stopped before the operator handoff

- **Candidate:** `release -Channel alpha -BuildId 104` from exactly `2d2b46a`. The manifest
  was verified: version 0.1.0, alpha, build 104, commit `2d2b46a…`, contract 17, schema as
  above, and 111 artifacts with every hash checked.
  - `AgencyOS.Windows.exe` SHA-256
    `7a0d16ccda9d97f98fde428b6e0d05025a9bb7f3b1147c771876b2c58e0c7aee`, matching the manifest.
  - `AgencyOS.Windows.dll` SHA-256
    `73a6064d15a5cd6de7faece5f9e1d5e8819e9b451bf981093d3ed616b8b62bce`.
- **Backend:** a private PostgreSQL 19 beta 3 cluster on 127.0.0.1:5435, whose
  `data_directory` was the run's own `pgdata`. The API ran from source at `2d2b46a`.
- **Tenant:** synthetic, `01a0d5e6-0135-7bcc-809e-0489ced4f4c6`.
- **Evidence:** in `artifacts/reviewer/rc-2d2b46a-20260925/`, not committed.

The pre-handoff route mapping of the C7 steps found that step 2 had no Windows operator route.
The RC stopped there, as its sealed section 4 required, **before OPERATOR HANDOFF START**. No
C3, accessibility-channel or C7 live evidence was taken or is claimed from it. That stop, like
the `f29a2ee` RC stop, is kept as historical evidence.

### The canonical blocker

- **Draft by default:** `Opportunity.Create(...)` sets `Draft` (`Opportunity.cs:365`).
- **The allowed transition:** `Draft → Active` is in the transition table, alongside
  `Draft → Cancelled` (`Opportunity.cs:224-226`).
- **Active-only market activity:** `MarketActiveStatuses = { Active }`
  (`Opportunity.cs:209-210`). `RequireMarketActive` refuses a Draft with "This opportunity is
  still a draft. Activate it before recording market activity." (`Opportunity.cs:630-638`).
  Target moves call it, and opening a deal needs an Active pursuit (`DealCommands.cs:281-286`)
  and a target at Interested or Advanced (`Deal.cs:233-238`).
- **The endpoint already existed in contract 17:**
  `POST /api/v1/organizations/{organizationId}/opportunities/{opportunityId}/status`,
  requiring `Permission.OpportunitiesWrite` (`M6Endpoints.cs:206-237`).
  `ChangeOpportunityStatusRequest(Status, ExpectedVersion, OccurredOn?, Outcome?, Reason?)`.
- **So did the client method:** `IAgencyOsApi.ChangeOpportunityStatusAsync`.
- **What was missing:** no Windows page, dialog, palette command or view model called it.
- **The sibling defect:** the Pipeline list defaults to the Active filter, and `CreateAsync`
  discarded the created record and reloaded under that filter, so the new Draft vanished the
  moment it was made.

### Reproduction, before any fix

`PipelineActivationSurfaceTests` was run against the unchanged `2d2b46a` sources. All three
tests failed:
- no `ActivateButton` existed ("Sequence contains no matching element");
- nothing in `RenderDetail` or any handler offered or invoked activation;
- `CreateAsync` neither kept the created pursuit nor revealed it.

### The repair

It is bounded, and it touches the Windows client and the client view model only.

- **`OpportunityDetailViewModel`:**
  - `CanActivate` is true only for a loaded pursuit whose status is `Draft`.
  - `ActivateAsync()` sends the existing `ChangeOpportunityStatusAsync(id,
    new ChangeOpportunityStatusRequest("Active", <observed version>), <key made once>)` and
    then reloads the authoritative detail.
  - A refusal, including a version conflict, is thrown to the caller unchanged. Nothing
    locally claims Active, and nothing retries with a newer version.
- **Pipeline page:**
  - "Activate opportunity" is an ordinary button on the existing action row, beside New
    opportunity and Move target. Its accessible name is "Activate opportunity". It is
    collapsed unless the selected pursuit is a Draft.
  - It runs through the page's existing `Guarded` convention, so a refusal shows the
    domain's own sentence.
  - On success the same pursuit is revealed again and says Active. A notice reads
    "Opportunity activated: <name> is active. Targets can now be moved and negotiations
    opened."
  - `CreateAsync` keeps the created pursuit and calls the page's existing
    `Reveal(created.Opportunity.Id)`. That widens the filters, reloads, and selects the new
    pursuit, still a Draft, with Activate offered.
  - Creation never activates. The normal Active default of the list is unchanged.

Only Draft → Active is exposed: there is no pause, resume, close, cancel or reopen control.
There was no server, API, contract, DTO, domain, schema, migration, permission or deal-semantic
change.

### Tests

| Suite | Test | Proves |
| --- | --- | --- |
| Client, `OpportunityActivationTests` | `ACreatedPursuit_IsADraft_AndIsNotActivated` | creation leaves a Draft and sends no status change |
| Client | `TheActiveDefault_HidesANewDraft_AndWideningShowsIt` | the list's Active default hides the new Draft; widening the status, as the page's reveal does, shows it as a Draft |
| Client | `ALoadedDraft_OffersActivation` | a loaded Draft offers activation |
| Client | `AnActivePursuit_IsNotOfferedActivation` | an Active pursuit is not offered it, and nothing is sent |
| Client | `Activation_SendsActiveWithTheObservedVersion_AndReloadsTheSamePursuit` | exactly one request through the existing method, Status "Active", the observed version and a key; then the reload shows the same id, now Active, version + 1, standing "Active - …", targets kept, and no second pursuit |
| Client | `AConflict_IsNotSuccess_AndIsNotRetried` | a 409 is thrown; there is one request only; the state stays Draft |
| Client | `ARefusal_LeavesTheDraftAsItWas` | a 403 leaves the Draft as it was |
| Client | `ActivationUnblocksMarketActivityOnTheSamePursuit` | a Draft's target move is refused with the domain's sentence; after the operator activates the pursuit, the same target moves Approved → Contacted → Interested, the stage a negotiation opens from |
| Windows, `PipelineActivationSurfaceTests` | `TheActionIsAnOrdinaryNamedButtonOnTheActionRow` | the button is on the same row as New opportunity and Move target, collapsed by default, and not taken out of the tab order |
| Windows | `TheActionIsOfferedForADraftAndActivatesThroughTheViewModel` | the button is shown for a Draft only and runs the view model's activation through `Guarded` |
| Windows | `CreationRevealsTheDraftAndDoesNotActivateIt` | creation reveals the created pursuit and never calls activation |

The unit fake now refuses a stale expected version (409) and a target move on a Draft (400,
with the domain's sentence), as the server does. The existing Opportunity and Pipeline client
tests still pass: 83 in all.

### Negative controls

With the gate green, two local, uncommitted mutations were made, one after the other, and each
restored.

1. **`ActivateAsync` returns before sending the command.**
   - The four activation tests failed.
   - The story test failed at the first target move with the Draft refusal, 400 "Invalid
     request" with the "still a draft" detail. The pursuit stayed a Draft, and the story
     could not reach market activity.
   - The view model was restored byte-identical (SHA-256
     `82c77ca30f4f92720fce5d2e6e0d1362258558c3be2e86df63426ab7d75201fe`), and the rerun was 8
     of 8.
2. **`CreateAsync` reverts to reloading without revealing the created pursuit.**
   - Exactly `CreationRevealsTheDraftAndDoesNotActivateIt` failed.
   - The page was restored byte-identical (SHA-256
     `985367c550bcc4b99317b90387794ce43caa59b94096f71c6e00ba308b786d4d`).

### Local gates

| Gate | Result |
| --- | --- |
| `build` | 0 warnings, 0 errors |
| `test-unit` | 4118 passed |
| `test-windows` | 1823 passed |
| `test-reviewer` | 163 passed |
| `OpportunityTests` on the LAB PostgreSQL 19 cluster | 17 passed (corroboration only, not 18.6 authority); they already drive the existing endpoint from Draft to Active before market activity |

The other route observations from the stopped RC are not part of this repair and were not
touched:
- the client-money receivable context;
- the "Streamer" party role;
- the signature-date interaction;
- the unnamed Finance automation IDs.

C3, C7 and C9 are not claimed. A production change means the next accepted SHA needs a fresh CI
run and a fresh RC from the beginning. Build 96 has not been created.
