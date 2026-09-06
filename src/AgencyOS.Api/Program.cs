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
using AgencyOS.Api.Http;
using AgencyOS.Api.Middleware;
using AgencyOS.Application.Abstractions;
using AgencyOS.Application.Audit;
using AgencyOS.Application.Memberships;
using AgencyOS.Application.Organizations;
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

// Application composition. The host decides which capabilities it uses; the
// infrastructure assembly only provides them.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IExecutionContext, HttpExecutionContext>();
builder.Services.AddScoped<AuditRecorder>();
builder.Services.AddScoped<CreateOrganizationHandler>();
builder.Services.AddScoped<GrantMembershipHandler>();
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

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AgencyOsExceptionHandler>();
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

app.UseExceptionHandler();

// Release enforcement is the outermost gate on mutations: a revoked or
// incompatible client is refused before authentication, authorization or any
// handler is consulted.
app.UseMiddleware<ClientCompatibilityMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = static (context, report) =>
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(new HealthResponse(report.Status.ToString()));
    },
});

app.MapGet("/version", static () => VersionResponse.Current());

app.MapAgencyOsApi();

app.Logger.LogInformation(
    "AgencyOS API starting. version={Version} channel={Channel} buildId={BuildId} commit={GitCommit} apiContract={ApiContractVersion} authentication={AuthenticationMode}",
    BuildInfo.Version,
    BuildInfo.Channel,
    BuildInfo.BuildId,
    BuildInfo.GitCommit,
    ApiContract.Current,
    authenticationMode);

app.Run();

/// <summary>
/// Entry point of the AgencyOS API host. Declared explicitly so
/// <c>AgencyOS.Tests.Integration</c> can host it with <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program;
