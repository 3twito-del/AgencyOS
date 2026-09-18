using System.Diagnostics;
using AgencyOS.Api.Authorization;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Legal;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Legal;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Legal;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M8 legal surface, routed under the tenant that owns the records.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint policies are an early gate only. The authoritative tenant-scoped check
/// lives in <see cref="ContractQueryService"/> and the handlers, along with the
/// term, economics and privilege redaction, so an endpoint added later cannot
/// forget any of them (ADR-0007, ADR-0021, ADR-0022).
/// </para>
/// <para>
/// Nothing here sends a notice, stores a document or verifies a signature. The
/// routes are named for recording throughout, because recording is all AgencyOS
/// does: it has no outbound transport, no document repository until M10, and no
/// cryptographic verification at all.
/// </para>
/// </remarks>
internal static class M8Endpoints
{
    public static void MapContracts(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}");

        MapContractRecords(tenant);
        MapVersions(tenant);
        MapParties(tenant);
        MapRights(tenant);
        MapOptions(tenant);
        MapObligations(tenant);
        MapNotices(tenant);
        MapLegalViews(tenant);
        MapCatalog(api);
    }

    // -------------------------------------------------------------- contracts

    private static void MapContractRecords(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/contracts", async (
                Guid organizationId,
                ContractQueryService queries,
                string? status,
                string? kind,
                Guid? ownerUserId,
                Guid? dealId,
                Guid? partyCompanyId,
                Guid? partyPersonId,
                Guid? talentProfileId,
                Guid? projectId,
                bool? awaitingSignature,
                bool? effectiveOnly,
                bool? hasUnresolvedReconciliation,
                DateOnly? executedAfter,
                DateOnly? executedBefore,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                ContractFilter filter = new(
                    EndpointParsing.ParseNullableEnum<ContractStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<ContractKind>(kind, nameof(kind)),
                    ownerUserId is { } owner ? new UserId(owner) : null,
                    dealId is { } deal ? new DealId(deal) : null,
                    partyCompanyId,
                    partyPersonId,
                    talentProfileId,
                    projectId,
                    awaitingSignature ?? false,
                    effectiveOnly ?? false,
                    hasUnresolvedReconciliation ?? false,
                    executedAfter,
                    executedBefore,
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim());

                IReadOnlyList<ContractSummaryModel> contracts = await queries
                    .ListContractsAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(contracts.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsRead))
            .WithName("ListContracts");

        tenant.MapGet("/contracts/{contractId:guid}", async (
                Guid organizationId,
                Guid contractId,
                ContractQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.contract.overview");

                ContractDetailModel? contract = await queries
                    .GetContractAsync(
                        new OrganizationId(organizationId), new ContractId(contractId), cancellationToken)
                    .ConfigureAwait(false);

                return contract is null ? Results.NotFound() : Results.Ok(Map(contract));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsRead))
            .WithName("GetContract");

        tenant.MapGet("/contracts/{contractId:guid}/history", async (
                Guid organizationId,
                Guid contractId,
                ContractQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<ContractHistoryEntryModel> history = await queries
                    .GetHistoryAsync(
                        new OrganizationId(organizationId), new ContractId(contractId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(history.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsRead))
            .WithName("GetContractHistory");

        tenant.MapPost("/contracts", async (
                Guid organizationId,
                CreateContractRequest request,
                ContractHandler handler,
                ContractQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                CreateContractCommand command = new(
                    new OrganizationId(organizationId),
                    new DealId(request.DealId),
                    new OfferId(request.AcceptedOfferId),
                    request.Title,
                    EndpointParsing.ParseEnum<ContractKind>(request.Kind, nameof(request.Kind)),
                    new UserId(request.OwnerUserId),
                    request.Reference,
                    request.Summary,
                    request.LegalAnalysis,
                    request.StrategyNotes,
                    ParsePrivilege(request.Privilege));

                ContractId id = await handler.HandleAsync(command, cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.ContractsOpened.Add(
                    1, new KeyValuePair<string, object?>("kind", command.Kind.ToString()));

                ContractDetailModel? contract = await queries
                    .GetContractAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/contracts/{id.Value}", Map(contract!));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("CreateContract");

        tenant.MapPut("/contracts/{contractId:guid}", async (
                Guid organizationId,
                Guid contractId,
                UpdateContractRequest request,
                ContractHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new UpdateContractCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            request.Title,
                            EndpointParsing.ParseEnum<ContractKind>(request.Kind, nameof(request.Kind)),
                            new UserId(request.OwnerUserId),
                            ParsePrivilege(request.Privilege),
                            request.Reference,
                            request.Summary,
                            request.LegalAnalysis,
                            request.StrategyNotes,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("UpdateContract");

        // A named lifecycle move, not an arbitrary status patch. The two statuses
        // that mean somebody signed are unreachable here by construction: they
        // follow from recording signatures, and nothing else produces them.
        tenant.MapPost("/contracts/{contractId:guid}/status", async (
                Guid organizationId,
                Guid contractId,
                ChangeContractStatusRequest request,
                ContractHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ContractTransition transition = EndpointParsing.ParseEnum<ContractTransition>(
                    request.Transition, nameof(request.Transition));

                await handler.HandleAsync(
                        new ChangeContractStatusCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            transition,
                            request.Reason,
                            request.TerminatedOn,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.ContractStatusChanges.Add(
                    1, new KeyValuePair<string, object?>("transition", transition.ToString()));

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("ChangeContractStatus");

        // Separate route because effectiveness is a separate fact. Folding it into
        // execution would collapse two dates the milestone exists to keep apart.
        tenant.MapPost("/contracts/{contractId:guid}/effective-date", async (
                Guid organizationId,
                Guid contractId,
                RecordEffectiveDateRequest request,
                ContractHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new RecordEffectiveDateCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            request.EffectiveOn,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("RecordContractEffectiveDate");

        tenant.MapPost("/contracts/{contractId:guid}/relationships", async (
                Guid organizationId,
                Guid contractId,
                RecordContractRelationshipRequest request,
                ContractHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new RecordContractRelationshipCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            new ContractId(request.RelatedContractId),
                            EndpointParsing.ParseEnum<ContractRelationshipKind>(
                                request.Kind, nameof(request.Kind)),
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("RecordContractRelationship");

        tenant.MapPost("/contracts/{contractId:guid}/tasks", async (
                Guid organizationId,
                Guid contractId,
                CreateContractTaskRequest request,
                ObligationHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid taskId = await handler.HandleAsync(
                        new CreateContractTaskCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            ParseFollowUp(request.FollowUp)!,
                            request.ObligationId is { } obligation
                                ? new ObligationId(obligation)
                                : null,
                            request.ContractOptionId is { } option
                                ? new ContractOptionId(option)
                                : null),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new CreateContractTaskResponse(taskId));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("CreateContractTask");
    }

    // --------------------------------------------------------------- versions

    private static void MapVersions(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/contract-versions/{versionId:guid}", async (
                Guid organizationId,
                Guid versionId,
                ContractQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ContractVersionModel? version = await queries
                    .GetVersionAsync(
                        new OrganizationId(organizationId),
                        new ContractVersionId(versionId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return version is null ? Results.NotFound() : Results.Ok(Map(version));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsRead))
            .WithName("GetContractVersion");

        // Records a draft. It does not store the document: the reference fields say
        // where the file actually lives, and there is no hash, because AgencyOS has
        // not seen the bytes (ADR-0022).
        tenant.MapPost("/contracts/{contractId:guid}/versions", async (
                Guid organizationId,
                Guid contractId,
                RecordContractVersionRequest request,
                ContractVersionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                VersionDirection direction = EndpointParsing.ParseEnum<VersionDirection>(
                    request.Direction, nameof(request.Direction));

                RecordContractVersionCommand command = new(
                    new OrganizationId(organizationId),
                    new ContractId(contractId),
                    request.Label,
                    direction,
                    request.ExpectedVersion,
                    request.ReceivedOn,
                    request.SentOn,
                    request.ExternalReference,
                    request.SourceSystem,
                    request.DisplayFileName,
                    request.MediaType,
                    request.Notes,
                    ParseTerms(request.Terms));

                RecordContractVersionResult result = await handler
                    .HandleAsync(command, cancellationToken)
                    .ConfigureAwait(false);

                // Identifiers and shape only. A counter tagged with a term value
                // would be a second copy of the economics with none of the
                // permissions guarding the first (ADR-0021).
                AgencyOsTelemetry.ContractVersionsRecorded.Add(
                    1,
                    new KeyValuePair<string, object?>("direction", direction.ToString()),
                    new KeyValuePair<string, object?>(
                        "has_terms", request.Terms is { Count: > 0 }));

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/contract-versions/{result.VersionId.Value}",
                    new RecordContractVersionResponse(result.VersionId.Value, result.VersionNumber));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("RecordContractVersion");

        tenant.MapPost("/contract-versions/{versionId:guid}/terms", async (
                Guid organizationId,
                Guid versionId,
                ChangeContractTermRequest request,
                ContractVersionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ChangeContractTermCommand(
                            new OrganizationId(organizationId),
                            new ContractVersionId(versionId),
                            EndpointParsing.ParseEnum<ContractTermCode>(
                                request.Code, nameof(request.Code)),
                            request.Value is null ? null : ParseValue(request.Value),
                            request.ExpectedVersion,
                            request.ClauseReference,
                            request.Label,
                            request.Notes,
                            ParsePrivilege(request.Privilege)),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("ChangeContractTerm");

        tenant.MapPost("/contract-versions/{versionId:guid}/record", async (
                Guid organizationId,
                Guid versionId,
                FinaliseContractVersionRequest request,
                ContractVersionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new FinaliseContractVersionCommand(
                            new OrganizationId(organizationId),
                            new ContractVersionId(versionId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("FinaliseContractVersion");

        // The comparison the milestone exists for. It reports what differs and
        // never whether a difference is acceptable (ADR-0022).
        tenant.MapGet("/contracts/{contractId:guid}/versions/{versionId:guid}/reconciliation", async (
                Guid organizationId,
                Guid contractId,
                Guid versionId,
                ContractQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.contract.reconcile");

                ReconciliationModel? reconciliation = await queries
                    .ReconcileAsync(
                        new OrganizationId(organizationId),
                        new ContractId(contractId),
                        new ContractVersionId(versionId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (reconciliation is null)
                {
                    return Results.NotFound();
                }

                AgencyOsTelemetry.Reconciliations.Add(
                    1,
                    new KeyValuePair<string, object?>("faithful", reconciliation.IsFaithful));

                return Results.Ok(Map(reconciliation));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsRead))
            .WithName("ReconcileContractVersion");
    }

    // ---------------------------------------------------------------- parties

    private static void MapParties(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/contracts/{contractId:guid}/parties", async (
                Guid organizationId,
                Guid contractId,
                AddContractPartyRequest request,
                ContractHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ContractPartyRef party = new(
                    request.PersonId is { } person ? new Domain.People.PersonId(person) : null,
                    request.CompanyId is { } company
                        ? new Domain.Companies.CompanyId(company)
                        : null,
                    request.ExternalName,
                    request.Provenance);

                Guid id = await handler.HandleAsync(
                        new AddContractPartyCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            party,
                            EndpointParsing.ParseEnum<ContractPartyRole>(
                                request.Role, nameof(request.Role)),
                            request.IsRequiredSignatory,
                            request.Notes,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new AddContractPartyResponse(id));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("AddContractParty");

        // The only route to execution, and an assertion rather than a verification.
        // AgencyOS implements no electronic signature and checks nothing
        // cryptographically (ADR-0022).
        tenant.MapPost("/contracts/{contractId:guid}/signatures", async (
                Guid organizationId,
                Guid contractId,
                RecordSignatureRequest request,
                ContractHandler handler,
                ContractQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ContractStatus status = await handler.HandleAsync(
                        new RecordSignatureCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            request.ContractPartyId,
                            request.SignedOn,
                            EndpointParsing.ParseEnum<SignatureMethod>(
                                request.Method, nameof(request.Method)),
                            request.ExternalReference,
                            request.Notes,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.SignaturesRecorded.Add(
                    1, new KeyValuePair<string, object?>("status", status.ToString()));

                if (status == ContractStatus.Executed)
                {
                    AgencyOsTelemetry.ContractsExecuted.Add(1);
                }

                ContractDetailModel? contract = await queries
                    .GetContractAsync(
                        new OrganizationId(organizationId), new ContractId(contractId), cancellationToken)
                    .ConfigureAwait(false);

                ContractPartyModel? party = contract?.Parties
                    .FirstOrDefault(x => x.Id == request.ContractPartyId);

                return Results.Ok(new RecordSignatureResponse(
                    party?.Id ?? request.ContractPartyId,
                    status.ToString(),
                    contract?.Contract.OutstandingSignatureCount ?? 0));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsWrite))
            .WithName("RecordContractSignature");
    }

    // ----------------------------------------------------------------- rights

    private static void MapRights(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/rights-grants", async (
                Guid organizationId,
                ContractQueryService queries,
                Guid? contractId,
                Guid? projectId,
                bool? currentOnly,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<RightsGrantModel> grants = await queries
                    .ListRightsGrantsAsync(
                        new OrganizationId(organizationId),
                        contractId is { } contract ? new ContractId(contract) : null,
                        projectId,
                        currentOnly ?? true,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(grants.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RightsRead))
            .WithName("ListRightsGrants");

        tenant.MapPost("/contracts/{contractId:guid}/rights-grants", async (
                Guid organizationId,
                Guid contractId,
                RecordRightsGrantRequest request,
                RightsHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                RecordRightsGrantCommand command = new(
                    new OrganizationId(organizationId),
                    new ContractId(contractId),
                    new ContractVersionId(request.ContractVersionId),
                    request.GrantorPartyId,
                    request.GranteePartyId,
                    EndpointParsing.ParseEnum<RightType>(request.RightType, nameof(request.RightType)),
                    EndpointParsing.ParseEnum<RightsMedium>(request.Medium, nameof(request.Medium)),
                    EndpointParsing.ParseEnum<RightsTerritory>(
                        request.Territory, nameof(request.Territory)),
                    EndpointParsing.ParseEnum<GrantExclusivity>(
                        request.Exclusivity, nameof(request.Exclusivity)),
                    EndpointParsing.ParseEnum<GrantPeriodKind>(
                        request.PeriodKind, nameof(request.PeriodKind)),
                    request.StartsOn,
                    request.EndsOn,
                    request.TerritoryDetail,
                    request.ClauseReference,
                    request.SourcePropertyId,
                    request.ProjectId,
                    request.Reservations,
                    request.Notes,
                    request.SupersedesGrantId is { } superseded
                        ? new RightsGrantId(superseded)
                        : null,
                    request.SupersededExpectedVersion);

                RightsGrantId id = await handler.HandleAsync(command, cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.RightsGrantsRecorded.Add(
                    1,
                    new KeyValuePair<string, object?>("right", command.RightType.ToString()),
                    new KeyValuePair<string, object?>(
                        "supersedes", request.SupersedesGrantId is not null));

                return Results.Ok(new RecordRightsGrantResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RightsWrite))
            .WithName("RecordRightsGrant");

        tenant.MapPost("/rights-grants/{grantId:guid}/end", async (
                Guid organizationId,
                Guid grantId,
                EndRightsGrantRequest request,
                RightsHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new EndRightsGrantCommand(
                            new OrganizationId(organizationId),
                            new RightsGrantId(grantId),
                            request.EndedOn,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RightsWrite))
            .WithName("EndRightsGrant");
    }

    // ---------------------------------------------------------------- options

    private static void MapOptions(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/contract-options", async (
                Guid organizationId,
                ContractQueryService queries,
                Guid? contractId,
                string? status,
                string? kind,
                bool? exercisableOnly,
                bool? pastDeadlineOnly,
                DateOnly? deadlineBefore,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                OptionFilter filter = new(
                    contractId is { } contract ? new ContractId(contract) : null,
                    EndpointParsing.ParseNullableEnum<OptionStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<OptionKind>(kind, nameof(kind)),
                    exercisableOnly ?? false,
                    pastDeadlineOnly ?? false,
                    deadlineBefore);

                IReadOnlyList<ContractOptionModel> options = await queries
                    .ListOptionsAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(options.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RightsRead))
            .WithName("ListContractOptions");

        tenant.MapPost("/contracts/{contractId:guid}/options", async (
                Guid organizationId,
                Guid contractId,
                RecordOptionRequest request,
                OptionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                RecordOptionCommand command = new(
                    new OrganizationId(organizationId),
                    new ContractId(contractId),
                    new ContractVersionId(request.ContractVersionId),
                    EndpointParsing.ParseEnum<OptionKind>(request.Kind, nameof(request.Kind)),
                    request.HolderPartyId,
                    request.Subject,
                    ParseDeadline(request.Deadline),
                    request.AnchorDate,
                    request.WindowOpensOn,
                    request.ClauseReference,
                    request.ExerciseMethod,
                    request.ProjectId,
                    request.SourcePropertyId,
                    request.EconomicsTermId is { } term ? new ContractTermId(term) : null,
                    request.Notes);

                ContractOptionId id = await handler.HandleAsync(command, cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.OptionsRecorded.Add(
                    1, new KeyValuePair<string, object?>("kind", command.Kind.ToString()));

                return Results.Ok(new RecordOptionResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RightsWrite))
            .WithName("RecordContractOption");

        // One route for every outcome, because they are the same decision recorded
        // differently. None of them happens on its own: an option is never
        // exercised because money arrived, and never expires because a date passed
        // (ADR-0022).
        tenant.MapPost("/contract-options/{optionId:guid}/resolve", async (
                Guid organizationId,
                Guid optionId,
                ResolveOptionRequest request,
                OptionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                OptionOutcome outcome = EndpointParsing.ParseEnum<OptionOutcome>(
                    request.Outcome, nameof(request.Outcome));

                OptionStatus status = await handler.HandleAsync(
                        new ResolveOptionCommand(
                            new OrganizationId(organizationId),
                            new ContractOptionId(optionId),
                            outcome,
                            request.ExpectedVersion,
                            request.OccurredOn,
                            request.Reason,
                            ParseFollowUp(request.FollowUp)),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.OptionsResolved.Add(
                    1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));

                return Results.Ok(new ResolveOptionResponse(optionId, status.ToString()));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.RightsWrite))
            .WithName("ResolveContractOption");
    }

    // ------------------------------------------------------------ obligations

    private static void MapObligations(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/obligations", async (
                Guid organizationId,
                ContractQueryService queries,
                Guid? contractId,
                string? status,
                string? kind,
                Guid? obligorPartyId,
                bool? outstandingOnly,
                bool? overdueOnly,
                DateOnly? dueBefore,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                ObligationFilter filter = new(
                    contractId is { } contract ? new ContractId(contract) : null,
                    EndpointParsing.ParseNullableEnum<ObligationStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<ObligationKind>(kind, nameof(kind)),
                    obligorPartyId,
                    outstandingOnly ?? false,
                    overdueOnly ?? false,
                    dueBefore);

                IReadOnlyList<ObligationModel> obligations = await queries
                    .ListObligationsAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(obligations.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ObligationsRead))
            .WithName("ListObligations");

        tenant.MapPost("/contracts/{contractId:guid}/obligations", async (
                Guid organizationId,
                Guid contractId,
                RecordObligationRequest request,
                ObligationHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                RecordObligationCommand command = new(
                    new OrganizationId(organizationId),
                    new ContractId(contractId),
                    new ContractVersionId(request.ContractVersionId),
                    request.ObligorPartyId,
                    request.ObligeePartyId,
                    EndpointParsing.ParseEnum<ObligationKind>(request.Kind, nameof(request.Kind)),
                    request.Description,
                    ParseDeadline(request.Due),
                    request.AnchorDate,
                    request.ClauseReference,
                    request.RelatedOptionId is { } option ? new ContractOptionId(option) : null,
                    request.RelatedRightsGrantId is { } grant ? new RightsGrantId(grant) : null,
                    request.Notes,
                    ParsePrivilege(request.Privilege));

                ObligationId id = await handler.HandleAsync(command, cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.ObligationsRecorded.Add(
                    1, new KeyValuePair<string, object?>("kind", command.Kind.ToString()));

                return Results.Ok(new RecordObligationResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ObligationsWrite))
            .WithName("RecordObligation");

        // Breach is one outcome among five and requires a reason. It is never
        // reached by a due date passing: past due is a fact AgencyOS derives, and
        // breach is a determination a person makes (ADR-0022).
        tenant.MapPost("/obligations/{obligationId:guid}/resolve", async (
                Guid organizationId,
                Guid obligationId,
                ResolveObligationRequest request,
                ObligationHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ObligationOutcome outcome = EndpointParsing.ParseEnum<ObligationOutcome>(
                    request.Outcome, nameof(request.Outcome));

                ObligationStatus status = await handler.HandleAsync(
                        new ResolveObligationCommand(
                            new OrganizationId(organizationId),
                            new ObligationId(obligationId),
                            outcome,
                            request.ExpectedVersion,
                            request.OccurredOn,
                            request.Reason,
                            ParseFollowUp(request.FollowUp)),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.ObligationsResolved.Add(
                    1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));

                return Results.Ok(new ResolveObligationResponse(obligationId, status.ToString()));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ObligationsWrite))
            .WithName("ResolveObligation");
    }

    // ---------------------------------------------------------------- notices

    private static void MapNotices(RouteGroupBuilder tenant)
    {
        tenant.MapPost("/contracts/{contractId:guid}/notice-requirements", async (
                Guid organizationId,
                Guid contractId,
                RecordNoticeRequirementRequest request,
                ObligationHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                NoticeRequirementId id = await handler.HandleAsync(
                        new RecordNoticeRequirementCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            new ContractVersionId(request.ContractVersionId),
                            request.ObligorPartyId,
                            request.RecipientPartyId,
                            request.Description,
                            ParseDeadline(request.Due),
                            EndpointParsing.ParseEnum<NoticeMethod>(
                                request.Method, nameof(request.Method)),
                            request.AnchorDate,
                            request.ClauseReference,
                            request.AddressReference,
                            request.RelatedOptionId is { } option
                                ? new ContractOptionId(option)
                                : null,
                            request.RelatedObligationId is { } obligation
                                ? new ObligationId(obligation)
                                : null,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new RecordNoticeRequirementResponse(id.Value));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ObligationsWrite))
            .WithName("RecordNoticeRequirement");

        // "Record", never "send". AgencyOS has no outbound transport and cannot
        // confirm that anything reached anybody; this stores an assertion that a
        // notice passed between the parties (ADR-0022).
        tenant.MapPost("/contracts/{contractId:guid}/notices", async (
                Guid organizationId,
                Guid contractId,
                RecordNoticeRequest request,
                ObligationHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                NoticeDirection direction = EndpointParsing.ParseEnum<NoticeDirection>(
                    request.Direction, nameof(request.Direction));

                Guid id = await handler.HandleAsync(
                        new RecordNoticeCommand(
                            new OrganizationId(organizationId),
                            new ContractId(contractId),
                            direction,
                            request.SenderPartyId,
                            request.RecipientPartyId,
                            request.OccurredOn,
                            EndpointParsing.ParseEnum<NoticeMethod>(
                                request.Method, nameof(request.Method)),
                            request.NoticeRequirementId is { } requirement
                                ? new NoticeRequirementId(requirement)
                                : null,
                            request.ExternalReference,
                            request.Summary,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.NoticesRecorded.Add(
                    1, new KeyValuePair<string, object?>("direction", direction.ToString()));

                return Results.Ok(new RecordNoticeResponse(id));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ObligationsWrite))
            .WithName("RecordNotice");
    }

    // ------------------------------------------------------------------ views

    private static void MapLegalViews(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/legal/deadlines", async (
                Guid organizationId,
                ContractQueryService queries,
                int? withinDays,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<LegalDeadlineModel> deadlines = await queries
                    .GetDeadlinesAsync(new OrganizationId(organizationId), withinDays, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(deadlines.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsRead))
            .WithName("ListLegalDeadlines");

        tenant.MapGet("/legal/command-center", async (
                Guid organizationId,
                ContractQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.contract.command_center");

                ContractCommandCenterModel centre = await queries
                    .GetCommandCenterAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(Map(centre));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.ContractsRead))
            .WithName("GetLegalCommandCenter");
    }

    private static void MapCatalog(RouteGroupBuilder api)
    {
        // Published so a client can build a term editor without hard-coding the
        // vocabulary. The catalog is the single source; a client that guessed would
        // drift from it (ADR-0021, ADR-0022).
        api.MapGet("/contract-terms/catalog", () =>
                Results.Ok(ContractTermCatalog.All
                    .Select(definition => new ContractTermDefinitionResponse(
                        definition.Code.ToString(),
                        definition.DisplayName,
                        definition.ValueKind.ToString(),
                        ContractTermCatalog.IsEconomic(definition.Code),
                        definition.IsCommercial))
                    .ToArray()))
            .RequireAuthorization()
            .WithName("GetContractTermCatalog");
    }

    // ----------------------------------------------------------------- parsing

    private static IReadOnlyList<ContractTermInput> ParseTerms(
        IReadOnlyList<ContractTermRequest>? terms) =>
        [.. (terms ?? []).Select(term => new ContractTermInput(
            EndpointParsing.ParseEnum<ContractTermCode>(term.Code, nameof(term.Code)),
            ParseValue(term.Value),
            term.ClauseReference,
            term.Label,
            term.Notes,
            ParsePrivilege(term.Privilege)))];

    /// <remarks>
    /// A caller who omits the object is refused as a caller, not as a bug
    /// (<c>AOS-R002-025</c>).
    /// </remarks>
    private static DealTermValue ParseValue(TermValueRequest value)
    {
        if (value is null)
        {
            throw new DomainException("Value is required.");
        }

        return new DealTermValue(
            EndpointParsing.ParseEnum<TermValueKind>(value.Kind, nameof(value.Kind)),
            value.Amount,
            value.Currency,
            value.Number,
            value.Whole,
            value.Text,
            value.Flag,
            value.Date,
            EndpointParsing.ParseNullableEnum<TermUnit>(value.Unit, nameof(value.Unit)));
    }

    /// <summary>
    /// Reads a deadline rule off the wire and validates its shape.
    /// </summary>
    /// <remarks>
    /// <see cref="DeadlineRule.Validated"/> refuses a rule the kernel cannot read -
    /// an absolute deadline with no date, a relative one with no anchor - so the
    /// caller learns immediately rather than storing a clause that silently
    /// resolves to nothing forever.
    /// </remarks>
    private static DeadlineRule ParseDeadline(DeadlineRuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new DeadlineRule(
            EndpointParsing.ParseEnum<DeadlineRuleKind>(request.Kind, nameof(request.Kind)),
            request.On,
            EndpointParsing.ParseNullableEnum<DeadlineAnchor>(request.Anchor, nameof(request.Anchor)),
            request.Offset,
            EndpointParsing.ParseNullableEnum<DeadlineOffsetUnit>(request.Unit, nameof(request.Unit)),
            request.Before,
            EndpointParsing.ParseEnumOrDefault(
                request.Basis, nameof(request.Basis), DeadlineCalendarBasis.CalendarDays),
            request.Description).Validated();
    }

    /// <summary>
    /// Reads a privilege classification, defaulting to ordinary.
    /// </summary>
    /// <remarks>
    /// Ordinary is the default so nothing becomes privileged by accident: marking
    /// something is always a deliberate act by a person, and AgencyOS never decides
    /// for itself that a clause is legal advice (ADR-0022).
    /// </remarks>
    private static PrivilegeClass ParsePrivilege(string? privilege) =>
        EndpointParsing.ParseEnumOrDefault(privilege, "privilege", PrivilegeClass.Ordinary);

    private static LegalFollowUpInput? ParseFollowUp(LegalFollowUpRequest? followUp) =>
        followUp is null
            ? null
            : new LegalFollowUpInput(
                followUp.Title,
                followUp.DueAt,
                followUp.AssignedTo is { } assignee ? new UserId(assignee) : null,
                followUp.Notes);

    // --------------------------------------------------------------- mapping

    /// <summary>Shared with the saved-view surface, which returns the same shape.</summary>
    internal static ContractSummaryResponse MapContractSummary(ContractSummaryModel contract) =>
        Map(contract);

    private static ContractSummaryResponse Map(ContractSummaryModel contract) =>
        new(
            contract.Id.Value,
            contract.Title,
            contract.Reference,
            contract.Kind.ToString(),
            contract.Status.ToString(),
            contract.DealId.Value,
            contract.DealName,
            contract.AcceptedOfferId.Value,
            contract.SubjectDisplayName,
            contract.CounterpartyDisplayName,
            contract.OwnerUserId.Value,
            contract.OwnerDisplayName,
            contract.ExecutedOn,
            contract.EffectiveOn,
            contract.TerminatedOn,
            contract.IsEffective,
            contract.VersionCount,
            contract.LatestVersionNumber,
            contract.LatestVersionLabel,
            contract.LatestVersionId?.Value,
            contract.OutstandingSignatureCount,
            contract.PartyCount,
            contract.RightsGrantCount,
            contract.OpenOptionCount,
            contract.OutstandingObligationCount,
            contract.OverdueObligationCount,
            contract.NextDeadlineOn,
            contract.NextDeadlineDescription,
            contract.UnresolvedDifferenceCount,
            contract.UpdatedAt,
            contract.Version);

    private static ContractDetailResponse Map(ContractDetailModel contract) =>
        new(
            Map(contract.Contract),
            contract.Summary,
            contract.LegalAnalysis,
            contract.StrategyNotes,
            contract.Privilege.ToString(),
            [.. contract.Parties.Select(Map)],
            [.. contract.Versions.Select(Map)],
            [.. contract.Relationships.Select(Map)],
            [.. contract.RightsGrants.Select(Map)],
            [.. contract.Options.Select(Map)],
            [.. contract.Obligations.Select(Map)],
            [.. contract.NoticeRequirements.Select(Map)],
            [.. contract.RecordedNotices.Select(Map)],
            [.. contract.OpenTasks.Select(Map)],
            contract.CreatedAt);

    private static ContractPartyResponse Map(ContractPartyModel party) =>
        new(
            party.Id,
            party.Role.ToString(),
            party.DisplayName,
            party.PersonId,
            party.CompanyId,
            party.IsResolved,
            party.Provenance,
            party.IsRequiredSignatory,
            party.HasSigned,
            party.SignedOn);

    private static ContractVersionResponse Map(ContractVersionModel version) =>
        new(
            version.Id.Value,
            version.ContractId.Value,
            version.VersionNumber,
            version.Label,
            version.Direction.ToString(),
            version.Status.ToString(),
            version.RecordedAt,
            version.ReceivedOn,
            version.SentOn,
            version.RecordedByDisplayName,
            version.ExternalReference,
            version.SourceSystem,
            version.DisplayFileName,
            version.MediaType,
            version.HoldsDocument,
            version.Notes,
            [.. version.Terms.Select(Map)],
            version.Version);

    private static ContractTermResponse Map(ContractTermModel term) =>
        new(
            term.Code.ToString(),
            term.DisplayName,
            term.ValueKind.ToString(),
            term.IsEconomic,
            term.IsCommercial,
            term.Amount,
            term.Currency,
            term.Number,
            term.Whole,
            term.Text,
            term.Flag,
            term.Date,
            term.Unit?.ToString(),
            term.DisplayValue,
            term.ClauseReference,
            term.Sequence,
            term.Notes);

    private static RightsGrantResponse Map(RightsGrantModel grant) =>
        new(
            grant.Id.Value,
            grant.ContractId.Value,
            grant.ContractVersionId.Value,
            grant.ClauseReference,
            grant.GrantorPartyId,
            grant.GrantorDisplayName,
            grant.GranteePartyId,
            grant.GranteeDisplayName,
            grant.RightType.ToString(),
            grant.Medium.ToString(),
            grant.Territory.ToString(),
            grant.TerritoryDetail,
            grant.Exclusivity.ToString(),
            grant.PeriodKind.ToString(),
            grant.StartsOn,
            grant.EndsOn,
            grant.IsCurrent,
            grant.SourcePropertyId,
            grant.SourcePropertyTitle,
            grant.ProjectId,
            grant.ProjectTitle,
            grant.Reservations,
            grant.Notes,
            grant.Status.ToString(),
            grant.SupersededByGrantId?.Value,
            grant.Version);

    /// <summary>Shared with the saved-view surface.</summary>
    internal static ContractOptionResponse MapOption(ContractOptionModel option) => Map(option);

    private static ContractOptionResponse Map(ContractOptionModel option) =>
        new(
            option.Id.Value,
            option.ContractId.Value,
            option.ContractVersionId.Value,
            option.ClauseReference,
            option.Kind.ToString(),
            option.HolderPartyId,
            option.HolderDisplayName,
            option.Subject,
            option.ProjectId,
            option.ProjectTitle,
            option.WindowOpensOn,
            option.DeadlineOn,
            option.DeadlineUnresolvedReason,
            option.DeadlineDescription,
            option.ExerciseMethod,
            option.Status.ToString(),
            option.ResolvedOn,
            option.IsExercisable,
            option.IsPastDeadline,
            option.EconomicsTermId?.Value,
            option.NoticeRequirementId?.Value,
            option.Notes,
            option.Version);

    /// <summary>Shared with the saved-view surface.</summary>
    internal static ObligationResponse MapObligation(ObligationModel obligation) => Map(obligation);

    private static ObligationResponse Map(ObligationModel obligation) =>
        new(
            obligation.Id.Value,
            obligation.ContractId.Value,
            obligation.ContractVersionId.Value,
            obligation.ClauseReference,
            obligation.ObligorPartyId,
            obligation.ObligorDisplayName,
            obligation.ObligeePartyId,
            obligation.ObligeeDisplayName,
            obligation.Kind.ToString(),
            obligation.Description,
            obligation.DueOn,
            obligation.DueUnresolvedReason,
            obligation.DueDescription,
            obligation.Status.ToString(),
            obligation.ResolvedOn,
            obligation.IsPastDue,
            obligation.RelatedOptionId?.Value,
            obligation.RelatedRightsGrantId?.Value,
            obligation.Notes,
            obligation.Version);

    private static NoticeRequirementResponse Map(NoticeRequirementModel requirement) =>
        new(
            requirement.Id.Value,
            requirement.ContractId.Value,
            requirement.ClauseReference,
            requirement.ObligorPartyId,
            requirement.ObligorDisplayName,
            requirement.RecipientPartyId,
            requirement.RecipientDisplayName,
            requirement.Description,
            requirement.DueOn,
            requirement.DueUnresolvedReason,
            requirement.DueDescription,
            requirement.Method.ToString(),
            requirement.AddressReference,
            requirement.RelatedOptionId?.Value,
            requirement.RelatedObligationId?.Value,
            requirement.RecordedNoticeCount,
            requirement.Version);

    private static NoticeRecordResponse Map(NoticeRecordModel notice) =>
        new(
            notice.Id,
            notice.ContractId.Value,
            notice.NoticeRequirementId?.Value,
            notice.Direction.ToString(),
            notice.SenderDisplayName,
            notice.RecipientDisplayName,
            notice.OccurredOn,
            notice.Method.ToString(),
            notice.ExternalReference,
            notice.Summary,
            notice.RecordedByDisplayName);

    private static ContractRelationshipResponse Map(ContractRelationshipModel relationship) =>
        new(
            relationship.Id,
            relationship.Kind.ToString(),
            relationship.RelatedContractId.Value,
            relationship.RelatedContractTitle,
            relationship.RelatedContractStatus.ToString(),
            relationship.Notes);

    private static ContractTaskResponse Map(ContractTaskModel task) =>
        new(
            task.Id,
            task.Title,
            task.State,
            task.Priority,
            task.DueAt,
            task.ObligationId?.Value,
            task.ContractOptionId?.Value);

    private static ContractHistoryEntryResponse Map(ContractHistoryEntryModel entry) =>
        new(entry.OccurredAt, entry.Kind, entry.Summary, entry.Detail, entry.ActorDisplayName);

    private static ReconciliationResponse Map(ReconciliationModel reconciliation) =>
        new(
            reconciliation.ContractId.Value,
            reconciliation.ContractVersionId.Value,
            reconciliation.AcceptedOfferId.Value,
            [.. reconciliation.Lines.Select(Map)],
            reconciliation.DifferenceCount,
            reconciliation.IsFaithful);

    private static ReconciliationLineResponse Map(ReconciliationLineModel line) =>
        new(
            line.Code.ToString(),
            line.DisplayName,
            line.Result,
            line.Direction,
            line.Negotiated is null ? null : Map(line.Negotiated),
            line.Contracted is null ? null : Map(line.Contracted));

    private static LegalDeadlineResponse Map(LegalDeadlineModel deadline) =>
        new(
            deadline.Source.ToString(),
            deadline.SourceId,
            deadline.ContractId.Value,
            deadline.ContractTitle,
            deadline.DueOn,
            deadline.Description,
            deadline.DaysRemaining,
            deadline.IsPast);

    private static DealWithoutContractResponse Map(DealsWithoutContractModel deal) =>
        new(deal.DealId.Value, deal.DealName, deal.CounterpartyDisplayName, deal.AgreedAt);

    private static ContractCommandCenterResponse Map(ContractCommandCenterModel centre) =>
        new(
            [.. centre.UnderReview.Select(Map)],
            [.. centre.AwaitingSignature.Select(Map)],
            [.. centre.WithUnresolvedDifferences.Select(Map)],
            [.. centre.RecentlyExecuted.Select(Map)],
            [.. centre.TermsAgreedWithoutContract.Select(Map)],
            [.. centre.UpcomingDeadlines.Select(Map)],
            [.. centre.OverdueObligations.Select(Map)],
            [.. centre.OptionsPastDeadline.Select(Map)],
            [.. centre.OverdueTasks.Select(Map)],
            centre.UnderReviewCount,
            centre.AwaitingSignatureCount,
            centre.EffectiveCount);
}
