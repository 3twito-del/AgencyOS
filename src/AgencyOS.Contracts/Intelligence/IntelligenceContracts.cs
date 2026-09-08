namespace AgencyOS.Contracts.Intelligence;

// ------------------------------------------------------------------- requests

/// <param name="Kind">
/// Person, Company, TalentProfile, Project, SourceProperty, Package, ProjectRole,
/// Opportunity, Deal or Contract.
/// </param>
public sealed record IntelligenceSubjectRequest(
    string Kind,
    Guid SubjectId,
    string? Note = null);

/// <summary>
/// Records a piece of evidence.
/// </summary>
/// <remarks>
/// <para>
/// A source is never labelled true. It is labelled <em>what it is</em> — a stored
/// document version, a synchronized message, a page somewhere else, or what
/// somebody observed — and how reliable somebody judged it to be, separately and
/// with their name on it (§1, ADR-0030).
/// </para>
/// <para>
/// The three times are kept apart on purpose. When it was published, when the
/// agency observed it, and when the row was written are three different facts, and
/// collapsing them loses the one that matters in a dispute.
/// </para>
/// </remarks>
/// <param name="Kind">
/// DocumentVersion, Message, ExternalUrl, ManualObservation or Other.
/// </param>
/// <param name="Sensitivity">
/// Internal, Confidential, SourceSensitive or Restricted. Always stated, never
/// inferred from the content: whether somebody spoke in confidence is something
/// they said, not something a system can detect (§26).
/// </param>
public sealed record RecordSourceRequest(
    string Kind,
    string Title,
    string Sensitivity,
    Guid? DocumentVersionId = null,
    Guid? MessageId = null,
    string? Url = null,
    string? Publisher = null,
    string? Author = null,
    string? ExternalReference = null,
    DateTimeOffset? PublishedAt = null,
    DateTimeOffset? ObservedAt = null,
    string? Notes = null);

/// <param name="Reliability">Low, Medium, High or Primary. Never Unassessed.</param>
public sealed record AssessSourceReliabilityRequest(
    string Reliability,
    int ExpectedVersion,
    string? Rationale = null);

public sealed record UpdateSourceRequest(
    string Title,
    string Sensitivity,
    int ExpectedVersion,
    string? Notes = null);

/// <param name="Role">Primary, Corroborating, Contradicting or Context.</param>
/// <param name="Excerpt">
/// An analyst's selection from the source, never the whole of it.
/// </param>
public sealed record SignalEvidenceRequest(
    Guid SourceId,
    string Role = "Primary",
    string? Excerpt = null,
    string? Locator = null);

/// <summary>
/// Records an observed claim, with the evidence for it.
/// </summary>
/// <remarks>
/// At least one source is required and the requirement never relaxes. A claim with
/// no provenance is a rumour with a database row, and the whole of M11 exists to
/// refuse that (§1).
/// </remarks>
/// <param name="Kind">
/// PersonnelMove, ProjectStatus, CorporateAction, DealActivity, MarketAppetite,
/// TalentActivity, CreditOrRecognition or Observation.
/// </param>
/// <param name="Confidence">
/// Unstated, Low, Medium or High. What a person judged, never what a model output.
/// </param>
public sealed record RecordSignalRequest(
    string Title,
    string Claim,
    string Kind,
    string Sensitivity,
    IReadOnlyList<SignalEvidenceRequest> Evidence,
    IReadOnlyList<IntelligenceSubjectRequest>? Subjects = null,
    DateTimeOffset? OccurredAt = null,
    DateTimeOffset? ObservedAt = null,
    string Confidence = "Unstated",
    string? Notes = null);

public sealed record UpdateSignalRequest(
    string Title,
    string Claim,
    string Kind,
    string Sensitivity,
    string Confidence,
    int ExpectedVersion,
    string? Notes = null);

/// <summary>
/// Changes what corroboration a signal has.
/// </summary>
/// <remarks>
/// <strong>There is no Verified.</strong> Corroborated means somebody else's
/// independent evidence agrees; it does not mean the claim is true, and offering a
/// word that reads as "true" would invite exactly that reading (§1, ADR-0030).
/// </remarks>
/// <param name="Verification">Unverified, Corroborated, Disputed or Retracted.</param>
public sealed record ChangeSignalVerificationRequest(
    string Verification,
    int ExpectedVersion,
    string? Note = null);

public sealed record LinkSignalEvidenceRequest(
    Guid SourceId,
    string Role,
    int ExpectedVersion,
    string? Excerpt = null,
    string? Locator = null);


