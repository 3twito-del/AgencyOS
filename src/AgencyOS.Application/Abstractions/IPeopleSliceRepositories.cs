using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Tasks;

namespace AgencyOS.Application.Abstractions;

/// <summary>
/// Repositories for the M2 people slice.
/// </summary>
/// <remarks>
/// Every lookup takes the tenant explicitly. There is deliberately no
/// <c>FindById</c> that omits it: a repository method that can be called without a
/// tenant is a cross-tenant read waiting to be written. The database enforces the
/// same containment through composite foreign keys (ADR-0011); this is the layer
/// that makes forgetting impossible in the first place.
/// </remarks>
public interface IPersonRepository
{
    Task<Person?> FindAsync(OrganizationId organizationId, PersonId id, CancellationToken cancellationToken = default);

    /// <summary>Determines whether a person exists and is active within the tenant.</summary>
    Task<bool> ExistsActiveAsync(
        OrganizationId organizationId,
        PersonId id,
        CancellationToken cancellationToken = default);

    void Add(Person person);
}

public interface ICompanyRepository
{
    Task<Company?> FindAsync(OrganizationId organizationId, CompanyId id, CancellationToken cancellationToken = default);

    /// <summary>Determines whether a company exists and is active within the tenant.</summary>
    Task<bool> ExistsActiveAsync(
        OrganizationId organizationId,
        CompanyId id,
        CancellationToken cancellationToken = default);

    void Add(Company company);
}

public interface IRelationshipRepository
{
    Task<ProfessionalRelationship?> FindAsync(
        OrganizationId organizationId,
        RelationshipId id,
        CancellationToken cancellationToken = default);

    void Add(ProfessionalRelationship relationship);
}

public interface IInteractionRepository
{
    Task<Interaction?> FindAsync(
        OrganizationId organizationId,
        InteractionId id,
        CancellationToken cancellationToken = default);

    void Add(Interaction interaction);
}

public interface ITaskRepository
{
    Task<TaskItem?> FindAsync(
        OrganizationId organizationId,
        TaskItemId id,
        CancellationToken cancellationToken = default);

    void Add(TaskItem task);
}
