using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Representations;
using AgencyOS.Domain.Talent;

namespace AgencyOS.Application.Abstractions;

/// <summary>
/// Repositories for the M4 representation model.
/// </summary>
/// <remarks>
/// Every lookup takes the tenant explicitly, for the same reason the M2
/// repositories do: a method that can be called without a tenant is a cross-tenant
/// read waiting to be written. The database enforces the same containment through
/// composite foreign keys (ADR-0011).
/// </remarks>
public interface ITalentProfileRepository
{
    Task<TalentProfile?> FindAsync(
        OrganizationId organizationId,
        TalentProfileId id,
        CancellationToken cancellationToken = default);

    /// <summary>Finds the profile for a person, since there is at most one per tenant.</summary>
    Task<TalentProfile?> FindByPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default);

    void Add(TalentProfile profile);
}

public interface IProspectRepository
{
    Task<Prospect?> FindAsync(
        OrganizationId organizationId,
        ProspectId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the live pursuit of a person, if there is one.
    /// </summary>
    /// <remarks>
    /// At most one pursuit of a person may be open at a time. Two would mean two
    /// agents courting the same person without knowing about each other, which is
    /// the situation this system exists to prevent.
    /// </remarks>
    Task<Prospect?> FindOpenForPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default);

    void Add(Prospect prospect);
}

public interface IRepresentationRepository
{
    Task<Representation?> FindAsync(
        OrganizationId organizationId,
        RepresentationId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the representation of a person that can still change.
    /// </summary>
    /// <remarks>
    /// At most one exists, enforced here and again by a partial unique index. A
    /// duplicate active representation is what a retried conversion would produce,
    /// so it is made impossible rather than merely unlikely.
    /// </remarks>
    Task<Representation?> FindNonTerminalForPersonAsync(
        OrganizationId organizationId,
        PersonId personId,
        CancellationToken cancellationToken = default);

    void Add(Representation representation);
}

public interface ICreditRepository
{
    Task<Credit?> FindAsync(
        OrganizationId organizationId,
        CreditId id,
        CancellationToken cancellationToken = default);

    void Add(Credit credit);
}

public interface IMaterialRepository
{
    Task<Material?> FindAsync(
        OrganizationId organizationId,
        MaterialId id,
        CancellationToken cancellationToken = default);

    void Add(Material material);
}
