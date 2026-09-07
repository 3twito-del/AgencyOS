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
using AgencyOS.Domain.Idempotency;
using AgencyOS.Domain.Releases;
using AgencyOS.Domain.SavedViews;
using AgencyOS.Domain.Sync;
using AgencyOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    private readonly IClock _clock;

    public AgencyOsDbContext(DbContextOptions<AgencyOsDbContext> options, IClock clock)
        : base(options) => _clock = clock;

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

    // ---- Search, saved views and synchronization (M3) ----

    /// <summary>Per-user named queries. Private to their owner (ADR-0013).</summary>
    public DbSet<SavedView> SavedViews => Set<SavedView>();

    /// <summary>
    /// The per-tenant change feed. Written by <see cref="ChangeFeedRecorder"/> in
    /// the same transaction as the change it describes.
    /// </summary>
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();

    /// <summary>The counter that hands out commit-ordered feed positions.</summary>
    public DbSet<ChangeSequence> ChangeSequences => Set<ChangeSequence>();

    /// <summary>
    /// Recorded idempotency keys, which make a retried offline command safe
    /// (ADR-0014).
    /// </summary>
    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    /// <summary>
    /// Saves, recording a change-feed entry for every cached record that moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The feed entries and the business change commit together or not at all. A
    /// change without its entry would be invisible to every client until something
    /// else touched the record; an entry without its change would send clients to
    /// fetch a state that does not exist.
    /// </para>
    /// <para>
    /// A transaction is opened when the caller has not already opened one, because
    /// the position allocation must hold the tenant's counter row until the same
    /// commit. When the caller owns a transaction, that one is used and committed
    /// by them.
    /// </para>
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PendingChange> pending = ChangeFeedRecorder.Collect(ChangeTracker);

        if (pending.Count == 0)
        {
            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Database.CurrentTransaction is not null)
        {
            await ChangeFeedRecorder
                .RecordAsync(this, pending, _clock.UtcNow, cancellationToken)
                .ConfigureAwait(false);

            return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await using IDbContextTransaction transaction =
            await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ChangeFeedRecorder
            .RecordAsync(this, pending, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        int written = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return written;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AgencyOsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
