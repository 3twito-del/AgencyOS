using AgencyOS.Api.Authorization;
using AgencyOS.Api.Provisioning;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Memberships;
using AgencyOS.Application.Organizations;
using AgencyOS.Application.Provisioning;
using AgencyOS.Application.Releases;
using AgencyOS.Contracts.Audit;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.Provisioning;
using AgencyOS.Contracts.Releases;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Provisioning;
using AgencyOS.Domain.Releases;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// The versioned M1 API surface.
/// </summary>
/// <remarks>
/// Routed under <c>/api/v1</c>. The version in the path is the coarse contract
/// boundary; <c>ApiContract.Current</c> is the fine one negotiated in the
/// handshake (<c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>).
/// </remarks>
internal static class ApiEndpoints
{
    /// <summary>Maps the versioned API surface.</summary>
    /// <param name="app">Route builder.</param>
    /// <param name="bootstrapGate">
    /// The first-run token gate, or <see langword="null"/> when no bootstrap token
    /// is configured. When null the bootstrap route is never mapped, so a
    /// deployment that has not deliberately enabled first-run initialization has
    /// no such endpoint at all.
    /// </param>
    public static IEndpointRouteBuilder MapAgencyOsApi(
        this IEndpointRouteBuilder app,
        BootstrapTokenGate? bootstrapGate)
    {
        RouteGroupBuilder api = app.MapGroup("/api/v1");

        MapSystem(api, bootstrapGate);
        MapRelease(api);
        MapOrganizations(api);
        MapAudit(api);

        // The M2 people slice, routed beneath the tenant that owns the records.
        PeopleSliceEndpoints.MapPeopleSlice(api);

        return app;
    }

    private static void MapSystem(RouteGroupBuilder api, BootstrapTokenGate? bootstrapGate)
    {
        // Anonymous and always available: a client needs to know whether to offer
        // first-run setup, and the answer is a single boolean that reveals nothing
        // an unauthenticated caller could act on.
        api.MapGet("/system/status", async (
                ISystemInitializationRepository initialization,
                CancellationToken cancellationToken) =>
            {
                bool initialized = await initialization
                    .IsInitializedAsync(cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new SystemStatusResponse(initialized));
            })
            .AllowAnonymous()
            .WithName("GetSystemStatus");

        if (bootstrapGate is null)
        {
            // No token configured: the route does not exist.
            return;
        }

        api.MapPost("/system/bootstrap", async (
                HttpRequest request,
                BootstrapRequest body,
                BootstrapSystemHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (!bootstrapGate.IsSatisfiedBy(request))
                {
                    // Deliberately indistinguishable from a wrong token: no hint
                    // about whether the system is already initialized is given to a
                    // caller that has not proved possession of the token.
                    return Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Bootstrap token required",
                        detail: $"Present a valid {BootstrapHeaders.Token} header.");
                }

                OrganizationType type = ParseEnum<OrganizationType>(
                    body.OrganizationType,
                    nameof(body.OrganizationType));

                BootstrapSystemResult result = await handler
                    .HandleAsync(
                        new BootstrapSystemCommand(
                            body.OrganizationName,
                            body.OrganizationLegalName,
                            type,
                            body.OwnerSubject,
                            body.OwnerDisplayName,
                            body.OwnerEmail),
                        cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created(
                    $"/api/v1/organizations/{result.OrganizationId.Value}",
                    new BootstrapResponse(
                        result.OrganizationId.Value,
                        result.OwnerUserId.Value,
                        result.MembershipId.Value,
                        result.InitializedAt,
                        result.ReleasePolicyPlatform,
                        result.ReleasePolicyRing,
                        result.ReleasePolicyVersion));
            })
            .AllowAnonymous()
            .WithName("BootstrapSystem");
    }

