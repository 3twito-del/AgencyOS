using AgencyOS.Application.Directory;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Application.Representations;

/// <param name="Id">Talent profile identifier.</param>
/// <param name="PersonId">The person described.</param>
/// <param name="DisplayName">Their name, as shown.</param>
/// <param name="CareerStage">Roughly where they are in their career.</param>
/// <param name="Disciplines">Disciplines that currently apply.</param>
/// <param name="RepresentationStatus">Current representation status, or null when never represented.</param>
/// <param name="IsClient">Whether an active representation makes them a client right now.</param>
/// <param name="LeadUserId">Internal owner of the relationship, when there is one.</param>
/// <param name="LeadDisplayName">That person's name.</param>
/// <param name="Scopes">Areas currently represented.</param>
/// <param name="UpdatedAt">Last change instant.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record TalentSummaryModel(
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

/// <param name="Summary">Headline fields.</param>
/// <param name="ProfileSummary">Short factual summary.</param>
/// <param name="PositioningNotes">
/// Internal judgment. Null when the caller lacks <c>talent.notes.read</c>, which is
/// deliberately indistinguishable from the field being empty: an observer learns
/// nothing either way.
/// </param>
/// <param name="BaseMarket">Primary market.</param>
/// <param name="Languages">Languages they work in.</param>
/// <param name="CreatedAt">Creation instant.</param>
public sealed record TalentDetailModel(
    TalentSummaryModel Summary,
    string? ProfileSummary,
    string? PositioningNotes,
    string? BaseMarket,
    string? Languages,
    DateTimeOffset CreatedAt);

/// <param name="Area">Area represented.</param>
/// <param name="StartsOn">When it began.</param>
/// <param name="EndsOn">When it ended, or null while current.</param>
public sealed record RepresentationScopeModel(string Area, DateOnly StartsOn, DateOnly? EndsOn);

/// <param name="UserId">Internal user assigned.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="Role">What they do on this team.</param>
/// <param name="StartsOn">When they joined.</param>
/// <param name="EndsOn">When they left, or null while current.</param>
public sealed record RepresentationTeamMemberModel(
    Guid UserId,
    string DisplayName,
    string Role,
    DateOnly StartsOn,
    DateOnly? EndsOn);

/// <param name="Id">Representation identifier.</param>
/// <param name="PersonId">Person represented.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="Status">Current status.</param>
/// <param name="StartsOn">When it takes effect.</param>
/// <param name="EndsOn">When it ends, if it does.</param>
/// <param name="IsExclusive">Exclusivity, or null when never established.</param>
/// <param name="Territory">Territory covered, as recorded.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="Scopes">Every scope, current and historical.</param>
/// <param name="Team">Every assignment, current and historical.</param>
/// <param name="UpdatedAt">Last change instant.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record RepresentationModel(
    Guid Id,
    Guid PersonId,
    string DisplayName,
    string Status,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    bool? IsExclusive,
    string? Territory,
    string? Notes,
    IReadOnlyList<RepresentationScopeModel> Scopes,
    IReadOnlyList<RepresentationTeamMemberModel> Team,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Id">Prospect identifier.</param>
/// <param name="PersonId">Person being pursued.</param>
/// <param name="DisplayName">Their name.</param>
/// <param name="Stage">How far the pursuit has got.</param>
/// <param name="OwnerUserId">Internal user pursuing them.</param>
/// <param name="OwnerDisplayName">That person's name.</param>
/// <param name="Source">How the agency came across them.</param>
/// <param name="StrategyNotes">Internal strategy. Null without <c>talent.notes.read</c>.</param>
/// <param name="IdentifiedOn">When they were identified.</param>
/// <param name="NextFollowUpOn">When somebody should next act.</param>
/// <param name="ConvertedToRepresentationId">The representation they became, if they did.</param>
/// <param name="UpdatedAt">Last change instant.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record ProspectModel(
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

/// <param name="Id">Credit identifier.</param>
/// <param name="PersonId">Person credited.</param>
/// <param name="Title">Work title, as reported.</param>
/// <param name="Role">Character name or job title.</param>
/// <param name="Type">Kind of contribution.</param>
/// <param name="Status">Where the work has got to.</param>
/// <param name="Year">Year the work is dated to.</param>
/// <param name="CompanyId">Studio or network, when recorded.</param>
/// <param name="CompanyName">That company's name.</param>
/// <param name="Source">Where the information came from.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record CreditModel(
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

