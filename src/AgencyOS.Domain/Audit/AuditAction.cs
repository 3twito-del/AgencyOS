namespace AgencyOS.Domain.Audit;

/// <summary>
/// Action names recorded in the audit trail.
/// </summary>
/// <remarks>
/// Strings rather than an enum, for the same reason as <c>Permission</c>: audit
/// rows outlive the build that wrote them, and a stored value must stay readable
/// after an enum is renumbered or a member is removed.
/// </remarks>
public static class AuditAction
{
    public const string OrganizationCreated = "organization.created";
    public const string OrganizationArchived = "organization.archived";

    public const string MembershipGranted = "membership.granted";
    public const string MembershipRevoked = "membership.revoked";

    public const string UserRegistered = "user.registered";

    /// <summary>First-run initialization. Occurs at most once in a system's life.</summary>
    public const string SystemBootstrapped = "system.bootstrapped";

    /// <summary>A release policy was published for a platform and ring.</summary>
    public const string ReleasePolicyPublished = "release.policy.published";

    // ---- People vertical slice (M2) ----

    public const string PersonCreated = "person.created";
    public const string PersonUpdated = "person.updated";
    public const string PersonArchived = "person.archived";

    public const string CompanyCreated = "company.created";
    public const string CompanyUpdated = "company.updated";
    public const string CompanyArchived = "company.archived";

    public const string RelationshipCreated = "relationship.created";
    public const string RelationshipEnded = "relationship.ended";

    public const string InteractionRecorded = "interaction.recorded";

    public const string TaskCreated = "task.created";
    public const string TaskCompleted = "task.completed";
    public const string TaskReopened = "task.reopened";

    /// <summary>Somebody was made accountable for a task, or the assignment was cleared.</summary>
    public const string TaskAssigned = "task.assigned";

    // ---- Saved views (M3) ----
    //
    // A saved view holds no business fact, but it is stored user data and
    // deleting it is destructive, so the lifecycle is audited like any other
    // privileged operation.

    // ---- Talent and representation (M4) ----

    public const string TalentProfileCreated = "talentprofile.created";
    public const string TalentProfileUpdated = "talentprofile.updated";
    public const string TalentDisciplineAdded = "talentprofile.discipline.added";
    public const string TalentDisciplineRemoved = "talentprofile.discipline.removed";

    public const string ProspectCreated = "prospect.created";
    public const string ProspectUpdated = "prospect.updated";
    public const string ProspectStageChanged = "prospect.stage.changed";
    public const string ProspectConverted = "prospect.converted";

    public const string RepresentationCreated = "representation.created";
    public const string RepresentationUpdated = "representation.updated";
    public const string RepresentationStatusChanged = "representation.status.changed";
    public const string RepresentationScopeAdded = "representation.scope.added";
    public const string RepresentationScopeEnded = "representation.scope.ended";
    public const string RepresentationTeamAssigned = "representation.team.assigned";
    public const string RepresentationTeamRemoved = "representation.team.removed";

    public const string CreditAdded = "credit.added";
    public const string CreditUpdated = "credit.updated";

    public const string MaterialAdded = "material.added";
    public const string MaterialUpdated = "material.updated";

    // ---- Projects and packaging (M5) ----

    public const string ProjectCreated = "project.created";
    public const string ProjectUpdated = "project.updated";
    public const string ProjectStatusChanged = "project.status.changed";
    public const string ProjectStageChanged = "project.stage.changed";

    public const string SourcePropertyCreated = "sourceproperty.created";
    public const string SourcePropertyUpdated = "sourceproperty.updated";
    public const string SourcePropertyLinked = "project.sourceproperty.linked";
    public const string SourcePropertyUnlinked = "project.sourceproperty.unlinked";

    public const string ProjectRoleCreated = "project.role.created";
    public const string ProjectRoleUpdated = "project.role.updated";
    public const string ProjectRoleClosed = "project.role.closed";

    public const string AttachmentCreated = "attachment.created";
    public const string AttachmentUpdated = "attachment.updated";
    public const string AttachmentStatusChanged = "attachment.status.changed";

