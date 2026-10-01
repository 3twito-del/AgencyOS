# Operational regression gate: the pre-repair parity population

Work in progress on `operational-regression-gate`. **Not a release candidate. C3, C9 and the
FAST gate are not proved**: product repair has not started, and this records exactly what it
must repair.

Produced by `tests/AgencyOS.Tests.Windows/Accessibility/OperationalListParityTests.cs` after
the methodology correction. Every figure below was produced by running it.

## 1. The catalog

| | |
| --- | --- |
| Row templates in the client | **112** |
| Declared inline as `ListView.ItemTemplate` | 106 |
| Keyed in `App.xaml`, used only as a `ListView` `ItemTemplate` | 6 |
| Templates executed by the parity property | **112** |
| Templates excluded for an inaccessible row type | **0** |
| Visible bindings accounted for | **346**: 328 primary, 18 secondary |

The count is unchanged from the first run. It is now proved structurally rather than assumed:
`EveryCatalogTemplateIsAListItemTemplate` requires each template to be declared inline as a
list's item template, or to be keyed and used, at least once and only, as one.

### The six formerly unreachable rows

At `cad3a97` six templates were excluded because their row types are declared inside the
WinUI assembly. The first remedy tried was to load that assembly and build the exact types. It
failed for a reason that settles the question: touching any type in `AgencyOS.Windows.dll`
runs its module initializer, which starts the Windows App Runtime bootstrap
(`DllNotFoundException: Microsoft.WindowsAppRuntime.Bootstrap.dll`). Building the exact types
means initializing the GUI stack.

They are built from **surrogates** instead, under the three conditions the brief sets, each
checked:

1. `RowLabel`, `TaskLine`, `PartyLine` and `TargetLine` read a row by reflection: its type name
   and its public properties' names, types and values. Their one type-identity branch,
   `row is PredictionResponse`, matches neither the production types nor the surrogates.
2. `EverySurrogateMatchesItsProductionDeclaration` reads each production declaration and
   requires the same type name and exactly the same public properties with the same types.
3. The same test fails if a declaration overrides `ToString` or declares a computed property.

| Template | Production type | Declared in |
| --- | --- | --- |
| `Dialogs/AllocatePaymentDialog.xaml#AllocationList` | `AllocationRow` | `Dialogs/RecordPaymentDialog.xaml.cs` |
| `Dialogs/RecordPaymentDialog.xaml#AllocationList` | `AllocationRow` | `Dialogs/RecordPaymentDialog.xaml.cs` |
| `Dialogs/RecordInvoiceDialog.xaml#LineList` | `AllocationRow` | `Dialogs/RecordPaymentDialog.xaml.cs` |
| `Dialogs/PostJournalEntryDialog.xaml#LineList` | `JournalLineRow` | `Dialogs/PostJournalEntryDialog.xaml.cs` |
| `Dialogs/RecordOfferDialog.xaml#TermList` | `RecordOfferDialog+StagedTerm` | `Dialogs/RecordOfferDialog.xaml.cs` |
| `Pages/OrganizationPage.xaml#MemberList` | `OrganizationPage+MemberLine` | `Pages/OrganizationPage.xaml.cs` |

Their results are in the population below; they contribute **8** primary gaps.

## 2. The comparison rules

Each visible value is rendered through the client function its converter calls — no
formatting is copied into the test — and compared by what it is:

| Kind | Covered when |
| --- | --- |
| Text | the announcement contains it as a whole word sequence |
| Token (`DisplayLabel`) | the announcement contains the same words |
| Number | the same number, invariant or current culture |
| Flag | its role word, because a set flag is announced by name |
| Date (`DateOnly`, or shown through the date converter) | the same calendar date in any of the product's formats; no time is asked |
| Instant (raw `DateTime` / `DateTimeOffset`) | the same date and time to the second; for an offset value, the same instant written with an offset |
| Money | the same amount in the same currency, whatever the number format |
| Bare amount | the row visibly states that money's currency, and the announcement says the amount in it |
| Party | the role word and the name together, as `PartyLine` writes them |
| Composed converter output | each part, split at the product's visible separator |

**Role identity.** A row showing more than one number, date or flag, or more than one text or
token outside `RowLabel`'s settled vocabularies (headline, qualifiers, context, stated value),
must announce each with its role word, or one could answer for another. The role word is the
field's own name, or the list's visible column header where the manifest records one.

**Dates and instants.** A difference in format alone is not a parity defect. What the visible
channel shows is what must be kept: a date shows a calendar day, while a raw instant shows a
day, a time and (as a `DateTimeOffset`) an offset. No visible formatting was changed.

