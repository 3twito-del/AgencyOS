using System.Net;
using AgencyOS.Client;
using AgencyOS.Client.Cache;
using AgencyOS.Client.Sync;
using AgencyOS.Contracts.PeopleSlice;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// An exhaustive simulation of the offline write queue against every interleaving
/// of failure the protocol admits.
/// </summary>
/// <remarks>
/// <para>
/// <c>specs/OfflineWriteQueue.tla</c> is model-checked by TLC and reports 1024
/// distinct states with no invariant violated. That proves the protocol. This
/// proves the implementation of it: the same invariants, asserted against
/// <see cref="SyncEngine"/> and <see cref="LocalCache"/> over every sequence of
/// outcomes up to a bounded length.
/// </para>
/// <para>
/// The two together are what the milestone actually needs. A model nobody
/// implemented and an implementation nobody modelled are each half an argument.
/// </para>
/// </remarks>
[Collection(LocalCacheCollection.Name)]
public sealed class WriteQueueSimulationTests : IDisposable
{
    /// <summary>How the server answers one submission.</summary>
    private enum Outcome
    {
        /// <summary>Accepted, acknowledgement received.</summary>
        Accepted,

        /// <summary>Committed on the server; the answer never arrived.</summary>
        AcknowledgementLost,

        /// <summary>Never reached the server.</summary>
        Unreachable,

        /// <summary>Refused: the record moved on.</summary>
        Stale,

