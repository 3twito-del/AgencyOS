namespace AgencyOS.Contracts.Opportunities;

// ------------------------------------------------------------------- requests

/// <param name="Kind">TalentProfile, Project, Package or ProjectRole.</param>
/// <param name="TargetId">The record it points at.</param>
/// <param name="Role">Primary, Context or Supporting.</param>
/// <param name="Note">Why it is part of the pursuit.</param>
public sealed record OpportunitySubjectRequest(
    string Kind,
    Guid TargetId,
    string? Role = null,
    string? Note = null);

/// <param name="Name">What the pursuit is called.</param>
/// <param name="Kind">TalentEngagement, ProjectMarket, PackageMarket, Staffing, Partnership or Other.</param>
/// <param name="OwnerUserId">Internal owner.</param>
/// <param name="OpenedOn">When the pursuit opened. Defaults to today.</param>
/// <param name="Priority">Low, Normal or High. Set by a person; nothing computes it.</param>
/// <param name="Description">Factual description of what is being pursued.</param>
/// <param name="StrategyNotes">Internal strategy. Requires <c>opportunities.strategy.read</c> to read back.</param>
/// <param name="Subjects">What the pursuit is about.</param>
public sealed record CreateOpportunityRequest(
    string Name,
    string Kind,
    Guid OwnerUserId,
    DateOnly? OpenedOn = null,
    string? Priority = null,
    string? Description = null,
    string? StrategyNotes = null,
    IReadOnlyList<OpportunitySubjectRequest>? Subjects = null);

/// <param name="ExpectedVersion">The version the caller observed. Required (ADR-0014).</param>
public sealed record UpdateOpportunityRequest(
    string Name,
    Guid OwnerUserId,
    int ExpectedVersion,
    string? Priority = null,
    string? Description = null,
    string? StrategyNotes = null);

/// <param name="Status">Draft, Active, Paused, Closed or Cancelled.</param>
/// <param name="OccurredOn">When it happened. Defaults to today.</param>
/// <param name="Outcome">
/// Placed, NoInterest, Withdrawn, Superseded or NotPursued. Required when closing,
/// refused otherwise. Never a deal outcome: whether money changed hands is M7.
/// </param>
/// <param name="Reason">Why, for the history.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record ChangeOpportunityStatusRequest(
    string Status,
    int ExpectedVersion,
    DateOnly? OccurredOn = null,
    string? Outcome = null,
    string? Reason = null);

/// <param name="ExpectedVersion">The opportunity's version. Required.</param>
public sealed record AddOpportunitySubjectRequest(
    string Kind,
    Guid TargetId,
    int ExpectedVersion,
    string? Role = null,
    string? Note = null);

/// <param name="ExpectedVersion">The opportunity's version. Required.</param>
public sealed record RemoveOpportunitySubjectRequest(int ExpectedVersion);

/// <param name="CompanyId">The company approached. Exactly one of this and PersonId.</param>
/// <param name="PersonId">The person approached. Exactly one of this and CompanyId.</param>
/// <param name="ContactPersonId">
/// The individual dealt with. Valid only for a company target: for a person target
/// the person is who you are dealing with.
/// </param>
/// <param name="OwnerUserId">Who owns this target, when it differs from the opportunity's owner.</param>
/// <param name="NextActionOn">When somebody should next act.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="ExpectedVersion">The opportunity's version. Required.</param>
public sealed record AddOpportunityTargetRequest(
    int ExpectedVersion,
    Guid? CompanyId = null,
    Guid? PersonId = null,
    Guid? ContactPersonId = null,
    Guid? OwnerUserId = null,
    DateOnly? NextActionOn = null,
    string? Notes = null);

/// <param name="ExpectedVersion">The <em>target's</em> version, not the opportunity's.</param>
public sealed record UpdateOpportunityTargetRequest(
    int ExpectedVersion,
    Guid? ContactPersonId = null,
    Guid? OwnerUserId = null,
    DateOnly? NextActionOn = null,
    string? Notes = null);

