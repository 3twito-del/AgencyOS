using System.Security.Cryptography;
using System.Text;
using AgencyOS.Contracts.Provisioning;

namespace AgencyOS.Api.Provisioning;

/// <summary>
/// Validates the out-of-band bootstrap token.
/// </summary>
/// <remarks>
/// <para>
/// Registered only when a token is configured. When none is, the bootstrap
/// endpoints are never mapped and the route simply does not exist - a deployment
/// that has not deliberately enabled first-run initialization cannot be
/// initialized over HTTP at all.
/// </para>
/// <para>
/// Comparison is over SHA-256 digests so it is constant time in both content and
/// length. Comparing the raw strings would leak the token's length through timing,
/// which is a small leak about a credential that grants the first owner.
/// </para>
/// </remarks>
public sealed class BootstrapTokenGate
{
    /// <summary>
    /// Shortest token the host will accept.
    /// </summary>
    /// <remarks>
    /// A short bootstrap token is guessable, and guessing it once on an
    /// uninitialized system is enough to own the system. The host refuses to start
    /// rather than accept a weak one.
    /// </remarks>
    public const int MinimumTokenLength = 32;

    private readonly byte[] _expectedDigest;

    public BootstrapTokenGate(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (token.Length < MinimumTokenLength)
        {
            throw new InvalidOperationException(
                $"The bootstrap token must be at least {MinimumTokenLength} characters.");
        }

        _expectedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>Determines whether the request presents the configured token.</summary>
    public bool IsSatisfiedBy(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Headers.TryGetValue(BootstrapHeaders.Token, out var header))
        {
            return false;
        }

        string presented = header.ToString();

        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        byte[] presentedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(presented));

        return CryptographicOperations.FixedTimeEquals(presentedDigest, _expectedDigest);
    }
}
