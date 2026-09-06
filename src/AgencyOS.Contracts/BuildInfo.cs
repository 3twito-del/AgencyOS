using System.Globalization;
using System.Reflection;

namespace AgencyOS.Contracts;

/// <summary>
/// Build identity of the running AgencyOS assembly, read from assembly metadata
/// stamped at compile time by <c>Directory.Build.targets</c>.
/// </summary>
/// <remarks>
/// Governing documents: <c>docs/05_RELEASE_ENGINEERING.md</c> (rings and package
/// identity) and <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c> (the fields a client
/// presents to the release authority).
/// </remarks>
public static class BuildInfo
{
    private const string UnknownValue = "unknown";

    private static readonly Assembly SelfAssembly = typeof(BuildInfo).Assembly;

    /// <summary>Gets the informational version of this build, for example <c>0.1.0</c>.</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>
    /// Gets the release ring this build belongs to, for example <c>forge</c> or <c>alpha</c>.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>forge</c> when unset. Forge is the one ring that forbids real
    /// data outright (<c>config/release-channels.yaml</c>), so an unconfigured build
    /// cannot present itself as entitled to canonical data.
    /// </remarks>
    public static string Channel { get; } = ReadMetadata("AgencyOS.Channel");

    /// <summary>Gets the build identifier, for example <c>20260906.1842</c>.</summary>
    public static string BuildId { get; } = ReadMetadata("AgencyOS.BuildId");

    /// <summary>Gets the abbreviated git commit this build was produced from.</summary>
    public static string GitCommit { get; } = ReadMetadata("AgencyOS.GitCommit");

    /// <summary>
    /// Gets the API contract version recorded in build metadata, or <c>0</c> when absent.
    /// </summary>
    public static int ApiContractVersion { get; } = ResolveApiContractVersion();

    private static string ReadMetadata(string key)
    {
        foreach (AssemblyMetadataAttribute attribute in SelfAssembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, key, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(attribute.Value) ? UnknownValue : attribute.Value;
            }
        }

        return UnknownValue;
    }

    private static string ResolveVersion()
    {
        string? informational = SelfAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return UnknownValue;
        }

        // Source-link builds append "+<commit>"; the commit is exposed separately.
        int plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
        return plusIndex < 0 ? informational : informational[..plusIndex];
    }

    private static int ResolveApiContractVersion()
    {
        string raw = ReadMetadata("AgencyOS.ApiContractVersion");

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
    }
}
