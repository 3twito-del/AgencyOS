namespace AgencyOS.Domain.Memberships;

/// <summary>Lifecycle state of a membership.</summary>
public enum MembershipStatus
{
    Active = 1,

    /// <summary>Ended. The row is retained so historical authority is explainable.</summary>
    Revoked = 2,
}