/// <param name="Stage">
/// Identified, Approved, Contacted, Engaged, Interested, Advanced, Passed,
/// Withdrawn or Exhausted. Must be legal from the current stage.
/// </param>
/// <param name="OccurredAt">When it happened, which is not always now.</param>
/// <param name="Note">Why, for the history.</param>
/// <param name="ExpectedVersion">The target's version. Required.</param>
public sealed record MoveOpportunityTargetRequest(
    string Stage,
    int ExpectedVersion,
    DateTimeOffset? OccurredAt = null,
    string? Note = null);

/// <param name="Kind">Acknowledged, MoreMaterialRequested, MeetingRequested or Noted.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="SubmissionId">The submission this responds to, when it responds to one.</param>
/// <param name="Note">What they said.</param>
/// <param name="ExpectedVersion">The target's version. Required.</param>
public sealed record RecordTargetResponseRequest(
    string Kind,
    int ExpectedVersion,
    DateTimeOffset? OccurredAt = null,
    Guid? SubmissionId = null,
    string? Note = null);

/// <param name="MaterialId">The material sent or shown.</param>
/// <param name="Note">Why it was included.</param>
public sealed record SubmissionMaterialRequest(Guid MaterialId, string? Note = null);

/// <param name="Title">What the follow-up is.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="AssignedTo">Who should do it. Defaults to the caller.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record OpportunityFollowUpRequest(
    string Title,
    DateTimeOffset? DueAt = null,
    Guid? AssignedTo = null,
    string? Notes = null);

/// <param name="SentAt">When the agent says it went. Defaults to now.</param>
/// <param name="Channel">Email, Portal, Courier, InPerson, Phone or Other.</param>
/// <param name="Materials">What was sent. A snapshot of each is recorded.</param>
/// <param name="Subject">What it was called.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="ResponseExpectedBy">
/// When a reply is expected, so silence becomes visible. Nothing is ever recorded
/// to represent a non-response; it is derived from this date.
/// </param>
/// <param name="ExternalReference">An external identifier the agent has. AgencyOS assigns it no meaning.</param>
/// <param name="FollowUp">Optional next action, created in the same transaction.</param>
/// <param name="ExpectedVersion">The target's version. Required.</param>
public sealed record RecordSubmissionRequest(
    string Channel,
    int ExpectedVersion,
    DateTimeOffset? SentAt = null,
    IReadOnlyList<SubmissionMaterialRequest>? Materials = null,
    string? Subject = null,
    string? Notes = null,
    DateOnly? ResponseExpectedBy = null,
    string? ExternalReference = null,
    OpportunityFollowUpRequest? FollowUp = null);

/// <param name="ExpectedVersion">The submission's version. Required.</param>
public sealed record AmendSubmissionRequest(
    int ExpectedVersion,
    string? Subject = null,
    string? Notes = null,
    DateOnly? ResponseExpectedBy = null,
    string? ExternalReference = null);

/// <param name="PartyKind">Person or Company.</param>
/// <param name="PartyId">Identifier of that party.</param>
/// <param name="Role">What they were doing there.</param>
public sealed record PitchParticipantRequest(string PartyKind, Guid PartyId, string? Role = null);

/// <param name="InteractionType">Call, Meeting, Email and so on. The M2 vocabulary.</param>
/// <param name="OccurredAt">When it happened. Defaults to now.</param>
/// <param name="Summary">What happened, in a line. Becomes the interaction's summary.</param>
/// <param name="Participants">Who was there.</param>
/// <param name="Kind">Introductory, Formal, FollowUp or Incidental.</param>
/// <param name="Outcome">
/// NoDecision, FollowUpRequested, MoreMaterialRequested, Interested or Passed.
/// Deliberately no offer-shaped outcomes: those are M7.
/// </param>
/// <param name="Materials">What was shown. A snapshot of each is recorded.</param>
/// <param name="Subject">What was pitched.</param>
/// <param name="Notes">Pitch-specific context.</param>
/// <param name="DetailedNotes">Longer notes, stored on the interaction.</param>
/// <param name="FollowUp">Optional next action, created in the same transaction.</param>
/// <param name="ExpectedVersion">The target's version. Required.</param>
public sealed record RecordPitchRequest(
    string InteractionType,
    string Summary,
    IReadOnlyList<PitchParticipantRequest> Participants,
    string Kind,
    string Outcome,
    int ExpectedVersion,
    DateTimeOffset? OccurredAt = null,
    IReadOnlyList<SubmissionMaterialRequest>? Materials = null,
    string? Subject = null,
    string? Notes = null,
    string? DetailedNotes = null,
    OpportunityFollowUpRequest? FollowUp = null);

