using AgencyOS.Domain.Audit;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Releases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the append-only audit trail.
/// </summary>
/// <remarks>
/// Deliberately carries no foreign keys. An audit record must survive the removal
/// or archival of what it describes; a referential constraint would let a delete
/// elsewhere cascade into the trail, which is the one thing the trail must never
/// permit.
/// </remarks>
public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AuditEventId(value));

        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder.Property(x => x.Action)
            .HasColumnName("action")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(x => x.ActorSubject).HasColumnName("actor_subject").HasMaxLength(256);

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new OrganizationId(value.Value) : null);

        builder.Property(x => x.Permission).HasColumnName("permission").HasMaxLength(128);

        builder.Property(x => x.SemanticDelta)
            .HasColumnName("semantic_delta")
            .HasColumnType("jsonb");

        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1024);

        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ClientPlatform).HasColumnName("client_platform").HasMaxLength(64);
        builder.Property(x => x.ClientChannel).HasColumnName("client_channel").HasMaxLength(32);
        builder.Property(x => x.ClientVersion).HasColumnName("client_version").HasMaxLength(64);
        builder.Property(x => x.ClientBuildId).HasColumnName("client_build_id").HasMaxLength(64);
        builder.Property(x => x.ApiContractVersion).HasColumnName("api_contract_version");

        builder.HasIndex(x => new { x.EntityType, x.EntityId })
            .HasDatabaseName("ix_audit_events_entity");

        builder.HasIndex(x => x.OccurredAt).HasDatabaseName("ix_audit_events_occurred_at");

        builder.HasIndex(x => x.CorrelationId).HasDatabaseName("ix_audit_events_correlation_id");
    }
}

/// <summary>
/// Mapping for <see cref="ReleasePolicy"/>, keyed by the platform and ring it governs.
/// </summary>
public sealed class ReleasePolicyConfiguration : IEntityTypeConfiguration<ReleasePolicy>
{
    public void Configure(EntityTypeBuilder<ReleasePolicy> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("release_policies");

        // One published policy per platform and ring: the natural key is the identity.
        builder.HasKey(x => new { x.Platform, x.Ring });

        builder.Property(x => x.Platform)
            .HasColumnName("platform")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Ring)
            .HasColumnName("ring")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.LatestVersion)
            .HasColumnName("latest_version")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.MinimumSupportedVersion)
            .HasColumnName("minimum_supported_version")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.BehindPolicy)
            .HasColumnName("behind_policy")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.ApiContractMinimum).HasColumnName("api_contract_minimum").IsRequired();
        builder.Property(x => x.ApiContractMaximum).HasColumnName("api_contract_maximum").IsRequired();
        builder.Property(x => x.SecurityEpoch).HasColumnName("security_epoch").IsRequired();
        builder.Property(x => x.KillSwitch).HasColumnName("kill_switch").IsRequired();
        builder.Property(x => x.MandatoryAfterUtc).HasColumnName("mandatory_after_utc");
        builder.Property(x => x.RollbackTarget).HasColumnName("rollback_target").HasMaxLength(64);

        builder.Property(x => x.RevokedVersions)
            .HasColumnName("revoked_versions")
            .HasConversion(
                value => value.ToArray(),
                value => value.ToList(),
                new ValueComparer<IReadOnlyList<string>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
                    value => value.ToList()))
            .IsRequired();

        builder.Property(x => x.ArtifactSha256).HasColumnName("artifact_sha256").HasMaxLength(128);
        builder.Property(x => x.ArtifactSignature).HasColumnName("artifact_signature").HasMaxLength(1024);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
