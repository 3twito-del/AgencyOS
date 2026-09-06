namespace AgencyOS.Domain.Releases;

/// <summary>
/// The release rings defined in <c>docs/05_RELEASE_ENGINEERING.md</c>.
/// </summary>
/// <remarks>
/// All seven rings are modelled, including <see cref="Rc"/>. Note that
/// <c>config/release-channels.yaml</c> currently declares only six channels and
/// has no <c>rc</c> entry, so an RC package identity and endpoint environment
/// still need to be added there before that ring can actually be shipped.
/// </remarks>
public enum ReleaseRing
{
    Forge = 1,
    Lab = 2,
    Nightly = 3,
    Alpha = 4,
    Beta = 5,
    Rc = 6,
    Stable = 7,
}

/// <summary>Parsing and formatting for <see cref="ReleaseRing"/> using the wire names in config.</summary>
public static class ReleaseRingNames
{
    /// <summary>Rings permitted to hold real, canonical data.</summary>
    /// <remarks>
    /// Mirrors <c>canonical_data_allowed_from: alpha</c> in
    /// <c>config/version-policy.yaml</c>. FORGE and LAB are excluded by design.
    /// </remarks>
    public static bool AllowsRealData(ReleaseRing ring) => ring >= ReleaseRing.Alpha;

    public static string ToWireName(ReleaseRing ring) => ring switch
    {
        ReleaseRing.Forge => "forge",
        ReleaseRing.Lab => "lab",
        ReleaseRing.Nightly => "nightly",
        ReleaseRing.Alpha => "alpha",
        ReleaseRing.Beta => "beta",
        ReleaseRing.Rc => "rc",
        ReleaseRing.Stable => "stable",
        _ => "unknown",
    };

    public static bool TryParse(string? value, out ReleaseRing ring)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "forge": ring = ReleaseRing.Forge; return true;
            case "lab": ring = ReleaseRing.Lab; return true;
            case "nightly": ring = ReleaseRing.Nightly; return true;
            case "alpha": ring = ReleaseRing.Alpha; return true;
            case "beta": ring = ReleaseRing.Beta; return true;
            case "rc": ring = ReleaseRing.Rc; return true;
            case "stable": ring = ReleaseRing.Stable; return true;
            default: ring = default; return false;
        }
    }
}
