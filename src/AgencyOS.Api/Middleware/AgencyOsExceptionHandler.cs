using AgencyOS.Api.Observability;
using AgencyOS.Application.Authorization;
using AgencyOS.Application.Provisioning;
using AgencyOS.Application.SavedViews;
using AgencyOS.Domain.Idempotency;
using AgencyOS.Domain.Common;
using AgencyOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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

    public AgencyOsExceptionHandler(ILogger<AgencyOsExceptionHandler> logger) => _logger = logger;

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
            DomainException => (StatusCodes.Status400BadRequest, "Invalid request"),

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
            IdempotencyInProgressException => "idempotency_in_progress",
            IdempotencyConflictException => "idempotency_key_reused",
            SavedViewNameInUseException => "saved_view_name_in_use",
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
}
