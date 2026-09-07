using AgencyOS.Client.Cache;

namespace AgencyOS.Client.Sync;

/// <summary>
/// The durable queue of commands a client has made but the server has not seen.
/// </summary>
/// <remarks>
/// <para>
/// This and <see cref="ISyncEngine"/> are the seam
/// <c>docs/adr/ADR-0013-synchronization-architecture.md</c> names: the two things
/// a future Rust sync engine would replace. Everything above them - the view
/// models, the Windows UI - depends on these interfaces, and everything below is
/// an implementation detail. Replacing them touches neither the domain, the API
/// contract, nor the UI.
/// </para>
/// <para>
/// <c>docs/02_ARCHITECTURE.md</c> names Rust as the eventual owner of a sync
/// engine. M3 implements the first one in C#, because there is no profiling or
/// correctness evidence that C# is inadequate and adopting a second language for
/// a first implementation would add a native boundary to the least-understood
/// part of the system. The seam exists so that decision stays reversible.
/// </para>
/// </remarks>
public interface IWriteQueue
{
    /// <summary>
    /// Records a command locally before any attempt to send it.
    /// </summary>
    /// <remarks>
    /// The idempotency key is generated here, once, and never regenerated. A key
    /// minted at submission time would make a retry after a lost response look like
    /// a second command to the server, which is the duplicate the queue exists to
    /// prevent.
    /// </remarks>
    QueuedCommand Enqueue(
        QueuedOperation operation,
        string description,
        object payload,
        DateTimeOffset now,
        Guid? targetId = null,
        int? expectedVersion = null);

    /// <summary>Reads commands still wanting to be sent, oldest first.</summary>
    IReadOnlyList<QueuedCommand> ReadOutstanding(int limit = 100);

    /// <summary>Reads commands that need a person to decide something.</summary>
    IReadOnlyList<QueuedCommand> ReadNeedingAttention(int limit = 100);

    /// <summary>Reads every queued command, settled or not.</summary>
    IReadOnlyList<QueuedCommand> ReadAll(int limit = 500);

    /// <summary>Records the outcome of a submission attempt.</summary>
    void RecordAttempt(
        Guid id,
        QueuedState state,
        DateTimeOffset attemptedAt,
        string? error = null,
        int? serverVersion = null,
        bool countAttempt = true);

    /// <summary>Removes a settled command, or one the user has decided against.</summary>
    void Remove(Guid id);

    /// <summary>Gets how many commands are outstanding or waiting on a decision.</summary>
    (int Outstanding, int NeedsAttention) ReadQueueCounts();
}

/// <summary>
/// Sends what is queued and pulls what has changed.
/// </summary>
/// <remarks>
/// The other half of the replaceable seam. A different implementation must uphold
/// the same protocol, which is stated in <c>specs/OfflineWriteQueue.tla</c> rather
/// than only in code: at most one effect per command, an acknowledged command is
/// committed, a stale write never overwrites, and a lost acknowledgement is never
/// reported as permanent failure.
/// </remarks>
public interface ISyncEngine
{
    Task<SyncOutcome> SynchronizeAsync(CancellationToken cancellationToken = default);
}
