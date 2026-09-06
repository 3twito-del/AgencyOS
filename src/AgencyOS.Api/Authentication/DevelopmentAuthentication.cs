using System.Security.Claims;
using System.Text.Encodings.Web;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AgencyOS.Api.Authentication;

/// <summary>Scheme names and claim types used by AgencyOS authentication.</summary>
public static class AgencyOsAuthentication
{
    /// <summary>The development scheme. Replaced wholesale by OIDC; see the class remarks.</summary>
    public const string DevelopmentScheme = "AgencyOS.Development";

    /// <summary>Header carrying the asserted identity-provider subject in development.</summary>
    public const string SubjectHeader = "X-AgencyOS-Dev-Subject";

    /// <summary>Claim holding the AgencyOS user identifier.</summary>
    public const string UserIdClaim = "agencyos:user_id";
}

/// <summary>Options for the development authentication scheme.</summary>
public sealed class DevelopmentAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// Development-only authentication: trusts a header naming an identity-provider subject.
/// </summary>
/// <remarks>
/// <para>
/// This is the placeholder <c>docs/07_SECURITY_AND_AUDIT.md</c> calls for, and it
/// is deliberately the only thing that is provider-specific. Everything
/// downstream - <see cref="IExecutionContext"/>, the permission evaluator, the
/// audit trail - works from a resolved <see cref="User"/> located by subject.
/// Introducing Entra ID or any OIDC issuer replaces this one class and the
/// scheme registration; no domain, application or persistence code changes.
/// </para>
/// <para>
/// It trusts a header, so it must never run where real data lives. The host
/// refuses to start if this scheme is configured on a ring that permits canonical
/// data - see <c>Program.cs</c>. That check is what makes an otherwise dangerous
/// convenience safe to keep in the tree.
/// </para>
/// </remarks>
internal sealed class DevelopmentAuthenticationHandler
    : AuthenticationHandler<DevelopmentAuthenticationOptions>
{
    private readonly IUserRepository _users;

    public DevelopmentAuthenticationHandler(
        IOptionsMonitor<DevelopmentAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IUserRepository users)
        : base(options, logger, encoder)
    {
        _users = users;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AgencyOsAuthentication.SubjectHeader, out var header))
        {
            return AuthenticateResult.NoResult();
        }

        string subject = header.ToString();

        if (string.IsNullOrWhiteSpace(subject))
        {
            return AuthenticateResult.Fail("Empty subject header.");
        }

        User? user = await _users.FindBySubjectAsync(subject, Context.RequestAborted).ConfigureAwait(false);

        if (user is null)
        {
            // An unknown subject is not an identity. It is not silently upgraded
            // into one by provisioning a user on the fly.
            return AuthenticateResult.Fail("Unknown subject.");
        }

        Claim[] claims =
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.Value.ToString()),
            new Claim(AgencyOsAuthentication.UserIdClaim, user.Id.Value.ToString()),
            new Claim("sub", user.ExternalSubject),
            new Claim(ClaimTypes.Name, user.DisplayName),
        ];

        ClaimsPrincipal principal = new(
            new ClaimsIdentity(claims, AgencyOsAuthentication.DevelopmentScheme));

        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, AgencyOsAuthentication.DevelopmentScheme));
    }
}
