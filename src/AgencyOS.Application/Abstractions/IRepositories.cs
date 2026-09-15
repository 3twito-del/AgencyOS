using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Provisioning;
using AgencyOS.Domain.Releases;

namespace AgencyOS.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> FindByIdAsync(UserId id, CancellationToken cancellationToken = default);

    /// <summary>Finds a user by identity-provider subject.</summary>
    /// <remarks>
    /// The seam that keeps the identity provider replaceable: swapping providers
    /// changes what a subject string looks like, not how a user is located.
    /// </remarks>
    Task<User?> FindBySubjectAsync(string subject, CancellationToken cancellationToken = default);

    void Add(User user);
}

public interface IOrganizationRepository
{
    Task<Organization?> FindByIdAsync(OrganizationId id, CancellationToken cancellationToken = default);

    void Add(Organization organization);
}

public interface IMembershipRepository
{
    Task<Membership?> FindByIdAsync(MembershipId id, CancellationToken cancellationToken = default);

    /// <summary>Returns the active memberships held by a user.</summary>
    Task<IReadOnlyList<Membership>> ListActiveForUserAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    Task<Membership?> FindActiveAsync(
        OrganizationId organizationId,
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active memberships held in an organization.
    /// </summary>
    /// <remarks>
    /// Active only. A revoked membership is retained so historical authority stays
    /// explainable, and it confers nothing, so it is not who the organization is
    /// now.
    /// </remarks>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Every active membership, in the order they were granted.</returns>
    Task<IReadOnlyList<Membership>> ListActiveForOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    void Add(Membership membership);
}

/// <summary>
/// Append-only access to the audit trail.
/// </summary>
/// <remarks>
/// The interface exposes <see cref="Append"/> and reads. There is deliberately no
/// update or delete member: application code has no vocabulary for changing an
/// audit record, so it cannot request one by mistake.
/// </remarks>
public interface IAuditRepository
{
    void Append(AuditEvent auditEvent);

    Task<IReadOnlyList<AuditEvent>> ListForEntityAsync(
        string entityType,
        string entityId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEvent>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Access to the one-time system initialization record.
/// </summary>
/// <remarks>
/// There is no way to clear initialization. Un-initializing a system would mean
/// discarding the authority chain the audit trail explains, so it is not
/// expressible here; a fresh system is a fresh database.
/// </remarks>
public interface ISystemInitializationRepository
{
    /// <summary>Determines whether the system has been initialized.</summary>
    Task<bool> IsInitializedAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the initialization record, or <see langword="null"/> when absent.</summary>
    Task<SystemInitialization?> FindAsync(CancellationToken cancellationToken = default);

    void Add(SystemInitialization initialization);
}

public interface IReleasePolicyRepository
{
    Task<ReleasePolicy?> FindAsync(
        string platform,
        ReleaseRing ring,
        CancellationToken cancellationToken = default);

    void Add(ReleasePolicy policy);
}