public sealed record AddIntelligenceSubjectRequest(
    string Kind,
    Guid SubjectId,
    int ExpectedVersion,
    string? Note = null);


/// <summary>
/// States a view the agency holds.
/// </summary>
/// <remarks>
/// A thesis is never a fact and its status says so: Draft, Active, Retired or
/// Superseded. There is no True and no False, because a thesis is a position
/// somebody holds and the honest way to abandon one is to record why (§1).
/// </remarks>
/// <param name="Confidence">Unstated, Low, Medium or High.</param>
public sealed record CreateThesisRequest(
    string Title,
    string Proposition,
    string Sensitivity,
    Guid? OwnerUserId = null,
    string Confidence = "Unstated",
    string? Rationale = null,
    IReadOnlyList<IntelligenceSubjectRequest>? Subjects = null);

public sealed record ActivateThesisRequest(int ExpectedVersion);

/// <summary>Records a new position. Never edits the old one.</summary>
public sealed record ReviseThesisRequest(
    string Proposition,
    string Confidence,
    int ExpectedVersion,
    string? Rationale = null,
    string? ChangeNote = null);

/// <param name="SupersededBy">
/// The thesis that replaced this one, when there is one. Retires it rather than
/// deleting it: what the agency used to think is part of the record.
/// </param>
public sealed record CloseThesisRequest(
    string Reason,
    int ExpectedVersion,
    Guid? SupersededBy = null);

/// <param name="Stance">Supports, Challenges or Context.</param>
public sealed record LinkThesisEvidenceRequest(
    Guid SignalId,
    string Stance,
    int ExpectedVersion,
    string? Note = null);


/// <summary>
/// States something falsifiable, with a date and a probability.
/// </summary>
/// <remarks>
/// The probability is a decimal from 0 to 1, and it is an assertion by the person
/// making it. AgencyOS neither produces one nor holds one of its own: it records
/// what a forecaster said, keeps every revision, and scores the result afterwards
/// (§11, ADR-0030).
/// </remarks>
public sealed record CreatePredictionRequest(
    string Statement,
    DateTimeOffset ResolvesBy,
    decimal Probability,
    string Sensitivity,
    Guid? OwnerUserId = null,
    string? ResolutionCriteria = null,
    string? Rationale = null,
    IReadOnlyList<IntelligenceSubjectRequest>? Subjects = null);

/// <summary>States a new probability. The previous one stays on the record.</summary>
public sealed record RecordPredictionRevisionRequest(
    decimal Probability,
    int ExpectedVersion,
    string? Rationale = null);

/// <param name="Outcome">
/// Yes, No or Unresolvable. Unresolvable is a real answer and is never scored:
/// treating it as half right would manufacture a number from an absence (§14).
/// </param>
public sealed record ResolvePredictionRequest(
    string Outcome,
    int ExpectedVersion,
    string? Note = null,
    IReadOnlyList<PredictionEvidenceRequest>? Evidence = null);

/// <summary>Exactly one of the two, never both and never neither.</summary>
public sealed record PredictionEvidenceRequest(
    Guid? SourceId = null,
    Guid? SignalId = null,
    string? Note = null);

public sealed record CancelPredictionRequest(string Reason, int ExpectedVersion);

public sealed record CreateWatchlistRequest(
    string Name,
    string Sensitivity,
    Guid? OwnerUserId = null,
    string? Purpose = null,
    IReadOnlyList<IntelligenceSubjectRequest>? Entries = null);

public sealed record UpdateWatchlistRequest(
    string Name,
    string Sensitivity,
    int ExpectedVersion,
    string? Purpose = null);

public sealed record RecordReviewRequest(int ExpectedVersion);

public sealed record ArchiveWatchlistRequest(int ExpectedVersion);

/// <summary>
/// Puts somebody on the radar.
/// </summary>
/// <remarks>
/// A radar entry is a person somebody is looking at, with a written reason. It
/// carries no ranking, no score and no fit percentage: whether to pursue somebody
/// is a judgment the agency makes, and a number would launder it into a
/// recommendation (§18).
/// </remarks>
/// <param name="Priority">Unassigned, Low, Medium or High. Set by a person.</param>
public sealed record CreateRadarEntryRequest(
    Guid PersonId,
    string Rationale,
    string Sensitivity,
    Guid? OwnerUserId = null,
    string? IntendedDisciplines = null,
    string Priority = "Unassigned",
    DateTimeOffset? FirstObservedAt = null);

public sealed record UpdateRadarEntryRequest(
    string Rationale,
    string Sensitivity,
    string Priority,
    int ExpectedVersion,
    string? IntendedDisciplines = null);

