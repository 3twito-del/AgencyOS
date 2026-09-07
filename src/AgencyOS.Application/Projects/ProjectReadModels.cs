using AgencyOS.Application.Directory;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Projects;

namespace AgencyOS.Application.Projects;

/// <param name="Id">Project identifier.</param>
/// <param name="Title">Title in use.</param>
/// <param name="WorkingTitle">Internal or provisional title, when different.</param>
/// <param name="Type">What kind of work it is.</param>
/// <param name="Status">Operational status of the record.</param>
/// <param name="Stage">Where the work itself has got to.</param>
/// <param name="Year">Year the project is dated to.</param>
/// <param name="PrimaryCompanyId">The company most associated with it.</param>
/// <param name="PrimaryCompanyName">That company's name.</param>
/// <param name="LeadUserId">Internal owner, when assigned.</param>
/// <param name="LeadDisplayName">That person's name.</param>
/// <param name="OpenRoleCount">Roles still needing somebody.</param>
/// <param name="AttachedCount">Parties currently holding a role.</param>
/// <param name="PackageCount">Packages assembled around this project.</param>
/// <param name="UpdatedAt">Last change instant.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record ProjectSummaryModel(
    Guid Id,
    string Title,
    string? WorkingTitle,
    string Type,
    string Status,
    string Stage,
    int? Year,
    Guid? PrimaryCompanyId,
    string? PrimaryCompanyName,
    Guid? LeadUserId,
    string? LeadDisplayName,
    int OpenRoleCount,
    int AttachedCount,
    int PackageCount,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Id">Role identifier.</param>
/// <param name="Type">The kind of position.</param>
/// <param name="Label">Character name or description.</param>
/// <param name="Status">Whether it still needs somebody.</param>
/// <param name="IsExclusive">Whether only one party may hold it.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="Attachments">Who has held it, current and past.</param>
public sealed record ProjectRoleModel(
    Guid Id,
    string Type,
    string? Label,
    string Status,
    bool IsExclusive,
    string? Notes,
    IReadOnlyList<AttachmentModel> Attachments);

/// <param name="Id">Attachment identifier.</param>
/// <param name="ProjectRoleId">The role held.</param>
/// <param name="RoleType">That role's type, denormalized for lists.</param>
/// <param name="RoleLabel">That role's label.</param>
/// <param name="PersonId">The attached person, when the party is a person.</param>
/// <param name="CompanyId">The attached company, when the party is a company.</param>
/// <param name="DisplayName">Name of whichever party is attached.</param>
/// <param name="Status">How firm the attachment is.</param>
/// <param name="StartsOn">When it began.</param>
/// <param name="EndsOn">When it ended, or null while it holds.</param>
/// <param name="HoldsTheRole">Whether it currently occupies the role.</param>
/// <param name="Source">Where the information came from.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="UpdatedAt">Last change instant.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record AttachmentModel(
    Guid Id,
    Guid ProjectRoleId,
    string RoleType,
    string? RoleLabel,
    Guid? PersonId,
    Guid? CompanyId,
    string DisplayName,
    string Status,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    bool HoldsTheRole,
    string? Source,
    string? Notes,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Id">Participation identifier.</param>
/// <param name="CompanyId">The company involved.</param>
/// <param name="CompanyName">Its name.</param>
/// <param name="Capacity">The capacity it acts in.</param>
/// <param name="StartsOn">When the involvement began.</param>
/// <param name="EndsOn">When it ended, or null while current.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record ProjectCompanyModel(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Capacity,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    string? Notes);

/// <param name="Id">Source property identifier.</param>
/// <param name="Title">Its title.</param>
/// <param name="Type">What kind of property it is.</param>
/// <param name="AttributedCreator">Creator as reported. Attribution, not ownership.</param>
/// <param name="CreatorPersonId">The creator, when they are in the directory.</param>
/// <param name="SourceReference">External identifier: ISBN, URL, publication.</param>
/// <param name="Provenance">Where the agency's information came from.</param>
/// <param name="Year">Year of publication or release.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="ProjectCount">How many projects reference it.</param>
/// <param name="UpdatedAt">Last change instant.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record SourcePropertyModel(
    Guid Id,
    string Title,
    string Type,
    string? AttributedCreator,
    Guid? CreatorPersonId,
    string? SourceReference,
    string? Provenance,
    int? Year,
    string? Notes,
    int ProjectCount,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="MaterialId">The linked material.</param>
