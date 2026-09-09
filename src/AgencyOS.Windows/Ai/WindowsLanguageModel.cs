using System;
using System.Threading;
using System.Threading.Tasks;
using AgencyOS.Windows.Platform.Capabilities;
using AgencyOS.Windows.Platform.LocalInference;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;

namespace AgencyOS.Windows.Ai;

/// <summary>
/// The device-local model, over the Windows AI text API.
/// </summary>
/// <remarks>
/// <para>
/// One prompt in, one answer out. The API offers more —
/// <c>GenerateStructuredJsonResponseAsync</c>, embeddings, low-rank adapters —
/// and none of it is needed to draft a relationship brief, which is the only
/// device-local workload M13 ships (§34, ADR-0035).
/// </para>
/// <para>
/// <strong>No tool support, declared truthfully.</strong> The inspected surface
/// has no function-calling contract of any kind, so the M12 gateway is told
/// <c>SupportsTools: false</c>. Asking a model to print JSON and treating that as
/// a tool request would be emulation dressed as capability, and the approval
/// protocol would then be validating something the provider never actually
/// offered (§H).
/// </para>
/// <para>
/// Every failure is a category. A local runtime's exception text can carry a file
/// path, a model identifier or a fragment of the prompt, and this value travels
/// to the server and onto a screen (§V).
/// </para>
/// </remarks>
internal sealed class WindowsLanguageModel : ILocalLanguageModel
{
    private readonly IWindowsCapabilityProbe _probe;

    public WindowsLanguageModel(IWindowsCapabilityProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        _probe = probe;
    }

    /// <inheritdoc />
    public async ValueTask<LocalInferenceResult> GenerateAsync(
        string prompt,
        int maxOutputTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        // Asked again rather than trusted from whenever the shell last looked. A
        // model can be uninstalled, and a run that got this far on a stale answer
        // would fail inside the provider instead of here.
        WindowsCapabilityReport capability = await _probe
            .ProbeAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!capability.CanRunLocally)
        {
            return LocalInferenceResult.Refused(
                capability.LocalModel == LocalModelReadiness.NotReady
                    ? LocalInferenceFailure.LocalModelNotReady
                    : LocalInferenceFailure.LocalProviderUnavailable);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            LanguageModel model = await LanguageModel
                .CreateAsync()
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            using (model)
            {
                LanguageModelResponseResult result = await model
                    .GenerateResponseAsync(prompt)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);

                return string.IsNullOrWhiteSpace(result.Text)
                    ? LocalInferenceResult.Refused(LocalInferenceFailure.Unexpected)
                    : new LocalInferenceResult(
                        result.Text,
                        LocalInferenceFailure.None,
                        capability.ReadyDevices.Count > 0 ? capability.ReadyDevices[0] : null);
            }
        }
        catch (OperationCanceledException)
        {
            // A person changing their mind, not a failure of the device.
            return LocalInferenceResult.Refused(LocalInferenceFailure.Cancelled);
        }
#pragma warning disable CA1031 // A local runtime is somebody else's code.
        catch (Exception)
#pragma warning restore CA1031
        {
            return LocalInferenceResult.Refused(LocalInferenceFailure.Unexpected);
        }
    }
}
