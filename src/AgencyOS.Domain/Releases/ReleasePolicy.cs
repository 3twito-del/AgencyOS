using AgencyOS.Domain.Common;

namespace AgencyOS.Domain.Releases;

/// <summary>
/// The release authority's policy for one platform and ring, and the rule that
/// decides what a given client may do.
/// </summary>
/// <remarks>
/// <para>
/// Implements <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>. Evaluation lives on the
/// aggregate rather than in a service so the decision cannot be reimplemented
/// slightly differently at a second call site.
/// </para>
/// <para>
/// Severity order matters and is deliberate: revocation is checked before
/// contract compatibility, and contract compatibility before version currency.
/// The most severe applicable state wins.
/// </para>
/// </remarks>
public sealed class ReleasePolicy
{
    private ReleasePolicy()
    {
    }

    public string Platform { get; private set; } = string.Empty;

    public ReleaseRing Ring { get; private set; }

    public string LatestVersion { get; private set; } = string.Empty;

    public string MinimumSupportedVersion { get; private set; } = string.Empty;

    /// <summary>
    /// The policy reported to a client that is supported but not current.
    /// Constrained to a non-blocking state.
    /// </summary>
    public UpdatePolicy BehindPolicy { get; private set; }

    public int ApiContractMinimum { get; private set; }

    public int ApiContractMaximum { get; private set; }

    public int SecurityEpoch { get; private set; }

    /// <summary>When set, every client on this platform and ring is refused.</summary>
    public bool KillSwitch { get; private set; }

    public DateTimeOffset? MandatoryAfterUtc { get; private set; }

    public string? RollbackTarget { get; private set; }

    /// <summary>Specific versions withdrawn from service.</summary>
    public IReadOnlyList<string> RevokedVersions { get; private set; } = [];

    public string? ArtifactSha256 { get; private set; }

    public string? ArtifactSignature { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ReleasePolicy Create(
        string platform,
        ReleaseRing ring,
        string latestVersion,
        string minimumSupportedVersion,
        int apiContractMinimum,
        int apiContractMaximum,
        DateTimeOffset now,
        UpdatePolicy behindPolicy = UpdatePolicy.Recommended,
        int securityEpoch = 1,
        bool killSwitch = false,
        DateTimeOffset? mandatoryAfterUtc = null,
        string? rollbackTarget = null,
        IReadOnlyList<string>? revokedVersions = null,
        string? artifactSha256 = null,
        string? artifactSignature = null)
    {
        if (behindPolicy is not (UpdatePolicy.None or UpdatePolicy.Available or UpdatePolicy.Recommended))
        {
            throw new DomainException(
                "BehindPolicy describes a supported-but-not-current client and must be a non-blocking state.");
        }

        if (apiContractMinimum < 1 || apiContractMaximum < apiContractMinimum)
        {
            throw new DomainException("API contract range is invalid.");
        }

        if (!ClientVersion.TryParse(latestVersion, out ClientVersion? latest))
        {
            throw new DomainException($"LatestVersion '{latestVersion}' is not a valid version.");
        }

        if (!ClientVersion.TryParse(minimumSupportedVersion, out ClientVersion? minimum))
        {
            throw new DomainException($"MinimumSupportedVersion '{minimumSupportedVersion}' is not a valid version.");
        }

        if (minimum > latest)
        {
            throw new DomainException("MinimumSupportedVersion cannot exceed LatestVersion.");
        }

        return new ReleasePolicy
        {
            Platform = Ensure.NotBlankMax(platform, nameof(platform), 64),
            Ring = ring,
            LatestVersion = latest.Text,
            MinimumSupportedVersion = minimum.Text,
            BehindPolicy = behindPolicy,
            ApiContractMinimum = apiContractMinimum,
            ApiContractMaximum = apiContractMaximum,
            SecurityEpoch = securityEpoch,
            KillSwitch = killSwitch,
            MandatoryAfterUtc = mandatoryAfterUtc,
            RollbackTarget = rollbackTarget,
            RevokedVersions = revokedVersions is null ? [] : [.. revokedVersions],
            ArtifactSha256 = artifactSha256,
            ArtifactSignature = artifactSignature,
            UpdatedAt = now,
        };
    }

    /// <summary>Withdraws a specific build from service.</summary>
    public void RevokeVersion(string version, DateTimeOffset now)
    {
        if (!ClientVersion.TryParse(version, out ClientVersion? parsed))
        {
            throw new DomainException($"'{version}' is not a valid version.");
        }

        if (RevokedVersions.Contains(parsed.Text, StringComparer.Ordinal))
        {
            return;
        }

        RevokedVersions = [.. RevokedVersions, parsed.Text];
        UpdatedAt = now;
    }

    /// <summary>Refuses every client on this platform and ring.</summary>
    public void EngageKillSwitch(DateTimeOffset now)
    {
        KillSwitch = true;
        UpdatedAt = now;
    }

    /// <summary>Decides what <paramref name="client"/> is permitted to do.</summary>
    public ReleaseDecision Evaluate(ClientIdentity client, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(client);

        // 1. Withdrawal beats everything. A revoked build never writes canonical data.
        if (KillSwitch)
        {
            return Decide(UpdatePolicy.Revoked, "Release channel kill switch is engaged.", blocks: true);
        }

        if (RevokedVersions.Contains(client.Version.Text, StringComparer.Ordinal))
        {
            return Decide(
                UpdatePolicy.Revoked,
                $"Build {client.Version.Text} has been revoked.",
                blocks: true);
        }

        // 2. Protocol incompatibility. The server cannot honor a contract it does not speak.
        if (client.ApiContractVersion < ApiContractMinimum || client.ApiContractVersion > ApiContractMaximum)
        {
            return Decide(
                UpdatePolicy.Mandatory,
                $"API contract {client.ApiContractVersion} is outside the supported range "
                    + $"{ApiContractMinimum}-{ApiContractMaximum}.",
                blocks: true);
        }

        ClientVersion minimum = ClientVersion.Parse(MinimumSupportedVersion);
        ClientVersion latest = ClientVersion.Parse(LatestVersion);

        // 3. Below the supported floor.
        if (client.Version < minimum)
        {
            return Decide(
                UpdatePolicy.Mandatory,
                $"Version {client.Version.Text} is below the minimum supported version {MinimumSupportedVersion}.",
                blocks: true);
        }

        // 4. Supported, but a deadline has passed.
        if (client.Version < latest && MandatoryAfterUtc is { } deadline && now >= deadline)
        {
            return Decide(
                UpdatePolicy.Mandatory,
                $"Update deadline of {deadline:O} has passed.",
                blocks: true);
        }

        // 5. Supported and behind, or current.
        return client.Version < latest
            ? Decide(BehindPolicy, $"A newer version ({LatestVersion}) is available.", blocks: false)
            : Decide(UpdatePolicy.None, "Client is current.", blocks: false);
    }

    private ReleaseDecision Decide(UpdatePolicy policy, string reason, bool blocks) => new(
        policy,
        reason,
        blocks,
        LatestVersion,
        MinimumSupportedVersion,
        ApiContractMinimum,
        ApiContractMaximum,
        SecurityEpoch,
        KillSwitch,
        MandatoryAfterUtc,
        RollbackTarget);
}
