using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using AgencyOS.Api.Authentication;
using AgencyOS.Application.Abstractions;
using AgencyOS.Contracts;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Releases;

namespace AgencyOS.Api.Http;

/// <summary>
/// Builds the execution context from the current request.
/// </summary>
/// <remarks>
/// Actor comes from authenticated claims and client identity from validated
/// headers. Nothing is taken from the request body: a caller must not be able to
/// choose what the audit trail records about them.
/// </remarks>
internal sealed class HttpExecutionContext : IExecutionContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpExecutionContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public UserId? UserId
    {
        get
        {
            string? raw = _accessor.HttpContext?.User.FindFirstValue(AgencyOsAuthentication.UserIdClaim);
            return Guid.TryParse(raw, out Guid id) ? new UserId(id) : null;
        }
    }

    public string? Subject => _accessor.HttpContext?.User.FindFirstValue("sub");

    public string CorrelationId
    {
        get
        {
            HttpContext? context = _accessor.HttpContext;

            if (context is null)
            {
                return "none";
            }

            if (context.Request.Headers.TryGetValue(ClientHeaders.CorrelationId, out var supplied)
                && !string.IsNullOrWhiteSpace(supplied.ToString()))
            {
                return Truncate(supplied.ToString());
            }

            // Falls back to the W3C trace identifier, which the logging pipeline
            // already stamps on every log record.
            return Truncate(Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
        }
    }

    public ClientIdentity? Client => ClientIdentityParser.Parse(_accessor.HttpContext?.Request);

    private static string Truncate(string value) => value.Length <= 128 ? value : value[..128];
}

/// <summary>Parses the client identity headers described in <see cref="ClientHeaders"/>.</summary>
internal static class ClientIdentityParser
{
    /// <summary>
    /// Parses a client identity, returning <see langword="null"/> when the request
    /// does not present a complete and well-formed one.
    /// </summary>
    /// <remarks>
    /// Partial or malformed identity is treated as no identity. A half-parsed
    /// client would otherwise be evaluated against a policy row it was never
    /// meant to match.
    /// </remarks>
    public static ClientIdentity? Parse(HttpRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        string? platform = Read(request, ClientHeaders.Platform);
        string? channel = Read(request, ClientHeaders.Channel);
        string? version = Read(request, ClientHeaders.ClientVersion);
        string? contract = Read(request, ClientHeaders.ApiContractVersion);

        if (platform is null || channel is null || version is null || contract is null)
        {
            return null;
        }

        if (!ReleaseRingNames.TryParse(channel, out ReleaseRing ring))
        {
            return null;
        }

        if (!ClientVersion.TryParse(version, out ClientVersion? parsedVersion))
        {
            return null;
        }

        if (!int.TryParse(contract, NumberStyles.Integer, CultureInfo.InvariantCulture, out int contractVersion))
        {
            return null;
        }

        return new ClientIdentity(
            platform,
            ring,
            parsedVersion,
            contractVersion,
            BuildId: Read(request, ClientHeaders.BuildId));
    }

    private static string? Read(HttpRequest request, string header)
    {
        if (!request.Headers.TryGetValue(header, out var value))
        {
            return null;
        }

        string text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
