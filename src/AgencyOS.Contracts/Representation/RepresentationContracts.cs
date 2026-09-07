using AgencyOS.Contracts.PeopleSlice;

namespace AgencyOS.Contracts.Representation;

// ------------------------------------------------------------------ requests

/// <param name="PersonId">Person the profile describes.</param>
/// <param name="CareerStage">Unknown, Emerging, Established or Veteran.</param>
/// <param name="Summary">Short factual summary.</param>
/// <param name="PositioningNotes">
/// Internal judgment. Readable only with <c>talent.notes.read</c>; other callers
/// receive the record with this field absent.
/// </param>
/// <param name="BaseMarket">Primary market they work out of.</param>
/// <param name="Languages">Languages they work in.</param>
/// <param name="Disciplines">Actor, Writer, Director, Producer and so on.</param>
public sealed record CreateTalentProfileRequest(
    Guid PersonId,
    string? CareerStage = null,
    string? Summary = null,
    string? PositioningNotes = null,
    string? BaseMarket = null,
    string? Languages = null,
    IReadOnlyList<string>? Disciplines = null);

/// <param name="ExpectedVersion">The version the caller observed. Required; a mismatch is refused with 409.</param>
public sealed record UpdateTalentProfileRequest(
    int ExpectedVersion,
    string? CareerStage = null,
    string? Summary = null,
    string? PositioningNotes = null,
    string? BaseMarket = null,
    string? Languages = null);

/// <param name="Discipline">The discipline to add or end.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record ChangeTalentDisciplineRequest(string Discipline, int ExpectedVersion);

/// <param name="PersonId">Person to pursue.</param>
/// <param name="OwnerUserId">Internal user who owns the pursuit.</param>
/// <param name="IdentifiedOn">When they were identified. Defaults to today.</param>
/// <param name="Source">How the agency came across them.</param>
/// <param name="StrategyNotes">Internal strategy. Requires <c>talent.notes.read</c> to read back.</param>
/// <param name="NextFollowUpOn">When somebody should next act.</param>
public sealed record CreateProspectRequest(
    Guid PersonId,
    Guid OwnerUserId,
    DateOnly? IdentifiedOn = null,
    string? Source = null,
    string? StrategyNotes = null,
    DateOnly? NextFollowUpOn = null);

/// <param name="Stage">Contacted, Courting, Declined or Lost. Converting is a separate command.</param>
/// <param name="OccurredOn">When it happened, which is not always today.</param>
/// <param name="Reason">Why, for the history.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record AdvanceProspectRequest(
    string Stage,
    DateOnly OccurredOn,
    int ExpectedVersion,
    string? Reason = null);

/// <summary>
/// Turns a pursuit into a representation.
/// </summary>
/// <remarks>
/// Creates the representation and closes the prospect in one transaction. Safe to
/// retry with an idempotency key: the server replays the original answer rather
/// than signing the client twice.
/// </remarks>
/// <param name="StartsOn">When representation takes effect.</param>
/// <param name="LeadUserId">Who will lead the relationship.</param>
/// <param name="Scopes">Areas to represent, for example Television or Literary.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record ConvertProspectRequest(
    DateOnly StartsOn,
    Guid LeadUserId,
    IReadOnlyList<string> Scopes,
    int ExpectedVersion,
    bool? IsExclusive = null,
    string? Territory = null,
    string? Notes = null);

/// <param name="PersonId">Person to represent.</param>
/// <param name="StartsOn">When representation takes effect.</param>
/// <param name="LeadUserId">Who leads the relationship.</param>
/// <param name="Scopes">Areas represented.</param>
public sealed record CreateRepresentationRequest(
    Guid PersonId,
    DateOnly StartsOn,
    Guid LeadUserId,
    IReadOnlyList<string> Scopes,
    DateOnly? EndsOn = null,
    bool? IsExclusive = null,
    string? Territory = null,
    string? Notes = null);

