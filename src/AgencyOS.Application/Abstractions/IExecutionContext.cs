using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Releases;

namespace AgencyOS.Application.Abstractions;

/// <summary>
/// Who is acting, from where, and under which correlation identifier.
/// </summary>
/// <remarks>
/// Populated at the transport edge from authenticated identity and validated
/// client headers, never from a request body. Everything an audit record says
/// about the actor and the client comes from here.
/// </remarks>
public interface IExecutionContext
{
    /// <summary>The authenticated user, or <see langword="null"/> when unauthenticated.</summary>
    UserId? UserId { get; }

    /// <summary>The authenticated identity-provider subject.</summary>
    string? Subject { get; }

    /// <summary>Correlation identifier tying logs, traces and audit records together.</summary>
    string CorrelationId { get; }

    /// <summary>The client identity presented with the request, when it presented one.</summary>
    ClientIdentity? Client { get; }
}
