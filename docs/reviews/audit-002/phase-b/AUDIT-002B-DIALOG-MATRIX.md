# Audit 002 Phase B — dialog matrix

**61 dialogs.** Opened in Phase B: **38** (of which **8** newly unblocked by Phase B's fixture and harness work). Still blocked: **19**. Unreachable: **4**.

Every dialog was opened from the real interface. None was constructed directly.

| Dialog | Workspace | Purpose | Phase B | How | Fields | Raw ids | Focus in | Escape | Tab stays | A11y |
| --- | --- | --- | --- | --- | ---: | ---: | --- | --- | --- | ---: |
| `AddCreditDialog` | talent | Create | OPENED | PALETTE | 6 | 0 | yes | yes | yes | 0 |
| `AddIntelligenceSubjectDialog` | - | Create | UNREACHABLE | NONE | 3 | 1 | - | - | - | - |
| `AddMaterialDialog` | talent | Create | OPENED | PALETTE | 5 | 0 | yes | yes | yes | 0 |
| `AddOpportunityTargetDialog` | pipeline | Create | OPENED | BUTTON | 5 | 2 | yes | yes | yes | 0 |
| `AddPackageElementDialog` | packages | Create | OPENED_NOW | BUTTON | 3 | 1 | yes | yes | yes | 0 |
| `AddProjectCompanyDialog` | projects | Create | OPENED | PALETTE | 4 | 1 | yes | yes | yes | 0 |
| `AddProjectRoleDialog` | projects | Create | OPENED | BUTTON | 4 | 0 | yes | yes | yes | 0 |
| `AddRadarEntryDialog` | intelligence | Create | OPENED | PALETTE | 5 | 0 | yes | yes | yes | 0 |
| `AllocatePaymentDialog` | finance | Action | HARNESS_BLOCKED | NONE | 2 | 0 | - | - | - | - |
| `AnswerOfferDialog` | deals | Action | OPENED | PALETTE | 4 | 0 | yes | yes | yes | 0 |
| `ApproveAiActionDialog` | ai | Action | STILL_BLOCKED_FIXTURE | NONE | 1 | 0 | - | - | - | - |
| `AttachToRoleDialog` | projects | Action | OPENED_NOW | BUTTON | 6 | 1 | yes | yes | yes | 0 |
| `CalculateCommissionDialog` | - | Action | UNREACHABLE | NONE | 3 | 0 | - | - | - | - |
| `ChangePackageStatusDialog` | packages | Action | OPENED_NOW | BUTTON | 2 | 0 | yes | yes | yes | 0 |
| `ChangeProjectStageDialog` | projects | Action | OPENED | BUTTON | 2 | 0 | yes | yes | yes | 0 |
| `ChangeVerificationDialog` | intelligence | Action | HARNESS_BLOCKED | NONE | 2 | 0 | - | - | - | - |
| `ComposeMessageDialog` | communications | Action | OPENED_NOW | PALETTE | 5 | 0 | yes | yes | yes | 0 |
| `ConnectCompanyDialog` | people | Action | OPENED | BUTTON | 3 | 0 | yes | yes | yes | 0 |
| `ConnectMailboxDialog` | communications | Action | STILL_BLOCKED_FIXTURE | NONE | 4 | 0 | - | - | - | - |
| `ConvertProspectDialog` | prospects | Action | OPENED | BUTTON | 11 | 0 | yes | yes | yes | 0 |
| `CreateCommissionRuleDialog` | finance | Create | OPENED | PALETTE | 9 | 0 | yes | yes | yes | 0 |
| `CreateContractDialog` | contracts | Create | OPENED | BUTTON | 10 | 3 | yes | yes | yes | 0 |
| `CreateDealDialog` | deals | Create | OPENED | BUTTON | 8 | 3 | yes | yes | yes | 0 |
| `CreateOpportunityDialog` | pipeline | Create | OPENED | BUTTON | 7 | 2 | yes | yes | yes | 0 |
| `CreatePackageDialog` | packages | Create | OPENED | BUTTON | 5 | 2 | yes | yes | yes | 0 |
| `CreatePredictionDialog` | intelligence | Create | HARNESS_BLOCKED | NONE | 6 | 0 | - | - | - | - |
| `CreateProjectDialog` | projects | Create | OPENED | BUTTON | 6 | 0 | yes | yes | yes | 0 |
| `CreateThesisDialog` | intelligence | Create | OPENED | PALETTE | 5 | 0 | yes | yes | yes | 0 |
| `CreateWatchlistDialog` | intelligence | Create | OPENED | PALETTE | 3 | 0 | yes | yes | yes | 0 |
| `FinanceReasonDialog` | communications,documents,finance | Action | HARNESS_BLOCKED | NONE | 1 | 0 | - | - | - | - |
| `IngestAttachmentDialog` | communications | Action | STILL_BLOCKED_FIXTURE | NONE | 3 | 0 | - | - | - | - |
| `IntelligenceReasonDialog` | intelligence | Action | HARNESS_BLOCKED | NONE | 1 | 0 | - | - | - | - |
| `LinkRecordDialog` | communications,documents | Action | HARNESS_BLOCKED | NONE | 3 | 1 | - | - | - | - |
| `LinkResearchItemDialog` | intelligence | Action | HARNESS_BLOCKED | NONE | 3 | 1 | - | - | - | - |
| `MailboxVisibilityDialog` | communications | Action | STILL_BLOCKED_FIXTURE | NONE | 0 | 0 | - | - | - | - |
| `MoveTargetDialog` | pipeline | Action | OPENED | PALETTE | 2 | 0 | yes | yes | yes | 0 |
| `NewCompanyDialog` | companies | Action | OPENED | BUTTON | 5 | 0 | yes | yes | yes | 0 |
| `NewPersonDialog` | people | Action | OPENED | BUTTON | 6 | 0 | yes | yes | yes | 0 |
| `OpenResearchCaseDialog` | intelligence | Action | OPENED | PALETTE | 3 | 0 | yes | yes | yes | 0 |
| `PostJournalEntryDialog` | finance | Action | OPENED | PALETTE | 6 | 0 | yes | yes | yes | 0 |
| `RaiseReceivableDialog` | - | Create | UNREACHABLE | NONE | 6 | 0 | - | - | - | - |
| `RecordAdjustmentDialog` | finance | Create | OPENED_NOW | PALETTE | 6 | 0 | yes | yes | yes | 0 |
| `RecordContractVersionDialog` | contracts | Create | OPENED_NOW | PALETTE | 8 | 0 | **no** | yes | **no** | 0 |
| `RecordDocumentDialog` | documents | Create | OPENED | BUTTON | 5 | 0 | yes | yes | yes | 0 |
| `RecordForecastDialog` | intelligence | Create | HARNESS_BLOCKED | NONE | 2 | 0 | - | - | - | - |
| `RecordInteractionDialog` | people | Create | OPENED | BUTTON | 8 | 0 | yes | yes | yes | 0 |
| `RecordInvoiceDialog` | finance | Create | OPENED_NOW | PALETTE | 6 | 0 | yes | yes | yes | 0 |
| `RecordMonetaryObligationDialog` | - | Create | UNREACHABLE | NONE | 14 | 0 | - | - | - | - |
| `RecordNoticeDialog` | contracts | Create | OPENED_NOW | PALETTE | 8 | 0 | yes | yes | yes | 0 |
| `RecordOfferDialog` | deals | Create | OPENED | BUTTON | 10 | 0 | **no** | yes | **no** | 0 |
| `RecordPaymentDialog` | finance | Create | OPENED | PALETTE | 10 | 0 | yes | yes | yes | 0 |
| `RecordPitchDialog` | pipeline | Create | OPENED | PALETTE | 9 | 1 | yes | yes | yes | 0 |
| `RecordSignalDialog` | intelligence | Create | OPENED | PALETTE | 12 | 0 | yes | yes | yes | 0 |
| `RecordSignatureDialog` | contracts | Create | HARNESS_BLOCKED | NONE | 5 | 0 | - | - | - | - |
| `RecordSourceDialog` | intelligence | Create | HARNESS_BLOCKED | NONE | 10 | 0 | - | - | - | - |
| `RecordSubmissionDialog` | pipeline | Create | OPENED | BUTTON | 8 | 1 | yes | yes | yes | 0 |
| `ResolveObligationDialog` | contracts | Action | HARNESS_BLOCKED | NONE | 5 | 0 | - | - | - | - |
| `ResolveOptionDialog` | contracts | Action | HARNESS_BLOCKED | NONE | 5 | 0 | - | - | - | - |
| `ResolveParticipantDialog` | communications | Action | STILL_BLOCKED_FIXTURE | NONE | 0 | 0 | - | - | - | - |
| `ResolvePredictionDialog` | intelligence | Action | HARNESS_BLOCKED | NONE | 2 | 0 | - | - | - | - |
| `ReviseThesisDialog` | intelligence | Action | HARNESS_BLOCKED | NONE | 4 | 0 | - | - | - | - |

---

## Newly opened in Phase B

- `AddPackageElementDialog` (packages, BUTTON)
- `AttachToRoleDialog` (projects, BUTTON)
- `ChangePackageStatusDialog` (packages, BUTTON)
- `ComposeMessageDialog` (communications, PALETTE)
- `RecordAdjustmentDialog` (finance, PALETTE)
- `RecordContractVersionDialog` (contracts, PALETTE)
- `RecordInvoiceDialog` (finance, PALETTE)
- `RecordNoticeDialog` (contracts, PALETTE)

## Still blocked

| Dialog | Workspace | Classification | What happened |
| --- | --- | --- | --- |
| `AllocatePaymentDialog` | finance | HARNESS_BLOCKED | FinancePage: ran payment.allocate from the palette; 2 row(s) selected first |
| `ApproveAiActionDialog` | ai | STILL_BLOCKED_FIXTURE | AiPage: no tab made the opener available; 1 row(s) selected first |
| `ChangeVerificationDialog` | intelligence | HARNESS_BLOCKED | IntelligencePage: ran intelligence.signal.verification from the palette; 1 row(s) selected first |
| `ConnectMailboxDialog` | communications | STILL_BLOCKED_FIXTURE | CommunicationsPage: ran mailbox.connect from the palette; 2 row(s) selected first |
| `CreatePredictionDialog` | intelligence | HARNESS_BLOCKED | IntelligencePage: ran intelligence.prediction.create from the palette; 1 row(s) selected first |
| `FinanceReasonDialog` | communications,documents,finance | HARNESS_BLOCKED | CommunicationsPage: ran mailbox.disconnect from the palette; 2 row(s) selected first | DocumentsPage |
| `IngestAttachmentDialog` | communications | STILL_BLOCKED_FIXTURE | CommunicationsPage: ran attachment.ingest from the palette; 2 row(s) selected first |
| `IntelligenceReasonDialog` | intelligence | HARNESS_BLOCKED | IntelligencePage: ran intelligence.thesis.retire from the palette; 1 row(s) selected first |
| `LinkRecordDialog` | communications,documents | HARNESS_BLOCKED | CommunicationsPage: ran message.link from the palette; 2 row(s) selected first | DocumentsPage: ran  |
| `LinkResearchItemDialog` | intelligence | HARNESS_BLOCKED | IntelligencePage: ran intelligence.research.link from the palette; 1 row(s) selected first |
| `MailboxVisibilityDialog` | communications | STILL_BLOCKED_FIXTURE | CommunicationsPage: ran mailbox.visibility from the palette; 2 row(s) selected first |
| `RecordForecastDialog` | intelligence | HARNESS_BLOCKED | IntelligencePage: ran intelligence.prediction.forecast from the palette; 1 row(s) selected first |
| `RecordSignatureDialog` | contracts | HARNESS_BLOCKED | ContractsPage: ran contract.signature.record from the palette; 3 row(s) selected first |
| `RecordSourceDialog` | intelligence | HARNESS_BLOCKED | IntelligencePage: ran intelligence.source.record from the palette; 1 row(s) selected first |
| `ResolveObligationDialog` | contracts | HARNESS_BLOCKED | ContractsPage: ran obligation.resolve from the palette; 3 row(s) selected first |
| `ResolveOptionDialog` | contracts | HARNESS_BLOCKED | ContractsPage: ran option.resolve from the palette; 3 row(s) selected first |
| `ResolveParticipantDialog` | communications | STILL_BLOCKED_FIXTURE | CommunicationsPage: ran message.participant.resolve from the palette; 2 row(s) selected first |
| `ResolvePredictionDialog` | intelligence | HARNESS_BLOCKED | IntelligencePage: ran intelligence.prediction.resolve from the palette; 1 row(s) selected first |
| `ReviseThesisDialog` | intelligence | HARNESS_BLOCKED | IntelligencePage: ran intelligence.thesis.revise from the palette; 1 row(s) selected first |

## Unreachable

- `AddIntelligenceSubjectDialog` — nothing constructs it (`AOS-R001-017`)
- `CalculateCommissionDialog` — nothing constructs it (`AOS-R001-017`)
- `RaiseReceivableDialog` — nothing constructs it (`AOS-R001-017`)
- `RecordMonetaryObligationDialog` — nothing constructs it (`AOS-R001-017`)
