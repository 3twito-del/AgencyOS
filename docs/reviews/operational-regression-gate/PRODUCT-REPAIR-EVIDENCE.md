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
