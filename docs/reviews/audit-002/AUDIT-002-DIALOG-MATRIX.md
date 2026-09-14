# Audit 002 — dialog matrix

Every dialog the client declares, what reaches it, and what this audit did with it.

`OPENED` means opened from the real interface — navigate, satisfy the precondition, press the control the markup wires to the handler, or run the command from the palette. No dialog was constructed directly.

**61 dialogs.** Opened **30**, blocked **27**, not applicable **4**.

| Dialog | Workspace | Purpose | Reachability | Opened | How | Cancel/Escape | Focus | A11y | Fields | Raw ids | Mutation | Idem | Tests |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | ---: | ---: | --- | --- | ---: |
| `AddCreditDialog` | talent | Create | REACHABLE | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 6 | 0 | `AddCreditAsync` | no | 0 |
| `AddIntelligenceSubjectDialog` | - | Create | UNREACHABLE_DEFECT | N/A | - | BLOCKED | BLOCKED | BLOCKED | 3 | 1 | `-` | no | 0 |
| `AddMaterialDialog` | talent | Create | REACHABLE | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 5 | 0 | `AddMaterialAsync` | no | 0 |
| `AddOpportunityTargetDialog` | pipeline | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 5 | 2 | `AddOpportunityTargetAsync` | yes | 0 |
| `AddPackageElementDialog` | packages | Create | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 3 | 1 | `AddPackageElementAsync` | yes | 0 |
| `AddProjectCompanyDialog` | projects | Create | REACHABLE | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 4 | 1 | `AddProjectCompanyAsync` | yes | 0 |
| `AddProjectRoleDialog` | projects | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 4 | 0 | `CreateProjectRoleAsync` | yes | 0 |
| `AddRadarEntryDialog` | intelligence | Create | REACHABLE_WITH_PRECONDITION | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 5 | 0 | `-` | no | 0 |
| `AllocatePaymentDialog` | finance | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 2 | 0 | `AllocatePaymentAsync` | yes | 0 |
| `AnswerOfferDialog` | deals | Action | REACHABLE | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 4 | 0 | `AnswerOfferAsync` | yes | 0 |
| `ApproveAiActionDialog` | ai | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 1 | 0 | `-` | no | 0 |
| `AttachToRoleDialog` | projects | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 6 | 1 | `AttachToRoleAsync` | yes | 0 |
| `CalculateCommissionDialog` | - | Action | UNREACHABLE_DEFECT | N/A | - | BLOCKED | BLOCKED | BLOCKED | 3 | 0 | `-` | no | 1 |
| `ChangePackageStatusDialog` | packages | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 2 | 0 | `ChangePackageStatusAsync` | yes | 0 |
| `ChangeProjectStageDialog` | projects | Action | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 2 | 0 | `ChangeProjectStageAsync` | yes | 0 |
| `ChangeVerificationDialog` | intelligence | Action | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 2 | 0 | `-` | no | 0 |
| `ComposeMessageDialog` | communications | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 5 | 0 | `-` | yes | 0 |
| `ConnectCompanyDialog` | people | Action | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 3 | 0 | `-` | no | 0 |
| `ConnectMailboxDialog` | communications | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 4 | 0 | `-` | yes | 0 |
| `ConvertProspectDialog` | prospects | Action | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 11 | 0 | `-` | no | 0 |
| `CreateCommissionRuleDialog` | finance | Create | REACHABLE | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 9 | 0 | `CreateCommissionRuleAsync` | yes | 0 |
| `CreateContractDialog` | contracts | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 10 | 3 | `CreateContractAsync` | yes | 0 |
| `CreateDealDialog` | deals | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 8 | 3 | `CreateDealAsync` | yes | 1 |
| `CreateOpportunityDialog` | pipeline | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 7 | 2 | `CreateOpportunityAsync` | yes | 0 |
| `CreatePackageDialog` | packages | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 5 | 2 | `CreatePackageAsync` | yes | 0 |
| `CreatePredictionDialog` | intelligence | Create | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 6 | 0 | `-` | no | 0 |
| `CreateProjectDialog` | projects | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 6 | 0 | `CreateProjectAsync` | yes | 0 |
| `CreateThesisDialog` | intelligence | Create | REACHABLE_WITH_PRECONDITION | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 5 | 0 | `-` | no | 0 |
| `CreateWatchlistDialog` | intelligence | Create | REACHABLE_WITH_PRECONDITION | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 3 | 0 | `-` | no | 0 |
| `FinanceReasonDialog` | communications,documents,finance | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 1 | 0 | `-` | yes | 0 |
| `IngestAttachmentDialog` | communications | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 3 | 0 | `-` | yes | 0 |
| `IntelligenceReasonDialog` | intelligence | Action | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 1 | 0 | `-` | no | 0 |
| `LinkRecordDialog` | communications,documents | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 3 | 1 | `-` | yes | 0 |
| `LinkResearchItemDialog` | intelligence | Action | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 3 | 1 | `-` | no | 0 |
| `MailboxVisibilityDialog` | communications | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 0 | 0 | `-` | yes | 0 |
| `MoveTargetDialog` | pipeline | Action | REACHABLE | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 2 | 0 | `MoveOpportunityTargetAsync` | yes | 0 |
| `NewCompanyDialog` | companies | Action | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 5 | 0 | `CreateCompanyAsync` | no | 0 |
| `NewPersonDialog` | people | Action | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 6 | 0 | `-` | no | 1 |
| `OpenResearchCaseDialog` | intelligence | Action | REACHABLE_WITH_PRECONDITION | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 3 | 0 | `-` | no | 0 |
| `PostJournalEntryDialog` | finance | Action | REACHABLE | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 6 | 0 | `PostJournalEntryAsync` | yes | 0 |
| `RaiseReceivableDialog` | - | Create | UNREACHABLE_DEFECT | N/A | - | BLOCKED | BLOCKED | BLOCKED | 6 | 0 | `-` | no | 0 |
| `RecordAdjustmentDialog` | finance | Create | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 6 | 0 | `RecordAdjustmentAsync` | yes | 0 |
| `RecordContractVersionDialog` | contracts | Create | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 8 | 0 | `RecordContractVersionAsync` | yes | 0 |
| `RecordDocumentDialog` | documents | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 5 | 0 | `-` | yes | 0 |
| `RecordForecastDialog` | intelligence | Create | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 2 | 0 | `-` | no | 0 |
| `RecordInteractionDialog` | people | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 8 | 0 | `-` | no | 0 |
| `RecordInvoiceDialog` | finance | Create | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 6 | 0 | `RecordInvoiceAsync` | yes | 0 |
| `RecordMonetaryObligationDialog` | - | Create | UNREACHABLE_DEFECT | N/A | - | BLOCKED | BLOCKED | BLOCKED | 14 | 0 | `-` | no | 0 |
| `RecordNoticeDialog` | contracts | Create | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 8 | 0 | `RecordNoticeAsync` | yes | 0 |
| `RecordOfferDialog` | deals | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 10 | 0 | `RecordOfferAsync` | yes | 0 |
| `RecordPaymentDialog` | finance | Create | REACHABLE | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 10 | 0 | `RecordPaymentAsync` | yes | 0 |
| `RecordPitchDialog` | pipeline | Create | REACHABLE | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 9 | 1 | `RecordPitchAsync` | yes | 0 |
| `RecordSignalDialog` | intelligence | Create | REACHABLE_WITH_PRECONDITION | COMPLETE | PALETTE | COMPLETE | COMPLETE | COMPLETE | 12 | 0 | `-` | no | 0 |
| `RecordSignatureDialog` | contracts | Create | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 5 | 0 | `RecordContractSignatureAsync` | yes | 0 |
| `RecordSourceDialog` | intelligence | Create | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 10 | 0 | `-` | no | 0 |
| `RecordSubmissionDialog` | pipeline | Create | REACHABLE | COMPLETE | BUTTON | COMPLETE | COMPLETE | COMPLETE | 8 | 1 | `RecordSubmissionAsync` | yes | 0 |
| `ResolveObligationDialog` | contracts | Action | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 5 | 0 | `ResolveObligationAsync` | yes | 0 |
| `ResolveOptionDialog` | contracts | Action | REACHABLE | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 5 | 0 | `ResolveContractOptionAsync` | yes | 0 |
| `ResolveParticipantDialog` | communications | Action | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 0 | 0 | `-` | yes | 0 |
| `ResolvePredictionDialog` | intelligence | Action | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 2 | 0 | `-` | no | 0 |
| `ReviseThesisDialog` | intelligence | Action | REACHABLE_WITH_PRECONDITION | BLOCKED | - | BLOCKED | BLOCKED | BLOCKED | 4 | 0 | `-` | no | 0 |

