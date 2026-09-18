using System.Linq;

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
        : base($"This {Describe(entityType)} has changed since you last saw it "
            + $"(you had version {expectedVersion}, it is now {actualVersion}). "
            + "Refresh to see the current version, then decide what to do.")
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

    /// <summary>The kind of record, as a person would say it.</summary>
    /// <param name="entityType">The domain type's name, in Pascal case.</param>
    /// <returns>The same words, spaced and lower-cased.</returns>
    /// <remarks>
    /// <c>AOS-R002-007</c>. The sentence used to name the record by its identifier
    /// — "Contract '01a0a1ff-…' has changed" — and the product displays that
    /// identifier nowhere, so it told the reader nothing about which record they
    /// had lost. The operator already knows which record they were editing; what
    /// they need is what kind of thing it is and what to do next. The identifier
    /// is still in the problem's <c>entityId</c> extension for anything reading
    /// this by machine.
    /// </remarks>
    private static string Describe(string entityType) =>
        string.IsNullOrWhiteSpace(entityType)
            ? "record"
            : string.Concat(entityType.Select((character, index) =>
                index > 0 && char.IsUpper(character)
                    ? " " + char.ToLowerInvariant(character)
                    : char.ToLowerInvariant(character).ToString()));
}
