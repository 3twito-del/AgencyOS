namespace AgencyOS.Application.Abstractions;

/// <summary>
/// The API contract range this server speaks.
/// </summary>
/// <remarks>
/// An abstraction rather than a direct reference to the contracts assembly, so
/// the application layer keeps its single dependency on the domain. It exists
/// because first-run initialization has to publish a release policy, and a
/// release policy has to state a contract range - which is the server's fact to
/// state, never the caller's.
/// </remarks>
public interface IApiContractPolicy
{
    /// <summary>Lowest contract version this server accepts.</summary>
    int Minimum { get; }

    /// <summary>Highest contract version this server accepts.</summary>
    int Maximum { get; }
}
