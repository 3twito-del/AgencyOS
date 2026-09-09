using AgencyOS.Domain.Ai;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for <see cref="AiContextLease"/>.</summary>
/// <remarks>
/// <para>
/// A table of its own rather than columns on <c>ai_runs</c>. A lease has its own
/// lifecycle, its own expiry and its own one-time consumption, and a run can
/// legitimately have none, one, or — if a future build re-issues after an
/// invalidation — more than one. Folding that into the run row would mean
/// nullable columns whose combinations no constraint could describe (§AB).
/// </para>
/// <para>
/// The subject is carried denormalized rather than through a typed arc. Unlike
/// <c>ai_runs</c>, a lease is short-lived and never queried by subject; what it
/// needs is to <em>refuse</em> a mismatched one, which is an equality check
/// against a value, not a foreign key. The run's own arc already guarantees the
/// subject exists and belongs to the tenant.
/// </para>
/// </remarks>
public sealed class AiContextLeaseConfiguration : IEntityTypeConfiguration<AiContextLease>
{
    public void Configure(EntityTypeBuilder<AiContextLease> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ai_context_leases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AiContextLeaseId(value))
            .ValueGeneratedNever();

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.AgentRunId)
            .HasColumnName("ai_run_id")
            .HasConversion(id => id.Value, value => new AgentRunId(value))
            .IsRequired();

        builder.Property(x => x.SubjectKind).HasColumnName("subject_kind").IsRequired();
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");

        builder.Property(x => x.Residency).HasColumnName("residency").IsRequired();

        builder.Property(x => x.ContextFingerprint)
            .HasColumnName("context_fingerprint").HasMaxLength(64).IsRequired();

        builder.Property(x => x.ModelKey)
            .HasColumnName("model_key").HasMaxLength(200).IsRequired();

        builder.Property(x => x.PolicyVersion).HasColumnName("policy_version").IsRequired();

        builder.Property(x => x.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.State).HasColumnName("state").IsRequired();
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsConcurrencyToken()
            .IsRequired();

        // The lookup the protocol performs: find the issued lease for this run.
        builder.HasIndex(x => new { x.OrganizationId, x.AgentRunId, x.State })
            .HasDatabaseName("ix_ai_context_leases_run");

        // Expiry sweeps and diagnostics.
        builder.HasIndex(x => new { x.State, x.ExpiresAt })
            .HasDatabaseName("ix_ai_context_leases_expiry");
    }
}
