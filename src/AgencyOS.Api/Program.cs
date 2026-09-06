// AgencyOS API host.
//
// Governing documents:
//   docs/02_ARCHITECTURE.md          - modular monolith, HTTPS/OpenAPI boundary
//   docs/06_FORCED_UPDATE_PROTOCOL.md - version identity presented to clients
//   docs/07_SECURITY_AND_AUDIT.md     - server-side enforcement
//
// Milestone M0 exposes liveness and build identity only. Authentication,
// authorization, audit and the enforced version handshake arrive with M1; no
// business endpoint may be added before that enforcement exists.

using AgencyOS.Contracts;
using AgencyOS.Infrastructure.DependencyInjection;
using AgencyOS.Infrastructure.Logging;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.AddAgencyOSStructuredLogging();
builder.Services.AddAgencyOSInfrastructure();
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = static (context, report) =>
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(new HealthResponse(report.Status.ToString()));
    },
});

app.MapGet("/version", static () => VersionResponse.Current());

app.Logger.LogInformation(
    "AgencyOS API starting. version={Version} channel={Channel} buildId={BuildId} commit={GitCommit} apiContract={ApiContractVersion}",
    BuildInfo.Version,
    BuildInfo.Channel,
    BuildInfo.BuildId,
    BuildInfo.GitCommit,
    ApiContract.Current);

app.Run();

/// <summary>
/// Entry point of the AgencyOS API host. Declared explicitly so
/// <c>AgencyOS.Tests.Integration</c> can host it with <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program;
