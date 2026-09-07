using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.SavedViews;

/// <summary>Opaque, immutable identifier for a <see cref="SavedView"/>.</summary>
public readonly record struct SavedViewId(Guid Value)
{
    public static SavedViewId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// A named query a user saved for themselves.
/// </summary>
/// <remarks>
/// <para>
/// User-owned in M3, not shared. Sharing a view means deciding what happens when
/// the owner's permissions differ from a viewer's, and that is a real design
/// question rather than a checkbox - it is deferred until there is a reason to
/// answer it.
/// </para>
/// <para>
/// The definition is a validated, versioned document, never query text. See
/// <see cref="SavedViewDefinition"/>.
/// </para>
/// </remarks>
public sealed class SavedView
{
    private SavedView()
    {
    }

    public SavedViewId Id { get; private set; }

    /// <summary>The tenant that owns this record.</summary>
    public OrganizationId OrganizationId { get; private set; }

    /// <summary>The user this view belongs to. Views are private in M3.</summary>
    public UserId OwnerUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public SavedViewTarget Target { get; private set; }

    /// <summary>The validated query document.</summary>
    public SavedViewDefinition Definition { get; private set; } = null!;

    /// <summary>Schema version of <see cref="Definition"/>, denormalized for querying.</summary>
    public int DefinitionVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every mutation.</summary>
    public int Version { get; private set; }

    public static SavedView Create(
        OrganizationId organizationId,
        UserId ownerUserId,
        string name,
        SavedViewDefinition definition,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        definition.Validate();

        return new SavedView
        {
            Id = SavedViewId.New(),
            OrganizationId = organizationId,
            OwnerUserId = ownerUserId,
            Name = Ensure.NotBlankMax(name, nameof(name), 128),
            Target = definition.Target,
            Definition = definition,
            DefinitionVersion = definition.DefinitionVersion,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    /// <summary>Renames the view and replaces its definition.</summary>
    public void Update(string name, SavedViewDefinition definition, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        definition.Validate();

        Name = Ensure.NotBlankMax(name, nameof(name), 128);
        Target = definition.Target;
        Definition = definition;
        DefinitionVersion = definition.DefinitionVersion;
        UpdatedAt = now;
        Version++;
    }

    /// <summary>Fails unless the caller observed the current version.</summary>
    public void RequireVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyConflictException(nameof(SavedView), Id.ToString(), expectedVersion, Version);
        }
    }
}
