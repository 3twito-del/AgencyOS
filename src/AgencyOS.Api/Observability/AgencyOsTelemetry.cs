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
}