/// <param name="Title">Its title.</param>
/// <param name="Type">What kind of artifact it is.</param>
/// <param name="Status">Whether it is ready to send.</param>
/// <param name="PersonId">Whose material it is.</param>
/// <param name="PersonName">That person's name.</param>
/// <param name="Notes">Why it is linked to this project.</param>
public sealed record ProjectMaterialModel(
    Guid MaterialId,
    string Title,
    string Type,
    string Status,
    Guid PersonId,
    string PersonName,
    string? Notes);

/// <param name="Summary">Headline fields.</param>
/// <param name="Logline">One-line description.</param>
/// <param name="Synopsis">Longer description.</param>
/// <param name="Notes">Internal notes. Factual context, not strategy.</param>
/// <param name="Roles">Positions on the project, with who has held them.</param>
/// <param name="Companies">Companies involved, current and past.</param>
/// <param name="SourceProperties">Properties the project derives from.</param>
/// <param name="Materials">Materials linked to the project.</param>
/// <param name="Packages">Packages assembled around it.</param>
/// <param name="CreatedAt">Creation instant.</param>
public sealed record ProjectDetailModel(
    ProjectSummaryModel Summary,
    string? Logline,
    string? Synopsis,
    string? Notes,
    IReadOnlyList<ProjectRoleModel> Roles,
    IReadOnlyList<ProjectCompanyModel> Companies,
    IReadOnlyList<SourcePropertyModel> SourceProperties,
    IReadOnlyList<ProjectMaterialModel> Materials,
    IReadOnlyList<PackageSummaryModel> Packages,
    DateTimeOffset CreatedAt);

/// <param name="Id">Package identifier.</param>
/// <param name="ProjectId">The project it is built around.</param>
/// <param name="ProjectTitle">That project's title.</param>
/// <param name="Name">What the package is called.</param>
/// <param name="Status">How far along it is.</param>
/// <param name="LeadUserId">Internal owner.</param>
/// <param name="LeadDisplayName">That person's name.</param>
/// <param name="ElementCount">How many elements it holds.</param>
/// <param name="OpenRoleCount">Open roles it still names.</param>
/// <param name="UpdatedAt">Last change instant.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record PackageSummaryModel(
    Guid Id,
    Guid ProjectId,
    string ProjectTitle,
    string Name,
    string Status,
    Guid LeadUserId,
    string? LeadDisplayName,
    int ElementCount,
    int OpenRoleCount,
    DateTimeOffset UpdatedAt,
    int Version);

/// <param name="Id">Element identifier.</param>
/// <param name="Kind">What sort of thing this points at.</param>
/// <param name="TargetId">The thing it points at, interpreted by kind.</param>
/// <param name="DisplayName">A readable name for the target.</param>
/// <param name="Detail">Secondary line: a role type, a company capacity, a material type.</param>
/// <param name="IsAttached">
/// Whether this element corresponds to a real attachment. False for everything the
/// agency merely proposes, which is the distinction a package must never blur.
/// </param>
/// <param name="Note">Why it is in the package.</param>
/// <param name="Position">Its place in the package's ordering.</param>
public sealed record PackageElementModel(
    Guid Id,
    string Kind,
    Guid TargetId,
    string DisplayName,
    string? Detail,
    bool IsAttached,
    string? Note,
    int Position);

/// <param name="Summary">Headline fields.</param>
/// <param name="Thesis">What the package argues, in a sentence.</param>
/// <param name="StrategyNotes">
/// Internal strategy. Null when the caller lacks <c>packages.strategy.read</c>,
/// deliberately indistinguishable from the field being empty.
/// </param>
/// <param name="Elements">What is in the package.</param>
/// <param name="Gaps">Roles on the project that nothing currently holds.</param>
/// <param name="CreatedAt">Creation instant.</param>
public sealed record PackageDetailModel(
    PackageSummaryModel Summary,
    string? Thesis,
    string? StrategyNotes,
    IReadOnlyList<PackageElementModel> Elements,
    IReadOnlyList<ProjectRoleModel> Gaps,
    DateTimeOffset CreatedAt);

