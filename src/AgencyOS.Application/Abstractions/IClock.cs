namespace AgencyOS.Application.Abstractions;

/// <summary>Supplies the current instant.</summary>
/// <remarks>
/// Time is an input, not an ambient fact. Audit records and release deadlines are
/// both time-dependent, and both need to be testable at a chosen instant.
/// </remarks>
public interface IClock
{
    /// <summary>Gets the current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}
