using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Idempotency;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Application.Idempotency;

/// <summary>
/// Durable storage for idempotency keys.
/// </summary>
/// <remarks>
/// <see cref="TryReserveAsync"/> must be atomic across concurrent callers: the
/// implementation relies on a unique key so that exactly one of two simultaneous
/// submissions wins. In-memory checking would not survive the case this whole
/// mechanism exists for - two retries arriving at once after a reconnect.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Reserves a key, or returns the record that already holds it.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when this caller won the reservation and should
    /// execute; otherwise the existing record.
    /// </returns>
    Task<IdempotencyRecord?> TryReserveAsync(
        OrganizationId organizationId,
        string key,
        string requestFingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Stores the answer for a key that this caller reserved.</summary>
    Task CompleteAsync(
        OrganizationId organizationId,
        string key,
        int statusCode,
        string? responseBody,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a reservation whose execution failed before producing an answer.
    /// </summary>
    /// <remarks>
    /// Without this, a request that failed with a 500 would leave its key
    /// reserved forever and the client's retry - the correct thing for it to do -
    /// would be refused as in-progress until the retention window expired.
    /// </remarks>
    Task AbandonAsync(
        OrganizationId organizationId,
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>The outcome of asking to run a keyed command.</summary>
/// <param name="ShouldExecute">Whether this caller won the reservation and must do the work.</param>
/// <param name="ReplayStatusCode">Status of the stored answer, when replaying.</param>
/// <param name="ReplayBody">Body of the stored answer, when replaying.</param>
public sealed record IdempotencyDecision(bool ShouldExecute, int? ReplayStatusCode, string? ReplayBody)
{
    public static IdempotencyDecision Execute { get; } = new(ShouldExecute: true, null, null);

    public static IdempotencyDecision Replay(int statusCode, string? body) => new(false, statusCode, body);
}

/// <summary>
/// Enforces at-most-once execution for commands that carry an idempotency key.
/// </summary>
/// <remarks>
/// <para>
/// The protocol is reserve, execute, store. Reserving before any work means a
/// second submission arriving mid-flight is told to wait rather than allowed to
/// act, and storing the answer means a submission arriving after the first
/// finished gets that answer back instead of a second effect.
/// </para>
/// <para>
/// This is the server-side half of the offline write queue and is what makes it
/// safe. The client contributing a stable key is necessary but not sufficient:
/// a key that only the client checks is a client that can be wrong twice. See
/// <c>docs/adr/ADR-0014-concurrency-and-idempotency.md</c> and the model in
/// <c>specs/OfflineWriteQueue.tla</c>, whose <c>AtMostOneEffect</c> invariant is
/// exactly this property.
/// </para>
/// <para>
/// The lost-acknowledgement case is the one that matters: the server commits, the
/// response never arrives, the client retries. Replay turns that from a duplicate
/// person record into the same answer twice.
/// </para>
/// </remarks>
public sealed class IdempotencyCoordinator
{
    /// <summary>Longest key accepted. A GUID or an opaque token, not a payload.</summary>
    public const int MaximumKeyLength = 128;

    private readonly IIdempotencyStore _store;
    private readonly IClock _clock;

    public IdempotencyCoordinator(IIdempotencyStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    /// <summary>
    /// Computes the fingerprint compared on replay.
    /// </summary>
    /// <remarks>
    /// A hash rather than the payload: it is only ever compared, and command
    /// bodies carry contact data that gains nothing from a second copy sitting in
    /// a bookkeeping table.
    /// </remarks>
    /// <param name="method">HTTP method of the request.</param>
    /// <param name="path">Request path, which carries the target identity.</param>
    /// <param name="payload">
    /// A canonical description of what the caller asked for. The API derives it
    /// from the bound command arguments rather than the raw body: whitespace and
    /// property order are not differences in intent, and by the time the request
    /// reaches an endpoint filter the body has already been consumed.
    /// </param>
    public static string Fingerprint(string method, string path, string? payload)
    {
        byte[] bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{method}\n{path}\n{payload ?? string.Empty}")));

        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Decides whether this submission should execute or replay a stored answer.
    /// </summary>
    /// <exception cref="IdempotencyConflictException">
    /// The key was used for a materially different request.
    /// </exception>
    /// <exception cref="IdempotencyInProgressException">
    /// The first attempt is still running.
    /// </exception>
    public async Task<IdempotencyDecision> BeginAsync(
        OrganizationId organizationId,
        string key,
        string requestFingerprint,
        CancellationToken cancellationToken = default)
    {
        IdempotencyRecord? existing = await _store
            .TryReserveAsync(organizationId, key, requestFingerprint, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return IdempotencyDecision.Execute;
        }

        // Same key, different request. Serving either answer would be wrong.
        existing.RequireSameRequest(requestFingerprint);

        if (!existing.IsComplete)
        {
            // The first attempt is still in flight. Telling the client to retry is
            // honest; executing alongside it would be the duplicate this exists to
            // prevent.
            throw new IdempotencyInProgressException(key);
        }

        return IdempotencyDecision.Replay(existing.ResponseStatusCode!.Value, existing.ResponseBody);
    }

    /// <summary>Stores the answer produced by a reservation this caller won.</summary>
    public Task CompleteAsync(
        OrganizationId organizationId,
        string key,
        int statusCode,
        string? responseBody,
        CancellationToken cancellationToken = default) =>
        _store.CompleteAsync(organizationId, key, statusCode, responseBody, _clock.UtcNow, cancellationToken);

    /// <summary>Releases a reservation whose execution failed without an answer.</summary>
    public Task AbandonAsync(
        OrganizationId organizationId,
        string key,
        CancellationToken cancellationToken = default) =>
        _store.AbandonAsync(organizationId, key, cancellationToken);
}
