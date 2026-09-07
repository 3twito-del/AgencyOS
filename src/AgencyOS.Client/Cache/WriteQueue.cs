namespace AgencyOS.Client.Cache;

/// <summary>
/// The commands that may be queued while offline.
/// </summary>
/// <remarks>
/// <para>
/// A closed, deliberately small allow-list. Every entry is low-risk and
/// reversible, and every one has defined conflict semantics: creates are guarded
/// by an idempotency key, and edits and transitions carry the version the user
/// was looking at.
/// </para>
/// <para>
/// Authorization changes, release-policy changes, bootstrap and destructive
/// administrative actions are absent by design, and so is anything to do with
/// deals, contracts or money. Queueing a privileged operation would mean deciding
/// offline that it is permitted, and possession of a cached record is not
/// permission to change it later - the server re-authorizes every queued command
/// when it finally runs.
/// </para>
/// </remarks>
public enum QueuedOperation
{
    CreatePerson = 1,
    UpdatePerson = 2,
    CreateCompany = 3,
    UpdateCompany = 4,
    CreateTask = 5,
    CompleteTask = 6,
    ReopenTask = 7,
    RecordInteraction = 8,
}

/// <summary>
/// Where a queued command is in its life.
/// </summary>
/// <remarks>
/// These are exactly the states modelled and checked in
/// <c>specs/OfflineWriteQueue.tla</c>. Keeping the names identical is deliberate:
/// a model that describes different states than the code is a model of nothing.
/// </remarks>
public enum QueuedState
{
    /// <summary>Committed locally, not yet sent.</summary>
    LocalPending = 1,

    /// <summary>Submitted; the answer has not arrived.</summary>
    Sending = 2,

    /// <summary>The server acknowledged it. The effect happened exactly once.</summary>
    Synced = 3,

    /// <summary>
    /// The record moved on before this command reached the server.
    /// </summary>
    /// <remarks>
    /// A first-class state with its own UI, not an error. The user is shown what
    /// they intended and what the record says now, and chooses. Nothing is
    /// discarded and nothing is silently applied.
    /// </remarks>
    Conflict = 4,

    /// <summary>Failed for a reason worth retrying: no network, a server error.</summary>
    FailedRetryable = 5,

    /// <summary>
    /// Failed for a reason retrying cannot fix: refused, or no longer permitted.
    /// </summary>
    FailedPermanent = 6,
}

/// <summary>
/// One command waiting to reach the server.
/// </summary>
/// <param name="Id">Local identifier of the queue entry.</param>
/// <param name="IdempotencyKey">
/// Generated before the first submission and never regenerated, so every retry -
/// across reconnects, restarts and crashes - is recognizable to the server as the
/// same command.
/// </param>
/// <param name="Operation">Which command this is.</param>
/// <param name="TargetId">The record it acts on, for commands that act on one.</param>
/// <param name="ExpectedVersion">The version the user was looking at, for guarded commands.</param>
/// <param name="Payload">The request body, as JSON.</param>
/// <param name="State">Where it is in its life.</param>
/// <param name="Attempts">How many times submission has been tried.</param>
/// <param name="EnqueuedAt">When the user asked for it.</param>
/// <param name="LastAttemptAt">When it was last submitted.</param>
/// <param name="LastError">Why the last attempt did not succeed.</param>
/// <param name="ServerVersion">The version the server reported, when it conflicted.</param>
/// <param name="Description">What to show the user, in their words rather than the protocol's.</param>
public sealed record QueuedCommand(
    Guid Id,
    string IdempotencyKey,
    QueuedOperation Operation,
    Guid? TargetId,
    int? ExpectedVersion,
    string Payload,
    QueuedState State,
    int Attempts,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    int? ServerVersion,
    string Description)
{
    /// <summary>Gets a value indicating whether this command still wants to be sent.</summary>
    public bool IsOutstanding => State is QueuedState.LocalPending or QueuedState.FailedRetryable;

    /// <summary>Gets a value indicating whether this command needs a person to decide something.</summary>
    public bool NeedsAttention => State is QueuedState.Conflict or QueuedState.FailedPermanent;
}
