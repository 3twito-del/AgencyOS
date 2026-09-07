using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;

namespace AgencyOS.Domain.Provisioning;

/// <summary>
/// Records that the system has been initialized, exactly once.
/// </summary>
/// <remarks>
/// <para>
/// An explicit fact rather than an inference. "Is this system initialized?" could
/// be answered by asking whether any organization exists, but that is a
/// coincidence of the current rules rather than a statement of intent, and it
/// gives no way to make the answer atomic.
/// </para>
/// <para>
/// The row is a singleton by construction: the primary key is fixed at
/// <see cref="SingletonId"/>, so a second initialization is refused by the
/// database rather than by a check that two concurrent callers could both pass.
/// </para>
/// </remarks>
public sealed class SystemInitialization
{
    /// <summary>The only permitted primary key value.</summary>
    public const int SingletonId = 1;

    private SystemInitialization()
    {
    }

    /// <summary>Always <see cref="SingletonId"/>, enforced by a database constraint.</summary>
    public int Id { get; private set; }

    public DateTimeOffset InitializedAt { get; private set; }

    /// <summary>The organization created during initialization.</summary>
    public OrganizationId InitialOrganizationId { get; private set; }

    /// <summary>The user granted ownership during initialization.</summary>
    public UserId InitialOwnerUserId { get; private set; }

    public static SystemInitialization Record(
        OrganizationId organizationId,
        UserId ownerUserId,
        DateTimeOffset now)
    {
        return new SystemInitialization
        {
            Id = SingletonId,
            InitialOrganizationId = organizationId,
            InitialOwnerUserId = ownerUserId,
            InitializedAt = now,
        };
    }
}
