using System.Collections.ObjectModel;
using System.Globalization;
using AgencyOS.Client.Cache;
using AgencyOS.Client.Sync;

namespace AgencyOS.Client.ViewModels;

/// <summary>How the client currently stands with the server.</summary>
public enum ConnectionState
{
    /// <summary>Talking to the server; reads are canonical.</summary>
    Online = 1,

    /// <summary>Reading the cache; writes are queued.</summary>
    Offline = 2,

    /// <summary>Sending and pulling right now.</summary>
    Synchronizing = 3,
}

/// <summary>
/// A queued command as the user sees it.
/// </summary>
/// <param name="Id">Local identifier, so the UI can act on one entry.</param>
/// <param name="Description">What the user asked for, in their words.</param>
/// <param name="State">Where it stands.</param>
/// <param name="EnqueuedAt">When they asked for it.</param>
/// <param name="Attempts">How many times it has been tried.</param>
/// <param name="LastError">Why the last attempt did not settle it.</param>
/// <param name="ExpectedVersion">The version they were looking at.</param>
/// <param name="ServerVersion">The version the server holds, when it conflicted.</param>
public sealed record PendingChangeItem(
    Guid Id,
    string Description,
    QueuedState State,
    DateTimeOffset EnqueuedAt,
    int Attempts,
    string? LastError,
    int? ExpectedVersion,
    int? ServerVersion)
{
    /// <summary>Gets a value indicating whether this entry needs a person to decide.</summary>
    public bool NeedsDecision => State is QueuedState.Conflict or QueuedState.FailedPermanent;

    /// <summary>A one-line explanation of what happened, for the conflict list.</summary>
    public string Explanation => State switch
    {
        QueuedState.Conflict => string.Create(
            CultureInfo.InvariantCulture,
            $"The record changed while you were away. You saw version {ExpectedVersion}; it is now version {ServerVersion}."),

        QueuedState.FailedPermanent =>
            LastError ?? "The server refused this change and will refuse it again.",

        QueuedState.FailedRetryable => "Waiting to try again.",
        QueuedState.LocalPending => "Waiting to be sent.",
        QueuedState.Sending => "Sending.",
        QueuedState.Synced => "Sent.",
        _ => State.ToString(),
    };
}

/// <summary>
/// The offline and synchronization surface: what is queued, what conflicts, when
/// the cache was last current.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is stated, never implied. A user working offline needs to know
/// that they are, how stale what they are reading is, and exactly what has not
/// reached the server yet - an application that hides that is one that loses
/// somebody's afternoon.
/// </para>
/// <para>
/// A conflict is a decision, not an error. The list shows what the user meant and
/// what the record now says, and neither is applied until they choose.
/// </para>
/// </remarks>
public sealed class SyncStatusViewModel : ViewModelBase
{
    private readonly ISyncEngine _engine;
    private readonly IWriteQueue _queue;
    private readonly LocalCache _cache;
    private readonly TimeProvider _time;

    private ConnectionState _connection = ConnectionState.Online;
    private DateTimeOffset? _lastSyncedAt;
    private long _cursor;
    private int _outstandingCount;
    private int _attentionCount;
    private int _cachedPeople;
    private int _cachedCompanies;
    private int _cachedTasks;

    /// <summary>
    /// Depends on the synchronization seam rather than a concrete engine.
    /// </summary>
    /// <remarks>
    /// <see cref="ISyncEngine"/> and <see cref="IWriteQueue"/> are the boundary
    /// ADR-0013 names as replaceable by a future Rust engine. Binding the UI to
    /// them rather than to the C# implementation is what keeps that reversible.
    /// </remarks>
    public SyncStatusViewModel(ISyncEngine engine, LocalCache cache, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(cache);

        _engine = engine;
        _queue = cache;
        _cache = cache;
        _time = time ?? TimeProvider.System;

        Refresh();
    }

    /// <summary>Commands waiting to be sent or needing a decision.</summary>
    public ObservableCollection<PendingChangeItem> Pending { get; } = [];

    public ConnectionState Connection
    {
        get => _connection;
        private set
        {
            if (Set(ref _connection, value))
            {
                OnPropertyChanged(nameof(IsOffline));
                OnPropertyChanged(nameof(StatusLine));
            }
        }
    }

    /// <summary>When the cache last caught up with the server.</summary>
    public DateTimeOffset? LastSyncedAt
    {
        get => _lastSyncedAt;
        private set
        {
            if (Set(ref _lastSyncedAt, value))
            {
                OnPropertyChanged(nameof(StatusLine));
            }
        }
    }

    /// <summary>The change-feed position the cache holds.</summary>
    public long Cursor
    {
        get => _cursor;
        private set => Set(ref _cursor, value);
    }

