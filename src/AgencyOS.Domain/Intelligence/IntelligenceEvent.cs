using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Intelligence;

/// <summary>Which intelligence object an event happened to.</summary>
public enum IntelligenceOwnerKind
{
    Source = 1,
    Signal = 2,
    Thesis = 3,
    Prediction = 4,
    Watchlist = 5,
    TalentRadarEntry = 6,
    ResearchCase = 7,
}

/// <summary>What happened.</summary>
public enum IntelligenceEventKind
{
    SourceRecorded = 1,
    SourceReliabilityAssessed = 2,

    SignalRecorded = 10,
    SignalEvidenceAdded = 11,
    SignalEvidenceRemoved = 12,
    SignalCorroborated = 13,
    SignalDisputed = 14,
    SignalRetracted = 15,
    SignalUpdated = 16,

    ThesisCreated = 20,
    ThesisActivated = 21,
    ThesisRevised = 22,
    ThesisEvidenceLinked = 23,
    ThesisRetired = 24,
    ThesisSuperseded = 25,

    PredictionCreated = 30,
    PredictionRevised = 31,
    PredictionResolved = 32,
    PredictionCancelled = 33,

    WatchlistCreated = 40,
    WatchlistEntryAdded = 41,
    WatchlistEntryRemoved = 42,
    WatchlistReviewed = 43,
    WatchlistArchived = 44,

    RadarEntryCreated = 50,
    RadarStatusChanged = 51,
    RadarReviewed = 52,
    RadarConvertedToProspect = 53,
    RadarDismissed = 54,

    ResearchCaseOpened = 60,
    ResearchCaseLinked = 61,
    ResearchCaseStatusChanged = 62,
    ResearchCaseCompleted = 63,

    SubjectAdded = 70,
    SubjectRemoved = 71,
}

/// <summary>
/// The curated history of what happened to a piece of intelligence.
/// </summary>
/// <remarks>
/// <para>
/// A human projection, in the M6-M10 tradition: what an analyst would want to read
/// on a detail page, in the order it happened. It is <strong>not</strong> the audit
/// trail. <c>AuditEvent</c> answers who is accountable for a change; this answers
/// what became of a belief.
/// </para>
/// <para>
/// One table with an owner arc rather than seven event tables, because every one of
/// them would carry the same five columns and the timeline query wants them
/// interleaved anyway (ADR-0030).
/// </para>
/// <para>
/// Append-only by construction: there is no method here that changes a row.
/// </para>
/// </remarks>
public sealed class IntelligenceEvent
{
    private IntelligenceEvent()
    {
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public IntelligenceOwnerKind OwnerKind { get; private set; }

    /// <summary>
    /// Which object this happened to.
    /// </summary>
    /// <remarks>
    /// Persisted into the typed column matching <see cref="OwnerKind"/>, which
    /// carries the composite tenant foreign key.
    /// </remarks>
    public Guid OwnerId { get; private set; }

    public IntelligenceEventKind Kind { get; private set; }

    /// <summary>One line, readable without opening anything else.</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>
    /// More, when there is more worth saying.
    /// </summary>
    /// <remarks>
    /// Never the claim, the proposition or the rationale itself. Those live on
    /// their own rows under their own sensitivity, and copying them into a timeline
    /// would create a second copy with weaker guards (ADR-0030).
    /// </remarks>
    public string? Detail { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public UserId ActorUserId { get; private set; }

    public static IntelligenceEvent Record(
        OrganizationId organizationId,
        IntelligenceOwnerKind ownerKind,
        Guid ownerId,
        IntelligenceEventKind kind,
        string summary,
        UserId actor,
        DateTimeOffset now,
        string? detail = null)
    {
        if (!Enum.IsDefined(ownerKind))
        {
            throw new DomainException($"'{ownerKind}' is not an intelligence object.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"'{kind}' is not an intelligence event.");
        }

        if (ownerId == Guid.Empty)
        {
            throw new DomainException("An event must name what it happened to.");
        }

        return new IntelligenceEvent
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            OwnerKind = ownerKind,
            OwnerId = ownerId,
            Kind = kind,
            Summary = Ensure.NotBlankMax(summary, nameof(summary), 500),
            Detail = Ensure.OptionalMax(detail, nameof(detail), 2000),
            OccurredAt = now,
            ActorUserId = actor,
        };
    }
}
