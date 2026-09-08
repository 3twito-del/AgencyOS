using AgencyOS.Api.Authorization;
using AgencyOS.Application.Intelligence;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Communications;
using AgencyOS.Domain.Documents;
using AgencyOS.Domain.Intelligence;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The M11 intelligence surface.
/// </summary>
/// <remarks>
/// <para>
/// One chain, kept apart at every level: a <em>source</em> is evidence, a
/// <em>signal</em> is a claim with provenance, a <em>thesis</em> is a view somebody
/// holds, a <em>prediction</em> is a falsifiable statement with a date, and a
/// watchlist, radar entry or research case is a standing piece of work. Collapsing
/// any two of them into "an intelligence note" would lose the distinction the whole
/// milestone exists to keep (§1, ADR-0030).
/// </para>
/// <para>
/// <strong>Nothing here summarizes, extracts, ranks or scores.</strong> There is no
/// route that generates a signal from a document, no route that produces a
/// probability, and no route that returns a relationship score. Every judgment on
/// this surface was typed by a named person on a stated date; the only numbers
/// AgencyOS produces are counts and Brier arithmetic over resolved forecasts
/// (§18, §54).
/// </para>
/// </remarks>
internal static class M11Endpoints
{
    public static void MapIntelligence(RouteGroupBuilder api)
    {
        RouteGroupBuilder tenant =
            api.MapGroup("/organizations/{organizationId:guid}/intelligence");

        MapSources(tenant);
        MapSignals(tenant);
        MapTheses(tenant);
        MapPredictions(tenant);
        MapWatchlists(tenant);
        MapRadar(tenant);
        MapResearchCases(tenant);
        MapDerived(tenant);
    }

    // -------------------------------------------------------------- sources

