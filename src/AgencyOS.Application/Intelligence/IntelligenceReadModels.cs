using AgencyOS.Domain.Intelligence;

namespace AgencyOS.Application.Intelligence;

/// <summary>One subject an intelligence object concerns, with a readable name.</summary>
/// <param name="Label">
/// A name and nothing more. Never a figure, a status or a classification: a subject
/// label is rendered beside intelligence whose permissions differ from the
/// subject's own (ADR-0030).
/// </param>
public sealed record IntelligenceSubjectModel(
    Guid Id,
    IntelligenceSubjectKind Kind,
    Guid SubjectId,
    string Label,
    string? Note);

/// <param name="IsHeldByAgencyOS">
/// Whether AgencyOS actually holds the evidence or only a pointer to it. A URL is a
/// reference: the page is not archived, not hashed and not preserved, and this
/// field is what stops a reader assuming otherwise (ADR-0030).
/// </param>
public sealed record IntelligenceSourceModel(
    IntelligenceSourceId Id,
    IntelligenceSourceKind Kind,
    string Title,
    Guid? DocumentVersionId,
    Guid? MessageId,
    string? Url,
    string? Publisher,
    string? Author,
    string? ExternalReference,
    DateTimeOffset? PublishedAt,
    DateTimeOffset ObservedAt,
    DateTimeOffset RecordedAt,
    SourceReliability Reliability,
    string? ReliabilityRationale,
    string? ReliabilityAssessedByDisplayName,
    DateTimeOffset? ReliabilityAssessedAt,
    IntelligenceSensitivity Sensitivity,
    string? Notes,
    bool IsHeldByAgencyOS,
    string? RecordedByDisplayName,
    int SignalCount,
    int Version);

/// <summary>A source attached to a signal, and what it does for the claim.</summary>
/// <param name="Excerpt">
/// An analyst's selection, never the whole source. Null when the caller may not
/// read the underlying M10 artifact: an excerpt of a confidential contract is still
/// the confidential contract (ADR-0025, ADR-0030).
/// </param>
public sealed record SignalEvidenceModel(
    Guid Id,
    IntelligenceSourceId SourceId,
    string SourceTitle,
    IntelligenceSourceKind SourceKind,
    SourceReliability SourceReliability,
    SignalEvidenceRole Role,
    string? Excerpt,
    bool ExcerptWithheld,
    string? Locator,
    DateTimeOffset AddedAt,
    string? AddedByDisplayName);

public sealed record SignalSummaryModel(
    SignalId Id,
    string Title,
    string Claim,
    SignalKind Kind,
    SignalVerification Verification,
    SignalConfidence Confidence,
    IntelligenceSensitivity Sensitivity,
    DateTimeOffset? OccurredAt,
    DateTimeOffset ObservedAt,
    DateTimeOffset RecordedAt,
    string? RecordedByDisplayName,
    IReadOnlyList<IntelligenceSubjectModel> Subjects,
    int EvidenceCount,
    int Version);

public sealed record SignalDetailModel(
    SignalSummaryModel Signal,
    string? Notes,
    string? VerificationNote,
    string? VerificationChangedByDisplayName,
    DateTimeOffset? VerificationChangedAt,
    IReadOnlyList<SignalEvidenceModel> Evidence,
    IReadOnlyList<IntelligenceEventModel> History);

/// <summary>One stated position of a thesis. Immutable once written.</summary>
public sealed record ThesisRevisionModel(
    Guid Id,
    int Sequence,
    string Proposition,
    string? Rationale,
    ThesisConfidence Confidence,
    string? ChangeNote,
    DateTimeOffset RecordedAt,
    string? RecordedByDisplayName);

/// <param name="Stance">
/// Supports, Challenges or Context. Never summed: five weak signals do not
/// mechanically outweigh one strong contradiction (ADR-0030).
/// </param>
public sealed record ThesisEvidenceModel(
    Guid Id,
    SignalId SignalId,
    string SignalTitle,
    SignalVerification SignalVerification,
    ThesisEvidenceStance Stance,
    string? Note,
    DateTimeOffset AddedAt,
    string? AddedByDisplayName);

public sealed record ThesisSummaryModel(
    ThesisId Id,
    string Title,
    string Proposition,
    ThesisStatus Status,
    ThesisConfidence Confidence,
    IntelligenceSensitivity Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<IntelligenceSubjectModel> Subjects,
    int RevisionCount,
    int SupportingCount,
    int ChallengingCount,
    int Version);

