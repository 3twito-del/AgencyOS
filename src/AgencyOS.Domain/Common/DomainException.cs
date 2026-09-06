namespace AgencyOS.Domain.Common;

/// <summary>
/// Raised when a domain invariant would be violated.
/// </summary>
/// <remarks>
/// Domain invariants are enforced in the domain, not at the transport edge, so a
/// caller that bypasses the API still cannot construct an illegal state.
/// </remarks>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
