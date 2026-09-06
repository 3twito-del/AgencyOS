using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgencyOS.Infrastructure.Logging;

/// <summary>
/// Structured logging foundation for every AgencyOS host.
/// </summary>
/// <remarks>
/// <para>
/// Governing documents: <c>CLAUDE.md</c> section 2 (structured logging) and
/// <c>docs/07_SECURITY_AND_AUDIT.md</c> (correlation/trace identifiers on
/// consequential records).
/// </para>
/// <para>
/// Logs are emitted as one JSON object per line so they are machine-readable
/// without a parsing convention. OpenTelemetry export is deliberately absent in
/// M0: there is no consequential failure path to trace yet, and
/// <c>CLAUDE.md</c> section 5 forbids speculative infrastructure. Activity
/// tracking is enabled now so trace and span identifiers are already present
/// when exporters are introduced.
/// </para>
/// </remarks>
public static class AgencyOSLoggingExtensions
{
    /// <summary>
    /// Replaces the default providers with line-delimited JSON console logging,
    /// carrying scopes and W3C trace correlation.
    /// </summary>
    /// <param name="logging">The logging builder to configure.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ILoggingBuilder AddAgencyOSStructuredLogging(this ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        logging.ClearProviders();

        logging.Configure(static options =>
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId
                | ActivityTrackingOptions.SpanId
                | ActivityTrackingOptions.ParentId);

        logging.AddJsonConsole(static options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
            options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
        });

        return logging;
    }
}
