using System.Collections.Concurrent;
using AgencyOS.Application.Ai;
using AgencyOS.Domain.Ai;

namespace AgencyOS.Infrastructure.Ai;

/// <summary>
/// A model provider that does exactly what a test told it to.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is not a stub, it is the authoritative test infrastructure.</strong>
/// CI has no provider credential and must not need one: making a green build
/// depend on somebody else's service being reachable would mean the build is red
/// for reasons that have nothing to do with the code, and would make the
/// prompt-injection suite untestable — no real provider will emit a forged
/// approval on demand, and that is precisely the response that has to be proved
/// harmless (§74, §86).
/// </para>
/// <para>
/// It is registered always, in every environment. A build where the fake exists
/// only in tests is a build where the tests exercise a different composition from
/// the one that ships, which is how M10's provider seam earned its own fake.
/// </para>
/// </remarks>
public sealed class FakeModelProvider : IModelProvider
{
    /// <summary>The key this provider answers to.</summary>
    public const string ProviderKey = "fake";

    private readonly ConcurrentQueue<ModelResponse> _scripted = new();

    /// <summary>Every request it was given, in order, for assertions.</summary>
    /// <remarks>
    /// The prompt-injection tests read these: proving that hidden content did not
    /// reach the model means looking at what was actually sent, not at what the
    /// answer said (§49).
    /// </remarks>
    public List<ModelRequest> Requests { get; } = [];

    public string Key => ProviderKey;

    /// <summary>Always true. It needs no credential, which is the point.</summary>
    public bool IsConfigured => true;

    /// <summary>Queues the next answer.</summary>
    public FakeModelProvider Script(ModelResponse response)
    {
        _scripted.Enqueue(response);

        return this;
    }

    /// <summary>Queues a plain answer with no tool calls.</summary>
    public FakeModelProvider ScriptText(string text) =>
        Script(new ModelResponse(text, [], new ModelUsage(100, 50)));

    /// <summary>Queues a single tool request.</summary>
    public FakeModelProvider ScriptToolCall(string name, string argumentsJson) =>
        Script(new ModelResponse(
            null,
            [new RequestedToolCall($"call-{Guid.CreateVersion7()}", name, argumentsJson)],
            new ModelUsage(100, 20)));

    /// <summary>Queues a provider failure of a given kind.</summary>
    public FakeModelProvider ScriptFailure(AgentFailureKind kind, string detail = "scripted") =>
        Script(ModelResponse.Failed(kind, detail));

    /// <summary>Clears the script and the record of what was sent.</summary>
    public void Reset()
    {
        while (_scripted.TryDequeue(out _))
        {
        }

        Requests.Clear();
    }

    public Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(request);

        if (_scripted.TryDequeue(out ModelResponse? scripted))
        {
            return Task.FromResult(scripted);
        }

        // An unscripted call is a test that did not say what should happen, and
        // answering it with something plausible would let such a test pass by
        // accident. It fails as a provider error instead, which is a state the
        // runtime already handles and which shows up in the run.
        return Task.FromResult(ModelResponse.Failed(
            AgentFailureKind.ProviderUnavailable,
            "The fake provider was called without a scripted response."));
    }
}