public sealed record ThesisDetailModel(
    ThesisSummaryModel Thesis,
    string? Rationale,
    string? ClosedReason,
    DateTimeOffset? ClosedAt,
    Guid? SupersededByThesisId,
    IReadOnlyList<ThesisRevisionModel> Revisions,
    IReadOnlyList<ThesisEvidenceModel> Evidence,
    IReadOnlyList<IntelligenceEventModel> History);

/// <summary>One forecast, at a moment, by a person. Immutable once written.</summary>
/// <param name="Probability">
/// A decimal from 0 to 1. It is an analyst's assertion, never a model output and
/// never AgencyOS's own view, which is why the forecaster and the date travel with
/// it everywhere (ADR-0030).
/// </param>
public sealed record PredictionRevisionModel(
    Guid Id,
    int Sequence,
    decimal Probability,
    string? Rationale,
    DateTimeOffset RecordedAt,
    string? RecordedByDisplayName);

public sealed record PredictionEvidenceModel(
    Guid Id,
    IntelligenceSourceId? SourceId,
    SignalId? SignalId,
    string Label,
    string? Note,
    DateTimeOffset AddedAt,
    string? AddedByDisplayName);

/// <param name="CurrentProbability">
/// Derived from the latest revision, never stored. A stored copy would be a second
/// truth that drifts, and would erase the forecasting history calibration depends
/// on (ADR-0023, ADR-0030).
/// </param>
/// <param name="BrierScore">
/// Null until resolved Yes or No. An unresolvable prediction is never scored:
/// treating it as half right would manufacture a number from an absence.
/// </param>
public sealed record PredictionSummaryModel(
    PredictionId Id,
    string Statement,
    DateTimeOffset ResolvesBy,
    PredictionStatus Status,
    decimal CurrentProbability,
    DateTimeOffset? CurrentProbabilityAsOf,
    string? CurrentProbabilityByDisplayName,
    PredictionOutcome? Outcome,
    DateTimeOffset? ResolvedAt,
    decimal? BrierScore,
    IntelligenceSensitivity Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<IntelligenceSubjectModel> Subjects,
    int RevisionCount,
    int Version);

public sealed record PredictionDetailModel(
    PredictionSummaryModel Prediction,
    string? ResolutionCriteria,
    string? ResolutionNote,
    string? ResolvedByDisplayName,
    string? CancelledReason,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<PredictionRevisionModel> Revisions,
    IReadOnlyList<PredictionEvidenceModel> Evidence,
    IReadOnlyList<IntelligenceEventModel> History);

/// <summary>
/// Deterministic calibration over resolved binary predictions.
/// </summary>
/// <remarks>
/// Arithmetic and a sample count, with no qualitative label anywhere. There is no
/// "well calibrated" and no "strong forecaster": a mean Brier score over eleven
/// predictions says very little, and the count travels beside the number so a
/// reader can see that for themselves (ADR-0030).
/// </remarks>
public sealed record PredictionCalibrationModel(
    int ResolvedCount,
    int YesCount,
    int NoCount,
    int UnresolvableCount,
    int OpenCount,
    decimal? MeanBrierScore,
    decimal? MeanProbability,
    decimal? ObservedFrequency);

public sealed record WatchlistSummaryModel(
    WatchlistId Id,
    string Name,
    string? Purpose,
    WatchlistStatus Status,
    IntelligenceSensitivity Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset? LastReviewedAt,
    string? LastReviewedByDisplayName,
    DateTimeOffset CreatedAt,
    int EntryCount,
    int Version);

public sealed record WatchlistDetailModel(
    WatchlistSummaryModel Watchlist,
    IReadOnlyList<IntelligenceSubjectModel> Entries,
    IReadOnlyList<IntelligenceEventModel> History);

/// <summary>
/// What has happened around the things a watchlist watches.
/// </summary>
/// <remarks>
/// Derived by subject overlap at read time. Nothing is stored on a signal saying it
/// is watched, because such a flag would have to be maintained on every watchlist
/// change and would be silently wrong the first time one was missed (ADR-0030).
/// </remarks>
public sealed record WatchlistActivityModel(
    WatchlistSummaryModel Watchlist,
    IReadOnlyList<SignalSummaryModel> RecentSignals,
    int SignalsSinceLastReview,
    IReadOnlyList<ThesisSummaryModel> ActiveTheses,
    IReadOnlyList<PredictionSummaryModel> OpenPredictions,
    DateTimeOffset? NewestSignalAt);

