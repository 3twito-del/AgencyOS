using System.Globalization;
using System.Text.Json;
using AgencyOS.Client.Cache;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Sync;

namespace AgencyOS.Client.Sync;

/// <summary>What the last synchronization attempt did.</summary>
/// <param name="PagesApplied">Change-feed pages written to the cache.</param>
/// <param name="ChangesApplied">Individual changes applied.</param>
/// <param name="Cursor">Position the cache now holds.</param>
/// <param name="Submitted">Queued commands the server accepted.</param>
/// <param name="Conflicted">Queued commands refused because the record moved on.</param>
/// <param name="Failed">Queued commands that failed and will be retried.</param>
/// <param name="Refused">Queued commands refused for good, needing a person.</param>
public sealed record SyncOutcome(
    int PagesApplied,
    int ChangesApplied,
    long Cursor,
    int Submitted,
    int Conflicted,
    int Failed,
    int Refused)
{
    public static SyncOutcome None { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Gets a value indicating whether anything needs a person to decide.</summary>
    public bool NeedsAttention => Conflicted > 0 || Refused > 0;
}

/// <summary>
/// Drains the write queue, then pulls the change feed into the cache.
/// </summary>
/// <remarks>
/// <para>
/// Push before pull, deliberately. Sending first means a command the user made
/// offline is applied before the client learns what else changed, so the version
/// it carries is the version the user actually saw. Pulling first would refresh
/// the record underneath the queued command and turn every offline edit into a
/// conflict against a change the user never looked at.
/// </para>
/// <para>
/// The queue is drained oldest-first and stops at the first command that does not
/// settle. Two edits to one record must reach the server in the order they were
/// made, and skipping past a stuck command would apply the second without the
/// first.
/// </para>
/// <para>
/// The protocol this implements is modelled in <c>specs/OfflineWriteQueue.tla</c>
/// and checked with TLC. The invariants that matter here are
/// <c>AtMostOneEffect</c> - a command never takes effect twice, which the
/// idempotency key buys - and <c>StaleNeverOverwrites</c>, which the version token
/// buys. The model also produced the rule below about exhausted attempts.
/// </para>
/// </remarks>
public sealed class SyncEngine : ISyncEngine
{
    /// <summary>
    /// How many times a command is retried before it stops being tried
    /// automatically.
    /// </summary>
    /// <remarks>
    /// It becomes <see cref="QueuedState.LocalPending"/> again rather than
    /// permanently failed, and the user is shown that it is waiting. The model
    /// forced this: the attempt whose acknowledgement was lost may be the one that
    /// committed, so a client that gives up and calls it failed would be
    /// reporting the opposite of what happened. Only the server's own refusal is
    /// permanent.
    /// </remarks>
    public const int MaximumAutomaticAttempts = 5;

    /// <summary>Pages pulled in one synchronization pass.</summary>
    private const int MaximumPagesPerRun = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IAgencyOsApi _api;
    private readonly IWriteQueue _queue;
    private readonly LocalCache _cache;
    private readonly TimeProvider _time;

    public SyncEngine(IAgencyOsApi api, LocalCache cache, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(cache);

        _api = api;
        _queue = cache;
        _cache = cache;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Sends what is queued, then applies what has changed.</summary>
    public async Task<SyncOutcome> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        (int submitted, int conflicted, int failed, int refused) =
            await DrainQueueAsync(cancellationToken).ConfigureAwait(false);

        (int pages, int changes, long cursor) = await PullAsync(cancellationToken).ConfigureAwait(false);

        return new SyncOutcome(pages, changes, cursor, submitted, conflicted, failed, refused);
    }

    /// <summary>
    /// Submits queued commands until one does not settle.
    /// </summary>
    /// <remarks>
    /// A command is marked <see cref="QueuedState.Sending"/> before submission, so
    /// a crash mid-flight leaves a record that something was attempted. On restart
    /// it returns to the outstanding set and is retried under the same idempotency
    /// key, which is what makes retrying safe rather than duplicating.
    /// </remarks>
    private async Task<(int Submitted, int Conflicted, int Failed, int Refused)> DrainQueueAsync(
        CancellationToken cancellationToken)
    {
        int submitted = 0;
        int conflicted = 0;
        int failed = 0;
        int refused = 0;

        foreach (QueuedCommand command in _queue.ReadOutstanding())
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset now = _time.GetUtcNow();

            _queue.RecordAttempt(command.Id, QueuedState.Sending, now, countAttempt: false);

            try
            {
                await SubmitAsync(command, cancellationToken).ConfigureAwait(false);

                _queue.RecordAttempt(command.Id, QueuedState.Synced, _time.GetUtcNow());
                submitted++;
            }
            catch (AgencyOsApiException exception) when (exception.IsVersionConflict)
            {
                // The record moved on. Nothing is discarded and nothing is
                // applied: the user is shown what they meant and what it says now.
                _queue.RecordAttempt(
                    command.Id,
                    QueuedState.Conflict,
                    _time.GetUtcNow(),
                    exception.Message,
                    exception.ActualVersion);

                conflicted++;
                break;
            }
            catch (AgencyOsApiException exception) when (!exception.IsRetryable)
            {
                // A refusal - unauthorized, no longer permitted, invalid - will
                // refuse again. Offline possession of a record was never
                // permission to change it later; the server decides at execution
                // time, and it has.
                _queue.RecordAttempt(
                    command.Id,
                    QueuedState.FailedPermanent,
                    _time.GetUtcNow(),
                    exception.Message);

                refused++;
                break;
            }
            catch (Exception exception) when (exception is AgencyOsApiException or HttpRequestException or TaskCanceledException)
            {
                _queue.RecordAttempt(
                    command.Id,
                    NextRetryState(command),
                    _time.GetUtcNow(),
                    exception.Message);

                failed++;
                break;
            }
        }

        return (submitted, conflicted, failed, refused);
    }

    /// <summary>
    /// Decides what an unsuccessful attempt leaves behind.
    /// </summary>
    /// <remarks>
    /// Never <see cref="QueuedState.FailedPermanent"/>. That state means the
    /// server refused, and a transport failure is not a refusal: the request may
    /// have arrived and committed, with only the answer lost. Calling it
    /// permanently failed would tell the user their change did not happen when it
    /// may well have. The model checks this as
    /// <c>CommittedNeverPermanentlyFailed</c>.
    /// </remarks>
    private static QueuedState NextRetryState(QueuedCommand command) =>
        command.Attempts + 1 >= MaximumAutomaticAttempts
            ? QueuedState.LocalPending
            : QueuedState.FailedRetryable;

    private async Task SubmitAsync(QueuedCommand command, CancellationToken cancellationToken)
    {
        switch (command.Operation)
        {
            case QueuedOperation.CreatePerson:
                await _api
                    .CreatePersonAsync(Payload<CreatePersonRequest>(command), command.IdempotencyKey, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case QueuedOperation.UpdatePerson:
                await _api
                    .UpdatePersonAsync(
                        command.TargetId!.Value,
                        Payload<UpdatePersonRequest>(command),
                        command.IdempotencyKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case QueuedOperation.CreateCompany:
                await _api
                    .CreateCompanyAsync(Payload<CreateCompanyRequest>(command), command.IdempotencyKey, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case QueuedOperation.UpdateCompany:
                await _api
                    .UpdateCompanyAsync(
                        command.TargetId!.Value,
                        Payload<UpdateCompanyRequest>(command),
                        command.IdempotencyKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case QueuedOperation.CreateTask:
                await _api
                    .CreateTaskAsync(Payload<CreateTaskRequest>(command), command.IdempotencyKey, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case QueuedOperation.CompleteTask:
                await _api
                    .CompleteTaskAsync(
                        command.TargetId!.Value,
                        new TaskTransitionRequest(command.ExpectedVersion!.Value),
                        command.IdempotencyKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case QueuedOperation.ReopenTask:
                await _api
                    .ReopenTaskAsync(
                        command.TargetId!.Value,
                        new TaskTransitionRequest(command.ExpectedVersion!.Value),
                        command.IdempotencyKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case QueuedOperation.RecordInteraction:
                await _api
                    .RecordInteractionAsync(
                        Payload<RecordInteractionRequest>(command),
                        command.IdempotencyKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
                throw new InvalidOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Queued operation '{command.Operation}' has no submission path. A command was queued that this build cannot send."));
        }
    }

    /// <summary>
    /// Pulls change-feed pages into the cache until it is caught up.
    /// </summary>
    /// <remarks>
    /// Each page is applied and its cursor committed in one transaction before the
    /// next is requested, so an interruption resumes from the last page that
    /// actually landed. A cursor advanced ahead of the records it describes would
    /// mean those changes were never offered again.
    /// </remarks>
    private async Task<(int Pages, int Changes, long Cursor)> PullAsync(CancellationToken cancellationToken)
    {
        long cursor = _cache.ReadCursor();
        int pages = 0;
        int applied = 0;

        for (int page = 0; page < MaximumPagesPerRun; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SyncChangesResponse response = await _api
                .ReadSyncChangesAsync(cursor, take: null, cancellationToken)
                .ConfigureAwait(false);

            if (response.Changes.Count == 0)
            {
                break;
            }

            List<(string EntityType, Guid EntityId)> removed =
            [
                .. response.Changes
                    .Where(x => string.Equals(x.Kind, "Removed", StringComparison.Ordinal))
                    .Select(x => (x.EntityType, x.EntityId)),
            ];

            _cache.ApplyPage(
                response.Cursor,
                response.People,
                response.Companies,
                response.Tasks,
                removed,
                _time.GetUtcNow(),
                response.Talent);

            cursor = response.Cursor;
            applied += response.Changes.Count;
            pages++;

            if (!response.HasMore)
            {
                break;
            }
        }

        return (pages, applied, cursor);
    }

    private static T Payload<T>(QueuedCommand command) =>
        JsonSerializer.Deserialize<T>(command.Payload, Json)
        ?? throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Queued command {command.Id} has an unreadable payload."));
}
