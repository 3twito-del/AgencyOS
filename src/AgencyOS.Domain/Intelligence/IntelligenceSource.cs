using AgencyOS.Domain.Common;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Intelligence;

/// <summary>Opaque, immutable identifier for an <see cref="IntelligenceSource"/>.</summary>
public readonly record struct IntelligenceSourceId(Guid Value)
{
    public static IntelligenceSourceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// What kind of evidence a source is.
/// </summary>
/// <remarks>
/// The first two point at something AgencyOS actually holds. The rest are
/// references to things it does not, and the model keeps that difference visible
/// rather than flattening every source into "a document" (ADR-0030).
/// </remarks>
public enum IntelligenceSourceKind
{
    /// <summary>A stored document version. AgencyOS holds these bytes (M10).</summary>
    DocumentVersion = 1,

    /// <summary>A synchronized message. AgencyOS holds this message (M10).</summary>
    Message = 2,

    /// <summary>
    /// A published page somewhere else.
    /// </summary>
    /// <remarks>
    /// A <strong>reference</strong>, not a preserved document. AgencyOS holds the
    /// URL and what somebody recorded about it; it does not hold the page, has not
    /// archived it, and cannot prove what it said. Preserving a page means storing
    /// its bytes through M10, which is a separate, deliberate act.
    /// </remarks>
    ExternalUrl = 3,

    /// <summary>Something a person saw, heard or was told.</summary>
    /// <remarks>
    /// Provenance is the person and the moment. That is weaker than a document and
    /// it is not nothing, and recording it as itself is better than dressing it up
    /// as a citation.
    /// </remarks>
    ManualObservation = 4,

    /// <summary>Evidence that fits none of the above, described in words.</summary>
    Other = 99,
}

/// <summary>
/// How much weight a person has decided to give a source.
/// </summary>
/// <remarks>
/// <para>
/// A small ordinal scale rather than a percentage, because a percentage would
/// invite arithmetic that means nothing: a source is not 73% reliable, and two
/// sources at 60% do not make 84%. Four steps a person can defend out loud are
/// worth more than a number nobody can explain (ADR-0030).
/// </para>
/// <para>
/// It is <strong>never inferred</strong>. AgencyOS does not rate publications, does
/// not learn from past accuracy, and does not lower a source because a signal was
/// disputed. Somebody says what they think and signs their name to it.
/// </para>
/// <para>
/// This is not signal confidence, thesis confidence or prediction probability.
/// Those are three other questions about three other objects.
/// </para>
/// </remarks>
public enum SourceReliability
{
    /// <summary>Nobody has assessed this source.</summary>
    Unassessed = 0,

    /// <summary>Weak: rumour, an interested party, or an unclear chain of hearsay.</summary>
    Low = 1,

    /// <summary>Ordinary: plausible, unremarkable, worth reading with care.</summary>
    Medium = 2,

    /// <summary>Strong: a source that has been right about this kind of thing.</summary>
    High = 3,

    /// <summary>The record itself: an executed contract, a first-hand document.</summary>
    Primary = 4,
}

/// <summary>
/// A piece of evidence, or a reference to one.
/// </summary>
/// <remarks>
/// <para>
/// The bottom of the epistemic chain. A source is not a claim and is never "true":
/// it is a thing that exists, which somebody may read a claim out of. The claim is
/// a <see cref="Signal"/>, and it is a separate row for exactly that reason
/// (ADR-0030).
/// </para>
/// <para>
/// <strong>Three times, kept apart.</strong> <see cref="PublishedAt"/> is when the
/// source says it was published. <see cref="ObservedAt"/> is when a person here saw
/// it. <see cref="RecordedAt"/> is when AgencyOS was told. A trade article
/// published on Monday, read on Wednesday and typed in on Friday has three
/// different dates and no two of them are interchangeable — the same discipline
/// M10 applies to a message's sent, received and synchronized times.
/// </para>
/// <para>
/// For evidence AgencyOS already holds, the source references the canonical M10
/// identity and copies nothing. There is no second document store and no second
/// message store here.
/// </para>
/// </remarks>
public sealed class IntelligenceSource
{
    private IntelligenceSource()
    {
    }

    public IntelligenceSourceId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public IntelligenceSourceKind Kind { get; private set; }

    /// <summary>What this source is called, in a list.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The canonical M10 document version, when the source is one.</summary>
    public DocumentVersionId? DocumentVersionId { get; private set; }

    /// <summary>The canonical M10 message, when the source is one.</summary>
    public CommunicationMessageId? MessageId { get; private set; }

    /// <summary>Where the page is, for an external reference.</summary>
    public string? Url { get; private set; }

    /// <summary>Who published it, as the source names itself.</summary>
    public string? Publisher { get; private set; }

    /// <summary>The byline, when there is one.</summary>
    public string? Author { get; private set; }

    /// <summary>An identifier the outside world uses for this thing.</summary>
    public string? ExternalReference { get; private set; }

    /// <summary>When the source says it was published. Often unknown.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>When somebody here saw it.</summary>
    public DateTimeOffset ObservedAt { get; private set; }

    /// <summary>When AgencyOS was told. Never the same question as the two above.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>How much weight a person decided to give it. Never inferred.</summary>
    public SourceReliability Reliability { get; private set; }

    /// <summary>Why that reliability, in the assessor's words.</summary>
    public string? ReliabilityRationale { get; private set; }

    /// <summary>Who assessed the reliability, if anybody has.</summary>
    public UserId? ReliabilityAssessedBy { get; private set; }

    public DateTimeOffset? ReliabilityAssessedAt { get; private set; }

    /// <summary>How sensitive the source itself is. Stated, never inferred.</summary>
    public IntelligenceSensitivity Sensitivity { get; private set; }

    public string? Notes { get; private set; }

    public UserId RecordedBy { get; private set; }

    public int Version { get; private set; }

    /// <summary>Whether AgencyOS actually holds the evidence rather than a pointer to it.</summary>
    /// <remarks>
    /// Published rather than assumed, on the M8 and M10 precedent: a reader who
    /// assumes the article is stored goes looking for it during an argument, and a
    /// URL that has since gone is not a citation anybody can check.
    /// </remarks>
    public bool IsHeldByAgencyOS =>
        Kind is IntelligenceSourceKind.DocumentVersion or IntelligenceSourceKind.Message;

    /// <summary>Records a source AgencyOS already holds: a stored document version.</summary>
    public static IntelligenceSource FromDocumentVersion(
        OrganizationId organizationId,
        DocumentVersionId documentVersionId,
        string title,
        UserId recordedBy,
        DateTimeOffset now,
        IntelligenceSensitivity sensitivity,
        DateTimeOffset? observedAt = null,
        DateTimeOffset? publishedAt = null,
        string? notes = null)
    {
        IntelligenceSource source = Start(
            organizationId,
            IntelligenceSourceKind.DocumentVersion,
            title,
            recordedBy,
            now,
            sensitivity,
            observedAt,
            publishedAt,
            notes);

        source.DocumentVersionId = documentVersionId;

        return source;
    }

    /// <summary>Records a source AgencyOS already holds: a synchronized message.</summary>
    public static IntelligenceSource FromMessage(
        OrganizationId organizationId,
        CommunicationMessageId messageId,
        string title,
        UserId recordedBy,
        DateTimeOffset now,
        IntelligenceSensitivity sensitivity,
        DateTimeOffset? observedAt = null,
        DateTimeOffset? publishedAt = null,
        string? notes = null)
    {
        IntelligenceSource source = Start(
            organizationId,
            IntelligenceSourceKind.Message,
            title,
            recordedBy,
            now,
            sensitivity,
            observedAt,
            publishedAt,
            notes);

        source.MessageId = messageId;

        return source;
    }

    /// <summary>
    /// Records a reference to something published elsewhere.
    /// </summary>
    /// <remarks>
    /// This stores a URL and what a person recorded about it. It does not archive
    /// the page, hash it, or preserve what it said, and nothing on this record
    /// claims otherwise. A page that matters should be captured through M10, where
    /// AgencyOS actually holds the bytes and can prove them (ADR-0024, ADR-0030).
    /// </remarks>
    public static IntelligenceSource FromExternalUrl(
        OrganizationId organizationId,
        string url,
        string title,
        UserId recordedBy,
        DateTimeOffset now,
        IntelligenceSensitivity sensitivity,
        string? publisher = null,
        string? author = null,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? observedAt = null,
        string? externalReference = null,
        string? notes = null)
    {
        IntelligenceSource source = Start(
            organizationId,
            IntelligenceSourceKind.ExternalUrl,
            title,
            recordedBy,
            now,
            sensitivity,
            observedAt,
            publishedAt,
            notes);

        string candidate = Ensure.NotBlankMax(url, nameof(url), 2000);

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException("A source URL must be an absolute http or https address.");
        }

        source.Url = candidate;
        source.Publisher = Ensure.OptionalMax(publisher, nameof(publisher), 200);
        source.Author = Ensure.OptionalMax(author, nameof(author), 200);
        source.ExternalReference = Ensure.OptionalMax(
            externalReference, nameof(externalReference), 200);

        return source;
    }

    /// <summary>Records something a person saw, heard or was told.</summary>
    public static IntelligenceSource FromObservation(
        OrganizationId organizationId,
        string title,
        UserId recordedBy,
        DateTimeOffset now,
        IntelligenceSensitivity sensitivity,
        DateTimeOffset? observedAt = null,
        string? notes = null)
    {
        IntelligenceSource source = Start(
            organizationId,
            IntelligenceSourceKind.ManualObservation,
            title,
            recordedBy,
            now,
            sensitivity,
            observedAt,

            // A conversation has no publication date. Leaving it null is the
            // honest answer, and inventing one would put a false citation in front
            // of somebody later.
            publishedAt: null,
            notes);

        return source;
    }

    /// <summary>Records evidence that fits none of the shapes above.</summary>
    public static IntelligenceSource FromOther(
        OrganizationId organizationId,
        string title,
        UserId recordedBy,
        DateTimeOffset now,
        IntelligenceSensitivity sensitivity,
        DateTimeOffset? observedAt = null,
        DateTimeOffset? publishedAt = null,
        string? externalReference = null,
        string? notes = null)
    {
        IntelligenceSource source = Start(
            organizationId,
            IntelligenceSourceKind.Other,
            title,
            recordedBy,
            now,
            sensitivity,
            observedAt,
            publishedAt,
            notes);

        source.ExternalReference = Ensure.OptionalMax(
            externalReference, nameof(externalReference), 200);

        return source;
    }

    /// <summary>
    /// Records a person's assessment of how much weight this source deserves.
    /// </summary>
    /// <remarks>
    /// An explicit human act with a name and a time on it. Reassessing overwrites
    /// the current value and the change is recorded in the intelligence history, so
    /// "we used to trust this" survives (ADR-0030).
    /// </remarks>
    public void AssessReliability(
        SourceReliability reliability,
        UserId assessedBy,
        DateTimeOffset now,
        int expectedVersion,
        string? rationale = null)
    {
        RequireVersion(expectedVersion);

        if (!Enum.IsDefined(reliability))
        {
            throw new DomainException($"'{reliability}' is not a reliability assessment.");
        }

        if (reliability == SourceReliability.Unassessed)
        {
            throw new DomainException(
                "Unassessed is the absence of an assessment, not one somebody can record.");
        }

        Reliability = reliability;
        ReliabilityRationale = Ensure.OptionalMax(rationale, nameof(rationale), 1000);
        ReliabilityAssessedBy = assessedBy;
        ReliabilityAssessedAt = now;

        Version++;
    }

    /// <summary>Corrects what a person recorded about the source.</summary>
    public void Update(
        string title,
        IntelligenceSensitivity sensitivity,
        int expectedVersion,
        string? notes = null)
    {
        RequireVersion(expectedVersion);

        Title = Ensure.NotBlankMax(title, nameof(title), 300);
        Sensitivity = RequireSensitivity(sensitivity);
        Notes = Ensure.OptionalMax(notes, nameof(notes), 4000);

        Version++;
    }

    private static IntelligenceSource Start(
        OrganizationId organizationId,
        IntelligenceSourceKind kind,
        string title,
        UserId recordedBy,
        DateTimeOffset now,
        IntelligenceSensitivity sensitivity,
        DateTimeOffset? observedAt,
        DateTimeOffset? publishedAt,
        string? notes)
    {
        DateTimeOffset observed = observedAt ?? now;

        // A source cannot have been seen before it was published. The reverse is
        // ordinary - most things are read after publication - so only this
        // direction is refused.
        if (publishedAt is { } published && published > observed)
        {
            throw new DomainException(
                "A source cannot have been observed before it was published.");
        }

        return new IntelligenceSource
        {
            Id = IntelligenceSourceId.New(),
            OrganizationId = organizationId,
            Kind = kind,
            Title = Ensure.NotBlankMax(title, nameof(title), 300),
            PublishedAt = publishedAt,
            ObservedAt = observed,
            RecordedAt = now,
            Reliability = SourceReliability.Unassessed,
            Sensitivity = RequireSensitivity(sensitivity),
            Notes = Ensure.OptionalMax(notes, nameof(notes), 4000),
            RecordedBy = recordedBy,
            Version = 1,
        };
    }

    private static IntelligenceSensitivity RequireSensitivity(IntelligenceSensitivity sensitivity)
    {
        if (!Enum.IsDefined(sensitivity))
        {
            throw new DomainException($"'{sensitivity}' is not a sensitivity.");
        }

        return sensitivity;
    }

    private void RequireVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(
                nameof(IntelligenceSource), Id.ToString(), expectedVersion, Version);
        }
    }
}
