namespace AgencyOS.Domain.Organizations;

/// <summary>Opaque, immutable identifier for an <see cref="Organization"/>.</summary>
public readonly record struct OrganizationId(Guid Value)
{
    public static OrganizationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
