namespace AgencyOS.Domain.Common;

/// <summary>
/// Raised when a caller tried to change a record it had not seen the latest version of.
/// </summary>
/// <remarks>
/// <para>
/// This is the failure that makes offline editing safe. A client that has been
/// disconnected sends the version it last observed; if the record moved on in the
/// meantime, the write is refused rather than applied over the newer state.
/// </para>
/// <para>
/// It carries both versions because the client needs them: the one it believed,
/// and the one that is actually current. That is the difference between "your
/// change failed" and a conflict the user can actually resolve.
/// </para>
/// </remarks>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string entityType, string entityId, int expectedVersion, int actualVersion)
        : base($"{entityType} '{entityId}' has changed since you last saw it "
            + $"(you had version {expectedVersion}, it is now {actualVersion}).")
    {
        EntityType = entityType;
        EntityId = entityId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public string EntityType { get; }

    public string EntityId { get; }

    /// <summary>The version the caller believed was current.</summary>
    public int ExpectedVersion { get; }

    /// <summary>The version that is actually current.</summary>
    public int ActualVersion { get; }
}