/// <param name="Status">
/// Watching, Researching or ReadyForReview. Conversion and dismissal have their own
/// operations, because both do more than move a status.
/// </param>
public sealed record ChangeRadarStatusRequest(string Status, int ExpectedVersion);

public sealed record DismissRadarEntryRequest(string Reason, int ExpectedVersion);

/// <summary>
/// Hands a radar entry to M4 as a prospect.
/// </summary>
/// <remarks>
/// The seam between "we are looking at this person" and "we are pursuing them".
/// The radar stops there deliberately: courting, signing and representation are
/// M4's, and a second pursuit pipeline would be two systems disagreeing about the
/// same relationship (§40).
/// </remarks>
public sealed record ConvertRadarEntryRequest(
    int ExpectedVersion,
    Guid? ProspectOwnerUserId = null,
    DateOnly? IdentifiedOn = null,
    string? Source = null,
    string? StrategyNotes = null);

public sealed record OpenResearchCaseRequest(
    string Question,
    string Sensitivity,
    Guid? OwnerUserId = null,
    string? Context = null,
    IReadOnlyList<IntelligenceSubjectRequest>? Subjects = null);

public sealed record UpdateResearchCaseRequest(
    string Question,
    string Sensitivity,
    int ExpectedVersion,
    string? Context = null);

/// <param name="Status">Open, Paused, Completed or Cancelled.</param>
public sealed record ChangeResearchCaseStatusRequest(
    string Status,
    int ExpectedVersion,
    string? Conclusion = null);

/// <param name="Kind">Source, Signal, Thesis, Prediction or Task.</param>
public sealed record LinkResearchItemRequest(
    string Kind,
    Guid LinkedId,
    int ExpectedVersion,
    string? Note = null);


// ------------------------------------------------------------------ responses

/// <summary>The identifier of something just created or just attached.</summary>
public sealed record IntelligenceIdResponse(Guid Id);

public sealed record IntelligenceSubjectResponse(
    Guid Id,
    string Kind,
    Guid SubjectId,
    string Label,
    string? Note);

/// <param name="IsHeldByAgencyOS">
/// Whether AgencyOS holds the evidence or only a pointer to it. A URL is a
/// reference: the page is not archived, not hashed and not preserved, and this
/// field is what stops a reader assuming otherwise.
/// </param>
public sealed record IntelligenceSourceResponse(
    Guid Id,
    string Kind,
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
    string Reliability,
    string? ReliabilityRationale,
    string? ReliabilityAssessedByDisplayName,
    DateTimeOffset? ReliabilityAssessedAt,
    string Sensitivity,
    string? Notes,
    bool IsHeldByAgencyOS,
    string? RecordedByDisplayName,
    int SignalCount,
    int Version);

/// <param name="ExcerptWithheld">
/// True when the citation is legitimate but the M10 artifact it quotes is
/// classified above the reader. Said out loud rather than left blank, so a withheld
/// excerpt does not read as an analyst who never took one.
/// </param>
public sealed record SignalEvidenceResponse(
    Guid Id,
    Guid SourceId,
    string SourceTitle,
    string SourceKind,
    string SourceReliability,
    string Role,
    string? Excerpt,
    bool ExcerptWithheld,
    string? Locator,
    DateTimeOffset AddedAt,
    string? AddedByDisplayName);

public sealed record SignalResponse(
    Guid Id,
    string Title,
    string Claim,
    string Kind,
    string Verification,
    string Confidence,
    string Sensitivity,
    DateTimeOffset? OccurredAt,
    DateTimeOffset ObservedAt,
    DateTimeOffset RecordedAt,
    string? RecordedByDisplayName,
    IReadOnlyList<IntelligenceSubjectResponse> Subjects,
    int EvidenceCount,
    int Version);

public sealed record SignalDetailResponse(
    SignalResponse Signal,
    string? Notes,
    string? VerificationNote,
    string? VerificationChangedByDisplayName,
    DateTimeOffset? VerificationChangedAt,
    IReadOnlyList<SignalEvidenceResponse> Evidence,
    IReadOnlyList<IntelligenceEventResponse> History);

public sealed record ThesisRevisionResponse(
    Guid Id,
    int Sequence,
    string Proposition,
    string? Rationale,
    string Confidence,
    string? ChangeNote,
    DateTimeOffset RecordedAt,
    string? RecordedByDisplayName);

public sealed record ThesisEvidenceResponse(
    Guid Id,
    Guid SignalId,
    string SignalTitle,
    string SignalVerification,
    string Stance,
    string? Note,
    DateTimeOffset AddedAt,
    string? AddedByDisplayName);

