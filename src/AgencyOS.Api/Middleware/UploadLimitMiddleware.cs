using AgencyOS.Api.Http;
using Microsoft.AspNetCore.Mvc;

namespace AgencyOS.Api.Middleware;

/// <summary>
/// Refuses a body larger than this deployment accepts, and says how large that is.
/// </summary>
/// <remarks>
/// <para>
/// An explicit check rather than a framework limit, because the framework limits
/// disagree with each other and with the host. Kestrel stops a body at
/// <c>MaxRequestBodySize</c>; the multipart reader stops one at
/// <c>MultipartBodyLengthLimit</c> and reports it as a malformed body rather than
/// an oversized one; and <c>TestServer</c> honours neither the way Kestrel does, so
/// the behaviour under test would not be the behaviour in production (§27,
/// ADR-0037).
/// </para>
/// <para>
/// One check, before the body is read, in terms AgencyOS chose. The framework
/// limits stay configured underneath as a second line for the case this one cannot
/// see — a chunked upload that declares no length.
/// </para>
/// </remarks>
public sealed class UploadLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly UploadLimit _limit;

    public UploadLimitMiddleware(RequestDelegate next, UploadLimit limit)
    {
        _next = next;
        _limit = limit;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.ContentLength is { } length && length > _limit.Bytes)
        {
            await RefuseAsync(context, length).ConfigureAwait(false);

            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private async Task RefuseAsync(HttpContext context, long length)
    {
        // Refused before reading, so an oversized upload costs the bandwidth it
        // has already spent and nothing more.
        ProblemDetails problem = new()
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Title = "Upload too large",
            Detail = $"This deployment accepts uploads up to {_limit.Describe()}.",
        };

        problem.Extensions["maximumUploadBytes"] = _limit.Bytes;
        problem.Extensions["requestBytes"] = length;

        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problem).ConfigureAwait(false);
    }
}