**BalanceList passes, for the canonical reason and with no exemption.** The row shows three
bare amounts beside one Currency column. Each amount is covered because the row visibly states
that money's currency and the announcement says the amount in it. A bare amount on a row with
no visible currency is not covered, and a self-test holds that case.

### Controls on the comparator (all passing)

| Test | Proves |
| --- | --- |
| `TheComparatorRefusesTheRightNameUnderTheWrongRole` | a name under the wrong role does not cover |
| `TheComparatorFailsTwoDatesSwappedBetweenRoles` | two dates swapped between roles both fail |
| `TheComparatorRefusesTheSameDateUnderTheWrongRole` | one date under the wrong role does not cover the other |
| `TheComparatorAcceptsADateInAnotherFormat` | format alone is not a defect |
| `TheComparatorRefusesTheSameAmountInAnotherCurrency` | same amount, other currency, fails; money followed by the separator passes |
| `TheComparatorAcceptsABareAmountInTheRowsStatedCurrency` | the BalanceList rule |
| `TheComparatorRefusesABareAmountWhoseCurrencyIsNotOnTheRow` | the rule does not excuse an amount with no currency |
| `TheComparatorCoversAFlagOnlyByItsRole` | one flag cannot answer for another |
| `TheComparatorRequiresWholeWords` | a token inside a longer word is not said |
| `SentinelsAreDeterministic` | two builds give the same row, identifiers included |
| `SentinelsAreDistinctWithinEveryRow` | no two scalar fields of a row share a value |

## 3. PRIMARY gaps: values shown and not in the row's name

**110 values on 70 templates**, grouped by the mechanism in
`RowLabel` that loses them.

### D1 Dates: RowLabel has no date vocabulary: 11

| Template | Row type | Binding |
| --- | --- | --- |
| `Pages/ContractsPage.xaml#MoneyObligationList` | `MonetaryObligationResponse` | `{Binding DueOn}` |
| `Pages/ContractsPage.xaml#NoticeList` | `NoticeRecordResponse` | `{Binding OccurredOn}` |
| `Pages/ContractsPage.xaml#NoticeRequirementList` | `NoticeRequirementResponse` | `{Binding DueOn}` |
| `Pages/ContractsPage.xaml#ObligationList` | `ObligationResponse` | `{Binding DueOn}` |
| `Pages/ContractsPage.xaml#OptionList` | `ContractOptionResponse` | `{Binding DeadlineOn}` |
| `Pages/ContractsPage.xaml#PartyList` | `ContractPartyResponse` | `{Binding SignedOn}` |
| `Pages/FinancePage.xaml#CommissionRuleList` | `CommissionRuleResponse` | `{Binding EffectiveFrom}` |
| `Pages/FinancePage.xaml#JournalList` | `JournalEntryResponse` | `{Binding PostingDate}` |
| `Pages/FinancePage.xaml#PaymentList` | `PaymentResponse` | `{Binding ReceivedOn}` |
| `Pages/TalentPage.xaml#HistoryList` | `RepresentationHistoryEntryResponse` | `{Binding OccurredOn}` |
| `Pages/TalentPage.xaml#ScopeList` | `RepresentationScopeResponse` | `{Binding StartsOn, Converter={StaticResource IsoDate}}` |

### D1b Instants: RowLabel has no date or time vocabulary: 9

| Template | Row type | Binding |
| --- | --- | --- |
| `App.xaml#TimelineEntryTemplate` | `TimelineEntryResponse` | `{Binding OccurredAt}` |
| `Pages/AiPage.xaml#ApprovalList` | `AiApprovalResponse` | `{Binding ExpiresAt}` |
| `Pages/AiPage.xaml#RunList` | `AgentRunResponse` | `{Binding StartedAt}` |
| `Pages/AiPage.xaml#StepList` | `AgentStepResponse` | `{Binding OccurredAt}` |
| `Pages/CommunicationsPage.xaml#MailboxList` | `CommunicationAccountResponse` | `{Binding LastSyncedAt}` |
| `Pages/CommunicationsPage.xaml#MessageList` | `MessageSummaryResponse` | `{Binding OccurredAt}` |
| `Pages/DocumentsPage.xaml#HistoryList` | `DocumentEventResponse` | `{Binding OccurredAt}` |
| `Pages/DocumentsPage.xaml#VersionList` | `DocumentVersionResponse` | `{Binding RecordedAt}` |
| `Pages/FinancePage.xaml#HistoryList` | `FinanceHistoryEntryResponse` | `{Binding OccurredAt}` |

### D2 Numbers: RowLabel announces no bare numbers: 5

