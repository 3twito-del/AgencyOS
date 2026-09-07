namespace AgencyOS.Contracts.SavedViews;

/// <summary>
/// The filters a saved view applies.
/// </summary>
/// <remarks>
/// A closed set of optional predicates, not an expression language. Everything
/// here maps to a specific reviewed clause in the query layer, so a saved view can
/// never become an arbitrary query.
/// </remarks>
/// <param name="Status">Person or company lifecycle status.</param>
/// <param name="CompanyId">Restricts people to one primary company.</param>
/// <param name="TitleContains">Substring match on a person's title.</param>
/// <param name="TextContains">Substring match on the primary name field.</param>
/// <param name="TaskState">Open or Completed.</param>
/// <param name="DueWithinDays">Tasks due within this many days.</param>
/// <param name="OverdueOnly">Only tasks past their due date.</param>
/// <param name="Discipline">Restricts talent to one professional discipline.</param>
/// <param name="ScopeArea">Restricts talent to one represented area.</param>
/// <param name="LeadUserId">Restricts talent to one internal owner.</param>
/// <param name="ClientsOnly">Only people the agency currently represents.</param>
/// <param name="FormerClientsOnly">Only people the agency used to represent.</param>
/// <param name="ProspectStage">Restricts prospects to one stage.</param>
/// <param name="OwnerUserId">Restricts prospects to one internal owner.</param>
/// <param name="FollowUpWithinDays">Only prospects needing attention within this many days.</param>
public sealed record SavedViewFiltersModel(
    string? Status = null,
    Guid? CompanyId = null,
    string? TitleContains = null,
    string? TextContains = null,
    string? TaskState = null,
    int? DueWithinDays = null,
    bool OverdueOnly = false,
    string? Discipline = null,
    string? ScopeArea = null,
    Guid? LeadUserId = null,
    bool ClientsOnly = false,
    bool FormerClientsOnly = false,
    string? ProspectStage = null,
    Guid? OwnerUserId = null,
    int? FollowUpWithinDays = null,
    string? ProjectType = null,
    string? DevelopmentStage = null,
    string? ProjectStatus = null,
    Guid? AttachedPersonId = null,
    string? MissingRoleType = null,
    string? PackageStatus = null,
    Guid? ProjectId = null);

/// <param name="Field">Field to order by. Must be sortable for the target.</param>
/// <param name="Direction">Ascending or Descending.</param>
public sealed record SavedViewSortModel(string Field, string Direction);

/// <summary>
/// A saved view's query, as a versioned document.
/// </summary>
/// <param name="DefinitionVersion">
/// Schema version of this document. Version 2 is current; version 1 documents are
/// still understood and read as they always meant, because version 2 only added
/// targets and filters. A version the server genuinely does not understand is
/// rejected rather than guessed at.
/// </param>
/// <param name="Target">People, Companies or Tasks.</param>
/// <param name="Filters">Predicates to apply.</param>
/// <param name="Sort">Ordering, or null for the target's default.</param>
public sealed record SavedViewDefinitionModel(
    int DefinitionVersion,
    string Target,
    SavedViewFiltersModel Filters,
    SavedViewSortModel? Sort = null);

/// <param name="Name">What the user calls this view.</param>
/// <param name="Definition">The query document.</param>
public sealed record CreateSavedViewRequest(string Name, SavedViewDefinitionModel Definition);

/// <param name="Name">Replacement name.</param>
/// <param name="Definition">Replacement query document.</param>
/// <param name="ExpectedVersion">Version the caller observed. Required.</param>
public sealed record UpdateSavedViewRequest(
    string Name,
    SavedViewDefinitionModel Definition,
    int ExpectedVersion);

/// <summary>
/// What running a saved view produced.
/// </summary>
/// <remarks>
/// Typed per target rather than one untyped list, so the client renders people as
/// people and tasks as tasks without inspecting a discriminator. Exactly one of
/// the three collections is populated, named by <paramref name="Target"/>.
/// </remarks>
/// <param name="Target">Which list this is: People, Companies, Tasks, Talent, Prospects, Projects or Packages.</param>
/// <param name="People">Matching people.</param>
/// <param name="Companies">Matching companies.</param>
/// <param name="Tasks">Matching tasks.</param>
/// <param name="Talent">Matching talent.</param>
/// <param name="Prospects">Matching prospects.</param>
/// <param name="Projects">Matching projects.</param>
/// <param name="Packages">Matching packages.</param>
public sealed record SavedViewResultsResponse(
    string Target,
    IReadOnlyList<AgencyOS.Contracts.PeopleSlice.PersonSummaryResponse> People,
    IReadOnlyList<AgencyOS.Contracts.PeopleSlice.CompanySummaryResponse> Companies,
    IReadOnlyList<AgencyOS.Contracts.PeopleSlice.TaskResponse> Tasks,
    IReadOnlyList<AgencyOS.Contracts.Representation.TalentSummaryResponse> Talent,
    IReadOnlyList<AgencyOS.Contracts.Representation.ProspectResponse> Prospects,
    IReadOnlyList<AgencyOS.Contracts.Projects.ProjectSummaryResponse> Projects,
    IReadOnlyList<AgencyOS.Contracts.Projects.PackageSummaryResponse> Packages);

/// <param name="Id">Saved view identifier.</param>
/// <param name="Name">What the user calls it.</param>
/// <param name="Target">What it lists.</param>
/// <param name="Definition">The query document.</param>
/// <param name="DefinitionVersion">Schema version of the document.</param>
/// <param name="Version">Optimistic concurrency token.</param>
/// <param name="CreatedAt">Creation instant, UTC.</param>
/// <param name="UpdatedAt">Last change instant, UTC.</param>
public sealed record SavedViewResponse(
    Guid Id,
    string Name,
    string Target,
    SavedViewDefinitionModel Definition,
    int DefinitionVersion,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
