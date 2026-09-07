using AgencyOS.Domain.Common;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Sync;

/// <summary>What happened to a record, from a synchronizing client's point of view.</summary>
public enum ChangeKind
{
    /// <summary>The record was created or modified; fetch its current state.</summary>
    Upsert = 1,

    /// <summary>
    /// The record left the working set - archived, ended or revoked.
    /// </summary>
    /// <remarks>
    /// AgencyOS does not delete business records, so a client never has to handle
    /// a true deletion. It still has to stop showing an archived record, which is
    /// why this is distinct from an upsert.
    /// </remarks>
    Removed = 2,
}

/// <summary>
/// One entry in a tenant's change feed.
/// </summary>
/// <remarks>
/// <para>
/// Written in the same transaction as the business change it describes, so a
/// change can never be committed without its feed entry or the reverse.
/// </para>
/// <para>
/// <see cref="Sequence"/> is allocated from a per-tenant counter held under a row
/// lock, which makes sequence order equal commit order. A bare
/// <c>BIGSERIAL</c> would not: two transactions can take sequence values 5 and 6
/// and commit in the opposite order, so a client that reads up to 6 would never
/// see 5. See <c>docs/adr/ADR-0013-synchronization-architecture.md</c>.
/// </para>
/// </remarks>
public sealed class ChangeLogEntry
{
    private ChangeLogEntry()
    {
    }

    /// <summary>Per-tenant monotonic position. Ordered by commit, not by insert.</summary>
    public long Sequence { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>Entity type name, matching the audit vocabulary.</summary>
    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public ChangeKind Kind { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static ChangeLogEntry Record(
        OrganizationId organizationId,
        long sequence,
        string entityType,
        string entityId,
        ChangeKind kind,
        DateTimeOffset occurredAt)
    {
        return new ChangeLogEntry
        {
            Sequence = sequence,
            OrganizationId = organizationId,
            EntityType = Ensure.NotBlankMax(entityType, nameof(entityType), 128),
            EntityId = Ensure.NotBlankMax(entityId, nameof(entityId), 128),
            Kind = kind,
            OccurredAt = occurredAt,
        };
    }
}

/// <summary>
/// The per-tenant counter that hands out change-feed positions.
/// </summary>
/// <remarks>
/// Allocated with an <c>UPDATE … RETURNING</c>, which takes the row lock and so
/// serializes allocation with commit order within a tenant. Writes to one tenant
/// therefore serialize on this row; at one agency's volume that is unmeasurable,
/// and it buys a change feed with no ordering holes.
/// </remarks>
public sealed class ChangeSequence
{
    private ChangeSequence()
    {
    }

    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The last position handed out.</summary>
    public long LastValue { get; private set; }

    public static ChangeSequence Start(OrganizationId organizationId) =>
        new() { OrganizationId = organizationId, LastValue = 0 };
}