---

## Dialogs this audit could not open

Each was attempted down every declared path. The reason is recorded rather than assumed, because a dialog that declines to open because nothing is selected is not the same as one nobody can reach.

| Dialog | Workspace | What happened |
| --- | --- | --- |
| `AddPackageElementDialog` | packages | PackagesPage: ran package.element.add from the palette; 0 row(s) selected first |
| `AllocatePaymentDialog` | finance | FinancePage: ran payment.allocate from the palette; 1 row(s) selected first |
| `ApproveAiActionDialog` | ai | AiPage: no tab made the opener available; 1 row(s) selected first |
| `AttachToRoleDialog` | projects | ProjectsPage: clicked "Attach"; 2 row(s) selected first |
| `ChangePackageStatusDialog` | packages | PackagesPage: no tab made the opener available; 0 row(s) selected first |
| `ChangeVerificationDialog` | intelligence | IntelligencePage: ran intelligence.signal.verification from the palette; 1 row(s) selected first |
| `ComposeMessageDialog` | communications | CommunicationsPage: ran message.compose from the palette; 2 row(s) selected first |
| `ConnectMailboxDialog` | communications | CommunicationsPage: ran mailbox.connect from the palette; 2 row(s) selected first |
| `CreatePredictionDialog` | intelligence | IntelligencePage: ran intelligence.prediction.create from the palette; 1 row(s) selected first |
| `FinanceReasonDialog` | communications,documents,finance | CommunicationsPage: ran mailbox.disconnect from the palette; 2 row(s) selected first | DocumentsPage: ran document.arc |
| `IngestAttachmentDialog` | communications | CommunicationsPage: ran attachment.ingest from the palette; 2 row(s) selected first |
| `IntelligenceReasonDialog` | intelligence | IntelligencePage: ran intelligence.thesis.retire from the palette; 1 row(s) selected first |
| `LinkRecordDialog` | communications,documents | CommunicationsPage: ran message.link from the palette; 2 row(s) selected first | DocumentsPage: ran document.link from |
| `LinkResearchItemDialog` | intelligence | IntelligencePage: ran intelligence.research.link from the palette; 1 row(s) selected first |
| `MailboxVisibilityDialog` | communications | CommunicationsPage: ran mailbox.visibility from the palette; 2 row(s) selected first |
| `RecordAdjustmentDialog` | finance | FinancePage: ran adjustment.record from the palette; 1 row(s) selected first |
| `RecordContractVersionDialog` | contracts | ContractsPage: ran contract.version.record from the palette; 1 row(s) selected first |
| `RecordForecastDialog` | intelligence | IntelligencePage: ran intelligence.prediction.forecast from the palette; 1 row(s) selected first |
| `RecordInvoiceDialog` | finance | FinancePage: ran invoice.record from the palette; 1 row(s) selected first |
| `RecordNoticeDialog` | contracts | ContractsPage: ran notice.record from the palette; 1 row(s) selected first |
| `RecordSignatureDialog` | contracts | ContractsPage: ran contract.signature.record from the palette; 1 row(s) selected first |
| `RecordSourceDialog` | intelligence | IntelligencePage: ran intelligence.source.record from the palette; 1 row(s) selected first |
| `ResolveObligationDialog` | contracts | ContractsPage: ran obligation.resolve from the palette; 1 row(s) selected first |
| `ResolveOptionDialog` | contracts | ContractsPage: ran option.resolve from the palette; 1 row(s) selected first |
| `ResolveParticipantDialog` | communications | CommunicationsPage: ran message.participant.resolve from the palette; 2 row(s) selected first |
| `ResolvePredictionDialog` | intelligence | IntelligencePage: ran intelligence.prediction.resolve from the palette; 1 row(s) selected first |
| `ReviseThesisDialog` | intelligence | IntelligencePage: ran intelligence.thesis.revise from the palette; 1 row(s) selected first |

## Dialogs nothing constructs

`AOS-R001-017`, reconfirmed. No page, command or handler builds these, so no path exists to follow.

- `AddIntelligenceSubjectDialog`
- `CalculateCommissionDialog`
- `RaiseReceivableDialog`
- `RecordMonetaryObligationDialog`
