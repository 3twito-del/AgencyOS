using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Idempotency;
using AgencyOS.Contracts;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Api.Middleware;

/// <summary>
/// Makes a keyed mutation execute at most once.
/// </summary>
/// <remarks>
/// <para>
/// A client that queued a command offline generates its key before the first
/// submission and keeps it across every retry, restart and reconnect. This filter
/// is the server half: it reserves the key before the handler runs and stores the
/// answer afterwards, so the retry that follows a lost response replays that
/// answer rather than creating a second person.
/// </para>
/// <para>
/// Applied at the tenant group and skipped for reads, so a mutation added later
/// is protected by default. That is the right default: forgetting the filter on a
/// new command is silent, and the failure only appears as a duplicate record
/// weeks later.
/// </para>
/// <para>
/// The replayed answer is the status code and body of the original, not its
/// incidental headers. A replayed 201 therefore carries the created record but
/// not its <c>Location</c>; the client reads identity from the body, which it must
/// do anyway to apply the result to its cache.
/// </para>
/// <para>
/// See <c>docs/adr/ADR-0014-concurrency-and-idempotency.md</c> and the
/// <c>AtMostOneEffect</c> invariant in <c>specs/OfflineWriteQueue.tla</c>.
/// </para>
/// </remarks>
internal sealed class IdempotencyFilter : IEndpointFilter
{
    /// <summary>
    /// Serialization used to fingerprint a request's bound arguments.
    /// </summary>
    /// <remarks>
    /// Fixed rather than the ambient web defaults: the fingerprint of a command
    /// submitted today has to equal the fingerprint of its retry weeks later, so it
    /// must not follow whatever the host happens to be configured with.
    /// </remarks>
    private static readonly JsonSerializerOptions Fingerprinting = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
    };

    /// <summary>The assembly whose types carry a request's meaning.</summary>
    /// <remarks>
    /// A handler's arguments are a mixture of the command payload and the services
    /// the endpoint asked for. Only the contract types say what the caller wanted,
    /// and only they belong in the fingerprint.
    /// </remarks>
    private static readonly System.Reflection.Assembly ContractsAssembly = typeof(ClientHeaders).Assembly;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;

        if (!IsMutation(http.Request.Method))
        {
            return await next(context).ConfigureAwait(false);
        }

        if (!http.Request.Headers.TryGetValue(ClientHeaders.IdempotencyKey, out Microsoft.Extensions.Primitives.StringValues header))
        {
            return await next(context).ConfigureAwait(false);
        }

        string key = header.ToString().Trim();

        if (key.Length == 0)
        {
            return await next(context).ConfigureAwait(false);
        }

        if (key.Length > IdempotencyCoordinator.MaximumKeyLength)
        {
            return Results.Problem(
                title: "Invalid idempotency key",
                detail: string.Create(
                    CultureInfo.InvariantCulture,
                    $"An idempotency key may be at most {IdempotencyCoordinator.MaximumKeyLength} characters."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryReadOrganization(http, out OrganizationId organizationId))
        {
            // Not a tenant-scoped route, so there is no scope to record the key
            // in. Keys are per tenant so that one tenant can never replay
            // another's answer.
            return await next(context).ConfigureAwait(false);
        }

        IdempotencyCoordinator coordinator = http.RequestServices.GetRequiredService<IdempotencyCoordinator>();

        string fingerprint = IdempotencyCoordinator.Fingerprint(
            http.Request.Method,
            http.Request.Path.Value ?? string.Empty,
            DescribeArguments(context));

        IdempotencyDecision decision = await coordinator
            .BeginAsync(organizationId, key, fingerprint, http.RequestAborted)
            .ConfigureAwait(false);

        if (!decision.ShouldExecute)
        {
            // The number that says the offline queue is working. A replay is a
            // duplicate that did not happen, not a fault.
            AgencyOsTelemetry.IdempotentReplays.Add(1);

            Activity.Current?.SetTag("agencyos.idempotency.replayed", true);

            return decision.ReplayBody is null
                ? Results.StatusCode(decision.ReplayStatusCode!.Value)
                : Results.Content(
                    decision.ReplayBody,
                    "application/json; charset=utf-8",
                    Encoding.UTF8,
                    decision.ReplayStatusCode);
        }

        object? result;

        try
        {
            result = await next(context).ConfigureAwait(false);
        }
        catch
        {
            // The command failed without producing an answer. Releasing the
            // reservation is what lets the client's retry - the correct response
            // to a 500 - actually run, instead of being refused as in-progress
            // until the retention window expires.
            await coordinator.AbandonAsync(organizationId, key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (result is not IResult produced)
        {
            // A handler that returned a bare value rather than an IResult cannot
            // have its answer captured, so nothing is stored. Every M3 endpoint
            // returns an IResult; this arm exists so a future one that does not
            // fails open rather than silently recording an empty answer.
            await coordinator.AbandonAsync(organizationId, key, CancellationToken.None).ConfigureAwait(false);
            return result;
        }

        return await CaptureAsync(http, produced, coordinator, organizationId, key).ConfigureAwait(false);
    }

    private static bool IsMutation(string method) =>
        !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);

    private static bool TryReadOrganization(HttpContext http, out OrganizationId organizationId)
    {
        organizationId = default;

        if (http.Request.RouteValues.TryGetValue("organizationId", out object? raw)
            && Guid.TryParse(raw?.ToString(), out Guid parsed))
        {
            organizationId = new OrganizationId(parsed);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Describes what the caller asked for, from the arguments the endpoint bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bound arguments rather than the raw body, because by the time an
    /// endpoint filter runs the body has already been consumed by model binding and
    /// is no longer there to hash. Reading it here yields an empty string for every
    /// request, which would make two genuinely different commands sharing one key
    /// indistinguishable - and the second would be answered with the first's
    /// result. That is the exact failure this filter exists to prevent, so it is
    /// worth being explicit about why the body is not used.
    /// </para>
    /// <para>
    /// Deriving it from the bound payload is also the more faithful comparison.
    /// Whitespace and property order are not differences in intent, so a retry
    /// serialized by a different client build is still recognized as the same
    /// command.
    /// </para>
    /// </remarks>
    private static string DescribeArguments(EndpointFilterInvocationContext context)
    {
        StringBuilder builder = new();

        foreach (object? argument in context.Arguments)
        {
            if (argument is null || !ContractsAssembly.Equals(argument.GetType().Assembly))
            {
                continue;
            }

            builder.Append(JsonSerializer.Serialize(argument, argument.GetType(), Fingerprinting));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Runs the handler's result into a buffer, stores it, then writes it out.
    /// </summary>
    /// <remarks>
    /// The answer has to be stored before it is sent, not after: a process that
    /// dies between sending and storing would leave the client holding an answer
    /// the server would later re-execute.
    /// </remarks>
    private static async Task<IResult> CaptureAsync(
        HttpContext http,
        IResult produced,
        IdempotencyCoordinator coordinator,
        OrganizationId organizationId,
        string key)
    {
        Stream original = http.Response.Body;

        await using MemoryStream buffer = new();

        http.Response.Body = buffer;

        try
        {
            await produced.ExecuteAsync(http).ConfigureAwait(false);
        }
        finally
        {
            http.Response.Body = original;
        }

        byte[] bytes = buffer.ToArray();
        int status = http.Response.StatusCode;

        if (status >= StatusCodes.Status500InternalServerError)
        {
            // A server failure is not an answer. Storing it would make the defect
            // permanent for that key.
            await coordinator.AbandonAsync(organizationId, key, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await coordinator
                .CompleteAsync(
                    organizationId,
                    key,
                    status,
                    bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (bytes.Length > 0)
        {
            await original.WriteAsync(bytes, http.RequestAborted).ConfigureAwait(false);
        }

        // The response has already been written, headers included.
        return Results.Empty;
    }
}
