using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Releases;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

internal sealed class UserRepository : IUserRepository
{
    private readonly AgencyOsDbContext _context;

    public UserRepository(AgencyOsDbContext context) => _context = context;

    public Task<User?> FindByIdAsync(UserId id, CancellationToken cancellationToken = default) =>
        _context.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<User?> FindBySubjectAsync(string subject, CancellationToken cancellationToken = default) =>
        _context.Users.FirstOrDefaultAsync(x => x.ExternalSubject == subject, cancellationToken);

    public void Add(User user) => _context.Users.Add(user);
}

internal sealed class OrganizationRepository : IOrganizationRepository
{
    private readonly AgencyOsDbContext _context;

    public OrganizationRepository(AgencyOsDbContext context) => _context = context;

    public Task<Organization?> FindByIdAsync(OrganizationId id, CancellationToken cancellationToken = default) =>
        _context.Organizations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(Organization organization) => _context.Organizations.Add(organization);
}

internal sealed class MembershipRepository : IMembershipRepository
{
    private readonly AgencyOsDbContext _context;

    public MembershipRepository(AgencyOsDbContext context) => _context = context;

    public Task<Membership?> FindByIdAsync(MembershipId id, CancellationToken cancellationToken = default) =>
        _context.Memberships.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Membership>> ListActiveForUserAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Memberships
            .Where(x => x.UserId == userId && x.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Membership?> FindActiveAsync(
        OrganizationId organizationId,
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        return _context.Memberships.FirstOrDefaultAsync(
            x => x.OrganizationId == organizationId
                && x.UserId == userId
                && x.Status == MembershipStatus.Active,
            cancellationToken);
    }

    public void Add(Membership membership) => _context.Memberships.Add(membership);
}

/// <summary>
/// Append-only audit access.
/// </summary>
/// <remarks>
/// Reads are tracked-free: an audit record pulled into the change tracker could
/// in principle be marked modified by later code, and the cheapest way to make
/// that impossible is to never track it.
/// </remarks>
internal sealed class AuditRepository : IAuditRepository
{
    private readonly AgencyOsDbContext _context;

    public AuditRepository(AgencyOsDbContext context) => _context = context;

    public void Append(AuditEvent auditEvent) => _context.AuditEvents.Add(auditEvent);

    public async Task<IReadOnlyList<AuditEvent>> ListForEntityAsync(
        string entityType,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityType == entityType && x.EntityId == entityId)
            .OrderBy(x => x.OccurredAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEvent>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _context.AuditEvents
            .AsNoTracking()
            .OrderByDescending(x => x.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class ReleasePolicyRepository : IReleasePolicyRepository
{
    private readonly AgencyOsDbContext _context;

    public ReleasePolicyRepository(AgencyOsDbContext context) => _context = context;

    public Task<ReleasePolicy?> FindAsync(
        string platform,
        ReleaseRing ring,
        CancellationToken cancellationToken = default)
    {
        return _context.ReleasePolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Platform == platform && x.Ring == ring, cancellationToken);
    }

    public void Add(ReleasePolicy policy) => _context.ReleasePolicies.Add(policy);
}
