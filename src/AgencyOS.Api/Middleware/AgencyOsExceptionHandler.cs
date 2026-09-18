using AgencyOS.Api.Http;
using AgencyOS.Api.Observability;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Provisioning;
using AgencyOS.Application.Representations;
using AgencyOS.Application.SavedViews;
using AgencyOS.Domain.Idempotency;
using AgencyOS.Domain.Common;
using AgencyOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Api.Middleware;

/// <summary>
/// Maps application and domain failures to problem details.
/// </summary>
/// <remarks>
/// Mapping lives at the edge so handlers can fail by throwing a meaningful
/// exception rather than by threading a result type through every call site.
/// An unrecognized exception is deliberately not translated: it becomes a 500
/// with no detail, because leaking internals is worse than being unhelpful.
/// </remarks>
internal sealed class AgencyOsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<AgencyOsExceptionHandler> _logger;
    private readonly UploadLimit _uploadLimit;

    public AgencyOsExceptionHandler(
        ILogger<AgencyOsExceptionHandler> logger,
        UploadLimit uploadLimit)
    {
        _logger = logger;
        _uploadLimit = uploadLimit;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        (int status, string title) = exception switch
        {
            NotAuthenticatedException => (StatusCodes.Status401Unauthorized, "Authentication required"),
            PermissionDeniedException => (StatusCodes.Status403Forbidden, "Permission denied"),
            EntityNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            SystemAlreadyInitializedException => (StatusCodes.Status409Conflict, "Already initialized"),

            // The record moved while the caller was looking at it. A first-class
            // outcome with its own UI, not an error: the caller is told what they
            // had and what it is now, and decides.
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, "Version conflict"),

            // A key still being processed. Retrying is correct; executing
            // alongside the first attempt is the duplicate the key exists to
            // prevent.
            IdempotencyInProgressException => (StatusCodes.Status409Conflict, "Request in progress"),

            // Two different commands wearing one key. Serving either answer would
            // be wrong, so neither is served.
            IdempotencyConflictException => (StatusCodes.Status422UnprocessableEntity, "Idempotency key reused"),

            SavedViewNameInUseException => (StatusCodes.Status409Conflict, "Name already used"),

            // The record the caller wanted to create is already there. A conflict
            // rather than a bad request: nothing about the request was malformed.
            AlreadyExistsException => (StatusCodes.Status409Conflict, "Already exists"),
            DomainException => (StatusCodes.Status400BadRequest, "Invalid request"),

            // The backstop. UploadLimitMiddleware refuses anything that declares
            // an oversized length; this catches a chunked body that declared none
            // and was stopped by the framework instead (§27, ADR-0037).
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                (StatusCodes.Status413PayloadTooLarge, "Upload too large"),

            // Everything else the framework itself rejected before a handler ran:
            // a required query parameter the caller omitted, one it sent in a shape
            // that will not parse, a request body that is not the JSON it claims to
            // be.
            //
            // The status is the exception's own. This is not an arbitrary exception
            // being relabelled - BadHttpRequestException exists to say "the client
            // sent something this endpoint cannot accept" and carries the status it
            // means, which for parameter binding is 400. Honouring it is reporting
            // what the framework already decided.
            //
            // Only 4xx. A BadHttpRequestException carrying a 5xx would be a server
            // fault wearing a client-error type, and it keeps the untranslated
            // treatment below: 500, no detail.
            //
            // Before this arm only 413 was mapped, so every other value fell
            // through to the unhandled path and nine required query parameters and
            // every unparseable body answered 500. Binding runs after
            // authentication and authorization, so nothing here is reachable by a
            // caller who would not already have been refused.
            BadHttpRequestException { StatusCode: >= 400 and < 500 } malformed =>
                (malformed.StatusCode, "Invalid request"),

            // A body the framework itself could not parse - a multipart section
            // with a broken Content-Disposition, a malformed boundary. The caller
            // sent something invalid, so it is a refusal rather than a server
            // fault, and it is not logged as an integrity failure. Uploads are the
            // one place a hostile client can send a body no well-behaved client
            // would produce, and a 500 there is both wrong and noisy (ADR-0024).
            InvalidDataException => (StatusCodes.Status400BadRequest, "Malformed request body"),

            // Two requests read the same row and both tried to write it. The
            // application check on `expectedVersion` catches a client working from
            // a stale copy; this catches the case it cannot, where both callers
            // were current when they read and only one can be current when they
            // write. The caller is told the same thing either way: refresh and
            // decide again (ADR-0014).
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Version conflict"),

            // Reaching this means a defense-in-depth layer fired. It is a defect,
            // not a user error, and it is logged as one.
            AuditTrailImmutableException => (StatusCodes.Status500InternalServerError, "Audit trail violation"),

            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        if (exception is SystemAlreadyInitializedException)
        {
            // Reaching this means the bootstrap token is still configured on an
            // already-initialized deployment. Nothing was mutated - the handler
            // refuses, and the database singleton would refuse after it - but the
            // secret has outlived its single use and should be removed.
            _logger.LogWarning(
                "Bootstrap was attempted on an initialized system. No state was changed. "
                    + "The bootstrap token is still configured for this deployment and should be removed.");
        }

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled integrity failure on {Path}.", httpContext.Request.Path.Value);
        }
        else
        {
            _logger.LogInformation(
                "Request refused on {Path}: {Title} ({Message})",
                httpContext.Request.Path.Value,
                title,
                exception.Message);
        }

        ProblemDetails problem = new()
        {
            Status = status,
            Title = title,
            Detail = status >= StatusCodes.Status500InternalServerError ? null : exception.Message,
        };

        // A body the framework could not bind. Its own sentence names the DTO
        // class the endpoint declares — "Failed to read parameter
        // \"CreateDealRequest request\" from the request body as JSON" — which is
        // an internal type name, says nothing about which field was wrong, and
        // says nothing about what was expected. Every domain refusal in this
        // product does the opposite (AOS-R002-008).
        if (exception is BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest } malformedBody
            && malformedBody.InnerException is System.Text.Json.JsonException reading
            && Field(reading) is { Length: > 0 } field)
        {
            problem.Detail = Expectation(reading, field) is { Length: > 0 } expectation
                ? $"'{field}' could not be read. Expected {expectation}."
                : $"'{field}' could not be read.";
        }

        if (status == StatusCodes.Status413PayloadTooLarge)
        {
            // The framework's own message names no number. A refusal that does
            // not say what would have fit is not actionable.
            problem.Detail =
                $"This deployment accepts uploads up to {_uploadLimit.Describe()}.";

            problem.Extensions["maximumUploadBytes"] = _uploadLimit.Bytes;
        }

        if (exception is PermissionDeniedException denied)
        {
            problem.Extensions["requiredPermission"] = denied.Permission;
        }

        // A machine-readable code, because three different conditions answer 409
        // and a client that has to distinguish them by title string is a client
        // that breaks when the wording improves.
        string? code = exception switch
        {
            ConcurrencyConflictException => "version_conflict",

            // The same code as the checked conflict above. A client cannot act on
            // the difference between "you were stale when you asked" and "you were
            // current and somebody beat you", and both call for the same response.
            DbUpdateConcurrencyException => "version_conflict",
            IdempotencyInProgressException => "idempotency_in_progress",
            IdempotencyConflictException => "idempotency_key_reused",
            SavedViewNameInUseException => "saved_view_name_in_use",
            AlreadyExistsException => "already_exists",
            _ => null,
        };

        if (code is not null)
        {
            problem.Extensions["code"] = code;
        }

        if (exception is ConcurrencyConflictException conflict)
        {
            AgencyOsTelemetry.VersionConflicts.Add(
                1,
                new KeyValuePair<string, object?>("entityType", conflict.EntityType));

            // Both versions, so the client can say "you had 3, it is now 5"
            // rather than "something changed".
            problem.Extensions["entityType"] = conflict.EntityType;
            problem.Extensions["entityId"] = conflict.EntityId;
            problem.Extensions["expectedVersion"] = conflict.ExpectedVersion;
            problem.Extensions["actualVersion"] = conflict.ActualVersion;
        }

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>The field the caller sent that could not be read.</summary>
    /// <param name="reading">What the serializer refused.</param>
    /// <returns>The field's name as the caller wrote it, or empty.</returns>
    /// <remarks>
    /// The serializer reports a path — <c>$.ownerUserId</c>, or
    /// <c>$.participants[0].party</c> — which is the caller's own JSON and safe to
    /// quote back. Only the path is used; the rest of the serializer's sentence
    /// names .NET types and is never shown.
    /// </remarks>
    private static string Field(System.Text.Json.JsonException reading) =>
        reading.Path is { Length: > 0 } path
            ? path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$')
            : string.Empty;

    /// <summary>What the field should have looked like, in a reader's words.</summary>
    /// <param name="reading">What the serializer refused.</param>
    /// <param name="field">The field named by the serializer's path.</param>
    /// <returns>A description, or empty when nothing can be said with confidence.</returns>
    /// <remarks>
    /// <para>
    /// The serializer says what it was building, not what the member wanted: for a
    /// record it reports the request type itself, whichever property failed. So
    /// the member's type is looked up on the contract, and only the contract
    /// assembly is searched.
    /// </para>
    /// <para>
    /// Deliberately a closed list, and silent about anything outside it. Answering
    /// with a .NET type name would trade one internal name for another, which is
    /// the defect rather than the repair.
    /// </para>
    /// </remarks>
    private static string Expectation(System.Text.Json.JsonException reading, string field)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            reading.Message ?? string.Empty, @"converted to (?<type>[\w.]+)");

        if (!match.Success || field.Contains('.', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // The serializer ends its sentence with a full stop, and a type name can
        // contain dots, so the pattern takes both and the trailing one is dropped.
        string named = match.Groups["type"].Value.TrimEnd('.');

        Type? target = named.StartsWith("System.", StringComparison.Ordinal)
            ? Type.GetType(named)
            : typeof(AgencyOS.Contracts.ClientHeaders).Assembly.GetType(named);

        if (target is null)
        {
            return string.Empty;
        }

        // A record reports itself; the property the path names is the one that
        // could not be read.
        Type? member = target.Namespace?.StartsWith("System", StringComparison.Ordinal) == true
            ? target
            : target.GetProperties()
                .FirstOrDefault(x => string.Equals(x.Name, field, StringComparison.OrdinalIgnoreCase))
                ?.PropertyType;

        if (member is null)
        {
            return string.Empty;
        }

        return (Nullable.GetUnderlyingType(member) ?? member).Name switch
        {
            "Guid" => "an identifier",
            "DateTimeOffset" or "DateTime" => "a timestamp, such as 2026-09-18T03:13:39Z",
            "DateOnly" => "a date, such as 2026-09-18",
            "TimeOnly" => "a time of day, such as 14:30",
            "Int32" or "Int64" or "Int16" => "a whole number",
            "Decimal" or "Double" or "Single" => "a number",
            "Boolean" => "true or false",
            "String" => "text",
            _ => string.Empty,
        };
    }
}
