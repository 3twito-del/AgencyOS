namespace AgencyOS.Contracts.Ai;

// ------------------------------------------------------------------ requests

/// <param name="ModelKey">
/// Which device-local model the workstation intends to use. Recorded as
/// provenance and bound into the lease, so a result produced by a different one
/// is presenting something the lease was not issued for.
/// </param>
public sealed record IssueContextLeaseRequest(string ModelKey);

/// <param name="LeaseId">The lease this result is presented against.</param>
/// <param name="Text">
/// What the local model produced, or null when it produced nothing. Untrusted:
/// the server validates it exactly as it validates a cloud provider's answer.
/// </param>
/// <param name="Failure">
/// The client's account of why local execution did not happen. A category name,
/// never a provider message — a message from a local runtime can carry a file
/// path or a prompt fragment.
/// </param>
/// <param name="ExecutionDevice">
/// Which device the provider said it used. Provenance only; nothing depends on
/// it being true.
/// </param>
public sealed record SubmitLocalResultRequest(
    Guid LeaseId,
    string? Text = null,
    string? Failure = null,
    string? ExecutionDevice = null);

// ----------------------------------------------------------------- responses

/// <param name="Kind">
/// The context block's kind, as AgencyOS classified it — <c>Relationship</c>,
/// <c>Signal</c>, <c>Source</c> and so on.
/// </param>
/// <param name="Label">A short heading. Already bounded and already authorized.</param>
/// <param name="Content">
/// The material itself. Untrusted where <paramref name="IsUntrusted"/> says so,
/// which the client must preserve when it renders the prompt: the fence is part
/// of what AgencyOS authorized, not decoration the client may drop.
/// </param>
/// <param name="IsUntrusted">
/// Whether a person or an outside system wrote this. Carried so the workstation
/// cannot lose the distinction on the way to the model.
/// </param>
public sealed record AiContextBlockResponse(
    string Kind,
    string Label,
    string Content,
    bool IsUntrusted);

/// <summary>
/// Permission to run one run's context on this device, once.
/// </summary>
/// <remarks>
/// <para>
/// The envelope carries the assembled, authorized, policy-filtered context and
/// nothing else. No provider credential, no policy internals, no tenant data the
/// run did not ask for.
/// </para>
/// <para>
/// The fingerprint is returned so the client can detect its own corruption, not
/// so it can prove anything. The server recomputes it from what it assembled and
/// compares against the stored lease; a client-supplied fingerprint is evidence
/// of nothing (§B).
/// </para>
/// </remarks>
/// <param name="Prompt">
/// What AgencyOS wrote. The system framing, versioned in source control, and
/// never assembled from anything retrieved.
/// </param>
/// <param name="MaxOutputTokens">The ceiling the server expects to be honoured.</param>
public sealed record ContextLeaseResponse(
    Guid LeaseId,
    Guid AgentRunId,
    string Residency,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string ContextFingerprint,
    string ModelKey,
    string Prompt,
    IReadOnlyList<AiContextBlockResponse> Context,
    int MaxOutputTokens,
    int OmittedBlockCount);

/// <param name="Accepted">
/// Whether the result became part of the run. False is an ordinary outcome: a
/// lease may have lapsed, been consumed, or been invalidated by a cancellation
/// while the model was running.
/// </param>
/// <param name="Refusal">
/// Why not, as a category name. Deliberately coarse for anything
/// authorization-shaped, so a caller learns nothing about runs that are not
/// theirs.
/// </param>
public sealed record SubmitLocalResultResponse(
    bool Accepted,
    string Status,
    string? Refusal = null);

/// <summary>
/// What this build can execute, and where.
/// </summary>
/// <remarks>
/// Server-side capability only: which model keys exist, what residency each
/// carries, and whether the server holds what it needs to call them. It says
/// nothing about the workstation, because the workstation asks itself — a client
/// that had to be told what its own hardware can do would be trusting the server
/// with a question only it can answer.
/// </remarks>
/// <param name="Residency">
/// <c>ExternalCloud</c>, <c>OrganizationControlled</c> or <c>DeviceLocal</c>.
/// </param>
/// <param name="RequiresContextLease">
/// Whether running this model means taking a lease. True for anything that does
/// not execute inside the server process.
/// </param>
public sealed record AiExecutionTargetResponse(
    string ModelKey,
    string ProviderKey,
    string Residency,
    bool RequiresContextLease,
    bool SupportsTools,
    bool SupportsStructuredOutput,
    int? MaxContextTokens);