    private static void MapSources(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/sources", async (
                Guid organizationId,
                IntelligenceQueryService queries,
                string? kind,
                string? reliability,
                string? sensitivity,
                Guid? recordedBy,
                DateOnly? observedAfter,
                DateOnly? observedBefore,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                SourceFilter filter = new(
                    EndpointParsing.ParseNullableEnum<IntelligenceSourceKind>(kind, nameof(kind)),
                    EndpointParsing.ParseNullableEnum<SourceReliability>(
                        reliability, nameof(reliability)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSensitivity>(
                        sensitivity, nameof(sensitivity)),
                    recordedBy,
                    observedAfter,
                    observedBefore,
                    Trimmed(search));

                IReadOnlyList<IntelligenceSourceModel> sources = await queries
                    .ListSourcesAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(sources.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("ListIntelligenceSources");

        tenant.MapGet("/sources/{sourceId:guid}", async (
                Guid organizationId,
                Guid sourceId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IntelligenceSourceModel? source = await queries
                    .GetSourceAsync(
                        new OrganizationId(organizationId),
                        new IntelligenceSourceId(sourceId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return source is null ? Results.NotFound() : Results.Ok(Map(source));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetIntelligenceSource");

        tenant.MapPost("/sources", async (
                Guid organizationId,
                RecordSourceRequest request,
                IntelligenceSourceHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                IntelligenceSourceId id = await handler.HandleAsync(
                        new RecordSourceCommand(
                            new OrganizationId(organizationId),
                            EndpointParsing.ParseEnum<IntelligenceSourceKind>(
                                request.Kind, nameof(request.Kind)),
                            request.Title,
                            Sensitivity(request.Sensitivity),
                            request.DocumentVersionId is { } version
                                ? new DocumentVersionId(version)
                                : null,
                            request.MessageId is { } message
                                ? new CommunicationMessageId(message)
                                : null,
                            request.Url,
                            request.Publisher,
                            request.Author,
                            request.ExternalReference,
                            request.PublishedAt,
                            request.ObservedAt,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/intelligence/sources/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("RecordIntelligenceSource");

        tenant.MapPatch("/sources/{sourceId:guid}", async (
                Guid organizationId,
                Guid sourceId,
                UpdateSourceRequest request,
                IntelligenceSourceHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new UpdateSourceCommand(
                            new OrganizationId(organizationId),
                            new IntelligenceSourceId(sourceId),
                            request.Title,
                            Sensitivity(request.Sensitivity),
                            request.ExpectedVersion,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("UpdateIntelligenceSource");

        // Reliability is somebody's judgment about a source, recorded separately
        // from the source itself and never derived from what it says. A well
        // written rumour reads exactly like a well written fact (§1).
        tenant.MapPost("/sources/{sourceId:guid}/reliability", async (
                Guid organizationId,
                Guid sourceId,
                AssessSourceReliabilityRequest request,
                IntelligenceSourceHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new AssessSourceReliabilityCommand(
                            new OrganizationId(organizationId),
                            new IntelligenceSourceId(sourceId),
                            EndpointParsing.ParseEnum<SourceReliability>(
                                request.Reliability, nameof(request.Reliability)),
                            request.ExpectedVersion,
                            request.Rationale),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("AssessSourceReliability");
    }

    // -------------------------------------------------------------- signals

    private static void MapSignals(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/signals", async (
                Guid organizationId,
                IntelligenceQueryService queries,
                string? kind,
                string? verification,
                string? sensitivity,
                string? subjectKind,
                Guid? subjectId,
                Guid? sourceId,
                Guid? recordedBy,
                Guid? watchlistId,
                DateOnly? observedAfter,
                DateOnly? observedBefore,
                DateOnly? occurredAfter,
                DateOnly? occurredBefore,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                SignalFilter filter = new(
                    EndpointParsing.ParseNullableEnum<SignalKind>(kind, nameof(kind)),
                    EndpointParsing.ParseNullableEnum<SignalVerification>(
                        verification, nameof(verification)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSensitivity>(
                        sensitivity, nameof(sensitivity)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSubjectKind>(
                        subjectKind, nameof(subjectKind)),
                    subjectId,
                    sourceId is { } source ? new IntelligenceSourceId(source) : null,
                    recordedBy,
                    watchlistId,
                    observedAfter,
                    observedBefore,
                    occurredAfter,
                    occurredBefore,
                    Trimmed(search));

                IReadOnlyList<SignalSummaryModel> signals = await queries
                    .ListSignalsAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(signals.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("ListSignals");

        tenant.MapGet("/signals/{signalId:guid}", async (
                Guid organizationId,
                Guid signalId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                SignalDetailModel? signal = await queries
                    .GetSignalAsync(
                        new OrganizationId(organizationId),
                        new SignalId(signalId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return signal is null ? Results.NotFound() : Results.Ok(Map(signal));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetSignal");

        // At least one source, always. The requirement is in the aggregate, in the
        // handler and in a database trigger, because a claim with no provenance is
        // the one thing this milestone exists to refuse (§1).
        tenant.MapPost("/signals", async (
                Guid organizationId,
                RecordSignalRequest request,
                SignalHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                SignalId id = await handler.HandleAsync(
                        new RecordSignalCommand(
                            new OrganizationId(organizationId),
                            request.Title,
                            request.Claim,
                            EndpointParsing.ParseEnum<SignalKind>(
                                request.Kind, nameof(request.Kind)),
                            Sensitivity(request.Sensitivity),
                            ParseEvidence(request.Evidence),
                            ParseSubjects(request.Subjects),
                            request.OccurredAt,
                            request.ObservedAt,
                            EndpointParsing.ParseEnumOrDefault(
                                request.Confidence,
                                nameof(request.Confidence),
                                SignalConfidence.Unstated),
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/intelligence/signals/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("RecordSignal");

        tenant.MapPatch("/signals/{signalId:guid}", async (
                Guid organizationId,
                Guid signalId,
                UpdateSignalRequest request,
                SignalHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new UpdateSignalCommand(
                            new OrganizationId(organizationId),
                            new SignalId(signalId),
                            request.Title,
                            request.Claim,
                            EndpointParsing.ParseEnum<SignalKind>(
                                request.Kind, nameof(request.Kind)),
                            Sensitivity(request.Sensitivity),
                            EndpointParsing.ParseEnum<SignalConfidence>(
                                request.Confidence, nameof(request.Confidence)),
                            request.ExpectedVersion,
                            request.Notes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("UpdateSignal");

        // Unverified, Corroborated, Disputed or Retracted. There is deliberately no
        // Verified: corroboration means other evidence agrees, not that the claim
        // is true, and a word that reads as "true" would be read that way (§1).
        tenant.MapPost("/signals/{signalId:guid}/verification", async (
                Guid organizationId,
                Guid signalId,
                ChangeSignalVerificationRequest request,
                SignalHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ChangeSignalVerificationCommand(
                            new OrganizationId(organizationId),
                            new SignalId(signalId),
                            EndpointParsing.ParseEnum<SignalVerification>(
                                request.Verification, nameof(request.Verification)),
                            request.ExpectedVersion,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("ChangeSignalVerification");

        tenant.MapPost("/signals/{signalId:guid}/evidence", async (
                Guid organizationId,
                Guid signalId,
                LinkSignalEvidenceRequest request,
                SignalHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new LinkSignalEvidenceCommand(
                            new OrganizationId(organizationId),
                            new SignalId(signalId),
                            new IntelligenceSourceId(request.SourceId),
                            EndpointParsing.ParseEnum<SignalEvidenceRole>(
                                request.Role, nameof(request.Role)),
                            request.ExpectedVersion,
                            request.Excerpt,
                            request.Locator),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("LinkSignalEvidence");

        tenant.MapDelete("/signals/{signalId:guid}/evidence/{evidenceId:guid}", async (
                Guid organizationId,
                Guid signalId,
                Guid evidenceId,
                int expectedVersion,
                SignalHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                        new UnlinkSignalEvidenceCommand(
                            new OrganizationId(organizationId),
                            new SignalId(signalId),
                            evidenceId,
                            expectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("UnlinkSignalEvidence");

        tenant.MapPost("/signals/{signalId:guid}/subjects", async (
                Guid organizationId,
                Guid signalId,
                AddIntelligenceSubjectRequest request,
                SignalHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new AddSignalSubjectCommand(
                            new OrganizationId(organizationId),
                            new SignalId(signalId),
                            SubjectKind(request.Kind),
                            request.SubjectId,
                            request.ExpectedVersion,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("AddSignalSubject");

        tenant.MapDelete("/signals/{signalId:guid}/subjects/{subjectRowId:guid}", async (
                Guid organizationId,
                Guid signalId,
                Guid subjectRowId,
                int expectedVersion,
                SignalHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                        new RemoveSignalSubjectCommand(
                            new OrganizationId(organizationId),
                            new SignalId(signalId),
                            subjectRowId,
                            expectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("RemoveSignalSubject");
    }

    // --------------------------------------------------------------- theses

    private static void MapTheses(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/theses", async (
                Guid organizationId,
                IntelligenceQueryService queries,
                string? status,
                string? confidence,
                string? sensitivity,
                string? subjectKind,
                Guid? subjectId,
                Guid? ownerUserId,
                DateOnly? updatedAfter,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                ThesisFilter filter = new(
                    EndpointParsing.ParseNullableEnum<ThesisStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<ThesisConfidence>(
                        confidence, nameof(confidence)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSensitivity>(
                        sensitivity, nameof(sensitivity)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSubjectKind>(
                        subjectKind, nameof(subjectKind)),
                    subjectId,
                    ownerUserId,
                    updatedAfter,
                    Trimmed(search));

                IReadOnlyList<ThesisSummaryModel> theses = await queries
                    .ListThesesAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(theses.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("ListTheses");

        tenant.MapGet("/theses/{thesisId:guid}", async (
                Guid organizationId,
                Guid thesisId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ThesisDetailModel? thesis = await queries
                    .GetThesisAsync(
                        new OrganizationId(organizationId),
                        new ThesisId(thesisId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return thesis is null ? Results.NotFound() : Results.Ok(Map(thesis));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetThesis");

        tenant.MapPost("/theses", async (
                Guid organizationId,
                CreateThesisRequest request,
                ThesisHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ThesisId id = await handler.HandleAsync(
                        new CreateThesisCommand(
                            new OrganizationId(organizationId),
                            request.Title,
                            request.Proposition,
                            Sensitivity(request.Sensitivity),
                            request.OwnerUserId,
                            EndpointParsing.ParseEnumOrDefault(
                                request.Confidence,
                                nameof(request.Confidence),
                                ThesisConfidence.Unstated),
                            request.Rationale,
                            ParseSubjects(request.Subjects)),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/intelligence/theses/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("CreateThesis");

        tenant.MapPost("/theses/{thesisId:guid}/activate", async (
                Guid organizationId,
                Guid thesisId,
                ActivateThesisRequest request,
                ThesisHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ActivateThesisCommand(
                            new OrganizationId(organizationId),
                            new ThesisId(thesisId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("ActivateThesis");

        // A revision, never an edit. What the agency used to think is part of the
        // record, and a thesis that silently changed its mind is a thesis nobody
        // can hold anyone to (§10).
        tenant.MapPost("/theses/{thesisId:guid}/revisions", async (
                Guid organizationId,
                Guid thesisId,
                ReviseThesisRequest request,
                ThesisHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new ReviseThesisCommand(
                            new OrganizationId(organizationId),
                            new ThesisId(thesisId),
                            request.Proposition,
                            EndpointParsing.ParseEnum<ThesisConfidence>(
                                request.Confidence, nameof(request.Confidence)),
                            request.ExpectedVersion,
                            request.Rationale,
                            request.ChangeNote),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("ReviseThesis");

        tenant.MapPost("/theses/{thesisId:guid}/close", async (
                Guid organizationId,
                Guid thesisId,
                CloseThesisRequest request,
                ThesisHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new CloseThesisCommand(
                            new OrganizationId(organizationId),
                            new ThesisId(thesisId),
                            request.Reason,
                            request.ExpectedVersion,
                            request.SupersededBy is { } successor
                                ? new ThesisId(successor)
                                : null),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("CloseThesis");

        tenant.MapPost("/theses/{thesisId:guid}/evidence", async (
                Guid organizationId,
                Guid thesisId,
                LinkThesisEvidenceRequest request,
                ThesisHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new LinkThesisEvidenceCommand(
                            new OrganizationId(organizationId),
                            new ThesisId(thesisId),
                            new SignalId(request.SignalId),
                            EndpointParsing.ParseEnum<ThesisEvidenceStance>(
                                request.Stance, nameof(request.Stance)),
                            request.ExpectedVersion,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("LinkThesisEvidence");

        tenant.MapDelete("/theses/{thesisId:guid}/evidence/{evidenceId:guid}", async (
                Guid organizationId,
                Guid thesisId,
                Guid evidenceId,
                int expectedVersion,
                ThesisHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                        new UnlinkThesisEvidenceCommand(
                            new OrganizationId(organizationId),
                            new ThesisId(thesisId),
                            evidenceId,
                            expectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("UnlinkThesisEvidence");

        tenant.MapPost("/theses/{thesisId:guid}/subjects", async (
                Guid organizationId,
                Guid thesisId,
                AddIntelligenceSubjectRequest request,
                ThesisHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new AddThesisSubjectCommand(
                            new OrganizationId(organizationId),
                            new ThesisId(thesisId),
                            SubjectKind(request.Kind),
                            request.SubjectId,
                            request.ExpectedVersion,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("AddThesisSubject");
    }

    // ---------------------------------------------------------- predictions

    private static void MapPredictions(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/predictions", async (
                Guid organizationId,
                IntelligenceQueryService queries,
                string? status,
                string? outcome,
                string? sensitivity,
                string? subjectKind,
                Guid? subjectId,
                Guid? ownerUserId,
                DateOnly? resolvesAfter,
                DateOnly? resolvesBefore,
                decimal? probabilityAtLeast,
                decimal? probabilityAtMost,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                PredictionFilter filter = new(
                    EndpointParsing.ParseNullableEnum<PredictionStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<PredictionOutcome>(outcome, nameof(outcome)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSensitivity>(
                        sensitivity, nameof(sensitivity)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSubjectKind>(
                        subjectKind, nameof(subjectKind)),
                    subjectId,
                    ownerUserId,
                    resolvesAfter,
                    resolvesBefore,
                    probabilityAtLeast,
                    probabilityAtMost,
                    Trimmed(search));

                IReadOnlyList<PredictionSummaryModel> predictions = await queries
                    .ListPredictionsAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(predictions.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("ListPredictions");

        // Arithmetic and a sample count. No verdict, no label, no grade: a mean
        // Brier score over eleven predictions supports very little, and the count
        // travels beside it so a reader can see that (§14).
        tenant.MapGet("/predictions/calibration", async (
                Guid organizationId,
                IntelligenceQueryService queries,
                Guid? ownerUserId,
                DateOnly? resolvedAfter,
                DateOnly? resolvedBefore,
                CancellationToken cancellationToken) =>
            {
                PredictionCalibrationModel calibration = await queries
                    .GetCalibrationAsync(
                        new OrganizationId(organizationId),
                        ownerUserId,
                        resolvedAfter,
                        resolvedBefore,
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(Map(calibration));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetPredictionCalibration");

        tenant.MapGet("/predictions/{predictionId:guid}", async (
                Guid organizationId,
                Guid predictionId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                PredictionDetailModel? prediction = await queries
                    .GetPredictionAsync(
                        new OrganizationId(organizationId),
                        new PredictionId(predictionId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return prediction is null ? Results.NotFound() : Results.Ok(Map(prediction));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetPrediction");

        tenant.MapPost("/predictions", async (
                Guid organizationId,
                CreatePredictionRequest request,
                PredictionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                PredictionId id = await handler.HandleAsync(
                        new CreatePredictionCommand(
                            new OrganizationId(organizationId),
                            request.Statement,
                            request.ResolvesBy,
                            request.Probability,
                            Sensitivity(request.Sensitivity),
                            request.OwnerUserId,
                            request.ResolutionCriteria,
                            request.Rationale,
                            ParseSubjects(request.Subjects)),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/intelligence/predictions/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligencePredictionsWrite))
            .WithName("CreatePrediction");

        tenant.MapPost("/predictions/{predictionId:guid}/revisions", async (
                Guid organizationId,
                Guid predictionId,
                RecordPredictionRevisionRequest request,
                PredictionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new RecordPredictionRevisionCommand(
                            new OrganizationId(organizationId),
                            new PredictionId(predictionId),
                            request.Probability,
                            request.ExpectedVersion,
                            request.Rationale),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligencePredictionsWrite))
            .WithName("RecordPredictionRevision");

        tenant.MapPost("/predictions/{predictionId:guid}/resolve", async (
                Guid organizationId,
                Guid predictionId,
                ResolvePredictionRequest request,
                PredictionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ResolvePredictionCommand(
                            new OrganizationId(organizationId),
                            new PredictionId(predictionId),
                            EndpointParsing.ParseEnum<PredictionOutcome>(
                                request.Outcome, nameof(request.Outcome)),
                            request.ExpectedVersion,
                            request.Note,
                            ParsePredictionEvidence(request.Evidence)),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligencePredictionsWrite))
            .WithName("ResolvePrediction");

        tenant.MapPost("/predictions/{predictionId:guid}/cancel", async (
                Guid organizationId,
                Guid predictionId,
                CancelPredictionRequest request,
                PredictionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new CancelPredictionCommand(
                            new OrganizationId(organizationId),
                            new PredictionId(predictionId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligencePredictionsWrite))
            .WithName("CancelPrediction");

        tenant.MapPost("/predictions/{predictionId:guid}/subjects", async (
                Guid organizationId,
                Guid predictionId,
                AddIntelligenceSubjectRequest request,
                PredictionHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new AddPredictionSubjectCommand(
                            new OrganizationId(organizationId),
                            new PredictionId(predictionId),
                            SubjectKind(request.Kind),
                            request.SubjectId,
                            request.ExpectedVersion,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligencePredictionsWrite))
            .WithName("AddPredictionSubject");
    }

    // ----------------------------------------------------------- watchlists

    private static void MapWatchlists(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/watchlists", async (
                Guid organizationId,
                IntelligenceQueryService queries,
                string? status,
                string? sensitivity,
                Guid? ownerUserId,
                string? subjectKind,
                Guid? subjectId,
                DateOnly? reviewedBefore,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                WatchlistFilter filter = new(
                    EndpointParsing.ParseNullableEnum<WatchlistStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSensitivity>(
                        sensitivity, nameof(sensitivity)),
                    ownerUserId,
                    EndpointParsing.ParseNullableEnum<IntelligenceSubjectKind>(
                        subjectKind, nameof(subjectKind)),
                    subjectId,
                    reviewedBefore,
                    Trimmed(search));

                IReadOnlyList<WatchlistSummaryModel> watchlists = await queries
                    .ListWatchlistsAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(watchlists.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("ListWatchlists");

        tenant.MapGet("/watchlists/{watchlistId:guid}", async (
                Guid organizationId,
                Guid watchlistId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                WatchlistDetailModel? watchlist = await queries
                    .GetWatchlistAsync(
                        new OrganizationId(organizationId),
                        new WatchlistId(watchlistId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return watchlist is null ? Results.NotFound() : Results.Ok(Map(watchlist));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetWatchlist");

        // What has happened around the things this list watches, derived by subject
        // overlap at read time and narrowed to what the reader may see. Opening a
        // watchlist is not a grant to the intelligence about its members (§28).
        tenant.MapGet("/watchlists/{watchlistId:guid}/activity", async (
                Guid organizationId,
                Guid watchlistId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                WatchlistActivityModel? activity = await queries
                    .GetWatchlistActivityAsync(
                        new OrganizationId(organizationId),
                        new WatchlistId(watchlistId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return activity is null ? Results.NotFound() : Results.Ok(Map(activity));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetWatchlistActivity");

        tenant.MapPost("/watchlists", async (
                Guid organizationId,
                CreateWatchlistRequest request,
                WatchlistHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                WatchlistId id = await handler.HandleAsync(
                        new CreateWatchlistCommand(
                            new OrganizationId(organizationId),
                            request.Name,
                            Sensitivity(request.Sensitivity),
                            request.OwnerUserId,
                            request.Purpose,
                            ParseSubjects(request.Entries)),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/intelligence/watchlists/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("CreateWatchlist");

        tenant.MapPatch("/watchlists/{watchlistId:guid}", async (
                Guid organizationId,
                Guid watchlistId,
                UpdateWatchlistRequest request,
                WatchlistHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new UpdateWatchlistCommand(
                            new OrganizationId(organizationId),
                            new WatchlistId(watchlistId),
                            request.Name,
                            Sensitivity(request.Sensitivity),
                            request.ExpectedVersion,
                            request.Purpose),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("UpdateWatchlist");

        tenant.MapPost("/watchlists/{watchlistId:guid}/entries", async (
                Guid organizationId,
                Guid watchlistId,
                AddIntelligenceSubjectRequest request,
                WatchlistHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new AddWatchlistEntryCommand(
                            new OrganizationId(organizationId),
                            new WatchlistId(watchlistId),
                            SubjectKind(request.Kind),
                            request.SubjectId,
                            request.ExpectedVersion,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("AddWatchlistEntry");

        tenant.MapDelete("/watchlists/{watchlistId:guid}/entries/{entryId:guid}", async (
                Guid organizationId,
                Guid watchlistId,
                Guid entryId,
                int expectedVersion,
                WatchlistHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                        new RemoveWatchlistEntryCommand(
                            new OrganizationId(organizationId),
                            new WatchlistId(watchlistId),
                            entryId,
                            expectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("RemoveWatchlistEntry");

        // "Somebody looked at this on this date." A watchlist nobody reviews is a
        // list that has quietly stopped being watched, and the only way to tell is
        // to record the looking.
        tenant.MapPost("/watchlists/{watchlistId:guid}/review", async (
                Guid organizationId,
                Guid watchlistId,
                RecordReviewRequest request,
                WatchlistHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new RecordWatchlistReviewCommand(
                            new OrganizationId(organizationId),
                            new WatchlistId(watchlistId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("RecordWatchlistReview");

        tenant.MapPost("/watchlists/{watchlistId:guid}/archive", async (
                Guid organizationId,
                Guid watchlistId,
                ArchiveWatchlistRequest request,
                WatchlistHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ArchiveWatchlistCommand(
                            new OrganizationId(organizationId),
                            new WatchlistId(watchlistId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("ArchiveWatchlist");
    }

    // ---------------------------------------------------------------- radar

    private static void MapRadar(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/radar", async (
                Guid organizationId,
                IntelligenceQueryService queries,
                string? status,
                string? priority,
                string? sensitivity,
                Guid? ownerUserId,
                Guid? personId,
                Guid? watchlistId,
                string? discipline,
                DateOnly? reviewedBefore,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                TalentRadarFilter filter = new(
                    EndpointParsing.ParseNullableEnum<TalentRadarStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<TalentRadarPriority>(
                        priority, nameof(priority)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSensitivity>(
                        sensitivity, nameof(sensitivity)),
                    ownerUserId,
                    personId,
                    watchlistId,
                    Trimmed(discipline),
                    reviewedBefore,
                    Trimmed(search));

                IReadOnlyList<TalentRadarSummaryModel> entries = await queries
                    .ListRadarAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(entries.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("ListTalentRadar");

        tenant.MapGet("/radar/{entryId:guid}", async (
                Guid organizationId,
                Guid entryId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                TalentRadarDetailModel? entry = await queries
                    .GetRadarEntryAsync(
                        new OrganizationId(organizationId),
                        new TalentRadarEntryId(entryId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return entry is null ? Results.NotFound() : Results.Ok(Map(entry));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetTalentRadarEntry");

        tenant.MapPost("/radar", async (
                Guid organizationId,
                CreateRadarEntryRequest request,
                TalentRadarHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                TalentRadarEntryId id = await handler.HandleAsync(
                        new CreateRadarEntryCommand(
                            new OrganizationId(organizationId),
                            new PersonId(request.PersonId),
                            request.Rationale,
                            Sensitivity(request.Sensitivity),
                            request.OwnerUserId,
                            request.IntendedDisciplines,
                            EndpointParsing.ParseEnumOrDefault(
                                request.Priority,
                                nameof(request.Priority),
                                TalentRadarPriority.Unassigned),
                            request.FirstObservedAt),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/intelligence/radar/{id.Value}",
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRadarWrite))
            .WithName("CreateTalentRadarEntry");

        tenant.MapPatch("/radar/{entryId:guid}", async (
                Guid organizationId,
                Guid entryId,
                UpdateRadarEntryRequest request,
                TalentRadarHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new UpdateRadarEntryCommand(
                            new OrganizationId(organizationId),
                            new TalentRadarEntryId(entryId),
                            request.Rationale,
                            Sensitivity(request.Sensitivity),
                            EndpointParsing.ParseEnum<TalentRadarPriority>(
                                request.Priority, nameof(request.Priority)),
                            request.ExpectedVersion,
                            request.IntendedDisciplines),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRadarWrite))
            .WithName("UpdateTalentRadarEntry");

        tenant.MapPost("/radar/{entryId:guid}/status", async (
                Guid organizationId,
                Guid entryId,
                ChangeRadarStatusRequest request,
                TalentRadarHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ChangeRadarStatusCommand(
                            new OrganizationId(organizationId),
                            new TalentRadarEntryId(entryId),
                            EndpointParsing.ParseEnum<TalentRadarStatus>(
                                request.Status, nameof(request.Status)),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRadarWrite))
            .WithName("ChangeTalentRadarStatus");

        tenant.MapPost("/radar/{entryId:guid}/review", async (
                Guid organizationId,
                Guid entryId,
                RecordReviewRequest request,
                TalentRadarHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new RecordRadarReviewCommand(
                            new OrganizationId(organizationId),
                            new TalentRadarEntryId(entryId),
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRadarWrite))
            .WithName("RecordTalentRadarReview");

        tenant.MapPost("/radar/{entryId:guid}/dismiss", async (
                Guid organizationId,
                Guid entryId,
                DismissRadarEntryRequest request,
                TalentRadarHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new DismissRadarEntryCommand(
                            new OrganizationId(organizationId),
                            new TalentRadarEntryId(entryId),
                            request.Reason,
                            request.ExpectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRadarWrite))
            .WithName("DismissTalentRadarEntry");

        // The seam. The radar stops at "we are pursuing this person" and hands the
        // pursuit to M4, because courting, signing and representation already live
        // there and two pipelines would disagree about the same relationship (§40).
        tenant.MapPost("/radar/{entryId:guid}/convert", async (
                Guid organizationId,
                Guid entryId,
                ConvertRadarEntryRequest request,
                TalentRadarHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                RadarConversionResult result = await handler.HandleAsync(
                        new ConvertRadarEntryToProspectCommand(
                            new OrganizationId(organizationId),
                            new TalentRadarEntryId(entryId),
                            request.ExpectedVersion,
                            request.ProspectOwnerUserId,
                            request.IdentifiedOn,
                            request.Source,
                            request.StrategyNotes),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new RadarConversionResponse(
                    result.ProspectId.Value,
                    result.TalentProfileId.Value,
                    result.CreatedTalentProfile));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRadarWrite))
            .WithName("ConvertTalentRadarEntry");
    }

    // ------------------------------------------------------- research cases

    private static void MapResearchCases(RouteGroupBuilder tenant)
    {
        tenant.MapGet("/research-cases", async (
                Guid organizationId,
                IntelligenceQueryService queries,
                string? status,
                string? sensitivity,
                Guid? ownerUserId,
                string? subjectKind,
                Guid? subjectId,
                string? search,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                ResearchCaseFilter filter = new(
                    EndpointParsing.ParseNullableEnum<ResearchCaseStatus>(status, nameof(status)),
                    EndpointParsing.ParseNullableEnum<IntelligenceSensitivity>(
                        sensitivity, nameof(sensitivity)),
                    ownerUserId,
                    EndpointParsing.ParseNullableEnum<IntelligenceSubjectKind>(
                        subjectKind, nameof(subjectKind)),
                    subjectId,
                    Trimmed(search));

                IReadOnlyList<ResearchCaseSummaryModel> cases = await queries
                    .ListResearchCasesAsync(
                        new OrganizationId(organizationId), filter, limit, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(cases.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("ListResearchCases");

        tenant.MapGet("/research-cases/{researchCaseId:guid}", async (
                Guid organizationId,
                Guid researchCaseId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                ResearchCaseDetailModel? researchCase = await queries
                    .GetResearchCaseAsync(
                        new OrganizationId(organizationId),
                        new ResearchCaseId(researchCaseId),
                        cancellationToken)
                    .ConfigureAwait(false);

                return researchCase is null
                    ? Results.NotFound()
                    : Results.Ok(Map(researchCase));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetResearchCase");

        tenant.MapPost("/research-cases", async (
                Guid organizationId,
                OpenResearchCaseRequest request,
                ResearchCaseHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                ResearchCaseId id = await handler.HandleAsync(
                        new OpenResearchCaseCommand(
                            new OrganizationId(organizationId),
                            request.Question,
                            Sensitivity(request.Sensitivity),
                            request.OwnerUserId,
                            request.Context,
                            ParseSubjects(request.Subjects)),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{organizationId}/intelligence/research-cases/"
                        + id.Value,
                    new { id = id.Value });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("OpenResearchCase");

        tenant.MapPatch("/research-cases/{researchCaseId:guid}", async (
                Guid organizationId,
                Guid researchCaseId,
                UpdateResearchCaseRequest request,
                ResearchCaseHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new UpdateResearchCaseCommand(
                            new OrganizationId(organizationId),
                            new ResearchCaseId(researchCaseId),
                            request.Question,
                            Sensitivity(request.Sensitivity),
                            request.ExpectedVersion,
                            request.Context),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("UpdateResearchCase");

        tenant.MapPost("/research-cases/{researchCaseId:guid}/status", async (
                Guid organizationId,
                Guid researchCaseId,
                ChangeResearchCaseStatusRequest request,
                ResearchCaseHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                await handler.HandleAsync(
                        new ChangeResearchCaseStatusCommand(
                            new OrganizationId(organizationId),
                            new ResearchCaseId(researchCaseId),
                            EndpointParsing.ParseEnum<ResearchCaseStatus>(
                                request.Status, nameof(request.Status)),
                            request.ExpectedVersion,
                            request.Conclusion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("ChangeResearchCaseStatus");

        tenant.MapPost("/research-cases/{researchCaseId:guid}/links", async (
                Guid organizationId,
                Guid researchCaseId,
                LinkResearchItemRequest request,
                ResearchCaseHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new LinkResearchItemCommand(
                            new OrganizationId(organizationId),
                            new ResearchCaseId(researchCaseId),
                            EndpointParsing.ParseEnum<ResearchLinkKind>(
                                request.Kind, nameof(request.Kind)),
                            request.LinkedId,
                            request.ExpectedVersion,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("LinkResearchItem");

        tenant.MapDelete("/research-cases/{researchCaseId:guid}/links/{linkId:guid}", async (
                Guid organizationId,
                Guid researchCaseId,
                Guid linkId,
                int expectedVersion,
                ResearchCaseHandler handler,
                CancellationToken cancellationToken) =>
            {
                await handler.HandleAsync(
                        new UnlinkResearchItemCommand(
                            new OrganizationId(organizationId),
                            new ResearchCaseId(researchCaseId),
                            linkId,
                            expectedVersion),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.NoContent();
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("UnlinkResearchItem");

        tenant.MapPost("/research-cases/{researchCaseId:guid}/subjects", async (
                Guid organizationId,
                Guid researchCaseId,
                AddIntelligenceSubjectRequest request,
                ResearchCaseHandler handler,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);

                Guid id = await handler.HandleAsync(
                        new AddResearchSubjectCommand(
                            new OrganizationId(organizationId),
                            new ResearchCaseId(researchCaseId),
                            SubjectKind(request.Kind),
                            request.SubjectId,
                            request.ExpectedVersion,
                            request.Note),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new { id });
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceWrite))
            .WithName("AddResearchSubject");
    }

    // -------------------------------------------------------------- derived

    private static void MapDerived(RouteGroupBuilder tenant)
    {
        // Dimensions, never a score. There is no RelationshipHealth here and no
        // Affinity: a composite number would be arithmetic over incommensurable
        // things, and its apparent precision would be believed (§18).
        tenant.MapGet("/relationships/{kind}/{subjectId:guid}", async (
                Guid organizationId,
                string kind,
                Guid subjectId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                RelationshipIntelligenceModel? relationship = await queries
                    .GetRelationshipIntelligenceAsync(
                        new OrganizationId(organizationId),
                        SubjectKind(kind),
                        subjectId,
                        cancellationToken)
                    .ConfigureAwait(false);

                return relationship is null
                    ? Results.NotFound()
                    : Results.Ok(Map(relationship));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetRelationshipIntelligence");

        tenant.MapGet("/command-center", async (
                Guid organizationId,
                IntelligenceQueryService queries,
                CancellationToken cancellationToken) =>
            {
                IntelligenceCommandCenterModel model = await queries
                    .GetCommandCenterAsync(new OrganizationId(organizationId), cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(Map(model));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.IntelligenceRead))
            .WithName("GetIntelligenceCommandCenter");
    }

    // -------------------------------------------------------------- parsing

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IntelligenceSensitivity Sensitivity(string value) =>
        EndpointParsing.ParseEnum<IntelligenceSensitivity>(value, "sensitivity");

    private static IntelligenceSubjectKind SubjectKind(string value) =>
        EndpointParsing.ParseEnum<IntelligenceSubjectKind>(value, "kind");

    private static IReadOnlyList<SubjectInput>? ParseSubjects(
        IReadOnlyList<IntelligenceSubjectRequest>? subjects) =>
        subjects is null
            ? null
            : [.. subjects.Select(x => new SubjectInput(
                SubjectKind(x.Kind), x.SubjectId, x.Note))];

    private static IReadOnlyList<SignalEvidenceInput> ParseEvidence(
        IReadOnlyList<SignalEvidenceRequest> evidence) =>
        evidence is null
            ? []
            : [.. evidence.Select(x => new SignalEvidenceInput(
                new IntelligenceSourceId(x.SourceId),
                EndpointParsing.ParseEnumOrDefault(
                    x.Role, nameof(x.Role), SignalEvidenceRole.Primary),
                x.Excerpt,
                x.Locator))];

    private static IReadOnlyList<PredictionEvidenceInput>? ParsePredictionEvidence(
        IReadOnlyList<PredictionEvidenceRequest>? evidence) =>
        evidence is null
            ? null
            : [.. evidence.Select(x => new PredictionEvidenceInput(
                x.SourceId is { } source ? new IntelligenceSourceId(source) : null,
                x.SignalId is { } signal ? new SignalId(signal) : null,
                x.Note))];

    // -------------------------------------------------------------- mapping

    private static IntelligenceSubjectResponse Map(IntelligenceSubjectModel subject) =>
        new(subject.Id, subject.Kind.ToString(), subject.SubjectId, subject.Label, subject.Note);

    private static IntelligenceSourceResponse Map(IntelligenceSourceModel source) =>
        new(
            source.Id.Value,
            source.Kind.ToString(),
            source.Title,
            source.DocumentVersionId,
            source.MessageId,
            source.Url,
            source.Publisher,
            source.Author,
            source.ExternalReference,
            source.PublishedAt,
            source.ObservedAt,
            source.RecordedAt,
            source.Reliability.ToString(),
            source.ReliabilityRationale,
            source.ReliabilityAssessedByDisplayName,
            source.ReliabilityAssessedAt,
            source.Sensitivity.ToString(),
            source.Notes,
            source.IsHeldByAgencyOS,
            source.RecordedByDisplayName,
            source.SignalCount,
            source.Version);

    internal static SignalResponse Map(SignalSummaryModel signal) =>
        new(
            signal.Id.Value,
            signal.Title,
            signal.Claim,
            signal.Kind.ToString(),
            signal.Verification.ToString(),
            signal.Confidence.ToString(),
            signal.Sensitivity.ToString(),
            signal.OccurredAt,
            signal.ObservedAt,
            signal.RecordedAt,
            signal.RecordedByDisplayName,
            [.. signal.Subjects.Select(Map)],
            signal.EvidenceCount,
            signal.Version);

    private static SignalDetailResponse Map(SignalDetailModel signal) =>
        new(
            Map(signal.Signal),
            signal.Notes,
            signal.VerificationNote,
            signal.VerificationChangedByDisplayName,
            signal.VerificationChangedAt,
            [
                .. signal.Evidence.Select(x => new SignalEvidenceResponse(
                    x.Id,
                    x.SourceId.Value,
                    x.SourceTitle,
                    x.SourceKind.ToString(),
                    x.SourceReliability.ToString(),
                    x.Role.ToString(),
                    x.Excerpt,
                    x.ExcerptWithheld,
                    x.Locator,
                    x.AddedAt,
                    x.AddedByDisplayName)),
            ],
            [.. signal.History.Select(Map)]);

    internal static ThesisResponse Map(ThesisSummaryModel thesis) =>
        new(
            thesis.Id.Value,
            thesis.Title,
            thesis.Proposition,
            thesis.Status.ToString(),
            thesis.Confidence.ToString(),
            thesis.Sensitivity.ToString(),
            thesis.OwnerDisplayName,
            thesis.CreatedAt,
            thesis.UpdatedAt,
            [.. thesis.Subjects.Select(Map)],
            thesis.RevisionCount,
            thesis.SupportingCount,
            thesis.ChallengingCount,
            thesis.Version);

    private static ThesisDetailResponse Map(ThesisDetailModel thesis) =>
        new(
            Map(thesis.Thesis),
            thesis.Rationale,
            thesis.ClosedReason,
            thesis.ClosedAt,
            thesis.SupersededByThesisId,
            [
                .. thesis.Revisions.Select(x => new ThesisRevisionResponse(
                    x.Id,
                    x.Sequence,
                    x.Proposition,
                    x.Rationale,
                    x.Confidence.ToString(),
                    x.ChangeNote,
                    x.RecordedAt,
                    x.RecordedByDisplayName)),
            ],
            [
                .. thesis.Evidence.Select(x => new ThesisEvidenceResponse(
                    x.Id,
                    x.SignalId.Value,
                    x.SignalTitle,
                    x.SignalVerification.ToString(),
                    x.Stance.ToString(),
                    x.Note,
                    x.AddedAt,
                    x.AddedByDisplayName)),
            ],
            [.. thesis.History.Select(Map)]);

    internal static PredictionResponse Map(PredictionSummaryModel prediction) =>
        new(
            prediction.Id.Value,
            prediction.Statement,
            prediction.ResolvesBy,
            prediction.Status.ToString(),
            prediction.CurrentProbability,
            prediction.CurrentProbabilityAsOf,
            prediction.CurrentProbabilityByDisplayName,
            prediction.Outcome?.ToString(),
            prediction.ResolvedAt,
            prediction.BrierScore,
            prediction.Sensitivity.ToString(),
            prediction.OwnerDisplayName,
            prediction.CreatedAt,
            [.. prediction.Subjects.Select(Map)],
            prediction.RevisionCount,
            prediction.Version);

    private static PredictionDetailResponse Map(PredictionDetailModel prediction) =>
        new(
            Map(prediction.Prediction),
            prediction.ResolutionCriteria,
            prediction.ResolutionNote,
            prediction.ResolvedByDisplayName,
            prediction.CancelledReason,
            prediction.CancelledAt,
            [
                .. prediction.Revisions.Select(x => new PredictionRevisionResponse(
                    x.Id,
                    x.Sequence,
                    x.Probability,
                    x.Rationale,
                    x.RecordedAt,
                    x.RecordedByDisplayName)),
            ],
            [
                .. prediction.Evidence.Select(x => new PredictionEvidenceResponse(
                    x.Id,
                    x.SourceId?.Value,
                    x.SignalId?.Value,
                    x.Label,
                    x.Note,
                    x.AddedAt,
                    x.AddedByDisplayName)),
            ],
            [.. prediction.History.Select(Map)]);

    private static PredictionCalibrationResponse Map(PredictionCalibrationModel calibration) =>
        new(
            calibration.ResolvedCount,
            calibration.YesCount,
            calibration.NoCount,
            calibration.UnresolvableCount,
            calibration.OpenCount,
            calibration.MeanBrierScore,
            calibration.MeanProbability,
            calibration.ObservedFrequency);

    private static WatchlistResponse Map(WatchlistSummaryModel watchlist) =>
        new(
            watchlist.Id.Value,
            watchlist.Name,
            watchlist.Purpose,
            watchlist.Status.ToString(),
            watchlist.Sensitivity.ToString(),
            watchlist.OwnerDisplayName,
            watchlist.LastReviewedAt,
            watchlist.LastReviewedByDisplayName,
            watchlist.CreatedAt,
            watchlist.EntryCount,
            watchlist.Version);

    private static WatchlistDetailResponse Map(WatchlistDetailModel watchlist) =>
        new(
            Map(watchlist.Watchlist),
            [.. watchlist.Entries.Select(Map)],
            [.. watchlist.History.Select(Map)]);

    private static WatchlistActivityResponse Map(WatchlistActivityModel activity) =>
        new(
            Map(activity.Watchlist),
            [.. activity.RecentSignals.Select(Map)],
            activity.SignalsSinceLastReview,
            [.. activity.ActiveTheses.Select(Map)],
            [.. activity.OpenPredictions.Select(Map)],
            activity.NewestSignalAt);

    internal static TalentRadarResponse Map(TalentRadarSummaryModel entry) =>
        new(
            entry.Id.Value,
            entry.PersonId,
            entry.PersonDisplayName,
            entry.PersonTitle,
            entry.CompanyName,
            entry.Status.ToString(),
            entry.Priority.ToString(),
            entry.Rationale,
            entry.IntendedDisciplines,
            entry.Sensitivity.ToString(),
            entry.OwnerDisplayName,
            entry.FirstObservedAt,
            entry.LastReviewedAt,
            entry.ProspectId,
            entry.SignalCount,
            entry.Version);

    private static TalentRadarDetailResponse Map(TalentRadarDetailModel entry) =>
        new(
            Map(entry.Entry),
            entry.DismissedReason,
            entry.ConvertedAt,
            entry.ConvertedByDisplayName,
            [
                .. entry.Credits.Select(x => new RadarCreditResponse(
                    x.Id, x.Title, x.Role, x.Kind, x.Year)),
            ],
            entry.Disciplines,
            [.. entry.Signals.Select(Map)],
            [.. entry.Theses.Select(Map)],
            [.. entry.Predictions.Select(Map)],
            [.. entry.Watchlists.Select(Map)],
            [.. entry.History.Select(Map)]);

    private static ResearchCaseResponse Map(ResearchCaseSummaryModel researchCase) =>
        new(
            researchCase.Id.Value,
            researchCase.Question,
            researchCase.Status.ToString(),
            researchCase.Sensitivity.ToString(),
            researchCase.OwnerDisplayName,
            researchCase.OpenedAt,
            researchCase.ClosedAt,
            [.. researchCase.Subjects.Select(Map)],
            researchCase.SourceCount,
            researchCase.SignalCount,
            researchCase.ThesisCount,
            researchCase.PredictionCount,
            researchCase.TaskCount,
            researchCase.OpenTaskCount,
            researchCase.Version);

    private static ResearchCaseDetailResponse Map(ResearchCaseDetailModel researchCase) =>
        new(
            Map(researchCase.ResearchCase),
            researchCase.Context,
            researchCase.Conclusion,
            [.. researchCase.Sources.Select(Map)],
            [.. researchCase.Signals.Select(Map)],
            [.. researchCase.Theses.Select(Map)],
            [.. researchCase.Predictions.Select(Map)],
            [
                .. researchCase.Tasks.Select(x => new ResearchTaskResponse(
                    x.Id, x.Title, x.State, x.DueAt, x.IsOverdue, x.AssignedToDisplayName)),
            ],
            [.. researchCase.History.Select(Map)]);

    private static RelationshipIntelligenceResponse Map(RelationshipIntelligenceModel model) =>
        new(
            model.SubjectId,
            model.Kind.ToString(),
            model.DisplayName,
            model.Title,
            model.CompanyName,
            model.RecordedStrength,
            model.RecordedRelationshipNote,
            model.RelationshipOwnerDisplayName,
            model.LastInteractionAt,
            model.LastInteractionKind,
            model.DaysSinceLastInteraction,
            model.Interactions30Days,
            model.Interactions90Days,
            model.Interactions365Days,
            model.RecentInteractionKinds,
            model.OpenTaskCount,
            model.OverdueTaskCount,
            model.NextTaskDueAt,
            [.. model.RecentSignals.Select(Map)],
            [.. model.Watchlists.Select(Map)],
            [.. model.ActiveTheses.Select(Map)],
            [.. model.OpenPredictions.Select(Map)]);

    private static IntelligenceCommandCenterResponse Map(IntelligenceCommandCenterModel model) =>
        new(
            [.. model.PredictionsAwaitingResolution.Select(Map)],
            [.. model.PredictionsDueSoon.Select(Map)],
            [.. model.DisputedSignals.Select(Map)],
            [.. model.RadarAwaitingReview.Select(Map)],
            [.. model.WatchlistsWithNewActivity.Select(Map)],
            [.. model.ResearchCasesWithOverdueTasks.Select(Map)],
            model.AwaitingResolutionCount,
            model.DueSoonCount,
            model.DisputedSignalCount,
            model.RadarAwaitingReviewCount);

    private static IntelligenceEventResponse Map(IntelligenceEventModel entry) =>
        new(
            entry.OccurredAt,
            entry.Kind.ToString(),
            entry.Summary,
            entry.Detail,
            entry.ActorDisplayName);
}