| Template | Row type | Binding |
| --- | --- | --- |
| `Pages/CommunicationsPage.xaml#OutboundList` | `OutboundDispatchResponse` | `{Binding AttemptCount}` |
| `Pages/ContractsPage.xaml#VersionList` | `ContractVersionResponse` | `{Binding VersionNumber}` |
| `Pages/DealsPage.xaml#OfferList` | `OfferResponse` | `{Binding Sequence}` |
| `Pages/DocumentsPage.xaml#VersionList` | `DocumentVersionResponse` | `{Binding Sequence}` |
| `Pages/TalentPage.xaml#CreditList` | `CreditResponse` | `{Binding Year}` |

### D3 Flags: a shown flag is not one of RowLabel's qualifiers: 1

| Template | Row type | Binding |
| --- | --- | --- |
| `Pages/CommunicationsPage.xaml#AttachmentList` | `MessageAttachmentResponse` | `{Binding HoldsContent}` |

### D4 Qualifier budget: RowLabel stops after two qualifiers: 3

| Template | Row type | Binding |
| --- | --- | --- |
| `Pages/ContractsPage.xaml#MoneyObligationList` | `MonetaryObligationResponse` | `{Binding Category, Converter={StaticResource DisplayLabel}}` |
| `Pages/FinancePage.xaml#CommissionList` | `CommissionEntitlementResponse` | `{Binding Status, Converter={StaticResource DisplayLabel}}` |
| `Pages/ProjectsPage.xaml#ProjectList` | `ProjectSummaryResponse` | `{Binding Type, Converter={StaticResource DisplayLabel}}` |

### T1 Text on a row with no headline vocabulary: it announces its type name: 25

| Template | Row type | Binding |
| --- | --- | --- |
| `Dialogs/PostJournalEntryDialog.xaml#LineList` | `JournalLineRow` | `{Binding Account}` |
| `Dialogs/PostJournalEntryDialog.xaml#LineList` | `JournalLineRow` | `{Binding AmountDisplay}` |
| `Dialogs/PostJournalEntryDialog.xaml#LineList` | `JournalLineRow` | `{Binding Side}` |
| `Pages/AiPage.xaml#ModelList` | `AiModelDescriptorResponse` | `{Binding Key}` |
| `Pages/AiPage.xaml#ModelList` | `AiModelDescriptorResponse` | `{Binding ProviderKey}` |
| `Pages/AiPage.xaml#PolicyList` | `AiProviderPolicyResponse` | `{Binding MaximumSensitivity}` |
| `Pages/AiPage.xaml#PolicyList` | `AiProviderPolicyResponse` | `{Binding ProviderKey}` |
| `Pages/AiPage.xaml#RunList` | `AgentRunResponse` | `{Binding Task}` |
| `Pages/CommunicationsPage.xaml#AttachmentList` | `MessageAttachmentResponse` | `{Binding FileName}` |
| `Pages/CommunicationsPage.xaml#AttachmentList` | `MessageAttachmentResponse` | `{Binding MediaType}` |
| `Pages/CommunicationsPage.xaml#MessageLinkList` | `MessageLinkResponse` | `{Binding TargetLabel}` |
| `Pages/CommunicationsPage.xaml#MessageLinkList` | `MessageLinkResponse` | `{Binding Target}` |
| `Pages/ContractsPage.xaml#RightsList` | `RightsGrantResponse` | `{Binding Medium}` |
| `Pages/ContractsPage.xaml#RightsList` | `RightsGrantResponse` | `{Binding PeriodKind}` |
| `Pages/ContractsPage.xaml#RightsList` | `RightsGrantResponse` | `{Binding RightType}` |
| `Pages/ContractsPage.xaml#RightsList` | `RightsGrantResponse` | `{Binding Territory}` |
| `Pages/DocumentsPage.xaml#LinkList` | `DocumentLinkResponse` | `{Binding TargetLabel}` |
| `Pages/DocumentsPage.xaml#LinkList` | `DocumentLinkResponse` | `{Binding Target}` |
| `Pages/FinancePage.xaml#CommissionRuleList` | `CommissionRuleResponse` | `{Binding Basis}` |
| `Pages/FinancePage.xaml#CommissionRuleList` | `CommissionRuleResponse` | `{Binding ClientDisplayName}` |
| `Pages/FinancePage.xaml#PaymentList` | `PaymentResponse` | `{Binding ExternalReference}` |
| `Pages/IntelligencePage.xaml#SignalEvidenceList` | `SignalEvidenceResponse` | `{Binding SourceTitle}` |
| `Pages/IntelligencePage.xaml#ThesisEvidenceList` | `ThesisEvidenceResponse` | `{Binding SignalTitle}` |
| `Pages/IntelligencePage.xaml#ThesisEvidenceList` | `ThesisEvidenceResponse` | `{Binding Stance}` |
| `Pages/ProjectsPage.xaml#CompanyList` | `ProjectCompanyResponse` | `{Binding Capacity}` |