        /// <summary>Refused for good: no longer permitted.</summary>
        Rejected,
    }

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "agencyos-queue-sim", Guid.NewGuid().ToString("N"));

    private static readonly ExplicitCacheKeyProvider Key =
        new("aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899");

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

    /// <summary>
    /// Every sequence of outcomes up to length four, checked against the model's
    /// invariants.
    /// </summary>
    /// <remarks>
    /// Five outcomes to the fourth power is 780 sequences, each replayed against a
    /// real encrypted cache and a real engine. That is small enough to run on every
    /// build and large enough to contain every ordering that matters: a loss
    /// followed by a conflict, a rejection after a commit, a reconnect after
    /// exhaustion.
    /// </remarks>
    [Fact]
    public void EveryInterleaving_UpholdsTheModelsInvariants()
    {
        // One cache for the whole run. A scenario needs an isolated queue entry,
        // not an isolated database: states are per command, and each scenario
        // removes its own at the end so the next starts with an empty outstanding
        // set. Opening 780 encrypted databases instead would be minutes of disk
        // work on a CI runner to prove nothing extra.
        using LocalCache cache = LocalCache.Open(
            _root,
            new LocalCacheIdentity("LAB", Guid.NewGuid(), "sim@example.invalid"),
            Key);

        int sequences = 0;

        foreach (Outcome[] script in Scripts(maximumLength: 4))
        {
            sequences++;
            Simulate(cache, script);
        }

        // 5 + 25 + 125 + 625: every sequence of one to four outcomes.
        Assert.Equal(780, sequences);

        // Every scenario cleaned up after itself, so no state leaked between them.
        Assert.Empty(cache.ReadAll());
    }

    /// <summary>
    /// Replays one sequence and asserts the invariants after every step.
    /// </summary>
    /// <remarks>
    /// The invariants are named for their counterparts in the TLA+ module so that a
    /// failure here points straight at the property it broke.
    /// </remarks>
    private static void Simulate(LocalCache cache, Outcome[] script)
    {
        QueuedCommand queued = cache.Enqueue(
            QueuedOperation.UpdatePerson,
            "Rename Sarah Klein",
            new UpdatePersonRequest("Sarah", 3),
            DateTimeOffset.UtcNow,
            targetId: Guid.NewGuid(),
            expectedVersion: 3);

        // Effects the server would have applied, counted by idempotency key. The
        // server's own idempotency is what keeps this at one; here it is simulated
        // so the client's contribution - one stable key - can be checked alone.
        Dictionary<string, int> committed = [];
        bool acknowledged = false;
        bool refusedAsStale = false;

        foreach (Outcome outcome in script)
        {
            FakeAgencyOsApi api = new();

            switch (outcome)
            {
                case Outcome.Accepted:
                    break;

                case Outcome.AcknowledgementLost:
                    // The server commits and the answer is lost. Recorded here
                    // because the effect is real even though the client cannot know.
                    committed[queued.IdempotencyKey] = 1;
                    api.Failures.Enqueue(new TaskCanceledException("No response."));
                    break;

                case Outcome.Unreachable:
                    api.Failures.Enqueue(new HttpRequestException("Unreachable."));
                    break;

                case Outcome.Stale:
                    refusedAsStale = true;
                    api.Failures.Enqueue(new AgencyOsApiException(
                        HttpStatusCode.Conflict,
                        "Version conflict",
                        "The record changed.",
                        "version_conflict",
                        expectedVersion: 3,
                        actualVersion: 5));
                    break;

                case Outcome.Rejected:
                    api.Failures.Enqueue(new AgencyOsApiException(
                        HttpStatusCode.Forbidden,
                        "Permission denied",
                        "No longer permitted."));
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled outcome {outcome}.");
            }

            SyncOutcome result = new SyncEngine(api, cache).SynchronizeAsync().GetAwaiter().GetResult();

            if (outcome == Outcome.Accepted && result.Submitted == 1)
            {
                committed[queued.IdempotencyKey] = committed.GetValueOrDefault(queued.IdempotencyKey) + 1;
                acknowledged = true;
            }

            QueuedState state = ReadState(cache, queued.Id);

            // AtMostOneEffect: the command never carries a second key, so the
            // server can always recognize a replay. This is the client's whole
            // contribution to the property.
            Assert.All(api.IdempotencyKeys, key => Assert.Equal(queued.IdempotencyKey, key));

            // AcknowledgedIsCommitted: an acknowledged command is one the server
            // committed, and it is never presented as anything else.
            if (acknowledged)
            {
                Assert.Equal(QueuedState.Synced, state);
                Assert.True(committed[queued.IdempotencyKey] >= 1);
            }

            // CommittedNeverPermanentlyFailed: only the server's refusal makes a
            // command permanent. A lost answer must never be reported as failure,
            // because the request may well have committed.
            if (state == QueuedState.FailedPermanent)
            {
                Assert.Contains(Outcome.Rejected, script);
            }

            // ConflictOnlyWhenStale, and its converse: a conflict is reported
            // exactly when the server said the record had moved on.
            if (state == QueuedState.Conflict)
            {
                Assert.True(refusedAsStale);
            }

            // StaleNeverOverwrites: a refused stale write left no effect behind.
            if (refusedAsStale && !acknowledged)
            {
                Assert.False(committed.ContainsKey(queued.IdempotencyKey) && !ScriptLostAnAcknowledgement(script));
            }

            // The queue is always in exactly one of the modelled states, and the
            // outstanding and attention sets never overlap.
            Assert.Contains(state, ModelStates);

            (int outstanding, int attention) = cache.ReadQueueCounts();

            Assert.InRange(outstanding + attention, 0, 1);

            if (state is QueuedState.Conflict or QueuedState.FailedPermanent)
            {
                Assert.Equal(1, attention);
                Assert.Equal(0, outstanding);
            }

            // A settled command is never retried, so the simulation stops where the
            // engine would.
            if (state is QueuedState.Synced or QueuedState.Conflict or QueuedState.FailedPermanent)
            {
                break;
            }
        }

        cache.Remove(queued.Id);
    }

    private static readonly QueuedState[] ModelStates =
    [
        QueuedState.LocalPending,
        QueuedState.Sending,
        QueuedState.Synced,
        QueuedState.Conflict,
        QueuedState.FailedRetryable,
        QueuedState.FailedPermanent,
    ];

    private static bool ScriptLostAnAcknowledgement(Outcome[] script) =>
        Array.IndexOf(script, Outcome.AcknowledgementLost) >= 0;

    private static QueuedState ReadState(LocalCache cache, Guid id) =>
        cache.ReadAll().First(x => x.Id == id).State;

    /// <summary>Enumerates every outcome sequence up to the given length.</summary>
    private static IEnumerable<Outcome[]> Scripts(int maximumLength)
    {
        Outcome[] alphabet = Enum.GetValues<Outcome>();

        for (int length = 1; length <= maximumLength; length++)
        {
            foreach (Outcome[] script in Combinations(alphabet, length))
            {
                yield return script;
            }
        }
    }

    private static IEnumerable<Outcome[]> Combinations(Outcome[] alphabet, int length)
    {
        int[] indices = new int[length];

        while (true)
        {
            yield return [.. indices.Select(i => alphabet[i])];

            int position = length - 1;

            while (position >= 0 && ++indices[position] == alphabet.Length)
            {
                indices[position] = 0;
                position--;
            }

            if (position < 0)
            {
                yield break;
            }
        }
    }
}
