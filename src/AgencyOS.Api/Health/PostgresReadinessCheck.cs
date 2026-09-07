using AgencyOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgencyOS.Api.Health;

/// <summary>
/// Readiness check for canonical PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Liveness and readiness answer different questions, and conflating them causes
/// real damage. Liveness asks "is this process healthy?" - a failing answer means
/// restart me. Readiness asks "can this instance serve traffic?" - a failing
/// answer means route around me.
/// </para>
/// <para>
/// If PostgreSQL becoming unreachable failed liveness, an orchestrator would
/// restart every API instance during a database outage. Restarting them does not
/// bring the database back; it removes the instances that would have recovered on
/// their own the moment it returned. So this check is tagged <c>ready</c> only,
/// and the liveness endpoint runs no checks at all.
/// </para>
/// </remarks>
internal sealed class PostgresReadinessCheck : IHealthCheck
{
    private readonly AgencyOsDbContext _context;
    private readonly ILogger<PostgresReadinessCheck> _logger;

    public PostgresReadinessCheck(AgencyOsDbContext context, ILogger<PostgresReadinessCheck> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool reachable = await _context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);

            return reachable
                ? HealthCheckResult.Healthy("Canonical PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("Canonical PostgreSQL is not reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A readiness probe reports; it does not throw. An exception here would
            // surface as a 500 rather than as the "not ready" answer it actually is.
            _logger.LogWarning(ex, "Readiness check could not reach canonical PostgreSQL.");

            return HealthCheckResult.Unhealthy("Canonical PostgreSQL is not reachable.", ex);
        }
    }
}