    /// <summary>How many commands are waiting to reach the server.</summary>
    public int OutstandingCount
    {
        get => _outstandingCount;
        private set
        {
            if (Set(ref _outstandingCount, value))
            {
                OnPropertyChanged(nameof(HasOutstanding));
                OnPropertyChanged(nameof(StatusLine));
            }
        }
    }

    /// <summary>How many commands need a person to decide something.</summary>
    public int AttentionCount
    {
        get => _attentionCount;
        private set
        {
            if (Set(ref _attentionCount, value))
            {
                OnPropertyChanged(nameof(HasConflicts));
                OnPropertyChanged(nameof(StatusLine));
            }
        }
    }

    public int CachedPeople
    {
        get => _cachedPeople;
        private set => Set(ref _cachedPeople, value);
    }

    public int CachedCompanies
    {
        get => _cachedCompanies;
        private set => Set(ref _cachedCompanies, value);
    }

    public int CachedTasks
    {
        get => _cachedTasks;
        private set => Set(ref _cachedTasks, value);
    }

    public bool IsOffline => Connection == ConnectionState.Offline;

    public bool HasOutstanding => OutstandingCount > 0;

    public bool HasConflicts => AttentionCount > 0;

    public override bool IsEmpty => Pending.Count == 0;

    /// <summary>
    /// One line stating exactly where the client stands.
    /// </summary>
    /// <remarks>
    /// Deliberately specific. "Offline" alone leaves the user guessing how stale
    /// their screen is and whether their work is safe; this says both.
    /// </remarks>
    public string StatusLine
    {
        get
        {
            string staleness = LastSyncedAt is { } at
                ? string.Create(CultureInfo.InvariantCulture, $"last updated {Describe(_time.GetUtcNow() - at)}")
                : "never synchronized";

            string queued = OutstandingCount switch
            {
                0 => "nothing waiting to send",
                1 => "1 change waiting to send",
                _ => string.Create(CultureInfo.InvariantCulture, $"{OutstandingCount} changes waiting to send"),
            };

            string attention = AttentionCount == 0
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $", {AttentionCount} needing your decision");

            string connection = Connection switch
            {
                ConnectionState.Online => "Online",
                ConnectionState.Offline => "Offline",
                _ => "Synchronizing",
            };

            return string.Create(CultureInfo.InvariantCulture, $"{connection} - {staleness}, {queued}{attention}.");
        }
    }

    /// <summary>Sends what is queued and pulls what has changed.</summary>
    public Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        Connection = ConnectionState.Synchronizing;

        return RunAsync(async token =>
        {
            try
            {
                SyncOutcome outcome = await _engine.SynchronizeAsync(token).ConfigureAwait(true);

                Connection = ConnectionState.Online;
                Cursor = outcome.Cursor;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Unreachable rather than broken. The queue is durable, so
                // nothing is lost by being offline, and saying so plainly is
                // better than an error the user cannot act on.
                Connection = ConnectionState.Offline;
            }
            finally
            {
                Refresh();
            }
        }, cancellationToken);
    }

    /// <summary>Abandons a queued command the user has decided against.</summary>
    /// <remarks>
    /// Only ever at the user's request. A conflicted command is never discarded
    /// automatically: the whole point of surfacing it is that a person decides.
    /// </remarks>
    public void Discard(Guid id)
    {
        _queue.Remove(id);
        Refresh();
    }

    /// <summary>Re-reads the queue and cache counters.</summary>
    public void Refresh()
    {
        Pending.Clear();

        foreach (QueuedCommand command in _queue.ReadAll())
        {
            if (command.State == QueuedState.Synced)
            {
                continue;
            }

            Pending.Add(new PendingChangeItem(
                command.Id,
                command.Description,
                command.State,
                command.EnqueuedAt,
                command.Attempts,
                command.LastError,
                command.ExpectedVersion,
                command.ServerVersion));
        }

        (int outstanding, int attention) = _queue.ReadQueueCounts();

        OutstandingCount = outstanding;
        AttentionCount = attention;
        LastSyncedAt = _cache.ReadLastSyncedAt();
        Cursor = _cache.ReadCursor();

        (int people, int companies, int tasks) = _cache.ReadCounts();

        CachedPeople = people;
        CachedCompanies = companies;
        CachedTasks = tasks;

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StatusLine));
    }

    private static string Describe(TimeSpan age) => age switch
    {
        { TotalSeconds: < 90 } => "just now",
        { TotalMinutes: < 90 } => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes} minutes ago"),
        { TotalHours: < 36 } => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours} hours ago"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalDays} days ago"),
    };
}