    private static void MapRelease(RouteGroupBuilder api)
    {
        // Anonymous by necessity: a client must be able to learn that it is
        // revoked without first being trusted.
        api.MapPost("/release/handshake", async (
                HandshakeRequest request,
                ClientCompatibilityService compatibility,
                CancellationToken cancellationToken) =>
            {
                ClientIdentity? identity = null;

                if (ReleaseRingNames.TryParse(request.Channel, out ReleaseRing ring)
                    && ClientVersion.TryParse(request.ClientVersion, out ClientVersion? version))
                {
                    identity = new ClientIdentity(
                        request.Platform,
                        ring,
                        version,
                        request.ApiContractVersion,
                        request.BuildId,
                        request.GitCommit,
                        request.LocalSchemaVersion,
                        request.WindowsBuild);
                }

                ReleaseDecision decision = await compatibility
                    .EvaluateAsync(identity, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(new HandshakeResponse(
                    Platform: request.Platform,
                    Channel: request.Channel,
                    Policy: decision.Policy.ToString().ToUpperInvariant(),
                    Reason: decision.Reason,
                    BlocksProtectedMutations: decision.BlocksProtectedMutations,
                    LatestVersion: decision.LatestVersion,
                    MinimumSupportedVersion: decision.MinimumSupportedVersion,
                    ApiContract: new ApiContractRange(decision.ApiContractMinimum, decision.ApiContractMaximum),
                    SecurityEpoch: decision.SecurityEpoch,
                    MandatoryAfterUtc: decision.MandatoryAfterUtc,
                    KillSwitch: decision.KillSwitch,
                    RollbackTarget: decision.RollbackTarget,
                    Artifact: null));
            })
            .AllowAnonymous()
            .WithName("ReleaseHandshake");
    }

    private static void MapOrganizations(RouteGroupBuilder api)
    {
        api.MapPost("/organizations", async (
                CreateOrganizationRequest request,
                CreateOrganizationHandler handler,
                IOrganizationRepository organizations,
                CancellationToken cancellationToken) =>
            {
                OrganizationType type = ParseEnum<OrganizationType>(request.Type, nameof(request.Type));

                CreateOrganizationResult result = await handler
                    .HandleAsync(
                        new CreateOrganizationCommand(request.Name, request.LegalName, type),
                        cancellationToken)
                    .ConfigureAwait(false);

                Organization created =
                    await organizations.FindByIdAsync(result.Id, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The created organization could not be read back.");

                return Results.Created($"/api/v1/organizations/{created.Id.Value}", Map(created));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OrganizationsCreate))
            .WithName("CreateOrganization");

        api.MapGet("/organizations/{id:guid}", async (
                Guid id,
                IOrganizationRepository organizations,
                CancellationToken cancellationToken) =>
            {
                Organization? organization = await organizations
                    .FindByIdAsync(new OrganizationId(id), cancellationToken)
                    .ConfigureAwait(false);

                return organization is null ? Results.NotFound() : Results.Ok(Map(organization));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.OrganizationsRead))
            .WithName("GetOrganization");

        api.MapPost("/organizations/{id:guid}/memberships", async (
                Guid id,
                GrantMembershipRequest request,
                GrantMembershipHandler handler,
                IMembershipRepository memberships,
                CancellationToken cancellationToken) =>
            {
                AgencyRole role = ParseEnum<AgencyRole>(request.Role, nameof(request.Role));

                GrantMembershipResult result = await handler
                    .HandleAsync(
                        new GrantMembershipCommand(new OrganizationId(id), new UserId(request.UserId), role),
                        cancellationToken)
                    .ConfigureAwait(false);

                Membership granted =
                    await memberships.FindByIdAsync(result.Id, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The granted membership could not be read back.");

                return Results.Created(
                    $"/api/v1/organizations/{id}/memberships/{granted.Id.Value}",
                    new MembershipResponse(
                        granted.Id.Value,
                        granted.OrganizationId.Value,
                        granted.UserId.Value,
                        granted.Role.ToString(),
                        granted.Status.ToString()));
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.MembershipsGrant))
            .WithName("GrantMembership");
    }

    private static void MapAudit(RouteGroupBuilder api)
    {
        api.MapGet("/audit", async (
                IAuditRepository audit,
                string? entityType,
                string? entityId,
                int? limit,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<AuditEvent> events =
                    entityType is not null && entityId is not null
                        ? await audit.ListForEntityAsync(entityType, entityId, cancellationToken).ConfigureAwait(false)
                        : await audit.ListRecentAsync(Math.Clamp(limit ?? 50, 1, 200), cancellationToken).ConfigureAwait(false);

                return Results.Ok(events.Select(Map).ToArray());
            })
            .RequireAuthorization(PermissionPolicy.Name(Permission.AuditRead))
            .WithName("ListAuditEvents");
    }

    private static OrganizationResponse Map(Organization organization) => new(
        organization.Id.Value,
        organization.Name,
        organization.LegalName,
        organization.Type.ToString(),
        organization.Status.ToString(),
        organization.CreatedAt);

    private static AuditEventResponse Map(AuditEvent auditEvent) => new(
        auditEvent.Id.Value,
        auditEvent.OccurredAt,
        auditEvent.Action,
        auditEvent.EntityType,
        auditEvent.EntityId,
        auditEvent.ActorUserId?.Value,
        auditEvent.OrganizationId?.Value,
        auditEvent.Permission,
        auditEvent.SemanticDelta,
        auditEvent.CorrelationId,
        auditEvent.ClientChannel,
        auditEvent.ClientVersion);

    private static TEnum ParseEnum<TEnum>(string value, string field)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse(value, ignoreCase: true, out TEnum parsed) || !Enum.IsDefined(parsed))
        {
            throw new DomainException(
                $"{field} '{value}' is not valid. Expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        }

        return parsed;
    }
}
