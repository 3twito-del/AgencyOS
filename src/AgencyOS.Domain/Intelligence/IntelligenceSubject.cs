using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Intelligence;

/// <summary>
/// What a piece of intelligence can be about.
/// </summary>
/// <remarks>
/// <para>
/// A closed, reviewed set, on the M10 precedent. Each member has a real column and
/// a real composite foreign key behind it, so a subject cannot name a record that
/// does not exist or one belonging to another tenant. That is the whole reason this
/// is an enum rather than an entity name beside a bare GUID (ADR-0030).
/// </para>
/// <para>
/// It deliberately is <strong>not</strong> <c>DocumentLinkTarget</c>. The two
/// overlap heavily and answer different questions — "what is this document filed
/// against" and "what is this claim about" — and they will diverge. Sharing one
/// enum would mean every future member of either had to make sense for both.
/// </para>
/// </remarks>
public enum IntelligenceSubjectKind
{
    Person = 1,
    Company = 2,
    TalentProfile = 3,
    Project = 4,
    SourceProperty = 5,
    Package = 6,
    ProjectRole = 7,
    Opportunity = 8,
    Deal = 9,
    Contract = 10,
}

/// <summary>
/// One thing an intelligence object is about.
/// </summary>
/// <remarks>
/// <para>
/// Intelligence is rarely about one record. "Studio X acquires Project Y" concerns
/// a company and a project, and forcing a single primary subject would lose half
/// the fact. Every intelligence object therefore carries a set of these, and none
/// of them is privileged over the others.
/// </para>
/// <para>
/// One row carries a discriminator and exactly one typed identifier, checked by the
/// database. This is the same shape <c>DocumentLink</c> uses and for the same
/// reason: an untyped <c>(kind, guid)</c> pair has no referential integrity and
/// happily points at a deleted or foreign-tenant row (ADR-0025, ADR-0030).
/// </para>
/// </remarks>
public abstract class IntelligenceSubject
{
    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>Which kind of record this is about.</summary>
    public IntelligenceSubjectKind Kind { get; private set; }

    /// <summary>
    /// The record's identifier.
    /// </summary>
    /// <remarks>
    /// Persisted into the typed column matching <see cref="Kind"/>, which carries
    /// the composite tenant foreign key. This property is the domain's single
    /// readable view of whichever column that was.
    /// </remarks>
    public Guid SubjectId { get; private set; }

    /// <summary>Why this subject is named, when somebody said.</summary>
    public string? Note { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public UserId AddedBy { get; private set; }

    /// <summary>Fills the shared fields. Each owner supplies its own key.</summary>
    private protected void Initialize(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"'{kind}' is not something intelligence can be about.");
        }

        if (subjectId == Guid.Empty)
        {
            throw new DomainException("A subject must name the record it points at.");
        }

        Id = Guid.CreateVersion7();
        OrganizationId = organizationId;
        Kind = kind;
        SubjectId = subjectId;
        Note = Ensure.OptionalMax(note, nameof(note), 500);
        AddedAt = now;
        AddedBy = addedBy;
    }
}

/// <summary>A subject a <see cref="Signal"/> concerns.</summary>
public sealed class SignalSubject : IntelligenceSubject
{
    private SignalSubject()
    {
    }

    public SignalId SignalId { get; private set; }

    internal static SignalSubject Create(
        OrganizationId organizationId,
        SignalId signalId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        SignalSubject subject = new() { SignalId = signalId };

        subject.Initialize(organizationId, kind, subjectId, addedBy, now, note);

        return subject;
    }
}

/// <summary>A subject a <see cref="Thesis"/> concerns.</summary>
public sealed class ThesisSubject : IntelligenceSubject
{
    private ThesisSubject()
    {
    }

    public ThesisId ThesisId { get; private set; }

    internal static ThesisSubject Create(
        OrganizationId organizationId,
        ThesisId thesisId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        ThesisSubject subject = new() { ThesisId = thesisId };

        subject.Initialize(organizationId, kind, subjectId, addedBy, now, note);

        return subject;
    }
}

/// <summary>A subject a <see cref="Prediction"/> concerns.</summary>
public sealed class PredictionSubject : IntelligenceSubject
{
    private PredictionSubject()
    {
    }

    public PredictionId PredictionId { get; private set; }

    internal static PredictionSubject Create(
        OrganizationId organizationId,
        PredictionId predictionId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        PredictionSubject subject = new() { PredictionId = predictionId };

        subject.Initialize(organizationId, kind, subjectId, addedBy, now, note);

        return subject;
    }
}

/// <summary>
/// Something a <see cref="Watchlist"/> is deliberately monitoring.
/// </summary>
/// <remarks>
/// Membership is explicit. M11 has no rule engine and no filter language of its
/// own: a watchlist is a list somebody put things on, and what is new about those
/// things is derived by subject overlap rather than stored on the signal
/// (ADR-0030).
/// </remarks>
public sealed class WatchlistEntry : IntelligenceSubject
{
    private WatchlistEntry()
    {
    }

    public WatchlistId WatchlistId { get; private set; }

    internal static WatchlistEntry Create(
        OrganizationId organizationId,
        WatchlistId watchlistId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        WatchlistEntry entry = new() { WatchlistId = watchlistId };

        entry.Initialize(organizationId, kind, subjectId, addedBy, now, note);

        return entry;
    }
}

/// <summary>A subject a <see cref="ResearchCase"/> is investigating.</summary>
public sealed class ResearchCaseSubject : IntelligenceSubject
{
    private ResearchCaseSubject()
    {
    }

    public ResearchCaseId ResearchCaseId { get; private set; }

    internal static ResearchCaseSubject Create(
        OrganizationId organizationId,
        ResearchCaseId researchCaseId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        UserId addedBy,
        DateTimeOffset now,
        string? note = null)
    {
        ResearchCaseSubject subject = new() { ResearchCaseId = researchCaseId };

        subject.Initialize(organizationId, kind, subjectId, addedBy, now, note);

        return subject;
    }
}
