using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Interactions;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Provisioning;
using AgencyOS.Domain.Relationships;
using AgencyOS.Domain.Releases;
using AgencyOS.Domain.Tasks;
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

    /// <summary>
    /// The one-time initialization record. A singleton enforced by the database,
    /// not by convention.
    /// </summary>
    public DbSet<SystemInitialization> SystemInitializations => Set<SystemInitialization>();

    // ---- People vertical slice (M2) ----

    public DbSet<Person> People => Set<Person>();

    /// <summary>External bodies the agency holds records about, never tenants (ADR-0010).</summary>
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<ProfessionalRelationship> Relationships => Set<ProfessionalRelationship>();

    public DbSet<Interaction> Interactions => Set<Interaction>();

    public DbSet<InteractionParticipant> InteractionParticipants => Set<InteractionParticipant>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgencyOsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
