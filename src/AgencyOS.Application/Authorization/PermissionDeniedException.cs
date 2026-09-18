using AgencyOS.Domain.Authorization;

namespace AgencyOS.Application.Authorization;

/// <summary>Raised when the acting user does not hold the required permission.</summary>
public sealed class PermissionDeniedException : Exception
{
    /// <remarks>
    /// The sentence names the capability, not the permission string
    /// (<c>AOS-R002-014</c>). The string itself stays on <see cref="Permission"/>
    /// and reaches the caller as the problem's <c>requiredPermission</c> extension,
    /// so nothing reading this by machine lost anything.
    /// </remarks>
    public PermissionDeniedException(string permission)
        : base(PermissionCapability.Describe(permission))
    {
        Permission = permission;
    }

    public string Permission { get; }
}

/// <summary>Raised when a command names an entity that does not exist.</summary>
public sealed class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entityType, string entityId)
        : base($"{entityType} '{entityId}' was not found.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }

    public string EntityType { get; }

    public string EntityId { get; }
}

/// <summary>Raised when a command is attempted without an authenticated actor.</summary>
public sealed class NotAuthenticatedException : Exception
{
    public NotAuthenticatedException()
        : base("An authenticated actor is required.")
    {
    }
}
