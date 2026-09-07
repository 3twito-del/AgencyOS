using AgencyOS.Application.Directory;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Opportunities;

/// <param name="Id">Opportunity identifier.</param>
/// <param name="Name">What the pursuit is called.</param>
/// <param name="Kind">What kind of outcome is being pursued.</param>
/// <param name="Status">Where the pursuit stands.</param>
/// <param name="Priority">How urgent, as set by a person.</param>
/// <param name="OwnerUserId">Internal owner.</param>
/// <param name="OwnerDisplayName">That person's name.</param>
/// <param name="OpenedOn">When the pursuit opened.</param>
/// <param name="ClosedOn">When it ended, or null while it runs.</param>
/// <param name="Outcome">What came of it, on closure.</param>
/// <param name="PrimarySubject">What it is fundamentally about, derived from the kind.</param>
/// <param name="TargetCount">How many targets it has.</param>
/// <param name="OpenTargetCount">How many can still move.</param>
/// <param name="SubmissionCount">How many submissions have gone out. Derived.</param>
/// <param name="AwaitingResponseCount">
/// Submissions past their expected response date with nothing recorded since.
/// Derived; silence is never stored as an event.
/// </param>
/// <param name="NextActionOn">The earliest outstanding target action.</param>
/// <param name="LastActivityAt">The most recent thing that happened. Derived.</param>
/// <param name="UpdatedAt">Last change instant.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record OpportunitySummaryModel(
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
    OpportunitySubjectModel? PrimarySubject,
    int TargetCount,
    int OpenTargetCount,
    int SubmissionCount,
    int AwaitingResponseCount,
    DateOnly? NextActionOn,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Id">Subject identifier.</param>
/// <param name="Kind">TalentProfile, Project, Package or ProjectRole.</param>
/// <param name="Role">Primary, Context or Supporting.</param>
/// <param name="TargetId">The record it points at.</param>
/// <param name="DisplayName">A readable name for it.</param>
/// <param name="Detail">Secondary line, such as a project's stage or a role's type.</param>
/// <param name="Note">Why it is part of the pursuit.</param>
public sealed record OpportunitySubjectModel(
    Guid Id,
    string Kind,
    string Role,
    Guid TargetId,
    string DisplayName,
    string? Detail,
    string? Note);

/// <param name="Id">Target identifier.</param>
/// <param name="CompanyId">The company approached, when the target is a company.</param>
/// <param name="PersonId">The person approached, when the target is a person.</param>
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
/// <param name="AwaitingResponseSince">
/// When a response became overdue, or null. Derived from the submission's expected
/// date and the absence of anything recorded after it.
/// </param>
/// <param name="UpdatedAt">Last change instant.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record OpportunityTargetModel(
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

/// <param name="MaterialId">The material sent.</param>
/// <param name="TitleAtSubmission">Its title when it went, not its title now.</param>
/// <param name="TypeAtSubmission">Its type when it went.</param>
/// <param name="VersionLabelAtSubmission">Its version label when it went.</param>
/// <param name="CurrentTitle">Its title today, so a change is visible.</param>
/// <param name="Note">Why it was included.</param>
public sealed record SubmissionMaterialModel(
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
/// <param name="Channel">How it went.</param>
/// <param name="Subject">What it was called.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="ResponseExpectedBy">When a reply was expected.</param>
/// <param name="ExternalReference">An external identifier the agent had, if any.</param>
/// <param name="Materials">What was sent, as it read at the time.</param>
/// <param name="LastResponseAt">The most recent thing the target did about it. Derived.</param>
/// <param name="IsAwaitingResponse">
/// Whether the expected date has passed with nothing recorded since. Derived; no
/// row is ever written to represent silence.
/// </param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record SubmissionModel(
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
    IReadOnlyList<SubmissionMaterialModel> Materials,
    DateTimeOffset? LastResponseAt,
    bool IsAwaitingResponse,
    int Version);

/// <param name="Id">Pitch identifier.</param>
/// <param name="OpportunityId">The pursuit it belongs to.</param>
/// <param name="OpportunityTargetId">The target pitched.</param>
/// <param name="TargetDisplayName">That target's name.</param>
/// <param name="InteractionId">The one interaction this is the commercial reading of.</param>
/// <param name="Kind">What kind of pitch it was.</param>
/// <param name="Outcome">How it left things.</param>
/// <param name="Subject">What was pitched.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Participants">Who was there, from the interaction.</param>
/// <param name="Materials">What was shown, as it read at the time.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record PitchModel(
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
    IReadOnlyList<SubmissionMaterialModel> Materials,
    int Version);

/// <param name="OccurredAt">When it happened.</param>
/// <param name="Kind">What kind of entry this is.</param>
/// <param name="Summary">A readable sentence.</param>
/// <param name="Detail">Secondary context, when there is any.</param>
/// <param name="TargetDisplayName">Which target it concerned, when it concerned one.</param>
/// <param name="ActorDisplayName">Who recorded it, when the record names somebody.</param>
public sealed record OpportunityHistoryEntryModel(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    string? TargetDisplayName,
    string? ActorDisplayName);

