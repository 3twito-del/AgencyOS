namespace AgencyOS.Application.Authorization;

/// <summary>Raised when the acting user does not hold the required permission.</summary>
public sealed class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string permission)
        : base($"Permission '{permission}' is required.")
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
