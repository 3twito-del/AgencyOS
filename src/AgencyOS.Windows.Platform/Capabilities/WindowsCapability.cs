namespace AgencyOS.Windows.Platform.Capabilities;

/// <summary>
/// Whether a device-local language model can run here.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>Microsoft.Windows.AI.AIFeatureReadyState</c> rather than inventing
/// a scale. The distinction between "this device cannot" and "this device could
/// once something is installed" is the difference between a message telling
/// somebody to give up and one telling them to wait (ADR-0035).
/// </para>
/// <para>
/// <see cref="Unknown"/> is what a probe returns when it could not ask — an older
/// Windows build, a missing runtime, a WinRT call that threw. It is deliberately
/// distinct from <see cref="NotSupported"/>: not knowing is not the same as
/// knowing the answer is no, and only one of them is worth retrying.
/// </para>
/// </remarks>
public enum LocalModelReadiness
{
    /// <summary>The probe could not determine the state.</summary>
    Unknown = 0,

    /// <summary>This device cannot run the model. No amount of waiting changes it.</summary>
    NotSupported,

    /// <summary>Supported, and not yet installed or provisioned.</summary>
    NotReady,

    /// <summary>Ready to run.</summary>
    Ready,
}

/// <summary>
/// A compute device a local model could execute on.
/// </summary>
/// <remarks>
/// Reported only when it was actually detected through
/// <c>Microsoft.Windows.AI.MachineLearning.ExecutionProviderCatalog</c>. Nothing
/// here is inferred from a CPU or GPU product name: a marketing string is not a
/// capability, and a build that guessed would offer somebody an NPU that cannot
/// run the model they chose (§28, ADR-0035).
/// </remarks>
public enum ExecutionDevice
{
    Cpu = 1,
    Gpu = 2,
    Npu = 3,
}

/// <summary>
/// One execution provider the machine reported.
/// </summary>
/// <param name="Name">
/// The provider's own name, for diagnostics. Not parsed to infer anything.
/// </param>
/// <param name="Device">Which kind of device it drives.</param>
/// <param name="IsReady">Whether it is registered and usable now.</param>
public sealed record ExecutionProviderInfo(string Name, ExecutionDevice Device, bool IsReady);

/// <summary>
/// What this workstation can actually do.
/// </summary>
/// <remarks>
/// <para>
/// Normalized AgencyOS types, never WinRT ones. The rest of the client — and the
/// contract it reports to the server — must not depend on the shape of a Windows
/// API, or replacing that API becomes a change to the server contract (§G).
/// </para>
/// <para>
/// <see cref="Detail"/> carries why, in words safe for a diagnostic paste. It
/// names no record and no person.
/// </para>
/// </remarks>
public sealed record WindowsCapabilityReport(
    LocalModelReadiness LocalModel,
    IReadOnlyList<ExecutionProviderInfo> Providers,
    string Detail)
{
    /// <summary>What a probe reports when it could not ask at all.</summary>
    public static WindowsCapabilityReport Unknown(string detail) =>
        new(LocalModelReadiness.Unknown, [], detail);

    /// <summary>Whether a device-local run could be attempted right now.</summary>
    public bool CanRunLocally => LocalModel == LocalModelReadiness.Ready;

    /// <summary>The devices reported as ready, strongest first.</summary>
    /// <remarks>
    /// Ordering is a preference, not a promise: which device a model actually runs
    /// on is the provider's decision, and AgencyOS reports what happened rather
    /// than asserting what will (§29).
    /// </remarks>
    public IReadOnlyList<ExecutionDevice> ReadyDevices =>
    [
        .. Providers
            .Where(x => x.IsReady)
            .Select(x => x.Device)
            .Distinct()
            .OrderByDescending(x => x),
    ];
}

/// <summary>
/// Asks Windows what it can do.
/// </summary>
/// <remarks>
/// <para>
/// An interface so the answer can be fixed in a test. CI has no NPU, no GPU worth
/// the name and no provisioned language model, and a suite that could only run
/// where the hardware exists would test nothing on the machine that gates
/// promotion (§G, §63).
/// </para>
/// <para>
/// Implementations never throw. A probe that failed loudly would take down the
/// shell over a capability question, and "I could not tell" is a real answer that
/// the rest of the client already has to handle.
/// </para>
/// </remarks>
public interface IWindowsCapabilityProbe
{
    /// <summary>Reports what this device can do. Never throws.</summary>
    ValueTask<WindowsCapabilityReport> ProbeAsync(CancellationToken cancellationToken = default);
}