    public const string ProjectCompanyParticipationAdded = "project.company.added";
    public const string ProjectCompanyParticipationEnded = "project.company.ended";

    public const string PackageCreated = "package.created";
    public const string PackageUpdated = "package.updated";
    public const string PackageStatusChanged = "package.status.changed";
    public const string PackageElementAdded = "package.element.added";
    public const string PackageElementRemoved = "package.element.removed";

    public const string CreditLinkedToProject = "credit.project.linked";
    public const string CreditUnlinkedFromProject = "credit.project.unlinked";

    public const string MaterialLinkedToProject = "project.material.linked";
    public const string MaterialUnlinkedFromProject = "project.material.unlinked";

    // ---- Opportunities and submissions (M6) ----

    public const string OpportunityCreated = "opportunity.created";
    public const string OpportunityUpdated = "opportunity.updated";
    public const string OpportunityStatusChanged = "opportunity.status.changed";
    public const string OpportunitySubjectAdded = "opportunity.subject.added";
    public const string OpportunitySubjectRemoved = "opportunity.subject.removed";

    public const string OpportunityTargetAdded = "opportunity.target.added";
    public const string OpportunityTargetUpdated = "opportunity.target.updated";
    public const string OpportunityTargetMoved = "opportunity.target.moved";
    public const string OpportunityTargetEventRecorded = "opportunity.target.event";

    public const string SubmissionRecorded = "submission.recorded";
    public const string SubmissionAmended = "submission.amended";

    public const string PitchRecorded = "pitch.recorded";
    public const string PitchAmended = "pitch.amended";

    // ---- Deals and offers (M7) ----

    public const string DealOpened = "deal.opened";
    public const string DealUpdated = "deal.updated";
    public const string DealStatusChanged = "deal.status.changed";
    public const string DealReopened = "deal.reopened";

    public const string OfferDrafted = "offer.drafted";
    public const string OfferRecorded = "offer.recorded";
    public const string OfferTermChanged = "offer.term.changed";

    /// <summary>
    /// The most consequential act in the milestone: this is the moment the agency
    /// asserts commercial terms are agreed.
    /// </summary>
    public const string OfferAccepted = "offer.accepted";

    public const string OfferRejected = "offer.rejected";
    public const string OfferWithdrawn = "offer.withdrawn";
    public const string OfferExpired = "offer.expired";

    // ---- Contracts, rights and obligations (M8) ----

    public const string ContractOpened = "contract.opened";
    public const string ContractUpdated = "contract.updated";
    public const string ContractStatusChanged = "contract.status.changed";
    public const string ContractPartyAdded = "contract.party.added";
    public const string ContractVersionRecorded = "contract.version.recorded";
    public const string ContractTermChanged = "contract.term.changed";
    public const string ContractRelationshipRecorded = "contract.relationship.recorded";

    /// <summary>A party's signature, and with it any advance in execution state.</summary>
    public const string ContractSignatureRecorded = "contract.signature.recorded";

    public const string ContractEffectiveDateRecorded = "contract.effective.recorded";

    public const string RightsGrantRecorded = "rights.grant.recorded";
    public const string RightsGrantSuperseded = "rights.grant.superseded";
    public const string RightsGrantEnded = "rights.grant.ended";

    public const string OptionRecorded = "option.recorded";
    public const string OptionExercised = "option.exercised";
    public const string OptionDeclined = "option.declined";
    public const string OptionWaived = "option.waived";
    public const string OptionExpired = "option.expired";
    public const string OptionCancelled = "option.cancelled";

    public const string ObligationRecorded = "obligation.recorded";
    public const string ObligationSatisfied = "obligation.satisfied";
    public const string ObligationWaived = "obligation.waived";
    public const string ObligationBreachRecorded = "obligation.breach.recorded";
    public const string ObligationReinstated = "obligation.reinstated";
    public const string ObligationCancelled = "obligation.cancelled";

    public const string NoticeRequirementRecorded = "notice.requirement.recorded";
    public const string NoticeRecorded = "notice.recorded";