### T2 Text in a field no RowLabel vocabulary reads: 52

| Template | Row type | Binding |
| --- | --- | --- |
| `App.xaml#PersonRowTemplate` | `PersonSummaryResponse` | `{Binding Title}` |
| `App.xaml#RelationshipRowTemplate` | `RelationshipResponse` | `{Binding To.Name}` |
| `Dialogs/AllocatePaymentDialog.xaml#AllocationList` | `AllocationRow` | `{Binding AmountDisplay}` |
| `Dialogs/ComposeMessageDialog.xaml#RecipientList` | `RecipientRequest` | `{Binding Address}` |
| `Dialogs/RecordInvoiceDialog.xaml#LineList` | `AllocationRow` | `{Binding AmountDisplay}` |
| `Dialogs/RecordPaymentDialog.xaml#AllocationList` | `AllocationRow` | `{Binding AmountDisplay}` |
| `Dialogs/ResolveParticipantDialog.xaml#SuggestionList` | `ParticipantSuggestionResponse` | `{Binding MatchedAddress}` |
| `MainWindow.xaml#PaletteResults` | `PaletteCommand` | `{Binding Category}` |
| `MainWindow.xaml#PaletteResults` | `PaletteCommand` | `{Binding Shortcut}` |
| `MainWindow.xaml#SearchResults` | `SearchHit` | `{Binding MatchedOn}` |
| `MainWindow.xaml#SearchResults` | `SearchHit` | `{Binding Subtitle}` |
| `Pages/AiPage.xaml#ApprovalList` | `AiApprovalResponse` | `{Binding ToolName}` |
| `Pages/AiPage.xaml#ToolList` | `AiToolDescriptorResponse` | `{Binding RequiredPermission}` |
| `Pages/CommunicationsPage.xaml#DeskList` | `OutboundDispatchResponse` | `{Binding MailboxAddress}` |
| `Pages/CommunicationsPage.xaml#MailboxList` | `CommunicationAccountResponse` | `{Binding MailboxAddress}` |
| `Pages/CommunicationsPage.xaml#MailboxList` | `CommunicationAccountResponse` | `{Binding Visibility}` |
| `Pages/CommunicationsPage.xaml#MessageList` | `MessageSummaryResponse` | `{Binding FromAddress}` |
| `Pages/CommunicationsPage.xaml#OutboundList` | `OutboundDispatchResponse` | `{Binding MailboxAddress}` |
| `Pages/CommunicationsPage.xaml#ParticipantList` | `ParticipantResponse` | `{Binding Address}` |
| `Pages/ContractsPage.xaml#ReconcileList` | `ReconciliationLineResponse` | `{Binding Contracted.DisplayValue}` |
| `Pages/ContractsPage.xaml#ReconcileList` | `ReconciliationLineResponse` | `{Binding Result}` |
| `Pages/ContractsPage.xaml#TermList` | `ContractTermResponse` | `{Binding ClauseReference}` |
| `Pages/DealsPage.xaml#ComparisonList` | `TermDifferenceResponse` | `{Binding Change}` |
| `Pages/DealsPage.xaml#ComparisonList` | `TermDifferenceResponse` | `{Binding Current.DisplayValue}` |
| `Pages/DocumentsPage.xaml#DocumentList` | `DocumentSummaryResponse` | `{Binding Sensitivity}` |
| `Pages/DocumentsPage.xaml#VersionList` | `DocumentVersionResponse` | `{Binding ContentHash}` |
| `Pages/DocumentsPage.xaml#VersionList` | `DocumentVersionResponse` | `{Binding MediaType}` |
| `Pages/FinancePage.xaml#JournalList` | `JournalEntryResponse` | `{Binding Source}` |
| `Pages/FinancePage.xaml#ReceivableList` | `ReceivableResponse` | `{Binding Beneficiary}` |
| `Pages/FinancePage.xaml#ReceivableList` | `ReceivableResponse` | `{Binding ContractTitle}` |
| `Pages/IntelligencePage.xaml#SignalList` | `SignalResponse` | `{Binding Sensitivity}` |
| `Pages/IntelligencePage.xaml#SignalList` | `SignalResponse` | `{Binding Verification}` |
| `Pages/IntelligencePage.xaml#SourceList` | `IntelligenceSourceResponse` | `{Binding Reliability}` |
| `Pages/IntelligencePage.xaml#SourceList` | `IntelligenceSourceResponse` | `{Binding Sensitivity}` |
| `Pages/OrganizationPage.xaml#MemberList` | `MemberLine` | `{Binding Email}` |
| `Pages/OrganizationPage.xaml#MemberList` | `MemberLine` | `{Binding Note}` |
| `Pages/PackagesPage.xaml#AttachedList` | `PackageElementResponse` | `{Binding Detail}` |
| `Pages/PackagesPage.xaml#ProposedList` | `PackageElementResponse` | `{Binding Detail}` |
| `Pages/PipelinePage.xaml#HistoryList` | `OpportunityHistoryEntryResponse` | `{Binding TargetDisplayName}` |
| `Pages/PipelinePage.xaml#PitchList` | `PitchResponse` | `{Binding TargetDisplayName}` |
| `Pages/PipelinePage.xaml#SubjectList` | `OpportunitySubjectResponse` | `{Binding Detail}` |
| `Pages/PipelinePage.xaml#SubmissionList` | `SubmissionResponse` | `{Binding TargetDisplayName}` |
| `Pages/ProjectsPage.xaml#AttachmentList` | `AttachmentResponse` | `{Binding RoleType}` |
| `Pages/ProjectsPage.xaml#MaterialList` | `ProjectMaterialResponse` | `{Binding PersonName}` |
| `Pages/ProjectsPage.xaml#SourceList` | `SourcePropertyResponse` | `{Binding AttributedCreator}` |
| `Pages/SavedViewsPage.xaml#ResultList` | `SavedViewRow` | `{Binding Subtitle}` |
| `Pages/SavedViewsPage.xaml#ViewList` | `SavedViewResponse` | `{Binding Target}` |
| `Pages/SyncPage.xaml#QueueList` | `PendingChangeItem` | `{Binding Explanation}` |
| `Pages/TalentPage.xaml#CreditList` | `CreditResponse` | `{Binding Role}` |
| `Pages/TalentPage.xaml#MaterialList` | `MaterialResponse` | `{Binding VersionLabel}` |
| `Pages/TalentPage.xaml#TalentList` | `TalentSummaryResponse` | `{Binding CareerStage}` |
| `Pages/TalentPage.xaml#TalentList` | `TalentSummaryResponse` | `{Binding RepresentationStatus}` |

