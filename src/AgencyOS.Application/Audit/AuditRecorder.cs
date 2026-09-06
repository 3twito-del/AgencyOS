using System.Text.Json;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Releases;

namespace AgencyOS.Application.Audit;

/// <summary>
/// Builds audit records from the current execution context.
/// </summary>
/// <remarks>
/// Centralized so every audit record carries the same actor, client and
/// correlation fields. A handler that wrote these by hand would eventually write
/// one of them differently, and an audit trail is only as useful as it is
/// uniform.
/// </remarks>
public sealed class AuditRecorder
{
    private static readonly JsonSerializerOptions DeltaOptions = new(JsonSerializerDefaults.Web);

    private readonly IExecutionContext _execution;
    private readonly IClock _clock;
    private readonly IAuditRepository _audit;

    public AuditRecorder(IExecutionContext execution, IClock clock, IAuditRepository audit)
    {
        _execution = execution;
        _clock = clock;
        _audit = audit;
    }

    /// <summary>Appends an audit record describing a consequential change.</summary>
    public AuditEvent Record(
        string action,
        string entityType,
        string entityId,
        OrganizationId? organizationId = null,
        string? permission = null,
        object? semanticDelta = null,
        string? reason = null)
    {
        ClientIdentity? client = _execution.Client;

        AuditEvent auditEvent = AuditEvent.Record(
            action: action,
            entityType: entityType,
            entityId: entityId,
            occurredAt: _clock.UtcNow,
            correlationId: _execution.CorrelationId,
            actorUserId: _execution.UserId,
            actorSubject: _execution.Subject,
            organizationId: organizationId,
            permission: permission,
            semanticDelta: semanticDelta is null ? null : JsonSerializer.Serialize(semanticDelta, DeltaOptions),
            reason: reason,
            clientPlatform: client?.Platform,
            clientChannel: client is null ? null : ReleaseRingNames.ToWireName(client.Ring),
            clientVersion: client?.Version.Text,
            clientBuildId: client?.BuildId,
            apiContractVersion: client?.ApiContractVersion);

        _audit.Append(auditEvent);
        return auditEvent;
    }
}
