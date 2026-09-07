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

    /// <summary>Writes refused because the record had moved on.</summary>
    public static Counter<long> VersionConflicts { get; } =
        Meter.CreateCounter<long>(
            "agencyos.concurrency.conflicts",
            description: "Writes refused because the caller's version was stale.");
}
