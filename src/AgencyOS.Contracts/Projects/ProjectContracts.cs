namespace AgencyOS.Contracts.Projects;

// ------------------------------------------------------------------- requests

/// <param name="Title">Title in use.</param>
/// <param name="Type">FeatureFilm, TelevisionSeries, LimitedSeries, Pilot, ShortFilm, Documentary, Book, Podcast, Stage, Digital, BrandEntertainment or Other.</param>
/// <param name="WorkingTitle">Internal or provisional title, when it differs.</param>
/// <param name="Stage">Concept, Development, Packaging, PreProduction, Production, PostProduction or Released. Defaults to Concept.</param>
/// <param name="Logline">One-line description.</param>
/// <param name="Synopsis">Longer description.</param>
/// <param name="PrimaryCompanyId">The company most associated with the project.</param>
/// <param name="Year">Year the project is dated to.</param>
/// <param name="LeadUserId">Internal owner.</param>
/// <param name="Notes">Factual context. Not strategy.</param>
public sealed record CreateProjectRequest(
    string Title,
    string Type,
    string? WorkingTitle = null,
    string? Stage = null,
    string? Logline = null,
    string? Synopsis = null,
    Guid? PrimaryCompanyId = null,
    int? Year = null,
    Guid? LeadUserId = null,
    string? Notes = null);

/// <param name="ExpectedVersion">The version the caller observed. Required (ADR-0014).</param>
public sealed record UpdateProjectRequest(
    string Title,
    string Type,
    int ExpectedVersion,
    string? WorkingTitle = null,
    string? Logline = null,
    string? Synopsis = null,
    Guid? PrimaryCompanyId = null,
    int? Year = null,
    Guid? LeadUserId = null,
    string? Notes = null);

/// <param name="Status">Active, Inactive, Completed, Cancelled or Archived.</param>
/// <param name="Reason">Why, for the project history.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record ChangeProjectStatusRequest(
    string Status,
    int ExpectedVersion,
    string? Reason = null);

/// <param name="Stage">The development stage to move to.</param>
/// <param name="Reason">Why, for the project history.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record ChangeProjectStageRequest(
    string Stage,
    int ExpectedVersion,
    string? Reason = null);

/// <param name="Type">Writer, Director, Actor, Producer, ExecutiveProducer, Showrunner, Creator, Composer, Cinematographer, Host or Other.</param>
/// <param name="Label">Character name, or a description of the position.</param>
/// <param name="IsExclusive">
/// Whether only one party may hold this role at a time. Opt-in: claiming it wrongly
/// blocks legitimate data entry, omitting it wrongly only fails to catch a duplicate.
/// </param>
/// <param name="Notes">Free-text context.</param>
/// <param name="ExpectedVersion">The project's version. Required.</param>
public sealed record CreateProjectRoleRequest(
    string Type,
    int ExpectedVersion,
    string? Label = null,
    bool IsExclusive = false,
    string? Notes = null);

/// <param name="Action">Update, Hold, Close or Reopen.</param>
/// <param name="ExpectedVersion">The project's version. Required.</param>
public sealed record ChangeProjectRoleRequest(
    string Action,
    int ExpectedVersion,
    string? Label = null,
    bool IsExclusive = false,
    string? Notes = null);

/// <param name="PersonId">The person attaching. Exactly one of this and CompanyId.</param>
/// <param name="CompanyId">The company attaching. Exactly one of this and PersonId.</param>
/// <param name="Status">InDiscussion, Attached or Conditional. Terminal states are reached by ending, not by creating.</param>
/// <param name="StartsOn">When the attachment begins.</param>
/// <param name="EndsOn">When it is known to end, when that is already agreed.</param>
/// <param name="Source">Where the information came from.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="ExpectedVersion">The project's version. Required.</param>
public sealed record AttachToRoleRequest(
    string Status,
    DateOnly StartsOn,
    int ExpectedVersion,
    Guid? PersonId = null,
    Guid? CompanyId = null,
    DateOnly? EndsOn = null,
    string? Source = null,
    string? Notes = null);