/// <summary>
/// What is factually known about a working relationship.
/// </summary>
/// <remarks>
/// <para>
/// Dimensions, never a score. There is no <c>RelationshipHealth</c>, no
/// <c>Affinity</c> and no <c>InfluenceScore</c> anywhere in M11: a composite number
/// would be arithmetic over incommensurable things, and its apparent precision
/// would be believed (ADR-0030).
/// </para>
/// <para>
/// <strong>Recorded assessment and derived activity are kept apart.</strong>
/// <see cref="RecordedStrength"/> is what a person wrote down. Everything else is
/// counted from M2 rows. Fourteen emails is not a strong relationship, and the
/// model refuses to say it is.
/// </para>
/// </remarks>
public sealed record RelationshipIntelligenceModel(
    Guid SubjectId,
    IntelligenceSubjectKind Kind,
    string DisplayName,
    string? Title,
    string? CompanyName,

    // ---- recorded by a person ----
    string? RecordedStrength,
    string? RecordedRelationshipNote,
    string? RelationshipOwnerDisplayName,

    // ---- derived from M2 rows ----
    DateTimeOffset? LastInteractionAt,
    string? LastInteractionKind,
    int? DaysSinceLastInteraction,
    int Interactions30Days,
    int Interactions90Days,
    int Interactions365Days,
    IReadOnlyList<string> RecentInteractionKinds,
    int OpenTaskCount,
    int OverdueTaskCount,
    DateTimeOffset? NextTaskDueAt,

    // ---- intelligence ----
    IReadOnlyList<SignalSummaryModel> RecentSignals,
    IReadOnlyList<WatchlistSummaryModel> Watchlists,
    IReadOnlyList<ThesisSummaryModel> ActiveTheses,
    IReadOnlyList<PredictionSummaryModel> OpenPredictions);

public sealed record TalentRadarSummaryModel(
    TalentRadarEntryId Id,
    Guid PersonId,
    string PersonDisplayName,
    string? PersonTitle,
    string? CompanyName,
    TalentRadarStatus Status,
    TalentRadarPriority Priority,
    string Rationale,
    string? IntendedDisciplines,
    IntelligenceSensitivity Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset? LastReviewedAt,
    Guid? ProspectId,
    int SignalCount,
    int Version);

/// <param name="Credits">
/// Projected from M4, never copied. A second copy of somebody's credits would go
/// stale the first time the real one was corrected (ADR-0030).
/// </param>
public sealed record TalentRadarDetailModel(
    TalentRadarSummaryModel Entry,
    string? DismissedReason,
    DateTimeOffset? ConvertedAt,
    string? ConvertedByDisplayName,
    IReadOnlyList<RadarCreditModel> Credits,
    IReadOnlyList<string> Disciplines,
    IReadOnlyList<SignalSummaryModel> Signals,
    IReadOnlyList<ThesisSummaryModel> Theses,
    IReadOnlyList<PredictionSummaryModel> Predictions,
    IReadOnlyList<WatchlistSummaryModel> Watchlists,
    IReadOnlyList<IntelligenceEventModel> History);

/// <summary>A credit, as M4 holds it.</summary>
public sealed record RadarCreditModel(
    Guid Id,
    string Title,
    string? Role,
    string? Kind,
    int? Year);

public sealed record ResearchCaseSummaryModel(
    ResearchCaseId Id,
    string Question,
    ResearchCaseStatus Status,
    IntelligenceSensitivity Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<IntelligenceSubjectModel> Subjects,
    int SourceCount,
    int SignalCount,
    int ThesisCount,
    int PredictionCount,
    int TaskCount,
    int OpenTaskCount,
    int Version);

public sealed record ResearchCaseDetailModel(
    ResearchCaseSummaryModel ResearchCase,
    string? Context,
    string? Conclusion,
    IReadOnlyList<IntelligenceSourceModel> Sources,
    IReadOnlyList<SignalSummaryModel> Signals,
    IReadOnlyList<ThesisSummaryModel> Theses,
    IReadOnlyList<PredictionSummaryModel> Predictions,
    IReadOnlyList<ResearchTaskModel> Tasks,
    IReadOnlyList<IntelligenceEventModel> History);

/// <summary>A task attached to a research case, as M2 holds it.</summary>
public sealed record ResearchTaskModel(
    Guid Id,
    string Title,
    string State,
    DateTimeOffset? DueAt,
    bool IsOverdue,
    string? AssignedToDisplayName);

