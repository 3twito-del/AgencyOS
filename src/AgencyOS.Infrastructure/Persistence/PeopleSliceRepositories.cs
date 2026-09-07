using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <remarks>
/// Every query filters on the tenant as well as the identifier. The predicate is
/// not redundant with the composite foreign keys: those stop bad data being
/// written, this stops good data being read by the wrong tenant.
/// </remarks>
internal sealed class PersonRepository : IPersonRepository
{
    private readonly AgencyOsDbContext _context;

    public PersonRepository(AgencyOsDbContext context) => _context = context;

    public Task<Person?> FindAsync(
        OrganizationId organizationId,
        PersonId id,
        CancellationToken cancellationToken = default)
    {
        return _context.People.FirstOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            cancellationToken);
    }

    public Task<bool> ExistsActiveAsync(
        OrganizationId organizationId,
        PersonId id,
        CancellationToken cancellationToken = default)
    {
        return _context.People
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == id && x.OrganizationId == organizationId && x.Status == PersonStatus.Active,
                cancellationToken);
    }

    public void Add(Person person) => _context.People.Add(person);
}

internal sealed class CompanyRepository : ICompanyRepository
{
    private readonly AgencyOsDbContext _context;

    public CompanyRepository(AgencyOsDbContext context) => _context = context;

    public Task<Company?> FindAsync(
        OrganizationId organizationId,
        CompanyId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Companies.FirstOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            cancellationToken);
    }

    public Task<bool> ExistsActiveAsync(
        OrganizationId organizationId,
        CompanyId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Companies
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == id && x.OrganizationId == organizationId && x.Status == CompanyStatus.Active,
                cancellationToken);
    }

    public void Add(Company company) => _context.Companies.Add(company);
}

internal sealed class RelationshipRepository : IRelationshipRepository
{
    private readonly AgencyOsDbContext _context;

    public RelationshipRepository(AgencyOsDbContext context) => _context = context;

    public Task<ProfessionalRelationship?> FindAsync(
        OrganizationId organizationId,
        RelationshipId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Relationships.FirstOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            cancellationToken);
    }

    public void Add(ProfessionalRelationship relationship) => _context.Relationships.Add(relationship);
}

internal sealed class InteractionRepository : IInteractionRepository
{
    private readonly AgencyOsDbContext _context;

    public InteractionRepository(AgencyOsDbContext context) => _context = context;

    public Task<Interaction?> FindAsync(
        OrganizationId organizationId,
        InteractionId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Interactions
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);
    }

    public void Add(Interaction interaction) => _context.Interactions.Add(interaction);
}

internal sealed class TaskRepository : ITaskRepository
{
    private readonly AgencyOsDbContext _context;

    public TaskRepository(AgencyOsDbContext context) => _context = context;

    public Task<TaskItem?> FindAsync(
        OrganizationId organizationId,
        TaskItemId id,
        CancellationToken cancellationToken = default)
    {
        return _context.Tasks.FirstOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId,
            cancellationToken);
    }

    public void Add(TaskItem task) => _context.Tasks.Add(task);
}
