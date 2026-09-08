using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Application.Abstractions;

/// <summary>Reads and writes evidence records.</summary>
public interface IIntelligenceSourceRepository
{
    void Add(IntelligenceSource source);

    Task<IntelligenceSource?> FindAsync(
        OrganizationId organizationId,
        IntelligenceSourceId id,
        CancellationToken cancellationToken = default);

    /// <summary>Whether every one of these sources exists in this tenant.</summary>
    /// <remarks>
    /// Asked before a signal cites them, so a cross-tenant citation is refused as a
    /// sentence rather than as a foreign-key violation on save (ADR-0030).
    /// </remarks>
    Task<bool> AllExistAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<IntelligenceSourceId> ids,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes signals with their subjects and evidence.</summary>
public interface ISignalRepository
{
    void Add(Signal signal);

    Task<Signal?> FindAsync(
        OrganizationId organizationId,
        SignalId id,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        OrganizationId organizationId,
        SignalId id,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes theses with their revisions, subjects and evidence.</summary>
public interface IThesisRepository
{
    void Add(Thesis thesis);

    Task<Thesis?> FindAsync(
        OrganizationId organizationId,
        ThesisId id,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes predictions with their forecast history.</summary>
public interface IPredictionRepository
{
    void Add(Prediction prediction);

    Task<Prediction?> FindAsync(
        OrganizationId organizationId,
        PredictionId id,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes watchlists and their membership.</summary>
public interface IWatchlistRepository
{
    void Add(Watchlist watchlist);

    Task<Watchlist?> FindAsync(
        OrganizationId organizationId,
        WatchlistId id,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes talent radar entries.</summary>
public interface ITalentRadarRepository
{
    void Add(TalentRadarEntry entry);

    Task<TalentRadarEntry?> FindAsync(
        OrganizationId organizationId,
        TalentRadarEntryId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The open radar entry for a person, if there is one.
    /// </summary>
    /// <remarks>
    /// One person is watched once at a time. Two open entries for the same person
    /// would be two analysts researching in parallel without knowing, and the
    /// second conversion would fail confusingly rather than the second entry being
    /// refused clearly (ADR-0030).
    /// </remarks>
    Task<TalentRadarEntry?> FindOpenForPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads and writes research cases with their subjects and links.</summary>
public interface IResearchCaseRepository
{
    void Add(ResearchCase researchCase);

    Task<ResearchCase?> FindAsync(
        OrganizationId organizationId,
        ResearchCaseId id,
        CancellationToken cancellationToken = default);
}

/// <summary>Appends the curated intelligence history.</summary>
/// <remarks>
/// Append-only by construction: there is no update and no delete. This is a human
/// projection and not the audit trail, which lives on its own and answers a
/// different question (ADR-0012, ADR-0030).
/// </remarks>
public interface IIntelligenceEventRepository
{
    void Add(IntelligenceEvent entry);
}

/// <summary>
/// Proves that something an intelligence object names actually exists here.
/// </summary>
/// <remarks>
/// The subject arc carries a real composite foreign key per kind, so the database
/// refuses a cross-tenant subject on its own. This exists so the refusal is a
/// sentence a person can read rather than a constraint violation, which is the same
/// reason M10's link validator exists (ADR-0025, ADR-0030).
/// </remarks>
public interface IIntelligenceSubjectValidator
{
    Task<bool> ExistsAsync(
        OrganizationId organizationId,
        IntelligenceSubjectKind kind,
        Guid subjectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Short names for the subjects an intelligence list is about.
    /// </summary>
    /// <remarks>
    /// Names only: never a figure, a clause, a classification or a status. A
    /// subject label is rendered beside intelligence whose permissions differ from
    /// the subject's own, so anything richer than a name would leak through the
    /// label (ADR-0025, ADR-0030).
    /// </remarks>
    Task<IReadOnlyDictionary<(IntelligenceSubjectKind Kind, Guid SubjectId), string>> DescribeAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<(IntelligenceSubjectKind Kind, Guid SubjectId)> subjects,
        CancellationToken cancellationToken = default);
}
