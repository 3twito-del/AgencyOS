# Audit 002 Phase C — every dialog, and what it did

One row per declared dialog, from the latest reading of each. `Enter` is the
button the markup declares as the dialog's default action, read rather than
pressed — see [`AUDIT-002C-KEYBOARD.md`](AUDIT-002C-KEYBOARD.md).

The eight unopened dialogs carry their cause rather than the word "blocked";
each is explained in [`AUDIT-002C-BLOCKERS.md`](AUDIT-002C-BLOCKERS.md).

| Dialog | Outcome | How | Focus in | Tab stays | Shift+Tab stays | Escape | Focus restored | Enter |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `AddCreditDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Primary |
| `AddIntelligenceSubjectDialog` | **nothing constructs it** | — | — | — | — | — | — | — |
| `AddMaterialDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Primary |
| `AddMemberDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `AddOpportunityTargetDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `AddPackageElementDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `AddProjectCompanyDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Primary |
| `AddProjectRoleDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `AddRadarEntryDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `AllocatePaymentDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Close |
| `AnswerOfferDialog` | opened | PALETTE | yes | yes | yes | yes | yes | ? |
| `ApproveAiActionDialog` | **PRECONDITION_NOT_MET** | — | — | — | — | — | — | — |
| `AttachToRoleDialog` | opened | BUTTON | yes | yes | yes | yes | yes | ? |
| `CalculateCommissionDialog` | **nothing constructs it** | — | — | — | — | — | — | — |
| `ChangeMemberRoleDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `ChangePackageStatusDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `ChangeProjectStageDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `ChangeVerificationDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `ComposeMessageDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `ConnectCompanyDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `ConnectMailboxDialog` | **PRECONDITION_NOT_MET** | — | — | — | — | — | — | — |
| `ConvertProspectDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Close |
| `CreateCommissionRuleDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `CreateContractDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `CreateDealDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `CreateOpportunityDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `CreatePackageDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `CreatePredictionDialog` | **INCONCLUSIVE** | — | — | — | — | — | — | — |
| `CreateProjectDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `CreateThesisDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `CreateWatchlistDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `FinanceReasonDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Close |
| `IngestAttachmentDialog` | **HARNESS_LIMITATION** | — | — | — | — | — | — | — |
| `IntelligenceReasonDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `LinkRecordDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `LinkResearchItemDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `MailboxVisibilityDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Close |
| `MoveTargetDialog` | opened | PALETTE | yes | yes | yes | yes | yes | ? |
| `NewCompanyDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `NewPersonDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `OpenResearchCaseDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `PostJournalEntryDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `RaiseReceivableDialog` | **nothing constructs it** | — | — | — | — | — | — | — |
| `RecordAdjustmentDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `RecordContractVersionDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Primary |
| `RecordDocumentDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Close |
| `RecordForecastDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `RecordInteractionDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `RecordInvoiceDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `RecordMonetaryObligationDialog` | **nothing constructs it** | — | — | — | — | — | — | — |
| `RecordNoticeDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Primary |
| `RecordOfferDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Primary |
| `RecordPaymentDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `RecordPitchDialog` | opened | PALETTE | yes | yes | yes | yes | yes | ? |
| `RecordSignalDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `RecordSignatureDialog` | **PRECONDITION_NOT_MET** | — | — | — | — | — | — | — |
| `RecordSourceDialog` | **INCONCLUSIVE** | — | — | — | — | — | — | — |
| `RecordSubmissionDialog` | opened | BUTTON | yes | yes | yes | yes | yes | ? |
| `ResolveObligationDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
| `ResolveOptionDialog` | opened | BUTTON | yes | yes | yes | yes | yes | Close |
| `ResolveParticipantDialog` | **HARNESS_LIMITATION** | — | — | — | — | — | — | — |
| `ResolvePredictionDialog` | **INCONCLUSIVE** | — | — | — | — | — | — | — |
| `ReviseThesisDialog` | opened | PALETTE | yes | yes | yes | yes | yes | Close |
