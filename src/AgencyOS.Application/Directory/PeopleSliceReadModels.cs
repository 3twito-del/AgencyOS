using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Application.Directory;

/// <summary>A party as it appears when referenced from somewhere else.</summary>
/// <param name="Kind">Person or Company.</param>
/// <param name="Id">Identifier of the party.</param>
/// <param name="Name">Display name at the time of reading.</param>
public sealed record PartyReference(string Kind, Guid Id, string Name);

/// <param name="Id">Person identifier.</param>
/// <param name="DisplayName">Name as shown.</param>
/// <param name="Title">Free-text professional role.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Telephone number.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="PrimaryCompanyId">Principal company, when known.</param>
/// <param name="PrimaryCompanyName">Principal company name, when known.</param>
/// <param name="UpdatedAt">Last change instant.</param>
public sealed record PersonSummaryModel(
    Guid Id,
    string DisplayName,
    string? Title,
    string? Email,
    string? Phone,
    string Status,
    Guid? PrimaryCompanyId,
    string? PrimaryCompanyName,
    DateTimeOffset UpdatedAt);

/// <param name="Summary">Headline fields.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="MiddleName">Middle name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="PreferredName">What they go by.</param>
/// <param name="Notes">Unstructured judgment.</param>
/// <param name="CreatedAt">Creation instant.</param>
/// <param name="Relationships">Relationships this person is an endpoint of.</param>
public sealed record PersonDetailModel(
    PersonSummaryModel Summary,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string? PreferredName,
    string? Notes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RelationshipModel> Relationships);

/// <param name="Id">Company identifier.</param>
/// <param name="Name">Trading name.</param>
/// <param name="LegalName">Registered legal name.</param>
/// <param name="Type">Kind of external body.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="Website">Public website.</param>
/// <param name="UpdatedAt">Last change instant.</param>
public sealed record CompanySummaryModel(
    Guid Id,
    string Name,
    string? LegalName,
    string Type,
    string Status,
    string? Website,
    DateTimeOffset UpdatedAt);

/// <param name="Summary">Headline fields.</param>
/// <param name="Notes">Unstructured judgment.</param>
/// <param name="CreatedAt">Creation instant.</param>
/// <param name="Relationships">Relationships this company is an endpoint of.</param>
/// <param name="People">People whose principal company this is.</param>
public sealed record CompanyDetailModel(
    CompanySummaryModel Summary,
    string? Notes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RelationshipModel> Relationships,
    IReadOnlyList<PersonSummaryModel> People);

/// <param name="Id">Relationship identifier.</param>
/// <param name="From">Party the relationship runs from.</param>
/// <param name="To">Party the relationship runs to.</param>
/// <param name="Type">Kind of relationship.</param>
/// <param name="Direction">How it reads.</param>
/// <param name="Status">Active or ended.</param>
/// <param name="Strength">Subjective strength, 1 to 5.</param>
/// <param name="StartedAt">When it began.</param>
/// <param name="EndedAt">When it ended.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record RelationshipModel(
    Guid Id,
    PartyReference From,
    PartyReference To,
    string Type,
    string Direction,
    string Status,
    int? Strength,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Notes);

/// <param name="Id">Task identifier.</param>
/// <param name="Title">What needs doing.</param>
/// <param name="State">Open or completed.</param>
/// <param name="Priority">How urgently it wants attention.</param>
/// <param name="DueAt">When it is due.</param>
/// <param name="Subject">Party the task concerns.</param>
/// <param name="SourceInteractionId">Interaction it came out of.</param>
/// <param name="CreatedAt">Creation instant.</param>
/// <param name="CompletedAt">Completion instant.</param>
public sealed record TaskModel(
    Guid Id,
    string Title,
    string State,
    string Priority,
    DateTimeOffset? DueAt,
    PartyReference? Subject,
    Guid? SourceInteractionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <param name="Id">Interaction identifier.</param>
/// <param name="Type">Kind of contact.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Summary">One-line factual summary.</param>
/// <param name="DetailedNotes">Longer account.</param>
/// <param name="Participants">Everyone involved.</param>
public sealed record InteractionModel(
    Guid Id,
    string Type,
    DateTimeOffset OccurredAt,
    string Summary,
    string? DetailedNotes,
    IReadOnlyList<PartyReference> Participants);

/// <summary>
/// One event on a party's unified timeline.
/// </summary>
/// <remarks>
/// A curated, human-facing projection. It is deliberately not the audit trail:
/// audit records exist for forensics and carry actor, client, permission and
/// correlation; a timeline entry answers "what happened with this person" and
/// carries none of that. See <c>docs/adr/ADR-0012-timeline-projection.md</c>.
/// </remarks>
/// <param name="OccurredAt">When the event happened, not when it was recorded.</param>
/// <param name="Kind">Event kind, for example <c>interaction</c> or <c>relationship.ended</c>.</param>
/// <param name="Title">One-line description.</param>
/// <param name="Detail">Supporting detail, when useful.</param>
/// <param name="EntityId">The record the entry points at, for navigation.</param>
public sealed record TimelineEntryModel(
    DateTimeOffset OccurredAt,
    string Kind,
    string Title,
    string? Detail,
    Guid? EntityId);

/// <param name="Overdue">Open tasks past their due date, most overdue first.</param>
/// <param name="DueSoon">Open tasks due within the near horizon.</param>
/// <param name="Unscheduled">Open tasks with no due date.</param>
/// <param name="RecentInteractions">Most recently recorded contact.</param>
/// <param name="OpenTaskCount">Total open tasks in the tenant.</param>
/// <param name="PeopleCount">Total active people in the tenant.</param>
/// <param name="CompanyCount">Total active companies in the tenant.</param>
public sealed record CommandCenterModel(
    IReadOnlyList<TaskModel> Overdue,
    IReadOnlyList<TaskModel> DueSoon,
    IReadOnlyList<TaskModel> Unscheduled,
    IReadOnlyList<InteractionModel> RecentInteractions,
    int OpenTaskCount,
    int PeopleCount,
    int CompanyCount);

/// <summary>
/// Read-side queries for the people slice.
/// </summary>
/// <remarks>
/// Implemented in the infrastructure layer as projections, and deliberately
/// unauthorized: authorization is applied by <c>PeopleSliceQueryService</c>, so
/// there is one place to look for it rather than one per query.
/// </remarks>
public interface IPeopleSliceQueries
{
    Task<IReadOnlyList<PersonSummaryModel>> ListPeopleAsync(
        OrganizationId organizationId,
        string? search,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken = default);

    Task<PersonDetailModel?> GetPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanySummaryModel>> ListCompaniesAsync(
        OrganizationId organizationId,
        string? search,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CompanyDetailModel?> GetCompanyAsync(
        OrganizationId organizationId,
        CompanyId companyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimelineEntryModel>?> GetPersonTimelineAsync(
        OrganizationId organizationId,
        PersonId personId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimelineEntryModel>?> GetCompanyTimelineAsync(
        OrganizationId organizationId,
        CompanyId companyId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<CommandCenterModel> GetCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskModel>> ListTasksAsync(
        OrganizationId organizationId,
        bool openOnly,
        int limit,
        CancellationToken cancellationToken = default);
}
