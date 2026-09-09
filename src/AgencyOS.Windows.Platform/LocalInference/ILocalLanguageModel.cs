using AgencyOS.Windows.Platform.Capabilities;

namespace AgencyOS.Windows.Platform.LocalInference;

/// <summary>
/// Why a local model produced nothing.
/// </summary>
/// <remarks>
/// Categories, never a provider message. A local runtime's error text can carry a
/// file path, a model name or a fragment of the prompt, and this value is sent to
/// the server and shown to a person (§V, ADR-0035).
/// </remarks>
public enum LocalInferenceFailure
{
    None = 0,

    /// <summary>This device cannot run the model at all.</summary>
    LocalProviderUnavailable,

    /// <summary>Supported, but not provisioned yet.</summary>
    LocalModelNotReady,

    /// <summary>It took longer than the run allowed.</summary>
    ProviderTimeout,

    /// <summary>The user or the shell stopped it.</summary>
    Cancelled,

    /// <summary>It failed in a way this build does not recognize.</summary>
    Unexpected,
}

/// <summary>What a local model produced.</summary>
/// <param name="Text">The answer, or null when there was none.</param>
/// <param name="Device">
/// Which device the runtime said it used. Provenance for a diagnostic line, and
/// nothing depends on it: the server records it as the client's claim.
/// </param>
public sealed record LocalInferenceResult(
    string? Text,
    LocalInferenceFailure Failure = LocalInferenceFailure.None,
    ExecutionDevice? Device = null)
{
    /// <summary>Whether the model answered.</summary>
    public bool Succeeded => Failure == LocalInferenceFailure.None
        && !string.IsNullOrWhiteSpace(Text);

    /// <summary>A refusal of a given kind.</summary>
    public static LocalInferenceResult Refused(LocalInferenceFailure failure) =>
        new(null, failure);
}

/// <summary>
/// The device-local model, as AgencyOS needs it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrow: one prompt in, one answer out, with cancellation. The
/// inspected <c>Microsoft.Windows.AI.Text.LanguageModel</c> offers rather more —
/// structured JSON, embeddings, low-rank adapters, content-filter options — and
/// none of it is needed to draft a relationship brief. A wider interface would be
/// surface nobody uses and a second place to keep truthful.
/// </para>
/// <para>
/// <strong>There is no tool-calling method, because the API has no tool-calling
/// contract.</strong> Adding one would mean asking the model to print JSON and
/// treating that as a tool request, which is emulation dressed as capability —
/// and the M12 gateway would then be told this provider supports tools when it
/// does not (§H, ADR-0035).
/// </para>
/// <para>
/// An interface so CI can answer it deterministically. The machine that gates
/// promotion has no Copilot+ NPU, so a test suite that could only run where the
/// model exists would test nothing there (§63).
/// </para>
/// </remarks>
public interface ILocalLanguageModel
{
    /// <summary>
    /// Generates an answer, or says why it could not.
    /// </summary>
    /// <remarks>
    /// Never throws for an inference failure. Whether the model is missing, not
    /// provisioned or simply slow is a category the caller has to handle either
    /// way, and an exception would make the ordinary case exceptional.
    /// </remarks>
    ValueTask<LocalInferenceResult> GenerateAsync(
        string prompt,
        int maxOutputTokens,
        CancellationToken cancellationToken = default);
}
