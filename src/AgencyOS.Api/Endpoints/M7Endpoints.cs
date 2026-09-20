using System.Diagnostics;
using AgencyOS.Api.Authorization;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Deals;
using AgencyOS.Contracts.Deals;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Deals;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Opportunities;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M7 negotiation surface, routed under the tenant that owns the records.
/// </summary>
/// <remarks>
/// <para>
/// Endpoint policies are an early gate only. The authoritative tenant-scoped check
/// lives in the services, along with the economics and strategy redaction, so an
/// endpoint added later cannot forget either (ADR-0007, ADR-0021).
/// </para>
/// <para>
/// Nothing here sends an offer. The routes are named for recording, because that
/// is all AgencyOS does: it has no outbound transport and cannot confirm that
/// anything reached anybody.
/// </para>
/// </remarks>
internal static class M7Endpoints
{
    public static void MapDeals(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant = api.MapGroup("/organizations/{organizationId:guid}");

        MapDealRecords(tenant);
        MapOffers(tenant);
        MapNegotiationViews(tenant);
        MapCatalog(api);
    }

    // ------------------------------------------------------------------ deals

    private static void MapDealRecords(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/deals", async (
                Guid organizationId,
                DealQueryService queries,
                string? status,
                string? kind,
                Guid? ownerUserId,
                Guid? opportunityId,
                Guid? opportunityTargetId,
                Guid? counterpartyCompanyId,
                Guid? counterpartyPersonId,
                Guid? talentProfileId,
                Guid? projectId,
                bool? hasOpenOffer,
                bool? termsAgreed,
                DateOnly? openedAfter,
                DateOnly? openedBefore,
                string? search,
                bool? openOnly,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                DealFilter filter = new(
                    EndpointParsing.ParseNullableEnum<DealStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<DealKind>(kind, nameof(kind)),
                    ownerUserId is { } owner ? new UserId(owner) : null,
                    opportunityId is { } opportunity ? new OpportunityId(opportunity) : null,
                    opportunityTargetId is { } target ? new OpportunityTargetId(target) : null,
                    counterpartyCompanyId,
                    counterpartyPersonId,
                    talentProfileId,
                    projectId,
                    hasOpenOffer ?? false,
                    termsAgreed ?? false,
                    openedAfter,
                    openedBefore,
                    string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                    openOnly ?? false);

                IReadOnlyList<DealSummaryModel> deals = await queries
                    .ListDealsAsync(new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(deals.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DealsRead))
            .WithName("ListDeals");

        tenant.MapGet("/deals/{dealId:guid}", async (
                Guid organizationId,
                Guid dealId,
                DealQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.deal.overview");

                DealDetailModel? deal = await queries
                    .GetDealAsync(new OrganizationId(organizationId), new DealId(dealId), cancellationToken)
                    .ConfigureAwait(false);

                return deal is null ? Results.NotFound() : Results.Ok(Map(deal));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DealsRead))
            .WithName("GetDeal");

        tenant.MapGet("/deals/{dealId:guid}/history", async (
                Guid organizationId,
                Guid dealId,
                DealQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<DealHistoryEntryModel> history = await queries
                    .GetHistoryAsync(
                        new OrganizationId(organizationId), new DealId(dealId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(history.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DealsRead))
            .WithName("GetDealHistory");

        tenant.MapPost("/deals", async (
                Guid organizationId,
                CreateDealRequest request,
                DealHandler handler,
                DealQueryService queries,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                CreateDealCommand command = new(
                    new OrganizationId(organizationId),
                    new OpportunityId(request.OpportunityId),
                    new OpportunityTargetId(request.OpportunityTargetId),
                    request.Name,
                    EndpointParsing.ParseEnum<DealKind>(request.Kind, nameof(request.Kind)),
                    new UserId(request.OwnerUserId),
                    request.OpenedOn ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                    request.Reference,
                    request.Summary,
                    request.StrategyNotes);

                DealId id = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

                AgencyOsTelemetry.DealsOpened.Add(1);

                DealDetailModel? deal = await queries
                    .GetDealAsync(new OrganizationId(organizationId), id, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created($"/api/v1/organizations/{organizationId}/deals/{id.Value}", Map(deal!));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DealsWrite))
            .WithName("CreateDeal");

        tenant.MapPut("/deals/{dealId:guid}", async (
                Guid organizationId,
                Guid dealId,
                UpdateDealRequest request,
                DealHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new UpdateDealMetadataCommand(
                            new OrganizationId(organizationId),
                            new DealId(dealId),
                            request.Name,
                            EndpointParsing.ParseEnum<DealKind>(request.Kind, nameof(request.Kind)),
                            new UserId(request.OwnerUserId),
                            request.Reference,
                            request.Summary,
                            request.StrategyNotes,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DealsWrite))
            .WithName("UpdateDeal");

        // A closure, not an arbitrary status patch: the route accepts NoDeal and
        // Cancelled only, and the two statuses that matter commercially are
        // reachable solely by recording and accepting offers.
        tenant.MapPost("/deals/{dealId:guid}/close", async (
                Guid organizationId,
                Guid dealId,
                CloseDealRequest request,
                DealHandler handler,
                IClock clock,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                DealStatus status = EndpointParsing.ParseEnum<DealStatus>(
                    request.Status, nameof(request.Status));

                await handler.HandleAsync(
                        new CloseDealCommand(
                            new OrganizationId(organizationId),
                            new DealId(dealId),
                            status,
                            request.ClosedOn ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.DealStatusChanges.Add(
                    1, new KeyValuePair<string, object?>("status", status.ToString()));

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DealsWrite))
            .WithName("CloseDeal");

        tenant.MapPost("/deals/{dealId:guid}/reopen", async (
                Guid organizationId,
                Guid dealId,
                ReopenNegotiationRequest request,
                DealHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ReopenNegotiationCommand(
                            new OrganizationId(organizationId),
                            new DealId(dealId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.NegotiationsReopened.Add(1);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DealsWrite))
            .WithName("ReopenNegotiation");
    }

    // ----------------------------------------------------------------- offers

    private static void MapOffers(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/offers", async (
                Guid organizationId,
                DealQueryService queries,
                Guid? dealId,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<OfferModel> offers = await queries
                    .ListOffersAsync(
                        new OrganizationId(organizationId),
                        dealId is { } deal ? new DealId(deal) : null,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(offers.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersRead))
            .WithName("ListOffers");

        tenant.MapGet("/offers/{offerId:guid}", async (
                Guid organizationId,
                Guid offerId,
                DealQueryService queries,
                CancellationToken cancellationToken) =>
            {
                OfferModel? offer = await queries
                    .GetOfferAsync(
                        new OrganizationId(organizationId), new OfferId(offerId), cancellationToken)
                    .ConfigureAwait(false);

                return offer is null ? Results.NotFound() : Results.Ok(Map(offer));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersRead))
            .WithName("GetOffer");

        tenant.MapGet("/deals/{dealId:guid}/current-offer", async (
                Guid organizationId,
                Guid dealId,
                DealQueryService queries,
                CancellationToken cancellationToken) =>
            {
                OfferModel? offer = await queries
                    .GetCurrentOfferAsync(
                        new OrganizationId(organizationId), new DealId(dealId), cancellationToken)
                    .ConfigureAwait(false);

                return offer is null ? Results.NoContent() : Results.Ok(Map(offer));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersRead))
            .WithName("GetCurrentOffer");

        // Records that an offer was made or received. It does not send one.
        tenant.MapPost("/deals/{dealId:guid}/offers", async (
                Guid organizationId,
                Guid dealId,
                RecordOfferRequest request,
                OfferHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                OfferDirection direction = EndpointParsing.ParseEnum<OfferDirection>(
                    request.Direction, nameof(request.Direction));

                RecordOfferCommand command = new(
                    new OrganizationId(organizationId),
                    new DealId(dealId),
                    direction,
                    ParseTerms(request.Terms),
                    request.ExpectedVersion,
                    request.RespondsToOfferId is { } responds ? new OfferId(responds) : null,
                    request.CommunicatedAt,
                    request.ExpiresAt,
                    request.Summary,
                    request.Notes,
                    ParseFollowUp(request.FollowUp));

                RecordOfferResult result = await handler
                    .HandleAsync(command, cancellationToken)
                    .ConfigureAwait(false);

                // Identifiers and shape only. A counter that carried the amount
                // would make telemetry a second copy of the economics with none of
                // the permissions guarding the first (ADR-0021).
                AgencyOsTelemetry.OffersRecorded.Add(
                    1,
                    new KeyValuePair<string, object?>("direction", direction.ToString()),
                    new KeyValuePair<string, object?>(
                        "responds_to", request.RespondsToOfferId is not null));

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/offers/{result.OfferId.Value}",
                    new RecordOfferResponse(
                        result.OfferId.Value,
                        result.DealId.Value,
                        result.SupersededOfferId?.Value,
                        result.FollowUpTaskId));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersWrite))
            .WithName("RecordOffer");

        tenant.MapPost("/deals/{dealId:guid}/draft-offers", async (
                Guid organizationId,
                Guid dealId,
                DraftOfferRequest request,
                OfferHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                OfferId id = await handler
                    .HandleAsync(
                        new DraftOfferCommand(
                            new OrganizationId(organizationId),
                            new DealId(dealId),
                            EndpointParsing.ParseEnum<OfferDirection>(
                                request.Direction, nameof(request.Direction)),
                            request.ExpectedVersion,
                            request.RespondsToOfferId is { } responds ? new OfferId(responds) : null,
                            request.ExpiresAt,
                            request.Summary,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/offers/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersWrite))
            .WithName("DraftOffer");

        tenant.MapPut("/offers/{offerId:guid}", async (
                Guid organizationId,
                Guid offerId,
                UpdateDraftOfferRequest request,
                OfferHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new UpdateDraftOfferCommand(
                            new OrganizationId(organizationId),
                            new OfferId(offerId),
                            request.ExpectedVersion,
                            request.CommunicatedAt,
                            request.ExpiresAt,
                            request.Summary,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersWrite))
            .WithName("UpdateDraftOffer");

        tenant.MapPost("/offers/{offerId:guid}/terms", async (
                Guid organizationId,
                Guid offerId,
                ChangeOfferTermRequest request,
                OfferHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ChangeDraftOfferTermCommand(
                            new OrganizationId(organizationId),
                            new OfferId(offerId),
                            EndpointParsing.ParseEnum<DealTermCode>(request.Code, nameof(request.Code)),
                            request.Value is null ? null : ParseValue(request.Value),
                            request.ExpectedVersion,
                            request.Label,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersWrite))
            .WithName("ChangeOfferTerm");

        tenant.MapPost("/offers/{offerId:guid}/record", async (
                Guid organizationId,
                Guid offerId,
                OpenOfferRequest request,
                OfferHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                RecordOfferResult result = await handler
                    .HandleAsync(
                        new OpenOfferCommand(
                            new OrganizationId(organizationId),
                            new OfferId(offerId),
                            request.ExpectedVersion,
                            request.CommunicatedAt,
                            ParseFollowUp(request.FollowUp)),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.OffersRecorded.Add(
                    1, new KeyValuePair<string, object?>("from_draft", true));

                return Results.Ok(new RecordOfferResponse(
                    result.OfferId.Value,
                    result.DealId.Value,
                    result.SupersededOfferId?.Value,
                    result.FollowUpTaskId));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersWrite))
            .WithName("RecordDraftOffer");

        tenant.MapPost("/offers/{offerId:guid}/answer", async (
                Guid organizationId,
                Guid offerId,
                AnswerOfferRequest request,
                OfferHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                OfferAnswer answer = EndpointParsing.ParseEnum<OfferAnswer>(
                    request.Answer, nameof(request.Answer));

                AnswerOfferResult result = await handler
                    .HandleAsync(
                        new AnswerOfferCommand(
                            new OrganizationId(organizationId),
                            new OfferId(offerId),
                            answer,
                            request.ExpectedVersion,
                            request.Reason,
                            ParseFollowUp(request.FollowUp)),
                        cancellationToken)
                    .ConfigureAwait(false);

                AgencyOsTelemetry.OfferAnswers.Add(
                    1, new KeyValuePair<string, object?>("answer", answer.ToString()));

                if (answer == OfferAnswer.Accept)
                {
                    AgencyOsTelemetry.OffersAccepted.Add(1);
                }

                return Results.Ok(new AnswerOfferResponse(
                    result.OfferId.Value, result.DealStatus.ToString(), result.FollowUpTaskId));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersWrite))
            .WithName("AnswerOffer");
    }

    // -------------------------------------------------------- negotiation views

    private static void MapNegotiationViews(RouteGroupBuilder tenant)
    {
        // Requires deals.economics.read and refuses without it. Every other read
        // redacts; this one cannot, because a diff with the economic rows removed
        // would report that nothing changed when the number moved (ADR-0021).
        tenant.MapGet("/deals/{dealId:guid}/comparison", async (
                Guid organizationId,
                Guid dealId,
                Guid previousOfferId,
                Guid currentOfferId,
                DealQueryService queries,
                CancellationToken cancellationToken) =>
            {
                using Activity? activity =
                    AgencyOsTelemetry.Source.StartActivity("agencyos.offer.comparison");

                OfferComparisonModel comparison = await queries
                    .CompareOffersAsync(
                        new OrganizationId(organizationId),
                        new OfferId(previousOfferId),
                        new OfferId(currentOfferId),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (comparison.DealId.Value != dealId)
                {
                    return Results.NotFound();
                }

                AgencyOsTelemetry.OfferComparisons.Add(1);

                return Results.Ok(new OfferComparisonResponse(
                    comparison.DealId.Value,
                    comparison.PreviousOfferId.Value,
                    comparison.CurrentOfferId.Value,
                    [.. comparison.Differences.Select(Map)]));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OffersRead))
            .WithName("CompareOffers");

        tenant.MapGet("/deal-pipeline", async (
                Guid organizationId,
                DealQueryService queries,
                Guid? ownerUserId,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<DealPipelineColumnModel> columns = await queries
                    .GetPipelineAsync(
                        new OrganizationId(organizationId),
                        ownerUserId is { } owner ? new UserId(owner) : null,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(columns
                    .Select(column => new DealPipelineColumnResponse(
                        column.Status.ToString(), [.. column.Deals.Select(Map)]))
                    .ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DealsRead))
            .WithName("GetDealPipeline");

        tenant.MapGet("/deal-command-center", async (
                Guid organizationId,
                DealQueryService queries,
                CancellationToken cancellationToken) =>
            {
                DealCommandCenterModel model = await queries
                    .GetCommandCenterAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new DealCommandCenterResponse(
                    [.. model.Negotiating.Select(Map)],
                    [.. model.AwaitingResponse.Select(Map)],
                    [.. model.ExpiringSoon.Select(Map)],
                    [.. model.RecentlyReceived.Select(Map)],
                    [.. model.RecentlyAccepted.Select(Map)],
                    [.. model.RecentlyClosed.Select(Map)],
                    [.. model.TermsAgreedAwaitingContract.Select(Map)],
                    [.. model.OverdueTasks.Select(Map)],
                    model.NegotiatingCount,
                    model.TermsAgreedCount));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.DealsRead))
            .WithName("GetDealCommandCenter");
    }

    /// <summary>
    /// The term vocabulary, so a client can build an editor without hard-coding it.
    /// </summary>
    /// <remarks>
    /// Not tenant-scoped: the catalog is a property of the build, identical for
    /// everybody, and carries no tenant data. It still requires authentication.
    /// </remarks>
    private static void MapCatalog(RouteGroupBuilder api)
    {
        api.MapGet("/deal-terms", () =>
                Results.Ok(DealTermCatalog.All
                    .Select(definition => new DealTermDefinitionResponse(
                        definition.Code.ToString(),
                        definition.DisplayName,
                        definition.ValueKind.ToString(),
                        definition.Sensitivity == TermSensitivity.Economic,
                        [.. definition.AllowedUnits.Select(unit => unit.ToString())],
                        definition.Minimum,
                        definition.Maximum))
                    .ToArray()))
            .WithName("ListDealTerms");
    }

    // ---------------------------------------------------------------- mapping

    private static IReadOnlyList<OfferTermInput> ParseTerms(IReadOnlyList<OfferTermRequest>? terms) =>
        [.. (terms ?? []).Select(term => new OfferTermInput(
            EndpointParsing.ParseEnum<DealTermCode>(term.Code, nameof(term.Code)),
            ParseValue(term.Value),
            term.Label,
            term.Notes))];

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

    private static DealFollowUpInput? ParseFollowUp(DealFollowUpRequest? followUp) =>
        followUp is null
            ? null
            : new DealFollowUpInput(
                followUp.Title,
                followUp.DueAt,
                followUp.AssignedTo is { } assignee ? new UserId(assignee) : null,
                followUp.Notes);

    /// <summary>Shared with the saved-view surface, which returns the same shape.</summary>
    internal static DealSummaryResponse MapDealSummary(DealSummaryModel deal) => Map(deal);

    private static DealSummaryResponse Map(DealSummaryModel deal) =>
        new(
            deal.Id.Value,
            deal.Name,
            deal.Reference,
            deal.Kind.ToString(),
            deal.Status.ToString(),
            deal.OpportunityId.Value,
            deal.OpportunityName,
            deal.OpportunityTargetId.Value,
            deal.CounterpartyDisplayName,
            deal.CounterpartyCompanyId,
            deal.CounterpartyPersonId,
            deal.SubjectDisplayName,
            deal.OwnerUserId.Value,
            deal.OwnerDisplayName,
            deal.OpenedOn,
            deal.ClosedOn,
            deal.OfferCount,
            deal.LatestOfferId?.Value,
            deal.LatestOfferDirection?.ToString(),
            deal.LatestOfferAt,
            deal.HasOpenOffer,
            deal.OpenOfferExpiresAt,
            deal.AcceptedOfferId?.Value,
            deal.OpenTaskCount,
            deal.NextTaskDueAt,
            deal.UpdatedAt,
            deal.Version);

    private static DealDetailResponse Map(DealDetailModel deal) =>
        new(
            Map(deal.Deal),
            deal.Summary,
            deal.StrategyNotes,
            [.. deal.Offers.Select(Map)],
            deal.AcceptedOffer is { } accepted ? Map(accepted) : null,
            deal.OpenOffer is { } open ? Map(open) : null,
            [.. deal.OpenTasks.Select(Map)],
            deal.CreatedAt);

    private static OfferResponse Map(OfferModel offer) =>
        new(
            offer.Id.Value,
            offer.DealId.Value,
            offer.Direction.ToString(),
            offer.Status.ToString(),
            offer.Sequence,
            offer.RespondsToOfferId?.Value,
            offer.ResponseKind?.ToString(),
            offer.RecordedAt,
            offer.CommunicatedAt,
            offer.RecordedByUserId.Value,
            offer.RecordedByDisplayName,
            offer.Summary,
            offer.Notes,
            offer.ExpiresAt,
            [.. offer.Terms.Select(Map)],
            offer.Version);

    private static OfferTermResponse Map(OfferTermModel term) =>
        new(
            term.Code.ToString(),
            term.DisplayName,
            term.ValueKind.ToString(),
            term.IsEconomic,
            term.Amount,
            term.Currency,
            term.Number,
            term.Whole,
            term.Text,
            term.Flag,
            term.Date,
            term.Unit?.ToString(),
            term.DisplayValue,
            term.Sequence,
            term.Notes);

    private static DealTaskResponse Map(DealTaskModel task) =>
        new(
            task.Id,
            task.Title,
            task.State,
            task.Priority,
            task.DueAt,
            task.OfferId?.Value,
            task.AssigneeUserId,
            task.AssigneeDisplayName);

    private static DealHistoryEntryResponse Map(DealHistoryEntryModel entry) =>
        new(
            entry.OccurredAt,
            entry.Kind,
            entry.Summary,
            entry.Detail,
            entry.OfferId?.Value,
            entry.ActorDisplayName);

    private static TermDifferenceResponse Map(TermDifferenceModel difference) =>
        new(
            difference.Code.ToString(),
            difference.DisplayName,
            difference.Change,
            difference.Direction,
            difference.Previous is { } previous ? Map(previous) : null,
            difference.Current is { } current ? Map(current) : null);
}