// ------------------------------------------------------------------ responses

/// <param name="Id">Subject identifier.</param>
/// <param name="Kind">What sort of record it points at.</param>
/// <param name="Role">What it is doing in the pursuit.</param>
/// <param name="TargetId">The record it points at.</param>
/// <param name="DisplayName">A readable name for it.</param>
/// <param name="Detail">Secondary line.</param>
/// <param name="Note">Why it is part of the pursuit.</param>
public sealed record OpportunitySubjectResponse(
    Guid Id,
    string Kind,
    string Role,
    Guid TargetId,
    string DisplayName,
    string? Detail,
    string? Note);

/// <param name="Id">Opportunity identifier.</param>
/// <param name="Name">What the pursuit is called.</param>
/// <param name="Kind">What kind of outcome is being pursued.</param>
/// <param name="Status">Where the pursuit stands.</param>
/// <param name="Priority">How urgent, as set by a person.</param>
/// <param name="OwnerUserId">Internal owner.</param>
/// <param name="OwnerDisplayName">That person's name.</param>
/// <param name="OpenedOn">When it opened.</param>
/// <param name="ClosedOn">When it ended, or null while it runs.</param>
/// <param name="Outcome">What came of it, on closure.</param>
/// <param name="PrimarySubject">What it is fundamentally about, derived from the kind.</param>
/// <param name="TargetCount">How many targets it has.</param>
/// <param name="OpenTargetCount">How many can still move.</param>
/// <param name="SubmissionCount">How many submissions have gone out. Derived.</param>
/// <param name="AwaitingResponseCount">Submissions past their expected reply date. Derived.</param>
/// <param name="NextActionOn">The earliest outstanding target action.</param>
/// <param name="LastActivityAt">The most recent thing that happened. Derived.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record OpportunitySummaryResponse(
    Guid Id,
    string Name,
    string Kind,
    string Status,
    string Priority,
    Guid OwnerUserId,
    string? OwnerDisplayName,
    DateOnly OpenedOn,
    DateOnly? ClosedOn,
    string? Outcome,
    OpportunitySubjectResponse? PrimarySubject,
    int TargetCount,
    int OpenTargetCount,
    int SubmissionCount,
    int AwaitingResponseCount,
    DateOnly? NextActionOn,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Id">Target identifier.</param>
/// <param name="CompanyId">The company approached.</param>
/// <param name="PersonId">The person approached.</param>
/// <param name="DisplayName">Name of whichever party is targeted.</param>
/// <param name="ContactPersonId">The individual dealt with at a company target.</param>
/// <param name="ContactDisplayName">That person's name.</param>
/// <param name="Stage">How far along this target is.</param>
/// <param name="IsOpen">Whether it can still move.</param>
/// <param name="OwnerUserId">Who owns it, when that differs from the opportunity.</param>
/// <param name="OwnerDisplayName">That person's name.</param>
/// <param name="NextActionOn">When somebody should next act.</param>
/// <param name="ClosedOn">When it finished, or null while open.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="SubmissionCount">Submissions to this target. Derived.</param>
/// <param name="LastSubmittedAt">When material last went. Derived.</param>
/// <param name="PitchCount">Pitches to this target. Derived.</param>
/// <param name="LastPitchedAt">When they were last pitched. Derived.</param>
/// <param name="LastActivityAt">The most recent thing that happened. Derived.</param>
/// <param name="AwaitingResponseSince">When a reply became overdue, or null. Derived.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record OpportunityTargetResponse(
    Guid Id,
    Guid? CompanyId,
    Guid? PersonId,
    string DisplayName,
    Guid? ContactPersonId,
    string? ContactDisplayName,
    string Stage,
    bool IsOpen,
    Guid? OwnerUserId,
    string? OwnerDisplayName,
    DateOnly? NextActionOn,
    DateOnly? ClosedOn,
    string? Notes,
    int SubmissionCount,
    DateTimeOffset? LastSubmittedAt,
    int PitchCount,
    DateTimeOffset? LastPitchedAt,
    DateTimeOffset? LastActivityAt,
    DateOnly? AwaitingResponseSince,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="MaterialId">The material.</param>
