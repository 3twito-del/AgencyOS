using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Audit;

/// <summary>
/// An immutable record that a consequential thing happened.
/// </summary>
/// <remarks>
/// <para>
/// Append-only by design, and enforced in three independent places, because a
/// single point of enforcement is a single point of failure for the property that
/// makes the trail worth keeping:
/// </para>
/// <list type="number">
/// <item>this type exposes no mutator and no public constructor, so application
/// code cannot express a change;</item>
/// <item>a save interceptor rejects any modified or deleted audit entry, catching
/// a change made through the object graph;</item>
/// <item>a database trigger rejects UPDATE and DELETE, catching a change made by
/// anything that bypasses the application, including a direct SQL session.</item>
/// </list>
/// <para>
/// Fields follow the audit record described in
/// <c>docs/07_SECURITY_AND_AUDIT.md</c>: actor, source/client, timestamp, entity,
/// action, semantic delta, reason, and correlation identifier.
/// </para>
/// </remarks>
public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    public AuditEventId Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    /// <summary>The acting user, or <see langword="null"/> for a system action.</summary>
    public UserId? ActorUserId { get; private set; }

    /// <summary>Identity-provider subject of the actor, retained even if the user row later changes.</summary>
    public string? ActorSubject { get; private set; }

    /// <summary>Organization the action was scoped to, when applicable.</summary>
    public OrganizationId? OrganizationId { get; private set; }

    /// <summary>The permission that authorized the action.</summary>
    public string? Permission { get; private set; }

    /// <summary>Semantic description of what changed, as JSON.</summary>
    public string? SemanticDelta { get; private set; }

    /// <summary>Caller-supplied reason, where the operation requires one.</summary>
    public string? Reason { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public string? ClientPlatform { get; private set; }

    public string? ClientChannel { get; private set; }

    public string? ClientVersion { get; private set; }

    public string? ClientBuildId { get; private set; }

    public int? ApiContractVersion { get; private set; }

    /// <summary>Records that an action occurred.</summary>
    /// <remarks>
    /// There is no counterpart that changes one. Construction is the only
    /// transition an audit event has.
    /// </remarks>
    public static AuditEvent Record(
        string action,
        string entityType,
        string entityId,
        DateTimeOffset occurredAt,
        string correlationId,
        UserId? actorUserId = null,
        string? actorSubject = null,
        OrganizationId? organizationId = null,
        string? permission = null,
        string? semanticDelta = null,
        string? reason = null,
        string? clientPlatform = null,
        string? clientChannel = null,
        string? clientVersion = null,
        string? clientBuildId = null,
        int? apiContractVersion = null)
    {
        return new AuditEvent
        {
            Id = AuditEventId.New(),
            Action = Ensure.NotBlankMax(action, nameof(action), 128),
            EntityType = Ensure.NotBlankMax(entityType, nameof(entityType), 128),
            EntityId = Ensure.NotBlankMax(entityId, nameof(entityId), 128),
            OccurredAt = occurredAt,
            CorrelationId = Ensure.NotBlankMax(correlationId, nameof(correlationId), 128),
            ActorUserId = actorUserId,
            ActorSubject = Ensure.OptionalMax(actorSubject, nameof(actorSubject), 256),
            OrganizationId = organizationId,
            Permission = Ensure.OptionalMax(permission, nameof(permission), 128),
            SemanticDelta = semanticDelta,
            Reason = Ensure.OptionalMax(reason, nameof(reason), 1024),
            ClientPlatform = Ensure.OptionalMax(clientPlatform, nameof(clientPlatform), 64),
            ClientChannel = Ensure.OptionalMax(clientChannel, nameof(clientChannel), 32),
            ClientVersion = Ensure.OptionalMax(clientVersion, nameof(clientVersion), 64),
            ClientBuildId = Ensure.OptionalMax(clientBuildId, nameof(clientBuildId), 64),
            ApiContractVersion = apiContractVersion,
        };
    }
}