public sealed record IntelligenceEventModel(
    DateTimeOffset OccurredAt,
    IntelligenceEventKind Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <summary>
/// What the intelligence desk has to look at.
/// </summary>
/// <remarks>
/// Counts and real rows. Nothing here is ranked, scored or prioritized by the
/// system: there is no AI priority, no opportunity probability and no hot-talent
/// score. M11 records what is known and leaves the judgment where it belongs
/// (ADR-0030).
/// </remarks>
public sealed record IntelligenceCommandCenterModel(
    IReadOnlyList<PredictionSummaryModel> PredictionsAwaitingResolution,
    IReadOnlyList<PredictionSummaryModel> PredictionsDueSoon,
    IReadOnlyList<SignalSummaryModel> DisputedSignals,
    IReadOnlyList<TalentRadarSummaryModel> RadarAwaitingReview,
    IReadOnlyList<WatchlistSummaryModel> WatchlistsWithNewActivity,
    IReadOnlyList<ResearchCaseSummaryModel> ResearchCasesWithOverdueTasks,
    int AwaitingResolutionCount,
    int DueSoonCount,
    int DisputedSignalCount,
    int RadarAwaitingReviewCount);

// ------------------------------------------------------------------- filters

/// <param name="TextContains">
/// Matches the title and the claim, and only within the classifications the caller
/// may already read. Never an excerpt: an excerpt is a quotation from an M10
/// artifact, and matching one would report its contents to whoever ran the search
/// (ADR-0025, ADR-0030).
/// </param>
public sealed record SignalFilter(
    SignalKind? Kind = null,
    SignalVerification? Verification = null,
    IntelligenceSensitivity? Sensitivity = null,
    IntelligenceSubjectKind? SubjectKind = null,
    Guid? SubjectId = null,
    IntelligenceSourceId? SourceId = null,
    Guid? RecordedByUserId = null,
    Guid? WatchlistId = null,
    DateOnly? ObservedAfter = null,
    DateOnly? ObservedBefore = null,
    DateOnly? OccurredAfter = null,
    DateOnly? OccurredBefore = null,
    string? TextContains = null);

public sealed record SourceFilter(
    IntelligenceSourceKind? Kind = null,
    SourceReliability? Reliability = null,
    IntelligenceSensitivity? Sensitivity = null,
    Guid? RecordedByUserId = null,
    DateOnly? ObservedAfter = null,
    DateOnly? ObservedBefore = null,
    string? TextContains = null);

public sealed record ThesisFilter(
    ThesisStatus? Status = null,
    ThesisConfidence? Confidence = null,
    IntelligenceSensitivity? Sensitivity = null,
    IntelligenceSubjectKind? SubjectKind = null,
    Guid? SubjectId = null,
    Guid? OwnerUserId = null,
    DateOnly? UpdatedAfter = null,
    string? TextContains = null);

/// <param name="ProbabilityAtLeast">
/// Narrows by the current forecast. Applied only within the classifications the
/// caller may already read, so a probability filter cannot be used to probe for
/// predictions they cannot see (ADR-0030).
/// </param>
public sealed record PredictionFilter(
    PredictionStatus? Status = null,
    PredictionOutcome? Outcome = null,
    IntelligenceSensitivity? Sensitivity = null,
    IntelligenceSubjectKind? SubjectKind = null,
    Guid? SubjectId = null,
    Guid? OwnerUserId = null,
    DateOnly? ResolvesAfter = null,
    DateOnly? ResolvesBefore = null,
    decimal? ProbabilityAtLeast = null,
    decimal? ProbabilityAtMost = null,
    string? TextContains = null);

public sealed record WatchlistFilter(
    WatchlistStatus? Status = null,
    IntelligenceSensitivity? Sensitivity = null,
    Guid? OwnerUserId = null,
    IntelligenceSubjectKind? SubjectKind = null,
    Guid? SubjectId = null,
    DateOnly? ReviewedBefore = null,
    string? TextContains = null);

public sealed record TalentRadarFilter(
    TalentRadarStatus? Status = null,
    TalentRadarPriority? Priority = null,
    IntelligenceSensitivity? Sensitivity = null,
    Guid? OwnerUserId = null,
    Guid? PersonId = null,
    Guid? WatchlistId = null,
    string? Discipline = null,
    DateOnly? ReviewedBefore = null,
    string? TextContains = null);

public sealed record ResearchCaseFilter(
    ResearchCaseStatus? Status = null,
    IntelligenceSensitivity? Sensitivity = null,
    Guid? OwnerUserId = null,
    IntelligenceSubjectKind? SubjectKind = null,
    Guid? SubjectId = null,
    string? TextContains = null);

/// <summary>What the caller may read, plus what they asked for.</summary>
/// <remarks>
/// Carried into the query so the classification filter is applied in SQL, before
/// anything is counted or paged (ADR-0030).
/// </remarks>
public sealed record IntelligenceScope<TFilter>(
    IReadOnlySet<IntelligenceSensitivity> Readable,
    TFilter Filter);
