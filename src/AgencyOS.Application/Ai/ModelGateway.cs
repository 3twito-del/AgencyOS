using AgencyOS.Domain.Ai;

namespace AgencyOS.Application.Ai;

/// <summary>
/// One message in a model conversation.
/// </summary>
/// <remarks>
/// <para>
/// The roles are the ones every provider has in some form. What matters is the
/// distinction between <see cref="ModelRole.System"/>, which AgencyOS writes, and
/// everything else, which it does not: nothing arriving from a document, a
/// message or a user typing can produce a system message, because that is the one
/// role providers treat as instruction (§7).
/// </para>
/// <para>
/// There is deliberately no role for "trusted content". Content is content; the
/// framing that says so lives in the context block that carries it.
/// </para>
/// </remarks>
public enum ModelRole
{
    /// <summary>Written by AgencyOS. Never derived from retrieved content.</summary>
    System = 1,

    /// <summary>The task, and the context assembled for it.</summary>
    User = 2,

    /// <summary>What the model said last turn.</summary>
    Assistant = 3,

    /// <summary>What a tool answered.</summary>
    Tool = 4,
}

/// <param name="ToolCallId">
/// The provider's identifier for the call this answers, for a
/// <see cref="ModelRole.Tool"/> message. Correlation only: it carries no
/// authority, and AgencyOS matches it against its own request row rather than
/// trusting it (§48).
/// </param>
public sealed record ModelMessage(ModelRole Role, string Content, string? ToolCallId = null);

/// <summary>
/// A tool as the model is told about it.
/// </summary>
/// <remarks>
/// Derived from the registry, never from model output. A model that describes a
/// tool it would like to exist has described nothing: the list it is offered is
/// the list AgencyOS registered, and a call to anything else is refused before it
/// reaches a handler (§8, §32).
/// </remarks>
public sealed record ToolDefinition(string Name, string Description, string JsonSchema);

/// <summary>
/// A tool the model asked for, as the provider reported it.
/// </summary>
/// <remarks>
/// Everything here is untrusted. The name may not be a registered tool, the
/// arguments may not match the schema, and both are checked before anything
/// happens (§38).
/// </remarks>
public sealed record RequestedToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>What a provider reported about its own work.</summary>
/// <remarks>
/// Token counts where the provider gives them, and nothing derived. Cost is
/// deliberately absent: a price is a versioned configuration fact rather than a
/// property of a response, and computing one here would bake today's rate card
/// into the execution history (§4, §44).
/// </remarks>
public sealed record ModelUsage(int? InputTokens, int? OutputTokens);

/// <summary>Why a provider call did not produce an answer.</summary>
public sealed record ModelFailure(AgentFailureKind Kind, string Detail);

/// <param name="Model">
/// Which model to call, by the key the catalog uses. Never a name from model
/// output and never a hard-coded literal in a call site (§4).
/// </param>
/// <param name="Tools">
/// What the model may ask for on this turn. An empty list means it may ask for
/// nothing, which is how the brief-only agents run.
/// </param>
/// <param name="JsonSchema">
/// When set, the shape the answer must take. Validated on arrival; a response
/// that does not fit is a failure rather than something to salvage (§21).
/// </param>
public sealed record ModelRequest(
    string Model,
    IReadOnlyList<ModelMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    string? JsonSchema = null,
    int? MaxOutputTokens = null,
    TimeSpan? Timeout = null);

/// <param name="Text">What the model said, when it said anything.</param>
/// <param name="ToolCalls">What it asked for. Untrusted, and validated later.</param>
/// <param name="Failure">Set when the call did not produce an answer at all.</param>
public sealed record ModelResponse(
    string? Text,
    IReadOnlyList<RequestedToolCall> ToolCalls,
    ModelUsage Usage,
    ModelFailure? Failure = null)
{
    public bool Succeeded => Failure is null;

    public static ModelResponse Failed(AgentFailureKind kind, string detail) =>
        new(null, [], new ModelUsage(null, null), new ModelFailure(kind, detail));
}

/// <summary>
/// One provider AgencyOS can call.
/// </summary>
/// <remarks>
/// <para>
/// The entire provider-specific world lives behind this interface: request shapes,
/// authentication, retry semantics, error taxonomies, streaming. Nothing outside
/// the AI infrastructure project references a provider SDK type, which is what
/// makes a future local provider a matter of adding a class rather than a matter
/// of finding every call site (§2, §58).
/// </para>
/// <para>
/// An implementation is expected to translate its own failures into
/// <see cref="AgentFailureKind"/> rather than throwing provider exceptions
/// outward. A caller that had to catch four SDKs' exception hierarchies would end
/// up catching everything, and a swallowed authorization error looks exactly like
/// a timeout to the person waiting.
/// </para>
/// </remarks>
public interface IModelProvider
{
    /// <summary>The key this provider is known by in configuration and policy.</summary>
    string Key { get; }

    /// <summary>Whether the server holds a usable credential for it.</summary>
    /// <remarks>
    /// Says nothing about whether any organization may use it. That is the
    /// provider policy's question, and a configured credential is precisely the
    /// situation §43 warns about (ADR-0031).
    /// </remarks>
    bool IsConfigured { get; }

    Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The one way AgencyOS calls a model.
/// </summary>
/// <remarks>
/// <para>
/// Selects a provider, applies the timeout and the output bound, records the
/// invocation and normalizes failures. Agents call this; they never hold a
/// provider.
/// </para>
/// <para>
/// It does not decide whether the context may be transmitted. That is settled
/// before a request is built, by the context assembler and the data policy, which
/// is the boundary §34 describes — by the time a request reaches here, every block
/// in it has already been permitted to leave.
/// </para>
/// </remarks>
public interface IModelGateway
{
    /// <summary>Calls the model named in the request.</summary>
    Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>The models this build can call, as configured.</summary>
    IReadOnlyList<ModelDescriptor> Catalog { get; }

    /// <summary>Looks a model up, or null when it is unknown or disabled.</summary>
    ModelDescriptor? Describe(string modelKey);
}

/// <summary>
/// What AgencyOS believes about a model.
/// </summary>
/// <remarks>
/// <para>
/// Every field is configuration rather than inference. A model's name says nothing
/// about whether it supports tool use or structured output, marketing material
/// says less, and a build that guessed would fail at the worst moment — mid-run,
/// against a provider, in front of a user (§4).
/// </para>
/// <para>
/// Context limits are stated only where somebody configured them. An unset limit
/// means unknown, and unknown is handled by bounding what AgencyOS sends rather
/// than by assuming a number.
/// </para>
/// </remarks>
public sealed record ModelDescriptor(
    string Key,
    string ProviderKey,
    string ModelId,
    bool SupportsTools,
    bool SupportsStructuredOutput,
    bool SupportsStreaming,
    int? MaxContextTokens,
    bool IsEnabled);
