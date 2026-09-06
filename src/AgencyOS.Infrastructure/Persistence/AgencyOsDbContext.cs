using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Releases;
using Microsoft.EntityFrameworkCore;

namespace AgencyOS.Infrastructure.Persistence;

/// <summary>
/// The canonical PostgreSQL context.
/// </summary>
/// <remarks>
/// <c>docs/02_ARCHITECTURE.md</c>: PostgreSQL is canonical. Nothing else holds
/// truth, and the Windows client never sees this schema - it talks to versioned
/// contracts only.
/// </remarks>
public sealed class AgencyOsDbContext : DbContext, IUnitOfWork
{
    public AgencyOsDbContext(DbContextOptions<AgencyOsDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    /// <summary>
    /// The audit trail. Append-only: see <see cref="AuditAppendOnlyInterceptor"/>
    /// and the database trigger installed by the initial migration.
    /// </summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<ReleasePolicy> ReleasePolicies => Set<ReleasePolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgencyOsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