/// <param name="Status">The status to move to.</param>
/// <param name="OccurredOn">When it happened, which is not always today.</param>
/// <param name="Reason">Why, for the history.</param>
/// <param name="ExpectedVersion">The <em>attachment's</em> version, not the project's.</param>
public sealed record ChangeAttachmentRequest(
    string Status,
    DateOnly OccurredOn,
    int ExpectedVersion,
    string? Reason = null);

/// <param name="CompanyId">The company involved.</param>
/// <param name="Capacity">Studio, Network, Streamer, ProductionCompany, Financier, Distributor, SalesCompany or Other.</param>
/// <param name="StartsOn">When the involvement began.</param>
/// <param name="EndsOn">When it ended, when that is already known.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="ExpectedVersion">The project's version. Required.</param>
public sealed record AddProjectCompanyRequest(
    Guid CompanyId,
    string Capacity,
    DateOnly StartsOn,
    int ExpectedVersion,
    DateOnly? EndsOn = null,
    string? Notes = null);

/// <param name="EndsOn">When the involvement ended.</param>
/// <param name="ExpectedVersion">The project's version. Required.</param>
public sealed record EndProjectCompanyRequest(DateOnly EndsOn, int ExpectedVersion);

/// <param name="Title">The property's title.</param>
/// <param name="Type">OriginalScreenplay, Book, Article, Podcast, LifeRights, SourceFilm, Franchise, StageWork, OriginalConcept or Other.</param>
/// <param name="AttributedCreator">
/// The creator as reported. Attribution only: AgencyOS asserts nothing about who
/// owns anything, and rights are M8.
/// </param>
/// <param name="CreatorPersonId">The creator, when they are in the directory.</param>
/// <param name="SourceReference">ISBN, URL, publication - whatever identifies it externally.</param>
/// <param name="Provenance">Where the agency's information came from.</param>
/// <param name="Year">Year of publication or release.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record CreateSourcePropertyRequest(
    string Title,
    string Type,
    string? AttributedCreator = null,
    Guid? CreatorPersonId = null,
    string? SourceReference = null,
    string? Provenance = null,
    int? Year = null,
    string? Notes = null);

/// <param name="SourcePropertyId">The property to link or unlink.</param>
/// <param name="Notes">Why the project derives from it.</param>
/// <param name="ExpectedVersion">The project's version. Required.</param>
public sealed record LinkSourcePropertyRequest(
    Guid SourcePropertyId,
    int ExpectedVersion,
    string? Notes = null);

/// <param name="MaterialId">The material to link or unlink.</param>
/// <param name="Notes">Why it belongs to this project.</param>
/// <param name="ExpectedVersion">The project's version. Required.</param>
public sealed record LinkProjectMaterialRequest(
    Guid MaterialId,
    int ExpectedVersion,
    string? Notes = null);

/// <param name="ProjectId">The project to link to, or null to unlink.</param>
/// <param name="ExpectedVersion">The credit's version. Required.</param>
public sealed record LinkCreditToProjectRequest(Guid? ProjectId, int ExpectedVersion);

/// <param name="ProjectId">The project the package is built around.</param>
/// <param name="Name">What the package is called.</param>
/// <param name="LeadUserId">Internal owner. A package nobody is assembling is not a package.</param>
/// <param name="Thesis">What the package argues, in a sentence.</param>
/// <param name="StrategyNotes">Internal strategy. Requires <c>packages.strategy.read</c> to read back.</param>
public sealed record CreatePackageRequest(
    Guid ProjectId,
    string Name,
    Guid LeadUserId,
    string? Thesis = null,
    string? StrategyNotes = null);

/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record UpdatePackageRequest(
    string Name,
    Guid LeadUserId,
    int ExpectedVersion,
    string? Thesis = null,
    string? StrategyNotes = null);

