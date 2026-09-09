using AgencyOS.Contracts.Ai;

namespace AgencyOS.Client;

/// <summary>
/// The M13 device-local inference surface, as the Windows client sees it.
/// </summary>
/// <remarks>
/// <para>
/// Three calls, and the client's authority in all of them is nil. It cannot name
/// a residency — that belongs to the run. It cannot supply context — the server
/// assembles it. It cannot assert that a lease is valid — the server reassembles
/// and recomputes the fingerprint (ADR-0035).
/// </para>
/// <para>
/// ONLINE_ONLY, like the rest of the AI surface. A lease is permission to compute
/// now against material the server just authorized, and neither half of that
/// survives being queued.
/// </para>
/// </remarks>
public partial interface IAgencyOsApi : ILocalInferenceApi
{
}

/// <summary>
/// The three calls the device-local protocol needs, and no more.
/// </summary>
/// <remarks>
/// Segregated from <see cref="IAgencyOsApi"/> so the workstation's protocol
/// runner depends on what it uses. The runner carries authorized context between
/// a server and a local model; giving it the whole client surface would let a
/// future change reach for a business endpoint from inside an inference loop, and
/// would make it untestable without standing up two hundred members (ADR-0035).
/// </remarks>
public interface ILocalInferenceApi
{
    /// <summary>
    /// Takes a single-use lease and receives the context it covers.
    /// </summary>
    /// <remarks>
    /// One call, because issuing the lease and disclosing the context are one act.
    /// A client that held a lease for context it never received would be holding
    /// an authorization for nothing.
    /// </remarks>
    Task<ContextLeaseResponse> IssueAiContextLeaseAsync(
        Guid runId,
        IssueContextLeaseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns what the local model produced, or why it produced nothing.
    /// </summary>
    /// <remarks>
    /// A refusal comes back as a successful response saying it was refused: a
    /// lease may have lapsed or been withdrawn while the model ran, and the
    /// workstation needs to be told which rather than given an error to guess at.
    /// </remarks>
    Task<SubmitLocalResultResponse> SubmitAiLocalResultAsync(
        Guid runId,
        SubmitLocalResultRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>What this server can execute, and where.</summary>
    /// <remarks>
    /// Says nothing about this workstation's hardware. The workstation asks
    /// itself; a client told by the server what its own device can do would be
    /// trusting the wrong party with the one question only it can answer.
    /// </remarks>
    Task<IReadOnlyList<AiExecutionTargetResponse>> ListAiExecutionTargetsAsync(
        CancellationToken cancellationToken = default);
}

public sealed partial class AgencyOsApiClient
{
    public Task<ContextLeaseResponse> IssueAiContextLeaseAsync(
        Guid runId,
        IssueContextLeaseRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<IssueContextLeaseRequest, ContextLeaseResponse>(
            HttpMethod.Post,
            $"{AiRoot}/runs/{runId}/local-lease",
            request,
            idempotencyKey: null,
            cancellationToken);

    public Task<SubmitLocalResultResponse> SubmitAiLocalResultAsync(
        Guid runId,
        SubmitLocalResultRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<SubmitLocalResultRequest, SubmitLocalResultResponse>(
            HttpMethod.Post,
            $"{AiRoot}/runs/{runId}/local-result",
            request,
            idempotencyKey,
            cancellationToken);

    public Task<IReadOnlyList<AiExecutionTargetResponse>> ListAiExecutionTargetsAsync(
        CancellationToken cancellationToken = default) =>
        GetListAsync<AiExecutionTargetResponse>(
            $"{AiRoot}/execution-targets", cancellationToken);
}
