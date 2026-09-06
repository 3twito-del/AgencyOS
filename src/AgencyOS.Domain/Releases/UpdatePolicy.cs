namespace AgencyOS.Domain.Releases;

/// <summary>
/// The update policy states defined in <c>docs/06_FORCED_UPDATE_PROTOCOL.md</c>.
/// </summary>
/// <remarks>
/// Update enforcement is a data-integrity mechanism, not a UX affordance. The two
/// blocking states are <see cref="Mandatory"/> and <see cref="Revoked"/>; the
/// others inform the client without restricting it.
/// </remarks>
public enum UpdatePolicy
{
    /// <summary>Client is current. Nothing to do.</summary>
    None = 0,

    /// <summary>A newer build exists.</summary>
    Available = 1,

    /// <summary>A newer build exists and updating is advised.</summary>
    Recommended = 2,

    /// <summary>The client must update. Protected mutations are refused until it does.</summary>
    Mandatory = 3,

    /// <summary>
    /// The build is withdrawn. It is refused for protected mutations regardless of
    /// what its own UI believes.
    /// </summary>
    Revoked = 4,
}
