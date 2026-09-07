using System.Text.Json;
using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Idempotency;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.SavedViews;
using AgencyOS.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="SavedView"/>.
/// </summary>
/// <remarks>
/// <para>
/// The definition is stored as <c>jsonb</c> rather than shredded into columns.
/// It is a versioned document the server validates on the way in and on the way
/// out; giving each filter its own column would tie the schema to one version of
/// the format and turn every future filter into a migration.
/// </para>
/// <para>
/// The stored version number is a real column, not just a field inside the
/// document, so a definition the server no longer understands can be found
/// without parsing every row.
/// </para>
/// </remarks>
public sealed class SavedViewConfiguration : IEntityTypeConfiguration<SavedView>
{
    /// <summary>
    /// Serialization used for the stored definition.
    /// </summary>
    /// <remarks>
    /// Deliberately explicit and not the ambient web defaults: this document is
    /// read back months later, so its shape must not follow whatever the API
    /// happens to be configured with today.
    /// </remarks>
    private static readonly JsonSerializerOptions DefinitionJson = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
    };

    public void Configure(EntityTypeBuilder<SavedView> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("saved_views");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new SavedViewId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();

        builder.Property(x => x.Target).HasColumnName("target").HasConversion<int>().IsRequired();

        builder.Property(x => x.Definition)
            .HasColumnName("definition")
            .HasColumnType("jsonb")
            .HasConversion(
                definition => JsonSerializer.Serialize(definition, DefinitionJson),
                json => JsonSerializer.Deserialize<SavedViewDefinition>(json, DefinitionJson)!,
                new ValueComparer<SavedViewDefinition>(
                    (left, right) => left != null && right != null && left.Equals(right),
                    definition => definition.GetHashCode(),
                    definition => definition))
            .IsRequired();

        builder.Property(x => x.DefinitionVersion).HasColumnName("definition_version").IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // A view belongs to an owner within a tenant, and both keys appear in
        // every lookup, so this is the index those lookups actually use.
        builder.HasIndex(x => new { x.OrganizationId, x.OwnerUserId })
            .HasDatabaseName("ix_saved_views_organization_owner");

        // One name per owner per tenant, enforced by the database. Two views
        // called "Priority contacts" is a usability defect the application check
        // can lose to a race; this cannot.
        builder.HasIndex(x => new { x.OrganizationId, x.OwnerUserId, x.Name })
            .IsUnique()
            .HasDatabaseName("ux_saved_views_owner_name");
    }
}

/// <summary>
/// Mapping for <see cref="ChangeLogEntry"/>.
/// </summary>
/// <remarks>
/// Keyed on <c>(organization_id, sequence)</c>: the feed is per tenant, so the
/// tenant is part of the identity rather than a filter applied afterwards. That
/// also makes the primary-key index the exact index a feed read uses.
/// </remarks>
public sealed class ChangeLogEntryConfiguration : IEntityTypeConfiguration<ChangeLogEntry>
{
    public void Configure(EntityTypeBuilder<ChangeLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("change_log");

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();

        builder.HasKey(x => new { x.OrganizationId, x.Sequence });

        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(128).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapping for <see cref="ChangeSequence"/>, one counter row per tenant.</summary>
public sealed class ChangeSequenceConfiguration : IEntityTypeConfiguration<ChangeSequence>
{
    public void Configure(EntityTypeBuilder<ChangeSequence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("change_sequence");

        builder.HasKey(x => x.OrganizationId);

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.LastValue).HasColumnName("last_value").IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Mapping for <see cref="IdempotencyRecord"/>.
/// </summary>
/// <remarks>
/// The primary key is <c>(organization_id, key)</c>, which is what makes the
/// reservation atomic: two concurrent submissions of the same key contend on this
/// key and exactly one insert survives. Scoping to the tenant means one tenant's
/// key can never replay another's answer.
/// </remarks>
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("idempotency_keys");

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(128).IsRequired();

        builder.HasKey(x => new { x.OrganizationId, x.Key });

        builder.Property(x => x.RequestFingerprint)
            .HasColumnName("request_fingerprint")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ResponseStatusCode).HasColumnName("response_status_code");
        builder.Property(x => x.ResponseBody).HasColumnName("response_body");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nothing prunes expired keys in M3 (ADR-0014). The index exists so that
        // when a reaper is added it does not need a table scan, and so the
        // retention question is visible in the schema rather than only in prose.
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_idempotency_keys_expires_at");
    }
}
