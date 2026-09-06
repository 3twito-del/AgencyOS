namespace AgencyOS.Contracts.Releases;

/// <summary>
/// What a client presents to the release authority before loading business data.
/// </summary>
/// <remarks>
/// Shape follows the request in <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>.
/// </remarks>
/// <param name="Platform">Platform moniker, for example <c>windows-x64</c>.</param>
/// <param name="Channel">Release ring name, for example <c>alpha</c>.</param>
/// <param name="ClientVersion">Client build version.</param>
/// <param name="ApiContractVersion">API contract version the client speaks.</param>
/// <param name="BuildId">Build identifier.</param>
/// <param name="GitCommit">Source commit.</param>
/// <param name="LocalSchemaVersion">Local cache schema version.</param>
/// <param name="WindowsBuild">Reported Windows build.</param>
public sealed record HandshakeRequest(
    string Platform,
    string Channel,
    string ClientVersion,
    int ApiContractVersion,
    string? BuildId = null,
    string? GitCommit = null,
    int? LocalSchemaVersion = null,
    string? WindowsBuild = null);

/// <summary>The supported API contract range.</summary>
/// <param name="Minimum">Lowest contract version the server accepts.</param>
/// <param name="Maximum">Highest contract version the server accepts.</param>
public sealed record ApiContractRange(int Minimum, int Maximum);

/// <summary>Integrity material for the artifact a client is being directed to.</summary>
/// <param name="Sha256">Artifact hash.</param>
/// <param name="Signature">Artifact signature.</param>
public sealed record ArtifactDescriptor(string? Sha256, string? Signature);

/// <summary>
/// The release authority's answer.
/// </summary>
/// <remarks>
/// <para>
/// Reconciles the two shapes the repository previously carried: the response in
/// <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c> and the richer example in
/// <c>config/release-policy.example.json</c>. See
/// <c>docs/adr/ADR-0005-release-handshake-contract.md</c>.
/// </para>
/// <para>
/// <see cref="BlocksProtectedMutations"/> is informational to the client. The
/// server does not rely on the client honoring it; the same decision is enforced
/// on every protected request.
/// </para>
/// </remarks>
/// <param name="Platform">Echo of the requested platform.</param>
/// <param name="Channel">Echo of the requested ring.</param>
/// <param name="Policy">One of NONE, AVAILABLE, RECOMMENDED, MANDATORY, REVOKED.</param>
/// <param name="Reason">Human-readable explanation of the decision.</param>
/// <param name="BlocksProtectedMutations">Whether protected mutations are refused for this client.</param>
/// <param name="LatestVersion">Newest published version, when known.</param>
/// <param name="MinimumSupportedVersion">Lowest supported version, when known.</param>
/// <param name="ApiContract">Supported contract range.</param>
/// <param name="SecurityEpoch">Current security epoch.</param>
/// <param name="MandatoryAfterUtc">Instant after which updating becomes mandatory.</param>
/// <param name="KillSwitch">Whether the ring is under a kill switch.</param>
/// <param name="RollbackTarget">Version to roll back to, when one is designated.</param>
/// <param name="Artifact">Integrity material for the target artifact.</param>
public sealed record HandshakeResponse(
    string Platform,
    string Channel,
    string Policy,
    string Reason,
    bool BlocksProtectedMutations,
    string? LatestVersion,
    string? MinimumSupportedVersion,
    ApiContractRange ApiContract,
    int SecurityEpoch,
    DateTimeOffset? MandatoryAfterUtc,
    bool KillSwitch,
    string? RollbackTarget,
    ArtifactDescriptor? Artifact);