/// <param name="SupportingCount">
/// Reported beside <c>ChallengingCount</c> and never netted against it. Five weak
/// supporting signals do not mechanically outweigh one strong contradiction, and a
/// single "evidence score" would assert that they do.
/// </param>
public sealed record ThesisResponse(
    Guid Id,
    string Title,
    string Proposition,
    string Status,
    string Confidence,
    string Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<IntelligenceSubjectResponse> Subjects,
    int RevisionCount,
    int SupportingCount,
    int ChallengingCount,
    int Version);

public sealed record ThesisDetailResponse(
    ThesisResponse Thesis,
    string? Rationale,
    string? ClosedReason,
    DateTimeOffset? ClosedAt,
    Guid? SupersededByThesisId,
    IReadOnlyList<ThesisRevisionResponse> Revisions,
    IReadOnlyList<ThesisEvidenceResponse> Evidence,
    IReadOnlyList<IntelligenceEventResponse> History);

public sealed record PredictionRevisionResponse(
    Guid Id,
    int Sequence,
    decimal Probability,
    string? Rationale,
    DateTimeOffset RecordedAt,
    string? RecordedByDisplayName);

public sealed record PredictionEvidenceResponse(
    Guid Id,
    Guid? SourceId,
    Guid? SignalId,
    string Label,
    string? Note,
    DateTimeOffset AddedAt,
    string? AddedByDisplayName);

/// <param name="CurrentProbability">
/// The latest stated forecast, derived from the revisions and never stored. A
/// stored copy would drift from the history calibration is computed against.
/// </param>
/// <param name="BrierScore">
/// Null until the question resolved Yes or No. Cancelled and unresolvable
/// predictions are counted and never scored.
/// </param>
public sealed record PredictionResponse(
    Guid Id,
    string Statement,
    DateTimeOffset ResolvesBy,
    string Status,
    decimal CurrentProbability,
    DateTimeOffset? CurrentProbabilityAsOf,
    string? CurrentProbabilityByDisplayName,
    string? Outcome,
    DateTimeOffset? ResolvedAt,
    decimal? BrierScore,
    string Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<IntelligenceSubjectResponse> Subjects,
    int RevisionCount,
    int Version);

public sealed record PredictionDetailResponse(
    PredictionResponse Prediction,
    string? ResolutionCriteria,
    string? ResolutionNote,
    string? ResolvedByDisplayName,
    string? CancelledReason,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<PredictionRevisionResponse> Revisions,
    IReadOnlyList<PredictionEvidenceResponse> Evidence,
    IReadOnlyList<IntelligenceEventResponse> History);

/// <summary>
/// Deterministic calibration over resolved binary predictions.
/// </summary>
/// <remarks>
/// Arithmetic and a sample count, with no qualitative label anywhere. There is no
/// "well calibrated" and no "strong forecaster": a mean Brier score over eleven
/// predictions supports very little, and the count travels beside the number so a
/// reader can see that for themselves (§14).
/// </remarks>
public sealed record PredictionCalibrationResponse(
    int ResolvedCount,
    int YesCount,
    int NoCount,
    int UnresolvableCount,
    int OpenCount,
    decimal? MeanBrierScore,
    decimal? MeanProbability,
    decimal? ObservedFrequency);

public sealed record WatchlistResponse(
    Guid Id,
    string Name,
    string? Purpose,
    string Status,
    string Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset? LastReviewedAt,
    string? LastReviewedByDisplayName,
    DateTimeOffset CreatedAt,
    int EntryCount,
    int Version);

public sealed record WatchlistDetailResponse(
    WatchlistResponse Watchlist,
    IReadOnlyList<IntelligenceSubjectResponse> Entries,
    IReadOnlyList<IntelligenceEventResponse> History);

/// <summary>What has happened around the things a watchlist watches.</summary>
/// <remarks>
/// Derived by subject overlap at read time, and narrowed to what the reader may
/// see. Opening a watchlist does not entitle somebody to signals about its members
/// that they could not otherwise read.
/// </remarks>
public sealed record WatchlistActivityResponse(
    WatchlistResponse Watchlist,
    IReadOnlyList<SignalResponse> RecentSignals,
    int SignalsSinceLastReview,
    IReadOnlyList<ThesisResponse> ActiveTheses,
    IReadOnlyList<PredictionResponse> OpenPredictions,
    DateTimeOffset? NewestSignalAt);

