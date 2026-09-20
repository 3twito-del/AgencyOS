using System.Diagnostics;
using AgencyOS.Api.Authorization;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Directory;
using AgencyOS.Application.Representations;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Representation;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M4 representation surface, routed under the tenant that owns the records.
/// </summary>
/// <remarks>
/// Endpoint policies are an early gate only; the authoritative, tenant-scoped
/// check and the redaction of internal notes happen inside the services, so an
/// endpoint added later cannot forget either (ADR-0007, ADR-0017).
/// </remarks>
internal static class M4Endpoints
{
    public static void MapRepresentation(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}");

        MapTalent(tenant);
        MapProspects(tenant);
        MapRepresentations(tenant);
        MapCreditsAndMaterials(tenant);
    }

    // ---------------------------------------------------------------- talent

    private static void MapTalent(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/talent", async (
                Guid organizationId,
                RepresentationQueryService queries,
                bool? clientsOnly,
                bool? formerClientsOnly,
                string? discipline,
                string? scope,
                Guid? leadUserId,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                TalentFilter filter = new(
                    clientsOnly ?? false,
                    formerClientsOnly ?? false,
                    EndpointParsing.ParseNullableEnum<ProfessionalDiscipline>(discipline, nameof(discipline)),
                    EndpointParsing.ParseNullableEnum<RepresentationScopeArea>(scope, nameof(scope)),
                    leadUserId,
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<TalentSummaryModel> talent = await queries
                    .ListTalentAsync(new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(talent.Select(MapTalentSummary).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentRead))
            .WithName("ListTalent");

        tenant.MapGet("/talent/{personId:guid}", async (
                Guid organizationId,
                Guid personId,
                RepresentationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                TalentDetailModel? talent = await queries
                    .GetTalentAsync(new OrganizationId(organizationId), new PersonId(personId), cancellationToken)
                    .ConfigureAwait(false);

                return talent is null ? Results.NotFound() : Results.Ok(Map(talent));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentRead))
            .WithName("GetTalentProfile");

        tenant.MapPost("/talent", async (
                Guid organizationId,
                CreateTalentProfileRequest request,
                CreateTalentProfileHandler handler,
                RepresentationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new CreateTalentProfileCommand(
                        new OrganizationId(organizationId),
                        new PersonId(request.PersonId),
                        EndpointParsing.ParseEnumOrDefault(
                            request.CareerStage,
                            nameof(request.CareerStage),
                            CareerStage.Unknown),
                        request.Summary,
                        request.PositioningNotes,
                        request.BaseMarket,
                        request.Languages,
                        ParseDisciplines(request.Disciplines)),
                    cancellationToken).ConfigureAwait(false);

                TalentDetailModel created = (await queries
                    .GetTalentAsync(
                        new OrganizationId(organizationId),
                        new PersonId(request.PersonId),
                        cancellationToken)
                    .ConfigureAwait(false))!;

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/talent/{request.PersonId}",
                    Map(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentWrite))
            .WithName("CreateTalentProfile");

        // Reads under /talent are keyed by person, because that is how the agency
        // asks the question: "show me Ada Reyes". The profile itself is a separate
        // resource, keyed by its own identifier, because that is what an update
        // targets and what carries the version. Keeping both under /talent would
        // produce two OpenAPI paths that differ only in the name of a template
        // parameter, which OpenAPI 3.1 forbids and no generated client could
        // sensibly represent.
        tenant.MapPut("/talent-profiles/{talentProfileId:guid}", async (
                Guid organizationId,
                Guid talentProfileId,
                UpdateTalentProfileRequest request,
                UpdateTalentProfileHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new UpdateTalentProfileCommand(
                        new OrganizationId(organizationId),
                        new TalentProfileId(talentProfileId),
                        EndpointParsing.ParseEnumOrDefault(
                            request.CareerStage,
                            nameof(request.CareerStage),
                            CareerStage.Unknown),
                        request.Summary,
                        request.PositioningNotes,
                        request.BaseMarket,
                        request.Languages,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentWrite))
            .WithName("UpdateTalentProfile");

        tenant.MapPost("/talent-profiles/{talentProfileId:guid}/disciplines", async (
                Guid organizationId,
                Guid talentProfileId,
                ChangeTalentDisciplineRequest request,
                ChangeTalentDisciplineHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(ToDisciplineCommand(organizationId, talentProfileId, request), add: true, cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentWrite))
            .WithName("AddTalentDiscipline");

        tenant.MapPost("/talent-profiles/{talentProfileId:guid}/disciplines/end", async (
                Guid organizationId,
                Guid talentProfileId,
                ChangeTalentDisciplineRequest request,
                ChangeTalentDisciplineHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(ToDisciplineCommand(organizationId, talentProfileId, request), add: false, cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentWrite))
            .WithName("EndTalentDiscipline");

        tenant.MapGet("/talent/{personId:guid}/overview", async (
                Guid organizationId,
                Guid personId,
                RepresentationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity = AgencyOsTelemetry.Source.StartActivity("agencyos.client.overview");

                ClientOverviewModel? overview = await queries
                    .GetClientOverviewAsync(
                        new OrganizationId(organizationId),
                        new PersonId(personId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (overview is null)
                {
                    return Results.NotFound();
                }

                activity?.SetTag("agencyos.client.is_client", overview.Talent.Summary.IsClient);

                return Results.Ok(Map(overview));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentRead))
            .WithName("GetClientOverview");

        tenant.MapGet("/talent/{personId:guid}/history", async (
                Guid organizationId,
                Guid personId,
                RepresentationQueryService queries,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<RepresentationHistoryEntryModel> history = await queries
                    .GetRepresentationHistoryAsync(
                        new OrganizationId(organizationId),
                        new PersonId(personId),
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(history.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RepresentationRead))
            .WithName("GetRepresentationHistory");
    }

    // -------------------------------------------------------------- prospects

    private static void MapProspects(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/prospects", async (
                Guid organizationId,
                RepresentationQueryService queries,
                bool? openOnly,
                string? stage,
                Guid? ownerUserId,
                DateOnly? dueOnOrBefore,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                ProspectFilter filter = new(
                    openOnly ?? true,
                    EndpointParsing.ParseNullableEnum<ProspectStage>(stage, nameof(stage)),
                    ownerUserId,
                    dueOnOrBefore);

                IReadOnlyList<ProspectModel> prospects = await queries
                    .ListProspectsAsync(new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(prospects.Select(MapProspect).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProspectsRead))
            .WithName("ListProspects");

        tenant.MapGet("/prospects/{prospectId:guid}", async (
                Guid organizationId,
                Guid prospectId,
                RepresentationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ProspectModel? prospect = await queries
                    .GetProspectAsync(
                        new OrganizationId(organizationId),
                        new ProspectId(prospectId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return prospect is null ? Results.NotFound() : Results.Ok(MapProspect(prospect));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProspectsRead))
            .WithName("GetProspect");

        tenant.MapPost("/prospects", async (
                Guid organizationId,
                CreateProspectRequest request,
                CreateProspectHandler handler,
                RepresentationQueryService queries,
                IClockAccessor clock,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ProspectId id = await handler.HandleAsync(
                    new CreateProspectCommand(
                        new OrganizationId(organizationId),
                        new PersonId(request.PersonId),
                        new UserId(request.OwnerUserId),
                        request.IdentifiedOn ?? clock.Today,
                        request.Source,
                        request.StrategyNotes,
                        request.NextFollowUpOn),
                    cancellationToken).ConfigureAwait(false);

                ProspectModel created = (await queries
                    .GetProspectAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false))!;

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/prospects/{id.Value}",
                    MapProspect(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProspectsWrite))
            .WithName("CreateProspect");

        tenant.MapPost("/prospects/{prospectId:guid}/advance", async (
                Guid organizationId,
                Guid prospectId,
                AdvanceProspectRequest request,
                AdvanceProspectHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new AdvanceProspectCommand(
                        new OrganizationId(organizationId),
                        new ProspectId(prospectId),
                        EndpointParsing.ParseEnum<ProspectStage>(request.Stage, nameof(request.Stage)),
                        request.OccurredOn,
                        request.Reason,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ProspectsWrite))
            .WithName("AdvanceProspect");

        tenant.MapPost("/prospects/{prospectId:guid}/convert", async (
                Guid organizationId,
                Guid prospectId,
                ConvertProspectRequest request,
                ConvertProspectHandler handler,
                RepresentationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity = AgencyOsTelemetry.Source.StartActivity("agencyos.prospect.convert");

                RepresentationId id = await handler.HandleAsync(
                    new ConvertProspectCommand(
                        new OrganizationId(organizationId),
                        new ProspectId(prospectId),
                        request.StartsOn,
                        new UserId(request.LeadUserId),
                        ParseScopes(request.Scopes),
                        request.IsExclusive,
                        request.Territory,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                activity?.SetTag("agencyos.representation.id", id.Value);

                AgencyOsTelemetry.ProspectConversions.Add(1);

                RepresentationModel created = (await queries
                    .GetRepresentationAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false))!;

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/representations/{id.Value}",
                    Map(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RepresentationWrite))
            .WithName("ConvertProspect");
    }

    // --------------------------------------------------------- representations

    private static void MapRepresentations(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/representations/{representationId:guid}", async (
                Guid organizationId,
                Guid representationId,
                RepresentationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                RepresentationModel? representation = await queries
                    .GetRepresentationAsync(
                        new OrganizationId(organizationId),
                        new RepresentationId(representationId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return representation is null ? Results.NotFound() : Results.Ok(Map(representation));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RepresentationRead))
            .WithName("GetRepresentation");

        tenant.MapPost("/representations", async (
                Guid organizationId,
                CreateRepresentationRequest request,
                CreateRepresentationHandler handler,
                RepresentationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                RepresentationId id = await handler.HandleAsync(
                    new CreateRepresentationCommand(
                        new OrganizationId(organizationId),
                        new PersonId(request.PersonId),
                        request.StartsOn,
                        new UserId(request.LeadUserId),
                        ParseScopes(request.Scopes),
                        request.EndsOn,
                        request.IsExclusive,
                        request.Territory,
                        request.Notes),
                    cancellationToken).ConfigureAwait(false);

                RepresentationModel created = (await queries
                    .GetRepresentationAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false))!;

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/representations/{id.Value}",
                    Map(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RepresentationWrite))
            .WithName("CreateRepresentation");

        tenant.MapPost("/representations/{representationId:guid}/transition", async (
                Guid organizationId,
                Guid representationId,
                TransitionRepresentationRequest request,
                TransitionRepresentationHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.representation.transition");

                RepresentationStatus target =
                    EndpointParsing.ParseEnum<RepresentationStatus>(request.Status, nameof(request.Status));

                await handler.HandleAsync(
                    new TransitionRepresentationCommand(
                        new OrganizationId(organizationId),
                        new RepresentationId(representationId),
                        target,
                        request.OccurredOn,
                        request.Reason,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                activity?.SetTag("agencyos.representation.status", target.ToString());

                AgencyOsTelemetry.RepresentationTransitions.Add(
                    1,
                    new KeyValuePair<string, object?>("status", target.ToString()));

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RepresentationWrite))
            .WithName("TransitionRepresentation");

        tenant.MapPost("/representations/{representationId:guid}/scopes", async (
                Guid organizationId,
                Guid representationId,
                ChangeRepresentationScopeRequest request,
                ChangeRepresentationScopeHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(ToScopeCommand(organizationId, representationId, request), add: true, cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RepresentationWrite))
            .WithName("AddRepresentationScope");

        tenant.MapPost("/representations/{representationId:guid}/scopes/end", async (
                Guid organizationId,
                Guid representationId,
                ChangeRepresentationScopeRequest request,
                ChangeRepresentationScopeHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(ToScopeCommand(organizationId, representationId, request), add: false, cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RepresentationWrite))
            .WithName("EndRepresentationScope");

        tenant.MapPost("/representations/{representationId:guid}/team", async (
                Guid organizationId,
                Guid representationId,
                AssignRepresentationTeamMemberRequest request,
                AssignRepresentationTeamMemberHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new AssignRepresentationTeamMemberCommand(
                        new OrganizationId(organizationId),
                        new RepresentationId(representationId),
                        new UserId(request.UserId),
                        EndpointParsing.ParseEnum<RepresentationTeamRole>(request.Role, nameof(request.Role)),
                        request.OccurredOn,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RepresentationWrite))
            .WithName("AssignRepresentationTeamMember");

        tenant.MapPost("/representations/{representationId:guid}/team/remove", async (
                Guid organizationId,
                Guid representationId,
                RemoveRepresentationTeamMemberRequest request,
                RemoveRepresentationTeamMemberHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new RemoveRepresentationTeamMemberCommand(
                        new OrganizationId(organizationId),
                        new RepresentationId(representationId),
                        new UserId(request.UserId),
                        request.OccurredOn,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RepresentationWrite))
            .WithName("RemoveRepresentationTeamMember");
    }

    // ------------------------------------------------------ credits, materials

    private static void MapCreditsAndMaterials(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/talent/{personId:guid}/credits", async (
                Guid organizationId,
                Guid personId,
                RepresentationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<CreditModel> credits = await queries
                    .ListCreditsAsync(new OrganizationId(organizationId), new PersonId(personId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(credits.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentRead))
            .WithName("ListCredits");

        tenant.MapPost("/credits", async (
                Guid organizationId,
                AddCreditRequest request,
                AddCreditHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                CreditId id = await handler.HandleAsync(
                    new AddCreditCommand(
                        new OrganizationId(organizationId),
                        new PersonId(request.PersonId),
                        request.Title,
                        EndpointParsing.ParseEnum<CreditType>(request.Type, nameof(request.Type)),
                        EndpointParsing.ParseEnumOrDefault(
                            request.Status,
                            nameof(request.Status),
                            CreditStatus.Released),
                        request.Role,
                        request.Year,
                        request.CompanyId.HasValue ? new CompanyId(request.CompanyId.Value) : null,
                        request.Source,
                        request.Notes),
                    cancellationToken).ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/credits/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentWrite))
            .WithName("AddCredit");

        tenant.MapPut("/credits/{creditId:guid}", async (
                Guid organizationId,
                Guid creditId,
                UpdateCreditRequest request,
                UpdateCreditHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new UpdateCreditCommand(
                        new OrganizationId(organizationId),
                        new CreditId(creditId),
                        request.Title,
                        EndpointParsing.ParseEnum<CreditType>(request.Type, nameof(request.Type)),
                        EndpointParsing.ParseEnumOrDefault(
                            request.Status,
                            nameof(request.Status),
                            CreditStatus.Released),
                        request.Role,
                        request.Year,
                        request.CompanyId.HasValue ? new CompanyId(request.CompanyId.Value) : null,
                        request.Source,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentWrite))
            .WithName("UpdateCredit");

        tenant.MapGet("/talent/{personId:guid}/materials", async (
                Guid organizationId,
                Guid personId,
                RepresentationQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<MaterialModel> materials = await queries
                    .ListMaterialsAsync(new OrganizationId(organizationId), new PersonId(personId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(materials.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentRead))
            .WithName("ListMaterials");

        tenant.MapPost("/materials", async (
                Guid organizationId,
                AddMaterialRequest request,
                AddMaterialHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                MaterialId id = await handler.HandleAsync(
                    new AddMaterialCommand(
                        new OrganizationId(organizationId),
                        new PersonId(request.PersonId),
                        request.Title,
                        EndpointParsing.ParseEnum<MaterialType>(request.Type, nameof(request.Type)),
                        EndpointParsing.ParseEnumOrDefault(
                            request.Status,
                            nameof(request.Status),
                            MaterialStatus.Draft),
                        request.VersionLabel,
                        request.ExternalUri,
                        request.ReceivedOn,
                        request.Source,
                        request.Notes),
                    cancellationToken).ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/materials/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentWrite))
            .WithName("AddMaterial");

        tenant.MapPut("/materials/{materialId:guid}", async (
                Guid organizationId,
                Guid materialId,
                UpdateMaterialRequest request,
                UpdateMaterialHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new UpdateMaterialCommand(
                        new OrganizationId(organizationId),
                        new MaterialId(materialId),
                        request.Title,
                        EndpointParsing.ParseEnum<MaterialType>(request.Type, nameof(request.Type)),
                        EndpointParsing.ParseEnumOrDefault(
                            request.Status,
                            nameof(request.Status),
                            MaterialStatus.Draft),
                        request.VersionLabel,
                        request.ExternalUri,
                        request.ReceivedOn,
                        request.Source,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.TalentWrite))
            .WithName("UpdateMaterial");
    }

    // --------------------------------------------------------------- parsing

    private static ChangeTalentDisciplineCommand ToDisciplineCommand(
        Guid organizationId,
        Guid talentProfileId,
        ChangeTalentDisciplineRequest request) =>
        new(
            new OrganizationId(organizationId),
            new TalentProfileId(talentProfileId),
            EndpointParsing.ParseEnum<ProfessionalDiscipline>(request.Discipline, nameof(request.Discipline)),
            request.ExpectedVersion);

    private static ChangeRepresentationScopeCommand ToScopeCommand(
        Guid organizationId,
        Guid representationId,
        ChangeRepresentationScopeRequest request) =>
        new(
            new OrganizationId(organizationId),
            new RepresentationId(representationId),
            EndpointParsing.ParseEnum<RepresentationScopeArea>(request.Area, nameof(request.Area)),
            request.OccurredOn,
            request.ExpectedVersion);

    private static IReadOnlyList<ProfessionalDiscipline> ParseDisciplines(IReadOnlyList<string>? disciplines) =>
    [
        .. (disciplines ?? []).Select(x =>
            EndpointParsing.ParseEnum<ProfessionalDiscipline>(x, "Disciplines")),
    ];

    private static IReadOnlyList<RepresentationScopeArea> ParseScopes(IReadOnlyList<string>? scopes) =>
    [
        .. (scopes ?? []).Select(x => EndpointParsing.ParseEnum<RepresentationScopeArea>(x, "Scopes")),
    ];

    // --------------------------------------------------------------- mapping

    /// <summary>Shared with the saved-view endpoint, so one shape is produced once.</summary>
    internal static TalentSummaryResponse MapTalentSummary(TalentSummaryModel model) => new(
        model.Id,
        model.PersonId,
        model.DisplayName,
        model.CareerStage,
        model.Disciplines,
        model.RepresentationStatus,
        model.IsClient,
        model.LeadUserId,
        model.LeadDisplayName,
        model.Scopes,
        model.UpdatedAt,
        model.Version);

    private static TalentDetailResponse Map(TalentDetailModel model) => new(
        MapTalentSummary(model.Summary),
        model.ProfileSummary,
        model.PositioningNotes,
        model.BaseMarket,
        model.Languages,
        model.CreatedAt);

    private static RepresentationResponse Map(RepresentationModel model) => new(
        model.Id,
        model.PersonId,
        model.DisplayName,
        model.Status,
        model.StartsOn,
        model.EndsOn,
        model.IsExclusive,
        model.Territory,
        model.Notes,
        [.. model.Scopes.Select(x => new RepresentationScopeResponse(x.Area, x.StartsOn, x.EndsOn))],
        [
            .. model.Team.Select(x => new RepresentationTeamMemberResponse(
                x.UserId,
                x.DisplayName,
                x.Role,
                x.StartsOn,
                x.EndsOn)),
        ],
        model.UpdatedAt,
        model.Version);

    internal static ProspectResponse MapProspect(ProspectModel model) => new(
        model.Id,
        model.PersonId,
        model.DisplayName,
        model.Stage,
        model.OwnerUserId,
        model.OwnerDisplayName,
        model.Source,
        model.StrategyNotes,
        model.IdentifiedOn,
        model.NextFollowUpOn,
        model.ConvertedToRepresentationId,
        model.UpdatedAt,
        model.Version);

    private static CreditResponse Map(CreditModel model) => new(
        model.Id,
        model.PersonId,
        model.Title,
        model.Role,
        model.Type,
        model.Status,
        model.Year,
        model.CompanyId,
        model.CompanyName,
        model.Source,
        model.Notes,
        model.ProjectId,
        model.Version);

    private static MaterialResponse Map(MaterialModel model) => new(
        model.Id,
        model.PersonId,
        model.Title,
        model.Type,
        model.Status,
        model.VersionLabel,
        model.ExternalUri,
        model.ReceivedOn,
        model.Source,
        model.Notes,
        model.Version);

    private static RepresentationHistoryEntryResponse Map(RepresentationHistoryEntryModel model) =>
        new(model.OccurredOn, model.Kind, model.Title, model.Detail);

    private static ClientOverviewResponse Map(ClientOverviewModel model) => new(
        Map(model.Talent),
        model.Representation is null ? null : Map(model.Representation),
        [.. model.OpenTasks.Select(MapTask)],
        [.. model.RecentInteractions.Select(MapInteraction)],
        [.. model.Credits.Select(Map)],
        [.. model.Materials.Select(Map)],
        [.. model.RecentHistory.Select(Map)]);

    private static TaskResponse MapTask(TaskModel model) => new(
        model.Id,
        model.Title,
        model.State,
        model.Priority,
        model.DueAt,
        model.Subject is null
            ? null
            : new PartyReferenceResponse(model.Subject.Kind, model.Subject.Id, model.Subject.Name),
        model.SourceInteractionId,
        model.CreatedAt,
        model.CompletedAt,
        model.Version,
        model.AssigneeUserId,
        model.AssigneeDisplayName);

    private static InteractionResponse MapInteraction(InteractionModel model) => new(
        model.Id,
        model.Type,
        model.OccurredAt,
        model.Summary,
        model.DetailedNotes,
        [.. model.Participants.Select(p => new PartyReferenceResponse(p.Kind, p.Id, p.Name))]);
}

/// <summary>Supplies today's date to endpoints that default one.</summary>
/// <remarks>
/// A seam rather than <c>DateOnly.FromDateTime(DateTime.UtcNow)</c> inline, so a
/// test can pin the date and assert on it. Kept tiny deliberately.
/// </remarks>
public interface IClockAccessor
{
    DateOnly Today { get; }
}
