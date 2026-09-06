namespace AgencyOS.Domain.Audit;

/// <summary>Opaque, immutable identifier for an <see cref="AuditEvent"/>.</summary>
public readonly record struct AuditEventId(Guid Value)
{
    public static AuditEventId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
