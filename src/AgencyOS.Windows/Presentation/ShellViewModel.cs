using AgencyOS.Contracts;

namespace AgencyOS.Windows.Presentation;

/// <summary>
/// View model for the shell window.
/// </summary>
/// <remarks>
/// The MVVM seam required by <c>docs/12_WINDOWS_NATIVE.md</c>. It is intentionally
/// a plain class: no MVVM framework is taken as a dependency until a real binding
/// surface justifies one.
/// </remarks>
public sealed class ShellViewModel
{
    /// <summary>Gets the build identity of this client.</summary>
    public string BuildLine =>
        $"{BuildInfo.Version} · {BuildInfo.Channel} · build {BuildInfo.BuildId} · {BuildInfo.GitCommit}";

    /// <summary>Gets the API contract this client speaks.</summary>
    public string ContractLine => $"API contract v{ApiContract.Current}";
}
