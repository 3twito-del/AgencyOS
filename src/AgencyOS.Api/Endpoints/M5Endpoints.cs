using System.Diagnostics;
using AgencyOS.Api.Authorization;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Projects;
using AgencyOS.Contracts.Projects;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M5 project and packaging surface, routed under the tenant that owns the records.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint policies are an early gate only; the authoritative tenant-scoped check
/// and the redaction of package strategy happen inside the services, so an
/// endpoint added later cannot forget either (ADR-0007, ADR-0019).
/// </para>
/// <para>
/// Every path parameter names exactly one kind of identifier. M4 shipped
/// <c>/talent/{personId}</c> and <c>/talent/{talentProfileId}</c>, which route
/// correctly and are the same path in OpenAPI 3.1; the contract test that caught
/// it still runs, and this surface is laid out to keep passing it.
/// </para>
/// </remarks>
internal static class M5Endpoints
{
    public static void MapProjects(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}");

        MapProjectRecords(tenant);
        MapRoles(tenant);
        MapAttachments(tenant);
        MapCompanies(tenant);
        MapSourceProperties(tenant);
        MapLinks(tenant);
        MapPackages(tenant);
    }

    // -------------------------------------------------------------- projects

    private static void MapProjectRecords(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/projects", async (
                Guid organizationId,
                ProjectQueryService queries,
                string? status,
                string? stage,
                string? type,
                Guid? leadUserId,
                Guid? attachedPersonId,
                string? missingRole,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                ProjectFilter filter = new(
                    EndpointParsing.ParseNullableEnum<ProjectStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<DevelopmentStage>(stage, nameof(stage)),
                    EndpointParsing.ParseNullableEnum<ProjectType>(type, nameof(type)),
                    leadUserId,
                    attachedPersonId,
                    EndpointParsing.ParseNullableEnum<ProjectRoleType>(missingRole, nameof(missingRole)),
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<ProjectSummaryModel> projects = await queries
                    .ListProjectsAsync(new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(projects.Select(MapProjectSummary).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsRead))
            .WithName("ListProjects");

        tenant.MapGet("/projects/{projectId:guid}", async (
                Guid organizationId,
                Guid projectId,
                ProjectQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity = AgencyOsTelemetry.Source.StartActivity("agencyos.project.detail");

                ProjectDetailModel? project = await queries
                    .GetProjectAsync(new OrganizationId(organizationId), new ProjectId(projectId), cancellationToken)
                    .ConfigureAwait(false);

                return project is null ? Results.NotFound() : Results.Ok(Map(project));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsRead))
            .WithName("GetProject");

        tenant.MapGet("/projects/{projectId:guid}/history", async (
                Guid organizationId,
                Guid projectId,
                ProjectQueryService queries,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<ProjectHistoryEntryModel> history = await queries
                    .GetProjectHistoryAsync(
                        new OrganizationId(organizationId), new ProjectId(projectId), limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(history.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsRead))
            .WithName("GetProjectHistory");

        tenant.MapGet("/projects/{projectId:guid}/attachments", async (
                Guid organizationId,
                Guid projectId,
                ProjectQueryService queries,
                bool? currentOnly,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<AttachmentModel> attachments = await queries
                    .ListAttachmentsAsync(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        currentOnly ?? false,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(attachments.Select(MapAttachment).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsRead))
            .WithName("ListProjectAttachments");

        tenant.MapGet("/project-command-center", async (
                Guid organizationId,
                ProjectQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ProjectCommandCenterModel model = await queries
                    .GetProjectCommandCenterAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new ProjectCommandCenterResponse(
                    [.. model.RecentlyChanged.Select(MapProjectSummary)],
                    [.. model.Packages.Select(MapPackageSummary)],
                    [.. model.RecentAttachments.Select(MapAttachment)],
                    model.ActiveProjectCount,
                    model.PackagesInProgressCount));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsRead))
            .WithName("GetProjectCommandCenter");

        tenant.MapPost("/projects", async (
                Guid organizationId,
                CreateProjectRequest request,
                CreateProjectHandler handler,
                ProjectQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ProjectId id = await handler.HandleAsync(
                    new CreateProjectCommand(
                        new OrganizationId(organizationId),
                        request.Title,
                        EndpointParsing.ParseEnum<ProjectType>(request.Type, nameof(request.Type)),
                        request.WorkingTitle,
                        EndpointParsing.ParseEnumOrDefault(
                            request.Stage, nameof(request.Stage), DevelopmentStage.Concept),
                        request.Logline,
                        request.Synopsis,
                        request.PrimaryCompanyId is { } company ? new CompanyId(company) : null,
                        request.Year,
                        request.LeadUserId is { } lead ? new UserId(lead) : null,
                        request.Notes),
                    cancellationToken).ConfigureAwait(false);

                ProjectDetailModel created = (await queries
                    .GetProjectAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false))!;

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/projects/{id.Value}",
                    Map(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("CreateProject");

        tenant.MapPut("/projects/{projectId:guid}", async (
                Guid organizationId,
                Guid projectId,
                UpdateProjectRequest request,
                UpdateProjectHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new UpdateProjectCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        request.Title,
                        EndpointParsing.ParseEnum<ProjectType>(request.Type, nameof(request.Type)),
                        request.WorkingTitle,
                        request.Logline,
                        request.Synopsis,
                        request.PrimaryCompanyId is { } company ? new CompanyId(company) : null,
                        request.Year,
                        request.LeadUserId is { } lead ? new UserId(lead) : null,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("UpdateProject");

        // Status and stage are separate commands because they are separate facts.
        // A single PATCH taking arbitrary fields would let a caller move both at
        // once with one reason, and lose which one they meant (ADR-0018).
        tenant.MapPost("/projects/{projectId:guid}/status", async (
                Guid organizationId,
                Guid projectId,
                ChangeProjectStatusRequest request,
                ChangeProjectLifecycleHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.project.status");

                await handler.HandleAsync(
                    new ChangeProjectStatusCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        EndpointParsing.ParseEnum<ProjectStatus>(request.Status, nameof(request.Status)),
                        request.Reason,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.ProjectStatusChanges.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("ChangeProjectStatus");

        tenant.MapPost("/projects/{projectId:guid}/stage", async (
                Guid organizationId,
                Guid projectId,
                ChangeProjectStageRequest request,
                ChangeProjectLifecycleHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.project.stage");

                await handler.HandleAsync(
                    new ChangeProjectStageCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        EndpointParsing.ParseEnum<DevelopmentStage>(request.Stage, nameof(request.Stage)),
                        request.Reason,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.ProjectStageChanges.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("ChangeProjectStage");
    }

    // ----------------------------------------------------------------- roles

    private static void MapRoles(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/projects/{projectId:guid}/roles", async (
                Guid organizationId,
                Guid projectId,
                CreateProjectRoleRequest request,
                ProjectRoleHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ProjectRoleId id = await handler.HandleAsync(
                    new CreateProjectRoleCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        EndpointParsing.ParseEnum<ProjectRoleType>(request.Type, nameof(request.Type)),
                        request.Label,
                        request.IsExclusive,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/projects/{projectId}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("CreateProjectRole");

        tenant.MapPost("/projects/{projectId:guid}/roles/{roleId:guid}/change", async (
                Guid organizationId,
                Guid projectId,
                Guid roleId,
                ChangeProjectRoleRequest request,
                ProjectRoleHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new ChangeProjectRoleCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        new ProjectRoleId(roleId),
                        EndpointParsing.ParseEnum<ProjectRoleAction>(request.Action, nameof(request.Action)),
                        request.Label,
                        request.IsExclusive,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("ChangeProjectRole");
    }

    // ----------------------------------------------------------- attachments

    private static void MapAttachments(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/projects/{projectId:guid}/roles/{roleId:guid}/attachments", async (
                Guid organizationId,
                Guid projectId,
                Guid roleId,
                AttachToRoleRequest request,
                AttachmentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.project.attach");

                AttachmentId id = await handler.HandleAsync(
                    new AttachToProjectRoleCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        new ProjectRoleId(roleId),
                        request.PersonId is { } person ? new PersonId(person) : null,
                        request.CompanyId is { } company ? new CompanyId(company) : null,
                        EndpointParsing.ParseEnum<AttachmentStatus>(request.Status, nameof(request.Status)),
                        request.StartsOn,
                        request.EndsOn,
                        request.Source,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.AttachmentChanges.Add(1);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/projects/{projectId}/attachments",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("AttachToProjectRole");

        tenant.MapPost("/projects/{projectId:guid}/attachments/{attachmentId:guid}/status", async (
                Guid organizationId,
                Guid projectId,
                Guid attachmentId,
                ChangeAttachmentRequest request,
                AttachmentHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.project.attachment.status");

                await handler.HandleAsync(
                    new ChangeAttachmentStatusCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        new AttachmentId(attachmentId),
                        EndpointParsing.ParseEnum<AttachmentStatus>(request.Status, nameof(request.Status)),
                        request.OccurredOn,
                        request.Reason,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.AttachmentChanges.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("ChangeAttachmentStatus");
    }

    // ------------------------------------------------------------- companies

    private static void MapCompanies(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/projects/{projectId:guid}/companies", async (
                Guid organizationId,
                Guid projectId,
                AddProjectCompanyRequest request,
                ProjectCompanyHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                    new AddProjectCompanyCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        new CompanyId(request.CompanyId),
                        EndpointParsing.ParseEnum<ProjectCompanyCapacity>(
                            request.Capacity, nameof(request.Capacity)),
                        request.StartsOn,
                        request.EndsOn,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/projects/{projectId}",
                    new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("AddProjectCompany");

        tenant.MapPost("/projects/{projectId:guid}/companies/{participationId:guid}/end", async (
                Guid organizationId,
                Guid projectId,
                Guid participationId,
                EndProjectCompanyRequest request,
                ProjectCompanyHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new EndProjectCompanyCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        participationId,
                        request.EndsOn,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("EndProjectCompany");
    }

    // ------------------------------------------------------ source properties

    private static void MapSourceProperties(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/source-properties", async (
                Guid organizationId,
                ProjectQueryService queries,
                string? type,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                SourcePropertyFilter filter = new(
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                    EndpointParsing.ParseNullableEnum<SourcePropertyType>(type, nameof(type)));

                IReadOnlyList<SourcePropertyModel> properties = await queries
                    .ListSourcePropertiesAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(properties.Select(MapSourceProperty).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsRead))
            .WithName("ListSourceProperties");

        tenant.MapGet("/source-properties/{sourcePropertyId:guid}", async (
                Guid organizationId,
                Guid sourcePropertyId,
                ProjectQueryService queries,
                CancellationToken cancellationToken) =>
            {
                SourcePropertyModel? property = await queries
                    .GetSourcePropertyAsync(
                        new OrganizationId(organizationId),
                        new SourcePropertyId(sourcePropertyId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return property is null ? Results.NotFound() : Results.Ok(MapSourceProperty(property));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsRead))
            .WithName("GetSourceProperty");

        tenant.MapPost("/source-properties", async (
                Guid organizationId,
                CreateSourcePropertyRequest request,
                SourcePropertyHandler handler,
                ProjectQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                SourcePropertyId id = await handler.HandleAsync(
                    new CreateSourcePropertyCommand(
                        new OrganizationId(organizationId),
                        request.Title,
                        EndpointParsing.ParseEnum<SourcePropertyType>(request.Type, nameof(request.Type)),
                        request.AttributedCreator,
                        request.CreatorPersonId is { } creator ? new PersonId(creator) : null,
                        request.SourceReference,
                        request.Provenance,
                        request.Year,
                        request.Notes),
                    cancellationToken).ConfigureAwait(false);

                SourcePropertyModel created = (await queries
                    .GetSourcePropertyAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false))!;

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/source-properties/{id.Value}",
                    MapSourceProperty(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("CreateSourceProperty");

        tenant.MapPost("/projects/{projectId:guid}/source-properties", async (
                Guid organizationId,
                Guid projectId,
                LinkSourcePropertyRequest request,
                SourcePropertyHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new LinkSourcePropertyCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        new SourcePropertyId(request.SourcePropertyId),
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("LinkSourcePropertyToProject");

        tenant.MapPost("/projects/{projectId:guid}/source-properties/unlink", async (
                Guid organizationId,
                Guid projectId,
                LinkSourcePropertyRequest request,
                SourcePropertyHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new UnlinkSourcePropertyCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        new SourcePropertyId(request.SourcePropertyId),
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("UnlinkSourcePropertyFromProject");
    }

    // ----------------------------------------------------------------- links

    private static void MapLinks(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/projects/{projectId:guid}/materials", async (
                Guid organizationId,
                Guid projectId,
                LinkProjectMaterialRequest request,
                ProjectLinkHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new LinkMaterialToProjectCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        new MaterialId(request.MaterialId),
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("LinkMaterialToProject");

        tenant.MapPost("/projects/{projectId:guid}/materials/unlink", async (
                Guid organizationId,
                Guid projectId,
                LinkProjectMaterialRequest request,
                ProjectLinkHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new UnlinkMaterialFromProjectCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(projectId),
                        new MaterialId(request.MaterialId),
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("UnlinkMaterialFromProject");

        // Linking a credit is one command taking a nullable project, rather than a
        // link and an unlink: the fact being recorded is "this credit's project is
        // X", and null is a legitimate value for it.
        tenant.MapPost("/credits/{creditId:guid}/project", async (
                Guid organizationId,
                Guid creditId,
                LinkCreditToProjectRequest request,
                ProjectLinkHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new LinkCreditToProjectCommand(
                        new OrganizationId(organizationId),
                        new CreditId(creditId),
                        request.ProjectId is { } project ? new ProjectId(project) : null,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProjectsWrite))
            .WithName("LinkCreditToProject");
    }

    // -------------------------------------------------------------- packages

    private static void MapPackages(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/packages", async (
                Guid organizationId,
                ProjectQueryService queries,
                string? status,
                Guid? projectId,
                Guid? leadUserId,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                PackageFilter filter = new(
                    EndpointParsing.ParseNullableEnum<PackageStatus>(status, nameof(status)),
                    projectId,
                    leadUserId,
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<PackageSummaryModel> packages = await queries
                    .ListPackagesAsync(new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(packages.Select(MapPackageSummary).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PackagesRead))
            .WithName("ListPackages");

        tenant.MapGet("/packages/{packageId:guid}", async (
                Guid organizationId,
                Guid packageId,
                ProjectQueryService queries,
                CancellationToken cancellationToken) =>
            {
                PackageDetailModel? package = await queries
                    .GetPackageAsync(new OrganizationId(organizationId), new PackageId(packageId), cancellationToken)
                    .ConfigureAwait(false);

                return package is null ? Results.NotFound() : Results.Ok(Map(package));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PackagesRead))
            .WithName("GetPackage");

        tenant.MapPost("/packages", async (
                Guid organizationId,
                CreatePackageRequest request,
                PackageHandler handler,
                ProjectQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                PackageId id = await handler.HandleAsync(
                    new CreatePackageCommand(
                        new OrganizationId(organizationId),
                        new ProjectId(request.ProjectId),
                        request.Name,
                        new UserId(request.LeadUserId),
                        request.Thesis,
                        request.StrategyNotes),
                    cancellationToken).ConfigureAwait(false);

                PackageDetailModel created = (await queries
                    .GetPackageAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false))!;

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/packages/{id.Value}",
                    Map(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PackagesWrite))
            .WithName("CreatePackage");

        tenant.MapPut("/packages/{packageId:guid}", async (
                Guid organizationId,
                Guid packageId,
                UpdatePackageRequest request,
                PackageHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new UpdatePackageCommand(
                        new OrganizationId(organizationId),
                        new PackageId(packageId),
                        request.Name,
                        new UserId(request.LeadUserId),
                        request.Thesis,
                        request.StrategyNotes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PackagesWrite))
            .WithName("UpdatePackage");

        tenant.MapPost("/packages/{packageId:guid}/status", async (
                Guid organizationId,
                Guid packageId,
                ChangePackageStatusRequest request,
                PackageHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.package.status");

                await handler.HandleAsync(
                    new ChangePackageStatusCommand(
                        new OrganizationId(organizationId),
                        new PackageId(packageId),
                        EndpointParsing.ParseEnum<PackageStatus>(request.Status, nameof(request.Status)),
                        request.Reason,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.PackageStatusChanges.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PackagesWrite))
            .WithName("ChangePackageStatus");

        tenant.MapPost("/packages/{packageId:guid}/elements", async (
                Guid organizationId,
                Guid packageId,
                AddPackageElementRequest request,
                PackageHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                    new AddPackageElementCommand(
                        new OrganizationId(organizationId),
                        new PackageId(packageId),
                        EndpointParsing.ParseEnum<PackageElementKind>(request.Kind, nameof(request.Kind)),
                        request.TargetId,
                        request.Note,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/packages/{packageId}",
                    new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PackagesWrite))
            .WithName("AddPackageElement");

        tenant.MapPost("/packages/{packageId:guid}/elements/{elementId:guid}/remove", async (
                Guid organizationId,
                Guid packageId,
                Guid elementId,
                RemovePackageElementRequest request,
                PackageHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new RemovePackageElementCommand(
                        new OrganizationId(organizationId),
                        new PackageId(packageId),
                        elementId,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.PackagesWrite))
            .WithName("RemovePackageElement");
    }

    // --------------------------------------------------------------- mapping

    internal static ProjectSummaryResponse MapProjectSummary(ProjectSummaryModel model) => new(
        model.Id,
        model.Title,
        model.WorkingTitle,
        model.Type,
        model.Status,
        model.Stage,
        model.Year,
        model.PrimaryCompanyId,
        model.PrimaryCompanyName,
        model.LeadUserId,
        model.LeadDisplayName,
        model.OpenRoleCount,
        model.AttachedCount,
        model.PackageCount,
        model.UpdatedAt,
        model.Version);

    internal static PackageSummaryResponse MapPackageSummary(PackageSummaryModel model) => new(
        model.Id,
        model.ProjectId,
        model.ProjectTitle,
        model.Name,
        model.Status,
        model.LeadUserId,
        model.LeadDisplayName,
        model.ElementCount,
        model.OpenRoleCount,
        model.UpdatedAt,
        model.Version);

    internal static AttachmentResponse MapAttachment(AttachmentModel model) => new(
        model.Id,
        model.ProjectRoleId,
        model.RoleType,
        model.RoleLabel,
        model.PersonId,
        model.CompanyId,
        model.DisplayName,
        model.Status,
        model.StartsOn,
        model.EndsOn,
        model.HoldsTheRole,
        model.Source,
        model.Notes,
        model.UpdatedAt,
        model.Version);

    private static SourcePropertyResponse MapSourceProperty(SourcePropertyModel model) => new(
        model.Id,
        model.Title,
        model.Type,
        model.AttributedCreator,
        model.CreatorPersonId,
        model.SourceReference,
        model.Provenance,
        model.Year,
        model.Notes,
        model.ProjectCount,
        model.UpdatedAt,
        model.Version);

    private static ProjectRoleResponse MapRole(ProjectRoleModel model) => new(
        model.Id,
        model.Type,
        model.Label,
        model.Status,
        model.IsExclusive,
        model.Notes,
        [.. model.Attachments.Select(MapAttachment)]);

    private static ProjectHistoryEntryResponse Map(ProjectHistoryEntryModel model) => new(
        model.OccurredAt,
        model.Kind,
        model.Summary,
        model.Detail,
        model.ActorDisplayName);

    private static ProjectDetailResponse Map(ProjectDetailModel model) => new(
        MapProjectSummary(model.Summary),
        model.Logline,
        model.Synopsis,
        model.Notes,
        [.. model.Roles.Select(MapRole)],
        [
            .. model.Companies.Select(x => new ProjectCompanyResponse(
                x.Id, x.CompanyId, x.CompanyName, x.Capacity, x.StartsOn, x.EndsOn, x.Notes)),
        ],
        [.. model.SourceProperties.Select(MapSourceProperty)],
        [
            .. model.Materials.Select(x => new ProjectMaterialResponse(
                x.MaterialId, x.Title, x.Type, x.Status, x.PersonId, x.PersonName, x.Notes)),
        ],
        [.. model.Packages.Select(MapPackageSummary)],
        model.CreatedAt);

    private static PackageDetailResponse Map(PackageDetailModel model) => new(
        MapPackageSummary(model.Summary),
        model.Thesis,
        model.StrategyNotes,
        [
            .. model.Elements.Select(x => new PackageElementResponse(
                x.Id, x.Kind, x.TargetId, x.DisplayName, x.Detail, x.IsAttached, x.Note, x.Position)),
        ],
        [.. model.Gaps.Select(MapRole)],
        model.CreatedAt);
}
