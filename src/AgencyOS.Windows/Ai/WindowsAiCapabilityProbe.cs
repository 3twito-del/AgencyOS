using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgencyOS.Windows.Platform.Capabilities;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.MachineLearning;
using Microsoft.Windows.AI.Text;

namespace AgencyOS.Windows.Ai;

/// <summary>
/// Asks Windows what this machine can actually do.
/// </summary>
/// <remarks>
/// <para>
/// The real implementation, over the APIs the Windows App SDK 2.4.0 baseline
/// genuinely ships: <see cref="LanguageModel.GetReadyState"/> for the model and
/// <see cref="ExecutionProviderCatalog"/> for the compute devices. Both were
/// verified to compile and resolve against the pinned SDK before this class was
/// written — ADR-0035 records the evidence rather than the assumption.
/// </para>
/// <para>
/// <strong>Nothing is inferred from a product name.</strong> The catalogue is
/// asked what providers exist and whether each is ready; a CPU or GPU marketing
/// string is not a capability, and a build that guessed would offer somebody an
/// NPU that cannot run the model they chose (§28).
/// </para>
/// <para>
/// Never throws. A capability question must not take down the shell, and "I could
/// not tell" is a real answer the rest of the client already handles — which is
/// also what a machine without the runtime installed will produce.
/// </para>
/// </remarks>
internal sealed class WindowsAiCapabilityProbe : IWindowsCapabilityProbe
{
    /// <inheritdoc />
    public ValueTask<WindowsCapabilityReport> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        LocalModelReadiness readiness;
        string detail;

        try
        {
            readiness = Map(LanguageModel.GetReadyState());
            detail = Explain(readiness);
        }
#pragma warning disable CA1031 // A missing runtime throws whatever it likes.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Distinct from NotSupported on purpose: not knowing is not the same
            // as knowing the answer is no, and only one of them is worth retrying.
            return ValueTask.FromResult(WindowsCapabilityReport.Unknown(
                $"The Windows AI runtime could not be queried ({ex.GetType().Name})."));
        }

        return ValueTask.FromResult(
            new WindowsCapabilityReport(readiness, Providers(), detail));
    }

    /// <summary>
    /// Reads the execution providers the machine actually registered.
    /// </summary>
    /// <remarks>
    /// A provider's device is taken from its own name because that is the only
    /// thing the catalogue offers, and the match is deliberately conservative:
    /// anything that does not clearly announce itself as an NPU or a GPU is
    /// reported as CPU rather than guessed upward. Over-reporting an accelerator
    /// is the failure that leads somebody to expect performance that is not
    /// there (§29).
    /// </remarks>
    private static IReadOnlyList<ExecutionProviderInfo> Providers()
    {
        try
        {
            List<ExecutionProviderInfo> providers = [];

            foreach (ExecutionProvider provider in
                ExecutionProviderCatalog.GetDefault().FindAllProviders())
            {
                providers.Add(new ExecutionProviderInfo(
                    provider.Name,
                    Classify(provider.Name),
                    provider.ReadyState == ExecutionProviderReadyState.Ready));
            }

            return providers;
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            // An unavailable catalogue means no known providers, not a crash and
            // not an invented one.
            return [];
        }
    }

    private static ExecutionDevice Classify(string name) =>
        name.Contains("NPU", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Neural", StringComparison.OrdinalIgnoreCase)
            ? ExecutionDevice.Npu
            : name.Contains("GPU", StringComparison.OrdinalIgnoreCase)
                || name.Contains("DirectML", StringComparison.OrdinalIgnoreCase)
                ? ExecutionDevice.Gpu
                : ExecutionDevice.Cpu;

    private static LocalModelReadiness Map(AIFeatureReadyState state) => state switch
    {
        AIFeatureReadyState.Ready => LocalModelReadiness.Ready,
        AIFeatureReadyState.NotReady => LocalModelReadiness.NotReady,
        AIFeatureReadyState.DisabledByUser => LocalModelReadiness.NotSupported,
        AIFeatureReadyState.NotSupportedOnCurrentSystem => LocalModelReadiness.NotSupported,
        _ => LocalModelReadiness.Unknown,
    };

    /// <summary>
    /// What the state means, in words a person can act on.
    /// </summary>
    /// <remarks>
    /// The unsupported case is the one that matters most, because it is what every
    /// machine without a Copilot+ neural processor reports — including the
    /// workstation AgencyOS is developed on. Saying so plainly is better than an
    /// error that invites somebody to go looking for a fix (§F).
    /// </remarks>
    private static string Explain(LocalModelReadiness readiness) => readiness switch
    {
        LocalModelReadiness.Ready => "A device-local language model is ready.",
        LocalModelReadiness.NotReady =>
            "This device supports a local language model, and it is not installed yet.",
        LocalModelReadiness.NotSupported =>
            "This device has no supported local language model. Windows AI text "
                + "generation needs a Copilot+ PC with a neural processor.",
        _ => "Whether this device can run a local language model could not be determined.",
    };
}