### T3 Text cut by the 160-character budget: 2

| Template | Row type | Binding |
| --- | --- | --- |
| `Pages/FinancePage.xaml#CommissionList` | `CommissionEntitlementResponse` | `{Binding ClientDisplayName}` |
| `Pages/FinancePage.xaml#CommissionList` | `CommissionEntitlementResponse` | `{Binding ContractTitle}` |

### T4 Text announced but not attributed to its role: 2

| Template | Row type | Binding |
| --- | --- | --- |
| `Pages/ContractsPage.xaml#ReconcileList` | `ReconciliationLineResponse` | `{Binding Negotiated.DisplayValue}` |
| `Pages/DealsPage.xaml#ComparisonList` | `TermDifferenceResponse` | `{Binding Previous.DisplayValue}` |

## 4. SECONDARY values

Explanatory prose beneath a row already identified by its primary facts. Classified per row,
not by property name: a `Detail` that is a role or a kind (package elements, opportunity
subjects) is primary. **Every secondary value is LIVE PROOF PENDING.** Wiring is structural
only, and a channel is accepted only when the release-candidate client run shows an operator
finding and reading it from the same row.

| Template | Binding | Candidate channel | Wired now | Status | Why secondary |
| --- | --- | --- | --- | --- | --- |
| `App.xaml#InteractionTemplate` | `{Binding DetailedNotes}` | Descendant | yes | LIVE PROOF PENDING | The interaction's long account, behind the "Detailed notes" expander; the row is identified by its summary and type. |
| `App.xaml#TimelineEntryTemplate` | `{Binding Detail}` | HelpText | no | LIVE PROOF PENDING | Supporting detail beneath a timeline entry identified by its title and time. |
| `Pages/AiPage.xaml#ToolList` | `{Binding Description}` | HelpText | no | LIVE PROOF PENDING | What the tool does, in prose, beneath a tool identified by its name and the permission it needs. |
| `Pages/ContractsPage.xaml#VersionList` | `{Binding Notes}` | HelpText | no | LIVE PROOF PENDING | What changed, in prose, beneath a version identified by its number, label, direction and status. |
| `Pages/ContractsPage.xaml#HistoryList` | `{Binding Detail}` | HelpText | no | LIVE PROOF PENDING | Secondary context beneath a history entry identified by its summary, actor and kind. |
| `Pages/DealsPage.xaml#TermList` | `{Binding Notes}` | HelpText | no | LIVE PROOF PENDING | Context that is not part of the term's value; the term is identified by its name and value. |
| `Pages/DealsPage.xaml#HistoryList` | `{Binding Detail}` | HelpText | no | LIVE PROOF PENDING | Secondary context beneath a history entry identified by its summary, actor and kind. |
| `Pages/IntelligencePage.xaml#DisputedList` | `{Binding Claim}` | HelpText | no | LIVE PROOF PENDING | The claim in full beneath a disputed signal identified by its title. |
| `Pages/IntelligencePage.xaml#RadarReviewList` | `{Binding Rationale}` | HelpText | no | LIVE PROOF PENDING | Why the person is being watched, beneath a review entry identified by the person. |
| `Pages/IntelligencePage.xaml#SignalList` | `{Binding Claim}` | HelpText | no | LIVE PROOF PENDING | The claim in full beneath a signal identified by its title, verification and sensitivity. |
| `Pages/IntelligencePage.xaml#SignalEvidenceList` | `{Binding Excerpt}` | HelpText | no | LIVE PROOF PENDING | An analyst's quotation from the source, beneath evidence identified by its source and role. |
| `Pages/IntelligencePage.xaml#ThesisList` | `{Binding Proposition}` | HelpText | no | LIVE PROOF PENDING | The belief in full beneath a thesis identified by its title and status. |
| `Pages/IntelligencePage.xaml#ThesisRevisionList` | `{Binding ChangeNote}` | HelpText | no | LIVE PROOF PENDING | Why the belief changed, beneath a revision identified by the proposition it moved to. |
| `Pages/IntelligencePage.xaml#WatchlistList` | `{Binding Purpose}` | HelpText | no | LIVE PROOF PENDING | What the watchlist is for, beneath a watchlist identified by its name. |
| `Pages/IntelligencePage.xaml#WatchlistSignalList` | `{Binding Claim}` | HelpText | no | LIVE PROOF PENDING | The claim in full beneath a signal identified by its title. |
| `Pages/IntelligencePage.xaml#RadarList` | `{Binding Rationale}` | HelpText | no | LIVE PROOF PENDING | Why the person is being watched, beneath a radar entry identified by the person, status and priority. |
| `Pages/ProjectsPage.xaml#HistoryList` | `{Binding Detail}` | HelpText | no | LIVE PROOF PENDING | Secondary context beneath a history entry identified by its summary, actor and kind. |
| `Pages/TalentPage.xaml#HistoryList` | `{Binding Detail}` | HelpText | no | LIVE PROOF PENDING | Supporting detail beneath a history entry identified by its title and date. |