    // ---- Finance (M9) ----
    //
    // Audit is not the ledger and the ledger is not audit. The ledger says what
    // the books record; this says who did it, under which permission, from which
    // client. Both exist because neither answers the other's question, and a
    // finance history built from audit rows would be a security artefact shown to
    // a bookkeeper (ADR-0012, ADR-0023).

    public const string CommissionRuleCreated = "finance.commission.rule.created";
    public const string CommissionRuleEnded = "finance.commission.rule.ended";
    public const string CommissionCalculated = "finance.commission.calculated";
    public const string CommissionAdjusted = "finance.commission.adjusted";

    public const string MonetaryObligationRecorded = "finance.obligation.recorded";
    public const string MonetaryObligationQuantified = "finance.obligation.quantified";
    public const string MonetaryObligationReleased = "finance.obligation.released";
    public const string MonetaryObligationCancelled = "finance.obligation.cancelled";

    public const string ReceivableRaised = "finance.receivable.raised";
    public const string ReceivableWrittenOff = "finance.receivable.writtenoff";
    public const string ReceivableCancelled = "finance.receivable.cancelled";

    public const string InvoiceRecorded = "finance.invoice.recorded";
    public const string InvoiceIssued = "finance.invoice.issued";
    public const string InvoiceVoided = "finance.invoice.voided";

    public const string PaymentRecorded = "finance.payment.recorded";
    public const string PaymentReversed = "finance.payment.reversed";
    public const string PaymentAllocated = "finance.payment.allocated";
    public const string PaymentAllocationReversed = "finance.payment.allocation.reversed";
    public const string PaymentAdjustmentRecorded = "finance.payment.adjustment.recorded";
    public const string PaymentAdjustmentReversed = "finance.payment.adjustment.reversed";

    public const string JournalEntryPosted = "finance.journal.posted";
    public const string JournalEntryReversed = "finance.journal.reversed";

    // ---- Documents and communications (M10) ----
    //
    // Consequential acts only. Opening a preview, polling a mailbox and typing in a
    // search box are not audited: they would bury the acts that matter under
    // traffic, and an audit trail nobody can read is one nobody reads (ADR-0025).

    public const string DocumentRecorded = "document.recorded";
    public const string DocumentUpdated = "document.updated";
    public const string DocumentVersionAdded = "document.version.added";
    public const string DocumentLinked = "document.linked";
    public const string DocumentUnlinked = "document.unlinked";
    public const string DocumentArchived = "document.archived";
    public const string DocumentRestored = "document.restored";
    public const string DocumentDownloaded = "document.downloaded";

    public const string CommunicationAccountConnected = "communication.account.connected";
    public const string CommunicationAccountDisconnected = "communication.account.disconnected";
    public const string CommunicationAccountVisibilityChanged =
        "communication.account.visibility.changed";

    public const string CommunicationMessageLinked = "communication.message.linked";
    public const string CommunicationMessageUnlinked = "communication.message.unlinked";
    public const string CommunicationParticipantResolved = "communication.participant.resolved";
    public const string CommunicationAttachmentIngested = "communication.attachment.ingested";

    public const string OutboundDispatchComposed = "communication.dispatch.composed";
    public const string OutboundDispatchQueued = "communication.dispatch.queued";
    public const string OutboundDispatchCancelled = "communication.dispatch.cancelled";
    public const string OutboundSendRequested = "communication.send.requested";
    public const string OutboundSendConfirmed = "communication.send.confirmed";
    public const string OutboundSendFailed = "communication.send.failed";
    public const string OutboundOutcomeUnknown = "communication.send.outcome.unknown";
    public const string OutboundReconciled = "communication.send.reconciled";

    // ---- Intelligence (M11) ----
    //
    // Recording what somebody believed, and when they changed their mind. Reads
    // are absent on purpose: opening a thesis or running a calibration query is
    // ordinary work, and auditing it would bury the entries that matter
    // (ADR-0012, ADR-0030).

    public const string IntelligenceSourceRecorded = "intelligence.source.recorded";
    public const string IntelligenceSourceAssessed = "intelligence.source.assessed";
    public const string IntelligenceSourceUpdated = "intelligence.source.updated";

