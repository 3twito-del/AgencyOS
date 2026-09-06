using AgencyOS.Domain.Identity;
using AgencyOS.Domain.Memberships;
using AgencyOS.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgencyOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="User"/>.
/// </summary>
/// <remarks>
/// <c>docs/10_ENGINEERING_STANDARDS.md</c> asks for explicit constraints rather
/// than application-only enforcement, so uniqueness and length live in the
/// database as well as the domain.
/// </remarks>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new UserId(value));

        builder.Property(x => x.ExternalSubject)
            .HasColumnName("external_subject")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // The identity-provider subject is an external identifier, unique per issuer.
        builder.HasIndex(x => x.ExternalSubject)
            .IsUnique()
            .HasDatabaseName("ix_users_external_subject");
    }
}

/// <summary>Mapping for <see cref="Organization"/>.</summary>
public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("organizations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OrganizationId(value));

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.LegalName)
            .HasColumnName("legal_name")
            .HasMaxLength(256);

        builder.Property(x => x.Type)
            .HasColumnName("type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(x => x.Name).HasDatabaseName("ix_organizations_name");
    }
}

/// <summary>Mapping for <see cref="Membership"/>.</summary>
public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("memberships");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new MembershipId(value));

        builder.Property(x => x.OrganizationId)
            .HasColumnName("organization_id")
            .HasConversion(id => id.Value, value => new OrganizationId(value))
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.Role)
            .HasColumnName("role")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.GrantedAt).HasColumnName("granted_at").IsRequired();

        builder.Property(x => x.GrantedBy)
            .HasColumnName("granted_by")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        builder.Property(x => x.RevokedBy)
            .HasColumnName("revoked_by")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrganizationId, x.UserId, x.Status })
            .HasDatabaseName("ix_memberships_organization_user_status");

        builder.HasIndex(x => new { x.UserId, x.Status })
            .HasDatabaseName("ix_memberships_user_status");
    }
}
