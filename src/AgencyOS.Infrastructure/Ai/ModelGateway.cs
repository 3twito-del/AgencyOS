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
    bool IsEnabled = true);

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
                model.IsEnabled);
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

        _models = models;
    }

    public IReadOnlyList<ModelDescriptor> Catalog => [.. _models.Values.Where(x => x.IsEnabled)];

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