/// <param name="Status">Draft, Assembling, Ready, Active, Paused, Closed or Abandoned.</param>
/// <param name="Reason">Why, for the history.</param>
/// <param name="ExpectedVersion">The version the caller observed. Required.</param>
public sealed record ChangePackageStatusRequest(
    string Status,
    int ExpectedVersion,
    string? Reason = null);

/// <param name="Kind">
/// AttachedParty, ProposedPerson, ProposedCompany, OpenRole, Material or
/// SourceProperty. Only AttachedParty refers to something actually attached.
/// </param>
/// <param name="TargetId">What the element points at, interpreted by kind.</param>
/// <param name="Note">Why it is in the package.</param>
/// <param name="ExpectedVersion">The package's version. Required.</param>
public sealed record AddPackageElementRequest(
    string Kind,
    Guid TargetId,
    int ExpectedVersion,
    string? Note = null);

/// <param name="ExpectedVersion">The package's version. Required.</param>
public sealed record RemovePackageElementRequest(int ExpectedVersion);

// ------------------------------------------------------------------ responses

/// <param name="Id">Project identifier.</param>
/// <param name="Title">Title in use.</param>
/// <param name="WorkingTitle">Internal or provisional title.</param>
/// <param name="Type">What kind of work it is.</param>
/// <param name="Status">Operational status of the record.</param>
/// <param name="Stage">Where the work itself has got to.</param>
/// <param name="Year">Year the project is dated to.</param>
/// <param name="PrimaryCompanyId">The company most associated with it.</param>
/// <param name="PrimaryCompanyName">That company's name.</param>
/// <param name="LeadUserId">Internal owner.</param>
/// <param name="LeadDisplayName">That person's name.</param>
/// <param name="OpenRoleCount">Roles still needing somebody.</param>
/// <param name="AttachedCount">Parties currently holding a role.</param>
/// <param name="PackageCount">Packages assembled around it.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record ProjectSummaryResponse(
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

