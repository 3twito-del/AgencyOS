using System.Diagnostics;
using AgencyOS.Application.Ai;
using AgencyOS.Domain.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgencyOS.Infrastructure.Ai;

/// <summary>
/// What models this server can call, and on what terms.
/// </summary>
/// <remarks>
/// Configuration, never inference. A model identifier says nothing about whether
/// the model supports tool use or structured output, and a build that assumed
/// would discover it was wrong mid-run, in front of somebody (§4).
/// </remarks>
public sealed class AiOptions
{
    public const string Section = "AgencyOS:Ai";

    /// <summary>The models this build may call.</summary>
    public List<ModelOptions> Models { get; init; } = [];

    /// <summary>
    /// Which model an agent uses when nothing says otherwise.
    /// </summary>
    /// <remarks>
    /// Defaults to the deterministic fake, so a server that has been given no AI
    /// configuration at all is one where AI is inert rather than one that reaches
    /// for a provider nobody chose.
    /// </remarks>
    public string DefaultModelKey { get; init; } = "fake-default";
}

/// <param name="Key">How AgencyOS refers to this model. Stable across provider renames.</param>
public sealed record ModelOptions(
    string Key,
    string Provider,
    string ModelId,
    bool SupportsTools = true,
    bool SupportsStructuredOutput = true,
    bool SupportsStreaming = false,
    int? MaxContextTokens = null,
    bool IsEnabled = true,
    ModelResidency Residency = ModelResidency.ExternalCloud);

/// <summary>
/// The device-local model AgencyOS knows how to ask a Windows client to run.
/// </summary>
/// <remarks>
/// Declared here rather than in configuration because its capabilities are a
/// property of the Windows API, not of a deployment. The inspected
/// <c>Microsoft.Windows.AI.Text.LanguageModel</c> surface offers
/// <c>GenerateResponseAsync</c> and <c>GenerateStructuredJsonResponseAsync</c>
/// and no function-calling contract of any kind, so tool support is declared
/// false — truthfully, rather than emulated by asking a model to print JSON and
/// treating that as a tool call (§H, ADR-0035).
/// </remarks>
public static class WindowsLocalModel
{
    /// <summary>How AgencyOS refers to the Windows device-local model.</summary>
    public const string Key = "windows-local";

    /// <summary>The provider key it belongs to.</summary>
    public const string ProviderKey = "windows";

    internal static ModelDescriptor Descriptor { get; } = new(
        Key,
        ProviderKey,
        ModelId: "windows-ai-languagemodel",
        SupportsTools: false,
        SupportsStructuredOutput: true,
        SupportsStreaming: false,
        MaxContextTokens: null,
        IsEnabled: true,
        Residency: ModelResidency.DeviceLocal);
}

/// <summary>
/// The one way AgencyOS calls a model.
/// </summary>
/// <remarks>
/// <para>
/// Chooses the provider, applies the timeout, records the outcome and normalizes
/// failure. Everything provider-shaped stops here (§2).
/// </para>
/// <para>
/// It does not decide whether the context may be transmitted. By the time a
/// request arrives, the assembler and the data policy have already settled that,
/// and a second opinion here would be a second place for the answer to differ
/// (§34).
/// </para>
/// </remarks>
public sealed class ModelGateway : IModelGateway
{
    private readonly IReadOnlyDictionary<string, IModelProvider> _providers;
    private readonly IReadOnlyDictionary<string, ModelDescriptor> _models;
    private readonly string _defaultKey;
    private readonly ILogger<ModelGateway> _logger;

    public ModelGateway(
        IEnumerable<IModelProvider> providers,
        IOptions<AiOptions> options,
        ILogger<ModelGateway> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);

        _providers = providers.ToDictionary(x => x.Key, StringComparer.Ordinal);
        _logger = logger;

        Dictionary<string, ModelDescriptor> models = new(StringComparer.Ordinal);

        foreach (ModelOptions model in options.Value.Models)
        {
            models[model.Key] = new ModelDescriptor(
                model.Key,
                model.Provider,
                model.ModelId,
                model.SupportsTools,
                model.SupportsStructuredOutput,
                model.SupportsStreaming,
                model.MaxContextTokens,
                model.IsEnabled,
                model.Residency);
        }

        // The deterministic model always exists. A server with no AI configuration
        // can still run every agent against the fake, which is what makes CI
        // independent of anybody's network (§86).
        models.TryAdd(
            "fake-default",
            new ModelDescriptor(
                "fake-default",
                FakeModelProvider.ProviderKey,
                "deterministic",
                SupportsTools: true,
                SupportsStructuredOutput: true,
                SupportsStreaming: false,
                MaxContextTokens: null,
                IsEnabled: true));