    public const string SignalRecorded = "intelligence.signal.recorded";
    public const string SignalUpdated = "intelligence.signal.updated";
    public const string SignalEvidenceLinked = "intelligence.signal.evidence.linked";
    public const string SignalEvidenceUnlinked = "intelligence.signal.evidence.unlinked";
    public const string SignalCorroborated = "intelligence.signal.corroborated";
    public const string SignalDisputed = "intelligence.signal.disputed";
    public const string SignalRetracted = "intelligence.signal.retracted";

    public const string ThesisCreated = "intelligence.thesis.created";
    public const string ThesisActivated = "intelligence.thesis.activated";
    public const string ThesisRevised = "intelligence.thesis.revised";
    public const string ThesisEvidenceLinked = "intelligence.thesis.evidence.linked";
    public const string ThesisRetired = "intelligence.thesis.retired";
    public const string ThesisSuperseded = "intelligence.thesis.superseded";

    public const string PredictionCreated = "intelligence.prediction.created";
    public const string PredictionRevised = "intelligence.prediction.revised";
    public const string PredictionResolved = "intelligence.prediction.resolved";
    public const string PredictionCancelled = "intelligence.prediction.cancelled";

    public const string WatchlistCreated = "intelligence.watchlist.created";
    public const string WatchlistUpdated = "intelligence.watchlist.updated";
    public const string WatchlistEntryAdded = "intelligence.watchlist.entry.added";
    public const string WatchlistEntryRemoved = "intelligence.watchlist.entry.removed";
    public const string WatchlistReviewed = "intelligence.watchlist.reviewed";

    public const string RadarEntryCreated = "intelligence.radar.created";
    public const string RadarEntryUpdated = "intelligence.radar.updated";
    public const string RadarStatusChanged = "intelligence.radar.status.changed";
    public const string RadarConvertedToProspect = "intelligence.radar.converted";
    public const string RadarDismissed = "intelligence.radar.dismissed";

    public const string ResearchCaseOpened = "intelligence.research.opened";
    public const string ResearchCaseUpdated = "intelligence.research.updated";
    public const string ResearchCaseLinked = "intelligence.research.linked";
    public const string ResearchCaseStatusChanged = "intelligence.research.status.changed";

    // ---- AI runtime (M12) ----
    //
    // Six actions, and the list is short on purpose. What is audited is what a
    // person did or what crossed a boundary: a run started, an approval decided, a
    // canonical write executed because somebody allowed it, and the configuration
    // that decides what leaves the building. Model invocations and read-only tool
    // calls are not here — they belong in the run's own execution history, and an
    // audit trail that grew by a row per model turn would bury the writes it
    // exists to record (§51, ADR-0012, ADR-0031).

    public const string AgentRunStarted = "ai.run.started";
    public const string AgentRunCancelled = "ai.run.cancelled";

    public const string AiApprovalGranted = "ai.approval.granted";
    public const string AiApprovalRejected = "ai.approval.rejected";

    /// <summary>
    /// A canonical command ran because a person approved an AI proposal.
    /// </summary>
    /// <remarks>
    /// Recorded in addition to the underlying command's own audit entry, not
    /// instead of it. The command's entry names the human actor, because a human
    /// authorized it; this one records that the proposal came from a run, which is
    /// the provenance a reviewer needs six months later (§23, §51).
    /// </remarks>
    public const string AiProposedWriteExecuted = "ai.proposal.executed";

    /// <summary>
    /// Authorized context was disclosed to a user's workstation.
    /// </summary>
    /// <remarks>
    /// Recorded because it is the one fact a reviewer cannot reconstruct
    /// afterwards from anything else: material left the server for a device, and
    /// no later revocation reaches into that device's memory (ADR-0035).
    /// </remarks>
    public const string AiContextLeaseIssued = "ai.lease.issued";

    /// <summary>A device-local result was validated and became part of a run.</summary>
    public const string AiLocalResultAccepted = "ai.local.accepted";

    public const string AiProviderPolicyChanged = "ai.provider.policy.changed";

    public const string SavedViewCreated = "savedview.created";
    public const string SavedViewUpdated = "savedview.updated";
    public const string SavedViewDeleted = "savedview.deleted";
}
