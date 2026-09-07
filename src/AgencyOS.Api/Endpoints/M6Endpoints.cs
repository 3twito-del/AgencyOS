using System.Diagnostics;
using AgencyOS.Api.Authorization;
using AgencyOS.Application.Abstractions;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Interactions;
using AgencyOS.Application.Opportunities;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Projects;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M6 pursuit surface, routed under the tenant that owns the records.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint policies are an early gate only; the authoritative tenant-scoped check
/// and the redaction of opportunity strategy happen inside the services, so an
/// endpoint added later cannot forget either (ADR-0007, ADR-0020).
/// </para>
/// <para>
/// Every path parameter names exactly one kind of identifier, and no two paths
/// differ only in what a parameter is called. M4 shipped such a pair and the
/// contract test that caught it still runs.
/// </para>
/// </remarks>
internal static class M6Endpoints
{
    public static void MapOpportunities(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}");

        MapOpportunityRecords(tenant);
        MapSubjects(tenant);
        MapTargets(tenant);
        MapMarketActivity(tenant);
        MapPipeline(tenant);
    }

    // --------------------------------------------------------- opportunities

    private static void MapOpportunityRecords(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/opportunities", async (
                Guid organizationId,
                OpportunityQueryService queries,
                string? status,
                string? kind,
                Guid? ownerUserId,
                Guid? talentProfileId,
                Guid? projectId,
                Guid? packageId,
                Guid? targetCompanyId,
                Guid? targetPersonId,
                string? targetStage,
                bool? hasSubmission,
                bool? awaitingResponse,
                DateOnly? followUpDueBy,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                OpportunityFilter filter = new(
                    EndpointParsing.ParseNullableEnum<OpportunityStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<OpportunityKind>(kind, nameof(kind)),
                    ownerUserId,
                    talentProfileId,
                    projectId,
                    packageId,
                    targetCompanyId,
                    targetPersonId,
                    EndpointParsing.ParseNullableEnum<OpportunityTargetStage>(
                        targetStage, nameof(targetStage)),
                    hasSubmission ?? false,
                    awaitingResponse ?? false,
                    followUpDueBy,
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<OpportunitySummaryModel> opportunities = await queries
                    .ListOpportunitiesAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(opportunities.Select(MapOpportunitySummary).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesRead))
            .WithName("ListOpportunities");

        tenant.MapGet("/opportunities/{opportunityId:guid}", async (
                Guid organizationId,
                Guid opportunityId,
                OpportunityQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.opportunity.overview");

                OpportunityDetailModel? opportunity = await queries
                    .GetOpportunityAsync(
                        new OrganizationId(organizationId),
                        new OpportunityId(opportunityId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return opportunity is null ? Results.NotFound() : Results.Ok(Map(opportunity));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesRead))
            .WithName("GetOpportunity");

        tenant.MapGet("/opportunities/{opportunityId:guid}/history", async (
                Guid organizationId,
                Guid opportunityId,
                OpportunityQueryService queries,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<OpportunityHistoryEntryModel> history = await queries
                    .GetHistoryAsync(
                        new OrganizationId(organizationId),
                        new OpportunityId(opportunityId),
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(history.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesRead))
            .WithName("GetOpportunityHistory");

        tenant.MapPost("/opportunities", async (
                Guid organizationId,
                CreateOpportunityRequest request,
                OpportunityHandler handler,
                OpportunityQueryService queries,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                OpportunityId id = await handler.HandleAsync(
                    new CreateOpportunityCommand(
                        new OrganizationId(organizationId),
                        request.Name,
                        EndpointParsing.ParseEnum<OpportunityKind>(request.Kind, nameof(request.Kind)),
                        new UserId(request.OwnerUserId),
                        request.OpenedOn ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                        EndpointParsing.ParseEnumOrDefault(
                            request.Priority, nameof(request.Priority), OpportunityPriority.Normal),
                        request.Description,
                        request.StrategyNotes,
                        [.. (request.Subjects ?? []).Select(ToSubjectInput)]),
                    cancellationToken).ConfigureAwait(false);

                OpportunityDetailModel created = (await queries
                    .GetOpportunityAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false))!;

                AgencyOsTelemetry.OpportunitiesCreated.Add(1);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/opportunities/{id.Value}",
                    Map(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("CreateOpportunity");

        tenant.MapPut("/opportunities/{opportunityId:guid}", async (
                Guid organizationId,
                Guid opportunityId,
                UpdateOpportunityRequest request,
                OpportunityHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new UpdateOpportunityCommand(
                        new OrganizationId(organizationId),
                        new OpportunityId(opportunityId),
                        request.Name,
                        EndpointParsing.ParseEnumOrDefault(
                            request.Priority, nameof(request.Priority), OpportunityPriority.Normal),
                        new UserId(request.OwnerUserId),
                        request.Description,
                        request.StrategyNotes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("UpdateOpportunity");

        // One command for the whole lifecycle rather than activate/pause/close
        // endpoints. The legal moves are a published table; splitting them across
        // five routes would restate that table in the routing and let the two drift.
        tenant.MapPost("/opportunities/{opportunityId:guid}/status", async (
                Guid organizationId,
                Guid opportunityId,
                ChangeOpportunityStatusRequest request,
                OpportunityHandler handler,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.opportunity.status");

                await handler.HandleAsync(
                    new ChangeOpportunityStatusCommand(
                        new OrganizationId(organizationId),
                        new OpportunityId(opportunityId),
                        EndpointParsing.ParseEnum<OpportunityStatus>(
                            request.Status, nameof(request.Status)),
                        request.OccurredOn ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                        EndpointParsing.ParseNullableEnum<OpportunityOutcome>(
                            request.Outcome, nameof(request.Outcome)),
                        request.Reason,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.OpportunityStatusChanges.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("ChangeOpportunityStatus");
    }

    // --------------------------------------------------------------- subjects

    private static void MapSubjects(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/opportunities/{opportunityId:guid}/subjects", async (
                Guid organizationId,
                Guid opportunityId,
                AddOpportunitySubjectRequest request,
                OpportunityHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                    new AddOpportunitySubjectCommand(
                        new OrganizationId(organizationId),
                        new OpportunityId(opportunityId),
                        ToSubjectRef(request.Kind, request.TargetId),
                        EndpointParsing.ParseEnumOrDefault(
                            request.Role, nameof(request.Role), OpportunitySubjectRole.Context),
                        request.Note,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/opportunities/{opportunityId}",
                    new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("AddOpportunitySubject");

        tenant.MapPost("/opportunities/{opportunityId:guid}/subjects/{subjectId:guid}/remove", async (
                Guid organizationId,
                Guid opportunityId,
                Guid subjectId,
                RemoveOpportunitySubjectRequest request,
                OpportunityHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new RemoveOpportunitySubjectCommand(
                        new OrganizationId(organizationId),
                        new OpportunityId(opportunityId),
                        subjectId,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("RemoveOpportunitySubject");
    }

    // ---------------------------------------------------------------- targets

    private static void MapTargets(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/opportunities/{opportunityId:guid}/targets", async (
                Guid organizationId,
                Guid opportunityId,
                OpportunityQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<OpportunityTargetModel> targets = await queries
                    .ListTargetsAsync(
                        new OrganizationId(organizationId),
                        new OpportunityId(opportunityId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(targets.Select(MapTarget).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesRead))
            .WithName("ListOpportunityTargets");

        tenant.MapGet("/opportunity-targets/{targetId:guid}", async (
                Guid organizationId,
                Guid targetId,
                OpportunityQueryService queries,
                CancellationToken cancellationToken) =>
            {
                OpportunityTargetModel? target = await queries
                    .GetTargetAsync(
                        new OrganizationId(organizationId),
                        new OpportunityTargetId(targetId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return target is null ? Results.NotFound() : Results.Ok(MapTarget(target));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesRead))
            .WithName("GetOpportunityTarget");

        tenant.MapPost("/opportunities/{opportunityId:guid}/targets", async (
                Guid organizationId,
                Guid opportunityId,
                AddOpportunityTargetRequest request,
                OpportunityTargetHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                OpportunityTargetId id = await handler.HandleAsync(
                    new AddOpportunityTargetCommand(
                        new OrganizationId(organizationId),
                        new OpportunityId(opportunityId),
                        request.CompanyId is { } company ? new CompanyId(company) : null,
                        request.PersonId is { } person ? new PersonId(person) : null,
                        request.ContactPersonId is { } contact ? new PersonId(contact) : null,
                        request.OwnerUserId is { } owner ? new UserId(owner) : null,
                        request.NextActionOn,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.OpportunityTargetChanges.Add(1);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/opportunity-targets/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("AddOpportunityTarget");

        tenant.MapPut("/opportunity-targets/{targetId:guid}", async (
                Guid organizationId,
                Guid targetId,
                UpdateOpportunityTargetRequest request,
                OpportunityTargetHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new UpdateOpportunityTargetCommand(
                        new OrganizationId(organizationId),
                        new OpportunityTargetId(targetId),
                        request.ContactPersonId is { } contact ? new PersonId(contact) : null,
                        request.OwnerUserId is { } owner ? new UserId(owner) : null,
                        request.NextActionOn,
                        request.Notes,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("UpdateOpportunityTarget");

        // Interest, a pass and a withdrawal are all the same act: moving a target
        // to a stage the table says is legal. Separate endpoints per stage would
        // restate the transition table in the routing.
        tenant.MapPost("/opportunity-targets/{targetId:guid}/stage", async (
                Guid organizationId,
                Guid targetId,
                MoveOpportunityTargetRequest request,
                OpportunityTargetHandler handler,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.opportunity.target.stage");

                await handler.HandleAsync(
                    new MoveOpportunityTargetCommand(
                        new OrganizationId(organizationId),
                        new OpportunityTargetId(targetId),
                        EndpointParsing.ParseEnum<OpportunityTargetStage>(
                            request.Stage, nameof(request.Stage)),
                        request.OccurredAt ?? clock.UtcNow,
                        request.Note,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.OpportunityTargetChanges.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("MoveOpportunityTarget");

        tenant.MapPost("/opportunity-targets/{targetId:guid}/responses", async (
                Guid organizationId,
                Guid targetId,
                RecordTargetResponseRequest request,
                OpportunityTargetHandler handler,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                    new RecordTargetResponseCommand(
                        new OrganizationId(organizationId),
                        new OpportunityTargetId(targetId),
                        EndpointParsing.ParseEnum<OpportunityTargetEventKind>(
                            request.Kind, nameof(request.Kind)),
                        request.OccurredAt ?? clock.UtcNow,
                        request.SubmissionId is { } submission
                            ? new SubmissionId(submission)
                            : null,
                        request.Note,
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("RecordTargetResponse");
    }

    // -------------------------------------------------------- market activity

    private static void MapMarketActivity(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/submissions", async (
                Guid organizationId,
                OpportunityQueryService queries,
                Guid? opportunityId,
                Guid? targetId,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<SubmissionModel> submissions = await queries
                    .ListSubmissionsAsync(
                        new OrganizationId(organizationId),
                        opportunityId is { } opportunity ? new OpportunityId(opportunity) : null,
                        targetId is { } target ? new OpportunityTargetId(target) : null,
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(submissions.Select(MapSubmission).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.SubmissionsRead))
            .WithName("ListSubmissions");

        tenant.MapGet("/submissions/{submissionId:guid}", async (
                Guid organizationId,
                Guid submissionId,
                OpportunityQueryService queries,
                CancellationToken cancellationToken) =>
            {
                SubmissionModel? submission = await queries
                    .GetSubmissionAsync(
                        new OrganizationId(organizationId),
                        new SubmissionId(submissionId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return submission is null ? Results.NotFound() : Results.Ok(MapSubmission(submission));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.SubmissionsRead))
            .WithName("GetSubmission");

        // "Record", never "send". AgencyOS transmits nothing and verifies nothing;
        // it stores the agent's assertion that a submission occurred (ADR-0020).
        tenant.MapPost("/opportunity-targets/{targetId:guid}/submissions", async (
                Guid organizationId,
                Guid targetId,
                RecordSubmissionRequest request,
                RecordSubmissionHandler handler,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.submission.record");

                RecordSubmissionResult result = await handler.HandleAsync(
                    new RecordSubmissionCommand(
                        new OrganizationId(organizationId),
                        new OpportunityTargetId(targetId),
                        request.SentAt ?? clock.UtcNow,
                        EndpointParsing.ParseEnum<SubmissionChannel>(
                            request.Channel, nameof(request.Channel)),
                        [
                            .. (request.Materials ?? []).Select(
                                x => new SubmissionMaterialInput(new MaterialId(x.MaterialId), x.Note)),
                        ],
                        request.Subject,
                        request.Notes,
                        request.ResponseExpectedBy,
                        request.ExternalReference,
                        ToFollowUp(request.FollowUp),
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.SubmissionsRecorded.Add(1);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/submissions/{result.SubmissionId.Value}",
                    new RecordSubmissionResponse(
                        result.SubmissionId.Value, result.FollowUpTaskId?.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.SubmissionsWrite))
            .WithName("RecordSubmission");

        tenant.MapGet("/pitches", async (
                Guid organizationId,
                OpportunityQueryService queries,
                Guid? opportunityId,
                Guid? targetId,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<PitchModel> pitches = await queries
                    .ListPitchesAsync(
                        new OrganizationId(organizationId),
                        opportunityId is { } opportunity ? new OpportunityId(opportunity) : null,
                        targetId is { } target ? new OpportunityTargetId(target) : null,
                        limit,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(pitches.Select(MapPitch).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesRead))
            .WithName("ListPitches");

        // Creates the interaction itself. Asking the user to record one first and
        // link it is how a meeting ends up in the system twice.
        tenant.MapPost("/opportunity-targets/{targetId:guid}/pitches", async (
                Guid organizationId,
                Guid targetId,
                RecordPitchRequest request,
                RecordPitchHandler handler,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.pitch.record");

                RecordPitchResult result = await handler.HandleAsync(
                    new RecordPitchCommand(
                        new OrganizationId(organizationId),
                        new OpportunityTargetId(targetId),
                        EndpointParsing.ParseEnum<InteractionType>(
                            request.InteractionType, nameof(request.InteractionType)),
                        request.OccurredAt ?? clock.UtcNow,
                        request.Summary,
                        [.. (request.Participants ?? []).Select(ToParticipant)],
                        EndpointParsing.ParseEnum<PitchKind>(request.Kind, nameof(request.Kind)),
                        EndpointParsing.ParseEnum<PitchOutcome>(
                            request.Outcome, nameof(request.Outcome)),
                        [
                            .. (request.Materials ?? []).Select(
                                x => new SubmissionMaterialInput(new MaterialId(x.MaterialId), x.Note)),
                        ],
                        request.Subject,
                        request.Notes,
                        request.DetailedNotes,
                        ToFollowUp(request.FollowUp),
                        request.ExpectedVersion),
                    cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.PitchesRecorded.Add(1);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/pitches",
                    new RecordPitchResponse(
                        result.PitchId.Value,
                        result.InteractionId.Value,
                        result.FollowUpTaskId?.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesWrite))
            .WithName("RecordPitch");
    }

    // --------------------------------------------------------------- pipeline

    private static void MapPipeline(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/pipeline", async (
                Guid organizationId,
                OpportunityQueryService queries,
                Guid? ownerUserId,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.opportunity.pipeline");

                IReadOnlyList<PipelineColumnModel> columns = await queries
                    .GetPipelineAsync(new OrganizationId(organizationId), ownerUserId, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(columns.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesRead))
            .WithName("GetPipeline");

        tenant.MapGet("/opportunity-command-center", async (
                Guid organizationId,
                OpportunityQueryService queries,
                CancellationToken cancellationToken) =>
            {
                OpportunityCommandCenterModel model = await queries
                    .GetCommandCenterAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new OpportunityCommandCenterResponse(
                    [.. model.OverdueFollowUps.Select(Map)],
                    [.. model.AwaitingResponse.Select(MapSubmission)],
                    [.. model.RecentlyInterested.Select(Map)],
                    [.. model.RecentlyPassed.Select(Map)],
                    model.ActiveOpportunityCount,
                    model.OpenTargetCount));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OpportunitiesRead))
            .WithName("GetOpportunityCommandCenter");
    }

    // ---------------------------------------------------------------- mapping

    private static OpportunitySubjectInput ToSubjectInput(OpportunitySubjectRequest request) => new(
        ToSubjectRef(request.Kind, request.TargetId),
        EndpointParsing.ParseEnumOrDefault(
            request.Role, nameof(request.Role), OpportunitySubjectRole.Context),
        request.Note);

    private static OpportunitySubjectRef ToSubjectRef(string kind, Guid targetId)
    {
        OpportunitySubjectKind parsed =
            EndpointParsing.ParseEnum<OpportunitySubjectKind>(kind, nameof(kind));

        return parsed switch
        {
            OpportunitySubjectKind.TalentProfile =>
                OpportunitySubjectRef.Talent(new TalentProfileId(targetId)),

            OpportunitySubjectKind.Project =>
                OpportunitySubjectRef.Project(new ProjectId(targetId)),

            OpportunitySubjectKind.Package =>
                OpportunitySubjectRef.Package(new PackageId(targetId)),

            _ => OpportunitySubjectRef.ProjectRole(new ProjectRoleId(targetId)),
        };
    }

    private static InteractionParticipantInput ToParticipant(PitchParticipantRequest request)
    {
        PartyKind kind = EndpointParsing.ParseEnum<PartyKind>(
            request.PartyKind, nameof(request.PartyKind));

        RelationshipEndpoint endpoint = kind == PartyKind.Person
            ? RelationshipEndpoint.ForPerson(new PersonId(request.PartyId))
            : RelationshipEndpoint.ForCompany(new CompanyId(request.PartyId));

        return new InteractionParticipantInput(endpoint, request.Role);
    }

    private static OpportunityFollowUpInput? ToFollowUp(OpportunityFollowUpRequest? request) =>
        request is null
            ? null
            : new OpportunityFollowUpInput(
                request.Title,
                request.DueAt,
                request.AssignedTo is { } assignee ? new UserId(assignee) : null,
                request.Notes);

    internal static OpportunitySummaryResponse MapOpportunitySummary(
        OpportunitySummaryModel model) => new(
        model.Id,
        model.Name,
        model.Kind,
        model.Status,
        model.Priority,
        model.OwnerUserId,
        model.OwnerDisplayName,
        model.OpenedOn,
        model.ClosedOn,
        model.Outcome,
        model.PrimarySubject is { } subject ? MapSubject(subject) : null,
        model.TargetCount,
        model.OpenTargetCount,
        model.SubmissionCount,
        model.AwaitingResponseCount,
        model.NextActionOn,
        model.LastActivityAt,
        model.UpdatedAt,
        model.Version);

    private static OpportunitySubjectResponse MapSubject(OpportunitySubjectModel model) => new(
        model.Id,
        model.Kind,
        model.Role,
        model.TargetId,
        model.DisplayName,
        model.Detail,
        model.Note);

    internal static OpportunityTargetResponse MapTarget(OpportunityTargetModel model) => new(
        model.Id,
        model.CompanyId,
        model.PersonId,
        model.DisplayName,
        model.ContactPersonId,
        model.ContactDisplayName,
        model.Stage,
        model.IsOpen,
        model.OwnerUserId,
        model.OwnerDisplayName,
        model.NextActionOn,
        model.ClosedOn,
        model.Notes,
        model.SubmissionCount,
        model.LastSubmittedAt,
        model.PitchCount,
        model.LastPitchedAt,
        model.LastActivityAt,
        model.AwaitingResponseSince,
        model.UpdatedAt,
        model.Version);

    private static SubmissionMaterialResponse MapMaterial(SubmissionMaterialModel model) => new(
        model.MaterialId,
        model.TitleAtSubmission,
        model.TypeAtSubmission,
        model.VersionLabelAtSubmission,
        model.CurrentTitle,
        model.Note);

    internal static SubmissionResponse MapSubmission(SubmissionModel model) => new(
        model.Id,
        model.OpportunityId,
        model.OpportunityTargetId,
        model.TargetDisplayName,
        model.SentAt,
        model.SentByUserId,
        model.SentByDisplayName,
        model.Channel,
        model.Subject,
        model.Notes,
        model.ResponseExpectedBy,
        model.ExternalReference,
        [.. model.Materials.Select(MapMaterial)],
        model.LastResponseAt,
        model.IsAwaitingResponse,
        model.Version);

    private static PitchResponse MapPitch(PitchModel model) => new(
        model.Id,
        model.OpportunityId,
        model.OpportunityTargetId,
        model.TargetDisplayName,
        model.InteractionId,
        model.Kind,
        model.Outcome,
        model.Subject,
        model.Notes,
        model.OccurredAt,
        model.Participants,
        [.. model.Materials.Select(MapMaterial)],
        model.Version);

    private static OpportunityHistoryEntryResponse Map(OpportunityHistoryEntryModel model) => new(
        model.OccurredAt,
        model.Kind,
        model.Summary,
        model.Detail,
        model.TargetDisplayName,
        model.ActorDisplayName);

    private static PipelineEntryResponse Map(PipelineEntryModel model) => new(
        model.OpportunityId,
        model.OpportunityName,
        MapTarget(model.Target));

    private static PipelineColumnResponse Map(PipelineColumnModel model) => new(
        model.Stage,
        [.. model.Targets.Select(Map)]);

    private static OpportunityDetailResponse Map(OpportunityDetailModel model) => new(
        MapOpportunitySummary(model.Summary),
        model.Description,
        model.StrategyNotes,
        [.. model.Subjects.Select(MapSubject)],
        [.. model.Targets.Select(MapTarget)],
        [.. model.Submissions.Select(MapSubmission)],
        [.. model.Pitches.Select(MapPitch)],
        [
            .. model.OpenTasks.Select(x => new OpportunityTaskResponse(
                x.Id, x.Title, x.State, x.Priority, x.DueAt)),
        ],
        model.CreatedAt);
}