/// <param name="Summary">Headline fields.</param>
/// <param name="Description">Factual description of the pursuit.</param>
/// <param name="StrategyNotes">
/// Internal strategy. Null when the caller lacks
/// <c>opportunities.strategy.read</c>, deliberately indistinguishable from empty.
/// </param>
/// <param name="Subjects">What the pursuit is about.</param>
/// <param name="Targets">Who it is aimed at, and where each stands.</param>
/// <param name="Submissions">What has gone out.</param>
/// <param name="Pitches">What has been pitched.</param>
/// <param name="OpenTasks">Outstanding next actions linked to this pursuit.</param>
/// <param name="CreatedAt">Creation instant.</param>
public sealed record OpportunityDetailModel(
    OpportunitySummaryModel Summary,
    string? Description,
    string? StrategyNotes,
    IReadOnlyList<OpportunitySubjectModel> Subjects,
    IReadOnlyList<OpportunityTargetModel> Targets,
    IReadOnlyList<SubmissionModel> Submissions,
    IReadOnlyList<PitchModel> Pitches,
    IReadOnlyList<TaskModel> OpenTasks,
    DateTimeOffset CreatedAt);

/// <param name="Stage">The stage these targets are at.</param>
/// <param name="Targets">The targets, with their opportunity named.</param>
public sealed record PipelineColumnModel(
    string Stage,
    IReadOnlyList<PipelineEntryModel> Targets);

/// <param name="OpportunityId">The pursuit.</param>
/// <param name="OpportunityName">What it is called.</param>
/// <param name="Target">The target itself.</param>
public sealed record PipelineEntryModel(
    Guid OpportunityId,
    string OpportunityName,
    OpportunityTargetModel Target);

/// <param name="OverdueFollowUps">Targets whose next action date has passed.</param>
/// <param name="AwaitingResponse">Submissions past their expected reply date.</param>
/// <param name="RecentlyInterested">Targets that said yes recently.</param>
/// <param name="RecentlyPassed">Targets that said no recently.</param>
/// <param name="ActiveOpportunityCount">How many pursuits are being worked.</param>
/// <param name="OpenTargetCount">How many targets can still move.</param>
public sealed record OpportunityCommandCenterModel(
    IReadOnlyList<PipelineEntryModel> OverdueFollowUps,
    IReadOnlyList<SubmissionModel> AwaitingResponse,
    IReadOnlyList<PipelineEntryModel> RecentlyInterested,
    IReadOnlyList<PipelineEntryModel> RecentlyPassed,
    int ActiveOpportunityCount,
    int OpenTargetCount);

/// <param name="Status">Restrict to one status.</param>
/// <param name="Kind">Restrict to one kind of pursuit.</param>
/// <param name="OwnerUserId">Restrict to one internal owner.</param>
/// <param name="TalentProfileId">Only pursuits about this client.</param>
/// <param name="ProjectId">Only pursuits about this project.</param>
/// <param name="PackageId">Only pursuits about this package.</param>
/// <param name="TargetCompanyId">Only pursuits aimed at this company.</param>
/// <param name="TargetPersonId">Only pursuits aimed at this person.</param>
/// <param name="TargetStage">Only pursuits with a target at this stage.</param>
/// <param name="HasSubmission">Only pursuits something has gone out on.</param>
/// <param name="AwaitingResponse">Only pursuits with a reply overdue.</param>
/// <param name="FollowUpDueOnOrBefore">Only pursuits with a target action due by then.</param>
/// <param name="Search">Free text over name and description.</param>
public sealed record OpportunityFilter(
    OpportunityStatus? Status = null,
    OpportunityKind? Kind = null,
    Guid? OwnerUserId = null,
    Guid? TalentProfileId = null,
    Guid? ProjectId = null,
    Guid? PackageId = null,
    Guid? TargetCompanyId = null,
    Guid? TargetPersonId = null,
    OpportunityTargetStage? TargetStage = null,
    bool HasSubmission = false,
    bool AwaitingResponse = false,
    DateOnly? FollowUpDueOnOrBefore = null,
    string? Search = null);

/// <summary>
/// Read-side queries for the pursuit model.
/// </summary>
/// <remarks>
/// Implemented in infrastructure as projections and deliberately unauthorized:
/// <c>OpportunityQueryService</c> applies the tenant-scoped checks and the strategy
/// redaction, so both live in one place.
/// </remarks>
public interface IOpportunityQueries
{
    Task<IReadOnlyList<OpportunitySummaryModel>> ListOpportunitiesAsync(
        OrganizationId organizationId,
        OpportunityFilter filter,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);

    Task<OpportunityDetailModel?> GetOpportunityAsync(
        OrganizationId organizationId,
        OpportunityId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpportunityTargetModel>> ListTargetsAsync(
        OrganizationId organizationId,
        OpportunityId opportunityId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<OpportunityTargetModel?> GetTargetAsync(
        OrganizationId organizationId,
        OpportunityTargetId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubmissionModel>> ListSubmissionsAsync(
        OrganizationId organizationId,
        OpportunityId? opportunityId,
        OpportunityTargetId? targetId,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SubmissionModel?> GetSubmissionAsync(
        OrganizationId organizationId,
        SubmissionId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PitchModel>> ListPitchesAsync(
        OrganizationId organizationId,
        OpportunityId? opportunityId,
        OpportunityTargetId? targetId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OpportunityHistoryEntryModel>> GetHistoryAsync(
        OrganizationId organizationId,
        OpportunityId id,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineColumnModel>> GetPipelineAsync(
        OrganizationId organizationId,
        Guid? ownerUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<OpportunityCommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