public sealed record TalentRadarResponse(
    Guid Id,
    Guid PersonId,
    string PersonDisplayName,
    string? PersonTitle,
    string? CompanyName,
    string Status,
    string Priority,
    string Rationale,
    string? IntendedDisciplines,
    string Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset? LastReviewedAt,
    Guid? ProspectId,
    int SignalCount,
    int Version);

public sealed record RadarCreditResponse(
    Guid Id,
    string Title,
    string? Role,
    string? Kind,
    int? Year);

public sealed record TalentRadarDetailResponse(
    TalentRadarResponse Entry,
    string? DismissedReason,
    DateTimeOffset? ConvertedAt,
    string? ConvertedByDisplayName,
    IReadOnlyList<RadarCreditResponse> Credits,
    IReadOnlyList<string> Disciplines,
    IReadOnlyList<SignalResponse> Signals,
    IReadOnlyList<ThesisResponse> Theses,
    IReadOnlyList<PredictionResponse> Predictions,
    IReadOnlyList<WatchlistResponse> Watchlists,
    IReadOnlyList<IntelligenceEventResponse> History);

/// <summary>What the radar handed to M4.</summary>
public sealed record RadarConversionResponse(
    Guid ProspectId,
    Guid TalentProfileId,
    bool CreatedTalentProfile);

public sealed record ResearchCaseResponse(
    Guid Id,
    string Question,
    string Status,
    string Sensitivity,
    string? OwnerDisplayName,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<IntelligenceSubjectResponse> Subjects,
    int SourceCount,
    int SignalCount,
    int ThesisCount,
    int PredictionCount,
    int TaskCount,
    int OpenTaskCount,
    int Version);

public sealed record ResearchTaskResponse(
    Guid Id,
    string Title,
    string State,
    DateTimeOffset? DueAt,
    bool IsOverdue,
    string? AssignedToDisplayName);

public sealed record ResearchCaseDetailResponse(
    ResearchCaseResponse ResearchCase,
    string? Context,
    string? Conclusion,
    IReadOnlyList<IntelligenceSourceResponse> Sources,
    IReadOnlyList<SignalResponse> Signals,
    IReadOnlyList<ThesisResponse> Theses,
    IReadOnlyList<PredictionResponse> Predictions,
    IReadOnlyList<ResearchTaskResponse> Tasks,
    IReadOnlyList<IntelligenceEventResponse> History);

/// <summary>
/// What is factually known about a working relationship.
/// </summary>
/// <remarks>
/// <para>
/// Dimensions, never a score. There is no RelationshipHealth, no Affinity and no
/// InfluenceScore anywhere in this contract: a composite number would be arithmetic
/// over incommensurable things and its apparent precision would be believed (§18).
/// </para>
/// <para>
/// <strong>Recorded assessment and counted activity are kept apart.</strong>
/// <c>RecordedStrength</c> is what a person wrote down. Everything after it is
/// counted from M2 rows. Fourteen emails is not a strong relationship, and this
/// contract refuses to say it is.
/// </para>
/// </remarks>
public sealed record RelationshipIntelligenceResponse(
    Guid SubjectId,
    string Kind,
    string DisplayName,
    string? Title,
    string? CompanyName,
    string? RecordedStrength,
    string? RecordedRelationshipNote,
    string? RelationshipOwnerDisplayName,
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
    IReadOnlyList<SignalResponse> RecentSignals,
    IReadOnlyList<WatchlistResponse> Watchlists,
    IReadOnlyList<ThesisResponse> ActiveTheses,
    IReadOnlyList<PredictionResponse> OpenPredictions);

public sealed record IntelligenceEventResponse(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <summary>
/// What the intelligence desk has to look at.
/// </summary>
/// <remarks>
/// Counts and real rows. Nothing here is ranked, scored or prioritized by the
/// system: there is no AI priority, no opportunity probability and no hot-talent
/// score. M11 records what is known and leaves the judgment where it belongs (§18).
/// </remarks>
public sealed record IntelligenceCommandCenterResponse(
    IReadOnlyList<PredictionResponse> PredictionsAwaitingResolution,
    IReadOnlyList<PredictionResponse> PredictionsDueSoon,
    IReadOnlyList<SignalResponse> DisputedSignals,
    IReadOnlyList<TalentRadarResponse> RadarAwaitingReview,
    IReadOnlyList<WatchlistResponse> WatchlistsWithNewActivity,
    IReadOnlyList<ResearchCaseResponse> ResearchCasesWithOverdueTasks,
    int AwaitingResolutionCount,
    int DueSoonCount,
    int DisputedSignalCount,
    int RadarAwaitingReviewCount);