/// <param name="Status">Active, Suspended, Terminated or Expired.</param>
/// <param name="OccurredOn">When the change took effect.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
/// <param name="Reason">Why, for the history.</param>
public sealed record TransitionRepresentationRequest(
    string Status,
    DateOnly OccurredOn,
    int ExpectedVersion,
    string? Reason = null);

/// <param name="Area">Scope area to add or end.</param>
/// <param name="OccurredOn">When the change took effect.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record ChangeRepresentationScopeRequest(string Area, DateOnly OccurredOn, int ExpectedVersion);

/// <param name="UserId">Internal user to assign. Must be a member of this tenant.</param>
/// <param name="Role">Lead, Agent, Coordinator or Assistant.</param>
/// <param name="OccurredOn">When the assignment takes effect.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record AssignRepresentationTeamMemberRequest(
    Guid UserId,
    string Role,
    DateOnly OccurredOn,
    int ExpectedVersion);

/// <param name="UserId">Internal user to remove.</param>
/// <param name="OccurredOn">When they left the team.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record RemoveRepresentationTeamMemberRequest(Guid UserId, DateOnly OccurredOn, int ExpectedVersion);

/// <param name="PersonId">Person credited.</param>
/// <param name="Title">Work title, as reported.</param>
/// <param name="Type">Performance, Writing, Directing, Producing and so on.</param>
/// <param name="Role">Character name or job title.</param>
/// <param name="Status">Announced, InProduction, Completed or Released.</param>
/// <param name="Year">Year the work is dated to.</param>
/// <param name="CompanyId">Studio or network, when the agency has a record of it.</param>
/// <param name="Source">Where this information came from.</param>
public sealed record AddCreditRequest(
    Guid PersonId,
    string Title,
    string Type,
    string? Role = null,
    string? Status = null,
    int? Year = null,
    Guid? CompanyId = null,
    string? Source = null,
    string? Notes = null);

/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record UpdateCreditRequest(
    string Title,
    string Type,
    int ExpectedVersion,
    string? Role = null,
    string? Status = null,
    int? Year = null,
    Guid? CompanyId = null,
    string? Source = null,
    string? Notes = null);

/// <param name="PersonId">Person the material belongs to.</param>
/// <param name="Title">What it is called.</param>
/// <param name="Type">Screenplay, Pilot, Reel, Headshot and so on.</param>
/// <param name="ExternalUri">
/// Where the document lives. Must be an absolute http or https address: AgencyOS
/// stores no file content in this milestone, and a device-local path would resolve
/// on exactly one machine.
/// </param>
public sealed record AddMaterialRequest(
    Guid PersonId,
    string Title,
    string Type,
    string? Status = null,
    string? VersionLabel = null,
    string? ExternalUri = null,
    DateOnly? ReceivedOn = null,
    string? Source = null,
    string? Notes = null);

/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record UpdateMaterialRequest(
    string Title,
    string Type,
    int ExpectedVersion,
    string? Status = null,
    string? VersionLabel = null,
    string? ExternalUri = null,
    DateOnly? ReceivedOn = null,
    string? Source = null,
    string? Notes = null);

// ----------------------------------------------------------------- responses

/// <param name="Id">Talent profile identifier.</param>
/// <param name="PersonId">The person described.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="CareerStage">Roughly where they are in their career.</param>
/// <param name="Disciplines">Disciplines that currently apply.</param>
/// <param name="RepresentationStatus">
/// Current representation status, or null when the agency has never represented
/// them.
/// </param>
/// <param name="IsClient">
/// Whether an active representation makes them a client right now. Derived, never
/// stored: a flag would be a second source of truth that drifts.
/// </param>
/// <param name="LeadUserId">Internal owner of the relationship.</param>
/// <param name="LeadDisplayName">That person's name.</param>
/// <param name="Scopes">Areas currently represented.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record TalentSummaryResponse(
    Guid Id,
    Guid PersonId,
    string DisplayName,
    string CareerStage,
    IReadOnlyList<string> Disciplines,
    string? RepresentationStatus,
    bool IsClient,
    Guid? LeadUserId,
    string? LeadDisplayName,
    IReadOnlyList<string> Scopes,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="PositioningNotes">
