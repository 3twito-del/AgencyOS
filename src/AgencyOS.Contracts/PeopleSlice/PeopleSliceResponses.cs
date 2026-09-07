namespace AgencyOS.Contracts.PeopleSlice;

/// <summary>A party as referenced from a relationship, participant or task.</summary>
/// <param name="Kind">Person or Company.</param>
/// <param name="Id">Identifier of the party.</param>
/// <param name="Name">Display name at the time of reading.</param>
public sealed record PartyReferenceResponse(string Kind, Guid Id, string Name);

/// <param name="Id">Person identifier.</param>
/// <param name="DisplayName">Name as shown.</param>
/// <param name="Title">Free-text professional role.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Telephone number.</param>
/// <param name="Status">Active or Archived.</param>
/// <param name="PrimaryCompanyId">Principal company, when known.</param>
/// <param name="PrimaryCompanyName">Principal company name, when known.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token. Send it back as ExpectedVersion to change this record.</param>
public sealed record PersonSummaryResponse(
    Guid Id,
    string DisplayName,
    string? Title,
    string? Email,
    string? Phone,
    string Status,
    Guid? PrimaryCompanyId,
    string? PrimaryCompanyName,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Person">Headline fields.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="MiddleName">Middle name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="PreferredName">What they go by.</param>
/// <param name="Notes">Unstructured judgment.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
/// <param name="Relationships">Relationships this person is an endpoint of.</param>
public sealed record PersonDetailResponse(
    PersonSummaryResponse Person,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string? PreferredName,
    string? Notes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RelationshipResponse> Relationships);

/// <param name="Id">Company identifier.</param>
/// <param name="Name">Trading name.</param>
/// <param name="LegalName">Registered legal name.</param>
/// <param name="Type">Kind of external body.</param>
/// <param name="Status">Active or Archived.</param>
/// <param name="Website">Public website.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token. Send it back as ExpectedVersion to change this record.</param>
public sealed record CompanySummaryResponse(
    Guid Id,
    string Name,
    string? LegalName,
    string Type,
    string Status,
    string? Website,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Company">Headline fields.</param>
/// <param name="Notes">Unstructured judgment.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
/// <param name="Relationships">Relationships this company is an endpoint of.</param>
/// <param name="People">People whose principal company this is.</param>
public sealed record CompanyDetailResponse(
    CompanySummaryResponse Company,
    string? Notes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RelationshipResponse> Relationships,
    IReadOnlyList<PersonSummaryResponse> People);

/// <param name="Id">Relationship identifier.</param>
/// <param name="From">Party the relationship runs from.</param>
/// <param name="To">Party the relationship runs to.</param>
/// <param name="Type">Kind of relationship.</param>
/// <param name="Direction">Directed or Mutual.</param>
/// <param name="Status">Active or Ended.</param>
/// <param name="Strength">Subjective strength, 1 to 5.</param>
/// <param name="StartedAt">When it began.</param>
/// <param name="EndedAt">When it ended.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="Version">Optimistic concurrency token. Send it back as ExpectedVersion to change this record.</param>
public sealed record RelationshipResponse(
    Guid Id,
    PartyReferenceResponse From,
    PartyReferenceResponse To,
    string Type,
    string Direction,
    string Status,
    int? Strength,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Notes,
    int Version);

/// <param name="Id">Task identifier.</param>
/// <param name="Title">What needs doing.</param>
/// <param name="State">Open or Completed.</param>
/// <param name="Priority">Low, Normal, High or Urgent.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="Subject">Party the task concerns.</param>
/// <param name="SourceInteractionId">Interaction it came out of, when it did.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
/// <param name="CompletedAt">Completion instant, UTC. Always set when completed.</param>
/// <param name="Version">Optimistic concurrency token. Send it back as ExpectedVersion to change this record.</param>
public sealed record TaskResponse(
    Guid Id,
    string Title,
    string State,
    string Priority,
    DateTimeOffset? DueAt,
    PartyReferenceResponse? Subject,
    Guid? SourceInteractionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int Version);

/// <param name="Id">Interaction identifier.</param>
/// <param name="Type">Kind of contact.</param>
/// <param name="OccurredAt">When it happened, UTC.</param>
/// <param name="Summary">One-line factual summary.</param>
/// <param name="DetailedNotes">Longer account.</param>
/// <param name="Participants">Everyone involved.</param>
public sealed record InteractionResponse(
    Guid Id,
    string Type,
    DateTimeOffset OccurredAt,
    string Summary,
    string? DetailedNotes,
    IReadOnlyList<PartyReferenceResponse> Participants);

/// <param name="InteractionId">The interaction that was recorded.</param>
/// <param name="FollowUpTaskId">The follow-up task, when one was requested.</param>
public sealed record RecordInteractionResponse(Guid InteractionId, Guid? FollowUpTaskId);

/// <summary>
/// One event on a party's unified timeline.
/// </summary>
/// <remarks>
/// A curated, human-facing projection composed from interactions, relationships,
/// tasks and selected record changes. It is deliberately not the audit trail and
/// carries none of its security metadata - no actor, client or permission. See
/// <c>docs/adr/ADR-0012-timeline-projection.md</c>.
/// </remarks>
/// <param name="OccurredAt">When the event happened, UTC.</param>
/// <param name="Kind">Event kind, for example <c>interaction</c> or <c>task.completed</c>.</param>
/// <param name="Title">One-line description.</param>
/// <param name="Detail">Supporting detail, when useful.</param>
/// <param name="EntityId">The record the entry points at, for navigation.</param>
public sealed record TimelineEntryResponse(
    DateTimeOffset OccurredAt,
    string Kind,
    string Title,
    string? Detail,
    Guid? EntityId);

/// <summary>
/// The operational picture: what needs attention now.
/// </summary>
/// <remarks>
/// Deliberately operational rather than analytical in M2. The buckets are disjoint,
/// so a task appears exactly once and the counts reconcile. No scoring, no
/// prioritization beyond due date and priority - a number nobody can explain is
/// worse than no number.
/// </remarks>
/// <param name="Overdue">Open tasks past their due date, most overdue first.</param>
/// <param name="DueSoon">Open tasks due within the next seven days.</param>
/// <param name="Unscheduled">Open tasks with no due date.</param>
/// <param name="RecentInteractions">Most recently recorded contact.</param>
/// <param name="OpenTaskCount">Total open tasks in the tenant.</param>
/// <param name="PeopleCount">Total active people in the tenant.</param>
/// <param name="CompanyCount">Total active companies in the tenant.</param>
public sealed record CommandCenterResponse(
    IReadOnlyList<TaskResponse> Overdue,
    IReadOnlyList<TaskResponse> DueSoon,
    IReadOnlyList<TaskResponse> Unscheduled,
    IReadOnlyList<InteractionResponse> RecentInteractions,
    int OpenTaskCount,
    int PeopleCount,
    int CompanyCount);
