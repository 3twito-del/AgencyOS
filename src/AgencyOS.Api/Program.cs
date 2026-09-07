// AgencyOS API host.
//
// Governing documents:
//   docs/02_ARCHITECTURE.md           - modular monolith, HTTPS/OpenAPI boundary
//   docs/06_FORCED_UPDATE_PROTOCOL.md - version identity and release enforcement
//   docs/07_SECURITY_AND_AUDIT.md     - server-side enforcement, audit, identity
//
// Milestone M1 adds identity, organizations, memberships, permission-based
// authorization, an append-only audit trail and server-side release enforcement.

using AgencyOS.Api.Authentication;
using AgencyOS.Api.Authorization;
using AgencyOS.Api.Endpoints;
using AgencyOS.Api.Health;
using AgencyOS.Api.Http;
using AgencyOS.Api.Middleware;
using AgencyOS.Api.Provisioning;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Memberships;
using AgencyOS.Application.Organizations;
using AgencyOS.Application.Provisioning;
using AgencyOS.Application.Releases;
using AgencyOS.Contracts;
using AgencyOS.Domain.Authorization;
using AgencyOS.Domain.Releases;
using AgencyOS.Infrastructure.DependencyInjection;
using AgencyOS.Infrastructure.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.AddAgencyOSStructuredLogging();

// ---------------------------------------------------------------------------
// Release ring safety
//
// The development authentication scheme trusts a header. It is acceptable only
// on rings that forbid real data (config/release-channels.yaml). Refusing to
// start is the correct response: a host that silently accepted header identity
// against canonical data would be the single worst defect this system could
// ship, and it would be invisible.
// ---------------------------------------------------------------------------
string authenticationMode = builder.Configuration["AgencyOS:Authentication:Mode"] ?? "Development";
bool developmentAuthentication = string.Equals(authenticationMode, "Development", StringComparison.OrdinalIgnoreCase);

bool ringAllowsRealData =
    ReleaseRingNames.TryParse(BuildInfo.Channel, out ReleaseRing buildRing)
    && ReleaseRingNames.AllowsRealData(buildRing);

if (developmentAuthentication && ringAllowsRealData)
{
    throw new InvalidOperationException(
        $"Development authentication cannot run on ring '{BuildInfo.Channel}', which permits real data. "
            + "Configure AgencyOS:Authentication:Mode for a real identity provider.");
}

// ---------------------------------------------------------------------------
// Persistence
//
// A ring that permits real data must be told explicitly where canonical data
// lives. A convenient default is only acceptable where the data is disposable.
// ---------------------------------------------------------------------------
string? connectionString =
    builder.Configuration.GetConnectionString("AgencyOS")
    ?? Environment.GetEnvironmentVariable("AGENCYOS_CONNECTION");

if (string.IsNullOrWhiteSpace(connectionString))
{
    if (ringAllowsRealData)
    {
        throw new InvalidOperationException(
            $"No AgencyOS connection string is configured and ring '{BuildInfo.Channel}' permits real data.");
    }

    connectionString = "Host=localhost;Port=5432;Database=agencyos_dev;Username=postgres;Password=postgres";
}

builder.Services.AddAgencyOSInfrastructure(connectionString);

// ---------------------------------------------------------------------------
// First-run initialization
//
// The gate exists only when a token is configured. Constructing it validates the
// token's strength, so a weak bootstrap credential fails startup rather than
// sitting quietly in a deployment.
// ---------------------------------------------------------------------------
string? bootstrapToken =
    builder.Configuration["AgencyOS:Bootstrap:Token"]
    ?? Environment.GetEnvironmentVariable("AGENCYOS_BOOTSTRAP_TOKEN");

BootstrapTokenGate? bootstrapGate =
    string.IsNullOrWhiteSpace(bootstrapToken) ? null : new BootstrapTokenGate(bootstrapToken);

// Application composition. The host decides which capabilities it uses; the
// infrastructure assembly only provides them.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IExecutionContext, HttpExecutionContext>();
builder.Services.AddScoped<AuditRecorder>();
builder.Services.AddScoped<CreateOrganizationHandler>();
builder.Services.AddScoped<GrantMembershipHandler>();
builder.Services.AddSingleton<IApiContractPolicy, ServerApiContractPolicy>();
builder.Services.AddScoped<BootstrapSystemHandler>();
builder.Services.AddScoped<ClientCompatibilityService>();

// ---------------------------------------------------------------------------
// Authentication and authorization
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(AgencyOsAuthentication.DevelopmentScheme)
    .AddScheme<DevelopmentAuthenticationOptions, DevelopmentAuthenticationHandler>(
        AgencyOsAuthentication.DevelopmentScheme,
        configureOptions: null);

builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

AuthorizationBuilder authorization = builder.Services.AddAuthorizationBuilder();

// One policy per known permission, so an endpoint cannot name a permission that
// does not exist in the domain vocabulary.
foreach (string permission in Permission.All)
{
    authorization.AddPolicy(
        PermissionPolicy.Name(permission),
        policy => policy
            .AddAuthenticationSchemes(AgencyOsAuthentication.DevelopmentScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission)));
}

// ---------------------------------------------------------------------------
// Health
//
// Liveness and readiness are separate endpoints with separate meanings. Only the
// readiness endpoint runs checks; see PostgresReadinessCheck for why conflating
// them is harmful.
// ---------------------------------------------------------------------------
builder.Services
    .AddHealthChecks()
    .AddCheck<PostgresReadinessCheck>(
        "postgres",
        tags: [HealthResponseWriter.ReadyTag]);

// ---------------------------------------------------------------------------
// API contract
//
// The machine-readable contract required by CLAUDE.md principle 6. No UI is
// registered: the requirement is the document, not a browser experience.
// ---------------------------------------------------------------------------
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "AgencyOS API";
        document.Info.Version = "v1";
        document.Info.Description =
            $"AgencyOS versioned domain API. API contract version {ApiContract.Current}. "
                + "Clients present release identity headers on every request and are refused "
                + "for mutations when revoked or incompatible (docs/06_FORCED_UPDATE_PROTOCOL.md).";

        return Task.CompletedTask;
    });
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AgencyOsExceptionHandler>();

WebApplication app = builder.Build();

app.UseExceptionHandler();

// Release enforcement is the outermost gate on mutations: a revoked or
// incompatible client is refused before authentication, authorization or any
// handler is consulted.
app.UseMiddleware<ClientCompatibilityMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// Liveness. Runs no checks: the process being able to answer is the answer.
// A database outage must not cause an orchestrator to restart healthy instances.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = static _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

// Readiness. Fails when canonical PostgreSQL cannot be reached, so traffic is
// routed away from an instance that cannot serve it.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static registration => registration.Tags.Contains(HealthResponseWriter.ReadyTag),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.MapGet("/version", static () => VersionResponse.Current());

app.MapOpenApi();

app.MapAgencyOsApi(bootstrapGate);

app.Logger.LogInformation(
    "AgencyOS API starting. version={Version} channel={Channel} buildId={BuildId} commit={GitCommit} "
        + "apiContract={ApiContractVersion} authentication={AuthenticationMode} bootstrap={BootstrapEnabled}",
    BuildInfo.Version,
    BuildInfo.Channel,
    BuildInfo.BuildId,
    BuildInfo.GitCommit,
    ApiContract.Current,
    authenticationMode,
    bootstrapGate is not null);

app.Run();

/// <summary>
/// Entry point of the AgencyOS API host. Declared explicitly so
/// <c>AgencyOS.Tests.Integration</c> can host it with <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program;
