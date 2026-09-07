namespace AgencyOS.Domain.People;

/// <summary>Opaque, immutable identifier for a <see cref="Person"/>.</summary>
public readonly record struct PersonId(Guid Value)
{
    public static PersonId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Lifecycle state of a person record.</summary>
public enum PersonStatus
{
    Active = 1,

    /// <summary>Retained in full, excluded from working lists. Never a delete.</summary>
    Archived = 2,
}
