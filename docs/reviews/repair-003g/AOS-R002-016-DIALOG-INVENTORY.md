# `AOS-R002-016` — the 65-dialog keyboard inventory

Every dialog in the product, what its buttons say, what `Enter` used to do and
what it does now. Produced before any default was changed, then regenerated after,
so the "Was" column is measured rather than remembered.

**Convention (owner decision):** for an ordinary dialog whose primary button is the
normal affirmative create/save/apply action, **`Enter` commits**. Departures are
explicit, classified and reasoned; the list of them is enforced by
`EnterConventionTests`.

| Count | |
| --- | --- |
| Dialogs | **65** |
| `Enter` commits (`Primary`) | **61** |
| Explicit exceptions | **4** |
| Defaults changed by this decision | **36** |

Before this decision: 28 committed, 36 cancelled, 1 rejected — following no rule.

## The four exceptions

| Dialog | Class | `Enter` now | Why |
| --- | --- | --- | --- |
| `ApproveAiActionDialog` | `APPROVAL` | rejects (`Secondary`) | Its primary is "Approve and run", which executes a tool. The product had already defaulted away from it deliberately, and the finding named this as the one clearly considered case. Rejecting is the safe answer to a proposal. |
| `PostJournalEntryDialog` | `IRREVERSIBLE_OR_EXTERNAL` | nothing (`None`) | The domain's own words: *"a posted entry is immutable and counts; a reversed entry still counts, because the money moved and then moved back and both movements happened."* A post cannot be un-posted, only answered by a further entry. |
| `ConnectMailboxDialog` | `IRREVERSIBLE_OR_EXTERNAL` | nothing (`None`) | Connecting hands an authorization code to an external provider and opens a live connection to somebody's mailbox. The effect leaves the product. |
| `ConvertProspectDialog` | `IRREVERSIBLE_OR_EXTERNAL` | nothing (`None`) | Its primary is "Sign". It creates the representation and closes the pursuit in one transaction — the agency taking somebody on as a client — and the prospect stops being a prospect. |

### Why three of them are `None` rather than `Close`

The first attempt left them on `Close`, which is what their markup already said.
A live run showed what that meant: **`Enter` in a half-filled journal entry
dismissed the dialog and threw the entry away.** Not committing was the
requirement; discarding is not a way to meet it, and it is the same complaint
`AOS-R002-016` makes — one gesture that saves in one dialog and destroys work in
another.

`None` makes `Enter` do nothing at all, so the primary action is reached
deliberately. Measured afterwards: `Enter` in `PostJournalEntryDialog` leaves the
dialog open with its fields intact.

## Controls that consume `Enter` themselves

The convention is dialog-level and does not override a control that has its own
use for the key. Verified live rather than assumed:

| Control | What `Enter` does | Dialog commits? |
| --- | --- | --- |
| Multiline `TextBox` (`NotesBox`, `AcceptsReturn="True"`) | edits the text | **no** |
| `ComboBox` with its list open (`TypeBox`) | selects the highlighted item | **no** |
| `ComboBox`, closed (`StageBox`) | opens the list | **no** |
| Single-line `TextBox` (`ReasonBox`, `FirstNameBox`) | — | **yes** |

This is ordinary WinUI default-button behaviour; nothing was special-cased to
achieve it.

## Every dialog

