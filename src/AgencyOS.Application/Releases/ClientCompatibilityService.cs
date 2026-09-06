using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Releases;

namespace AgencyOS.Application.Releases;

/// <summary>
/// Answers the release authority question for a presented client identity.
/// </summary>
/// <remarks>
/// One implementation serves both the handshake endpoint and the compatibility
/// middleware. If the two computed the answer separately, a client could be told
/// it was acceptable and then refused, or worse, told it was refused and then
/// permitted.
/// </remarks>
public sealed class ClientCompatibilityService
{
    private readonly IReleasePolicyRepository _policies;
    private readonly IClock _clock;

    public ClientCompatibilityService(IReleasePolicyRepository policies, IClock clock)
    {
        _policies = policies;
        _clock = clock;
    }

    /// <summary>Evaluates a client identity against the applicable release policy.</summary>
    /// <param name="client">
    /// The presented client identity, or <see langword="null"/> when the request
    /// presented none.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReleaseDecision> EvaluateAsync(
        ClientIdentity? client,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            // A client that does not identify itself cannot be governed, so it is
            // not permitted to mutate. Failing closed is the whole point.
            return ReleaseDecision.NoPolicy("The request did not present a usable client identity.");
        }

        ReleasePolicy? policy = await _policies
            .FindAsync(client.Platform, client.Ring, cancellationToken)
            .ConfigureAwait(false);

        if (policy is null)
        {
            return ReleaseDecision.NoPolicy(
                $"No release policy is published for platform '{client.Platform}' on ring "
                    + $"'{ReleaseRingNames.ToWireName(client.Ring)}'.");
        }

        return policy.Evaluate(client, _clock.UtcNow);
    }
}