`HelpText` is a proposed channel; no row declares one yet, so those values fail
`EverySecondaryValueIsWiredToItsChannel`. The detailed-notes expander is wired now and still
pending live proof.

## 5. How the first run's figures changed

At `cad3a97`: 106 templates executed, 77 failing, 121 omitted values on 119 distinct fields
(`BalanceList.Amount` counted three times). Now:

| Change | Fields | Reason |
| --- | --- | --- |
| reclassified SECONDARY | −17 | explanatory prose, per row, with channel and reason (section 4) |
| `BalanceList` `Amount` and `Currency` | −2 | covered by the stated-currency rule |
| six formerly unreachable rows | +8 | now executed through surrogates |
| **PRIMARY gaps at `d1a6e77`** | **108** | on 70 templates, superseded in section 7 |

The detailed-notes value was skipped silently at `cad3a97` because it sits inside a named
expander. It is now accounted for as SECONDARY.

One detector artifact was found and fixed during this pass: the money pattern read the comma
of the row separator as part of the currency, so covered money on `PaymentList` and
`ReconcileList` was reported missing. A comparator control now holds that case.

## 6. The source-reading rule

Every test file that reads a file must declare which kind of read it is:

- `// SOURCE-PROOF:`, which reads the repository's source or markup, and why that text is the evidence;
- `// NOT-SOURCE-READ:`, which reads only what the test itself produced.

The rule keys on the read itself (text, bytes, streams, XML/XAML loads), so a helper handed an
already-resolved path cannot hide one. A stale declaration fails, and so does a
`NOT-SOURCE-READ` declaration in a file that names the product's source. There is no list of
approved classes. Controls in `SourceReadingJustificationTests.TheRuleTellsTheShapesApart`:

| Control | Verdict |
| --- | --- |
| direct read of repository source, undeclared | Undeclared |
| the same, declared | Sound |
| read through a helper handed a path (text, bytes, StreamReader) | Undeclared |
| XElement.Load, XDocument.Load | Undeclared |
| log read, declared NOT-SOURCE-READ | Sound |
| declared NOT-SOURCE-READ while naming src | Misclassified |
| declaration with no read | Stale |
| reason under 60 characters, or both declarations | Malformed |

**SOURCE-PROOF: 40 files.** At `cad3a97`, 38 existing test files received comment-only
declarations and the two gate files carried their own. The population is unchanged by this
pass; two gate-file declarations were reworded.

- `tests/AgencyOS.Tests.Reviewer/ReviewerBoundaryTests.cs`
- `tests/AgencyOS.Tests.Unit/Architecture/ArchitecturalFitnessTests.cs`
- `tests/AgencyOS.Tests.Unit/Architecture/OperatorGateVocabularyTests.cs`
- `tests/AgencyOS.Tests.Unit/Architecture/SecretScanTests.cs`
- `tests/AgencyOS.Tests.Unit/Architecture/SourceReadingJustificationTests.cs`
- `tests/AgencyOS.Tests.Unit/Commands/OrganizationCommandTests.cs`
- `tests/AgencyOS.Tests.Windows/Accessibility/OperationalListParityTests.cs`
- `tests/AgencyOS.Tests.Windows/Accessibility/XamlAccessibilityTests.cs`
- `tests/AgencyOS.Tests.Windows/Accessibility/XamlRowAndTokenTests.cs`
- `tests/AgencyOS.Tests.Windows/Dialogs/EnterConventionTests.cs`
- `tests/AgencyOS.Tests.Windows/Dialogs/LoadTimeHandlerTests.cs`
- `tests/AgencyOS.Tests.Windows/Dialogs/OpenerRefusalTests.cs`
- `tests/AgencyOS.Tests.Windows/Dialogs/RawIdentifierEntryTests.cs`
- `tests/AgencyOS.Tests.Windows/Layout/CommandReachTests.cs`
- `tests/AgencyOS.Tests.Windows/Layout/SelfScrollingListTests.cs`
- `tests/AgencyOS.Tests.Windows/Layout/ShellLayoutTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/AuthoringDiscoverabilityTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/AuthoringRouteTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/AuthoritySurfaceTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/CreationPairingTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/DealPaperTruthTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/DealsWorkspaceFilterTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/DiscoverabilitySurfaceTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/DispatchedRefusalTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/FieldDescriptionTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/FinanceSurfaceTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/ForecastSurfaceTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/IdentitySurfaceTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/MoneySurfaceTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/OperatorRouteTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/RefusalDestinationTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/RefusalPresentationTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/RepresentationReachTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/ResidualRouteTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/RowNameStructureTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/ScopeSurfaceTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/StayOpenRefusalTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/TargetSurfaceSemanticsTests.cs`
- `tests/AgencyOS.Tests.Windows/Presentation/TaskSurfaceSemanticsTests.cs`
- `tests/AgencyOS.Tests.Windows/Security/ClientBoundaryTests.cs`

**NOT-SOURCE-READ: 4 files**, added in this pass:

- `tests/AgencyOS.Tests.Integration/Operations/BackupRestoreDrillTests.cs`
- `tests/AgencyOS.Tests.Reviewer/BoundedCaptureTests.cs`
- `tests/AgencyOS.Tests.Unit/Client/DpapiCacheKeyProviderTests.cs`
- `tests/AgencyOS.Tests.Unit/Client/LocalCacheTests.cs`

None of the declarations supports a runtime claim that source cannot prove. Each file's own
remarks already bound its assertions to source, markup or build structure.

## 7. Final methodology correction

`d1a6e77` was not yet final: three mechanisms could still report green without the
announcement carrying what the row shows.

**Temporal fidelity.** Raw `DateTime` and `DateTimeOffset` values were reduced to a calendar
day, so an announcement with the right day and the wrong time, or no offset, would have
passed. They are now their own kind, `Instant`: covered only by the same date and time to the
second, and, for an offset value, by an announcement that carries an offset and denotes the
same instant. `DateOnly` and values shown through the date converter remain calendar dates,
with no time asked.

**Generic role = value.** Text and domain tokens were covered by presence alone, so subject X
and assignee Y swapped would have passed. When a row shows two or more text or token values
that could answer for each other, each must now appear with its role word in its own segment.
A value needs no label when its field is in one of `RowLabel`'s settled vocabularies, read by
reflection rather than copied: the headline it names the row by, its qualifiers, its context
field, and a term's stated value. Membership is judged on the row's own field, so
`Previous.DisplayValue` is judged as `Previous`. Where the field name is not the operator's
word, the manifest records the row's own column header, and
`EveryAccountedRoleWordIsVisibleOnItsPage` requires that word to be visible text on that page:

