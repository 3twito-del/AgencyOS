namespace AgencyOS.Contracts.Audit;

/// <summary>A record from the append-only audit trail.</summary>
/// <param name="Id">Audit event identifier.</param>
/// <param name="OccurredAt">When the action happened, UTC.</param>
/// <param name="Action">Action name, for example <c>organization.created</c>.</param>
/// <param name="EntityType">Type of the entity acted on.</param>
/// <param name="EntityId">Identifier of the entity acted on.</param>
/// <param name="ActorUserId">Acting user, when there was one.</param>
/// <param name="OrganizationId">Organization scope, when applicable.</param>
/// <param name="Permission">Permission that authorized the action.</param>
/// <param name="SemanticDelta">What changed, as JSON.</param>
/// <param name="CorrelationId">Correlation identifier.</param>
/// <param name="ClientChannel">Release ring of the acting client.</param>
/// <param name="ClientVersion">Version of the acting client.</param>
public sealed record AuditEventResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    string EntityId,
    Guid? ActorUserId,
    Guid? OrganizationId,
    string? Permission,
    string? SemanticDelta,
    string CorrelationId,
    string? ClientChannel,
    string? ClientVersion);
