using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Organizations;
using AgencyOS.Domain.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the one-time initialization record.
/// </summary>
/// <remarks>
/// The check constraint pinning <c>id</c> to 1 is what makes "at most one
/// initialization" a database guarantee rather than an application convention.
/// Combined with the primary key, a second bootstrap cannot be inserted even by
/// two callers racing past the application's own check.
/// </remarks>
public sealed class SystemInitializationConfiguration : IEntityTypeConfiguration<SystemInitialization>
{
    public void Configure(EntityTypeBuilder<SystemInitialization> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "system_initialization",
            table => table.HasCheckConstraint(
                "ck_system_initialization_singleton",
                $"id = {SystemInitialization.SingletonId}"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.InitializedAt)
            .HasColumnName("initialized_at")
            .IsRequired();

        builder.Property(x => x.InitialOrganizationId)
            .HasColumnName("initial_organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.InitialOwnerUserId)
            .HasColumnName("initial_owner_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
    }
}