| Dialog | Primary | Secondary | Close | Was | Now | Class |
| --- | --- | --- | --- | --- | --- | --- |
| `AddCreditDialog` | Add | — | Cancel | Primary | Primary |  |
| `AddIntelligenceSubjectDialog` | Add | — | Cancel | Close | **Primary** |  |
| `AddMaterialDialog` | Add | — | Cancel | Primary | Primary |  |
| `AddMemberDialog` | Add | — | Cancel | Primary | Primary |  |
| `AddOpportunityTargetDialog` | Add | — | Cancel | Primary | Primary |  |
| `AddPackageElementDialog` | Add | — | Cancel | Primary | Primary |  |
| `AddProjectCompanyDialog` | Record | — | Cancel | Primary | Primary |  |
| `AddProjectRoleDialog` | Add | — | Cancel | Primary | Primary |  |
| `AddRadarEntryDialog` | Add | — | Cancel | Close | **Primary** |  |
| `AllocatePaymentDialog` | Allocate | — | Cancel | Close | **Primary** |  |
| `AnswerOfferDialog` | Record | — | Cancel | Close | **Primary** |  |
| `ApproveAiActionDialog` | Approve and run | Reject | Decide later | Secondary | Secondary | APPROVAL |
| `AttachToRoleDialog` | Attach | — | Cancel | Primary | Primary |  |
| `CalculateCommissionDialog` | Calculate | — | Cancel | Close | **Primary** |  |
| `ChangeMemberRoleDialog` | Change role | — | Cancel | Primary | Primary |  |
| `ChangePackageStatusDialog` | Change | — | Cancel | Primary | Primary |  |
| `ChangeProjectStageDialog` | Change | — | Cancel | Primary | Primary |  |
| `ChangeRepresentationScopeDialog` | Save | — | Cancel | Primary | Primary |  |
| `ChangeRepresentationTeamDialog` | Save | — | Cancel | Primary | Primary |  |
| `ChangeVerificationDialog` | Record | — | Cancel | Close | **Primary** |  |
| `ComposeMessageDialog` | Save draft | — | Cancel | Close | **Primary** |  |
| `ConnectCompanyDialog` | Connect | — | Cancel | Primary | Primary |  |
| `ConnectMailboxDialog` | Connect | — | Cancel | Close | **None** | IRREVERSIBLE_OR_EXTERNAL |
| `ConvertProspectDialog` | Sign | — | Cancel | Close | **None** | IRREVERSIBLE_OR_EXTERNAL |
| `CreateCommissionRuleDialog` | Create | — | Cancel | Close | **Primary** |  |
| `CreateContractDialog` | Open | — | Cancel | Primary | Primary |  |
| `CreateDealDialog` | Open | — | Cancel | Primary | Primary |  |
| `CreateOpportunityDialog` | Create | — | Cancel | Primary | Primary |  |
| `CreatePackageDialog` | Create | — | Cancel | Primary | Primary |  |
| `CreatePredictionDialog` | State it | — | Cancel | Close | **Primary** |  |
| `CreateProjectDialog` | Create | — | Cancel | Primary | Primary |  |
| `CreateThesisDialog` | State it | — | Cancel | Close | **Primary** |  |
| `CreateWatchlistDialog` | Create | — | Cancel | Close | **Primary** |  |
| `FinanceReasonDialog` | Record | — | Cancel | Close | **Primary** |  |
| `IngestAttachmentDialog` | Store | — | Cancel | Close | **Primary** |  |
| `IntelligenceReasonDialog` | Record | — | Cancel | Close | **Primary** |  |
| `LinkRecordDialog` | Link | — | Cancel | Close | **Primary** |  |
| `LinkResearchItemDialog` | Attach | — | Cancel | Close | **Primary** |  |
| `MailboxVisibilityDialog` | Change | — | Cancel | Close | **Primary** |  |
| `MoveTargetDialog` | Move | — | Cancel | Primary | Primary |  |
| `NewCompanyDialog` | Create | — | Cancel | Primary | Primary |  |
| `NewPersonDialog` | Create | — | Cancel | Primary | Primary |  |
| `OpenResearchCaseDialog` | Open | — | Cancel | Close | **Primary** |  |
| `PostJournalEntryDialog` | Post | — | Cancel | Close | **None** | IRREVERSIBLE_OR_EXTERNAL |
| `RaiseReceivableDialog` | Raise | — | Cancel | Close | **Primary** |  |
| `RecordAdjustmentDialog` | Record | — | Cancel | Close | **Primary** |  |
| `RecordContractVersionDialog` | Record | — | Cancel | Primary | Primary |  |
| `RecordDocumentDialog` | Record | — | Cancel | Close | **Primary** |  |
| `RecordForecastDialog` | Record | — | Cancel | Close | **Primary** |  |
| `RecordInteractionDialog` | Record | — | Cancel | Primary | Primary |  |
| `RecordInvoiceDialog` | Record | — | Cancel | Close | **Primary** |  |
| `RecordMonetaryObligationDialog` | Record | — | Cancel | Close | **Primary** |  |
| `RecordNoticeDialog` | Record | — | Cancel | Primary | Primary |  |
| `RecordOfferDialog` | Record | — | Cancel | Primary | Primary |  |
| `RecordPaymentDialog` | Record payment | — | Cancel | Close | **Primary** |  |
| `RecordPitchDialog` | Record | — | Cancel | Primary | Primary |  |
| `RecordSignalDialog` | Record | — | Cancel | Close | **Primary** |  |
| `RecordSignatureDialog` | Record | — | Cancel | Close | **Primary** |  |
| `RecordSourceDialog` | Record | — | Cancel | Close | **Primary** |  |
| `RecordSubmissionDialog` | Record | — | Cancel | Primary | Primary |  |
| `ResolveObligationDialog` | Record | — | Cancel | Close | **Primary** |  |
| `ResolveOptionDialog` | Record | — | Cancel | Close | **Primary** |  |
| `ResolveParticipantDialog` | Record | Leave unidentified | Cancel | Close | **Primary** |  |
| `ResolvePredictionDialog` | Resolve | — | Cancel | Close | **Primary** |  |
| `ReviseThesisDialog` | Record the revision | — | Cancel | Close | **Primary** |  |

total 65 | primary 61 | exceptions 4 | changed 36
