namespace AgencyOS.Domain.Identity;

/// <summary>
/// Opaque, immutable identifier for a <see cref="User"/>.
/// </summary>
/// <remarks>
/// Per <c>docs/08_DATA_MODEL_FOUNDATION.md</c>, identifiers are opaque and carry
/// no business meaning. UUIDv7 is used so identifiers sort by creation time,
/// which keeps index locality without leaking anything beyond a timestamp.
/// </remarks>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