/// <param name="TitleAtSubmission">Its title when it went, not its title now.</param>
/// <param name="TypeAtSubmission">Its type when it went.</param>
/// <param name="VersionLabelAtSubmission">Its version label when it went.</param>
/// <param name="CurrentTitle">Its title today, so a change since is visible.</param>
/// <param name="Note">Why it was included.</param>
public sealed record SubmissionMaterialResponse(
    Guid MaterialId,
    string TitleAtSubmission,
    string TypeAtSubmission,
    string? VersionLabelAtSubmission,
    string? CurrentTitle,
    string? Note);

/// <param name="Id">Submission identifier.</param>
/// <param name="OpportunityId">The pursuit it belongs to.</param>
/// <param name="OpportunityTargetId">The target it went to.</param>
/// <param name="TargetDisplayName">That target's name.</param>
/// <param name="SentAt">When the agent says it went.</param>
/// <param name="SentByUserId">The colleague who sent it.</param>
/// <param name="SentByDisplayName">Their name.</param>
/// <param name="Channel">How it went, as reported.</param>
/// <param name="Subject">What it was called.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="ResponseExpectedBy">When a reply was expected.</param>
/// <param name="ExternalReference">An external identifier the agent had, if any.</param>
/// <param name="Materials">What was sent, as it read at the time.</param>
/// <param name="LastResponseAt">The most recent thing the target did about it. Derived.</param>
/// <param name="IsAwaitingResponse">Whether the expected date has passed with nothing since. Derived.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record SubmissionResponse(
    Guid Id,
    Guid OpportunityId,
    Guid OpportunityTargetId,
    string TargetDisplayName,
    DateTimeOffset SentAt,
    Guid SentByUserId,
    string? SentByDisplayName,
    string Channel,
    string? Subject,
    string? Notes,
    DateOnly? ResponseExpectedBy,
    string? ExternalReference,
    IReadOnlyList<SubmissionMaterialResponse> Materials,
    DateTimeOffset? LastResponseAt,
    bool IsAwaitingResponse,
    int Version);

/// <param name="Id">Pitch identifier.</param>
/// <param name="OpportunityId">The pursuit it belongs to.</param>
/// <param name="OpportunityTargetId">The target pitched.</param>
/// <param name="TargetDisplayName">That target's name.</param>
/// <param name="InteractionId">
/// The one interaction this is the commercial reading of. A pitch never creates a
/// second record of the same meeting.
/// </param>
/// <param name="Kind">What kind of pitch it was.</param>
/// <param name="Outcome">How it left things.</param>
/// <param name="Subject">What was pitched.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Participants">Who was there, from the interaction.</param>
/// <param name="Materials">What was shown, as it read at the time.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record PitchResponse(
    Guid Id,
    Guid OpportunityId,
    Guid OpportunityTargetId,
    string TargetDisplayName,
    Guid InteractionId,
    string Kind,
    string Outcome,
    string? Subject,
    string? Notes,
    DateTimeOffset OccurredAt,
    IReadOnlyList<string> Participants,
    IReadOnlyList<SubmissionMaterialResponse> Materials,
    int Version);

