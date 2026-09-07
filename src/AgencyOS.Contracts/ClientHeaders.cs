namespace AgencyOS.Contracts;

/// <summary>
/// Headers a client presents on every request so the server can govern it.
/// </summary>
/// <remarks>
/// Defined in the contracts assembly because both sides depend on them: the
/// Windows client sends them and the API enforces on them. A client that omits
/// them cannot be evaluated against release policy, and is therefore refused for
/// protected mutations.
/// </remarks>
public static class ClientHeaders
{
    public const string Platform = "X-AgencyOS-Platform";
    public const string Channel = "X-AgencyOS-Channel";
    public const string ClientVersion = "X-AgencyOS-Client-Version";
    public const string ApiContractVersion = "X-AgencyOS-Api-Contract";
    public const string BuildId = "X-AgencyOS-Build-Id";
    public const string CorrelationId = "X-AgencyOS-Correlation-Id";

    /// <summary>
    /// Carries the client's idempotency key for a mutating request.
    /// </summary>
    /// <remarks>
    /// Generated before the first submission and kept across every retry, restart
    /// and reconnect, so a replay is recognizable as one. See
    /// <c>docs/adr/ADR-0014-concurrency-and-idempotency.md</c>.
    /// </remarks>
    public const string IdempotencyKey = "X-AgencyOS-Idempotency-Key";
}
