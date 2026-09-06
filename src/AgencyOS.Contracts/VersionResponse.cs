namespace AgencyOS.Contracts;

/// <summary>
/// Build identity returned by <c>GET /version</c>.
/// </summary>
/// <remarks>
/// These are the fields a client presents to the release authority in
/// <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>. M0 exposes them for inspection; M1
/// turns the exchange into an enforced handshake.
/// </remarks>
/// <param name="Version">Informational version of the build.</param>
/// <param name="Channel">Release ring the build belongs to.</param>
/// <param name="BuildId">Build identifier.</param>
/// <param name="GitCommit">Abbreviated source commit.</param>
/// <param name="ApiContractVersion">API contract version this build speaks.</param>
public sealed record VersionResponse(
    string Version,
    string Channel,
    string BuildId,
    string GitCommit,
    int ApiContractVersion)
{
    /// <summary>Creates a response describing the currently running build.</summary>
    public static VersionResponse Current() => new(
        BuildInfo.Version,
        BuildInfo.Channel,
        BuildInfo.BuildId,
        BuildInfo.GitCommit,
        ApiContract.Current);
}
