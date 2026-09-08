using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgencyOS.Api.Observability;

/// <summary>
/// The activity source and instruments for the M3 failure paths.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0004 deferred OpenTelemetry in M0 because there was nothing distributed to
/// trace, and named the condition that would change the answer: the first
/// cross-process call. ADR-0016 records that M3 is that milestone. A single user
/// action can now span a client, a queue that survives a restart, a submission
/// days later and a replay of a request whose first attempt appears in no log.
/// </para>
/// <para>
/// The counters are chosen to answer one question without reading traces one at a
/// time: is the synchronization design behaving? Replays and conflicts are the
/// two numbers that say so.
/// </para>
/// <para>
/// No exporter is registered unless an OTLP endpoint is configured, so the
/// operational surface stays at zero until somebody wants to look.
/// </para>
/// </remarks>
public static class AgencyOsTelemetry
{
    /// <summary>Name shared by the activity source and the meter.</summary>
    public const string SourceName = "AgencyOS";

    public static ActivitySource Source { get; } = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    /// <summary>Searches executed, by whether they matched anything.</summary>
    public static Counter<long> Searches { get; } =
        Meter.CreateCounter<long>("agencyos.search.queries", description: "Search queries executed.");

    /// <summary>Change-feed pages served.</summary>
    public static Counter<long> SyncPages { get; } =
        Meter.CreateCounter<long>("agencyos.sync.pages", description: "Change-feed pages served to clients.");

    /// <summary>
    /// Requests answered from a stored idempotent response rather than executed.
    /// </summary>
    /// <remarks>
    /// The number that says the offline queue is doing its job. A non-zero count
    /// is not a fault: it is a duplicate that did not happen.
    /// </remarks>
    public static Counter<long> IdempotentReplays { get; } =
        Meter.CreateCounter<long>(
            "agencyos.idempotency.replays",
            description: "Requests answered from a stored response instead of executing again.");

    /// <summary>
    /// Prospects turned into representations.
    /// </summary>
    /// <remarks>
    /// The most consequential command in the representation model, and the one a
    /// retry could most easily corrupt. Counting it makes an unexpected rate
    /// visible without reading traces one at a time.
    /// </remarks>
    public static Counter<long> ProspectConversions { get; } =
        Meter.CreateCounter<long>(
            "agencyos.prospect.conversions",
            description: "Prospects converted into representations.");

    /// <summary>Representation status changes, by the status reached.</summary>
    public static Counter<long> RepresentationTransitions { get; } =
        Meter.CreateCounter<long>(
            "agencyos.representation.transitions",
            description: "Representation status changes.");

    /// <summary>Project operational status changes.</summary>
    /// <remarks>
    /// Status and stage are counted separately because they answer different
    /// questions: how many projects are being cancelled, versus how much work is
    /// moving through development. One counter would conflate them.
    /// </remarks>
    public static Counter<long> ProjectStatusChanges { get; } =
        Meter.CreateCounter<long>(
            "agencyos.project.status.changes",
            description: "Project operational status changes.");

    /// <summary>Project development stage changes.</summary>
    public static Counter<long> ProjectStageChanges { get; } =
        Meter.CreateCounter<long>(
            "agencyos.project.stage.changes",
            description: "Project development stage changes.");

    /// <summary>Attachments created or moved.</summary>
    /// <remarks>
    /// The high-traffic consequential mutation in M5, and the one whose invariant a
    /// concurrent retry could most easily test.
    /// </remarks>
    public static Counter<long> AttachmentChanges { get; } =
        Meter.CreateCounter<long>(
            "agencyos.project.attachment.changes",
            description: "Attachments created or moved to a new status.");

    /// <summary>Package status changes.</summary>
    public static Counter<long> PackageStatusChanges { get; } =
        Meter.CreateCounter<long>(
            "agencyos.package.status.changes",
            description: "Package status changes.");

    /// <summary>Pursuits opened.</summary>
    public static Counter<long> OpportunitiesCreated { get; } =
        Meter.CreateCounter<long>(
            "agencyos.opportunity.created",
            description: "Opportunities opened.");

