using AgencyOS.Api.Http;
using AgencyOS.Application.Releases;
using AgencyOS.Domain.Releases;
using Microsoft.AspNetCore.Mvc;

namespace AgencyOS.Api.Middleware;

/// <summary>
/// Refuses protected mutations from revoked or incompatible clients.
/// </summary>
/// <remarks>
/// <para>
/// This is where <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c> stops being advisory.
/// The handshake endpoint tells a client what it should do; this middleware makes
/// it so. A client that ignores its handshake answer, or never performs one, or
/// has been modified to believe it is current, still cannot write canonical data.
/// </para>
/// <para>
/// Reads are not governed. A revoked build is permitted to read so it can render
/// an explanation and update itself; the kill-switch modes in
/// <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c> describe exactly this, a build placed
/// into update-only or read-only mode rather than shut out entirely.
/// </para>
/// </remarks>
internal sealed class ClientCompatibilityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ClientCompatibilityMiddleware> _logger;

    public ClientCompatibilityMiddleware(
        RequestDelegate next,
        ILogger<ClientCompatibilityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ClientCompatibilityService compatibility)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(compatibility);

        if (!IsProtectedMutation(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        ClientIdentity? client = ClientIdentityParser.Parse(context.Request);

        ReleaseDecision decision = await compatibility
            .EvaluateAsync(client, context.RequestAborted)
            .ConfigureAwait(false);

        if (!decision.BlocksProtectedMutations)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        _logger.LogWarning(
            "Refused protected mutation. policy={Policy} reason={Reason} path={Path} clientVersion={ClientVersion} channel={Channel}",
            decision.Policy,
            decision.Reason,
            context.Request.Path.Value,
            client?.Version.Text ?? "(none)",
            client is null ? "(none)" : ReleaseRingNames.ToWireName(client.Ring));

        await WriteRefusalAsync(context, decision).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether the request is a business mutation subject to release policy.
    /// </summary>
    private static bool IsProtectedMutation(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method))
        {
            return false;
        }

        if (!request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The handshake itself must stay reachable, otherwise a client that needs
        // to be told to update could never be told.
        return !request.Path.StartsWithSegments("/api/v1/release", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteRefusalAsync(HttpContext context, ReleaseDecision decision)
    {
        int status = decision.Policy == UpdatePolicy.Revoked
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status426UpgradeRequired;

        ProblemDetails problem = new()
        {
            Status = status,
            Title = decision.Policy == UpdatePolicy.Revoked
                ? "Client build revoked"
                : "Client update required",
            Detail = decision.Reason,
            Type = "https://agencyos.invalid/problems/client-compatibility",
        };

        problem.Extensions["policy"] = decision.Policy.ToString().ToUpperInvariant();
        problem.Extensions["latestVersion"] = decision.LatestVersion;
        problem.Extensions["minimumSupportedVersion"] = decision.MinimumSupportedVersion;
        problem.Extensions["apiContractMinimum"] = decision.ApiContractMinimum;
        problem.Extensions["apiContractMaximum"] = decision.ApiContractMaximum;
        problem.Extensions["securityEpoch"] = decision.SecurityEpoch;
        problem.Extensions["killSwitch"] = decision.KillSwitch;
        problem.Extensions["rollbackTarget"] = decision.RollbackTarget;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json; charset=utf-8";

        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted).ConfigureAwait(false);
    }
}
