namespace AgencyOS.Domain.Releases;

/// <summary>
/// The server's answer to a client version handshake.
/// </summary>
/// <remarks>
/// <see cref="BlocksProtectedMutations"/> is the operative field. It is computed
/// here, on the server, and the compatibility middleware reads it directly. The
/// client is told the same thing so it can present a sensible experience, but it
/// is never the thing enforcing the outcome.
/// </remarks>
public sealed record ReleaseDecision(
    UpdatePolicy Policy,
    string Reason,
    bool BlocksProtectedMutations,
    string? LatestVersion,
    string? MinimumSupportedVersion,
    int ApiContractMinimum,
    int ApiContractMaximum,
    int SecurityEpoch,
    bool KillSwitch,
    DateTimeOffset? MandatoryAfterUtc,
    string? RollbackTarget)
{
    /// <summary>
    /// The answer for a client the server holds no policy for.
    /// </summary>
    /// <remarks>
    /// Fails closed. An unrecognized platform or ring cannot be governed by the
    /// release authority, and a client the authority cannot govern must not be
    /// able to write canonical data.
    /// </remarks>
    public static ReleaseDecision NoPolicy(string reason) => new(
        UpdatePolicy.Mandatory,
        reason,
        BlocksProtectedMutations: true,
        LatestVersion: null,
        MinimumSupportedVersion: null,
        ApiContractMinimum: 0,
        ApiContractMaximum: 0,
        SecurityEpoch: 0,
        KillSwitch: false,
        MandatoryAfterUtc: null,
        RollbackTarget: null);
}