/// Internal judgment. Absent unless the caller holds <c>talent.notes.read</c>, and
/// deliberately indistinguishable from the field being empty.
/// </param>
public sealed record TalentDetailResponse(
    TalentSummaryResponse Talent,
    string? Summary,
    string? PositioningNotes,
    string? BaseMarket,
    string? Languages,
    DateTimeOffset CreatedAt);

/// <param name="Area">Area represented.</param>
/// <param name="StartsOn">When it began.</param>
/// <param name="EndsOn">When it ended, or null while current.</param>
public sealed record RepresentationScopeResponse(string Area, DateOnly StartsOn, DateOnly? EndsOn);

/// <param name="UserId">Internal user assigned.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="Role">What they do on this team.</param>
/// <param name="StartsOn">When they joined.</param>
/// <param name="EndsOn">When they left, or null while current.</param>
public sealed record RepresentationTeamMemberResponse(
    Guid UserId,
    string DisplayName,
    string Role,
    DateOnly StartsOn,
    DateOnly? EndsOn);

/// <param name="Scopes">Every scope, current and historical.</param>
/// <param name="Team">Every assignment, current and historical.</param>
public sealed record RepresentationResponse(
    Guid Id,
    Guid PersonId,
    string DisplayName,
    string Status,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    bool? IsExclusive,
    string? Territory,
    string? Notes,
    IReadOnlyList<RepresentationScopeResponse> Scopes,
    IReadOnlyList<RepresentationTeamMemberResponse> Team,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="StrategyNotes">Internal strategy. Absent without <c>talent.notes.read</c>.</param>
public sealed record ProspectResponse(
    Guid Id,
    Guid PersonId,
    string DisplayName,
    string Stage,
    Guid OwnerUserId,
    string? OwnerDisplayName,
    string? Source,
    string? StrategyNotes,
    DateOnly IdentifiedOn,
    DateOnly? NextFollowUpOn,
    Guid? ConvertedToRepresentationId,
    DateTimeOffset UpdatedAt,
    int Version);

public sealed record CreditResponse(
    Guid Id,
    Guid PersonId,
    string Title,
    string? Role,
    string Type,
    string Status,
    int? Year,
    Guid? CompanyId,
    string? CompanyName,
    string? Source,
    string? Notes,
    int Version);

/// <param name="ExternalUri">Where the document lives. Never a device-local path.</param>
public sealed record MaterialResponse(
    Guid Id,
    Guid PersonId,
    string Title,
    string Type,
    string Status,
    string? VersionLabel,
    string? ExternalUri,
    DateOnly? ReceivedOn,
    string? Source,
    string? Notes,
    int Version);

/// <param name="OccurredOn">When it happened.</param>
/// <param name="Kind">What changed, for example <c>representation.status</c>.</param>
/// <param name="Title">One-line description.</param>
/// <param name="Detail">Supporting detail.</param>
public sealed record RepresentationHistoryEntryResponse(
    DateOnly OccurredOn,
    string Kind,
    string Title,
    string? Detail);

/// <summary>
/// Everything somebody needs before a conversation with a client.
/// </summary>
/// <remarks>
/// Operational, not analytical: no scoring, no prediction, no financial data.
/// Those belong to later milestones, and a number nobody can explain is worse than
/// no number.
/// </remarks>
public sealed record ClientOverviewResponse(
    TalentDetailResponse Talent,
    RepresentationResponse? Representation,
    IReadOnlyList<TaskResponse> OpenTasks,
    IReadOnlyList<InteractionResponse> RecentInteractions,
    IReadOnlyList<CreditResponse> Credits,
    IReadOnlyList<MaterialResponse> Materials,
    IReadOnlyList<RepresentationHistoryEntryResponse> RecentHistory);