| Binding | Role word | Source |
| --- | --- | --- |
| `DealsPage#ComparisonList` `Previous.DisplayValue` / `Current.DisplayValue` / `Change` | Previous / Current / Change | column headers |
| `ContractsPage#ReconcileList` `Negotiated.DisplayValue` / `Contracted.DisplayValue` / `Result` | Agreed / In the draft / Result | column headers |

A first cut of the rule judged only headline and qualifier fields as settled, and briefly
demanded labels on `PersonRowTemplate.PrimaryCompanyName`, `ProjectsPage#CompanyList.CompanyName`
and `ContractsPage#TermList.DisplayValue`. Those were the rule over-reaching, not product gaps:
each is in `RowLabel`'s context or stated-value vocabulary, which it deliberately announces
bare. They are not in the population.

**Surrogate shape.** The ratchet caught `ToString` and expression-bodied properties but not a
block-bodied getter. It now accepts only the auto-property forms and rejects any other
property body. None of the four production declarations violates it.

### Controls added (all passing)

| Test | Proves |
| --- | --- |
| `TheComparatorAsksNoTimeOfADate` | DateOnly and IsoDate acquire no time requirement |
| `TheComparatorAcceptsADateInAnotherFormat` | an IsoDate date in another format passes |
| `TheComparatorRefusesTheSameDayAtAnotherTime` | a raw DateTime on the same day at another time, or with the time dropped, fails |
| `TheComparatorAcceptsTheSameInstantInAnotherFormat` | the same date and time in another format passes |
| `TheComparatorRefusesAnInstantUnderAnotherOffset` | same wall clock under another offset fails, offset dropped fails; the same instant in another offset passes |
| `TheComparatorAcceptsTwoTextsInTheirRoles` | two texts in their roles pass |
| `TheComparatorFailsTwoTextsSwappedBetweenRoles` | swapped texts fail both, and unattributed presence fails |
| `TheComparatorFailsTwoTokensSwappedBetweenRoles` | swapped domain tokens fail |
| `TheComparatorDoesNotAskAHeadlineForARoleLabel` | a headline and one other text need no labels |
| `TheComparatorDoesNotAskAStatedValueForARoleLabel` | a term's stated value needs no label |
| `TheComparatorUsesTheRowsOwnRoleWord` | the header's word is the one required |
| `TheComparatorRefusesTheRightNameUnderTheWrongRole` | Party protection unchanged |
| `TheHiddenBehaviourDetectorTellsTheShapesApart` | auto-properties accepted; expression-bodied, block-bodied and accessor-bodied properties and ToString rejected |
| `EveryAccountedRoleWordIsVisibleOnItsPage` | no role word is invented |

### Population before and after

| | `d1a6e77` | Now |
| --- | --- | --- |
| PRIMARY values | 108 | **110** |
| PRIMARY-failing templates | 70 | **70** |
| SECONDARY total | 18 | **18** |
| SECONDARY unwired | 17 | **17** |

Deltas:

- **+2, exposed by role identity.** `ContractsPage#ReconcileList` `Negotiated.DisplayValue` and
  `DealsPage#ComparisonList` `Previous.DisplayValue`. Their values were already in the
  announcement, without the column's role word, so `d1a6e77` counted them covered.
- **9 re-typed from Date to Instant, count unchanged.** `VersionList.RecordedAt`,
  `StepList.OccurredAt`, `MailboxList.LastSyncedAt`, `FinancePage#HistoryList.OccurredAt`,
  `MessageList.OccurredAt`, `RunList.StartedAt`, `TimelineEntryTemplate.OccurredAt`,
  `DocumentsPage#HistoryList.OccurredAt`, `ApprovalList.ExpiresAt`. All were already failing,
  because `RowLabel` announces no dates; they are now held to date, time and offset.
- **Surrogate hardening:** no change.

`BalanceList` still passes with no exemption, and all 112 templates are still executed. No
further methodology change is intended: this population is the authority for product repair.

## 8. What this is not

- Not a repair. `RowLabel`, row names, secondary channels, visible dates and `BalanceList`
  markup are unchanged. The only production change on the branch is the behaviour-preserving
  `IsoDate` extraction, and its documentation now describes the product as it is.
- Not FAST green. The primary and secondary gaps above are red by design.
- Not C3, C7 or C9. No release-candidate run, no C7 chain, no negative control, no Build 96.
