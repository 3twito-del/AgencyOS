namespace AgencyOS.Domain.Identity;

/// <summary>Lifecycle state of a user account.</summary>
public enum UserStatus
{
    Active = 1,

    /// <summary>Retained for audit history but unable to authenticate or act.</summary>
    Suspended = 2,

    Deactivated = 3,
}