        // The device-local model always exists, for the same reason the
        // deterministic one does: a server with no AI configuration can still
        // describe what a Windows client could run, and CI can exercise the whole
        // lease protocol without a model file or an NPU (§63, ADR-0035).
        models.TryAdd(WindowsLocalModel.Key, WindowsLocalModel.Descriptor);

        _models = models;
        _defaultKey = options.Value.DefaultModelKey;
    }

    public IReadOnlyList<ModelDescriptor> Catalog => [.. _models.Values.Where(x => x.IsEnabled)];

    public ModelDescriptor? Default => Describe(_defaultKey);

    public ModelDescriptor? Describe(string modelKey) =>
        _models.TryGetValue(modelKey, out ModelDescriptor? descriptor) && descriptor.IsEnabled
            ? descriptor
            : null;

    public async Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Describe(request.Model) is not { } descriptor)
        {
            return ModelResponse.Failed(
                AgentFailureKind.ProviderUnavailable,
                $"No model is configured under the key '{request.Model}'.");
        }

        // THE NO-FALLBACK GUARANTEE, at the layer everything goes through.
        //
        // A device-local model executes on the user's workstation and nowhere
        // else. Asking the gateway to complete one means some code path lost track
        // of where the material was permitted to go, and the only safe answer is a
        // refusal: quietly reaching for a cloud provider instead would move
        // material the policy admitted only on-device (§E, ADR-0035).
        if (descriptor.RequiresContextLease)
        {
            return ModelResponse.Failed(
                AgentFailureKind.LocalProviderUnavailable,
                $"'{descriptor.Key}' runs on the user's device under a context lease. "
                    + "The server does not execute it, and will not send its context "
                    + "anywhere else.");
        }

        if (!_providers.TryGetValue(descriptor.ProviderKey, out IModelProvider? provider))
        {
            return ModelResponse.Failed(
                AgentFailureKind.ProviderUnavailable,
                $"No provider is registered for '{descriptor.ProviderKey}'.");
        }

        if (!provider.IsConfigured)
        {
            return ModelResponse.Failed(
                AgentFailureKind.ProviderUnavailable,
                $"The {descriptor.ProviderKey} provider has no credential on this server.");
        }

        // Capability is checked rather than attempted. A model that cannot do
        // structured output will answer prose, and the run would fail later at
        // validation with a confusing message about the answer rather than a clear
        // one about the model (§4).
        if (request.JsonSchema is not null && !descriptor.SupportsStructuredOutput)
        {
            return ModelResponse.Failed(
                AgentFailureKind.ProviderInvalidResponse,
                $"The model '{descriptor.Key}' is not configured for structured output.");
        }

        if (request.Tools.Count > 0 && !descriptor.SupportsTools)
        {
            return ModelResponse.Failed(
                AgentFailureKind.ProviderInvalidResponse,
                $"The model '{descriptor.Key}' is not configured for tool use.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        if (request.Timeout is { } window)
        {
            timeout.CancelAfter(window);
        }

        Stopwatch clock = Stopwatch.StartNew();

        try
        {
            ModelResponse response = await provider
                .CompleteAsync(
                    request with { Model = descriptor.ModelId },
                    timeout.Token)
                .ConfigureAwait(false);

            // Duration, provider, model and outcome. Never the prompt, never the
            // answer, and never a fragment of either: telemetry is the easiest
            // place for confidential material to escape an access boundary,
            // because nothing about a log line asks who may read it (§53).
            _logger.LogInformation(
                "Model call complete. provider={Provider} model={Model} outcome={Outcome} "
                    + "elapsedMs={Elapsed} tools={ToolCalls}",
                descriptor.ProviderKey,
                descriptor.Key,
                response.Succeeded ? "ok" : response.Failure!.Kind.ToString(),
                clock.ElapsedMilliseconds,
                response.ToolCalls.Count);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled. Distinct from a timeout, because one is a
            // person changing their mind and the other is a provider not answering.
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Model call timed out. provider={Provider} model={Model} elapsedMs={Elapsed}",
                descriptor.ProviderKey,
                descriptor.Key,
                clock.ElapsedMilliseconds);

            return ModelResponse.Failed(
                AgentFailureKind.ProviderTimeout,
                "The provider did not answer within the time allowed.");
        }
#pragma warning disable CA1031 // A provider is somebody else's code; it may throw anything.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // The message is logged, never returned. A provider exception can carry
            // a request identifier, a partial prompt or an endpoint, and none of
            // those belongs in something a user reads (§68).
            _logger.LogError(
                ex,
                "Model call failed. provider={Provider} model={Model}",
                descriptor.ProviderKey,
                descriptor.Key);

            return ModelResponse.Failed(
                AgentFailureKind.Unexpected,
                "The provider failed in a way this build does not recognize.");
        }
    }
}
