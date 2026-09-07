using System.Net;
using AgencyOS.Client;
using AgencyOS.Client.Cache;
using AgencyOS.Client.Sync;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// The synchronization protocol: push then pull, and what happens when neither
/// goes to plan.
/// </summary>
/// <remarks>
/// These assert the same properties TLC checks in
/// <c>specs/OfflineWriteQueue.tla</c>, against the implementation rather than the
/// model. A model that agrees with itself and disagrees with the code proves
/// nothing.
/// </remarks>
public sealed class SyncEngineTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "agencyos-sync-tests", Guid.NewGuid().ToString("N"));

    private static readonly LocalCacheIdentity Identity =
        new("LAB", Guid.Parse("22222222-2222-2222-2222-222222222222"), "agent@example.invalid");

    private static readonly ExplicitCacheKeyProvider Key =
        new("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Not worth failing a test over.
            }
        }
    }

    [Fact]
    public async Task Pull_AppliesPagesAndAdvancesTheCursor()
    {
        using LocalCache cache = Open();
        FakeAgencyOsApi api = new();

        Guid personId = Guid.NewGuid();

        api.SyncPages.Enqueue(Page(
            cursor: 2,
            hasMore: true,
            [Change(1, "Person", personId), Change(2, "Person", personId)],
            people: [Person(personId, "Sarah Klein")]));

        api.SyncPages.Enqueue(Page(cursor: 3, hasMore: false, [Change(3, "Company", Guid.NewGuid())]));

        SyncEngine engine = new(api, cache);
        SyncOutcome outcome = await engine.SynchronizeAsync();

        Assert.Equal(2, outcome.PagesApplied);
        Assert.Equal(3, outcome.ChangesApplied);
        Assert.Equal(3, outcome.Cursor);
        Assert.Equal(3, cache.ReadCursor());
        Assert.Single(cache.ReadPeople());
    }

    /// <summary>An interrupted pull resumes from the last page that actually landed.</summary>
    [Fact]
    public async Task Pull_ResumesFromTheLastPageThatLanded()
    {
        using LocalCache cache = Open();

        FakeAgencyOsApi first = new();
        first.SyncPages.Enqueue(Page(cursor: 4, hasMore: true, [Change(4, "Person", Guid.NewGuid())]));

        await new SyncEngine(first, cache).SynchronizeAsync();

        Assert.Equal(4, cache.ReadCursor());

        FakeAgencyOsApi second = new();
        second.SyncPages.Enqueue(Page(cursor: 6, hasMore: false, [Change(6, "Person", Guid.NewGuid())]));

        await new SyncEngine(second, cache).SynchronizeAsync();

        Assert.Equal(6, cache.ReadCursor());
    }

    [Fact]
    public async Task Push_SubmitsQueuedCommandsAndMarksThemSynced()
    {
        using LocalCache cache = Open();
        FakeAgencyOsApi api = new();

        cache.Enqueue(
            QueuedOperation.CreatePerson,
            "Add Sarah Klein",
            new CreatePersonRequest("Sarah", "Klein"),
            DateTimeOffset.UtcNow);

        SyncOutcome outcome = await new SyncEngine(api, cache).SynchronizeAsync();

        Assert.Equal(1, outcome.Submitted);
        Assert.Empty(cache.ReadOutstanding());
        Assert.Empty(cache.ReadNeedingAttention());
    }

    /// <summary>Every submission carries the key generated at enqueue, unchanged.</summary>
    [Fact]
    public async Task Push_CarriesTheKeyGeneratedAtEnqueue()
    {
        using LocalCache cache = Open();
        FakeAgencyOsApi api = new();

        QueuedCommand queued = cache.Enqueue(
            QueuedOperation.CreatePerson,
            "Add Sarah Klein",
            new CreatePersonRequest("Sarah", "Klein"),
            DateTimeOffset.UtcNow);

        await new SyncEngine(api, cache).SynchronizeAsync();

        Assert.Equal(queued.IdempotencyKey, Assert.Single(api.IdempotencyKeys));
    }

    /// <summary>
    /// A stale write is refused and becomes a decision, not a silent overwrite.
    /// </summary>
    /// <remarks>
    /// The model's <c>StaleNeverOverwrites</c> invariant, asserted against the code:
    /// the conflicted command produced no effect and the user is told both versions.
    /// </remarks>
    [Fact]
    public async Task Push_TurnsAStaleWriteIntoAConflict()
    {
        using LocalCache cache = Open();
        FakeAgencyOsApi api = new();

        api.Failures.Enqueue(new AgencyOsApiException(
            HttpStatusCode.Conflict,
            "Version conflict",
            "Person has changed since you last saw it.",
            "version_conflict",
            expectedVersion: 3,
            actualVersion: 5));

        Guid personId = Guid.NewGuid();

        cache.Enqueue(
            QueuedOperation.UpdatePerson,
            "Rename Sarah Klein",
            new UpdatePersonRequest("Sarah", 3),
            DateTimeOffset.UtcNow,
            targetId: personId,
            expectedVersion: 3);

        SyncOutcome outcome = await new SyncEngine(api, cache).SynchronizeAsync();

        Assert.Equal(1, outcome.Conflicted);
        Assert.True(outcome.NeedsAttention);

        QueuedCommand conflicted = Assert.Single(cache.ReadNeedingAttention());

        Assert.Equal(QueuedState.Conflict, conflicted.State);
        Assert.Equal(3, conflicted.ExpectedVersion);
        Assert.Equal(5, conflicted.ServerVersion);

        // Nothing was applied, and nothing was thrown away.
        Assert.Empty(api.Effects);
    }

    /// <summary>
    /// A refusal is permanent, because the server will refuse it again.
    /// </summary>
    /// <remarks>
    /// Offline possession of a record is not permission to change it later. The
    /// server re-authorizes at execution time, and when it says no, retrying is
    /// not a strategy.
    /// </remarks>
    [Fact]
    public async Task Push_TreatsARefusalAsPermanent()
    {
        using LocalCache cache = Open();
        FakeAgencyOsApi api = new();

        api.Failures.Enqueue(new AgencyOsApiException(
            HttpStatusCode.Forbidden,
            "Permission denied",
            "Permission 'people.write' is required."));

        cache.Enqueue(
            QueuedOperation.CreatePerson,
            "Add Sarah Klein",
            new CreatePersonRequest("Sarah", "Klein"),
            DateTimeOffset.UtcNow);

        SyncOutcome outcome = await new SyncEngine(api, cache).SynchronizeAsync();

        Assert.Equal(1, outcome.Refused);
        Assert.Equal(QueuedState.FailedPermanent, Assert.Single(cache.ReadNeedingAttention()).State);
    }

    /// <summary>A transport failure is retried, not reported as a refusal.</summary>
    [Fact]
    public async Task Push_KeepsRetryingAfterATransportFailure()
    {
        using LocalCache cache = Open();
        FakeAgencyOsApi api = new();

        api.Failures.Enqueue(new HttpRequestException("The server could not be reached."));

        cache.Enqueue(
            QueuedOperation.CreatePerson,
            "Add Sarah Klein",
            new CreatePersonRequest("Sarah", "Klein"),
            DateTimeOffset.UtcNow);

        SyncOutcome outcome = await new SyncEngine(api, cache).SynchronizeAsync();

        Assert.Equal(1, outcome.Failed);

        QueuedCommand queued = Assert.Single(cache.ReadOutstanding());

        Assert.Equal(QueuedState.FailedRetryable, queued.State);
        Assert.Equal(1, queued.Attempts);
    }

    /// <summary>
    /// Exhausting the automatic attempts does not make a command permanently failed.
    /// </summary>
    /// <remarks>
    /// The lesson the TLA+ model forced. The attempt whose acknowledgement was lost
    /// may be the one that committed, so a client that gave up and called it failed
    /// would be reporting the opposite of what happened. Only the server's own
    /// refusal is permanent - <c>CommittedNeverPermanentlyFailed</c>.
    /// </remarks>
    [Fact]
    public async Task Push_AfterExhaustingAttempts_WaitsRatherThanDeclaringFailure()
    {
        using LocalCache cache = Open();

        cache.Enqueue(
            QueuedOperation.CreatePerson,
            "Add Sarah Klein",
            new CreatePersonRequest("Sarah", "Klein"),
            DateTimeOffset.UtcNow);

        for (int attempt = 0; attempt < SyncEngine.MaximumAutomaticAttempts; attempt++)
        {
            FakeAgencyOsApi api = new();
            api.Failures.Enqueue(new HttpRequestException("Still unreachable."));

            await new SyncEngine(api, cache).SynchronizeAsync();
        }

        QueuedCommand queued = Assert.Single(cache.ReadOutstanding());

        Assert.Equal(QueuedState.LocalPending, queued.State);
        Assert.Equal(SyncEngine.MaximumAutomaticAttempts, queued.Attempts);
        Assert.Empty(cache.ReadNeedingAttention());
    }

    /// <summary>
    /// The queue stops at the first command that does not settle.
    /// </summary>
    /// <remarks>
    /// Two edits to one record are not commutative. Skipping a stuck command would
    /// apply the second without the first, which is a worse outcome than waiting.
    /// </remarks>
    [Fact]
    public async Task Push_StopsAtTheFirstCommandThatDoesNotSettle()
    {
        using LocalCache cache = Open();
        FakeAgencyOsApi api = new();

        api.Failures.Enqueue(new HttpRequestException("Unreachable."));

        DateTimeOffset start = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

        cache.Enqueue(QueuedOperation.CreatePerson, "first", new CreatePersonRequest("A"), start);
        cache.Enqueue(QueuedOperation.CreatePerson, "second", new CreatePersonRequest("B"), start.AddMinutes(1));

        await new SyncEngine(api, cache).SynchronizeAsync();

        // Only the first was attempted, and nothing took effect.
        Assert.Single(api.IdempotencyKeys);
        Assert.Empty(api.Effects);
        Assert.Equal(2, cache.ReadOutstanding().Count);
    }

    /// <summary>
    /// A retry after a lost acknowledgement carries the original key.
    /// </summary>
    /// <remarks>
    /// The <c>AtMostOneEffect</c> invariant depends entirely on this. The server can
    /// only recognize a replay if the key is the one it already recorded.
    /// </remarks>
    [Fact]
    public async Task Push_RetriesUnderTheOriginalKeyAfterALostAcknowledgement()
    {
        using LocalCache cache = Open();

        QueuedCommand queued = cache.Enqueue(
            QueuedOperation.CreatePerson,
            "Add Sarah Klein",
            new CreatePersonRequest("Sarah", "Klein"),
            DateTimeOffset.UtcNow);

        // The server commits; the answer never arrives.
        FakeAgencyOsApi lost = new();
        lost.Failures.Enqueue(new TaskCanceledException("The response was never received."));

        await new SyncEngine(lost, cache).SynchronizeAsync();

        Assert.Equal(QueuedState.FailedRetryable, Assert.Single(cache.ReadOutstanding()).State);

        // The client restarts and retries. Same key, so the server replays.
        SqliteConnection.ClearAllPools();

        FakeAgencyOsApi retry = new();

        await new SyncEngine(retry, cache).SynchronizeAsync();

        Assert.Equal(queued.IdempotencyKey, Assert.Single(retry.IdempotencyKeys));
        Assert.Equal(1, retry.Effects[queued.IdempotencyKey]);
        Assert.Empty(cache.ReadOutstanding());
    }

    /// <summary>Push happens before pull, so a queued edit carries the version the user saw.</summary>
    [Fact]
    public async Task Synchronize_PushesBeforeItPulls()
    {
        using LocalCache cache = Open();

        Guid personId = Guid.NewGuid();

        cache.ApplyPage(1, [Person(personId, "Sarah Klein", version: 3)], [], [], [], DateTimeOffset.UtcNow);

        cache.Enqueue(
            QueuedOperation.UpdatePerson,
            "Rename",
            new UpdatePersonRequest("Sarah", 3),
            DateTimeOffset.UtcNow,
            targetId: personId,
            expectedVersion: 3);

        FakeAgencyOsApi api = new();

        // The pull would refresh the person to version 9. If it ran first, the
        // queued edit would conflict against a change the user never saw.
        api.SyncPages.Enqueue(Page(
            cursor: 2,
            hasMore: false,
            [Change(2, "Person", personId)],
            people: [Person(personId, "Sarah Klein", version: 9)]));

        SyncOutcome outcome = await new SyncEngine(api, cache).SynchronizeAsync();

        Assert.Equal(1, outcome.Submitted);
        Assert.Equal(0, outcome.Conflicted);
        Assert.Equal(9, Assert.Single(cache.ReadPeople()).Version);
    }

    private LocalCache Open() => LocalCache.Open(_root, Identity, Key);

    private static ChangeEntryResponse Change(long sequence, string type, Guid id) =>
        new(sequence, type, id, "Upsert", DateTimeOffset.UtcNow);

    private static SyncChangesResponse Page(
        long cursor,
        bool hasMore,
        IReadOnlyList<ChangeEntryResponse> changes,
        IReadOnlyList<PersonSummaryResponse>? people = null) =>
        new(cursor, hasMore, changes, people ?? [], [], []);

    private static PersonSummaryResponse Person(Guid id, string name, int version = 1) =>
        new(id, name, null, null, null, "Active", null, null, DateTimeOffset.UtcNow, version);
}