/// <param name="Id">Attachment identifier.</param>
/// <param name="ProjectRoleId">The role held.</param>
/// <param name="RoleType">That role's type.</param>
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
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record AttachmentResponse(
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

/// <param name="Id">Role identifier.</param>
/// <param name="Type">The kind of position.</param>
/// <param name="Label">Character name or description.</param>
/// <param name="Status">Open, Filled, OnHold or Closed.</param>
/// <param name="IsExclusive">Whether only one party may hold it.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="Attachments">Who has held it, current and past.</param>
public sealed record ProjectRoleResponse(
    Guid Id,
    string Type,
    string? Label,
    string Status,
    bool IsExclusive,
    string? Notes,
    IReadOnlyList<AttachmentResponse> Attachments);

/// <param name="Id">Participation identifier.</param>
/// <param name="CompanyId">The company involved.</param>
/// <param name="CompanyName">Its name.</param>
/// <param name="Capacity">The capacity it acts in.</param>
/// <param name="StartsOn">When the involvement began.</param>
/// <param name="EndsOn">When it ended, or null while current.</param>
/// <param name="Notes">Free-text context.</param>
public sealed record ProjectCompanyResponse(
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
/// <param name="SourceReference">External identifier.</param>
/// <param name="Provenance">Where the information came from.</param>
/// <param name="Year">Year of publication or release.</param>
/// <param name="Notes">Free-text context.</param>
/// <param name="ProjectCount">How many projects reference it.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record SourcePropertyResponse(
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
public sealed record ProjectMaterialResponse(
    Guid MaterialId,
    string Title,
    string Type,
    string Status,
    Guid PersonId,
    string PersonName,
    string? Notes);

/// <param name="Id">Package identifier.</param>
/// <param name="ProjectId">The project it is built around.</param>
/// <param name="ProjectTitle">That project's title.</param>
/// <param name="Name">What the package is called.</param>
/// <param name="Status">How far along it is.</param>
/// <param name="LeadUserId">Internal owner.</param>
/// <param name="LeadDisplayName">That person's name.</param>
/// <param name="ElementCount">How many elements it holds.</param>
/// <param name="OpenRoleCount">Open roles on the project it still needs.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
/// <param name="Version">Optimistic concurrency token.</param>
public sealed record PackageSummaryResponse(
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

/// <param name="Project">Headline fields.</param>
/// <param name="Logline">One-line description.</param>
/// <param name="Synopsis">Longer description.</param>
/// <param name="Notes">Factual context.</param>
/// <param name="Roles">Positions on the project, with who has held them.</param>
/// <param name="Companies">Companies involved, current and past.</param>
/// <param name="SourceProperties">Properties the project derives from.</param>
/// <param name="Materials">Materials linked to it.</param>
/// <param name="Packages">Packages assembled around it.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
public sealed record ProjectDetailResponse(
    ProjectSummaryResponse Project,
    string? Logline,
    string? Synopsis,
    string? Notes,
    IReadOnlyList<ProjectRoleResponse> Roles,
    IReadOnlyList<ProjectCompanyResponse> Companies,
    IReadOnlyList<SourcePropertyResponse> SourceProperties,
    IReadOnlyList<ProjectMaterialResponse> Materials,
    IReadOnlyList<PackageSummaryResponse> Packages,
    DateTimeOffset CreatedAt);

/// <param name="Id">Element identifier.</param>
/// <param name="Kind">What sort of thing this points at.</param>
/// <param name="TargetId">The thing it points at, interpreted by kind.</param>
/// <param name="DisplayName">A readable name for the target.</param>
/// <param name="Detail">Secondary line: a role, a capacity, or "Proposed".</param>
/// <param name="IsAttached">
/// Whether this element corresponds to a real, current attachment. False for
/// everything the agency merely proposes - the distinction a package must never
/// blur.
/// </param>
/// <param name="Note">Why it is in the package.</param>
/// <param name="Position">Its place in the package's ordering.</param>
public sealed record PackageElementResponse(
    Guid Id,
    string Kind,
    Guid TargetId,
    string DisplayName,
    string? Detail,
    bool IsAttached,
    string? Note,
    int Position);

/// <param name="Package">Headline fields.</param>
/// <param name="Thesis">What the package argues.</param>
/// <param name="StrategyNotes">
/// Internal strategy. Absent without <c>packages.strategy.read</c>, and absent is
/// deliberately indistinguishable from empty.
/// </param>
/// <param name="Elements">What is in the package.</param>
/// <param name="Gaps">Roles on the project that nothing currently holds.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
public sealed record PackageDetailResponse(
    PackageSummaryResponse Package,
    string? Thesis,
    string? StrategyNotes,
    IReadOnlyList<PackageElementResponse> Elements,
    IReadOnlyList<ProjectRoleResponse> Gaps,
    DateTimeOffset CreatedAt);

/// <param name="OccurredAt">When it happened, UTC.</param>
/// <param name="Kind">What kind of change it was.</param>
/// <param name="Summary">A readable sentence describing it.</param>
/// <param name="Detail">Secondary context, when there is any.</param>
/// <param name="ActorDisplayName">Who did it, when the record names somebody.</param>
public sealed record ProjectHistoryEntryResponse(
    DateTimeOffset OccurredAt,
    string Kind,
    string Summary,
    string? Detail,
    string? ActorDisplayName);

/// <param name="RecentlyChanged">Active projects that changed most recently.</param>
/// <param name="Packages">Packages currently being assembled or used.</param>
/// <param name="RecentAttachments">Attachments that changed recently.</param>
/// <param name="ActiveProjectCount">How many projects are being worked.</param>
/// <param name="PackagesInProgressCount">How many packages are in progress.</param>
public sealed record ProjectCommandCenterResponse(
    IReadOnlyList<ProjectSummaryResponse> RecentlyChanged,
    IReadOnlyList<PackageSummaryResponse> Packages,
    IReadOnlyList<AttachmentResponse> RecentAttachments,
    int ActiveProjectCount,
    int PackagesInProgressCount);