    /// <summary>Opportunity status changes.</summary>
    public static Counter<long> OpportunityStatusChanges { get; } =
        Meter.CreateCounter<long>(
            "agencyos.opportunity.status.changes",
            description: "Opportunity status changes.");

    /// <summary>Targets added or moved through the pipeline.</summary>
    /// <remarks>
    /// The high-traffic mutation in M6, and the one whose rate says most about
    /// whether the pipeline is being worked at all.
    /// </remarks>
    public static Counter<long> OpportunityTargetChanges { get; } =
        Meter.CreateCounter<long>(
            "agencyos.opportunity.target.changes",
            description: "Opportunity targets added or moved.");

    /// <summary>Submissions recorded.</summary>
    /// <remarks>
    /// Counts what the agency says it sent. AgencyOS transmits nothing, so this is
    /// a count of assertions rather than of deliveries (ADR-0020).
    /// </remarks>
    public static Counter<long> SubmissionsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.submission.recorded",
            description: "Submissions recorded.");

    /// <summary>Pitches recorded.</summary>
    public static Counter<long> PitchesRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.pitch.recorded",
            description: "Pitches recorded, each carried by one interaction.");

    // ---- Deals and offers (M7) ----
    //
    // Every counter here carries identifiers, operation types and counts. None
    // carries a compensation figure, a currency amount or a term value: telemetry
    // is exported to places that have none of the permissions guarding the
    // economics, and a counter tagged with the number would be the leak
    // (ADR-0021).

    /// <summary>Negotiations opened.</summary>
    public static Counter<long> DealsOpened { get; } =
        Meter.CreateCounter<long>(
            "agencyos.deal.opened",
            description: "Negotiations opened from an opportunity target.");

    /// <summary>Deal status changes a caller requested.</summary>
    /// <remarks>
    /// Closures and cancellations only. Reaching Negotiating or TermsAgreed is a
    /// consequence of an offer act, and is counted by the offer counters instead.
    /// </remarks>
    public static Counter<long> DealStatusChanges { get; } =
        Meter.CreateCounter<long>(
            "agencyos.deal.status.changes",
            description: "Negotiations closed without agreement or cancelled.");

    /// <summary>Negotiations reopened.</summary>
    /// <remarks>
    /// Worth watching on its own: a reopen unwinds an agreement, which is the one
    /// routine operation that changes what the agency previously recorded as
    /// settled.
    /// </remarks>
    public static Counter<long> NegotiationsReopened { get; } =
        Meter.CreateCounter<long>(
            "agencyos.deal.reopened",
            description: "Negotiations reopened, unwinding an acceptance or a closure.");

    /// <summary>Offers recorded.</summary>
    /// <remarks>
    /// Counts what the agency says passed between the parties. AgencyOS transmits
    /// nothing, so this counts assertions rather than deliveries.
    /// </remarks>
    public static Counter<long> OffersRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.offer.recorded",
            description: "Offers recorded as made or received.");

    /// <summary>Answers recorded against standing offers.</summary>
    public static Counter<long> OfferAnswers { get; } =
        Meter.CreateCounter<long>(
            "agencyos.offer.answered",
            description: "Offers accepted, rejected, withdrawn or expired.");

    /// <summary>Offers accepted.</summary>
    /// <remarks>
    /// The most consequential act in the milestone, counted separately from the
    /// other answers because it is the only one that agrees a deal's terms.
    /// </remarks>
    public static Counter<long> OffersAccepted { get; } =
        Meter.CreateCounter<long>(
            "agencyos.offer.accepted",
            description: "Offers accepted, agreeing a negotiation's commercial terms.");

    /// <summary>Offer comparisons computed.</summary>
    public static Counter<long> OfferComparisons { get; } =
        Meter.CreateCounter<long>(
            "agencyos.offer.comparisons",
            description: "Offer comparisons computed by the deal rules kernel.");

    /// <summary>Writes refused because the record had moved on.</summary>
    public static Counter<long> VersionConflicts { get; } =
        Meter.CreateCounter<long>(
            "agencyos.concurrency.conflicts",
            description: "Writes refused because the caller's version was stale.");

    // ---- Contracts, rights and obligations (M8) ----
    //
    // The M7 rule applies unchanged, and one more with it. No counter carries a
    // term value, a compensation figure or a currency amount, because telemetry is
    // exported to places holding none of the permissions that guard the economics.
    // No counter carries a clause, a summary or anything a person classified as
    // privileged either: a metric tagged with legal analysis would be that analysis
    // leaving the permission boundary through the back door (ADR-0021, ADR-0022).

    /// <summary>Contracts opened against an agreed negotiation.</summary>
    public static Counter<long> ContractsOpened { get; } =
        Meter.CreateCounter<long>(
            "agencyos.contract.opened",
            description: "Contracts opened against a negotiation whose terms are agreed.");

    /// <summary>Contract lifecycle moves a caller requested.</summary>
    /// <remarks>
    /// Drafting moves only. Reaching PartiallyExecuted or Executed is a consequence
    /// of recording a signature, and is counted by the signature counters instead.
    /// </remarks>
    public static Counter<long> ContractStatusChanges { get; } =
        Meter.CreateCounter<long>(
            "agencyos.contract.status.changes",
            description: "Contracts moved through their drafting lifecycle.");

    /// <summary>Drafting versions recorded.</summary>
    /// <remarks>
    /// Counts versions, never documents. AgencyOS holds no document in M8, so this
    /// counts the milestones a person recorded about files it has never seen.
    /// </remarks>
    public static Counter<long> ContractVersionsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.contract.version.recorded",
            description: "Drafting versions recorded against a contract.");

    /// <summary>Signatures recorded.</summary>
    /// <remarks>
    /// Counts assertions that a party signed. Nothing here is verified: AgencyOS
    /// implements no electronic signature and holds no certificate.
    /// </remarks>
    public static Counter<long> SignaturesRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.contract.signature.recorded",
            description: "Signatures recorded against a contract party.");

    /// <summary>Contracts that became fully executed.</summary>
    /// <remarks>
    /// The most consequential act in the milestone, counted separately because it
    /// is the moment an agreement stops being paper somebody is drafting.
    /// </remarks>
    public static Counter<long> ContractsExecuted { get; } =
        Meter.CreateCounter<long>(
            "agencyos.contract.executed",
            description: "Contracts whose last required signature was recorded.");

    /// <summary>Reconciliations computed.</summary>
    /// <remarks>
    /// Tagged only with whether the draft matched. The count of differences is
    /// deliberately absent: it is a shape of the economics, and a rising number on
    /// a named contract would tell an observer something the permissions do not.
    /// </remarks>
    public static Counter<long> Reconciliations { get; } =
        Meter.CreateCounter<long>(
            "agencyos.contract.reconciliations",
            description: "Negotiated-against-drafted comparisons computed by the rules kernel.");

    /// <summary>Rights grants recorded.</summary>
    public static Counter<long> RightsGrantsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.rights.grant.recorded",
            description: "Grants recorded from a contract version.");

    /// <summary>Options recorded.</summary>
    public static Counter<long> OptionsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.option.recorded",
            description: "Elections recorded from a contract version.");

    /// <summary>Options resolved.</summary>
    /// <remarks>
    /// Every outcome is an act somebody recorded, expiry included. Nothing in
    /// AgencyOS resolves an option because a date passed (ADR-0022).
    /// </remarks>
    public static Counter<long> OptionsResolved { get; } =
        Meter.CreateCounter<long>(
            "agencyos.option.resolved",
            description: "Options exercised, declined, waived, expired or cancelled.");

    /// <summary>Obligations recorded.</summary>
    public static Counter<long> ObligationsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.obligation.recorded",
            description: "Duties recorded from a contract version.");

    /// <summary>Obligations resolved.</summary>
    /// <remarks>
    /// The breach tag is worth watching on its own: it is the one outcome that
    /// records a legal determination rather than an event, and it never follows
    /// from a due date passing.
    /// </remarks>
    public static Counter<long> ObligationsResolved { get; } =
        Meter.CreateCounter<long>(
            "agencyos.obligation.resolved",
            description: "Obligations satisfied, waived, breached, reinstated or cancelled.");

    /// <summary>Notices recorded as given or received.</summary>
    /// <remarks>
    /// AgencyOS sends nothing. This counts assertions that a notice passed between
    /// the parties, exactly as the offer counter counts assertions about offers.
    /// </remarks>
    public static Counter<long> NoticesRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.notice.recorded",
            description: "Notices recorded as given or received. AgencyOS transmits none.");

    // ---- Finance, commissions and the ledger (M9) ----
    //
    // The M7 and M8 rule holds, and finance makes it sharper. No counter carries an
    // amount, a balance, a commission rate, a bank reference or a payer name. What
    // is counted is that an act happened and what shape it had: a direction, a
    // method, a currency code, an outcome category. A metric tagged with the figure
    // would put the economics into every place metrics are exported to, none of
    // which holds a finance permission (ADR-0021, ADR-0023).

    /// <summary>Payable sums recorded from an operative contract.</summary>
    /// <remarks>
    /// Tagged with category and amount kind, which say what sort of money is being
    /// tracked without saying how much. A rising count of Unknown-amount
    /// obligations is worth seeing: it means the agency is recording duties nobody
    /// can yet value.
    /// </remarks>
    public static Counter<long> MonetaryObligationsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.obligation.recorded",
            description: "Payable sums recorded from an operative contract.");

    /// <summary>Receivables raised.</summary>
    /// <remarks>
    /// Tagged with the beneficiary, the one distinction that changes whose money it
    /// becomes. Client and agency receivables behave differently all the way
    /// through the ledger, so counting them together would hide the split.
    /// </remarks>
    public static Counter<long> ReceivablesRaised { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.receivable.raised",
            description: "Receivables raised from a quantified obligation.");

    /// <summary>Receivables written off.</summary>
    /// <remarks>
    /// A deliberate financial act, and the one that says the agency has given up on
    /// money it expected. Counted on its own because a rising rate is a business
    /// signal, not a system fault.
    /// </remarks>
    public static Counter<long> ReceivablesWrittenOff { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.receivable.written_off",
            description: "Receivables written off. A financial act, never data cleanup.");

    /// <summary>Deductions recorded against receivables.</summary>
    /// <remarks>
    /// Tagged with the kind, because withholding, a bank fee and an agreed
    /// reduction are different stories about the same shortfall. AgencyOS infers
    /// none of them: every one of these is a fact somebody entered.
    /// </remarks>
    public static Counter<long> AdjustmentsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.adjustment.recorded",
            description: "Deductions recorded against a receivable.");

    /// <summary>Invoices recorded.</summary>
    /// <remarks>
    /// Counts invoices AgencyOS was told about. It sends none: there is no
    /// transport anywhere in M9, and delivery belongs to a later milestone.
    /// </remarks>
    public static Counter<long> InvoicesRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.invoice.recorded",
            description: "Invoices recorded against existing receivables. AgencyOS sends none.");

    /// <summary>Invoices marked issued.</summary>
    public static Counter<long> InvoicesIssued { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.invoice.issued",
            description: "Invoices recorded as issued, by a person who issued them elsewhere.");

    /// <summary>Payments recorded.</summary>
    /// <remarks>
    /// Tagged with direction, currency code and whether the money was fully applied.
    /// The last of those is the operationally interesting one: a rising share of
    /// partially applied payments means cash is arriving that nobody has explained
    /// yet, which is a queue somebody has to work.
    /// </remarks>
    public static Counter<long> PaymentsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.payment.recorded",
            description: "Payments recorded as observed. Amounts are never tagged.");

    /// <summary>Payments reversed.</summary>
    /// <remarks>
    /// A payment recorded in error, undone by a reversing payment rather than an
    /// edit. Worth watching on its own: it is the routine operation that unwinds
    /// something already posted to the ledger.
    /// </remarks>
    public static Counter<long> PaymentsReversed { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.payment.reversed",
            description: "Payments undone by a reversing payment. The original is never edited.");

    /// <summary>Allocations applied.</summary>
    public static Counter<long> AllocationsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.allocation.recorded",
            description: "Payment allocations applied to receivables.");

    /// <summary>Allocations reversed.</summary>
    public static Counter<long> AllocationsReversed { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.allocation.reversed",
            description: "Allocations returned to unapplied, keeping the line as history.");

    /// <summary>Commission entitlements calculated.</summary>
    /// <remarks>
    /// Never tagged with the rate or the figure. The rate a client pays is the most
    /// sensitive number in the relationship, and a metric carrying it would put it
    /// outside every permission that guards it.
    /// </remarks>
    public static Counter<long> CommissionsCalculated { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.commission.calculated",
            description: "Commission entitlements calculated under a governing rule.");

    /// <summary>Commission adjustments recorded.</summary>
    public static Counter<long> CommissionsAdjusted { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.commission.adjusted",
            description: "Corrections, settlements and waivers recorded against an entitlement.");

    /// <summary>Journal entries posted.</summary>
    /// <remarks>
    /// Tagged with the source, which separates the entries the system wrote as a
    /// consequence of a business act from the ones a person wrote by hand. A rising
    /// count of manual adjustments is the number that says the automatic postings
    /// are not matching what the desk believes.
    /// </remarks>
    public static Counter<long> JournalEntriesPosted { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.journal.posted",
            description: "Journal entries posted. Balanced at the database before they land.");

    /// <summary>Journal entries reversed.</summary>
    /// <remarks>
    /// A posted entry is never edited. A correction is another entry saying the
    /// opposite, and both stay readable for ever.
    /// </remarks>
    public static Counter<long> JournalEntriesReversed { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.journal.reversed",
            description: "Journal entries undone by a reversing entry.");

    /// <summary>Receivable reconciliations computed.</summary>
    /// <remarks>
    /// Tagged with the outcome only. Not the variance: a rising shortfall count on
    /// a named tenant is a business fact somebody should see, but the size of the
    /// gap is the economics, and telemetry is not where the economics belong.
    /// </remarks>
    public static Counter<long> FinanceReconciliations { get; } =
        Meter.CreateCounter<long>(
            "agencyos.finance.reconciliations",
            description: "Expected-against-arrived comparisons computed for a receivable.");

    // ---- Documents and communications (M10) ----
    //
    // Every rule from M7 to M9 holds, and M10 adds two more that matter here.
    // Nothing carries a message body, a subject, a recipient address, an OAuth
    // token, a storage key or a byte of document content. What is counted is that
    // something happened and what shape it had: a provider, an operation, a byte
    // count, a state category, a duration. Telemetry is exported to places that
    // hold no document and no mailbox permission at all (ADR-0025, ADR-0026).

    /// <summary>Documents recorded, with or without a new version of their own.</summary>
    public static Counter<long> DocumentsRecorded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.document.recorded",
            description: "Documents recorded into the canonical store.");

    /// <summary>Versions added to existing documents.</summary>
    /// <remarks>
    /// Counted apart from new documents because the rate says something different:
    /// versions accumulating on one instrument is a negotiation being papered, and
    /// new documents arriving is an agency filing things.
    /// </remarks>
    public static Counter<long> DocumentVersionsAdded { get; } =
        Meter.CreateCounter<long>(
            "agencyos.document.version.added",
            description: "Versions added to an existing document. Version N is never rewritten.");

    /// <summary>Bytes accepted into the content store.</summary>
    /// <remarks>
    /// A byte count and a deduplication flag, never a filename and never a digest.
    /// The digest identifies specific content, and a metric carrying one would let
    /// an observer confirm whether a particular file is held.
    /// </remarks>
    public static Counter<long> BlobBytesStored { get; } =
        Meter.CreateCounter<long>(
            "agencyos.blob.bytes.stored",
            unit: "By",
            description: "Bytes written to the content store.");

    /// <summary>Uploads whose bytes the organization already held.</summary>
    public static Counter<long> BlobDeduplications { get; } =
        Meter.CreateCounter<long>(
            "agencyos.blob.deduplicated",
            description: "Uploads that reused bytes already held by the same organization.");

    /// <summary>Downloads served.</summary>
    /// <remarks>
    /// Tagged with the classification, because a rising rate of privileged
    /// downloads is worth seeing. Never with the document, the filename or the
    /// person.
    /// </remarks>
    public static Counter<long> DocumentDownloads { get; } =
        Meter.CreateCounter<long>(
            "agencyos.document.downloaded",
            description: "Document versions streamed to an authorized caller.");

    /// <summary>Stored bytes that no longer hash to their recorded digest.</summary>
    /// <remarks>
    /// Should be zero for ever. It is counted so that it is <em>visible</em> if it
    /// is not, rather than being discovered by somebody opening a broken file
    /// (ADR-0024).
    /// </remarks>
    public static Counter<long> BlobIntegrityFailures { get; } =
        Meter.CreateCounter<long>(
            "agencyos.blob.integrity.failures",
            description: "Stored objects whose bytes no longer match their recorded digest.");

    /// <summary>Uploads collected because they never became a document.</summary>
    public static Counter<long> OrphanedUploadsSwept { get; } =
        Meter.CreateCounter<long>(
            "agencyos.blob.orphans.swept",
            description: "Unreferenced staged uploads removed by the sweeper.");

    /// <summary>Documents connected to a business record.</summary>
    public static Counter<long> DocumentLinks { get; } =
        Meter.CreateCounter<long>(
            "agencyos.document.linked",
            description: "Documents linked to or unlinked from a business record.");

    /// <summary>Mailboxes connected or disconnected.</summary>
    /// <remarks>
    /// Tagged with the provider and the operation. Never with the mailbox address:
    /// a metric carrying one is a directory of who the agency corresponds through.
    /// </remarks>
    public static Counter<long> MailboxConnections { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.account.changes",
            description: "Mailboxes connected, disconnected or reshared.");

    /// <summary>Messages brought in by synchronization.</summary>
    public static Counter<long> MessagesSynchronized { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.messages.synchronized",
            description: "Messages newly recorded from a provider.");

    /// <summary>Synchronizations that failed.</summary>
    public static Counter<long> SyncFailures { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.sync.failures",
            description: "Mailbox synchronizations that could not complete.");

    /// <summary>Delta cursors the provider refused.</summary>
    /// <remarks>
    /// Not a failure: it is the provider asking for a full resynchronization, and
    /// counting it separately keeps it out of the failure rate where it would look
    /// like something was wrong (ADR-0026).
    /// </remarks>
    public static Counter<long> SyncCursorResets { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.sync.cursor.resets",
            description: "Delta cursors a provider rejected, prompting a full resync.");

    /// <summary>Message attachments pulled into the document store.</summary>
    public static Counter<long> AttachmentsIngested { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.attachment.ingested",
            description: "Message attachments ingested as canonical document versions.");

    /// <summary>Messages linked to a business record.</summary>
    public static Counter<long> MessageLinks { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.message.linked",
            description: "Messages linked to or unlinked from a business record.");

    /// <summary>Outbound messages composed.</summary>
    public static Counter<long> OutboundComposed { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.dispatch.composed",
            description: "Outbound message intents recorded.");

    /// <summary>Every transition of an outbound send, by the states involved.</summary>
    /// <remarks>
    /// The single most operationally useful counter in the milestone. Tagged with
    /// the state moved from and to, which says whether sends are completing, being
    /// refused, or piling up unresolved - without carrying anything about the
    /// messages themselves (ADR-0028).
    /// </remarks>
    public static Counter<long> OutboundStateChanges { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.dispatch.transitions",
            description: "Outbound send state changes, by the states moved between.");

    /// <summary>Sends whose outcome AgencyOS cannot yet prove.</summary>
    /// <remarks>
    /// Counted on its own because it is the one state nothing can resolve without a
    /// person or a successful reconciliation, and because a rising number means
    /// somebody has to go and look at a mailbox (ADR-0028).
    /// </remarks>
    public static Counter<long> OutboundUnknownOutcomes { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.dispatch.unknown",
            description: "Sends whose outcome could not be established. Never retried blindly.");

    /// <summary>Reconciliations run against a provider, by verdict.</summary>
    public static Counter<long> OutboundReconciliations { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.dispatch.reconciliations",
            description: "Provider searches for a message of unknown outcome.");

    /// <summary>Mailbox authorizations a provider refused.</summary>
    /// <remarks>
    /// Never tagged with a token, a fragment of one, or the mailbox. Only that a
    /// credential stopped working, which is what an operator needs to know
    /// (ADR-0027).
    /// </remarks>
    public static Counter<long> ProviderAuthorizationFailures { get; } =
        Meter.CreateCounter<long>(
            "agencyos.communication.provider.authorization.failures",
            description: "Provider calls refused because a stored credential was rejected.");
}
