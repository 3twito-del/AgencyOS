using AgencyOS.Domain.Common;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Idempotency;

/// <summary>Raised when a key is replayed with a materially different payload.</summary>
/// <remarks>
/// Two different commands sharing one key is a client defect, not a retry. Serving
/// either answer would be wrong, so the request is refused and the client is told
/// why.
/// </remarks>
public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string key)
        : base($"Idempotency key '{key}' was already used for a different request.")
    {
        Key = key;
    }

    public string Key { get; }
}

/// <summary>Raised when a key is replayed while its first attempt is still running.</summary>
public sealed class IdempotencyInProgressException : Exception
{
    public IdempotencyInProgressException(string key)
        : base($"Idempotency key '{key}' is currently being processed. Retry shortly.")
    {
        Key = key;
    }

    public string Key { get; }
}

/// <summary>
/// The record of a command that carried an idempotency key.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a retry safe. A queued offline command generates its key
/// before its first submission and keeps it across every retry, restart and
/// crash; the server records the key before doing any work, so a second
/// submission finds the first and replays its answer instead of acting again.
/// </para>
/// <para>
/// The fingerprint is a hash of the request, not the request itself: it is only
/// ever compared, never read back, and command payloads contain contact data that
/// does not need a second copy.
/// </para>
/// <para>
/// Scope is the tenant. Two tenants may use the same key without colliding, and a
/// key can never replay one tenant's answer to another.
/// </para>
/// </remarks>
public sealed class IdempotencyRecord
{
    /// <summary>How long a key is honoured before it may be reused.</summary>
    /// <remarks>
    /// Long enough that any realistic offline period is covered, short enough that
    /// the table does not grow without bound. Nothing prunes it in M3; that is a
    /// documented limitation rather than an oversight.
    /// </remarks>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private IdempotencyRecord()
    {
    }

    public OrganizationId OrganizationId { get; private set; }

    public string Key { get; private set; } = string.Empty;

    /// <summary>Hash of the command type and payload, used only for comparison.</summary>
    public string RequestFingerprint { get; private set; } = string.Empty;

    /// <summary>HTTP status of the stored answer, or null while still in flight.</summary>
    public int? ResponseStatusCode { get; private set; }

    /// <summary>The stored answer, replayed verbatim on a matching retry.</summary>
    public string? ResponseBody { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets a value indicating whether the first attempt finished.</summary>
    public bool IsComplete => CompletedAt.HasValue;

    /// <summary>
    /// Reserves a key before any work is done.
    /// </summary>
    /// <remarks>
    /// Reserving first is what stops two concurrent submissions both executing:
    /// the second one's insert loses to the unique key and it finds this record
    /// instead.
    /// </remarks>
    public static IdempotencyRecord Reserve(
        OrganizationId organizationId,
        string key,
        string requestFingerprint,
        DateTimeOffset now)
    {
        return new IdempotencyRecord
        {
            OrganizationId = organizationId,
            Key = Ensure.NotBlankMax(key, nameof(key), 128),
            RequestFingerprint = Ensure.NotBlankMax(requestFingerprint, nameof(requestFingerprint), 128),
            CreatedAt = now,
            ExpiresAt = now.Add(Retention),
        };
    }

    /// <summary>Stores the answer, so a later replay returns it without acting again.</summary>
    public void Complete(int statusCode, string? responseBody, DateTimeOffset now)
    {
        ResponseStatusCode = statusCode;
        ResponseBody = responseBody;
        CompletedAt = now;
    }

    /// <summary>
    /// Confirms a replay carries the same request as the original.
    /// </summary>
    /// <remarks>
    /// A matching fingerprint is a retry and is replayed. A differing one is two
    /// different commands wearing one key, which is refused.
    /// </remarks>
    public void RequireSameRequest(string requestFingerprint)
    {
        if (!string.Equals(RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException(Key);
        }
    }
}