/// <param name="OccurredAt">When it happened.</param>
/// <param name="Kind">What kind of change it was.</param>
/// <param name="Summary">A readable sentence describing it.</param>
/// <param name="Detail">Secondary context, when there is any.</param>
/// <param name="ActorDisplayName">Who did it, when the record names somebody.</param>
public sealed record ProjectHistoryEntryModel(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <param name="RecentlyChanged">Projects that changed most recently.</param>
/// <param name="Packages">Packages in progress.</param>
/// <param name="RecentAttachments">Attachments that changed recently.</param>
/// <param name="ActiveProjectCount">How many projects are being worked.</param>
/// <param name="PackagesInProgressCount">How many packages are being assembled.</param>
public sealed record ProjectCommandCenterModel(
    IReadOnlyList<ProjectSummaryModel> RecentlyChanged,
    IReadOnlyList<PackageSummaryModel> Packages,
    IReadOnlyList<AttachmentModel> RecentAttachments,
    int ActiveProjectCount,
    int PackagesInProgressCount);

/// <param name="Status">Restrict to one operational status.</param>
/// <param name="Stage">Restrict to one development stage.</param>
/// <param name="Type">Restrict to one kind of work.</param>
/// <param name="LeadUserId">Restrict to one internal owner.</param>
/// <param name="AttachedPersonId">Only projects this person currently holds a role on.</param>
/// <param name="MissingRoleType">
/// Only projects with no party currently holding a role of this type. The rule is
/// stated rather than implied: a project counts as missing a director when no
/// attachment to a directing role currently holds it, whether or not such a role
/// row exists at all.
/// </param>
/// <param name="Search">Free text over title and working title.</param>
public sealed record ProjectFilter(
    ProjectStatus? Status = null,
    DevelopmentStage? Stage = null,
    ProjectType? Type = null,
    Guid? LeadUserId = null,
    Guid? AttachedPersonId = null,
    ProjectRoleType? MissingRoleType = null,
    string? Search = null);

/// <param name="Status">Restrict to one package status.</param>
/// <param name="ProjectId">Restrict to one project.</param>
/// <param name="LeadUserId">Restrict to one internal owner.</param>
/// <param name="Search">Free text over the package name.</param>
public sealed record PackageFilter(
    PackageStatus? Status = null,
    Guid? ProjectId = null,
    Guid? LeadUserId = null,
    string? Search = null);

/// <param name="Search">Free text over title and attributed creator.</param>
/// <param name="Type">Restrict to one kind of property.</param>
public sealed record SourcePropertyFilter(string? Search = null, SourcePropertyType? Type = null);

/// <summary>
/// Read-side queries for the project model.
/// </summary>
/// <remarks>
/// Implemented in infrastructure as projections and deliberately unauthorized:
/// <c>ProjectQueryService</c> applies the tenant-scoped checks and the strategy
/// redaction, so there is one place to look for both.
/// </remarks>
public interface IProjectQueries
{
    Task<IReadOnlyList<ProjectSummaryModel>> ListProjectsAsync(
        OrganizationId organizationId,
        ProjectFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<ProjectDetailModel?> GetProjectAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttachmentModel>> ListAttachmentsAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        bool currentOnly,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectHistoryEntryModel>> GetProjectHistoryAsync(
        OrganizationId organizationId,
        ProjectId projectId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourcePropertyModel>> ListSourcePropertiesAsync(
        OrganizationId organizationId,
        SourcePropertyFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SourcePropertyModel?> GetSourcePropertyAsync(
        OrganizationId organizationId,
        SourcePropertyId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageSummaryModel>> ListPackagesAsync(
        OrganizationId organizationId,
        PackageFilter filter,
        int limit,
        CancellationToken cancellationToken = default);

    Task<PackageDetailModel?> GetPackageAsync(
        OrganizationId organizationId,
        PackageId id,
        CancellationToken cancellationToken = default);

    Task<ProjectCommandCenterModel> GetProjectCommandCenterAsync(
        OrganizationId organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