/// <param name="Id">Task identifier.</param>
/// <param name="Title">What needs doing.</param>
/// <param name="State">Open or Completed.</param>
/// <param name="Priority">How urgently it wants attention.</param>
/// <param name="DueAt">When it is due.</param>
public sealed record OpportunityTaskResponse(
    Guid Id,
    string Title,
    string State,
    string Priority,
    DateTimeOffset? DueAt);

/// <param name="Opportunity">Headline fields.</param>
/// <param name="Description">Factual description of the pursuit.</param>
/// <param name="StrategyNotes">
/// Internal strategy. Absent without <c>opportunities.strategy.read</c>, and absent
/// is deliberately indistinguishable from empty.
/// </param>
/// <param name="Subjects">What the pursuit is about.</param>
/// <param name="Targets">Who it is aimed at, and where each stands.</param>
/// <param name="Submissions">What has gone out.</param>
/// <param name="Pitches">What has been pitched.</param>
/// <param name="OpenTasks">Outstanding next actions linked to this pursuit.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
public sealed record OpportunityDetailResponse(
    OpportunitySummaryResponse Opportunity,
    string? Description,
    string? StrategyNotes,
    IReadOnlyList<OpportunitySubjectResponse> Subjects,
    IReadOnlyList<OpportunityTargetResponse> Targets,
    IReadOnlyList<SubmissionResponse> Submissions,
    IReadOnlyList<PitchResponse> Pitches,
    IReadOnlyList<OpportunityTaskResponse> OpenTasks,
    DateTimeOffset CreatedAt);

/// <param name="OccurredAt">When it happened, UTC.</param>
/// <param name="Kind">What kind of entry this is.</param>
/// <param name="Summary">A readable sentence.</param>
/// <param name="Detail">Secondary context, when there is any.</param>
/// <param name="TargetDisplayName">Which target it concerned, when it concerned one.</param>
/// <param name="ActorDisplayName">Who recorded it, when the record names somebody.</param>
public sealed record OpportunityHistoryEntryResponse(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    string? TargetDisplayName,
    string? ActorDisplayName);

/// <param name="OpportunityId">The pursuit.</param>
/// <param name="OpportunityName">What it is called.</param>
/// <param name="Target">The target itself.</param>
public sealed record PipelineEntryResponse(
    Guid OpportunityId,
    string OpportunityName,
    OpportunityTargetResponse Target);

/// <param name="Stage">The stage these targets are at.</param>
/// <param name="Targets">The targets, with their opportunity named.</param>
public sealed record PipelineColumnResponse(
    string Stage,
    IReadOnlyList<PipelineEntryResponse> Targets);

/// <param name="OverdueFollowUps">Targets whose next action date has passed.</param>
/// <param name="AwaitingResponse">Submissions past their expected reply date.</param>
/// <param name="RecentlyInterested">Targets that said yes recently.</param>
/// <param name="RecentlyPassed">Targets that said no recently.</param>
/// <param name="ActiveOpportunityCount">How many pursuits are being worked.</param>
/// <param name="OpenTargetCount">How many targets can still move.</param>
public sealed record OpportunityCommandCenterResponse(
    IReadOnlyList<PipelineEntryResponse> OverdueFollowUps,
    IReadOnlyList<SubmissionResponse> AwaitingResponse,
    IReadOnlyList<PipelineEntryResponse> RecentlyInterested,
    IReadOnlyList<PipelineEntryResponse> RecentlyPassed,
    int ActiveOpportunityCount,
    int OpenTargetCount);

/// <param name="SubmissionId">The submission recorded.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was asked for.</param>
public sealed record RecordSubmissionResponse(Guid SubmissionId, Guid? FollowUpTaskId);

/// <param name="PitchId">The pitch recorded.</param>
/// <param name="InteractionId">The single interaction it is the commercial reading of.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was asked for.</param>
public sealed record RecordPitchResponse(Guid PitchId, Guid InteractionId, Guid? FollowUpTaskId);
