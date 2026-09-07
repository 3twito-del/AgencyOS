using System.Diagnostics;
using AgencyOS.Api.Authorization;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Directory;
using AgencyOS.Application.SavedViews;
using AgencyOS.Application.Search;
using AgencyOS.Application.Sync;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.SavedViews;
using AgencyOS.Contracts.Search;
using AgencyOS.Contracts.Sync;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.SavedViews;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// Search, saved views and synchronization, routed under the tenant that owns
/// the records.
/// </summary>
/// <remarks>
/// The endpoint policies are early gates only, as everywhere else: the
/// authoritative, tenant-scoped check happens inside each service, so holding a
/// permission in one tenant gains nothing in another (ADR-0007).
/// </remarks>
internal static class M3Endpoints
{
    public static void MapSearchSavedViewsAndSync(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}");

        MapSearch(tenant);
        MapSavedViews(tenant);
        MapSync(tenant);
    }

    // ---------------------------------------------------------------- search

    private static void MapSearch(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/search", async (
                Guid organizationId,
                SearchService search,
                string? q,
                string? types,
                bool? includeArchived,
                int? skip,
                int? take,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity = AgencyOsTelemetry.Source.StartActivity("agencyos.search");

                SearchResultModel result = await search
                    .SearchAsync(
                        new OrganizationId(organizationId),
                        q,
                        ParseTypes(types),
                        includeArchived ?? false,
                        skip ?? 0,
                        take ?? 25,
                        cancellationToken)
                    .ConfigureAwait(false);

                // The query itself is never recorded: it is a person's name far more
                // often than not, and a trace is not the place for one.
                activity?.SetTag("agencyos.search.hits", result.Hits.Count);

                AgencyOsTelemetry.Searches.Add(
                    1,
                    new KeyValuePair<string, object?>("matched", result.Hits.Count > 0));

                return Results.Ok(new SearchResponse(
                    q ?? string.Empty,
                    [.. result.Hits.Select(Map)],
                    skip ?? 0,
                    take ?? 25,
                    result.HasMore));
            })

            // Searching people is the narrowest permission any search needs; the
            // service intersects the requested types with what the caller can
            // actually read, and refuses only when nothing survives.
            .RequireAuthorization(PermissionPolicy.Name(Permission.PeopleRead))
            .WithName("Search");
    }

    // ----------------------------------------------------------- saved views

    private static void MapSavedViews(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/saved-views", async (
                Guid organizationId,
                SavedViewService views,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<SavedView> saved = await views
                    .ListAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(saved.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OrganizationsRead))
            .WithName("ListSavedViews");

        tenant.MapGet("/saved-views/{savedViewId:guid}", async (
                Guid organizationId,
                Guid savedViewId,
                SavedViewService views,
                CancellationToken cancellationToken) =>
            {
                SavedView? view = await views
                    .GetAsync(new OrganizationId(organizationId), new SavedViewId(savedViewId), cancellationToken)
                    .ConfigureAwait(false);

                return view is null ? Results.NotFound() : Results.Ok(Map(view));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OrganizationsRead))
            .WithName("GetSavedView");

        tenant.MapGet("/saved-views/{savedViewId:guid}/results", async (
                Guid organizationId,
                Guid savedViewId,
                SavedViewService views,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity = AgencyOsTelemetry.Source.StartActivity("agencyos.savedview.run");

                SavedViewResultModel? results = await views
                    .RunAsync(
                        new OrganizationId(organizationId),
                        new SavedViewId(savedViewId),
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (results is null)
                {
                    return Results.NotFound();
                }

                activity?.SetTag("agencyos.savedview.rows", results.Count);

                return Results.Ok(new SavedViewResultsResponse(
                    results.Target.ToString(),
                    [.. results.People.Select(MapPerson)],
                    [.. results.Companies.Select(MapCompany)],
                    [.. results.Tasks.Select(MapTask)],
                    [.. results.Talent.Select(M4Endpoints.MapTalentSummary)],
                    [.. results.Prospects.Select(M4Endpoints.MapProspect)],
                    [.. results.Projects.Select(M5Endpoints.MapProjectSummary)],
                    [.. results.Packages.Select(M5Endpoints.MapPackageSummary)],
                    [.. results.Opportunities.Select(M6Endpoints.MapOpportunitySummary)],
                    [.. results.Deals.Select(M7Endpoints.MapDealSummary)]));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OrganizationsRead))
            .WithName("RunSavedView");

        tenant.MapPost("/saved-views", async (
                Guid organizationId,
                CreateSavedViewRequest request,
                SavedViewService views,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                SavedViewId id = await views
                    .CreateAsync(
                        new OrganizationId(organizationId),
                        request.Name,
                        ToDefinition(request.Definition),
                        cancellationToken)
                    .ConfigureAwait(false);

                SavedView created = (await views
                    .GetAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false))!;

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/saved-views/{id.Value}",
                    Map(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OrganizationsRead))
            .WithName("CreateSavedView");

        tenant.MapPut("/saved-views/{savedViewId:guid}", async (
                Guid organizationId,
                Guid savedViewId,
                UpdateSavedViewRequest request,
                SavedViewService views,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await views
                    .UpdateAsync(
                        new OrganizationId(organizationId),
                        new SavedViewId(savedViewId),
                        request.Name,
                        ToDefinition(request.Definition),
                        request.ExpectedVersion,
                        cancellationToken)
                    .ConfigureAwait(false);

                SavedView updated = (await views
                    .GetAsync(new OrganizationId(organizationId), new SavedViewId(savedViewId), cancellationToken)
                    .ConfigureAwait(false))!;

                return Results.Ok(Map(updated));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OrganizationsRead))
            .WithName("UpdateSavedView");

        tenant.MapDelete("/saved-views/{savedViewId:guid}", async (
                Guid organizationId,
                Guid savedViewId,
                SavedViewService views,
                CancellationToken cancellationToken) =>
            {
                await views
                    .DeleteAsync(new OrganizationId(organizationId), new SavedViewId(savedViewId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OrganizationsRead))
            .WithName("DeleteSavedView");
    }

    // ------------------------------------------------------------------ sync

    private static void MapSync(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/sync/changes", async (
                Guid organizationId,
                SyncService sync,
                long? cursor,
                int? take,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity = AgencyOsTelemetry.Source.StartActivity("agencyos.sync.pull");

                SyncPageModel page = await sync
                    .ReadChangesAsync(new OrganizationId(organizationId), cursor ?? 0, take, cancellationToken)
                    .ConfigureAwait(false);

                activity?.SetTag("agencyos.sync.from", cursor ?? 0);
                activity?.SetTag("agencyos.sync.to", page.Cursor);
                activity?.SetTag("agencyos.sync.changes", page.Changes.Count);

                AgencyOsTelemetry.SyncPages.Add(1);

                return Results.Ok(new SyncChangesResponse(
                    page.Cursor,
                    page.HasMore,
                    [.. page.Changes.Select(Map)],
                    [.. page.People.Select(MapPerson)],
                    [.. page.Companies.Select(MapCompany)],
                    [.. page.Tasks.Select(MapTask)],
                    [.. page.Talent.Select(M4Endpoints.MapTalentSummary)]));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PeopleRead))
            .WithName("ReadSyncChanges");

        tenant.MapGet("/sync/head", async (
                Guid organizationId,
                SyncService sync,
                CancellationToken cancellationToken) =>
            {
                long head = await sync
                    .GetHeadAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { cursor = head });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PeopleRead))
            .WithName("GetSyncHead");
    }

    // --------------------------------------------------------------- parsing

    /// <summary>
    /// Parses the comma-separated type filter.
    /// </summary>
    /// <remarks>
    /// An unrecognized name is refused rather than ignored. Silently dropping it
    /// would return results for the types that happened to parse, which reads as
    /// "there is nothing else" rather than "you asked for something that does not
    /// exist".
    /// </remarks>
    private static IReadOnlySet<SearchEntityType>? ParseTypes(string? types)
    {
        if (string.IsNullOrWhiteSpace(types))
        {
            return null;
        }

        HashSet<SearchEntityType> parsed = [];

        foreach (string name in types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            parsed.Add(EndpointParsing.ParseEnum<SearchEntityType>(name, "types"));
        }

        return parsed;
    }

    private static SavedViewDefinition ToDefinition(SavedViewDefinitionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        SavedViewFiltersModel filters = model.Filters ?? new SavedViewFiltersModel();

        return new SavedViewDefinition(
            model.DefinitionVersion,
            EndpointParsing.ParseEnum<SavedViewTarget>(model.Target, nameof(model.Target)),
            ToFilters(filters),
            model.Sort is null
                ? null
                : new SavedViewSort(
                    model.Sort.Field,
                    EndpointParsing.ParseEnum<SavedViewSortDirection>(model.Sort.Direction, "Sort.Direction")));
    }

    /// <summary>
    /// Maps the wire filters onto the domain filters, field by field, by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named arguments rather than positional ones. These two records have the same
    /// shape, so a positional call compiles perfectly while assigning values to the
    /// wrong fields - which is how M4 shipped eight filters the API never carried.
    /// </para>
    /// <para>
    /// Naming them stops misalignment but <em>not</em> omission: every filter is
    /// optional, so forgetting one still compiles. M5 proved that by forgetting
    /// seven. <c>SavedViewFilterMappingTests</c> closes the gap by reflection -
    /// it round-trips every property on the contract and fails when one does not
    /// survive the journey.
    /// </para>
    /// </remarks>
    internal static SavedViewFilters ToFilters(SavedViewFiltersModel filters) => new(
        Status: filters.Status,
        CompanyId: filters.CompanyId,
        TitleContains: filters.TitleContains,
        TextContains: filters.TextContains,
        TaskState: filters.TaskState,
        DueWithinDays: filters.DueWithinDays,
        OverdueOnly: filters.OverdueOnly,
        Discipline: filters.Discipline,
        ScopeArea: filters.ScopeArea,
        LeadUserId: filters.LeadUserId,
        ClientsOnly: filters.ClientsOnly,
        FormerClientsOnly: filters.FormerClientsOnly,
        ProspectStage: filters.ProspectStage,
        OwnerUserId: filters.OwnerUserId,
        FollowUpWithinDays: filters.FollowUpWithinDays,
        ProjectType: filters.ProjectType,
        DevelopmentStage: filters.DevelopmentStage,
        ProjectStatus: filters.ProjectStatus,
        AttachedPersonId: filters.AttachedPersonId,
        MissingRoleType: filters.MissingRoleType,
        PackageStatus: filters.PackageStatus,
        ProjectId: filters.ProjectId,
        OpportunityKind: filters.OpportunityKind,
        OpportunityStatus: filters.OpportunityStatus,
        TalentProfileId: filters.TalentProfileId,
        PackageId: filters.PackageId,
        TargetCompanyId: filters.TargetCompanyId,
        TargetPersonId: filters.TargetPersonId,
        TargetStage: filters.TargetStage,
        HasSubmission: filters.HasSubmission,
        AwaitingResponse: filters.AwaitingResponse,
        FollowUpDueWithinDays: filters.FollowUpDueWithinDays,
        DealKind: filters.DealKind,
        DealStatus: filters.DealStatus,
        OpportunityId: filters.OpportunityId,
        OpportunityTargetId: filters.OpportunityTargetId,
        CounterpartyCompanyId: filters.CounterpartyCompanyId,
        CounterpartyPersonId: filters.CounterpartyPersonId,
        HasOpenOffer: filters.HasOpenOffer,
        TermsAgreedOnly: filters.TermsAgreedOnly,
        OpenedAfter: filters.OpenedAfter,
        OpenedBefore: filters.OpenedBefore);

    /// <inheritdoc cref="ToFilters"/>
    internal static SavedViewFiltersModel ToModel(SavedViewFilters filters) => new(
        Status: filters.Status,
        CompanyId: filters.CompanyId,
        TitleContains: filters.TitleContains,
        TextContains: filters.TextContains,
        TaskState: filters.TaskState,
        DueWithinDays: filters.DueWithinDays,
        OverdueOnly: filters.OverdueOnly,
        Discipline: filters.Discipline,
        ScopeArea: filters.ScopeArea,
        LeadUserId: filters.LeadUserId,
        ClientsOnly: filters.ClientsOnly,
        FormerClientsOnly: filters.FormerClientsOnly,
        ProspectStage: filters.ProspectStage,
        OwnerUserId: filters.OwnerUserId,
        FollowUpWithinDays: filters.FollowUpWithinDays,
        ProjectType: filters.ProjectType,
        DevelopmentStage: filters.DevelopmentStage,
        ProjectStatus: filters.ProjectStatus,
        AttachedPersonId: filters.AttachedPersonId,
        MissingRoleType: filters.MissingRoleType,
        PackageStatus: filters.PackageStatus,
        ProjectId: filters.ProjectId,
        OpportunityKind: filters.OpportunityKind,
        OpportunityStatus: filters.OpportunityStatus,
        TalentProfileId: filters.TalentProfileId,
        PackageId: filters.PackageId,
        TargetCompanyId: filters.TargetCompanyId,
        TargetPersonId: filters.TargetPersonId,
        TargetStage: filters.TargetStage,
        HasSubmission: filters.HasSubmission,
        AwaitingResponse: filters.AwaitingResponse,
        FollowUpDueWithinDays: filters.FollowUpDueWithinDays,
        DealKind: filters.DealKind,
        DealStatus: filters.DealStatus,
        OpportunityId: filters.OpportunityId,
        OpportunityTargetId: filters.OpportunityTargetId,
        CounterpartyCompanyId: filters.CounterpartyCompanyId,
        CounterpartyPersonId: filters.CounterpartyPersonId,
        HasOpenOffer: filters.HasOpenOffer,
        TermsAgreedOnly: filters.TermsAgreedOnly,
        OpenedAfter: filters.OpenedAfter,
        OpenedBefore: filters.OpenedBefore);

    // --------------------------------------------------------------- mapping

    private static SearchHit Map(SearchHitModel model) => new(
        model.Type.ToString(),
        model.Id,
        model.Title,
        model.Subtitle,
        model.Status,
        model.Score,
        model.MatchedOn.ToString());

    private static SavedViewResponse Map(SavedView view) => new(
        view.Id.Value,
        view.Name,
        view.Target.ToString(),
        new SavedViewDefinitionModel(
            view.Definition.DefinitionVersion,
            view.Definition.Target.ToString(),
            ToModel(view.Definition.Filters),
            view.Definition.Sort is null
                ? null
                : new SavedViewSortModel(
                    view.Definition.Sort.Field,
                    view.Definition.Sort.Direction.ToString())),
        view.DefinitionVersion,
        view.Version,
        view.CreatedAt,
        view.UpdatedAt);

    private static ChangeEntryResponse Map(ChangeEntryModel model) => new(
        model.Sequence,
        model.EntityType,
        model.EntityId,
        model.Kind.ToString(),
        model.OccurredAt);

    private static PersonSummaryResponse MapPerson(PersonSummaryModel model) => new(
        model.Id,
        model.DisplayName,
        model.Title,
        model.Email,
        model.Phone,
        model.Status,
        model.PrimaryCompanyId,
        model.PrimaryCompanyName,
        model.UpdatedAt,
        model.Version);

    private static CompanySummaryResponse MapCompany(CompanySummaryModel model) => new(
        model.Id,
        model.Name,
        model.LegalName,
        model.Type,
        model.Status,
        model.Website,
        model.UpdatedAt,
        model.Version);

    private static TaskResponse MapTask(TaskModel model) => new(
        model.Id,
        model.Title,
        model.State,
        model.Priority,
        model.DueAt,
        model.Subject is null ? null : new PartyReferenceResponse(model.Subject.Kind, model.Subject.Id, model.Subject.Name),
        model.SourceInteractionId,
        model.CreatedAt,
        model.CompletedAt,
        model.Version);
}