/// <param name="Id">Material identifier.</param>
/// <param name="PersonId">Person it belongs to.</param>
/// <param name="Title">What it is called.</param>
/// <param name="Type">Kind of artifact.</param>
/// <param name="Status">Whether it is ready to send.</param>
/// <param name="VersionLabel">Which draft or cut.</param>
/// <param name="ExternalUri">Where it lives, when addressable. Never a device path.</param>
/// <param name="ReceivedOn">When the agency received it.</param>
/// <param name="Source">Who supplied it.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record MaterialModel(
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
/// <param name="Kind">What kind of change, for example <c>representation.status</c>.</param>
/// <param name="Title">One-line description.</param>
/// <param name="Detail">Supporting detail.</param>
public sealed record RepresentationHistoryEntryModel(
    DateOnly OccurredOn,
    string Kind,
    string Title,
    string? Detail);

/// <summary>
/// The first client overview: what somebody needs to know before a conversation.
/// </summary>
/// <remarks>
/// Operational, not analytical. It answers who this is, what the agency represents
/// them for, who owns the relationship, what has happened lately and what is
/// outstanding. It deliberately carries no scoring, no prediction and no financial
/// data: those are later milestones, and a number nobody can explain is worse than
/// no number.
/// </remarks>
/// <param name="Talent">Headline talent record.</param>
/// <param name="Representation">Current representation, when there is one.</param>
/// <param name="OpenTasks">Tasks still outstanding for this person.</param>
/// <param name="RecentInteractions">Most recent contact.</param>
/// <param name="Credits">Credits on file.</param>
/// <param name="Materials">Materials on file.</param>
/// <param name="RecentHistory">What changed lately in the relationship.</param>
public sealed record ClientOverviewModel(
    TalentDetailModel Talent,
    RepresentationModel? Representation,
    IReadOnlyList<TaskModel> OpenTasks,
    IReadOnlyList<InteractionModel> RecentInteractions,
    IReadOnlyList<CreditModel> Credits,
    IReadOnlyList<MaterialModel> Materials,
    IReadOnlyList<RepresentationHistoryEntryModel> RecentHistory);

/// <summary>How a talent list is narrowed.</summary>
/// <param name="ClientsOnly">Only people with an active representation.</param>
/// <param name="FormerClientsOnly">Only people whose representation has ended.</param>
/// <param name="Discipline">Restrict to one discipline.</param>
/// <param name="ScopeArea">Restrict to one represented area.</param>
/// <param name="LeadUserId">Restrict to one internal owner.</param>
/// <param name="Search">Substring match on the person's name.</param>
public sealed record TalentFilter(
    bool ClientsOnly = false,
    bool FormerClientsOnly = false,
    ProfessionalDiscipline? Discipline = null,
    RepresentationScopeArea? ScopeArea = null,
    Guid? LeadUserId = null,
    string? Search = null);

/// <param name="OpenOnly">Only pursuits that are still live.</param>
/// <param name="Stage">Restrict to one stage.</param>
/// <param name="OwnerUserId">Restrict to one internal owner.</param>
/// <param name="DueOnOrBefore">Only those needing follow-up by this date.</param>
public sealed record ProspectFilter(
    bool OpenOnly = true,
    ProspectStage? Stage = null,
    Guid? OwnerUserId = null,
    DateOnly? DueOnOrBefore = null);

/// <summary>
/// Read-side queries for the representation model.
/// </summary>
/// <remarks>
/// Implemented in infrastructure as projections and deliberately unauthorized:
/// <c>RepresentationQueryService</c> applies the tenant-scoped checks, so there is
/// one place to look for them rather than one per query.
/// </remarks>
public interface IRepresentationQueries
{
    Task<IReadOnlyList<TalentSummaryModel>> ListTalentAsync(
        OrganizationId organizationId,
        TalentFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<TalentDetailModel?> GetTalentAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProspectModel>> ListProspectsAsync(
        OrganizationId organizationId,
        ProspectFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<ProspectModel?> GetProspectAsync(
        OrganizationId organizationId,
        ProspectId prospectId,
        CancellationToken cancellationToken = default);

    Task<RepresentationModel?> GetRepresentationAsync(
        OrganizationId organizationId,
        RepresentationId representationId,
        CancellationToken cancellationToken = default);

    /// <summary>The current representation of a person, if they have one.</summary>
    Task<RepresentationModel?> GetCurrentRepresentationAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepresentationHistoryEntryModel>> GetRepresentationHistoryAsync(
        OrganizationId organizationId,
        PersonId personId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CreditModel>> ListCreditsAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialModel>> ListMaterialsAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default);

    Task<ClientOverviewModel?> GetClientOverviewAsync(
        OrganizationId organizationId,
        PersonId personId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
