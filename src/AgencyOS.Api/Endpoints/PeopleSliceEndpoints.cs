using AgencyOS.Api.Authorization;
using AgencyOS.Application.Companies;
using AgencyOS.Application.Directory;
using AgencyOS.Application.Interactions;
using AgencyOS.Application.People;
using AgencyOS.Application.Relationships;
using AgencyOS.Application.Tasks;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M2 people slice, routed under the tenant that owns the records.
/// </summary>
/// <remarks>
/// <para>
/// Every route sits beneath <c>/api/v1/organizations/{organizationId}</c>. The
/// tenant is therefore structural rather than a header or an inferred default,
/// which makes the organization-scoped authorization check the natural one to
/// perform and leaves no ambiguity about which tenant a request meant.
/// </para>
/// <para>
/// The endpoint policy is an early gate only. The authoritative, tenant-scoped
/// check happens inside each handler and query service, so a caller holding a
/// permission in one tenant gains nothing in another (ADR-0007).
/// </para>
/// </remarks>
internal static class PeopleSliceEndpoints
{
    public static void MapPeopleSlice(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}");

        MapPeople(tenant);
        MapCompanies(tenant);
        MapRelationships(tenant);
        MapInteractions(tenant);
        MapTasks(tenant);
        MapCommandCenter(tenant);
    }

    // ---------------------------------------------------------------- people

    private static void MapPeople(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/people", async (
                Guid organizationId,
                CreatePersonRequest request,
                CreatePersonHandler handler,
                PeopleSliceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                PersonId personId = await handler.HandleAsync(
                    new CreatePersonCommand(
                        new OrganizationId(organizationId),
                        ToDetails(request.FirstName, request.LastName, request.DisplayName, request.MiddleName,
                            request.PreferredName, request.PrimaryCompanyId, request.Title, request.Email,
                            request.Phone, request.Notes)),
                    cancellationToken).ConfigureAwait(false);

                PersonDetailModel? created = await queries
                    .GetPersonAsync(new OrganizationId(organizationId), personId, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/people/{personId.Value}",
                    Map(created!));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PeopleWrite))
            .WithName("CreatePerson");

        tenant.MapGet("/people", async (
                Guid organizationId,
                PeopleSliceQueryService queries,
                string? search,
                bool? includeArchived,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<PersonSummaryModel> people = await queries
                    .ListPeopleAsync(
                        new OrganizationId(organizationId),
                        search,
                        includeArchived ?? false,
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(people.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PeopleRead))
            .WithName("ListPeople");

        tenant.MapGet("/people/{personId:guid}", async (
                Guid organizationId,
                Guid personId,
                PeopleSliceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                PersonDetailModel? person = await queries
                    .GetPersonAsync(new OrganizationId(organizationId), new PersonId(personId), cancellationToken)
                    .ConfigureAwait(false);

                return person is null ? Results.NotFound() : Results.Ok(Map(person));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PeopleRead))
            .WithName("GetPerson");

        tenant.MapPut("/people/{personId:guid}", async (
                Guid organizationId,
                Guid personId,
                UpdatePersonRequest request,
                UpdatePersonHandler handler,
                PeopleSliceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                    new UpdatePersonCommand(
                        new OrganizationId(organizationId),
                        new PersonId(personId),
                        ToDetails(request.FirstName, request.LastName, request.DisplayName, request.MiddleName,
                            request.PreferredName, request.PrimaryCompanyId, request.Title, request.Email,
                            request.Phone, request.Notes),
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                PersonDetailModel? updated = await queries
                    .GetPersonAsync(new OrganizationId(organizationId), new PersonId(personId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(Map(updated!));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PeopleWrite))
            .WithName("UpdatePerson");

        tenant.MapGet("/people/{personId:guid}/timeline", async (
                Guid organizationId,
                Guid personId,
                PeopleSliceQueryService queries,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<TimelineEntryModel>? timeline = await queries
                    .GetPersonTimelineAsync(
                        new OrganizationId(organizationId),
                        new PersonId(personId),
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return timeline is null ? Results.NotFound() : Results.Ok(timeline.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.InteractionsRead))
            .WithName("GetPersonTimeline");
    }

    // ------------------------------------------------------------- companies

    private static void MapCompanies(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/companies", async (
                Guid organizationId,
                CreateCompanyRequest request,
                CreateCompanyHandler handler,
                PeopleSliceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                CompanyId companyId = await handler.HandleAsync(
                    new CreateCompanyCommand(
                        new OrganizationId(organizationId),
                        new CompanyDetails(
                            request.Name,
                            EndpointParsing.ParseEnum<CompanyType>(request.Type, nameof(request.Type)),
                            request.LegalName,
                            request.Website,
                            request.Notes)),
                    cancellationToken).ConfigureAwait(false);

                CompanyDetailModel? created = await queries
                    .GetCompanyAsync(new OrganizationId(organizationId), companyId, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/companies/{companyId.Value}",
                    Map(created!));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CompaniesWrite))
            .WithName("CreateCompany");

        tenant.MapGet("/companies", async (
                Guid organizationId,
                PeopleSliceQueryService queries,
                string? search,
                bool? includeArchived,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<CompanySummaryModel> companies = await queries
                    .ListCompaniesAsync(
                        new OrganizationId(organizationId),
                        search,
                        includeArchived ?? false,
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(companies.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CompaniesRead))
            .WithName("ListCompanies");

        tenant.MapGet("/companies/{companyId:guid}", async (
                Guid organizationId,
                Guid companyId,
                PeopleSliceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                CompanyDetailModel? company = await queries
                    .GetCompanyAsync(new OrganizationId(organizationId), new CompanyId(companyId), cancellationToken)
                    .ConfigureAwait(false);

                return company is null ? Results.NotFound() : Results.Ok(Map(company));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CompaniesRead))
            .WithName("GetCompany");

        tenant.MapPut("/companies/{companyId:guid}", async (
                Guid organizationId,
                Guid companyId,
                UpdateCompanyRequest request,
                UpdateCompanyHandler handler,
                PeopleSliceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                    new UpdateCompanyCommand(
                        new OrganizationId(organizationId),
                        new CompanyId(companyId),
                        new CompanyDetails(
                            request.Name,
                            EndpointParsing.ParseEnum<CompanyType>(request.Type, nameof(request.Type)),
                            request.LegalName,
                            request.Website,
                            request.Notes),
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                CompanyDetailModel? updated = await queries
                    .GetCompanyAsync(new OrganizationId(organizationId), new CompanyId(companyId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(Map(updated!));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.CompaniesWrite))
            .WithName("UpdateCompany");

        tenant.MapGet("/companies/{companyId:guid}/timeline", async (
                Guid organizationId,
                Guid companyId,
                PeopleSliceQueryService queries,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<TimelineEntryModel>? timeline = await queries
                    .GetCompanyTimelineAsync(
                        new OrganizationId(organizationId),
                        new CompanyId(companyId),
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return timeline is null ? Results.NotFound() : Results.Ok(timeline.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.InteractionsRead))
            .WithName("GetCompanyTimeline");
    }

    // --------------------------------------------------------- relationships

    private static void MapRelationships(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/relationships", async (
                Guid organizationId,
                CreateRelationshipRequest request,
                CreateRelationshipHandler handler,
                CancellationToken cancellationToken) =>
            {
                RelationshipId relationshipId = await handler.HandleAsync(
                    new CreateRelationshipCommand(
                        new OrganizationId(organizationId),
                        EndpointParsing.ToEndpoint(request.From, nameof(request.From)),
                        EndpointParsing.ToEndpoint(request.To, nameof(request.To)),
                        EndpointParsing.ParseEnum<RelationshipType>(request.Type, nameof(request.Type)),
                        EndpointParsing.ParseNullableEnum<RelationshipDirection>(
                            request.Direction,
                            nameof(request.Direction)),
                        request.Strength,
                        request.StartedAt,
                        request.Notes),
                    cancellationToken).ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/relationships/{relationshipId.Value}",
                    new { id = relationshipId.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RelationshipsWrite))
            .WithName("CreateRelationship");

        tenant.MapPost("/relationships/{relationshipId:guid}/end", async (
                Guid organizationId,
                Guid relationshipId,
                EndRelationshipRequest? request,
                EndRelationshipHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                    new EndRelationshipCommand(
                        new OrganizationId(organizationId),
                        new RelationshipId(relationshipId),
                        request?.EndedAt),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RelationshipsWrite))
            .WithName("EndRelationship");
    }

    // ---------------------------------------------------------- interactions

    private static void MapInteractions(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/interactions", async (
                Guid organizationId,
                RecordInteractionRequest request,
                RecordInteractionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                RecordInteractionResult result = await handler.HandleAsync(
                    new RecordInteractionCommand(
                        new OrganizationId(organizationId),
                        EndpointParsing.ParseEnum<InteractionType>(request.Type, nameof(request.Type)),
                        request.OccurredAt,
                        request.Summary,
                        [
                            .. (request.Participants ?? []).Select(p => new InteractionParticipantInput(
                                EndpointParsing.ToEndpoint(p.Party, "Participant"),
                                p.Role)),
                        ],
                        request.DetailedNotes,
                        request.FollowUp is null
                            ? null
                            : new FollowUpTaskInput(
                                request.FollowUp.Title,
                                request.FollowUp.DueAt,
                                EndpointParsing.ParseEnumOrDefault(
                                    request.FollowUp.Priority,
                                    "FollowUp.Priority",
                                    TaskPriority.Normal),
                                EndpointParsing.ToEndpointOrNull(request.FollowUp.Subject, "FollowUp.Subject"),
                                request.FollowUp.Notes)),
                    cancellationToken).ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/interactions/{result.InteractionId.Value}",
                    new RecordInteractionResponse(
                        result.InteractionId.Value,
                        result.FollowUpTaskId?.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.InteractionsRecord))
            .WithName("RecordInteraction");
    }

    // ----------------------------------------------------------------- tasks

    private static void MapTasks(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/tasks", async (
                Guid organizationId,
                PeopleSliceQueryService queries,
                bool? openOnly,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<TaskModel> tasks = await queries
                    .ListTasksAsync(new OrganizationId(organizationId), openOnly ?? true, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(tasks.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TasksRead))
            .WithName("ListTasks");

        tenant.MapPost("/tasks", async (
                Guid organizationId,
                CreateTaskRequest request,
                CreateTaskHandler handler,
                CancellationToken cancellationToken) =>
            {
                TaskItemId taskId = await handler.HandleAsync(
                    new CreateTaskCommand(
                        new OrganizationId(organizationId),
                        request.Title,
                        EndpointParsing.ParseEnumOrDefault(request.Priority, nameof(request.Priority), TaskPriority.Normal),
                        request.DueAt,
                        EndpointParsing.ToEndpointOrNull(request.Subject, nameof(request.Subject)),
                        request.Notes,
                        request.AssigneeUserId is { } assignee ? new UserId(assignee) : null),
                    cancellationToken).ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/tasks/{taskId.Value}",
                    new { id = taskId.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TasksWrite))
            .WithName("CreateTask");

        tenant.MapPost("/tasks/{taskId:guid}/assignee", async (
                Guid organizationId,
                Guid taskId,
                AssignTaskRequest request,
                AssignTaskHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                // A null assignee clears the assignment rather than failing. Work
                // that nobody owns is a real state, and saying so is better than
                // leaving somebody's name on something they handed back.
                await handler.HandleAsync(
                    new AssignTaskCommand(
                        new OrganizationId(organizationId),
                        new TaskItemId(taskId),
                        request.AssigneeUserId is { } assignee ? new UserId(assignee) : null,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TasksWrite))
            .WithName("AssignTask");

        tenant.MapPost("/tasks/{taskId:guid}/complete", async (
                Guid organizationId,
                Guid taskId,
                TaskTransitionRequest request,
                CompleteTaskHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new CompleteTaskCommand(
                        new OrganizationId(organizationId),
                        new TaskItemId(taskId),
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TasksWrite))
            .WithName("CompleteTask");

        tenant.MapPost("/tasks/{taskId:guid}/reopen", async (
                Guid organizationId,
                Guid taskId,
                TaskTransitionRequest request,
                ReopenTaskHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new ReopenTaskCommand(
                        new OrganizationId(organizationId),
                        new TaskItemId(taskId),
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TasksWrite))
            .WithName("ReopenTask");
    }

    // -------------------------------------------------------- command centre

    private static void MapCommandCenter(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/command-center", async (
                Guid organizationId,
                PeopleSliceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                CommandCenterModel model = await queries
                    .GetCommandCenterAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new CommandCenterResponse(
                    [.. model.Overdue.Select(Map)],
                    [.. model.DueSoon.Select(Map)],
                    [.. model.Unscheduled.Select(Map)],
                    [.. model.RecentInteractions.Select(Map)],
                    model.OpenTaskCount,
                    model.PeopleCount,
                    model.CompanyCount));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TasksRead))
            .WithName("GetCommandCenter");
    }

    // --------------------------------------------------------------- mapping

    private static PersonDetails ToDetails(
        string firstName,
        string? lastName,
        string? displayName,
        string? middleName,
        string? preferredName,
        Guid? primaryCompanyId,
        string? title,
        string? email,
        string? phone,
        string? notes) => new(
            firstName,
            lastName,
            displayName,
            middleName,
            preferredName,
            primaryCompanyId.HasValue ? new CompanyId(primaryCompanyId.Value) : null,
            title,
            email,
            phone,
            notes);

    private static PersonSummaryResponse Map(PersonSummaryModel model) => new(
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

    private static PersonDetailResponse Map(PersonDetailModel model) => new(
        Map(model.Summary),
        model.FirstName,
        model.MiddleName,
        model.LastName,
        model.PreferredName,
        model.Notes,
        model.CreatedAt,
        [.. model.Relationships.Select(Map)]);

    private static CompanySummaryResponse Map(CompanySummaryModel model) => new(
        model.Id,
        model.Name,
        model.LegalName,
        model.Type,
        model.Status,
        model.Website,
        model.UpdatedAt,
        model.Version);

    private static CompanyDetailResponse Map(CompanyDetailModel model) => new(
        Map(model.Summary),
        model.Notes,
        model.CreatedAt,
        [.. model.Relationships.Select(Map)],
        [.. model.People.Select(Map)]);

    private static PartyReferenceResponse Map(PartyReference reference) =>
        new(reference.Kind, reference.Id, reference.Name);

    private static RelationshipResponse Map(RelationshipModel model) => new(
        model.Id,
        Map(model.From),
        Map(model.To),
        model.Type,
        model.Direction,
        model.Status,
        model.Strength,
        model.StartedAt,
        model.EndedAt,
        model.Notes,
        model.Version);

    private static TaskResponse Map(TaskModel model) => new(
        model.Id,
        model.Title,
        model.State,
        model.Priority,
        model.DueAt,
        model.Subject is null ? null : Map(model.Subject),
        model.SourceInteractionId,
        model.CreatedAt,
        model.CompletedAt,
        model.Version,
        model.AssigneeUserId,
        model.AssigneeDisplayName);

    private static InteractionResponse Map(InteractionModel model) => new(
        model.Id,
        model.Type,
        model.OccurredAt,
        model.Summary,
        model.DetailedNotes,
        [.. model.Participants.Select(Map)]);

    private static TimelineEntryResponse Map(TimelineEntryModel model) =>
        new(model.OccurredAt, model.Kind, model.Title, model.Detail, model.EntityId);
}
