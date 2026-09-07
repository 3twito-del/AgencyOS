using AgencyOS.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgencyOS.Api.Health;

/// <summary>Writes health results using the versioned contract type.</summary>
public static class HealthResponseWriter
{
    /// <summary>Tag marking a check as a readiness dependency.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Serializes a health report as <see cref="HealthResponse"/>.</summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsJsonAsync(
            new HealthResponse(report.Status.ToString()),
            context.RequestAborted);
    }
}
